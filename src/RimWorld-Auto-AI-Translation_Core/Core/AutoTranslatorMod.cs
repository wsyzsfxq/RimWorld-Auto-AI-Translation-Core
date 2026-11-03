


using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責模組初始化、語言同步與設定載入。
// EN: This file handles module initialization, language synchronization, and settings loading.

namespace AutoTranslator_Core
{

    // 這個類別負責 自動翻譯器模組 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorMod.
    public partial class AutoTranslatorMod : Mod
    {
        // 這個欄位保存 設定 的執行狀態或快取資料。
        // EN: This field stores settings runtime state or cached data.
        public static AutoTranslatorSettings Settings;
        internal static string CoreModRoot { get; private set; } = string.Empty;


        // 這個欄位保存 主畫面執行緒Context 的執行狀態或快取資料。
        // EN: This field stores main thread context runtime state or cached data.
        public static System.Threading.SynchronizationContext MainThreadContext;


        // 這個欄位保存 cachedValid模組 的執行狀態或快取資料。
        // EN: This field stores cached valid mods runtime state or cached data.
        private static List<ModMetaData> _cachedValidMods = null;
        // 這個欄位保存已安裝模組數量與啟用狀態的快取資料。
        // EN: These fields cache the installed mod count and activation-state signature.
        private static int _lastInstalledModCount = -1;
        private static long _nextInstalledModStateCheckUtcTicks = 0L;
        private static bool _validModsCacheRefreshInFlight = false;
        private static int _validModsCacheGeneration = 0;
        private static int _validModsFilterSettingsRevision = 0;
        private static int _validModsCacheAppliedSettingsRevision = -1;
        private static int _validModsCacheVersion = 0;
        private static int _validModsCacheProgressCurrent = 0;
        private static int _validModsCacheProgressTotal = 0;
        private static string _validModsCacheProgressModName = "";
        private static string _lastInstalledModSignature = "";
        private static string _pendingInstalledModSignature = "";
        // 這個欄位保存 cached雲端Lookup 的執行狀態或快取資料。
        // EN: This field stores cached cloud lookup runtime state or cached data.
        private static Dictionary<string, List<CloudModRecord>> _cachedCloudLookup = null;
        // 這個欄位保存 last雲端登錄Count 的執行狀態或快取資料。
        // EN: This field stores last cloud registry count runtime state or cached data.
        private static int _lastCloudRegistryCount = -1;
        private static int _lastCloudRegistryGeneration = -1;
        // 這個欄位保存 last雲端語言Folder 的執行狀態或快取資料。
        // EN: This field stores last cloud language folder runtime state or cached data.
        private static string _lastCloudLangFolder = "";
        // 這個欄位保存 lastActive分頁 的執行狀態或快取資料。
        // EN: This field stores last active tab runtime state or cached data.
        private static int _lastActiveTab = -1;
        // 這個欄位保存 cached雲端Display模組 的執行狀態或快取資料。
        // EN: This field stores cached cloud display mods runtime state or cached data.
        private static List<ModMetaData> _cachedCloudDisplayMods = null;
        // 這個欄位保存 cached雲端搜尋Text 的執行狀態或快取資料。
        // EN: This field stores cached cloud search text runtime state or cached data.
        private static string _cachedCloudSearchText = null;
        // 這個欄位保存 cached雲端Valid模組Count 的執行狀態或快取資料。
        // EN: This field stores cached cloud valid mod count runtime state or cached data.
        private static int _cachedCloudValidModCount = -1;
        private static int _cachedCloudDisplayValidVersion = -1;
        private static int _cachedCloudDisplayTypeMask = -1;
        private static int _cachedCloudDisplayRegistryGeneration = -1;
        private static string _cachedCloudDisplayLangFolder = "";
        private static readonly List<CloudModRecord> EmptyCloudRecords = new List<CloudModRecord>(0);
        private static int _cachedCloudStatsRegistryCount = -1;
        private static int _cachedCloudStatsGeneration = -1;
        private static string _cachedCloudStatsLangFolder = "";
        private static int _cachedCloudCurrentLangCount = 0;
        private static int _cachedCloudOwnUploadCount = 0;
        private static List<CloudModRecord> _cachedOwnCloudRecords = null;
        private static int _cachedOwnCloudRecordsRegistryCount = -1;
        private static int _cachedOwnCloudRecordsGeneration = -1;
        private static string _cachedOwnCloudRecordsLangFolder = "";
        private static string _cachedOwnCloudRecordsSearchText = "";
        private static int _cachedOwnCloudRecordsTypeMask = -1;
        private static Dictionary<string, ModMetaData> _cachedCloudLocalModMap = null;
        private static int _cachedCloudLocalModMapCount = -1;
        private static int _cachedCloudLocalModMapVersion = -1;
        private static readonly Dictionary<string, int> _singleCorrectionCountCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _singleCorrectionCountFetchInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly LogViewCache _runtimeLogViewCache = new LogViewCache();
        private static readonly LogViewCache _warningLogViewCache = new LogViewCache();
        private static readonly LogViewCache _errorLogViewCache = new LogViewCache();

        private sealed class LogViewCache
        {
            public int SourceCount = -1;
            public string FirstLine = "";
            public string LastLine = "";
            public float Width = -1f;
            public readonly List<string> DisplayLogs = new List<string>();
            public readonly List<float> Heights = new List<float>();
            public float TotalHeight = 0f;
            public bool FollowTail = true;
        }

        private sealed class ValidModSnapshot
        {
            public ModMetaData Mod;
            public string PackageId;
            public string Name;
            public string RootDir;
        }

        public static bool IsValidModsCacheRefreshing => _validModsCacheRefreshInFlight;
        public static int ValidModsCacheVersion => _validModsCacheVersion;
        public static float ValidModsCacheProgress
        {
            get
            {
                int total = _validModsCacheProgressTotal;
                if (total <= 0) return _validModsCacheRefreshInFlight ? 0f : 1f;
                return Mathf.Clamp01((float)_validModsCacheProgressCurrent / total);
            }
        }
        public static int ValidModsCacheProgressCurrent => _validModsCacheProgressCurrent;
        public static int ValidModsCacheProgressTotal => _validModsCacheProgressTotal;
        public static string ValidModsCacheProgressModName => _validModsCacheProgressModName ?? "";


        // 這個方法負責取得 Valid模組Cached 資料。
        // EN: This method gets valid mods cached.
        public static List<ModMetaData> GetValidModsCached()
        {
            QueueValidModsCacheRefreshIfNeeded();
            return _cachedValidMods ?? new List<ModMetaData>();
        }

        private static void QueueValidModsCacheRefreshIfNeeded()
        {
            if (_validModsCacheRefreshInFlight) return;

            long nowTicks = DateTime.UtcNow.Ticks;
            if (_cachedValidMods != null && nowTicks < _nextInstalledModStateCheckUtcTicks)
            {
                return;
            }

            List<ValidModSnapshot> snapshots = SnapshotInstalledModsForValidMods(
                out int installedCount,
                out string signature);
            _nextInstalledModStateCheckUtcTicks = nowTicks + TimeSpan.TicksPerSecond;

            if (_cachedValidMods != null &&
                !_validModsCacheRefreshInFlight &&
                _lastInstalledModCount == installedCount &&
                string.Equals(_lastInstalledModSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            int generation = ++_validModsCacheGeneration;
            int settingsRevision = _validModsFilterSettingsRevision;
            _validModsCacheRefreshInFlight = true;
            _pendingInstalledModSignature = signature;
            _validModsCacheProgressCurrent = 0;
            _validModsCacheProgressTotal = snapshots.Count;
            _validModsCacheProgressModName = "";

            Task.Run(() =>
            {
                List<ModMetaData> validMods = new List<ModMetaData>();
                List<FilteredModInfo> filteredMods = new List<FilteredModInfo>();
                string error = null;

                try
                {
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        ValidModSnapshot snapshot = snapshots[i];
                        _validModsCacheProgressCurrent = i;
                        _validModsCacheProgressModName = snapshot != null ? snapshot.Name ?? "" : "";

                        bool includeInValidMods;
                        FilteredModInfo filteredInfo = EvaluateModFilter(snapshot, out includeInValidMods);
                        if (includeInValidMods && snapshot != null && snapshot.Mod != null)
                        {
                            validMods.Add(snapshot.Mod);
                        }
                        if (filteredInfo != null)
                        {
                            filteredMods.Add(filteredInfo);
                        }
                    }

                    _validModsCacheProgressCurrent = snapshots.Count;
                    _validModsCacheProgressModName = "";
                    validMods = validMods
                        .OrderBy(m => m.Name ?? "", StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    filteredMods = filteredMods
                        .OrderByDescending(info => info.IsFiltered)
                        .ThenBy(info => info.Name ?? "", StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    if (generation != _validModsCacheGeneration) return;

                    _validModsCacheRefreshInFlight = false;
                    _pendingInstalledModSignature = "";
                    _validModsCacheProgressCurrent = _validModsCacheProgressTotal;
                    _validModsCacheProgressModName = "";

                    if (!string.IsNullOrEmpty(error))
                    {
                        Verse.Log.Warning($"[AutoTranslationCore] Valid mod cache refresh failed: {error}");
                        _validModsCacheVersion++;
                        return;
                    }

                    _cachedValidMods = validMods ?? new List<ModMetaData>();
                    _cachedFilteredMods = filteredMods ?? new List<FilteredModInfo>();
                    AutoTranslatorSettings.FilteredModsCount = _cachedFilteredMods.Count(info => info.IsFiltered);
                    _lastInstalledModCount = installedCount;
                    _lastInstalledModSignature = signature;
                    _validModsCacheAppliedSettingsRevision = settingsRevision;
                    _validModsCacheVersion++;
                    _cachedCloudDisplayMods = null;
                    _cachedCloudLocalModMap = null;
                });
            });
        }

        private static List<ValidModSnapshot> SnapshotInstalledModsForValidMods(
            out int installedCount,
            out string signature)
        {
            List<ValidModSnapshot> snapshots = new List<ValidModSnapshot>();
            StringBuilder signatureBuilder = new StringBuilder();
            installedCount = 0;

            foreach (ModMetaData mod in Verse.ModLister.AllInstalledMods)
            {
                if (mod == null) continue;

                installedCount++;
                string packageId = mod.PackageId ?? "";
                string name = mod.Name ?? "";
                string rootDir = mod.RootDir != null ? mod.RootDir.FullName : "";

                signatureBuilder.Append(packageId).Append('|')
                    .Append(name).Append('|')
                    .Append(rootDir).Append('|')
                    .Append(mod.Active ? '1' : '0').Append('\n');

                snapshots.Add(new ValidModSnapshot
                {
                    Mod = mod,
                    PackageId = packageId,
                    Name = name,
                    RootDir = rootDir
                });
            }

            signature = installedCount + ":settings=" + _validModsFilterSettingsRevision + ":" + signatureBuilder;
            return snapshots;
        }

        private static bool ShouldSkipValidModPackage(string packageId)
        {
            if (string.IsNullOrWhiteSpace(packageId)) return true;
            return AutoTranslatorScanner.IsNonTranslatableSystemPackage(packageId);
        }


        // 這個方法負責處理 base 相關流程。
        // EN: This constructor initializes auto translator mod.
        public AutoTranslatorMod(ModContentPack content) : base(content)
        {
            CoreModRoot = content?.RootDir ?? string.Empty;
            EnsureNetworkDispatchReady();

            Settings = GetSettings<AutoTranslatorSettings>();
            LogLoadedLanguagePreference();
            SettingsWindowSizingPatch.EnsureInstalled();
            if (Settings.ApiConfigs == null || Settings.ApiConfigs.Count == 0)
            {
                Settings.ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
            }
            TryAutoSyncLanguageWithGame(resetCaches: false, log: false, writeSettings: true);

            AutoTranslator_StartupHook.EnsureInstalled();
            AutoTranslator_LongEventCompat.ExecuteWhenFinished(EnsureNetworkDispatchReady);
            AutoTranslator_LongEventCompat.ExecuteWhenFinished(CloudClientIdentity.InitializeOnMainThread);
        }

        // 這個方法負責確保 NetworkDispatchReady 已準備完成。
        // EN: This method ensures network dispatch ready is ready.
        internal static void EnsureNetworkDispatchReady()
        {
            if (!UnityData.IsInMainThread) return;

            MainThreadContext = System.Threading.SynchronizationContext.Current;
            ATC_Dispatcher.EnsureAlive();
        }
        // 這個方法負責取得 語言Label 資料。
        // EN: This method gets language label.
        private string GetLangLabel(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ATC_Lang_Traditional".Translate();
                case TargetLanguage.Simplified: return "ATC_Lang_Simplified".Translate();
                case TargetLanguage.Japanese: return "ATC_Lang_Japanese".Translate();
                case TargetLanguage.Korean: return "ATC_Lang_Korean".Translate();
                case TargetLanguage.Russian: return "ATC_Lang_Russian".Translate();
                case TargetLanguage.Ukrainian: return "ATC_Lang_Ukrainian".Translate();
                case TargetLanguage.English: return "ATC_Lang_English".Translate();

                case TargetLanguage.French: return "ATC_Lang_French".Translate();
                case TargetLanguage.German: return "ATC_Lang_German".Translate();
                case TargetLanguage.Spanish: return "ATC_Lang_Spanish".Translate();
                case TargetLanguage.Italian: return "ATC_Lang_Italian".Translate();
                case TargetLanguage.Polish: return "ATC_Lang_Polish".Translate();
                case TargetLanguage.Portuguese: return "ATC_Lang_Portuguese".Translate();
                case TargetLanguage.Turkish: return "ATC_Lang_Turkish".Translate();
                case TargetLanguage.Thai: return "ATC_Lang_Thai".Translate();
                default: return lang.ToString();
            }
        }


        // 這個方法負責處理 Sync語言WithGame 相關流程。
        // EN: This method handles sync language with game.
        private void SyncLanguageWithGame()
        {
            TryAutoSyncLanguageWithGame(resetCaches: true, log: true, writeSettings: true);
        }

        // 這個方法負責設定 目標語言 狀態。
        // EN: This method sets target language.
        private void SetTargetLanguage(TargetLanguage targetLang)
        {
            ApplyTargetLanguageChange(targetLang, true);
        }

        // 這個方法負責套用 目標語言Change 設定。
        // EN: This method applies target language change.
        private void ApplyTargetLanguageChange(TargetLanguage targetLang, bool manualSelection)
        {
            AutoTranslatorSettings settings = Settings;
            if (settings == null) return;
            TargetLanguage previousTarget = settings.TargetLang;
            TargetLanguage previousCloudTarget = settings.CloudTargetLang;
            bool previousManualSelection = settings.HasManualTargetLanguage;
            bool changed = previousTarget != targetLang;

            if (changed)
            {
                UIInterceptor.PrepareForLanguageChange();
            }

            settings.TargetLang = targetLang;
            settings.CloudTargetLang = targetLang;
            if (manualSelection) settings.HasManualTargetLanguage = true;

            try
            {
                // Persist the object we changed before any cache reload can fail.
                // Mod.WriteSettings writes its private settings reference instead.
                settings.Write();
            }
            catch (Exception ex)
            {
                settings.TargetLang = previousTarget;
                settings.CloudTargetLang = previousCloudTarget;
                settings.HasManualTargetLanguage = previousManualSelection;
                AutoTranslatorSettings.AddErrorLog("语言设置保存失败，未切换语言：" + ex.GetType().Name);
                Messages.Message(WfText("语言设置保存失败，仍保留之前的选择。请查看错误日志。",
                    "Could not save the language setting. The previous choice was kept; see the error log."),
                    MessageTypeDefOf.RejectInput, false);
                if (changed) TryRefreshLanguageDependentCaches();
                return;
            }

            Verse.Log.Message("[AutoTranslationCore] 语言选择已保存：目标=" + settings.TargetLang +
                "；手动=" + settings.HasManualTargetLanguage + "；配置=" + GetLanguagePreferenceFileName(settings));

            if (changed)
            {
                TryRefreshLanguageDependentCaches();
            }
        }

        private void LogLoadedLanguagePreference()
        {
            Verse.Log.Message("[AutoTranslationCore] 语言设置已读取：目标=" + Settings.TargetLang +
                "；云端=" + Settings.CloudTargetLang + "；手动=" + Settings.HasManualTargetLanguage +
                "；目标字段=" + Settings.TargetLanguageNodeWasLoaded +
                "；手动字段=" + Settings.ManualLanguageNodeWasLoaded +
                "；配置=" + GetLanguagePreferenceFileName(Settings));
        }

        private string GetLanguagePreferenceFileName(AutoTranslatorSettings settings)
        {
            Mod owner = settings?.Mod ?? this;
            return GenText.SanitizeFilename("Mod_" + (owner.Content?.FolderName ?? string.Empty) +
                "_" + owner.GetType().Name + ".xml");
        }

        private static void TryRefreshLanguageDependentCaches()
        {
            try { ResetLanguageDependentCaches(); }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddWarningLog("语言选择已保留，但译文刷新未完成：" + ex.GetType().Name);
                Messages.Message(WfText("语言选择已保留，但译文刷新未完成，请重试热重载。",
                    "The language choice was kept, but translations could not finish refreshing. Try hot reload again."),
                    MessageTypeDefOf.CautionInput, false);
            }
        }

        // 這個方法負責重置 語言DependentCaches 狀態。
        // EN: This method resets language dependent caches.
        private static void ResetLanguageDependentCaches()
        {
            ResetCloudFetchStateForLanguageChange();
            UIInterceptor.ReloadForLanguageChange();
            TargetedHardcodedUi.HardcodedUiTargetedPatchManager.RequestReload();
            ModUpdateDetector.ClearStatusCache();
            AutoTranslator_Core.Workflow.WorkflowTaskCoordinator.Instance.NotifyWorkbenchDataChanged();
            AutoTranslatorScanner.RequestMemoryDrop();
        }

        // 這個方法負責嘗試執行 自動Sync語言WithGame 並回報是否成功。
        // EN: This method tries to auto sync language with game and reports whether it succeeded.
        public static bool TryAutoSyncLanguageWithGame(bool resetCaches, bool log, bool writeSettings)
        {
            AutoTranslatorSettings settings = Settings;
            if (settings == null || settings.HasManualTargetLanguage) return false;
            if (!TryGetTargetLanguageFromActiveGameLanguage(out TargetLanguage detectedLang, out string activeFolder)) return false;
            if (settings.TargetLang == detectedLang && settings.CloudTargetLang == detectedLang) return false;

            if (resetCaches)
            {
                UIInterceptor.PrepareForLanguageChange();
            }

            settings.TargetLang = detectedLang;
            settings.CloudTargetLang = detectedLang;

            if (writeSettings)
            {
                try { settings.Write(); }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddWarningLog("跟随游戏的语言选择尚未保存：" + ex.GetType().Name);
                }
            }

            if (resetCaches)
            {
                TryRefreshLanguageDependentCaches();
            }

            if (log)
            {
                Verse.Log.Message("[AutoTranslationCore] 🔄 " + "ATC_Log_AutoSyncLanguage".Translate(activeFolder));
            }

            return true;
        }

        // 這個方法負責嘗試執行 Get目標語言FromActiveGame語言 並回報是否成功。
        // EN: This method tries to get target language from active game language and reports whether it succeeded.
        private static bool TryGetTargetLanguageFromActiveGameLanguage(out TargetLanguage targetLang, out string activeFolder)
        {
            targetLang = TargetLanguage.English;
            activeFolder = Verse.LanguageDatabase.activeLanguage?.folderName;
            if (string.IsNullOrEmpty(activeFolder)) return false;

            // 1.7 language folders may carry a localized suffix, for example
            // ChineseSimplified (简体中文). Use the same matcher as file scanning.
            foreach (TargetLanguage language in Enum.GetValues(typeof(TargetLanguage)))
            {
                if (!AutoTranslatorScanner.IsLanguageFolderMatch(activeFolder,
                        AutoTranslatorScanner.GetFolderNameByLanguage(language))) continue;
                targetLang = language;
                return true;
            }
            return false;
        }

        // 這個方法負責重置 雲端取得狀態For語言Change 狀態。
        // EN: This method resets cloud fetch state for language change.
        private static void ResetCloudFetchStateForLanguageChange()
        {
            AutoTranslatorSettings.CloudFetchGeneration++;
            AutoTranslatorSettings.IsFetchingCloud = false;
            AutoTranslatorSettings.HasFetchedCloudThisSession = false;
            AutoTranslatorSettings.CloudConnectionFailed = false;
            AutoTranslatorSettings.CloudFetchStartedUtcTicks = 0;
            AutoTranslatorSettings.CloudRegistry = new List<CloudModRecord>();
            AutoTranslatorSettings.SelectedCloudVersion.Clear();
            AutoTranslatorSettings.CloudSearchText = "";
            _cachedCloudLookup = null;
            _singleCorrectionCountCache.Clear();
            _singleCorrectionCountFetchInFlight.Clear();
            _lastCloudRegistryCount = -1;
            _lastCloudLangFolder = "";
        }

        // 這個方法負責啟動 雲端登錄取得 流程。
        // EN: This method starts cloud registry fetch.
        public static void StartCloudRegistryFetch(bool silent = false)
        {
            ATC_Dispatcher.EnsureAlive();
            if (AutoTranslatorSettings.IsFetchingCloud) return;

            AutoTranslatorSettings.IsFetchingCloud = true;
            AutoTranslatorSettings.HasFetchedCloudThisSession = true;
            AutoTranslatorSettings.CloudConnectionFailed = false;
            AutoTranslatorSettings.CloudFetchStartedUtcTicks = DateTime.UtcNow.Ticks;
            int fetchGeneration = ++AutoTranslatorSettings.CloudFetchGeneration;

            System.Threading.Tasks.Task.Run(async () =>
            {
                List<CloudModRecord> data = null;
                bool failed = false;
                string errorMessage = null;

                try
                {
                    data = await AutoTranslatorCloudClient.FetchRegistryAsync();
                    failed = data == null;
                }
                catch (Exception ex)
                {
                    failed = true;
                    errorMessage = ex.Message;
                    Verse.Log.Warning($"[ATC Cloud] Registry fetch task failed: {ex.Message}");
                }
                finally
                {
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        if (fetchGeneration != AutoTranslatorSettings.CloudFetchGeneration) return;

                        try
                        {
                            if (failed)
                            {
                                AutoTranslatorSettings.CloudRegistry = new List<CloudModRecord>();
                                AutoTranslatorSettings.CloudConnectionFailed = !silent;
                                AutoTranslatorSettings.HasFetchedCloudThisSession = !silent;
                                if (!silent)
                                {
                                    string message = string.IsNullOrEmpty(errorMessage)
                                        ? "ATC_Cloud_ConnectionFailed".Translate("")
                                        : "ATC_Cloud_ConnectionFailed".Translate(errorMessage);
                                    Verse.Messages.Message(message, RimWorld.MessageTypeDefOf.RejectInput, false);
                                }
                            }
                            else
                            {
                                AutoTranslatorSettings.CloudRegistry = data ?? new List<CloudModRecord>();
                                AutoTranslatorSettings.HasFetchedCloudThisSession = true;
                                AutoTranslatorSettings.CloudConnectionFailed = false;
                                if (silent && AutoTranslatorSettings.CloudRegistry.Count > 0)
                                {
                                    AutoTranslatorSettings.AddLog("☁️ " + "ATC_Log_CloudPrefetchSuccess".Translate(AutoTranslatorSettings.CloudRegistry.Count));
                                }
                            }

                            _cachedCloudLookup = null;
                            _singleCorrectionCountCache.Clear();
                            _singleCorrectionCountFetchInFlight.Clear();
                            _lastCloudRegistryCount = -1;
                            _lastCloudLangFolder = "";
                        }
                        finally
                        {
                            AutoTranslatorSettings.IsFetchingCloud = false;
                            AutoTranslatorSettings.CloudFetchStartedUtcTicks = 0;
                        }
                    });
                }
            });
        }

        internal static void ReplaceCloudRegistryAfterRecovery(
            List<CloudModRecord> records,
            string packageId,
            CloudModRecord selectedRecord)
        {
            if (records == null) return;
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                AutoTranslatorSettings.CloudRegistry = records;
                AutoTranslatorSettings.HasFetchedCloudThisSession = true;
                AutoTranslatorSettings.CloudConnectionFailed = false;
                AutoTranslatorSettings.CloudFetchGeneration++;
                if (!string.IsNullOrWhiteSpace(packageId) && selectedRecord != null)
                {
                    AutoTranslatorSettings.SelectedCloudVersion[packageId] = selectedRecord;
                }
                _cachedCloudLookup = null;
                _singleCorrectionCountCache.Clear();
                _singleCorrectionCountFetchInFlight.Clear();
                _lastCloudRegistryCount = -1;
                _lastCloudLangFolder = string.Empty;
            });
        }

        // 這個方法負責確保 雲端取得StartedForActive分頁 已準備完成。
        // EN: This method ensures cloud fetch started for active tab is ready.
        private static void EnsureCloudFetchStartedForActiveTab()
        {
            if (AutoTranslatorSettings.ActiveTab != AutoTranslatorSettings.CloudTabIndex) return;
            if (AutoTranslatorSettings.IsFetchingCloud || AutoTranslatorSettings.HasFetchedCloudThisSession) return;

            StartCloudRegistryFetch();
        }

        // 這個方法負責處理 Do設定視窗Contents 相關流程。
        // EN: This method handles do settings window contents.
        public override void DoSettingsWindowContents(Rect inRect)
        {
            Window_ModCatalogSync.OpenOnce();
            EnsureNetworkDispatchReady();
            AutoTranslatorAPI.MaintainModelFetchState();

            SyncLanguageWithGame();

            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {


                string[] tabLabels =
                {
                    "ATC_Tab_Cloud".Translate(),
                    "ATC_Tab_Main".Translate(),
                    "ATC_Tab_Editor".Translate(),
                    "ATC_Tab_Settings".Translate()
                };
                Rect tabRect = new Rect(inRect.x, inRect.y + 22f, inRect.width, 34f);
                const float tabGap = 2f;
                float tabWidth = (tabRect.width - tabGap * 3f) / 4f;
                for (int tabIndex = 0; tabIndex < tabLabels.Length; tabIndex++)
                {
                    Rect oneTab = new Rect(
                        tabRect.x + tabIndex * (tabWidth + tabGap),
                        tabRect.y,
                        tabWidth,
                        tabRect.height);
                    bool selected = AutoTranslatorSettings.ActiveTab == tabIndex;
                    if (WorkflowUiStyle.Button(
                            oneTab,
                            tabLabels[tabIndex],
                            selected ? WorkflowButtonStyle.ActiveTab : WorkflowButtonStyle.Tab,
                            true,
                            GameFont.Small))
                        AutoTranslatorSettings.ActiveTab = tabIndex;
                }
                if (_lastActiveTab != AutoTranslatorSettings.ActiveTab)
                {
                    if (AutoTranslatorSettings.ActiveTab != AutoTranslatorSettings.WorkbenchTabIndex)
                        EndWorkflowSelectionGesture();
                    _lastActiveTab = AutoTranslatorSettings.ActiveTab;
                    if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.CloudTabIndex)
                    {
                        EnsureCloudFetchStartedForActiveTab();
                    }
                }
                else
                {
                    EnsureCloudFetchStartedForActiveTab();
                }


                float contentTop = tabRect.yMax + 7f;
                Rect scrollRect = new Rect(
                    inRect.x,
                    contentTop,
                    inRect.width,
                    Mathf.Max(1f, inRect.yMax - contentTop));
                if (AutoTranslatorSettings.ActiveTab != AutoTranslatorSettings.CloudTabIndex)
                {
                    Widgets.DrawBoxSolid(scrollRect, WorkflowUiStyle.Panel);
                    WorkflowUiStyle.DrawBorder(scrollRect, new Color(0.28f, 0.31f, 0.33f));
                }
                bool fixedPrimaryLayout = SettingsLayoutPolicy.UseFixedPrimaryLayout(
                    AutoTranslatorSettings.ActiveTab,
                    scrollRect.width,
                    scrollRect.height);
                if (fixedPrimaryLayout)
                {
                    GUI.BeginGroup(scrollRect);
                    Listing_Standard workbenchListing = new Listing_Standard();
                    bool workbenchListingStarted = false;
                    try
                    {
                        Rect fixedViewRect = new Rect(0f, 0f, scrollRect.width, scrollRect.height);
                        workbenchListing.Begin(fixedViewRect);
                        workbenchListingStarted = true;
                        workbenchListing.Gap(5f);
                        if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.WorkbenchTabIndex)
                            DrawMainTab(workbenchListing, fixedViewRect);
                        else
                            DrawWorkflowEditorTab(workbenchListing, fixedViewRect);
                        AutoTranslatorSettings.lastSettingsViewHeight = workbenchListing.CurHeight + 10f;
                    }
                    finally
                    {
                        if (workbenchListingStarted) workbenchListing.End();
                        GUI.EndGroup();
                    }
                }
                else
                {
                    Rect viewRect = new Rect(0, 0, inRect.width - 20f, AutoTranslatorSettings.lastSettingsViewHeight);
                    Widgets.BeginScrollView(scrollRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);
                    bool listingStarted = false;
                    Listing_Standard l = new Listing_Standard();
                    try
                    {
                        Rect listRect = new Rect(0, 0, viewRect.width, 99999f);
                        l.Begin(listRect);
                        listingStarted = true;
                        l.Gap(5f);

                        if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.CloudTabIndex)
                            DrawCloudTab(l, viewRect);
                        else if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.WorkbenchTabIndex)
                            DrawMainTab(l, viewRect);
                        else if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.EditorTabIndex)
                            DrawWorkflowEditorTab(l, viewRect);
                        else if (AutoTranslatorSettings.ActiveTab == AutoTranslatorSettings.SettingsTabIndex)
                            DrawConfigTab(l, viewRect);
                        AutoTranslatorSettings.lastSettingsViewHeight = l.CurHeight + 50f;
                    }
                    finally
                    {
                        if (listingStarted) l.End();
                        Widgets.EndScrollView();
                    }
                }
            }
            finally
            {

                Patch_GUI_Label_GUIContent.BypassInterceptor = false;
            }
        }

        // 這個方法負責設定 tingsCategory 狀態。
        // EN: This method sets tings category.
        public override string SettingsCategory() => "ATC_ModTitle".Translate();
    }

}
