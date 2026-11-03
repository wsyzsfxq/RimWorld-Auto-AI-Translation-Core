using AutoTranslator_Core.TargetedHardcodedUi;
using AutoTranslator_Core.Workflow;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTranslator_Core
{
    internal sealed class TranslationBackupInfo
    {
        internal string Path;
        internal DateTime CreatedLocal;
        internal bool BeforeRestore;
    }

    internal sealed class TranslationBackupResult
    {
        internal string Path;
        internal string RollbackPath;
        internal string Warning;
        internal List<TranslationBackupOrigin> Origins;
    }

    // Only translation outputs are archived. Database and provider settings stay outside.
    internal static class TranslationBackupManager
    {
        private const string ManifestName = "TranslationSnapshot.json";
        private const string DllFile = "HardcodedUiPatchPrototype.json";
        private const string UiFile = "UI_Hardcoded_Cache.json";

        private sealed class Snapshot
        {
            public int Version = 1;
            public DateTime CreatedUtc;
            public string Kind;
            public List<string> Files = new List<string>();
            public List<TranslationBackupOrigin> Origins = new List<TranslationBackupOrigin>();
        }

        internal static string BackupRoot => Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "ManualBackups");

        internal static List<TranslationBackupInfo> ListBackups()
        {
            var result = new List<TranslationBackupInfo>();
            if (!Directory.Exists(BackupRoot)) return result;
            foreach (string file in Directory.GetFiles(BackupRoot, "*.zip", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file).StartsWith("translations_", StringComparison.OrdinalIgnoreCase))
                    result.Add(new TranslationBackupInfo
                    {
                        Path = file,
                        CreatedLocal = File.GetLastWriteTime(file),
                        BeforeRestore = string.Equals(Path.GetFileName(Path.GetDirectoryName(file)),
                            "BeforeRestore", StringComparison.OrdinalIgnoreCase)
                    });
            }
            return result.OrderByDescending(item => item.CreatedLocal).ToList();
        }

        internal static Task<TranslationBackupResult> CreateAsync()
        {
            return RunAsync(null);
        }

        internal static Task<TranslationBackupResult> RestoreAsync(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("请选择一份手动备份。");
            return RunAsync(archivePath);
        }

        private static async Task<TranslationBackupResult> RunAsync(string restorePath)
        {
            bool restore = restorePath != null;
            WorkflowTaskKind kind = restore ? WorkflowTaskKind.TranslationBackupRestore : WorkflowTaskKind.TranslationBackup;
            if (!WorkflowTaskCoordinator.Instance.TryBegin(kind,
                restore ? "Restore translation backup" : "Back up translations", out WorkflowTaskLease lease))
                throw new InvalidOperationException("其他任务正在处理译文，请稍后再操作。");
            using (lease)
            {
                bool paused = false;
                try
                {
                    await OnMainThread(() => { UIInterceptionModeController.PauseTranslationData(); paused = true; });
                    TranslationBackupResult result = await Task.Run(async () =>
                    {
                        await UIInterceptionModeController.DrainTranslationDataAsync();
                        lease.CancellationToken.ThrowIfCancellationRequested();
                        string packRoot = AutoTranslatorScanner.GetLocalPackPath();
                        if (!restore)
                            return new TranslationBackupResult
                            { Path = CreateArchive(packRoot, false, lease.CancellationToken) };
                        return RestoreFiles(packRoot, restorePath, lease.CancellationToken);
                    });

                    if (restore)
                    {
                        // Once files are committed, finish state reconciliation even if Stop
                        // was pressed just after the final file replacement.
                        try
                        {
                            await Task.Run(() => WorkflowBackendRuntime.GetOrCreate()
                                .SynchronizeTranslationBackupFiles(result.Origins));
                        }
                        catch (Exception ex)
                        {
                            result.Warning = "译文文件已恢复，但工作台状态同步失败：" + ex.Message;
                        }
                        await OnMainThread(() =>
                            AutoTranslatorScanner.NotifyTranslationFilesChanged(Path.Combine(
                                AutoTranslatorScanner.GetLocalPackPath(), "Languages")));
                        try
                        {
                            bool xmlReady = await AutoTranslatorScanner.RequestMemoryDropAsync();
                            bool dllReady = File.Exists(HardcodedUiTargetedPatchManager.ManifestPath)
                                ? await HardcodedUiTargetedPatchManager.RequestReloadAsync()
                                : await HardcodedUiTargetedPatchManager.ClearAfterTranslationRestoreAsync();
                            if (!xmlReady || !dllReady)
                                result.Warning = JoinWarning(result.Warning,
                                    "译文文件已恢复，部分游戏内显示未能刷新，请查看日志。");
                        }
                        catch (Exception ex)
                        {
                            result.Warning = JoinWarning(result.Warning,
                                "译文文件已恢复，但游戏内显示刷新失败：" + ex.Message);
                        }
                    }
                    WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId,
                        string.IsNullOrWhiteSpace(result.Warning) ? WorkflowRunState.Completed : WorkflowRunState.Failed,
                        result.Warning);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId, WorkflowRunState.Cancelled);
                    throw;
                }
                catch (Exception ex)
                {
                    WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId, WorkflowRunState.Failed, ex.Message);
                    throw;
                }
                finally
                {
                    try
                    {
                        if (paused) await OnMainThread(UIInterceptionModeController.ResumeTranslationData);
                    }
                    finally { WorkflowTaskCoordinator.Instance.NotifyWorkbenchDataChanged(); }
                }
            }
        }

        private static string JoinWarning(string first, string second)
        {
            return string.IsNullOrWhiteSpace(first) ? second : first + "\n" + second;
        }

        private static Task OnMainThread(Action action)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                try { action(); completion.TrySetResult(true); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }

        private static List<string> TranslationFiles(string packRoot)
        {
            var files = new List<string>();
            string languages = Path.Combine(packRoot, "Languages");
            if (Directory.Exists(languages))
            {
                files.AddRange(Directory.GetFiles(languages, "*.xml", SearchOption.AllDirectories)
                    .Select(file => "Languages/" + file.Substring(languages.Length + 1).Replace('\\', '/')));
                // These entry/hash records describe the XML translations' sources,
                // not provider configuration or general application settings.
                foreach (string language in Directory.GetDirectories(languages))
                {
                    string provenance = Path.Combine(language, "ATC_Provenance");
                    if (Directory.Exists(provenance))
                        files.AddRange(Directory.GetFiles(provenance, "*.json", SearchOption.TopDirectoryOnly)
                            .Select(file => "Languages/" + file.Substring(languages.Length + 1).Replace('\\', '/')));
                }
            }
            if (File.Exists(Path.Combine(packRoot, DllFile))) files.Add(DllFile);
            if (File.Exists(Path.Combine(packRoot, UiFile))) files.Add(UiFile);
            return files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string CreateArchive(string packRoot, bool beforeRestore, CancellationToken token)
        {
            string directory = beforeRestore ? Path.Combine(BackupRoot, "BeforeRestore") : BackupRoot;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "translations_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
            string temporary = path + ".tmp";
            var snapshot = new Snapshot
            {
                CreatedUtc = DateTime.UtcNow,
                Kind = beforeRestore ? "before-restore" : "manual",
                Files = TranslationFiles(packRoot)
            };
            // Minimal provenance, matched against exact file text again on restore;
            // this is not a database backup or a copy of provider configuration.
            snapshot.Origins = WorkflowBackendRuntime.GetOrCreate().CaptureTranslationBackupOrigins(snapshot.Files);
            try
            {
                using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                {
                    foreach (string relative in snapshot.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        archive.CreateEntryFromFile(Resolve(packRoot, relative), relative, CompressionLevel.Optimal);
                    }
                    using (var writer = new StreamWriter(archive.CreateEntry(ManifestName).Open()))
                        writer.Write(JsonConvert.SerializeObject(snapshot, Formatting.Indented));
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static TranslationBackupResult RestoreFiles(string packRoot, string archivePath, CancellationToken token)
        {
            // Fully extract and inspect the archive before changing a live translation.
            string work = Path.Combine(BackupRoot, ".restore_" + Guid.NewGuid().ToString("N"));
            string desired = Path.Combine(work, "desired");
            string rollback = Path.Combine(work, "rollback");
            try
            {
                Snapshot snapshot = ExtractSnapshot(archivePath, desired, token);
                string rollbackArchive = CreateArchive(packRoot, true, token);
                Snapshot previous = ExtractSnapshot(rollbackArchive, rollback, token);
                try { ApplyFiles(packRoot, desired, snapshot.Files, token); }
                catch (Exception restoreError)
                {
                    try { ApplyFiles(packRoot, rollback, previous.Files, CancellationToken.None); }
                    catch (Exception rollbackError)
                    {
                        throw new IOException("恢复未完成，回退也未能全部完成。恢复前的译文已保存在：" +
                            rollbackArchive + "。请保留这份备份。原因：" + rollbackError.Message, restoreError);
                    }
                    if (restoreError is OperationCanceledException) throw;
                    throw new IOException("恢复未完成，已还原操作前的译文。原因：" + restoreError.Message, restoreError);
                }
                return new TranslationBackupResult { Path = archivePath, RollbackPath = rollbackArchive,
                    Origins = snapshot.Origins ?? new List<TranslationBackupOrigin>() };
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); }
                catch (Exception ex) { AutoTranslatorSettings.AddWarningLog("清理备份临时文件失败：" + ex.Message); }
            }
        }

        private static Snapshot ExtractSnapshot(string path, string destination, CancellationToken token)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry metadata = archive.GetEntry(ManifestName);
                if (metadata == null || archive.Entries.Count(entry => entry.FullName == ManifestName) != 1)
                    throw new InvalidDataException("这不是完整的手动译文备份，请使用对应的备份恢复入口。");
                Snapshot snapshot;
                using (var reader = new StreamReader(metadata.Open()))
                    snapshot = JsonConvert.DeserializeObject<Snapshot>(reader.ReadToEnd());
                if (snapshot == null || snapshot.Version != 1 || snapshot.Files == null)
                    throw new InvalidDataException("手动备份信息不完整或版本不支持，未修改当前译文。");
                var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string relative in snapshot.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsTranslationPath(relative) || !selected.Add(relative))
                        throw new InvalidDataException("备份包含无效或重复的译文路径，未修改当前译文。");
                    ZipArchiveEntry entry = archive.GetEntry(relative);
                    if (entry == null || archive.Entries.Count(item =>
                        string.Equals(item.FullName, relative, StringComparison.OrdinalIgnoreCase)) != 1)
                        throw new InvalidDataException("备份缺少译文文件或包含重复文件：" + relative);
                    string target = Resolve(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target);
                }
                if (archive.Entries.Any(entry => entry.FullName != ManifestName && !selected.Contains(entry.FullName)))
                    throw new InvalidDataException("备份文件清单与内容不一致，未修改当前译文。");
                return snapshot;
            }
        }

        private static bool IsTranslationPath(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("\\") || Path.IsPathRooted(relative) ||
                relative.Split('/').Any(part => part.Length == 0 || part == "." || part == "..")) return false;
            string[] parts = relative.Split('/');
            return relative == DllFile || relative == UiFile ||
                (relative.StartsWith("Languages/", StringComparison.Ordinal) &&
                    relative.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) ||
                (parts.Length == 4 && parts[0] == "Languages" && parts[2] == "ATC_Provenance" &&
                    parts[3].EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        }

        private static string Resolve(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("备份路径超出译文目录。");
            return target;
        }

        private static void ApplyFiles(string packRoot, string stagedRoot, ICollection<string> desiredFiles, CancellationToken token)
        {
            var desired = new HashSet<string>(desiredFiles, StringComparer.OrdinalIgnoreCase);
            // Files created after the snapshot must disappear on restore, including
            // a nonempty category restored to its earlier empty state.
            foreach (string current in TranslationFiles(packRoot))
            {
                token.ThrowIfCancellationRequested();
                if (!desired.Contains(current)) File.Delete(Resolve(packRoot, current));
            }
            foreach (string relative in desiredFiles)
            {
                token.ThrowIfCancellationRequested();
                string destination = Resolve(packRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                string temporary = destination + ".restore_" + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(Resolve(stagedRoot, relative), temporary);
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
    }
}
