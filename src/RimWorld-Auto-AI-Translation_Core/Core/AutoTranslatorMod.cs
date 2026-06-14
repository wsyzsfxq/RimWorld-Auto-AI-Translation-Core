/*
    ___________________________________________________
   /  VVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVVV  \
  /   >>>>>  GUAI GUAI - GREEN COCONUT FLAVOR  <<<<<   \
 |    _____________________________________________     |
 |   |                                             |    |
 |   |    _     _           【 守 護 代 碼 】      |    |
 |   |   ( )___( )                                 |    |
 |   |   /  - -  \        Name: ANLN666            |    |
 |   |  (  > O <  )       Task: Keep Server Safe   |    |
 |   |___/   W   \___                              |    |
 |   /               \      🍀 🍀 🍀 🍀 🍀         |    |
 |  |  [ 🟢 乖 乖 ]   |    代碼乖乖 ‧ 不准報錯      |    |
 |  |   奶油椰子口味   |    機房重地 ‧ 閒人莫入      |    |
 |   \_______________/                             |    |
 |       |  |  |           EXP: Forever Green      |    |
 |      (   |   )                                  |    |
 |      (___|___)          MFG: 名揚電腦工作室      |    |
 |   |_____________________________________________|    |
 |                                                      |
 |    [ OK ]  NO ERROR    [ OK ]  NO LAG    [ 666 ]     |
  \   <<<<<  MAY THE SOURCE BE WITH YOU  >>>>>         /
   \__AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA____/
*/

using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    // ✨ 架構師升級：全語系制霸！加入法、德、西、義、波、葡、土
    public partial class AutoTranslatorMod : Mod
    {
        public static AutoTranslatorSettings Settings;

        // 🌟 新增一個靜態變數，用來記住遊戲的主執行緒
        public static System.Threading.SynchronizationContext MainThreadContext;

        // =========================================================
        // ✨ 架構師極速優化：全域快取引擎，徹底終結 FPS 暴跌！
        // =========================================================
        private static List<ModMetaData> _cachedValidMods = null;
        private static int _lastActiveModCount = -1;
        private static Dictionary<string, List<CloudModRecord>> _cachedCloudLookup = null;
        private static int _lastCloudRegistryCount = -1;
        private static string _lastCloudLangFolder = "";
        private static readonly Dictionary<string, float> _logHeightCache = new Dictionary<string, float>();
        private static int _lastActiveTab = -1;
        private static List<ModMetaData> _cachedCloudDisplayMods = null;
        private static string _cachedCloudSearchText = null;
        private static int _cachedCloudValidModCount = -1;

        // 取得有效模組清單 (附帶 O(1) 快取，避開每幀硬碟掃描)
        public static List<ModMetaData> GetValidModsCached()
        {
            int currentCount = Verse.ModLister.AllInstalledMods.Count(m => m.Active);
            if (_cachedValidMods == null || _lastActiveModCount != currentCount)
            {
                _cachedValidMods = Verse.ModLister.AllInstalledMods.Where(m =>
                    m.Active &&
                    m.PackageId.ToLower() != "auto.aitranslation.core" &&
                    m.PackageId.ToLower() != "aitranslation.pack" &&
                    !m.PackageId.ToLower().StartsWith("ludeon.rimworld") &&
                    !IsCodeOnlyMod(m) // 這裡的硬碟掃描現在只會執行一次！
                ).OrderBy(m => m.Name).ToList();
                _lastActiveModCount = currentCount;
            }
            return _cachedValidMods;
        }
        // =========================================================

        public AutoTranslatorMod(ModContentPack content) : base(content)
        {
            MainThreadContext = System.Threading.SynchronizationContext.Current;
            ATC_Dispatcher.EnsureAlive();

            Settings = GetSettings<AutoTranslatorSettings>();
            if (Settings.ApiConfigs == null || Settings.ApiConfigs.Count == 0)
            {
                Settings.ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
            }

            TryAutoSyncLanguageWithGame(resetCaches: false, log: false, writeSettings: true);
            AutoTranslatorScanner.QueueExternalPatchCoveredOverrideCleanup();
        }
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
                // ✨ 架構師擴充：將新語言連結到 XML 翻譯系統
                case TargetLanguage.French: return "ATC_Lang_French".Translate();
                case TargetLanguage.German: return "ATC_Lang_German".Translate();
                case TargetLanguage.Spanish: return "ATC_Lang_Spanish".Translate();
                case TargetLanguage.Italian: return "ATC_Lang_Italian".Translate();
                case TargetLanguage.Polish: return "ATC_Lang_Polish".Translate();
                case TargetLanguage.Portuguese: return "ATC_Lang_Portuguese".Translate();
                case TargetLanguage.Turkish: return "ATC_Lang_Turkish".Translate();
                default: return lang.ToString();
            }
        }

        // ==========================================
        // 🌟 咪咪的自動語系同步雷達
        // ==========================================
        private void SyncLanguageWithGame()
        {
            TryAutoSyncLanguageWithGame(resetCaches: true, log: true, writeSettings: true);
        }

        private void SetTargetLanguage(TargetLanguage targetLang)
        {
            ApplyTargetLanguageChange(targetLang, true);
        }

        private void ApplyTargetLanguageChange(TargetLanguage targetLang, bool manualSelection)
        {
            bool changed = Settings.TargetLang != targetLang;

            Settings.TargetLang = targetLang;
            Settings.CloudTargetLang = targetLang;
            if (manualSelection) Settings.HasManualTargetLanguage = true;

            if (changed)
            {
                ResetLanguageDependentCaches();
            }

            WriteSettings();
        }

        private static void ResetLanguageDependentCaches()
        {
            ResetCloudFetchStateForLanguageChange();
            UIInterceptor.ReloadForLanguageChange();
            ModNameTranslationCache.Clear();
            ModUpdateDetector.ClearStatusCache();
            TranslationWorkbenchTab.RequestRefresh();
            AutoTranslatorScanner.RequestMemoryDrop();
        }

        public static bool TryAutoSyncLanguageWithGame(bool resetCaches, bool log, bool writeSettings)
        {
            if (Settings == null || Settings.HasManualTargetLanguage) return false;
            if (!TryGetTargetLanguageFromActiveGameLanguage(out TargetLanguage detectedLang, out string activeFolder)) return false;
            if (Settings.TargetLang == detectedLang && Settings.CloudTargetLang == detectedLang) return false;

            Settings.TargetLang = detectedLang;
            Settings.CloudTargetLang = detectedLang;

            if (resetCaches)
            {
                ResetLanguageDependentCaches();
            }

            if (writeSettings)
            {
                try
                {
                    LoadedModManager.GetMod<AutoTranslatorMod>()?.WriteSettings();
                }
                catch { }
            }

            if (log)
            {
                Verse.Log.Message("[AutoTranslationCore] 🔄 " + "ATC_Log_AutoSyncLanguage".Translate(activeFolder));
            }

            return true;
        }

        private static bool TryGetTargetLanguageFromActiveGameLanguage(out TargetLanguage targetLang, out string activeFolder)
        {
            targetLang = TargetLanguage.English;
            activeFolder = Verse.LanguageDatabase.activeLanguage?.folderName;
            if (string.IsNullOrEmpty(activeFolder)) return false;

            switch (activeFolder)
            {
                case "ChineseTraditional": targetLang = TargetLanguage.Traditional; return true;
                case "ChineseSimplified": targetLang = TargetLanguage.Simplified; return true;
                case "Japanese": targetLang = TargetLanguage.Japanese; return true;
                case "Korean": targetLang = TargetLanguage.Korean; return true;
                case "Russian": targetLang = TargetLanguage.Russian; return true;
                case "Ukrainian": targetLang = TargetLanguage.Ukrainian; return true;
                case "French": targetLang = TargetLanguage.French; return true;
                case "German": targetLang = TargetLanguage.German; return true;
                case "Spanish": targetLang = TargetLanguage.Spanish; return true;
                case "Italian": targetLang = TargetLanguage.Italian; return true;
                case "Polish": targetLang = TargetLanguage.Polish; return true;
                case "PortugueseBrazilian": targetLang = TargetLanguage.Portuguese; return true;
                case "Turkish": targetLang = TargetLanguage.Turkish; return true;
                case "English": targetLang = TargetLanguage.English; return true;
                default: return false;
            }
        }

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
            _lastCloudRegistryCount = -1;
            _lastCloudLangFolder = "";
        }

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

        private static void EnsureCloudFetchStartedForActiveTab()
        {
            if (AutoTranslatorSettings.ActiveTab != 2) return;
            if (AutoTranslatorSettings.IsFetchingCloud || AutoTranslatorSettings.HasFetchedCloudThisSession) return;

            StartCloudRegistryFetch();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 🌟 咪咪的自動語系同步雷達：一打開設定畫面，就先檢查語言有沒有被切換！
            SyncLanguageWithGame();
            // 🛡️ 免死金牌：設定頁面絕不被攔截器影響
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                if (AutoTranslatorSettings.ShowFinishPopup)
                {
                    AutoTranslatorSettings.ShowFinishPopup = false;
                    Find.WindowStack.Add(new Dialog_MessageBox("ATC_FinishMessage_Text".Translate(), "ATC_FinishMessage_OK".Translate(), null, null, null, "ATC_FinishMessage_Title".Translate()));
                }

                // 🌟 V3.0 介面革命：重新排序，將編輯器升級為獨立主分頁！
                List<TabRecord> tabs = new List<TabRecord>
                {
                    new TabRecord("ATC_Tab_Main".Translate(), () => AutoTranslatorSettings.ActiveTab = 0, AutoTranslatorSettings.ActiveTab == 0),
                    new TabRecord("ATC_Tab_Editor".Translate(), () => AutoTranslatorSettings.ActiveTab = 1, AutoTranslatorSettings.ActiveTab == 1),
                    new TabRecord("ATC_Tab_Cloud".Translate(), () => AutoTranslatorSettings.ActiveTab = 2, AutoTranslatorSettings.ActiveTab == 2),
                    new TabRecord("ATC_Tab_Settings".Translate(), () => AutoTranslatorSettings.ActiveTab = 3, AutoTranslatorSettings.ActiveTab == 3)
                };
                Rect tabRect = new Rect(inRect.x, inRect.y + 25f, inRect.width, 30f);
                TabDrawer.DrawTabs(tabRect, tabs);
                if (_lastActiveTab != AutoTranslatorSettings.ActiveTab)
                {
                    _lastActiveTab = AutoTranslatorSettings.ActiveTab;
                    if (AutoTranslatorSettings.ActiveTab == 2)
                    {
                        EnsureCloudFetchStartedForActiveTab();
                    }
                }
                else
                {
                    EnsureCloudFetchStartedForActiveTab();
                }

                // 設定捲動視窗範圍 (向下推移以避開 Tabs)
                Rect viewRect = new Rect(0, 0, inRect.width - 20f, AutoTranslatorSettings.lastSettingsViewHeight);
                Rect scrollRect = new Rect(0, 65f, inRect.width, inRect.height - 65f);

                Widgets.BeginScrollView(scrollRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);
                bool listingStarted = false;
                Listing_Standard l = new Listing_Standard();
                try
                {
                    Rect listRect = new Rect(0, 0, viewRect.width, 99999f);
                    l.Begin(listRect);
                    listingStarted = true;
                    l.Gap(5f);

                    // 根據當前分頁呼叫對應的畫面
                    if (AutoTranslatorSettings.ActiveTab == 0)
                    {
                        DrawMainTab(l, viewRect);
                    }
                    else if (AutoTranslatorSettings.ActiveTab == 1)
                    {
                        // ✨ 呼叫我們全新打造的全域編輯器分頁！
                        TranslationWorkbenchTab.DrawEditorTab(l, viewRect);
                    }
                    else if (AutoTranslatorSettings.ActiveTab == 2)
                    {
                        DrawCloudTab(l, viewRect);
                    }
                    else if (AutoTranslatorSettings.ActiveTab == 3)
                    {
                        DrawConfigTab(l, viewRect);
                    }
                    AutoTranslatorSettings.lastSettingsViewHeight = l.CurHeight + 50f;
                }
                finally
                {
                    if (listingStarted) l.End();
                    Widgets.EndScrollView();
                }
            }
            finally
            {
                // 🛡️ 收回免死金牌
                Patch_GUI_Label_GUIContent.BypassInterceptor = false;
            }
        }

        public override string SettingsCategory() => "ATC_ModTitle".Translate();
    }

}
