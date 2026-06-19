using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責翻譯包備份、還原與舊檔清理。
// EN: This file creates backups, restores files, and clears old translation output.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {


        // 這個方法負責處理 UpdateLocal模組Meta 相關流程。
        // EN: This method handles update local mod meta.
        public static void UpdateLocalModMeta(string packageId, string targetLangFolder, int newAiCount)
        {
            try
            {
                string packPath = GetLocalPackPath();
                string extractRoot = Path.Combine(packPath, "Languages", targetLangFolder);
                Directory.CreateDirectory(extractRoot);

                string cleanPackageId = packageId.Replace(".", "_").ToLower();
                string metaPath = Path.Combine(extractRoot, $"{cleanPackageId}_ATC_Meta.json");

                LocalModMeta meta = new LocalModMeta
                {
                    OriginalRecordId = "",
                    TargetModVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild,
                    TranslationDate = DateTime.UtcNow,
                    IsSmartMerged = false,
                    MergedAiCount = 0
                };

                bool hasCloudIdentity = false;

                if (File.Exists(metaPath))
                {
                    try
                    {
                        var existing = JsonConvert.DeserializeObject<LocalModMeta>(File.ReadAllText(metaPath));
                        if (existing != null)
                        {
                            meta = existing;
                            if (!string.IsNullOrEmpty(meta.OriginalRecordId)) hasCloudIdentity = true;
                        }
                    }
                    catch { }
                }


                if (hasCloudIdentity)
                {
                    meta.IsSmartMerged = true;
                    meta.MergedAiCount += newAiCount;
                }
                else
                {

                    meta.IsSmartMerged = false;
                    meta.MergedAiCount = 0;
                }

                meta.TargetModVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild;

                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
                AutoTranslatorSettings.AddLog("📝 " + "ATC_Log_MetaUpdated".Translate(newAiCount));


            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] UpdateLocalModMeta error: {ex.Message}");
            }
        }


        // 這個方法負責建立 備份BeforeClear 物件或檔案。
        // EN: This method creates backup before clear.
        private static void CreateBackupBeforeClear(ModMetaData mod, List<string> filesToBackup)
        {
            if (filesToBackup == null || filesToBackup.Count == 0) return;
            try
            {
                string packPath = GetLocalPackPath();
                string backupRoot = Path.Combine(packPath, "Backups", mod.PackageId);
                Directory.CreateDirectory(backupRoot);


                string stagingDir = Path.Combine(Path.GetTempPath(), "ATC_Backup_" + mod.PackageId);
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                Directory.CreateDirectory(stagingDir);

                string langsPath = Path.Combine(packPath, "Languages");


                foreach (string file in filesToBackup)
                {
                    string relPath = file.Substring(langsPath.Length).TrimStart('\\', '/');
                    string destPath = Path.Combine(stagingDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    File.Copy(file, destPath, true);
                }


                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string zipPath = Path.Combine(backupRoot, $"{mod.PackageId}_{timestamp}.zip");

                if (File.Exists(zipPath)) File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, zipPath);
                Directory.Delete(stagingDir, true);


                var zipFiles = new DirectoryInfo(backupRoot).GetFiles("*.zip")
                                .OrderByDescending(f => f.CreationTimeUtc).ToList();

                for (int i = 3; i < zipFiles.Count; i++)
                {
                    zipFiles[i].Delete();
                }

                AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_BackupSuccess".Translate(mod.Name));
            }
            catch (Exception ex)
            {
                Log.Warning($"[ATC Backup] Failed to backup {mod.PackageId}: {ex.Message}");
            }
        }


        // 這個方法負責清除 Old翻譯Files 資料。
        // EN: This method clears old translation files.
        public static void ClearOldTranslationFiles(List<ModMetaData> modsToClear)
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                foreach (var mod in modsToClear)
                {
                    string id1 = mod.PackageId.ToLower();
                    string id2 = mod.PackageId.Replace(".", "_").ToLower();


                    List<string> filesToDelete = new List<string>();

                    foreach (var file in allXmls)
                    {

                        if (file.Contains("Upload_Workspace")) continue;

                        string fileName = Path.GetFileName(file).ToLower();
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            filesToDelete.Add(file);
                        }
                    }


                    if (filesToDelete.Count > 0)
                    {
                        CreateBackupBeforeClear(mod, filesToDelete);

                        foreach (var file in filesToDelete)
                        {

                            System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                            File.Delete(file);
                            deletedFiles++;
                        }
                    }
                }

                if (deletedFiles > 0)
                {
                    AutoTranslatorSettings.AddLog("ATC_ClearCacheSuccess".Translate(deletedFiles));
                    Log.Message($"[AutoTranslationCore] Auto-cleared {deletedFiles} old files for updated mods (Backup created).");
                }
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog($"Auto Clear Error: {ex.Message}");
            }
        }


        // 這個方法負責處理 RestoreLatest備份 相關流程。
        // EN: This method handles restore latest backups.
        public static int RestoreLatestBackups(List<ModMetaData> modsToRestore)
        {
            if (modsToRestore == null || modsToRestore.Count == 0) return 0;

            string packPath = GetLocalPackPath();
            string backupRoot = Path.Combine(packPath, "Backups");
            string langsPath = Path.Combine(packPath, "Languages");
            if (!Directory.Exists(backupRoot)) return 0;

            int restored = 0;
            foreach (var mod in modsToRestore)
            {
                if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;

                try
                {
                    string modBackupDir = Path.Combine(backupRoot, mod.PackageId);
                    if (!Directory.Exists(modBackupDir)) continue;

                    FileInfo latest = new DirectoryInfo(modBackupDir)
                        .GetFiles("*.zip")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .FirstOrDefault();

                    if (latest == null) continue;

                    Directory.CreateDirectory(langsPath);
                    using (var archive = System.IO.Compression.ZipFile.OpenRead(latest.FullName))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destPath = Path.GetFullPath(Path.Combine(langsPath, entry.FullName));
                            string safeRoot = Path.GetFullPath(langsPath);
                            if (!destPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, true);
                        }
                    }

                    restored++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[ATC Backup] Failed to restore {mod.PackageId}: {ex.Message}");
                }
            }

            if (restored > 0)
            {
                RequestMemoryDrop();
            }

            return restored;
        }
    }
}
