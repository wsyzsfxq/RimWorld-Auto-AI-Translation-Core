using AutoTranslator_Core.Workflow;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTranslator_Core
{
    // This optional display cache is independent of candidates and never renames a Mod.
    internal static class ModNameTranslationCache
    {
        private sealed class Entry
        {
            public string TargetLanguage;
            public string PackageId;
            public string SourceName;
            public string TranslatedName;
            public string UpdatedUtc;
        }

        private static readonly object Sync = new object();
        private static Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, DateTime> RetryAfter = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static string _path;
        private static bool _loadStarted;
        private static bool _loaded;
        private static DateTime _nextQueueUtc;
        private static CancellationTokenSource _activeCancellation;

        internal static string GetDisplayName(WorkflowModSummary mod)
        {
            string original = mod?.DisplayName ?? string.Empty;
            AutoTranslatorSettings settings = AutoTranslatorMod.Settings;
            if (mod == null || mod.IsProtectedSystemPackage || settings == null || !settings.TranslateWorkbenchModNames) return original;
            string key = GetKey(settings.TargetLang, mod.PackageId, original);
            EnsureLoadStarted();
            lock (Sync)
            {
                if (!_loaded || !_entries.TryGetValue(key, out Entry entry) || entry == null ||
                    entry.TargetLanguage != settings.TargetLang.ToString() ||
                    !string.Equals(entry.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) ||
                    entry.SourceName != original || string.IsNullOrWhiteSpace(entry.TranslatedName) ||
                    LanguageDetector.LooksLikePlaceholderTranslation(entry.TranslatedName, settings.TargetLang))
                    return original;
                string translated = LanguageDetector.NormalizeChineseVariant(entry.TranslatedName, settings.TargetLang);
                return string.Equals(translated.Trim(), original.Trim(), StringComparison.OrdinalIgnoreCase)
                    ? original : translated + " / " + original;
            }
        }

        internal static void CancelPending()
        {
            lock (Sync) _activeCancellation?.Cancel();
        }

        internal static bool IsRunning { get { lock (Sync) return _activeCancellation != null; } }

        internal static void QueueVisible(IEnumerable<WorkflowModSummary> visible)
        {
            AutoTranslatorSettings settings = AutoTranslatorMod.Settings;
            if (settings == null || !settings.TranslateWorkbenchModNames ||
                !AutoTranslatorAPI.HasUsableWorkflowConfiguration(TranslationTaskTier.Bulk) ||
                AutoTranslatorSettings.IsCancellationRequested || WorkflowTaskCoordinator.Instance.IsBusy ||
                AutoTranslatorSettings.LegacyPipelineIsRunning) return;
            EnsureLoadStarted();
            TargetLanguage language = settings.TargetLang;
            List<Entry> pending = new List<Entry>();
            DateTime now = DateTime.UtcNow;
            lock (Sync)
            {
                if (!_loaded || _activeCancellation != null || now < _nextQueueUtc) return;
                _nextQueueUtc = now.AddSeconds(2);
                foreach (WorkflowModSummary mod in visible ?? Enumerable.Empty<WorkflowModSummary>())
                {
                    if (mod == null || mod.IsProtectedSystemPackage || string.IsNullOrWhiteSpace(mod.PackageId) ||
                        string.IsNullOrWhiteSpace(mod.DisplayName)) continue;
                    string key = GetKey(language, mod.PackageId, mod.DisplayName);
                    if (_entries.TryGetValue(key, out Entry entry) && entry != null &&
                        !string.IsNullOrWhiteSpace(entry.TranslatedName) &&
                        !LanguageDetector.LooksLikePlaceholderTranslation(entry.TranslatedName, language)) continue;
                    if (RetryAfter.TryGetValue(key, out DateTime retry) && now < retry) continue;
                    if (pending.Any(item => string.Equals(item.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                        item.SourceName == mod.DisplayName)) continue;
                    pending.Add(new Entry { TargetLanguage = language.ToString(), PackageId = mod.PackageId, SourceName = mod.DisplayName });
                    if (pending.Count == 30) break;
                }
            }
            if (pending.Count == 0 || !WorkflowTaskCoordinator.Instance.TryBegin(
                WorkflowTaskKind.AiTranslation, "Translate Mod names", out WorkflowTaskLease lease)) return;
            CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.CancellationToken);
            lock (Sync) _activeCancellation = cancellation;
            string path = _path;
            Task.Run(() => TranslateAsync(pending, language, path, lease, cancellation));
        }

        private static async Task TranslateAsync(List<Entry> pending, TargetLanguage language, string path,
            WorkflowTaskLease lease, CancellationTokenSource cancellation)
        {
            try
            {
                if (AutoTranslatorMod.Settings?.TranslateWorkbenchModNames != true ||
                    AutoTranslatorMod.Settings.TargetLang != language) throw new OperationCanceledException();
                WorkflowTaskCoordinator.Instance.ReportProgress(0, pending.Count, string.Empty,
                    AutoTranslatorMod.WfText("正在翻译 Mod 名称", "Translating Mod names"));
                List<string> translated = await AutoTranslatorAPI.TranslateLegacyUiBatchAsync(
                    pending.Select(item => item.SourceName).ToList(), language, cancellation.Token, "mod_name_translation");
                cancellation.Token.ThrowIfCancellationRequested();
                Dictionary<string, Entry> snapshot;
                lock (Sync)
                {
                    for (int i = 0; i < pending.Count; i++)
                    {
                        if (LanguageDetector.LooksLikePlaceholderTranslation(translated[i], language))
                        {
                            RetryAfter[GetKey(language, pending[i].PackageId, pending[i].SourceName)] = DateTime.UtcNow.AddSeconds(30);
                            continue;
                        }
                        pending[i].TranslatedName = translated[i].Trim();
                        pending[i].UpdatedUtc = DateTime.UtcNow.ToString("o");
                        _entries[GetKey(language, pending[i].PackageId, pending[i].SourceName)] = pending[i];
                    }
                    snapshot = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);
                }
                Save(path, snapshot);
                WorkflowTaskCoordinator.Instance.ReportProgress(pending.Count, pending.Count, string.Empty,
                    AutoTranslatorMod.WfText("Mod 名称翻译完成", "Mod names translated"));
                WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId, WorkflowRunState.Completed);
            }
            catch (OperationCanceledException)
            {
                WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId, WorkflowRunState.Cancelled);
            }
            catch (Exception ex)
            {
                lock (Sync)
                    foreach (Entry item in pending)
                        RetryAfter[GetKey(language, item.PackageId, item.SourceName)] = DateTime.UtcNow.AddSeconds(30);
                AutoTranslatorSettings.AddWarningLog("Mod 名称翻译失败：" + ex.Message);
                WorkflowTaskCoordinator.Instance.MarkTerminal(lease.RunId, WorkflowRunState.Failed, ex.Message);
            }
            finally
            {
                lock (Sync) _activeCancellation = null;
                cancellation.Dispose();
                lease.Dispose();
            }
        }

        private static void EnsureLoadStarted()
        {
            lock (Sync)
            {
                if (_loadStarted) return;
                _loadStarted = true;
                _path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Cache", "ModNameTranslations.json");
            }
            string path = _path;
            Task.Run(() =>
            {
                try
                {
                    Dictionary<string, Entry> entries = File.Exists(path)
                        ? JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(path)) : null;
                    lock (Sync)
                        if (entries != null) _entries = new Dictionary<string, Entry>(entries, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { AutoTranslatorSettings.AddWarningLog("读取 Mod 名称译文失败：" + ex.Message); }
                finally { lock (Sync) _loaded = true; }
            });
        }

        private static string GetKey(TargetLanguage language, string packageId, string sourceName)
        {
            string raw = language + "\n" + (packageId ?? string.Empty).ToLowerInvariant() + "\n" + sourceName;
            lock (Sync)
            {
                if (Keys.TryGetValue(raw, out string key)) return key;
                using (SHA256 sha = SHA256.Create())
                    key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", string.Empty);
                Keys[raw] = key;
                return key;
            }
        }

        private static void Save(string path, Dictionary<string, Entry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(entries, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
