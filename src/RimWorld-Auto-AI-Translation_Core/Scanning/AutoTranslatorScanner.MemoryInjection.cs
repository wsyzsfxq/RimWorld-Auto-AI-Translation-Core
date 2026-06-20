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
// 這個檔案負責把翻譯資料注入記憶體與主執行緒。
// EN: This file injects translation data into memory through the main thread.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        private const int MemoryDropKeyedApplyBudget = 3000;
        private const int MemoryDropDefApplyBudget = 3000;

        private class MemoryDropPayload
        {
            public bool KeyedOnly;
            public bool PackageScoped;
            public string PackageId;
            public string TargetFolder;
            public string LangRoot;
            public string KeyedPath;
            public Dictionary<string, HashSet<string>> ClearKeysByDefType =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Keyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<MemoryDropDefTypePayload> DefsByType = new List<MemoryDropDefTypePayload>();
            public DateTime PreparedAtUtc;
        }

        private class MemoryDropDefTypePayload
        {
            public string DefTypeName;
            public Dictionary<string, string> Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private class MemoryDropApplyState
        {
            public MemoryDropPayload Payload;
            public System.Diagnostics.Stopwatch Timer;
            public List<KeyValuePair<string, string>> KeyedEntries = new List<KeyValuePair<string, string>>();
            public int KeyedIndex;
            public int DefTypeIndex;
            public List<KeyValuePair<string, string>> CurrentDefEntries;
            public int CurrentDefEntryIndex;
            public DefInjectionPackage CurrentPackage;
            public int InjectedKeyed;
            public int InjectedDefs;
            public int ClearedKeyed;
            public int ClearedDefs;
            public bool NeedsDataInjection;
            public bool PackageClearApplied;
        }

        // 這個方法負責重置 ValidationStats 狀態。
        // EN: This method resets validation stats.
        private static void ResetValidationStats()
        {
            lock (_validationStats)
            {
                _validationStats.Reset();
            }

            lock (_loggedEnglishResidualContexts)
            {
                _loggedEnglishResidualContexts.Clear();
            }
        }


        // 這個方法負責處理 AddValidationStat 相關流程。
        // EN: This method handles add validation stat.
        private static void AddValidationStat(Action<TranslationValidationStats> update)
        {
            lock (_validationStats) update(_validationStats);
        }


        // 這個方法負責處理 LogValidationSummary 相關流程。
        // EN: This method handles log validation summary.
        private static void LogValidationSummary()
        {
            TranslationValidationStats snapshot;
            lock (_validationStats)
            {
                snapshot = new TranslationValidationStats
                {
                    NewlineFixed = _validationStats.NewlineFixed,
                    RulePrefixFixed = _validationStats.RulePrefixFixed,
                    TokenFixed = _validationStats.TokenFixed,
                    StructureFallback = _validationStats.StructureFallback,
                    XmlKeySkipped = _validationStats.XmlKeySkipped,
                    EnglishResidualDetected = _validationStats.EnglishResidualDetected,
                    EnglishResidualRetried = _validationStats.EnglishResidualRetried,
                    EnglishResidualFallback = _validationStats.EnglishResidualFallback,
                    ProtectedTokenMismatchDetected = _validationStats.ProtectedTokenMismatchDetected,
                    ProtectedTokenMismatchRetried = _validationStats.ProtectedTokenMismatchRetried,
                    ProtectedTokenMismatchFallback = _validationStats.ProtectedTokenMismatchFallback
                };
            }

            int total = snapshot.NewlineFixed + snapshot.RulePrefixFixed + snapshot.TokenFixed + snapshot.StructureFallback + snapshot.XmlKeySkipped
                + snapshot.EnglishResidualDetected + snapshot.EnglishResidualRetried + snapshot.EnglishResidualFallback
                + snapshot.ProtectedTokenMismatchDetected + snapshot.ProtectedTokenMismatchRetried + snapshot.ProtectedTokenMismatchFallback;
            if (total <= 0)
            {
                AutoTranslatorSettings.AddLog("🩺 " + "ATC_Log_ValidationClean".Translate());
                return;
            }

            AutoTranslatorSettings.AddLog("🩺 " + "ATC_Log_ValidationSummary".Translate(
                snapshot.NewlineFixed,
                snapshot.RulePrefixFixed,
                snapshot.TokenFixed,
                snapshot.StructureFallback,
                snapshot.XmlKeySkipped));

            if (snapshot.EnglishResidualDetected > 0 || snapshot.EnglishResidualRetried > 0 || snapshot.EnglishResidualFallback > 0)
            {
                AutoTranslatorSettings.AddLog("🩺 " +
                    AutoTranslatorAPI.TranslateText(
                        "ATC_Log_EnglishResidualSummary",
                        snapshot.EnglishResidualDetected,
                        snapshot.EnglishResidualRetried,
                        snapshot.EnglishResidualFallback));
            }
        }


        // 這個方法負責送出 記憶體Drop 請求。
        // EN: This method requests memory drop.
        public static void RequestMemoryDrop()
        {
            QueueMemoryDropPreparation(false, true);
        }

        public static void RequestMemoryDropForPackage(string packageId)
        {
            RequestMemoryDropForPackage(packageId, null);
        }

        public static void RequestMemoryDropForPackage(string packageId, Dictionary<string, HashSet<string>> clearKeysByDefType)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                RequestMemoryDrop();
                return;
            }

            QueueMemoryDropPreparation(false, true, packageId, clearKeysByDefType);
        }


        // 這個方法負責處理 Pump主畫面執行緒派發器 相關流程。
        // EN: This method handles pump main thread dispatcher.
        public static void RequestKeyedMemoryDrop()
        {
            QueueMemoryDropPreparation(true, false);
        }

        public static void QueueStartupFullMemoryDrop(int delayMs = 8000)
        {
            if (Interlocked.CompareExchange(ref _startupFullMemoryDropQueued, 1, 0) != 0)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(1000, delayMs));
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        if (AutoTranslatorMod.Settings != null && LanguageDatabase.activeLanguage != null)
                        {
                            break;
                        }

                        await Task.Delay(1000);
                    }

                    if (AutoTranslatorMod.Settings == null || LanguageDatabase.activeLanguage == null)
                    {
                        Interlocked.Exchange(ref _startupFullMemoryDropQueued, 0);
                        return;
                    }

                    QueueMemoryDropPreparation(false, true);
                }
                catch (Exception ex)
                {
                    Log.Warning("[AutoTranslationCore] Startup full memory drop queue failed: " + ex.Message);
                }
            });
        }

        private static void QueueMemoryDropPreparation(bool keyedOnly, bool logQueued, string packageId = null, Dictionary<string, HashSet<string>> clearKeysByDefType = null)
        {
            string normalizedPackageId = string.IsNullOrWhiteSpace(packageId) ? null : packageId.Trim();
            lock (_pendingInjectLock)
            {
                if (keyedOnly)
                {
                    _pendingKeyedMemoryDrop = true;
                }
                else if (!string.IsNullOrEmpty(normalizedPackageId))
                {
                    if (!_pendingMemoryDrop)
                    {
                        _pendingMemoryDropPackageIds.Add(normalizedPackageId);
                        MergePendingPackageClearKeys(normalizedPackageId, clearKeysByDefType);
                    }
                }
                else
                {
                    _pendingMemoryDrop = true;
                    _pendingKeyedMemoryDrop = false;
                    _pendingMemoryDropPackageIds.Clear();
                    _pendingMemoryDropClearKeysByPackage.Clear();
                }
            }

            if (logQueued && !keyedOnly)
            {
                AutoTranslatorSettings.AddLog("🪂 " + "ATC_Log_MemoryDropQueued".Translate());
            }

            StartMemoryDropPreparationIfNeeded();
        }

        private static void StartMemoryDropPreparationIfNeeded()
        {
            bool keyedOnly;
            List<string> packageIds = null;
            Dictionary<string, Dictionary<string, HashSet<string>>> clearKeysByPackage = null;
            lock (_pendingInjectLock)
            {
                if (_pendingMemoryDropPayload != null)
                {
                    return;
                }

                if (_pendingMemoryDrop)
                {
                    keyedOnly = false;
                }
                else if (_pendingMemoryDropPackageIds.Count > 0)
                {
                    keyedOnly = false;
                    packageIds = _pendingMemoryDropPackageIds.ToList();
                    clearKeysByPackage = CloneClearKeysForPackages(packageIds);
                }
                else if (_pendingKeyedMemoryDrop)
                {
                    keyedOnly = true;
                }
                else
                {
                    return;
                }
            }

            if (Interlocked.CompareExchange(ref _memoryDropPrepareRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(() => PrepareMemoryDropPayloadWorker(keyedOnly, packageIds, clearKeysByPackage));
        }

        private static void PrepareMemoryDropPayloadWorker(bool keyedOnly, List<string> packageIds, Dictionary<string, Dictionary<string, HashSet<string>>> clearKeysByPackage)
        {
            try
            {
                MemoryDropPayload payload = BuildMemoryDropPayload(keyedOnly, packageIds, clearKeysByPackage);
                lock (_pendingInjectLock)
                {
                    if (payload != null)
                    {
                        _pendingMemoryDropPayload = payload;
                        if (payload.KeyedOnly)
                        {
                            _pendingKeyedMemoryDrop = false;
                        }
                        else
                        {
                            if (payload.PackageScoped && packageIds != null)
                            {
                                foreach (string packageId in packageIds)
                                {
                                    _pendingMemoryDropPackageIds.Remove(packageId);
                                    _pendingMemoryDropClearKeysByPackage.Remove(packageId);
                                }
                            }
                            else
                            {
                                _pendingMemoryDrop = false;
                                _pendingKeyedMemoryDrop = false;
                                _pendingMemoryDropPackageIds.Clear();
                                _pendingMemoryDropClearKeysByPackage.Clear();
                            }
                        }
                    }
                    else
                    {
                        if (keyedOnly)
                        {
                            _pendingKeyedMemoryDrop = false;
                        }
                        else if (packageIds != null)
                        {
                            foreach (string packageId in packageIds)
                            {
                                _pendingMemoryDropPackageIds.Remove(packageId);
                                _pendingMemoryDropClearKeysByPackage.Remove(packageId);
                            }
                        }
                        else
                        {
                            _pendingMemoryDrop = false;
                            _pendingKeyedMemoryDrop = false;
                            _pendingMemoryDropPackageIds.Clear();
                            _pendingMemoryDropClearKeysByPackage.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_MemoryDropFailed", ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop preparation failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _memoryDropPrepareRunning, 0);
                StartMemoryDropPreparationIfNeeded();
            }
        }


        internal static void PumpMainThreadDispatcher()
        {
            if (_activeMemoryDropApply != null)
            {
                ContinueMemoryDropApply();
            }

            MemoryDropPayload payload = null;
            lock (_pendingInjectLock)
            {
                if (_activeMemoryDropApply == null && _pendingMemoryDropPayload != null)
                {
                    payload = _pendingMemoryDropPayload;
                    _pendingMemoryDropPayload = null;
                }
            }

            if (payload != null)
            {
                BeginMemoryDropApply(payload);
            }

            if (_activeMemoryDropApply != null)
            {
                ContinueMemoryDropApply();
            }

            StartMemoryDropPreparationIfNeeded();

            PumpStaticCachedTranslationRefresh();
        }

        private static MemoryDropPayload BuildMemoryDropPayload(bool keyedOnly)
        {
            return BuildMemoryDropPayload(keyedOnly, null);
        }

        private static MemoryDropPayload BuildMemoryDropPayload(bool keyedOnly, List<string> packageIds)
        {
            return BuildMemoryDropPayload(keyedOnly, packageIds, null);
        }

        private static MemoryDropPayload BuildMemoryDropPayload(bool keyedOnly, List<string> packageIds, Dictionary<string, Dictionary<string, HashSet<string>>> clearKeysByPackage)
        {
            if (AutoTranslatorMod.Settings == null) return null;

            string packPath = GetLocalPackPath();
            string targetFolder = GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
            string langRoot = Path.Combine(packPath, "Languages", targetFolder);
            if (!Directory.Exists(langRoot)) return null;

            string keyedPath = Path.Combine(langRoot, "Keyed");
            bool packageScoped = !keyedOnly && packageIds != null && packageIds.Count > 0;
            HashSet<string> cleanPackageIds = packageScoped
                ? new HashSet<string>(packageIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(CleanPackageIdForFileMatch), StringComparer.OrdinalIgnoreCase)
                : null;

            if (packageScoped && cleanPackageIds.Count == 0)
            {
                return null;
            }

            if (!packageScoped && keyedOnly && IsKeyedMemoryDropStampCurrent(keyedPath, targetFolder))
            {
                return null;
            }

            if (!packageScoped && !keyedOnly && IsFullMemoryDropStampCurrent(langRoot, targetFolder))
            {
                Log.Message("[AutoTranslationCore] Memory Drop skipped: translation files unchanged since last full injection.");
                return null;
            }

            var payload = new MemoryDropPayload
            {
                KeyedOnly = keyedOnly,
                PackageScoped = packageScoped,
                PackageId = packageScoped ? string.Join(",", packageIds) : null,
                TargetFolder = targetFolder,
                LangRoot = langRoot,
                KeyedPath = keyedPath,
                ClearKeysByDefType = BuildMemoryDropClearKeys(packageIds, clearKeysByPackage),
                Keyed = Directory.Exists(keyedPath)
                    ? LoadXmlFilesToDictForMemoryDrop(keyedPath, cleanPackageIds)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DefsByType = new List<MemoryDropDefTypePayload>(),
                PreparedAtUtc = DateTime.UtcNow
            };

            string defPath = Path.Combine(langRoot, "DefInjected");
            if (!keyedOnly && Directory.Exists(defPath))
            {
                foreach (var typeDir in Directory.GetDirectories(defPath))
                {
                    string defTypeName = Path.GetFileName(typeDir);
                    var defDict = LoadXmlFilesToDictForMemoryDrop(typeDir, cleanPackageIds);
                    if (defDict.Count == 0) continue;

                    payload.DefsByType.Add(new MemoryDropDefTypePayload
                    {
                        DefTypeName = defTypeName,
                        Data = defDict
                    });
                }
            }

            return payload;
        }

        private static void MergePendingPackageClearKeys(string packageId, Dictionary<string, HashSet<string>> clearKeysByDefType)
        {
            if (string.IsNullOrWhiteSpace(packageId) || clearKeysByDefType == null || clearKeysByDefType.Count == 0) return;

            if (!_pendingMemoryDropClearKeysByPackage.TryGetValue(packageId, out Dictionary<string, HashSet<string>> target))
            {
                target = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                _pendingMemoryDropClearKeysByPackage[packageId] = target;
            }

            foreach (var pair in clearKeysByDefType)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Count == 0) continue;

                if (!target.TryGetValue(pair.Key, out HashSet<string> keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    target[pair.Key] = keys;
                }

                foreach (string key in pair.Value)
                {
                    if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                }
            }
        }

        private static Dictionary<string, Dictionary<string, HashSet<string>>> CloneClearKeysForPackages(List<string> packageIds)
        {
            var clone = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);
            if (packageIds == null || packageIds.Count == 0) return clone;

            foreach (string packageId in packageIds)
            {
                if (string.IsNullOrWhiteSpace(packageId)) continue;
                if (!_pendingMemoryDropClearKeysByPackage.TryGetValue(packageId, out Dictionary<string, HashSet<string>> source)) continue;

                var packageClone = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in source)
                {
                    packageClone[pair.Key] = pair.Value != null
                        ? new HashSet<string>(pair.Value, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                clone[packageId] = packageClone;
            }

            return clone;
        }

        private static Dictionary<string, HashSet<string>> BuildMemoryDropClearKeys(List<string> packageIds, Dictionary<string, Dictionary<string, HashSet<string>>> clearKeysByPackage)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            if (packageIds == null || clearKeysByPackage == null || clearKeysByPackage.Count == 0) return result;

            foreach (string packageId in packageIds)
            {
                if (string.IsNullOrWhiteSpace(packageId)) continue;
                if (!clearKeysByPackage.TryGetValue(packageId, out Dictionary<string, HashSet<string>> packageKeys)) continue;

                foreach (var pair in packageKeys)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null || pair.Value.Count == 0) continue;

                    if (!result.TryGetValue(pair.Key, out HashSet<string> keys))
                    {
                        keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        result[pair.Key] = keys;
                    }

                    foreach (string key in pair.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
                    }
                }
            }

            return result;
        }

        private static Dictionary<string, string> LoadXmlFilesToDictForMemoryDrop(string path, HashSet<string> cleanPackageIds)
        {
            if (cleanPackageIds == null || cleanPackageIds.Count == 0)
            {
                return LoadXmlFilesToDict(path);
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(path)) return dict;

            foreach (var file in GetXmlFilesCached(path, SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                if (!cleanPackageIds.Any(cleanId => fileName.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                var fileDict = LoadXmlFileToDict(file);
                foreach (var pair in fileDict)
                {
                    dict[pair.Key] = pair.Value;
                }
            }

            return dict;
        }

        private static string CleanPackageIdForFileMatch(string packageId)
        {
            return (packageId ?? "").Replace(".", "_").ToLowerInvariant();
        }

        private static void BeginMemoryDropApply(MemoryDropPayload payload)
        {
            if (payload == null) return;
            if (LanguageDatabase.activeLanguage == null || AutoTranslatorMod.Settings == null) return;

            _activeMemoryDropApply = new MemoryDropApplyState
            {
                Payload = payload,
                Timer = System.Diagnostics.Stopwatch.StartNew(),
                KeyedEntries = payload.Keyed != null
                    ? payload.Keyed.ToList()
                    : new List<KeyValuePair<string, string>>()
            };
        }

        private static void ContinueMemoryDropApply()
        {
            MemoryDropApplyState state = _activeMemoryDropApply;
            if (state == null) return;

            try
            {
                LoadedLanguage activeLang = LanguageDatabase.activeLanguage;
                if (activeLang == null || AutoTranslatorMod.Settings == null)
                {
                    FinishMemoryDropApply(state, false);
                    return;
                }

                ApplyPackageClearKeysIfNeeded(state, activeLang);
                if (ApplyKeyedBatch(state, activeLang)) return;
                if (!state.Payload.KeyedOnly && ApplyDefBatch(state, activeLang)) return;

                FinishMemoryDropApply(state, true);
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_MemoryDropFailed", ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop Failed: {ex.Message}");
                FinishMemoryDropApply(state, false);
            }
        }

        private static void ApplyPackageClearKeysIfNeeded(MemoryDropApplyState state, LoadedLanguage activeLang)
        {
            if (state == null || state.PackageClearApplied) return;
            state.PackageClearApplied = true;

            if (activeLang == null || state.Payload == null || !state.Payload.PackageScoped ||
                state.Payload.ClearKeysByDefType == null || state.Payload.ClearKeysByDefType.Count == 0)
            {
                return;
            }

            if (state.Payload.ClearKeysByDefType.TryGetValue("Keyed", out HashSet<string> keyedKeys) &&
                keyedKeys != null && keyedKeys.Count > 0 && activeLang.keyedReplacements != null)
            {
                foreach (string key in keyedKeys)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (activeLang.keyedReplacements.Remove(key))
                    {
                        state.ClearedKeyed++;
                    }
                }
            }

            foreach (var pair in state.Payload.ClearKeysByDefType)
            {
                if (pair.Value == null || pair.Value.Count == 0) continue;
                if (pair.Key.Equals("Keyed", StringComparison.OrdinalIgnoreCase)) continue;

                Type defType = GenTypes.GetTypeInAnyAssembly(pair.Key);
                if (defType == null) continue;

                DefInjectionPackage package = activeLang.defInjections?.FirstOrDefault(p => p.defType == defType);
                if (package?.injections == null) continue;

                foreach (string key in pair.Value)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (package.injections.Remove(key))
                    {
                        state.ClearedDefs++;
                        state.NeedsDataInjection = true;
                    }
                }
            }
        }

        private static bool ApplyKeyedBatch(MemoryDropApplyState state, LoadedLanguage activeLang)
        {
            if (state == null || activeLang == null || state.KeyedEntries == null) return false;
            if (state.KeyedIndex >= state.KeyedEntries.Count) return false;

            int remaining = MemoryDropKeyedApplyBudget;
            while (state.KeyedIndex < state.KeyedEntries.Count && remaining-- > 0)
            {
                var kvp = state.KeyedEntries[state.KeyedIndex++];

                if (activeLang.keyedReplacements.TryGetValue(kvp.Key, out LoadedLanguage.KeyedReplacement existingReplacement))
                {
                    RememberStaticTranslationSourceHint(kvp.Key, existingReplacement.value);
                }

                activeLang.keyedReplacements[kvp.Key] = new LoadedLanguage.KeyedReplacement
                {
                    key = kvp.Key,
                    value = kvp.Value
                };
                RememberStaticTranslationTargetHint(kvp.Key, kvp.Value);
                state.InjectedKeyed++;
            }

            return state.KeyedIndex < state.KeyedEntries.Count;
        }

        private static bool ApplyDefBatch(MemoryDropApplyState state, LoadedLanguage activeLang)
        {
            if (state == null || activeLang == null || state.Payload == null || state.Payload.DefsByType == null) return false;

            int remaining = MemoryDropDefApplyBudget;
            while (state.DefTypeIndex < state.Payload.DefsByType.Count && remaining > 0)
            {
                if (state.CurrentDefEntries == null)
                {
                    MemoryDropDefTypePayload typePayload = state.Payload.DefsByType[state.DefTypeIndex];
                    Type defType = GenTypes.GetTypeInAnyAssembly(typePayload.DefTypeName);
                    if (defType == null)
                    {
                        state.DefTypeIndex++;
                        continue;
                    }

                    DefInjectionPackage package = activeLang.defInjections.FirstOrDefault(p => p.defType == defType);
                    if (package == null)
                    {
                        package = new DefInjectionPackage(defType);
                        activeLang.defInjections.Add(package);
                    }

                    if (package.injections == null)
                    {
                        package.injections = new Dictionary<string, DefInjectionPackage.DefInjection>();
                    }

                    state.CurrentPackage = package;
                    state.CurrentDefEntries = typePayload.Data != null
                        ? typePayload.Data.ToList()
                        : new List<KeyValuePair<string, string>>();
                    state.CurrentDefEntryIndex = 0;
                }

                while (state.CurrentDefEntryIndex < state.CurrentDefEntries.Count && remaining-- > 0)
                {
                    var kvp = state.CurrentDefEntries[state.CurrentDefEntryIndex++];
                    state.CurrentPackage.injections[kvp.Key] = new DefInjectionPackage.DefInjection
                    {
                        path = kvp.Key,
                        injection = kvp.Value
                    };
                    state.InjectedDefs++;
                    state.NeedsDataInjection = true;
                }

                if (state.CurrentDefEntryIndex >= state.CurrentDefEntries.Count)
                {
                    state.CurrentPackage = null;
                    state.CurrentDefEntries = null;
                    state.CurrentDefEntryIndex = 0;
                    state.DefTypeIndex++;
                }
            }

            return state.DefTypeIndex < state.Payload.DefsByType.Count || state.CurrentDefEntries != null;
        }

        private static void FinishMemoryDropApply(MemoryDropApplyState state, bool success)
        {
            if (state == null) return;

            try
            {
                if (success && !state.Payload.KeyedOnly && state.NeedsDataInjection)
                {
                    LoadedLanguage activeLang = LanguageDatabase.activeLanguage;
                    if (activeLang != null)
                    {
                        activeLang.InjectIntoData_BeforeImpliedDefs();
                        activeLang.InjectIntoData_AfterImpliedDefs();
                        NormalizeInjectedDefs();
                    }
                }

                if (success && (state.InjectedKeyed > 0 || state.InjectedDefs > 0 || state.ClearedKeyed > 0 || state.ClearedDefs > 0))
                {
                    if (state.InjectedKeyed > 0 || state.ClearedKeyed > 0)
                    {
                        RequestStaticCachedTranslationRefresh();
                    }

                    if (!state.Payload.KeyedOnly)
                    {
                        AutoTranslatorSettings.AddLog("🪂 " + AutoTranslatorAPI.TranslateText("ATC_Log_MemoryDropSuccess", state.InjectedKeyed, state.InjectedDefs));
                    }

                    string mode = state.Payload.KeyedOnly
                        ? "Keyed memory prewarm"
                        : state.Payload.PackageScoped
                            ? "Package memory drop"
                            : "Memory Drop Success";
                    Log.Message($"[AutoTranslationCore] {mode}: Injected {state.InjectedKeyed} Keyed & {state.InjectedDefs} Defs without restart. Cleared {state.ClearedKeyed} Keyed & {state.ClearedDefs} Defs. Package={state.Payload.PackageId ?? "<full>"}");
                }

                if (success && state.Payload.KeyedOnly && state.InjectedKeyed > 0)
                {
                    MarkKeyedMemoryDropStamp(state.Payload.KeyedPath, state.Payload.TargetFolder);
                }
                else if (success && !state.Payload.KeyedOnly && !state.Payload.PackageScoped)
                {
                    MarkFullMemoryDropStamp(state.Payload.LangRoot, state.Payload.TargetFolder);
                }
            }
            finally
            {
                state.Timer?.Stop();
                if (!state.Payload.KeyedOnly)
                {
                    AutoTranslatorPerf.RecordMemoryDrop(state.Timer != null ? state.Timer.ElapsedMilliseconds : 0, state.InjectedKeyed, state.InjectedDefs);
                }

                if (ReferenceEquals(_activeMemoryDropApply, state))
                {
                    _activeMemoryDropApply = null;
                }
                Interlocked.Exchange(ref _startupFullMemoryDropQueued, 0);
                StartMemoryDropPreparationIfNeeded();
            }
        }

        private static void ApplyMemoryDropPayloadImmediate(MemoryDropPayload payload, System.Diagnostics.Stopwatch timer)
        {
            if (payload == null) return;
            if (LanguageDatabase.activeLanguage == null || AutoTranslatorMod.Settings == null) return;

            var state = new MemoryDropApplyState
            {
                Payload = payload,
                Timer = timer ?? System.Diagnostics.Stopwatch.StartNew(),
                KeyedEntries = payload.Keyed != null
                    ? payload.Keyed.ToList()
                    : new List<KeyValuePair<string, string>>()
            };

            LoadedLanguage activeLang = LanguageDatabase.activeLanguage;
            while (ApplyKeyedBatch(state, activeLang)) { }
            while (!payload.KeyedOnly && ApplyDefBatch(state, activeLang)) { }
            FinishMemoryDropApply(state, true);
        }

        private static void NormalizeInjectedDefs()
        {
            foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                NormalizeAutoGeneratedRecipeText(recipe);
            }

            foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                NormalizeThingDescriptionLead(thingDef);

                if (thingDef.race != null && !string.IsNullOrEmpty(thingDef.label))
                {

                    if (thingDef.race.corpseDef != null && thingDef.race.corpseDef.defName == "Corpse_" + thingDef.defName)
                    {
                        thingDef.race.corpseDef.label = "CorpseLabel".Translate(thingDef.label).Resolve();
                    }


                    if (thingDef.race.meatDef != null && thingDef.race.meatDef.defName == "Meat_" + thingDef.defName)
                    {
                        thingDef.race.meatDef.label = "MeatLabel".Translate(thingDef.label).Resolve();
                    }


                    if (thingDef.race.leatherDef != null && !string.IsNullOrEmpty(thingDef.race.leatherDef.label) && thingDef.race.leatherDef.defName == "Leather_" + thingDef.defName)
                    {
                        string engName = thingDef.defName;
                        if (thingDef.race.leatherDef.label.IndexOf(engName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            thingDef.race.leatherDef.label = System.Text.RegularExpressions.Regex.Replace(
                                thingDef.race.leatherDef.label,
                                engName,
                                thingDef.label,
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase
                            );
                        }
                    }
                }
            }
        }


        // 這個方法負責處理 記憶體DropInjectNow 相關流程。
        // EN: This method handles memory drop inject now.
        public static void MemoryDrop_InjectNow()
        {
            MemoryDrop_InjectNow(false);
        }

        public static void MemoryDrop_InjectKeyedNow()
        {
            MemoryDrop_InjectNow(true);
        }

        private static void MemoryDrop_InjectNow(bool keyedOnly)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                MemoryDropPayload payload = BuildMemoryDropPayload(keyedOnly);
                if (payload == null) return;
                ApplyMemoryDropPayloadImmediate(payload, timer);
                timer = null;
            }
            catch (Exception ex)
            {

                AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_MemoryDropFailed", ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop Failed: {ex.Message}");
            }
            finally
            {
                timer?.Stop();
            }
        }

        // 這個方法負責清除 Global翻譯Database 資料。
        // EN: This method clears global translation database.
        private static void NormalizeAutoGeneratedRecipeText(RecipeDef recipe)
        {
            if (recipe == null || recipe.products == null || recipe.products.Count == 0) return;

            ThingDef productDef = recipe.products[0].thingDef;
            if (productDef == null || string.IsNullOrWhiteSpace(productDef.label)) return;
            if (!ShouldNormalizeAutoGeneratedRecipeLabel(recipe)) return;

            string verb = GetRecipeVerb(recipe);
            if (string.IsNullOrWhiteSpace(verb)) return;

            recipe.label = $"{verb} {productDef.label}";
            if (recipe.defName.StartsWith("Make_", StringComparison.OrdinalIgnoreCase) &&
                IsMakeRecipeVerb(recipe) &&
                !string.IsNullOrWhiteSpace(recipe.jobString) &&
                ShouldNormalizeAutoGeneratedRecipeJobString(recipe))
            {
                recipe.jobString = "RecipeMakeJobString".Translate(productDef.label).Resolve();
            }
        }

        private static bool IsKeyedMemoryDropStampCurrent(string keyedPath, string targetFolder)
        {
            string stamp = BuildKeyedMemoryDropStamp(keyedPath, targetFolder);
            lock (_memoryDropStampLock)
            {
                return !string.IsNullOrEmpty(stamp) &&
                       string.Equals(_lastKeyedMemoryDropStamp, stamp, StringComparison.Ordinal);
            }
        }

        private static void MarkKeyedMemoryDropStamp(string keyedPath, string targetFolder)
        {
            string stamp = BuildKeyedMemoryDropStamp(keyedPath, targetFolder);
            if (string.IsNullOrEmpty(stamp)) return;

            lock (_memoryDropStampLock)
            {
                _lastKeyedMemoryDropStamp = stamp;
            }
        }

        private static string BuildKeyedMemoryDropStamp(string keyedPath, string targetFolder)
        {
            try
            {
                long ticks = 0L;
                if (Directory.Exists(keyedPath))
                {
                    ticks = Directory.GetLastWriteTimeUtc(keyedPath).Ticks;
                    foreach (string file in GetXmlFilesCached(keyedPath, SearchOption.AllDirectories))
                    {
                        long fileTicks = File.GetLastWriteTimeUtc(file).Ticks;
                        if (fileTicks > ticks) ticks = fileTicks;
                    }
                }

                return (targetFolder ?? "") + "|" + ticks.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static bool IsFullMemoryDropStampCurrent(string langRoot, string targetFolder)
        {
            string stamp = BuildFullMemoryDropStamp(langRoot, targetFolder);
            lock (_memoryDropStampLock)
            {
                return !string.IsNullOrEmpty(stamp) &&
                       string.Equals(_lastFullMemoryDropStamp, stamp, StringComparison.Ordinal);
            }
        }

        private static void MarkFullMemoryDropStamp(string langRoot, string targetFolder)
        {
            string stamp = BuildFullMemoryDropStamp(langRoot, targetFolder);
            if (string.IsNullOrEmpty(stamp)) return;

            lock (_memoryDropStampLock)
            {
                _lastFullMemoryDropStamp = stamp;
            }
        }

        private static string BuildFullMemoryDropStamp(string langRoot, string targetFolder)
        {
            try
            {
                return (targetFolder ?? "") + "|" + BuildXmlTreeFingerprint(langRoot);
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldNormalizeAutoGeneratedRecipeLabel(RecipeDef recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.defName) || string.IsNullOrWhiteSpace(recipe.label)) return false;
            if (!recipe.defName.StartsWith("Make_", StringComparison.OrdinalIgnoreCase)) return false;
            if (LanguageDetector.LooksLikeTargetLanguage(recipe.label, AutoTranslatorMod.Settings.TargetLang)) return false;

            return recipe.label.StartsWith("make ", StringComparison.OrdinalIgnoreCase) ||
                   recipe.label.StartsWith("make_", StringComparison.OrdinalIgnoreCase) ||
                   recipe.label.StartsWith("Make ", StringComparison.Ordinal);
        }

        private static bool ShouldNormalizeAutoGeneratedRecipeJobString(RecipeDef recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.jobString)) return false;
            if (LanguageDetector.LooksLikeTargetLanguage(recipe.jobString, AutoTranslatorMod.Settings.TargetLang)) return false;
            return recipe.jobString.StartsWith("Making ", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMakeRecipeVerb(RecipeDef recipe)
        {
            return string.Equals(GetRecipeVerbKind(recipe), "make", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRecipeVerb(RecipeDef recipe)
        {
            return GetRecipeVerbText(GetRecipeVerbKind(recipe));
        }

        private static string GetRecipeVerbKind(RecipeDef recipe)
        {
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.defName)) return "";
            string defName = recipe.defName;
            string jobString = recipe.jobString ?? "";
            string label = recipe.label ?? "";
            string combined = defName + " " + jobString + " " + label;

            if (combined.IndexOf("smelt", StringComparison.OrdinalIgnoreCase) >= 0) return "smelt";
            if (combined.IndexOf("refin", StringComparison.OrdinalIgnoreCase) >= 0) return "refine";
            if (combined.IndexOf("extract", StringComparison.OrdinalIgnoreCase) >= 0) return "extract";
            if (combined.IndexOf("incubat", StringComparison.OrdinalIgnoreCase) >= 0) return "incubate";
            if (combined.IndexOf("reclaim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                combined.IndexOf("recycle", StringComparison.OrdinalIgnoreCase) >= 0) return "recycle";
            if (defName.StartsWith("Make_", StringComparison.OrdinalIgnoreCase)) return "make";
            return "";
        }

        private static string GetRecipeVerbText(string verb)
        {
            bool simplified = AutoTranslatorMod.Settings.TargetLang == TargetLanguage.Simplified;
            switch (verb)
            {
                case "make": return simplified ? "制造" : "製作";
                case "smelt": return simplified ? "熔炼" : "熔煉";
                case "refine": return simplified ? "提炼" : "提煉";
                case "extract": return simplified ? "提取" : "提取";
                case "incubate": return simplified ? "培育" : "培育";
                case "recycle": return simplified ? "回收" : "回收";
                default: return "";
            }
        }

        private static void NormalizeThingDescriptionLead(ThingDef thingDef)
        {
            if (thingDef == null || string.IsNullOrWhiteSpace(thingDef.label) || string.IsNullOrWhiteSpace(thingDef.description)) return;
            if (AutoTranslatorMod.Settings.TargetLang != TargetLanguage.Simplified &&
                AutoTranslatorMod.Settings.TargetLang != TargetLanguage.Traditional) return;

            string label = thingDef.label.Trim();
            string description = thingDef.description.TrimStart();
            if (description.StartsWith(label, StringComparison.OrdinalIgnoreCase)) return;

            string[] markers = { "是一", "是", "為", "为" };
            int markerIndex = -1;
            string marker = "";
            foreach (string candidate in markers)
            {
                int index = description.IndexOf(candidate, StringComparison.Ordinal);
                if (index > 0 && index <= 18 && (markerIndex < 0 || index < markerIndex))
                {
                    markerIndex = index;
                    marker = candidate;
                }
            }

            if (markerIndex <= 0 || markerIndex > 18) return;

            string lead = description.Substring(0, markerIndex).Trim();
            if (lead.Length < 2 || lead.Length > 18) return;
            if (!lead.Any(IsCjkChar) || lead.Contains(" ") || lead.Contains("\n") || lead.Contains("\r")) return;

            thingDef.description = label + description.Substring(markerIndex);
        }

        private static bool IsCjkChar(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        private static void ClearGlobalTranslationDatabase()
        {
            GlobalPrimaryDefDict.Clear();
            GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear();
            GlobalSecondaryKeyedDict.Clear();
            AutoTranslatorSettings.AddLog("🧹 " + "ATC_Log_Clean".Translate());
        }


        // 這個方法負責建立 Global翻譯Database 所需資料。
        // EN: This method builds global translation database.
        private static void InvalidateGlobalTranslationDatabaseCache()
        {
            lock (GlobalTranslationDatabaseCacheLock)
            {
                GlobalTranslationDatabaseCacheGeneration++;
                GlobalTranslationDatabaseCacheKey = null;
                GlobalTranslationDatabaseCachedPrimaryDefDict = null;
                GlobalTranslationDatabaseCachedSecondaryDefDict = null;
                GlobalTranslationDatabaseCachedPrimaryKeyedDict = null;
                GlobalTranslationDatabaseCachedSecondaryKeyedDict = null;
            }
        }

        private static void BuildGlobalTranslationDatabase(List<ModMetaData> mods)
        {
            AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_Init".Translate());

            var settings = AutoTranslatorMod.Settings;
            settings.SubTaskName = "ATC_SubTask_AnalyzingDict".Translate();
            settings.SubProgress = 0f;

            GlobalPrimaryDefDict.Clear(); GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear(); GlobalSecondaryKeyedDict.Clear();

            string cacheKey = BuildGlobalTranslationDatabaseCacheKey(mods, settings.TargetLang);
            int cacheGeneration;
            lock (GlobalTranslationDatabaseCacheLock)
            {
                cacheGeneration = GlobalTranslationDatabaseCacheGeneration;
                if (!string.IsNullOrEmpty(cacheKey) &&
                    string.Equals(GlobalTranslationDatabaseCacheKey, cacheKey, StringComparison.Ordinal) &&
                    GlobalTranslationDatabaseCachedPrimaryDefDict != null &&
                    GlobalTranslationDatabaseCachedSecondaryDefDict != null &&
                    GlobalTranslationDatabaseCachedPrimaryKeyedDict != null &&
                    GlobalTranslationDatabaseCachedSecondaryKeyedDict != null)
                {
                    GlobalPrimaryDefDict = new Dictionary<string, string>(GlobalTranslationDatabaseCachedPrimaryDefDict, StringComparer.OrdinalIgnoreCase);
                    GlobalSecondaryDefDict = new Dictionary<string, string>(GlobalTranslationDatabaseCachedSecondaryDefDict, StringComparer.OrdinalIgnoreCase);
                    GlobalPrimaryKeyedDict = new Dictionary<string, string>(GlobalTranslationDatabaseCachedPrimaryKeyedDict, StringComparer.OrdinalIgnoreCase);
                    GlobalSecondaryKeyedDict = new Dictionary<string, string>(GlobalTranslationDatabaseCachedSecondaryKeyedDict, StringComparer.OrdinalIgnoreCase);
                    settings.SubProgress = 1f;
                    settings.SubTaskName = "ATC_SubTask_DictDone".Translate();
                    AutoTranslatorSettings.AddLog("??" + AutoTranslatorAPI.TranslateText("ATC_Log_InitDone", GlobalPrimaryDefDict.Count));
                    return;
                }
            }

            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);

            Action<string, Dictionary<string, string>, TargetLanguage?> loadKeyed = (languageDir, dict, lang) => {
                if (string.IsNullOrEmpty(languageDir) || dict == null || !Directory.Exists(languageDir)) return;
                var keyed = LoadXmlFilesToDict(Path.Combine(languageDir, "Keyed"), lang);
                foreach (var kv in keyed) dict[kv.Key] = kv.Value;
            };

            Action<string, Dictionary<string, string>, TargetLanguage?> loadDef = (path, dict, lang) => {
                if (!Directory.Exists(path)) return;
                foreach (var typeDir in Directory.GetDirectories(path))
                {
                    string defType = Path.GetFileName(typeDir);
                    foreach (var file in GetXmlFilesCached(typeDir, SearchOption.AllDirectories))
                    {
                        var d = LoadXmlFileToDict(file, lang);
                        foreach (var kv in d) dict[$"{defType}/{kv.Key}"] = kv.Value;
                    }
                }
                foreach (var file in GetXmlFilesCached(path, SearchOption.TopDirectoryOnly))
                {
                    var d = LoadXmlFileToDict(file, lang);
                    foreach (var kv in d) dict[$"General/{kv.Key}"] = kv.Value;
                }
            };

            string localPackLangRoot = Path.Combine(GetLocalPackPath(), "Languages");
            foreach (string targetLangDir in ResolveLanguageFolders(localPackLangRoot, targetFolder))
            {
                loadKeyed(targetLangDir, GlobalPrimaryKeyedDict, settings.TargetLang);
                loadDef(Path.Combine(targetLangDir, "DefInjected"), GlobalPrimaryDefDict, settings.TargetLang);
            }

            if (!string.IsNullOrEmpty(otherFolder))
            {
                TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                foreach (string secondaryLangDir in ResolveLanguageFolders(localPackLangRoot, otherFolder))
                {
                    loadKeyed(secondaryLangDir, GlobalSecondaryKeyedDict, secLang);
                    loadDef(Path.Combine(secondaryLangDir, "DefInjected"), GlobalSecondaryDefDict, secLang);
                }
            }

            int modCount = mods != null ? mods.Count : 0;
            for (int i = 0; i < modCount; i++)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return;

                var mod = mods[i];

                settings.SubProgress = modCount > 0 ? (float)i / modCount : 1f;
                settings.SubTaskName = AutoTranslatorAPI.TranslateText("ATC_SubTask_BuildingDict", mod.Name);

                var langRoots = IsTranslationPatchMod(mod)
                    ? GetAllTranslationPatchLangPaths(mod)
                    : GetAllEffectiveLangPaths(mod);

                foreach (var langRoot in langRoots)
                {
                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        loadKeyed(targetLangDir, GlobalPrimaryKeyedDict, settings.TargetLang);
                    }

                    if (!string.IsNullOrEmpty(otherFolder))
                    {
                        TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                        foreach (string secondaryLangDir in ResolveLanguageFolders(langRoot, otherFolder))
                        {
                            loadKeyed(secondaryLangDir, GlobalSecondaryKeyedDict, secLang);
                        }
                    }


                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        loadDef(Path.Combine(targetLangDir, "DefInjected"), GlobalPrimaryDefDict, settings.TargetLang);
                    }

                    if (!string.IsNullOrEmpty(otherFolder))
                    {
                        TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                        foreach (string secondaryLangDir in ResolveLanguageFolders(langRoot, otherFolder))
                        {
                            loadDef(Path.Combine(secondaryLangDir, "DefInjected"), GlobalSecondaryDefDict, secLang);
                        }
                    }
                }
            }

            settings.SubProgress = 1f;
            settings.SubTaskName = "ATC_SubTask_DictDone".Translate();
            AutoTranslatorSettings.AddLog("✨ " + AutoTranslatorAPI.TranslateText("ATC_Log_InitDone", GlobalPrimaryDefDict.Count));
            lock (GlobalTranslationDatabaseCacheLock)
            {
                if (!string.IsNullOrEmpty(cacheKey) && cacheGeneration == GlobalTranslationDatabaseCacheGeneration)
                {
                    GlobalTranslationDatabaseCacheKey = cacheKey;
                    GlobalTranslationDatabaseCachedPrimaryDefDict = new Dictionary<string, string>(GlobalPrimaryDefDict, StringComparer.OrdinalIgnoreCase);
                    GlobalTranslationDatabaseCachedSecondaryDefDict = new Dictionary<string, string>(GlobalSecondaryDefDict, StringComparer.OrdinalIgnoreCase);
                    GlobalTranslationDatabaseCachedPrimaryKeyedDict = new Dictionary<string, string>(GlobalPrimaryKeyedDict, StringComparer.OrdinalIgnoreCase);
                    GlobalTranslationDatabaseCachedSecondaryKeyedDict = new Dictionary<string, string>(GlobalSecondaryKeyedDict, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        private static string BuildGlobalTranslationDatabaseCacheKey(List<ModMetaData> mods, TargetLanguage targetLang)
        {
            try
            {
                string targetFolder = GetFolderNameByLanguage(targetLang);
                string secondaryFolder = GetSecondaryFolderNameByLanguage(targetLang);
                string packLangRoot = Path.Combine(GetLocalPackPath(), "Languages");
                string packFingerprint = BuildResolvedLanguageFoldersFingerprint(packLangRoot, targetFolder, secondaryFolder);

                IEnumerable<string> modKeys = (mods ?? new List<ModMetaData>())
                    .Where(m => m != null && !string.IsNullOrEmpty(m.PackageId))
                    .Select(m =>
                    {
                        string root = m.RootDir?.FullName ?? "";
                        List<string> langRoots = IsTranslationPatchMod(m)
                            ? GetAllTranslationPatchLangPaths(m)
                            : GetAllEffectiveLangPaths(m);
                        string langFingerprint = BuildLanguageRootsFingerprint(langRoots, targetFolder, secondaryFolder);
                        string loadFolders = BuildFileSignature(Path.Combine(root, "LoadFolders.xml"));
                        return (m.PackageId ?? "").ToLowerInvariant() + "|" + NormalizeCachePath(root) + "|" + loadFolders + "|" + langFingerprint;
                    })
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);

                return targetLang + "|" + packFingerprint + "|" + string.Join(";", modKeys.ToArray());
            }
            catch
            {
                return null;
            }
        }

    }
}
