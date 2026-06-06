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
    public enum TargetLanguage { Traditional, Simplified, Japanese, Korean, Russian, Ukrainian, English, French, German, Spanish, Italian, Polish, Portuguese, Turkish }
    public enum TranslatorProvider { Google, OpenAI, DeepSeek, Grok, GLM, Alibaba, OpenRouter, DeepL, Custom_OpenAI }
    public class ApiKeyConfig : IExposable
    {
        public TranslatorProvider Provider = TranslatorProvider.Google;
        public string Key = "";
        public string CustomBaseUrl = "";
        public string SelectedModel = "";

        public List<string> FetchedModels = new List<string>();

        [NonSerialized] public bool IsFetching = false;
        [NonSerialized] public string lastFetchedKey = "";

        // 🌟 新增這個變數：用來控制 UI 顯示「測試中⏳」
        [NonSerialized] public bool IsTesting = false;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Provider, "Provider", TranslatorProvider.Google);
            Scribe_Values.Look(ref Key, "Key", "");
            Scribe_Values.Look(ref CustomBaseUrl, "CustomBaseUrl", "");
            Scribe_Values.Look(ref SelectedModel, "SelectedModel", "");
            Scribe_Collections.Look(ref FetchedModels, "FetchedModels", LookMode.Value);

            if (FetchedModels == null) FetchedModels = new List<string>();
        }
    }

    public class AutoTranslatorSettings : ModSettings
    {
        public TargetLanguage TargetLang = TargetLanguage.Traditional;
        public bool OnlyScanActiveMods = true;
        public int MaxThreads = 3;
        public List<ApiKeyConfig> ApiConfigs = new List<ApiKeyConfig>();

        public float CurrentProgress = 0f;
        public string CurrentTaskName = "";
        public float SubProgress = 0f;
        public string SubTaskName = "";
        public static bool ShouldAutoScroll = true;
        public static bool IsSkipCurrentRequested = false;

        public static float lastSettingsViewHeight = 1000f;
        public static bool ShowFinishPopup = false;
        public static Vector2 mainScrollPos = Vector2.zero;

        public static Vector2 logScrollPos = Vector2.zero;
        public static List<string> RuntimeLogs = new List<string>();

        public static Vector2 errorScrollPos = Vector2.zero;
        public static List<string> ErrorLogs = new List<string>();

        public static readonly object logLock = new object();

        public static bool IsCancellationRequested = false;
        public static bool IsRunning = false;

        public bool EnableUIInterceptor = false;

        public bool ShowOriginalUI = false;

        public long TotalCharCount = 0;
        [NonSerialized] public long SessionCharCount = 0;

        // 分頁控制 (不需存檔)
        [NonSerialized] public static int ActiveTab = 0;
        // ===== V3.0 雲端狀態暫存 (不需存檔) =====
        [NonSerialized] public static List<CloudModRecord> CloudRegistry = new List<CloudModRecord>();
        [NonSerialized] public static bool IsFetchingCloud = false;
        [NonSerialized] public static bool HasFetchedCloudThisSession = false;
        [NonSerialized] public static string CloudUploadTarget = ""; // 記錄目前正在上傳哪一個 PackageId
        [NonSerialized] public static string CloudSearchText = "";   // ✨ 新增：雲端介面的搜尋框文字
        // ✨ 新增：明確記錄連線是否失敗，防止 UI 說謊！
        [NonSerialized] public static bool CloudConnectionFailed = false;
        public static Vector2 cloudScrollPos = Vector2.zero;
        // ===== V3.1 雲端使用者設定 =====
        public string CloudNickname = "野生大佬";
        public string CloudAdminToken = "";
        public string CloudUploadType = "AI_Auto"; // ✨ 新增：預設是 AI 機翻
        public TargetLanguage CloudTargetLang = TargetLanguage.Traditional; // ✨ 雲端專屬目標語言！
        // ✨ V3.0 新增：雲端多版本選取暫存 (不需要存檔，開機自動重整)
        [NonSerialized] public static Dictionary<string, CloudModRecord> SelectedCloudVersion = new Dictionary<string, CloudModRecord>(StringComparer.OrdinalIgnoreCase);
        // 新功能：檢測到更新是否直接全自動背景啟動翻譯
        public bool AutoTranslateOnUpdate = false;
        public int TimeoutSeconds = 60;
        // ✨ 增量翻譯開關與時間戳記憶體
        public bool AutoClearOldOnUpdate = true; // 預設開啟智能增量
        public Dictionary<string, long> ModLastVerifiedTimes = new Dictionary<string, long>();
        // ✨ 架構師新增：宣告一個公開靜態變數來記錄過濾數量
        public static int FilteredModsCount = 0;
        // ===== P3 新增：EULA 同意狀態 =====
        public bool HasAcceptedExportEula = false;
        public string EulaAcceptedTimestamp = "";   // ISO 8601 格式
        public string EulaAcceptedVersion = "";      // 同意的 EULA 版本
        public int EulaAcceptCount = 0;              // 累計同意次數

        // ===== P3 第二階段新增：導出冷卻與每日上限 =====
        public List<string> ExportHistory = new List<string>();  // ISO 8601 時間字串清單
        public string TodayExportDate = "";                       // 今日日期 yyyy-MM-dd
        public int TodayExportCount = 0;                          // 今日導出次數

        /// <summary>
        /// 判斷 EULA 同意是否仍有效（30 天內 且 版本一致）
        /// </summary>
        public bool IsEulaStillValid()
        {
            if (!HasAcceptedExportEula) return false;
            if (string.IsNullOrEmpty(EulaAcceptedTimestamp)) return false;
            if (EulaAcceptedVersion != ExportEulaVersion.CurrentVersion) return false;

            if (DateTime.TryParse(EulaAcceptedTimestamp, out DateTime accepted))
            {
                return (DateTime.Now - accepted).TotalDays < 30;
            }
            return false;
        }

        /// <summary>
        /// 取得 EULA 同意剩餘天數（負數表示已過期）
        /// </summary>
        public int GetEulaRemainingDays()
        {
            if (!DateTime.TryParse(EulaAcceptedTimestamp, out DateTime accepted)) return 0;
            return 30 - (int)(DateTime.Now - accepted).TotalDays;
        }

        public static void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            lock (logLock)
            {
                RuntimeLogs.Add(line);
                if (RuntimeLogs.Count > 500) RuntimeLogs.RemoveAt(0);
                WriteLogToFile(line);
            }
        }

        public static void AddErrorLog(string msg)
        {
            lock (logLock)
            {
                if (ErrorLogs.Any(x => x.Contains(msg))) return;
                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                ErrorLogs.Add(line);
                if (ErrorLogs.Count > 100) ErrorLogs.RemoveAt(0);
                errorScrollPos.y = 99999f;
                WriteLogToFile("[ERROR] " + line);
            }

            // 🌟 UX 革命：呼叫跨執行緒神器，保證 100% 在左上角彈出！
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                Verse.Messages.Message(msg, RimWorld.MessageTypeDefOf.RejectInput, false);
            });
        }
        private static void WriteLogToFile(string line)
        {
            try
            {
                string path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "AutoTranslation_Log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + "\n");
            }
            catch { }
        }

        public static void ClearLog()
        {
            lock (logLock)
            {
                RuntimeLogs.Clear();
                ErrorLogs.Clear();
                logScrollPos = Vector2.zero;
                errorScrollPos = Vector2.zero;
            }
            try
            {
                string path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "AutoTranslation_Log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, $"=== Auto Translation Core V4.9 Ultimate [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n\n");
            }
            catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TargetLang, "TargetLang", TargetLanguage.Traditional);
            Scribe_Values.Look(ref OnlyScanActiveMods, "OnlyScanActiveMods", true);
            Scribe_Values.Look(ref EnableUIInterceptor, "EnableUIInterceptor", false);
            Scribe_Values.Look(ref MaxThreads, "MaxThreads", 3);
            Scribe_Values.Look(ref ShowOriginalUI, "ShowOriginalUI", false);
            Scribe_Collections.Look(ref ApiConfigs, "ApiConfigs", LookMode.Deep);
            Scribe_Values.Look(ref TotalCharCount, "TotalCharCount", 0L);
            // ✨ 存檔掛載
            Scribe_Values.Look(ref AutoClearOldOnUpdate, "AutoClearOldOnUpdate", true);
            Scribe_Collections.Look(ref ModLastVerifiedTimes, "ModLastVerifiedTimes", LookMode.Value, LookMode.Value);
            if (ModLastVerifiedTimes == null) ModLastVerifiedTimes = new Dictionary<string, long>();
            // 確保超時秒數能被存檔，預設值為 60 秒
            Scribe_Values.Look(ref TimeoutSeconds, "TimeoutSeconds", 60);
            // ===== V3.1 雲端使用者設定 =====
            Scribe_Values.Look(ref CloudNickname, "CloudNickname", "野生大佬");
            Scribe_Values.Look(ref CloudAdminToken, "CloudAdminToken", "");
            Scribe_Values.Look(ref CloudUploadType, "CloudUploadType", "AI_Auto"); // ✨ 新增這行存檔
            Scribe_Values.Look(ref CloudTargetLang, "CloudTargetLang", TargetLanguage.Traditional); // ✨ 存檔掛載           
            // P3 新增：EULA 同意狀態序列化
            Scribe_Values.Look(ref HasAcceptedExportEula, "HasAcceptedExportEula", false);
            Scribe_Values.Look(ref EulaAcceptedTimestamp, "EulaAcceptedTimestamp", "");
            Scribe_Values.Look(ref EulaAcceptedVersion, "EulaAcceptedVersion", "");
            Scribe_Values.Look(ref EulaAcceptCount, "EulaAcceptCount", 0);
            Scribe_Values.Look(ref AutoTranslateOnUpdate, "AutoTranslateOnUpdate", false);

            // P3 第二階段：冷卻欄位序列化
            Scribe_Collections.Look(ref ExportHistory, "ExportHistory", LookMode.Value);
            Scribe_Values.Look(ref TodayExportDate, "TodayExportDate", "");
            Scribe_Values.Look(ref TodayExportCount, "TodayExportCount", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (ApiConfigs == null || ApiConfigs.Count == 0)
                {
                    ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
                }
                // 防 null：舊存檔可能沒有 ExportHistory 欄位
                if (ExportHistory == null) ExportHistory = new List<string>();
            }
        }
    }

    public class AutoTranslatorMod : Mod
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
            // 🌟 將代理人注入 Unity 遊戲核心，且切換場景時絕對不銷毀
            GameObject go = new GameObject("ATC_Dispatcher_Engine");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<ATC_Dispatcher>();

            Settings = GetSettings<AutoTranslatorSettings>();
            if (Settings.ApiConfigs == null || Settings.ApiConfigs.Count == 0)
            {
                Settings.ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
            }
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
            if (Verse.LanguageDatabase.activeLanguage == null) return;

            string activeFolder = Verse.LanguageDatabase.activeLanguage.folderName;
            TargetLanguage detectedLang = Settings.TargetLang; // 預設保持原樣

            // 根據遊戲當前的資料夾名稱，反推回我們的枚舉設定
            switch (activeFolder)
            {
                case "ChineseTraditional": detectedLang = TargetLanguage.Traditional; break;
                case "ChineseSimplified": detectedLang = TargetLanguage.Simplified; break;
                case "Japanese": detectedLang = TargetLanguage.Japanese; break;
                case "Korean": detectedLang = TargetLanguage.Korean; break;
                case "Russian": detectedLang = TargetLanguage.Russian; break;
                case "Ukrainian": detectedLang = TargetLanguage.Ukrainian; break;
                case "French": detectedLang = TargetLanguage.French; break;
                case "German": detectedLang = TargetLanguage.German; break;
                case "Spanish": detectedLang = TargetLanguage.Spanish; break;
                case "Italian": detectedLang = TargetLanguage.Italian; break;
                case "Polish": detectedLang = TargetLanguage.Polish; break;
                case "PortugueseBrazilian": detectedLang = TargetLanguage.Portuguese; break;
                case "Turkish": detectedLang = TargetLanguage.Turkish; break;
                case "English": detectedLang = TargetLanguage.English; break;
            }

            // 如果發現遊戲語言跟模組設定的語言不一樣，立刻自動同步！
            if (Settings.TargetLang != detectedLang)
            {
                Settings.TargetLang = detectedLang;

                // 🌟 最重要的一步：強迫雲端清單失效！這樣切換語言後，UI 就會要求重新連線抓取新語言的清單！
                AutoTranslatorSettings.HasFetchedCloudThisSession = false;
                AutoTranslatorSettings.CloudRegistry.Clear();

                // 完美本地化日誌輸出！
                Verse.Log.Message("[AutoTranslationCore] 🔄 " + "ATC_Log_AutoSyncLanguage".Translate(activeFolder));
            }
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

                // 設定捲動視窗範圍 (向下推移以避開 Tabs)
                Rect viewRect = new Rect(0, 0, inRect.width - 20f, AutoTranslatorSettings.lastSettingsViewHeight);
                Rect scrollRect = new Rect(0, 65f, inRect.width, inRect.height - 65f);

                Widgets.BeginScrollView(scrollRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);

                Listing_Standard l = new Listing_Standard();
                Rect listRect = new Rect(0, 0, viewRect.width, 99999f);
                l.Begin(listRect);
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
                l.End();
                Widgets.EndScrollView();
            }
            finally
            {
                // 🛡️ 收回免死金牌
                Patch_GUI_Label_GUIContent.BypassInterceptor = false;
            }
        }

        // ==========================================
        // 🖥️ 第一分頁：主控制台 (操作與日誌)
        // ==========================================
        private void DrawMainTab(Listing_Standard l, Rect viewRect)
        {
            // 1. 頂部四個功能按鈕
            Rect topBarRect = l.GetRect(30f);
            float btnWidth = (topBarRect.width - 30f) / 4f;
            float gap = 10f;
            if (Widgets.ButtonText(new Rect(topBarRect.x, topBarRect.y, btnWidth, topBarRect.height), "📜 " + "ATC_UpdateLog_Btn".Translate()))
            {
                Find.WindowStack.Add(new UpdateLogWindow());
            }
            if (Widgets.ButtonText(new Rect(topBarRect.x + (btnWidth + gap) * 1, topBarRect.y, btnWidth, topBarRect.height), "🗑️ " + "ATC_DeleteModTrans_Btn".Translate()))
            {
                Find.WindowStack.Add(new DeleteTranslationWindow());
            }
            if (Widgets.ButtonText(new Rect(topBarRect.x + (btnWidth + gap) * 2, topBarRect.y, btnWidth, topBarRect.height), "📖 " + "ATC_Tutorial_Btn".Translate()))
            {
                Find.WindowStack.Add(new TutorialWindow());
            }
            GUI.color = new Color(1f, 0.7f, 0.3f);
            if (Widgets.ButtonText(new Rect(topBarRect.x + (btnWidth + gap) * 3, topBarRect.y, btnWidth, topBarRect.height), "ATC_ExportTrans_Btn".Translate()))
            {
                ExportFlowController.StartExportFlow();
            }
            GUI.color = Color.white;
            l.Gap(15f);

            // 2. 翻譯行動按鈕區塊
            var updatedMods = ModUpdateDetector.GetUpdatedOrNewModsCached(); // 獲取快取更新清單

            Rect actionRow = l.GetRect(40f);
            Rect singleModRect = new Rect(actionRow.x, actionRow.y, actionRow.width * 0.3f, actionRow.height);
            Rect skipRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect stopRect = new Rect(actionRow.x + actionRow.width * 0.67f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect startRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.68f, actionRow.height);

            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;

            string multiBtnText = updatedMods.Count > 0
                           ? "ATC_SmartUpdateBtn".Translate(updatedMods.Count).ToString()
                           : "ATC_TranslateMultiMod".Translate().ToString();

            if (Widgets.ButtonText(singleModRect, multiBtnText))
            {
                if (!HasValidConfig()) Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                else if (!AutoTranslatorSettings.IsRunning) Find.WindowStack.Add(new ModSelectWindow(updatedMods));
            }

            if (AutoTranslatorSettings.IsRunning)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                if (Widgets.ButtonText(skipRect, "ATC_SkipCurrentMod".Translate()))
                {
                    AutoTranslatorSettings.IsSkipCurrentRequested = true;
                    AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkipRequested".Translate()); // ✨ 這裡拔除了寫死的中文！
                }
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (Widgets.ButtonText(stopRect, "🛑 " + "ATC_EmergencyStop".Translate()))
                {
                    AutoTranslatorSettings.IsCancellationRequested = true;
                    AutoTranslatorSettings.AddLog("⚠️ " + "ATC_CancelRequested".Translate());
                }
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.6f, 0.9f, 0.6f);
                if (Widgets.ButtonText(startRect, "🚀 " + "ATC_StartFullScan".Translate()))
                {
                    if (!HasValidConfig()) Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    else
                    {
                        AutoTranslatorSettings.ClearLog();
                        AutoTranslatorSettings.IsCancellationRequested = false;
                        AutoTranslatorSettings.IsSkipCurrentRequested = false;
                        AutoTranslatorScanner.StartFullScan();
                    }
                }
                GUI.color = Color.white;
            }
            l.Gap(15f);

            // 3. 一鍵熱重載
            Rect reloadRow = l.GetRect(35f);
            GUI.color = new Color(0.4f, 1f, 0.8f);
            if (Widgets.ButtonText(reloadRow, "🔄 " + "ATC_Button_HotReload".Translate()))
            {
                UIInterceptor.RequestHotReload();
            }
            GUI.color = Color.white;
            l.Gap(15f);

            // 4. 進度條區塊
            string displayTask = string.IsNullOrEmpty(Settings.CurrentTaskName) ? "ATC_Idle".Translate().ToString() : Settings.CurrentTaskName;
            l.Label("ATC_CurrentTask".Translate() + $": {displayTask}");
            Rect barRect = l.GetRect(25f);
            Widgets.FillableBar(barRect, Settings.CurrentProgress);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{(Settings.CurrentProgress * 100):F0}%");
            Text.Anchor = oldAnchor;

            Rect subBarRect = l.GetRect(15f);
            string displaySubTask = string.IsNullOrEmpty(Settings.SubTaskName) ? "" : Settings.SubTaskName;
            Widgets.FillableBar(subBarRect, Settings.SubProgress);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(subBarRect.x, subBarRect.y, subBarRect.width, subBarRect.height), $" {displaySubTask} ({(Settings.SubProgress * 100):F0}%)");
            Text.Font = GameFont.Small;
            l.Gap(15f);

            // 5. 統計數據儀表板
            Rect statsRect = l.GetRect(50f);
            Rect statsLeft = new Rect(statsRect.x, statsRect.y, statsRect.width * 0.48f, statsRect.height);
            Rect statsRight = new Rect(statsRect.xMax - statsRect.width * 0.48f, statsRect.y, statsRect.width * 0.48f, statsRect.height);
            string sessionText = "ATC_Stats_Session".Translate(Settings.SessionCharCount);
            string totalText = "ATC_Stats_Total".Translate(Settings.TotalCharCount);
            Widgets.Label(statsLeft, $"📊 {sessionText}\n📈 {totalText}");

            if (Settings.EnableUIInterceptor)
            {
                string uiQueue = UIInterceptor.GetQueueCount().ToString();
                string uiCache = UIInterceptor.Cache.Count.ToString();
                Widgets.Label(statsRight, $"🛡️ " + "ATC_Stats_UIQueue".Translate(uiQueue) + $"\n📦 " + "ATC_Stats_UICache".Translate(uiCache));
            }
            l.Gap(15f);

            // 5.5 動態過濾提示
            // 改從 AutoTranslatorSettings 讀取變數
            if (AutoTranslatorSettings.FilteredModsCount > 0)
            {
                GUI.color = Color.gray;
                string text = "ATC_FilteredModsCount".Translate(AutoTranslatorSettings.FilteredModsCount);

                Rect filterRect = l.GetRect(25f);
                Widgets.Label(filterRect, text);

                GUI.color = Color.white;
                l.Gap(10f);
            }
            // 6. 雙日誌紀錄面板
            Rect headerRect = l.GetRect(24f);
            float leftWidth = headerRect.width * 0.6f;
            float rightWidth = headerRect.width * 0.4f - 10f;
            Widgets.Label(new Rect(headerRect.x, headerRect.y, leftWidth, headerRect.height), "ATC_LogPanelTitle".Translate());
            Widgets.Label(new Rect(headerRect.x + leftWidth + 10f, headerRect.y, rightWidth, headerRect.height), "ATC_ErrorLogTitle".Translate());

            Rect logArea = l.GetRect(350f);
            Rect leftRect = new Rect(logArea.x, logArea.y, leftWidth, logArea.height);
            Rect rightRect = new Rect(logArea.x + leftWidth + 10f, logArea.y, rightWidth, logArea.height);

            Widgets.DrawBoxSolid(leftRect, new Color(0.05f, 0.05f, 0.05f, 1f));
            Widgets.DrawBox(leftRect, 1);
            DrawLogView(leftRect, AutoTranslatorSettings.RuntimeLogs, ref AutoTranslatorSettings.logScrollPos, false);

            Widgets.DrawBoxSolid(rightRect, new Color(0.1f, 0.0f, 0.0f, 1f));
            Widgets.DrawBox(rightRect, 1);
            DrawLogView(rightRect, AutoTranslatorSettings.ErrorLogs, ref AutoTranslatorSettings.errorScrollPos, true);

            // 7. Rickroll 彩蛋
            Rect eggRect = new Rect(viewRect.width - 150f, l.CurHeight + 5f, 140f, 20f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(eggRect, "ATC_Rickroll".Translate());
            if (Widgets.ButtonInvisible(eggRect))
            {
                if (Settings.TargetLang == TargetLanguage.Simplified) Application.OpenURL("https://www.bilibili.com/video/BV1GJ411x7h7");
                else Application.OpenURL("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            }
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        // ==========================================
        // ⚙️ 第二分頁：詳細設定 (系統與 API)
        // ==========================================
        private void DrawConfigTab(Listing_Standard l, Rect viewRect)
        {
            // 1. 系統自動化與攔截器開關
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_AutoClearOldOnUpdate".Translate(), ref Settings.AutoClearOldOnUpdate);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_AutoTranslateOnUpdate".Translate(), ref Settings.AutoTranslateOnUpdate);

            l.Gap(5f);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUIInterceptor".Translate(), ref Settings.EnableUIInterceptor);
            if (!Settings.EnableUIInterceptor && !AutoTranslatorSettings.IsRunning) GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_ShowOriginalUI".Translate(), ref Settings.ShowOriginalUI);
            GUI.color = Color.white;
            l.Gap(15f);

            // 2. 目標語言與掃描模式
            Rect row1 = l.GetRect(30f);
            Rect langRect = new Rect(row1.x, row1.y, row1.width * 0.4f, row1.height);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            if (Mouse.IsOver(langRect)) TooltipHandler.TipRegion(langRect, "ATC_Tooltip_TargetLang".Translate());
            if (Widgets.ButtonText(langRect, "ATC_TargetLang".Translate() + ": " + GetLangLabel(Settings.TargetLang)))
            {
                if (!AutoTranslatorSettings.IsRunning)
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                        options.Add(new FloatMenuOption(GetLangLabel(lang), () => Settings.TargetLang = lang));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            Rect activeScanRect = new Rect(row1.x + row1.width * 0.45f, row1.y, row1.width * 0.55f, row1.height);
            Widgets.CheckboxLabeled(activeScanRect, "ATC_OnlyScanActive".Translate(), ref Settings.OnlyScanActiveMods);
            GUI.color = Color.white;
            l.Gap(15f);

            // 3. 最大線程數滑桿
            Rect threadRow = l.GetRect(30f);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            Settings.MaxThreads = (int)Widgets.HorizontalSlider(
                threadRow, Settings.MaxThreads, 1f, 30f, false,
                $"{"ATC_MaxThreads".Translate()}: {Settings.MaxThreads}  ({"ATC_MaxThreadsTip".Translate()})", "1", "30"
            );
            GUI.color = Color.white;
            l.Gap(15f);

            // 🌟 3.5 API 請求超時秒數滑桿 (與線程數風格保持統一)
            Rect timeoutRow = l.GetRect(30f);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            // 滑鼠懸停的 Tooltip 提示
            if (Mouse.IsOver(timeoutRow)) TooltipHandler.TipRegion(timeoutRow, "ATC_Setting_Timeout_Tooltip".Translate());

            Settings.TimeoutSeconds = (int)Widgets.HorizontalSlider(
                timeoutRow,
                Settings.TimeoutSeconds,
                15f,
                600f,
                false,
                "ATC_Setting_Timeout".Translate(Settings.TimeoutSeconds.ToString()),
                "15",
                "600"
            );
            GUI.color = Color.white;
            l.Gap(15f);

            // 4. API 金鑰設定區塊 (支援多組並發輪詢)
            Text.Font = GameFont.Small;
            Widgets.Label(l.GetRect(24f), "🔧 " + "ATC_ApiConfigTitle".Translate());
            l.Gap(2f);
            Widgets.DrawLineHorizontal(0, l.CurHeight, viewRect.width);
            l.Gap(5f);

            for (int i = 0; i < Settings.ApiConfigs.Count; i++)
            {
                var config = Settings.ApiConfigs[i];
                if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;

                Rect rowA = l.GetRect(30f);
                Rect providerRect = new Rect(rowA.x, rowA.y, rowA.width * 0.3f, rowA.height - 2f);
                if (Widgets.ButtonText(providerRect, "ATC_Provider".Translate() + ": " + config.Provider))
                {
                    if (!AutoTranslatorSettings.IsRunning)
                    {
                        List<FloatMenuOption> opts = new List<FloatMenuOption>();
                        foreach (TranslatorProvider p in Enum.GetValues(typeof(TranslatorProvider)))
                        {
                            opts.Add(new FloatMenuOption(p.ToString(), () => { config.Provider = p; config.FetchedModels.Clear(); config.SelectedModel = ""; config.lastFetchedKey = ""; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                }

                Rect urlRect = new Rect(rowA.x + rowA.width * 0.32f, rowA.y, rowA.width * 0.58f, rowA.height - 2f);
                if (config.Provider != TranslatorProvider.Google)
                {
                    config.CustomBaseUrl = Widgets.TextField(urlRect, config.CustomBaseUrl);
                    if (string.IsNullOrEmpty(config.CustomBaseUrl)) Widgets.Label(urlRect, "  " + "ATC_CustomUrlOptional".Translate());
                }

                Rect delRect = new Rect(rowA.x + rowA.width * 0.92f, rowA.y, rowA.width * 0.08f, rowA.height - 2f);
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (Settings.ApiConfigs.Count > 1 && Widgets.ButtonText(delRect, "ATC_Delete".Translate()))
                {
                    Settings.ApiConfigs.RemoveAt(i);
                    GUI.color = Color.white;
                    break;
                }

                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                Rect rowB = l.GetRect(30f);
                Rect keyRect = new Rect(rowB.x, rowB.y, rowB.width * 0.45f, rowB.height - 2f);

                config.Key = Widgets.TextField(keyRect, config.Key);
                if (string.IsNullOrEmpty(config.Key)) Widgets.Label(keyRect, "  " + "ATC_PasteKey".Translate());

                // 自動獲取模型防呆
                if (config.Key != config.lastFetchedKey && config.Key.Length > 10 && !config.IsFetching && !AutoTranslatorSettings.IsRunning)
                {
                    config.lastFetchedKey = config.Key;
                    AutoTranslatorAPI.AutoFetchForConfig(config);
                }

                Rect modelInputRect = new Rect(rowB.x + rowB.width * 0.47f, rowB.y, rowB.width * 0.45f, rowB.height - 2f);
                Rect modelBtnRect = new Rect(modelInputRect.xMax + 5f, rowB.y, rowB.width * 0.08f - 5f, rowB.height - 2f);

                if (config.IsFetching)
                {
                    GUI.color = Color.yellow;
                    Widgets.Label(modelInputRect, "📡 " + "ATC_FetchingModel".Translate());
                    GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                }
                else
                {
                    config.SelectedModel = Widgets.TextField(modelInputRect, config.SelectedModel);
                    if (string.IsNullOrEmpty(config.SelectedModel))
                    {
                        GUI.color = Color.gray;
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(modelInputRect.x + 5f, modelInputRect.y + 2f, modelInputRect.width, modelInputRect.height), "ATC_InputOrSelectModel".Translate());
                        Text.Font = GameFont.Small;
                        GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                    }
                }

                if (Widgets.ButtonText(modelBtnRect, "▼"))
                {
                    if (config.FetchedModels.Count > 0 && !AutoTranslatorSettings.IsRunning && !config.IsFetching)
                    {
                        List<FloatMenuOption> opts = new List<FloatMenuOption>();
                        foreach (string m in config.FetchedModels) opts.Add(new FloatMenuOption(m, () => config.SelectedModel = m));
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    else if (!config.IsFetching && config.FetchedModels.Count == 0)
                    {
                        Messages.Message("ATC_Msg_NoModelListManualInput".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                }
                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;

                // 🌟 新增 Row C：API 連線測試按鈕
                Rect rowC = l.GetRect(24f);
                Rect testBtnRect = new Rect(rowC.x, rowC.y + 2f, 120f, rowC.height); // 往下推 2px 更好看

                if (config.IsTesting)
                {
                    GUI.color = Color.yellow;
                    Widgets.Label(testBtnRect, "⏳ " + "ATC_Testing".Translate());
                }
                else
                {
                    GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : new Color(0.6f, 0.9f, 0.6f);
                    if (Widgets.ButtonText(testBtnRect, "🔌 " + "ATC_TestConnection".Translate()))
                    {
                        if (string.IsNullOrEmpty(config.Key) || string.IsNullOrEmpty(config.SelectedModel))
                        {
                            Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                        }
                        else if (!AutoTranslatorSettings.IsRunning)
                        {
                            // 呼叫測試方法
                            AutoTranslatorAPI.RunConnectionTest(config);
                        }
                    }
                }
                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;

                l.Gap(15f);
            }

            GUI.color = new Color(0.4f, 0.8f, 1f);
            if (!AutoTranslatorSettings.IsRunning && l.ButtonText("＋ " + "ATC_AddApiBtn".Translate()))
            {
                Settings.ApiConfigs.Add(new ApiKeyConfig());
            }
            GUI.color = Color.white;

            // ==========================================
            // ✨ 架構師新增：終極急救箱與快取管理
            // ==========================================
            l.Gap(20f);
            Widgets.DrawLineHorizontal(0, l.CurHeight, viewRect.width);
            l.Gap(10f);

            Text.Font = GameFont.Small;
            Widgets.Label(l.GetRect(24f), "🚑 " + "ATC_EmergencyResetTitle".Translate());

            // ✨ 新增：溫和的 UI 快取清理按鈕
            Rect clearUIBtnRect = l.GetRect(35f);
            GUI.color = new Color(1f, 0.7f, 0.3f); // 橘色警告
            if (Widgets.ButtonText(clearUIBtnRect, "🧹 " + "ATC_Btn_ClearUICache".Translate()))
            {
                UIInterceptor.ClearUICache();
                Messages.Message("ATC_Msg_UICacheCleared".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            l.Gap(5f);

            // 原本的核彈級重置按鈕
            Rect resetBtnRect = l.GetRect(35f);
            GUI.color = new Color(1f, 0.3f, 0.3f); // 亮紅色警示
            if (Widgets.ButtonText(resetBtnRect, "⚠️ " + "ATC_Btn_FactoryReset".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "ATC_Msg_ConfirmFactoryReset".Translate(),
                    "ATC_Btn_Confirm".Translate(),
                    () => { ExecuteFactoryReset(); },
                    "ATC_Btn_Cancel".Translate(),
                    null,
                    "ATC_EmergencyResetTitle".Translate()
                ));
            }
            GUI.color = Color.white;
        }

        // ✨ 架構師新增：執行一鍵重置的實體方法 (緊接在 DrawConfigTab 方法之後)
        private void ExecuteFactoryReset()
        {
            try
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string langsPath = System.IO.Path.Combine(packPath, "Languages");

                // 1. 物理超渡：刪除所有實體翻譯檔案 (不管是什麼語言全部炸掉)
                if (System.IO.Directory.Exists(langsPath))
                {
                    // 🛡️ 強制爆破：先遞迴解除資料夾內所有檔案的「唯讀」屬性，再執行核彈爆破！
                    foreach (string file in System.IO.Directory.GetFiles(langsPath, "*", System.IO.SearchOption.AllDirectories))
                    {
                        System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                    }
                    System.IO.Directory.Delete(langsPath, true);
                }
                // 2. 清空前台 UI 快取記憶體與生字黑名單
                UIInterceptor.ClearUICache();

                // 3. 清空增量更新的記憶時間戳 (讓所有模組回到「未翻譯」的原始狀態)
                AutoTranslatorMod.Settings.ModLastVerifiedTimes.Clear();
                LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

                // 4. 重建基礎資料夾與免疫標記，防止系統崩潰
                AutoTranslatorScanner.EnsurePackInitialized();

                // 5. 洗淨 Log 面板並輸出成功提示
                AutoTranslatorSettings.ClearLog();
                AutoTranslatorSettings.AddLog("🚑 " + "ATC_Log_FactoryResetSuccess".Translate());

                Verse.Messages.Message("ATC_Msg_FactoryResetSuccess".Translate(), RimWorld.MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[AutoTranslationCore] Factory Reset Failed: {ex.Message}");
                AutoTranslatorSettings.AddErrorLog("Factory Reset Failed: " + ex.Message);
            }
        }
        // ==========================================
        // ☁️ 第三分頁：雲端共享 (Cloud Hub)
        // ==========================================
        private void DrawCloudTab(Listing_Standard l, Rect viewRect)
        {
            // --- 1. 第一排頂部工具列 (一般玩家操作) ---
            Rect topBarRect1 = l.GetRect(30f);

            if (AutoTranslatorSettings.IsFetchingCloud)
            {
                GUI.color = Color.yellow;
                Widgets.Label(topBarRect1, "ATC_Cloud_Fetching".Translate());
                GUI.color = Color.white;
            }
            else
            {
                if (!AutoTranslatorSettings.HasFetchedCloudThisSession || Widgets.ButtonText(new Rect(topBarRect1.x, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Refresh".Translate()))
                {
                    AutoTranslatorSettings.IsFetchingCloud = true;
                    AutoTranslatorSettings.HasFetchedCloudThisSession = true;
                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        var data = await AutoTranslatorCloudClient.FetchRegistryAsync();
                        ATC_Dispatcher.RunOnMainThread(() =>
                        {
                            if (data == null)
                            {
                                AutoTranslatorSettings.CloudRegistry = new List<CloudModRecord>();
                                AutoTranslatorSettings.CloudConnectionFailed = true; // ✨ 標記為失敗
                                Verse.Messages.Message("ATC_Cloud_ConnectionFailed".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                            }
                            else
                            {
                                AutoTranslatorSettings.CloudRegistry = data;
                                AutoTranslatorSettings.CloudConnectionFailed = false; // ✨ 標記為成功
                            }
                            AutoTranslatorSettings.IsFetchingCloud = false;
                        });
                    });
                }

                GUI.color = new Color(1f, 0.8f, 0.2f);
                if (Widgets.ButtonText(new Rect(topBarRect1.x + 150f, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Btn_BatchOfficial".Translate()))
                    ExecuteBatchDownload("Official_Group");

                GUI.color = new Color(0.4f, 0.8f, 1f);
                if (Widgets.ButtonText(new Rect(topBarRect1.x + 300f, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Btn_BatchAI".Translate()))
                    ExecuteBatchDownload("AI_Auto");
                GUI.color = Color.white;
            }

            l.Gap(5f);

            // --- ✨ 第二排頂部工具列 (漢化組大佬專區) ---
            Rect topBarRect2 = l.GetRect(30f);

            GUI.color = new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(topBarRect2.x, topBarRect2.y, 140f, topBarRect2.height), "ATC_Cloud_Btn_OpenWorkspace".Translate()))
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string workspaceRoot = System.IO.Path.Combine(packPath, "Upload_Workspace");
                System.IO.Directory.CreateDirectory(workspaceRoot); // 確保總管資料夾存在
                UnityEngine.Application.OpenURL("file://" + workspaceRoot); // 彈出 Windows 資料夾
            }

            GUI.color = new Color(1f, 0.6f, 0.2f);
            if (Widgets.ButtonText(new Rect(topBarRect2.x + 150f, topBarRect2.y, 140f, topBarRect2.height), "ATC_Cloud_Btn_BatchUpload".Translate()))
            {
                ExecuteBatchUpload(); // 呼叫批量上傳引擎
            }
            GUI.color = Color.white;

            l.Gap(5f);

            // 暱稱與特權碼輸入區
            Rect userRow = l.GetRect(24f);
            Widgets.Label(new Rect(userRow.x, userRow.y + 2f, 100f, 24f), "ATC_Cloud_Nickname".Translate());
            Settings.CloudNickname = Widgets.TextField(new Rect(userRow.x + 100f, userRow.y, 150f, 24f), Settings.CloudNickname);

            Widgets.Label(new Rect(userRow.x + 280f, userRow.y + 2f, 100f, 24f), "ATC_Cloud_AdminKey".Translate());
            Settings.CloudAdminToken = GUI.PasswordField(new Rect(userRow.x + 380f, userRow.y, 150f, 24f), Settings.CloudAdminToken, '*');

            // ✨ V3.5 上傳類型選擇區 (放在密碼框的下一行)
            l.Gap(5f);
            Rect typeRow = l.GetRect(24f);
            Widgets.Label(new Rect(typeRow.x, typeRow.y + 2f, 120f, 24f), "ATC_Cloud_Type_Select".Translate());

            if (Widgets.RadioButtonLabeled(new Rect(typeRow.x + 130f, typeRow.y, 100f, 24f), "ATC_Cloud_Type_AI".Translate(), Settings.CloudUploadType == "AI_Auto"))
            {
                Settings.CloudUploadType = "AI_Auto";
            }
            if (Widgets.RadioButtonLabeled(new Rect(typeRow.x + 240f, typeRow.y, 100f, 24f), "ATC_Cloud_Type_Manual".Translate(), Settings.CloudUploadType == "Manual"))
            {
                Settings.CloudUploadType = "Manual";
            }
            // ✨ V5.2 雲端專屬語言選擇器！與遊戲本體語言徹底脫鉤！
            l.Gap(5f);
            Rect cloudLangRow = l.GetRect(30f);
            Widgets.Label(new Rect(cloudLangRow.x, cloudLangRow.y + 5f, 120f, 24f), "ATC_Cloud_SelectLang".Translate());

            if (Widgets.ButtonText(new Rect(cloudLangRow.x + 130f, cloudLangRow.y, 200f, 30f), "🌐 " + GetLangLabel(Settings.CloudTargetLang)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                {
                    options.Add(new FloatMenuOption(GetLangLabel(lang), () => {
                        Settings.CloudTargetLang = lang;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            l.Gap(10f);
            Widgets.DrawLineHorizontal(0, l.CurHeight, viewRect.width);
            l.Gap(10f);

            if (AutoTranslatorSettings.IsFetchingCloud) return;

            // --- 2. 獲取本地有效模組清單 (✨ 調用極速快取) ---
            var localMods = GetValidModsCached();

            Text.Font = GameFont.Small;
            string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(Settings.CloudTargetLang);

            int currentLangCloudCount = AutoTranslatorSettings.CloudRegistry.Count(c => c.Language == targetLangFolder);

            if (AutoTranslatorSettings.CloudConnectionFailed)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                Widgets.Label(l.GetRect(25f), "⚠️ " + "ATC_Cloud_ConnectionFailed".Translate());
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.4f, 0.8f, 1f);
                Widgets.Label(l.GetRect(25f), "ATC_Cloud_ConnectionNormal".Translate(currentLangCloudCount));
                GUI.color = Color.white;
            }

            // ✨ V4.1 新增：雲端介面專屬搜尋框
            Rect searchRect = l.GetRect(30f);
            AutoTranslatorSettings.CloudSearchText = Widgets.TextField(searchRect, AutoTranslatorSettings.CloudSearchText);
            if (string.IsNullOrEmpty(AutoTranslatorSettings.CloudSearchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "🔍 " + "ATC_Cloud_SearchHint".Translate());
                GUI.color = Color.white;
            }
            l.Gap(10f);

            // ✨ 執行搜尋過濾
            if (!string.IsNullOrEmpty(AutoTranslatorSettings.CloudSearchText))
            {
                string searchLower = AutoTranslatorSettings.CloudSearchText.ToLower();
                localMods = localMods.Where(m => m.Name.ToLower().Contains(searchLower) || m.PackageId.ToLower().Contains(searchLower)).ToList();
            }

            if (localMods.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(l.GetRect(40f), "ATC_Cloud_NoModsWarning".Translate());
                GUI.color = Color.white;
                return; // 沒東西就提早結束繪製，節省效能
            }

            // 🚀🚀🚀 架構師極速優化大絕招 (真正解決 FPS 掉到 15 的元凶) 🚀🚀🚀
            // 不要在每幀重建字典與排序！利用快取將 O(N*M) 降維打擊成絕對的 0 延遲！
            if (_cachedCloudLookup == null || _lastCloudRegistryCount != AutoTranslatorSettings.CloudRegistry.Count || _lastCloudLangFolder != targetLangFolder)
            {
                _cachedCloudLookup = new Dictionary<string, List<CloudModRecord>>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in AutoTranslatorSettings.CloudRegistry)
                {
                    if (record.Language == targetLangFolder)
                    {
                        if (!_cachedCloudLookup.ContainsKey(record.PackageId))
                        {
                            _cachedCloudLookup[record.PackageId] = new List<CloudModRecord>();
                        }
                        _cachedCloudLookup[record.PackageId].Add(record);
                    }
                }

                // ✨ 提前把雲端紀錄排序好，絕對不要放在下方迴圈每秒排 60 次！
                foreach (var key in _cachedCloudLookup.Keys.ToList())
                {
                    _cachedCloudLookup[key] = _cachedCloudLookup[key]
                        .OrderByDescending(c => c.TranslationType == "Official_Group" || c.IsVerified)
                        .ThenByDescending(c => c.TranslationDate).ToList();
                }

                _lastCloudRegistryCount = AutoTranslatorSettings.CloudRegistry.Count;
                _lastCloudLangFolder = targetLangFolder;
            }
            var cloudLookup = _cachedCloudLookup;
            // --- 3. 繪製模組清單 ---
            float rowHeight = 40f;
            foreach (var mod in localMods)
            {
                Rect rowRect = l.GetRect(rowHeight);
                Widgets.DrawHighlightIfMouseover(rowRect);

                // ✨ V5.0 架構師優化：從剛才建好的高速字典中直接撈取，耗時趨近於 0！
                // ✨ V5.0 架構師優化：從剛才建好的高速字典中直接撈取，耗時趨近於 0！
                List<CloudModRecord> allVersions;
                if (cloudLookup.TryGetValue(mod.PackageId, out var foundList))
                {
                    allVersions = foundList; // ✨ 已經在剛才的快取中排好序了！直接套用！
                }
                else
                {
                    allVersions = new List<CloudModRecord>(); // 找不到給空清單
                }

                AutoTranslatorSettings.SelectedCloudVersion.TryGetValue(mod.PackageId, out CloudModRecord cloudRecord);

                if (cloudRecord == null || !allVersions.Any(v => v.RecordId == cloudRecord.RecordId))
                {
                    cloudRecord = allVersions.FirstOrDefault();
                    if (cloudRecord != null)
                    {
                        AutoTranslatorSettings.SelectedCloudVersion[mod.PackageId] = cloudRecord;
                    }
                    else
                    {
                        AutoTranslatorSettings.SelectedCloudVersion.Remove(mod.PackageId);
                    }
                }
             // --- 狀態判斷邏輯 ---
                string statusText = "";
                Color statusColor = Color.white;
                bool canDownload = false;

                if (cloudRecord == null)
                {
                    statusText = "ATC_Cloud_Status_NoCloud".Translate();
                    statusColor = Color.gray;
                }
                else if (cloudRecord.TranslationType == "Official_Group" || cloudRecord.IsVerified)
                {
                    statusText = "ATC_Cloud_Status_Official".Translate();
                    statusColor = new Color(1f, 0.8f, 0.2f); // 金色
                    canDownload = true;
                }
                else if (cloudRecord.TranslationType == "Manual")
                {
                    statusText = "ATC_Cloud_Status_Manual".Translate();
                    statusColor = new Color(0.4f, 1f, 0.4f); // 亮綠色
                    canDownload = true;
                }
                else
                {
                    statusText = "ATC_Cloud_Status_Latest".Translate();
                    statusColor = new Color(0.4f, 0.8f, 1f); // 藍色 (機翻)
                    canDownload = true;
                }

                // === 按鈕區塊 (從右到左排列，動態計算空間) ===
                Text.Font = GameFont.Small;
                float btnWidth = 85f; // ✨ 咪咪微調：加寬到 85f，確保「刪除中」、「上傳中」不會被切掉！
                float cursorX = rowRect.xMax - 5f;

                // 1. 特權雲端刪除
                if (!string.IsNullOrEmpty(Settings.CloudAdminToken) && cloudRecord != null)
                {
                    cursorX -= btnWidth;
                    Rect deleteCloudBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                    if (AutoTranslatorSettings.CloudUploadTarget == mod.PackageId + "_del")
                    {
                        GUI.color = Color.red;
                        Text.Anchor = TextAnchor.MiddleCenter; // ✨ 文字置中，完美對齊
                        Widgets.Label(deleteCloudBtn, "ATC_Cloud_Deleting".Translate());
                        Text.Anchor = TextAnchor.UpperLeft;    // ✨ 畫完記得恢復原狀
                    }
                    else
                    {
                        GUI.color = new Color(1f, 0.3f, 0.3f);
                        if (Widgets.ButtonText(deleteCloudBtn, "ATC_Cloud_Btn_DeleteCloud".Translate()))
                        {
                            AutoTranslatorSettings.CloudUploadTarget = mod.PackageId + "_del";
                            // ✨ 修正：傳入當前下拉選單選中的那筆精準 cloudRecord.RecordId 過去
                            string pid = mod.PackageId; string lang = targetLangFolder; string token = Settings.CloudAdminToken; string recId = cloudRecord.RecordId;
                            System.Threading.Tasks.Task.Run(async () => {
                                bool success = await AutoTranslatorCloudClient.DeleteCloudRecordAsync(pid, lang, recId, token); ATC_Dispatcher.RunOnMainThread(() => {
                                    AutoTranslatorSettings.CloudUploadTarget = "";
                                    if (success) { Messages.Message("ATC_Msg_DeleteCloudSuccess".Translate(mod.Name), MessageTypeDefOf.PositiveEvent, false); AutoTranslatorSettings.HasFetchedCloudThisSession = false; }
                                    else Messages.Message("ATC_Msg_DeleteCloudFailed".Translate(mod.Name), MessageTypeDefOf.RejectInput, false);
                                });
                            });
                        }
                    }
                    GUI.color = Color.white;
                    cursorX -= 5f;
                }

                // 2. 本地刪除
                cursorX -= btnWidth;
                Rect deleteLocalBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                GUI.color = new Color(1f, 0.6f, 0.6f);
                if (Widgets.ButtonText(deleteLocalBtn, "ATC_Cloud_Btn_DeleteLocal".Translate()))
                {
                    AutoTranslatorScanner.ClearOldTranslationFiles(new List<ModMetaData> { mod });
                    Messages.Message("ATC_Msg_DeleteLocalSuccess".Translate(mod.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                GUI.color = Color.white;
                cursorX -= 5f;

                // 3. 上傳按鈕
                cursorX -= btnWidth;
                Rect uploadBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                if (AutoTranslatorSettings.CloudUploadTarget == mod.PackageId)
                {
                    GUI.color = Color.yellow;
                    Text.Anchor = TextAnchor.MiddleCenter; // ✨ 文字置中
                    Widgets.Label(uploadBtn, "ATC_Cloud_Btn_Uploading".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    if (Widgets.ButtonText(uploadBtn, "ATC_Cloud_Btn_Upload".Translate()))
                    {
                        AutoTranslatorSettings.CloudUploadTarget = mod.PackageId;
                        string packPath = AutoTranslatorScanner.GetLocalPackPath();
                        string uNickname = Settings.CloudNickname; string uToken = Settings.CloudAdminToken;
                        string workspaceDir = System.IO.Path.Combine(packPath, "Upload_Workspace", mod.PackageId, targetLangFolder);
                        string liveLangDir = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
                        bool useWorkspace = System.IO.Directory.Exists(workspaceDir) && System.IO.Directory.GetFiles(workspaceDir, "*.xml", System.IO.SearchOption.AllDirectories).Length > 0;
                        string finalSourceDir = useWorkspace ? workspaceDir : liveLangDir;

                        string pId = mod.PackageId;
                        string tFolder = targetLangFolder;
                        string mName = mod.Name;
                        string fSource = finalSourceDir;
                        Find.WindowStack.Add(new Window_UploadPreview(mod, tFolder, fSource, mName));

                        // ✨ 因為上傳的複雜邏輯已經搬到 Window_UploadPreview 裡面了，所以這裡只要負責呼叫視窗就好！
                        // 呼叫完視窗先把狀態清空，避免按鈕一直卡在「上傳中」的黃色狀態
                        AutoTranslatorSettings.CloudUploadTarget = "";
                    }
                    GUI.color = Color.white;
                }
                cursorX -= 5f;

                // ✨ 4. 新增大佬專用：打開專屬上傳工作室 (Workspace)
                cursorX -= 40f;
                Rect openFolderBtn = new Rect(cursorX, rowRect.y + 5f, 35f, 30f);
                GUI.color = new Color(1f, 0.9f, 0.6f);
                // 🌟 改用本地化文字，完美適配 35f 的按鈕寬度
                if (Widgets.ButtonText(openFolderBtn, "ATC_Cloud_Btn_Dir".Translate()))
                {
                    string packPath = AutoTranslatorScanner.GetLocalPackPath();
                    string workspaceDir = System.IO.Path.Combine(packPath, "Upload_Workspace", mod.PackageId, targetLangFolder);
                    System.IO.Directory.CreateDirectory(workspaceDir);
                    UnityEngine.Application.OpenURL("file://" + workspaceDir);
                }
                // 🌟 清理重複的垃圾代碼，只留一個乾淨的本地化 Tooltip
                if (Mouse.IsOver(openFolderBtn)) TooltipHandler.TipRegion(openFolderBtn, "ATC_Cloud_Btn_DirTooltip".Translate());
                GUI.color = Color.white;
                cursorX -= 5f;

                // 5. 下載按鈕
                if (canDownload)
                {
                    float dlWidth = 85f;
                    cursorX -= dlWidth;
                    Rect downloadBtn = new Rect(cursorX, rowRect.y + 5f, dlWidth - 5f, 30f);
                    GUI.color = new Color(0.6f, 1f, 0.6f);
                    if (Widgets.ButtonText(downloadBtn, "ATC_Cloud_Btn_Download".Translate()))
                    {
                        Messages.Message("ATC_Msg_DownloadStart".Translate(mod.Name), MessageTypeDefOf.NeutralEvent, false);
                        // ✨ 鎖定目前的 CloudRecord 實體傳遞過去！
                        CloudModRecord targetRecord = cloudRecord;
                        System.Threading.Tasks.Task.Run(async () => {
                            bool success = await AutoTranslatorCloudClient.DownloadAndInjectAsync(mod.PackageId, targetLangFolder, targetRecord);
                            ATC_Dispatcher.RunOnMainThread(() => {
                                if (success) Messages.Message("ATC_Msg_DownloadSuccess".Translate(mod.Name), MessageTypeDefOf.PositiveEvent, false);
                                else Messages.Message("ATC_Msg_DownloadFailed".Translate(mod.Name), MessageTypeDefOf.RejectInput, false);
                            });
                        });
                    }
                    GUI.color = Color.white;
                    cursorX -= 5f;
                }

                // ✨ 6. V5.0 UI 革命：下拉選單 (支援全中文化與直白顯示)
                if (cloudRecord != null)
                {
                    float dropWidth = 140f;
                    cursorX -= dropWidth;
                    Rect verDropRect = new Rect(cursorX, rowRect.y + 5f, dropWidth - 5f, 30f);

                    string mergedTag = cloudRecord.IsSmartMerged ? "ATC_Cloud_SmartMerged".Translate().ToString() : "";

                    // 🌟 咪咪的翻譯轉換器：把硬梆梆的英文代碼轉成中文
                    Func<string, string> getLocType = (t) => {
                        if (t == "Official_Group") return "ATC_Type_Official".Translate();
                        if (t == "Manual") return "ATC_Type_Manual".Translate();
                        if (t == "AI_Auto") return "ATC_Type_AI".Translate();
                        return t;
                    };

                    string currentLocType = getLocType(cloudRecord.TranslationType);
                    // 外面收合時，顯示「版本 + 類型」
                    string verLabel = $"v{cloudRecord.LatestVersion} ({currentLocType}){mergedTag}";

                    if (Widgets.ButtonText(verDropRect, verLabel))
                    {
                        List<FloatMenuOption> verOptions = new List<FloatMenuOption>();
                        foreach (var v in allVersions)
                        {
                            string mTag = v.IsSmartMerged ? "ATC_Cloud_SmartMerged".Translate().ToString() : "";
                            string vLocType = getLocType(v.TranslationType);
                            // 下拉展開時，清楚顯示「日期、類型、上傳者」
                            string optLabel = $"[{v.LastUpdated:yyyy-MM-dd}] ({vLocType}) - {v.Author}{mTag}";
                            verOptions.Add(new FloatMenuOption(optLabel, () => { AutoTranslatorSettings.SelectedCloudVersion[mod.PackageId] = v; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(verOptions));
                    }

                    if (Mouse.IsOver(verDropRect))
                    {
                        string yesStr = "ATC_Cloud_YesWithCount".Translate(cloudRecord.MergedAiCount);
                        string noStr = "ATC_Cloud_No".Translate();
                        string mergeStatus = cloudRecord.IsSmartMerged ? yesStr : noStr;
                        string logDisplay = string.IsNullOrWhiteSpace(cloudRecord.UpdateLog) ? "ATC_Cloud_NoLog".Translate().ToString() : cloudRecord.UpdateLog;

                        // 懸停視窗也同步顯示翻譯過後的類型
                        string tipStr = "ATC_Cloud_UploadDate".Translate(cloudRecord.LastUpdated.ToString("yyyy-MM-dd HH:mm")) + "\n" +
                                        "ATC_Cloud_TransType".Translate(currentLocType) + "\n" +
                                        "ATC_Cloud_IsSmartMerged".Translate(mergeStatus) + "\n" +
                                        "📜 " + "ATC_Cloud_LogTitle".Translate() + ": " + logDisplay;
                        TooltipHandler.TipRegion(verDropRect, tipStr);
                    }
                }
                // === ✨ 最後繪製左側的文字 (動態計算剩餘寬度，絕對不會碰撞重疊！) ===
                float leftSpace = cursorX - rowRect.x - 10f;
                Rect nameRect = new Rect(rowRect.x + 5f, rowRect.y + 2f, leftSpace, 20f);
                Rect statusRect = new Rect(rowRect.x + 5f, rowRect.y + 22f, leftSpace, 18f);

                Text.Font = GameFont.Small;

                // 🌟 咪咪防禦網：關閉自動換行，名字太長就優雅截斷，滑鼠移過去再顯示全名！
                Text.WordWrap = false;
                Widgets.Label(nameRect, mod.Name);
                Text.WordWrap = true; // 畫完一定要開回來，不然其他地方的 UI 會壞掉！

                if (Mouse.IsOver(nameRect))
                {
                    TooltipHandler.TipRegion(nameRect, mod.Name); // 滑鼠懸停顯示完整名字
                }

                Text.Font = GameFont.Tiny;
                GUI.color = statusColor;
                Widgets.Label(statusRect, statusText);
                GUI.color = Color.white;
            }
        }
        // ==========================================
        // 🚀 批量空投引擎 (背景執行，不卡畫面) 語言隔離 + 本地化版
        // ==========================================
        private void ExecuteBatchDownload(string targetType)
        {
            var localMods = Verse.ModLister.AllInstalledMods.Where(m => m.Active).ToList();

            // ✨ 取得當前設定的語言，確保只下載符合玩家語系的翻譯！
            string targetLangStr = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.CloudTargetLang);

            // ✨ 這裡只宣告一次，精準抓取符合「語言」與「特權類型」的雲端紀錄
            var targetCloudRecords = AutoTranslatorSettings.CloudRegistry
                .Where(c => c.Language == targetLangStr && (c.TranslationType == targetType || (targetType == "Official_Group" && c.IsVerified)))
                .ToList();

            // 找出本地有安裝，且雲端符合目標類型的模組
            var modsToDownload = new List<Verse.ModMetaData>();
            foreach (var record in targetCloudRecords)
            {
                var mod = localMods.FirstOrDefault(m => m.PackageId.ToLower() == record.PackageId.ToLower());
                if (mod != null) modsToDownload.Add(mod);
            }

            if (modsToDownload.Count == 0)
            {
                Verse.Messages.Message("ATC_Msg_BatchNoMods".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                return;
            }

            Verse.Messages.Message("ATC_Msg_BatchStart".Translate(modsToDownload.Count), RimWorld.MessageTypeDefOf.NeutralEvent, false);

            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                // ✨ 架構師優化：鎖定系統狀態，讓主畫面的進度條開始運作
                AutoTranslatorSettings.IsRunning = true;

                for (int i = 0; i < modsToDownload.Count; i++)
                {
                    var mod = modsToDownload[i];

                    // ✨ 實時更新進度條與任務名稱
                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Downloading".Translate(mod.Name);
                    AutoTranslatorMod.Settings.CurrentProgress = (float)i / modsToDownload.Count;

                    // 找出這個 mod 對應的雲端紀錄
                    var record = targetCloudRecords.FirstOrDefault(c => c.PackageId.ToLower() == mod.PackageId.ToLower());

                    bool success = await AutoTranslatorCloudClient.DownloadAndInjectAsync(mod.PackageId, targetLangStr, record);
                    if (success) successCount++;
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    // ✨ 任務結束，解除鎖定並歸零進度條
                    AutoTranslatorSettings.IsRunning = false;
                    AutoTranslatorMod.Settings.CurrentTaskName = "";
                    AutoTranslatorMod.Settings.CurrentProgress = 0f;
                    Verse.Messages.Message("ATC_Msg_BatchSuccess".Translate(successCount, modsToDownload.Count), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                });
            });
        }
        // ==========================================
        // 🚀 批量上傳引擎 (專為漢化組打造，全自動掃描工作區)
        // ==========================================
        private void ExecuteBatchUpload()
        {
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string workspaceRoot = System.IO.Path.Combine(packPath, "Upload_Workspace");
            string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(Settings.CloudTargetLang);
            string uNickname = Settings.CloudNickname;
            string uToken = Settings.CloudAdminToken;

            // 1. 檢查總工作區存不存在
            if (!System.IO.Directory.Exists(workspaceRoot))
            {
                Messages.Message("ATC_Msg_WorkspaceEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 2. 獲取裡面所有的資料夾 (每一個資料夾名稱就是一個 PackageId)
            var modDirs = System.IO.Directory.GetDirectories(workspaceRoot);
            if (modDirs.Length == 0)
            {
                Messages.Message("ATC_Msg_WorkspaceEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("ATC_Msg_BatchUploadStart".Translate(modDirs.Length), MessageTypeDefOf.NeutralEvent, false);

            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                // ✨ 架構師優化：鎖定系統狀態
                AutoTranslatorSettings.IsRunning = true;

                for (int i = 0; i < modDirs.Length; i++)
                {
                    var modDir = modDirs[i];
                    // 資料夾名稱即為 PackageId
                    string packageId = System.IO.Path.GetFileName(modDir);

                    // 嘗試從本地模組清單抓取模組名稱
                    var tempMeta = Verse.ModLister.AllInstalledMods.FirstOrDefault(m => m.PackageId.ToLower() == packageId.ToLower());
                    string displayName = tempMeta != null ? tempMeta.Name : packageId;

                    // ✨ 實時更新進度條與任務名稱
                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Uploading".Translate(displayName);
                    AutoTranslatorMod.Settings.CurrentProgress = (float)i / modDirs.Length;

                    // 組裝語言資料夾路徑: Upload_Workspace / packageId / ChineseTraditional
                    string langDir = System.IO.Path.Combine(modDir, targetLangFolder);

                    // 如果這個語言資料夾不存在，或是裡面沒有 xml 檔，就跳過
                    if (!System.IO.Directory.Exists(langDir)) continue;
                    if (System.IO.Directory.GetFiles(langDir, "*.xml", System.IO.SearchOption.AllDirectories).Length == 0) continue;

                    string modName = tempMeta != null ? tempMeta.Name : packageId;

                    // 🚀 發射到雲端！
                    bool success = await AutoTranslatorCloudClient.UploadTranslationAsync(packageId, targetLangFolder, modName, uNickname, Settings.CloudUploadType, langDir, uToken);

                    if (success)
                    {
                        successCount++;
                        // ✨ 神級細節：上傳成功後，順便把檔案複製到遊戲真正的 Languages 目錄
                        string liveLangDir = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
                        foreach (string file in System.IO.Directory.GetFiles(langDir, "*.xml", System.IO.SearchOption.AllDirectories))
                        {
                            string relPath = file.Substring(langDir.Length).TrimStart('\\', '/');

                            // ✨ 架構師修復：批量上傳的本地複製，一樣強制冠上前綴！
                            string justFileName = System.IO.Path.GetFileName(file);
                            string justFileNameLower = justFileName.ToLower();
                            string id1 = packageId.ToLower();
                            string id2 = packageId.Replace(".", "_").ToLower();

                            if (!justFileNameLower.StartsWith(id1 + "_") && !justFileNameLower.StartsWith(id1 + ".") &&
                                !justFileNameLower.StartsWith(id2 + "_") && !justFileNameLower.StartsWith(id2 + "."))
                            {
                                string dirName = System.IO.Path.GetDirectoryName(relPath);
                                string newFileName = $"{id2}_{justFileName}";
                                relPath = string.IsNullOrEmpty(dirName) ? newFileName : System.IO.Path.Combine(dirName, newFileName);
                            }

                            string destPath = System.IO.Path.Combine(liveLangDir, relPath);
                            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath));
                            System.IO.File.Copy(file, destPath, true);
                        }
                    }
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    // ✨ 任務結束，解除鎖定並歸零進度條
                    AutoTranslatorSettings.IsRunning = false;
                    AutoTranslatorMod.Settings.CurrentTaskName = "";
                    AutoTranslatorMod.Settings.CurrentProgress = 0f;

                    Verse.Messages.Message("ATC_Msg_BatchUploadSuccess".Translate(successCount), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                    AutoTranslatorSettings.HasFetchedCloudThisSession = false; // 強制下次重整清單，讓 UI 變金色！
                });
            });
        }

        private bool HasValidConfig()
        {
            return Settings.ApiConfigs.Any(c => !string.IsNullOrEmpty(c.Key) && !string.IsNullOrEmpty(c.SelectedModel));
        }

        private void DrawLogView(Rect rect, List<string> logs, ref Vector2 scrollPos, bool isErrorBox)
        {
            List<string> displayLogs;
            lock (AutoTranslatorSettings.logLock) { displayLogs = logs.ToList(); }

            Text.Font = GameFont.Tiny;
            float totalHeight = 0f;
            List<float> heights = new List<float>();
            foreach (string log in displayLogs) { float h = Text.CalcHeight(log, rect.width - 20f); heights.Add(h); totalHeight += h; }

            float contentHeight = Mathf.Max(totalHeight, rect.height);
            Rect viewRect = new Rect(0, 0, rect.width - 20f, contentHeight);

            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            float currentY = 0;

            for (int i = 0; i < displayLogs.Count; i++)
            {
                string log = displayLogs[i];
                float h = heights[i];
                Rect lineRect = new Rect(5f, currentY, viewRect.width, h);

                if (isErrorBox || log.Contains("❌") || log.Contains("⚠️") || log.Contains("🛑")) GUI.color = new Color(1f, 0.4f, 0.4f);
                else if (log.Contains("✅") || log.Contains("✨") || log.Contains("🎉")) GUI.color = new Color(0.4f, 1f, 0.4f);
                else if (log.Contains("⚙️") || log.Contains("🔌") || log.Contains("🔄") || log.Contains("⏭️")) GUI.color = new Color(1f, 0.8f, 0.4f);
                else if (log.Contains("📦") || log.Contains("🌐") || log.Contains("🚀") || log.Contains("🔍") || log.Contains("🧹")) GUI.color = new Color(0.4f, 0.8f, 1f);
                else GUI.color = new Color(0.8f, 0.8f, 0.8f);

                Widgets.Label(lineRect, log);
                currentY += h;
            }

            float viewHeight = heights.Sum();
            float maxScroll = Mathf.Max(0f, viewHeight - rect.height);

            if (!isErrorBox && (maxScroll - scrollPos.y <= 100f))
            {
                scrollPos.y = maxScroll;
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.EndScrollView();
        }

        public override string SettingsCategory() => "ATC_ModTitle".Translate();
    }

    public class TutorialWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 InitialSize => new Vector2(750f, 700f);
        public TutorialWindow() { this.doCloseButton = true; this.doCloseX = true; this.forcePause = true; this.absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect inRect)
        {

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "📖 " + "ATC_Tutorial_Btn".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);
            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 100f);
            string contentText = "ATC_Tutorial_FullText".Translate();
            float textHeight = Text.CalcHeight(contentText, inRect.width - 20f);
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, Mathf.Max(textHeight + 50f, outRect.height));
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            Widgets.Label(new Rect(0, 0, viewRect.width, textHeight), contentText);
            Widgets.EndScrollView();
        }
    }

    public class UpdateLogWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 InitialSize => new Vector2(750f, 700f);
        public UpdateLogWindow() { this.doCloseButton = true; this.doCloseX = true; this.forcePause = true; this.absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "📜 " + "ATC_UpdateLog_Btn".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);
            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 100f);
            string logText = "ATC_UpdateLog_FullText".Translate();
            float textHeight = Text.CalcHeight(logText, inRect.width - 20f);
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, Mathf.Max(textHeight + 50f, outRect.height));
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            Widgets.Label(new Rect(0, 0, viewRect.width, textHeight), logText);
            Widgets.EndScrollView();
        }
    }

    // ==========================================
    // 🌟 V4.8 搜尋、排序與多選模組視窗 (絲滑無雙修復版)
    // ==========================================
    public class ModSelectWindow : Window
    {
        private string searchText = "";
        private Vector2 scrollPos = Vector2.zero;
        private HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        public override Vector2 InitialSize => new Vector2(600f, 700f);
        private bool? dragTargetState = null;

        private List<ModMetaData> preSelectedMods;
        public ModSelectWindow(List<ModMetaData> updatedMods = null)
        {
            this.doCloseButton = false;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.preSelectedMods = updatedMods ?? new List<ModMetaData>();

            // 自動把有更新的模組打勾
            if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate)
            {
                foreach (var m in this.preSelectedMods) selectedMods.Add(m);
            }
        }
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "ATC_MultiSelect_Title".Translate());
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(0, 45f, inRect.width, 30f);
            searchText = Widgets.TextField(searchRect, searchText);
            if (string.IsNullOrEmpty(searchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_MultiSelect_Search".Translate());
                GUI.color = Color.white;
            }

            var allValidMods = ModLister.AllInstalledMods.Where(m =>
            m.Active &&
            m.PackageId.ToLower() != "auto.aitranslation.core" &&
            m.PackageId.ToLower() != "aitranslation.pack" &&
            !m.PackageId.ToLower().StartsWith("ludeon.rimworld") && // ✨ 架構師絕殺:官方本體與所有 DLC 徹底隱形!
            !IsCodeOnlyMod(m) && // ✨ 同步隱形：純代碼模組從 UI 澈底蒸發！
            !AutoTranslatorScanner.IsTranslationPatchMod(m) // ✨ 咪咪絕殺：漢化補丁直接在 UI 隱形！
            ); if (!string.IsNullOrEmpty(searchText))
                allValidMods = allValidMods.Where(m => m.Name.ToLower().Contains(searchText.ToLower()) || m.PackageId.ToLower().Contains(searchText.ToLower()));

            var displayMods = allValidMods.OrderBy(m => m.Name).ToList();

            Rect btnRow = new Rect(0, 85f, inRect.width, 30f);

            bool isAllSelected = (displayMods.Count > 0 && displayMods.All(m => selectedMods.Contains(m)));
            string btnLabel = isAllSelected ? "ATC_DeselectAll".Translate() : "ATC_SelectAll".Translate();

            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, 120f, btnRow.height), btnLabel))
            {
                if (isAllSelected)
                {
                    foreach (var m in displayMods) selectedMods.Remove(m);
                }
                else
                {
                    foreach (var m in displayMods) selectedMods.Add(m);
                }
            }

            GUI.color = new Color(1f, 0.6f, 0.8f);
            if (Widgets.ButtonText(new Rect(btnRow.x + 130f, btnRow.y, 120f, btnRow.height), "ATC_One_click_chaos".Translate()))
            {
                selectedMods.Clear();
                var rand = new System.Random();
                foreach (var m in displayMods) { if (rand.NextDouble() > 0.5) selectedMods.Add(m); }
            }
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(0, 120f, inRect.width);
            Rect listOutRect = new Rect(0, 130f, inRect.width, inRect.height - 180f);
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, displayMods.Count * 30f);
            Widgets.BeginScrollView(listOutRect, ref scrollPos, viewRect);

            if (Event.current.type == EventType.MouseUp) dragTargetState = null;

            float currentY = 0f;
            foreach (var mod in displayMods)
            {
                Rect rowRect = new Rect(0, currentY, viewRect.width, 30f);
                bool isChecked = selectedMods.Contains(mod);

                Widgets.DrawHighlightIfMouseover(rowRect);

                if (Mouse.IsOver(rowRect))
                {
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        isChecked = !isChecked;
                        dragTargetState = isChecked;
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseDrag && dragTargetState.HasValue)
                    {
                        isChecked = dragTargetState.Value;
                        Event.current.Use();
                    }
                }

                Vector2 checkPos = new Vector2(rowRect.x, rowRect.y + (rowRect.height - 24f) / 2f);
                Widgets.CheckboxDraw(checkPos.x, checkPos.y, isChecked, false, 24f, null, null);

                Rect labelRect = new Rect(rowRect.x + 30f, rowRect.y, rowRect.width - 30f, rowRect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, $"{mod.Name} ({mod.PackageId})");
                Text.Anchor = TextAnchor.UpperLeft;

                if (isChecked) selectedMods.Add(mod); else selectedMods.Remove(mod);
                currentY += 30f;
            }
            Widgets.EndScrollView();

            Rect bottomBtnRect = new Rect(0, inRect.height - 40f, inRect.width, 40f);
            GUI.color = selectedMods.Count > 0 ? new Color(0.6f, 0.9f, 0.6f) : Color.grey;
            if (Widgets.ButtonText(bottomBtnRect, "ATC_MultiSelect_Start".Translate(selectedMods.Count)))
            {
                if (selectedMods.Count > 0)
                {
                    AutoTranslatorSettings.ClearLog();
                    AutoTranslatorSettings.IsCancellationRequested = false;
                    AutoTranslatorSettings.IsSkipCurrentRequested = false;
                    AutoTranslatorScanner.StartMultiScan(selectedMods.ToList());
                    this.Close();
                }
            }
            GUI.color = Color.white;
        }
    }

    // ==========================================
    // 🌟 V4.9 翻譯檔案焚化爐 (絲滑無雙 ＋ 全本地化版)
    // ==========================================
    public class DeleteTranslationWindow : Window
    {
        private string searchText = "";
        private Vector2 scrollPos = Vector2.zero;
        private HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        public override Vector2 InitialSize => new Vector2(600f, 700f);
        private bool? dragTargetState = null;

        public DeleteTranslationWindow()
        {
            this.doCloseButton = false;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "🗑️ " + "ATC_DeleteModTrans_Title".Translate());
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(0, 45f, inRect.width, 30f);
            searchText = Widgets.TextField(searchRect, searchText);
            if (string.IsNullOrEmpty(searchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_MultiSelect_Search".Translate());
                GUI.color = Color.white;
            }

            var allValidMods = ModLister.AllInstalledMods.Where(m =>
                m.PackageId.ToLower() != "auto.aitranslation.core" &&
                m.PackageId.ToLower() != "aitranslation.pack" &&
                !m.PackageId.ToLower().StartsWith("ludeon.rimworld") // ✨ 官方 DLC 不准在刪除列表出現！
            );
            if (!string.IsNullOrEmpty(searchText)) allValidMods = allValidMods.Where(m => m.Name.ToLower().Contains(searchText.ToLower()) || m.PackageId.ToLower().Contains(searchText.ToLower()));
            var displayMods = allValidMods.OrderBy(m => m.Name).ToList();

            Widgets.DrawLineHorizontal(0, 85f, inRect.width);
            Rect listOutRect = new Rect(0, 95f, inRect.width, inRect.height - 145f);
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, displayMods.Count * 30f);
            Widgets.BeginScrollView(listOutRect, ref scrollPos, viewRect);

            if (Event.current.type == EventType.MouseUp) dragTargetState = null;

            float currentY = 0f;
            foreach (var mod in displayMods)
            {
                Rect rowRect = new Rect(0, currentY, viewRect.width, 30f);
                bool isChecked = selectedMods.Contains(mod);

                Widgets.DrawHighlightIfMouseover(rowRect);

                if (Mouse.IsOver(rowRect))
                {
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        isChecked = !isChecked;
                        dragTargetState = isChecked;
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseDrag && dragTargetState.HasValue)
                    {
                        isChecked = dragTargetState.Value;
                        Event.current.Use();
                    }
                }

                Vector2 checkPos = new Vector2(rowRect.x, rowRect.y + (rowRect.height - 24f) / 2f);
                Widgets.CheckboxDraw(checkPos.x, checkPos.y, isChecked, false, 24f, null, null);

                Rect labelRect = new Rect(rowRect.x + 30f, rowRect.y, rowRect.width - 30f, rowRect.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(labelRect, $"{mod.Name} ({mod.PackageId})");
                Text.Anchor = TextAnchor.UpperLeft;

                if (isChecked) selectedMods.Add(mod); else selectedMods.Remove(mod);
                currentY += 30f;
            }
            Widgets.EndScrollView();

            Rect bottomBtnRect = new Rect(0, inRect.height - 40f, inRect.width, 40f);
            GUI.color = selectedMods.Count > 0 ? new Color(1f, 0.4f, 0.4f) : Color.grey;
            if (Widgets.ButtonText(bottomBtnRect, "ATC_ConfirmDelete_Btn".Translate(selectedMods.Count)))
            {
                if (selectedMods.Count > 0)
                {
                    ExecuteDelete(selectedMods.ToList());
                    this.Close();
                }
            }
            GUI.color = Color.white;
        }

        private void ExecuteDelete(List<ModMetaData> modsToDelete)
        {
            try
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                foreach (var mod in modsToDelete)
                {
                    // 🌟 咪咪的雙重雷達：有些檔案是用點 (.)，有些是用底線 (_)
                    string id1 = mod.PackageId.ToLower();
                    string id2 = mod.PackageId.Replace(".", "_").ToLower();

                    foreach (var file in allXmls)
                    {
                        string fileName = System.IO.Path.GetFileName(file).ToLower();
                        // 只要中任何一種命名規則，格殺勿論！
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            // 🛡️ 強制爆破：剝奪唯讀權限後再刪除
                            System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                            System.IO.File.Delete(file);
                            deletedFiles++;
                        }
                    }
                }

                // 🌟 同步廣播到我們自己的 UI 面板
                string logMsg = "ATC_Log_DeleteTransSuccess".Translate(modsToDelete.Count, deletedFiles);
                AutoTranslatorSettings.AddLog(logMsg);

                // 🌟 同步廣播到 RimWorld 原廠開發者日誌 (Dev Console)！這樣大哥絕對搜得到！
                Log.Message($"[AutoTranslationCore] {logMsg}");

                // 🌟 螢幕右上角彈窗提示
                Messages.Message("ATC_Message_DeleteTransSuccess".Translate(deletedFiles), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("ATC_Message_DeleteTransError".Translate(ex.Message));
                Log.Warning($"[AutoTranslationCore] Delete failed: {ex.Message}");
            }
        }

        public static class ModUpdateDetector
        {
            // ✨ 加入快取機制，保護玩家的 FPS！
            private static List<ModMetaData> _cachedMods = null;
            private static float _lastCheckTime = 0f;

            // 取得模組最新的修改時間戳
            private static long GetLatestTick(ModMetaData mod)
            {
                long latest = 0;
                // 掃描根目錄資料夾
                if (Directory.Exists(mod.RootDir.FullName))
                {
                    latest = Math.Max(latest, new DirectoryInfo(mod.RootDir.FullName).LastWriteTimeUtc.Ticks);
                }
                // 掃描 About.xml
                string aboutPath = Path.Combine(mod.RootDir.FullName, "About/About.xml");
                if (File.Exists(aboutPath))
                {
                    latest = Math.Max(latest, new FileInfo(aboutPath).LastWriteTimeUtc.Ticks);
                }
                return latest;
            }

            // ✨ 對外開放的 UI 呼叫口 (附帶 3 秒節流防護罩)
            public static List<ModMetaData> GetUpdatedOrNewModsCached()
            {
                // 如果快取是空的，或者距離上次掃描已經過了 3 秒，才允許觸發硬碟掃描
                if (_cachedMods == null || Time.realtimeSinceStartup - _lastCheckTime > 3f)
                {
                    _cachedMods = GetUpdatedOrNewModsForce();
                    _lastCheckTime = Time.realtimeSinceStartup;
                }
                return _cachedMods;
            }
            // 真實的底層硬碟掃描邏輯 (隱藏起來不讓 UI 狂呼叫)
            private static List<ModMetaData> GetUpdatedOrNewModsForce()
            {
                var dict = AutoTranslatorMod.Settings.ModLastVerifiedTimes;
                var result = new List<ModMetaData>();
                AutoTranslatorSettings.FilteredModsCount = 0; // 每次掃描前歸零


                foreach (var mod in ModLister.AllInstalledMods.Where(m => m.Active))
                {
                    string pid = mod.PackageId.ToLower();

                    if (IsCodeOnlyMod(mod))
                    {
                        AutoTranslatorSettings.FilteredModsCount++; // 命中過濾規則，計數加1
                        continue;
                    }

                    // ✨ 架構師修復:完美遮蔽官方 DLC (ludeon.rimworld 開頭) 與翻譯模組本身
                    if (pid == "auto.aitranslation.core" ||
                                            pid == "aitranslation.pack" ||
                                            pid.StartsWith("ludeon.rimworld") ||
                                            IsCodeOnlyMod(mod) ||  // ✨ 架構師絕殺：純代碼模組直接在源頭物理消滅，永不進入更新名單！
                                            AutoTranslatorScanner.IsTranslationPatchMod(mod)) // ✨ 咪咪防護網：漢化補丁不准進入更新名單！
                        continue; long currentTicks = GetLatestTick(mod);
                    if (!dict.TryGetValue(mod.PackageId, out long savedTicks) || currentTicks > savedTicks)
                    {
                        result.Add(mod);
                    }
                }
                return result;
            }

            public static void MarkModAsTranslated(string packageId, string rootDir)
            {
                var meta = ModLister.AllInstalledMods.FirstOrDefault(m => m.PackageId == packageId);
                if (meta != null)
                {
                    AutoTranslatorMod.Settings.ModLastVerifiedTimes[packageId] = GetLatestTick(meta);
                    LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

                    // ✨ 標記完成後，強制清空快取，讓下次 UI 刷新時重新計算數量
                    _cachedMods = null;
                }
            }
        }
        // 🌟 架構師的跨執行緒神器：永遠活在主畫面上的派發器
        // 🌟 架構師的跨執行緒神器 (極致效能無鎖版)
        public class ATC_Dispatcher : MonoBehaviour
        {
            // 拋棄傳統 Queue 與 lock，改用 Thread-Safe 的 ConcurrentQueue (耗能幾乎為 0)
            private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> executionQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();

            // 開放給任何背景 Task 呼叫，把任務無鎖推入序列
            public static void RunOnMainThread(Action action)
            {
                executionQueue.Enqueue(action);
            }

            // Unity 引擎的生命週期
            public void Update()
            {
                // TryDequeue 本身具備原子性操作 (Atomic)，沒有任何 lock 阻塞問題！
                // 只要裡面有東西，它就會瞬間抽出來執行；沒東西時，這行幾乎不花費任何 CPU 週期。
                while (executionQueue.TryDequeue(out Action action))
                {
                    action?.Invoke();
                }
            }
        }
        public static bool IsCodeOnlyMod(ModMetaData mod)
        {
            // 防呆：如果模組未正確載入或沒有根目錄，直接略過
            if (mod == null || mod.RootDir == null) return true;

            // 取得 RimWorld 原生解析支援當前版本的活動資料夾
            var folders = mod.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild);

            // ✨ 架構師修復：建立一個 HashSet 來儲存所有需要檢查的路徑，避免重複檢查並提升效能
            var pathsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 加入系統 API 回傳的資料夾
            if (folders != null && folders.Any())
            {
                foreach (var folder in folders)
                {
                    pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, folder.folderName));
                }
            }

            // 2. ✨ 終極補漏防線：強制加入根目錄與常見的版本資料夾！
            // 解決模組作者沒寫 LoadFolders.xml，直接把 Defs 塞在 1.5 / 1.6 導致的隱形 Bug！
            pathsToCheck.Add(mod.RootDir.FullName);
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, VersionControl.CurrentVersionStringWithoutBuild)); // 當前遊戲版本 (例如 1.6)
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "1.5"));
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "1.4"));
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "Common")); // 很多模組會把共用 Defs 放這

            // 3. 開始迴圈比對有效資料夾內，是否有需要翻譯的東西
            foreach (var basePath in pathsToCheck)
            {
                if (!Directory.Exists(basePath)) continue;

                string defPath = Path.Combine(basePath, "Defs");
                string patchPath = Path.Combine(basePath, "Patches");
                string langPath = Path.Combine(basePath, "Languages");

                // 只要這三個資料夾命中其中一個，就證明它「不是」純代碼模組，立刻放行！
                if (Directory.Exists(defPath) || Directory.Exists(patchPath) || Directory.Exists(langPath))
                {
                    return false;
                }
            }

            // 全部找完都沒有上述資料夾，確認是純代碼 / 純素材模組，回傳 true 準備過濾！
            return true;
        }
        // =====================================================================
        // 🚀 V4.0 終極全域編輯工作台 (內嵌分頁版) - 完美防彈裝甲版！
        // =====================================================================
        public static class TranslationWorkbenchTab
        {
            public class WorkbenchItem
            {
                public string Key;
                public string OriginalText;
                public string TranslatedText;
                public bool IsModified;
            }

            private static Verse.ModMetaData _editingMod = null;
            private static bool _isLoading = false;
            private static string _modSearchText = "";
            // ✨ 咪咪修復：預設改為 false，一進來就顯示所有模組，方便玩家編輯模組自帶的翻譯！
            private static bool _showOnlyTranslated = false;
            private static UnityEngine.Vector2 _modListScroll = UnityEngine.Vector2.zero;

            private static Dictionary<string, List<WorkbenchItem>> _categorizedData = new Dictionary<string, List<WorkbenchItem>>();
            private static string _selectedCategory = "";
            private static string _itemSearchText = "";
            private static UnityEngine.Vector2 _catListScroll = UnityEngine.Vector2.zero;
            private static UnityEngine.Vector2 _itemScroll = UnityEngine.Vector2.zero;

            private static HashSet<string> _translatedPackageIds = null;
            // ===== 全域搜尋專用變數 =====
            private static string _globalSearchText = "";
            private static bool _isGlobalSearching = false;
            private static UnityEngine.Vector2 _globalSearchScroll = UnityEngine.Vector2.zero;
            private static List<GlobalSearchResult> _globalSearchResults = new List<GlobalSearchResult>();
            private static TargetLanguage? _globalSearchLangFilter = null; // null 代表搜尋所有語言
            private static float _globalSearchProgress = 0f; // ✨ 搜尋進度百分比
            public class GlobalSearchResult
            {
                public Verse.ModMetaData Mod;
                public string Key;
                public string TranslatedText;
            }

            public static void RequestRefresh()
            {
                _translatedPackageIds = null;
            }

            private static void InitTranslatedModsCache()
            {
                if (_translatedPackageIds != null) return;
                _translatedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string packPath = AutoTranslatorScanner.GetLocalPackPath();

                // ✨ 咪咪修復：必須使用 TargetLang (遊戲當前語系)，而不是雲端的 CloudTargetLang！
                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string langRoot = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);

                if (System.IO.Directory.Exists(langRoot))
                {
                    foreach (var file in System.IO.Directory.GetFiles(langRoot, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        string fileName = System.IO.Path.GetFileName(file);
                        int splitIdx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
                        if (splitIdx == -1) splitIdx = fileName.LastIndexOf('_');

                        if (splitIdx > 0 && splitIdx < fileName.Length)
                        {
                            string pid = fileName.Substring(0, splitIdx).Replace("_", ".");
                            _translatedPackageIds.Add(pid);
                        }
                    }
                }
            }

            private static void ExecuteGlobalSearch(string keyword, TargetLanguage? langFilter)
            {
                _isGlobalSearching = true;
                _globalSearchProgress = 0f; // 歸零進度條
                _globalSearchResults.Clear();

                System.Threading.Tasks.Task.Run(() => {
                    var results = new List<GlobalSearchResult>();
                    string searchLower = keyword.ToLower();
                    var activeMods = Verse.ModLister.AllInstalledMods.Where(m => m.Active).ToList();
                    string targetFolderFilter = langFilter.HasValue ? AutoTranslatorScanner.GetFolderNameByLanguage(langFilter.Value) : null;

                    // ==================================================
                    // 🚀 第一階段：極速盤點總共有多少個檔案要掃描！(只搜集路徑不讀內容)
                    // ==================================================
                    var filesToScan = new List<(string FilePath, Verse.ModMetaData Mod)>();

                    // 1. 盤點 AI 翻譯包
                    string packPath = AutoTranslatorScanner.GetLocalPackPath();
                    string langsRoot = System.IO.Path.Combine(packPath, "Languages");
                    if (System.IO.Directory.Exists(langsRoot))
                    {
                        string[] searchDirs = targetFolderFilter != null
                            ? (System.IO.Directory.Exists(System.IO.Path.Combine(langsRoot, targetFolderFilter)) ? new[] { System.IO.Path.Combine(langsRoot, targetFolderFilter) } : new string[0])
                            : System.IO.Directory.GetDirectories(langsRoot);

                        foreach (var dir in searchDirs)
                        {
                            var allXmls = System.IO.Directory.GetFiles(dir, "*.xml", System.IO.SearchOption.AllDirectories);
                            foreach (var file in allXmls)
                            {
                                string fileName = System.IO.Path.GetFileName(file);
                                int splitIdx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
                                if (splitIdx == -1) splitIdx = fileName.LastIndexOf('_');

                                if (splitIdx > 0)
                                {
                                    string pid = fileName.Substring(0, splitIdx).Replace("_", ".").ToLower();
                                    var targetMod = activeMods.FirstOrDefault(m => m.PackageId.ToLower() == pid);
                                    if (targetMod != null) filesToScan.Add((file, targetMod));
                                }
                            }
                        }
                    }

                    // 2. 盤點所有原生模組自帶翻譯
                    foreach (var mod in activeMods)
                    {
                        if (mod.PackageId.ToLower() == "auto.aitranslation.core" || mod.PackageId.ToLower() == "aitranslation.pack") continue;

                        var modLangRoots = AutoTranslatorScanner.GetAllEffectiveLangPaths(mod);
                        foreach (var langRoot in modLangRoots)
                        {
                            string[] searchDirs = targetFolderFilter != null
                                ? (System.IO.Directory.Exists(System.IO.Path.Combine(langRoot, targetFolderFilter)) ? new[] { System.IO.Path.Combine(langRoot, targetFolderFilter) } : new string[0])
                                : System.IO.Directory.GetDirectories(langRoot);

                            foreach (var dir in searchDirs)
                            {
                                var allXmls = System.IO.Directory.GetFiles(dir, "*.xml", System.IO.SearchOption.AllDirectories);
                                foreach (var file in allXmls) filesToScan.Add((file, mod));
                            }
                        }
                    }

                    // ==================================================
                    // 🚀 第二階段：開始正式開挖並更新進度條！
                    // ==================================================
                    int totalFiles = filesToScan.Count;
                    if (totalFiles == 0) goto SearchDone; // 沒東西就直接結束

                    for (int i = 0; i < totalFiles; i++)
                    {
                        var item = filesToScan[i];

                        // ✨ 實時更新進度條的百分比！
                        _globalSearchProgress = (float)i / totalFiles;

                        var dict = AutoTranslatorScanner.LoadXmlFileToDict(item.FilePath);
                        foreach (var kv in dict)
                        {
                            if ((kv.Value != null && kv.Value.ToLower().Contains(searchLower)) ||
                                (kv.Key != null && kv.Key.ToLower().Contains(searchLower)))
                            {
                                if (!results.Any(r => r.Mod == item.Mod && r.Key == kv.Key))
                                {
                                    results.Add(new GlobalSearchResult { Mod = item.Mod, Key = kv.Key, TranslatedText = kv.Value });
                                }
                                if (results.Count >= 200) goto SearchDone;
                            }
                        }
                    }

                SearchDone:
                    // 確保結束時進度條是滿的
                    _globalSearchProgress = 1f;

                    ATC_Dispatcher.RunOnMainThread(() => {
                        _globalSearchResults = results;
                        _isGlobalSearching = false;
                    });
                });
            }
            public static void DrawEditorTab(Verse.Listing_Standard l, UnityEngine.Rect viewRect)
            {
                InitTranslatedModsCache();

                float contentHeight = 600f;
                float leftWidth = 250f;
                float spacing = 15f;
                float rightWidth = viewRect.width - leftWidth - spacing;

                UnityEngine.Rect fullRect = l.GetRect(contentHeight);
                UnityEngine.Rect leftOutRect = new UnityEngine.Rect(fullRect.x, fullRect.y, leftWidth, contentHeight);
                UnityEngine.Rect rightOutRect = new UnityEngine.Rect(fullRect.x + leftWidth + spacing, fullRect.y, rightWidth, contentHeight);

                Verse.Widgets.DrawBoxSolid(leftOutRect, new UnityEngine.Color(0.1f, 0.1f, 0.1f, 0.5f));
                Verse.Widgets.DrawBoxSolid(rightOutRect, new UnityEngine.Color(0.05f, 0.05f, 0.05f, 0.5f));

                // ==========================================
                // 狀態 A：還沒選擇模組，顯示「模組搜尋列表」
                // ==========================================
                if (_editingMod == null)
                {
                    UnityEngine.Rect searchRect = new UnityEngine.Rect(leftOutRect.x + 5f, leftOutRect.y + 5f, leftOutRect.width - 10f, 30f);
                    _modSearchText = Verse.Widgets.TextField(searchRect, _modSearchText);
                    if (string.IsNullOrEmpty(_modSearchText))
                    {
                        UnityEngine.GUI.color = UnityEngine.Color.gray;
                        Verse.Widgets.Label(new UnityEngine.Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_Workbench_SearchMod".Translate());
                        UnityEngine.GUI.color = UnityEngine.Color.white;
                    }

                    UnityEngine.Rect filterRect = new UnityEngine.Rect(leftOutRect.x + 5f, leftOutRect.y + 40f, leftOutRect.width - 10f, 24f);
                    Verse.Widgets.CheckboxLabeled(filterRect, "ATC_Workbench_ShowTranslatedOnly".Translate(), ref _showOnlyTranslated);

                    Verse.Widgets.DrawLineHorizontal(leftOutRect.x, leftOutRect.y + 70f, leftOutRect.width);

                    // ✨ 架構師極速優化：直接呼叫剛才寫好的全域快取，瞬間省下數千次硬碟讀取！
                    IEnumerable<Verse.ModMetaData> allMods = AutoTranslatorMod.GetValidModsCached();

                    if (_showOnlyTranslated)
                        allMods = allMods.Where(m => _translatedPackageIds.Contains(m.PackageId));

                    if (!string.IsNullOrEmpty(_modSearchText))
                    {
                        string searchLower = _modSearchText.ToLower();
                        allMods = allMods.Where(m => m.Name.ToLower().Contains(searchLower) || m.PackageId.ToLower().Contains(searchLower));
                    }

                    // 因為 GetValidModsCached() 已經處理過 OrderBy 了，這裡直接轉 List 即可，省下龐大的算力！
                    var displayMods = allMods.ToList();
                    UnityEngine.Rect listOutRect = new UnityEngine.Rect(leftOutRect.x, leftOutRect.y + 75f, leftOutRect.width, leftOutRect.height - 75f);
                    UnityEngine.Rect listViewRect = new UnityEngine.Rect(0, 0, listOutRect.width - 20f, displayMods.Count * 35f);

                    Verse.Widgets.BeginScrollView(listOutRect, ref _modListScroll, listViewRect);
                    try
                    {
                        float modY = 0f;
                        foreach (var mod in displayMods)
                        {
                            UnityEngine.Rect rowRect = new UnityEngine.Rect(5f, modY, listViewRect.width - 5f, 30f);
                            Verse.Widgets.DrawHighlightIfMouseover(rowRect);

                            if (Verse.Widgets.ButtonInvisible(rowRect))
                            {
                                _editingMod = mod;
                                _isLoading = true;
                                _itemSearchText = "";
                                System.Threading.Tasks.Task.Run(() => LoadRealData(mod));
                            }

                            bool isTrans = _translatedPackageIds.Contains(mod.PackageId);
                            UnityEngine.GUI.color = isTrans ? new UnityEngine.Color(0.6f, 1f, 0.6f) : UnityEngine.Color.white;

                            Verse.Text.WordWrap = false;
                            Verse.Widgets.Label(rowRect, $"{(isTrans ? "✓ " : "")}{mod.Name}");
                            Verse.Text.WordWrap = true;

                            UnityEngine.GUI.color = UnityEngine.Color.white;
                            modY += 35f;
                        }
                    }
                    finally
                    {
                        Verse.Widgets.EndScrollView();
                    }

                    // --- 右側：全域搜尋引擎面板 ---
                    Verse.Text.Font = Verse.GameFont.Medium;
                    string searchTitle = "ATC_Workbench_GlobalSearchTitle".CanTranslate() ? "ATC_Workbench_GlobalSearchTitle".Translate().ToString() : "🔍 全域文本搜尋 (Global Search)";
                    Verse.Widgets.Label(new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 5f, rightOutRect.width, 30f), searchTitle);
                    Verse.Text.Font = Verse.GameFont.Small;

                    // 搜尋輸入框
                    UnityEngine.Rect searchBoxRect = new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 40f, rightOutRect.width - 240f, 30f);
                    _globalSearchText = Verse.Widgets.TextField(searchBoxRect, _globalSearchText);

                    // 語系過濾器按鈕 (下拉選單)
                    UnityEngine.Rect langFilterRect = new UnityEngine.Rect(rightOutRect.xMax - 220f, rightOutRect.y + 40f, 110f, 30f);
                    string filterLabel = _globalSearchLangFilter.HasValue ? _globalSearchLangFilter.Value.ToString() : ("ATC_Lang_All".CanTranslate() ? "ATC_Lang_All".Translate().ToString() : "🌍 All");
                    if (Verse.Widgets.ButtonText(langFilterRect, filterLabel))
                    {
                        var opts = new List<Verse.FloatMenuOption>();
                        opts.Add(new Verse.FloatMenuOption("ATC_Lang_All".CanTranslate() ? "ATC_Lang_All".Translate().ToString() : "🌍 All Languages", () => _globalSearchLangFilter = null));
                        foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                        {
                            TargetLanguage captureLang = lang; // 必須捕捉閉包
                            opts.Add(new Verse.FloatMenuOption(captureLang.ToString(), () => _globalSearchLangFilter = captureLang));
                        }
                        Verse.Find.WindowStack.Add(new Verse.FloatMenu(opts));
                    }

                    // 搜尋按鈕
                    UnityEngine.GUI.color = new UnityEngine.Color(0.4f, 0.8f, 1f);
                    string searchBtnLabel = "ATC_Btn_Search".CanTranslate() ? "ATC_Btn_Search".Translate().ToString() : "搜尋";
                    if (Verse.Widgets.ButtonText(new UnityEngine.Rect(rightOutRect.xMax - 100f, rightOutRect.y + 40f, 90f, 30f), searchBtnLabel))
                    {
                        if (!string.IsNullOrWhiteSpace(_globalSearchText) && !_isGlobalSearching)
                        {
                            ExecuteGlobalSearch(_globalSearchText, _globalSearchLangFilter);
                        }
                    }
                    UnityEngine.GUI.color = UnityEngine.Color.white;

                    Verse.Widgets.DrawLineHorizontal(rightOutRect.x, rightOutRect.y + 80f, rightOutRect.width);

                    if (_isGlobalSearching)
                    {
                        Verse.Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;

                        // 定義一塊用來放提示文字與進度條的置中區域
                        UnityEngine.Rect loadingArea = new UnityEngine.Rect(rightOutRect.x, rightOutRect.y + 90f, rightOutRect.width, 80f);

                        UnityEngine.GUI.color = UnityEngine.Color.yellow;
                        string loadingMsg = "ATC_Workbench_GlobalSearching".CanTranslate() ? "ATC_Workbench_GlobalSearching".Translate().ToString() : "🔄 正在背景高速掃描快取...";
                        Verse.Widgets.Label(new UnityEngine.Rect(loadingArea.x, loadingArea.y, loadingArea.width, 30f), loadingMsg);
                        UnityEngine.GUI.color = UnityEngine.Color.white;

                        // ✨ 繪製邊緣世界官方樣式的 FillableBar (置中顯示，長度佔寬度 60%)
                        float barWidth = loadingArea.width * 0.6f;
                        UnityEngine.Rect barRect = new UnityEngine.Rect(loadingArea.x + (loadingArea.width - barWidth) / 2f, loadingArea.y + 35f, barWidth, 22f);
                        Verse.Widgets.FillableBar(barRect, _globalSearchProgress);

                        // ✨ 疊加百分比文字在進度條正中央
                        Verse.Text.Font = Verse.GameFont.Tiny;
                        Verse.Widgets.Label(barRect, $"{(_globalSearchProgress * 100f):F0}%");
                        Verse.Text.Font = Verse.GameFont.Small;

                        Verse.Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
                    }
                    else if (_globalSearchResults.Count > 0)
                    {
                        UnityEngine.Rect resOutRect = new UnityEngine.Rect(rightOutRect.x, rightOutRect.y + 85f, rightOutRect.width, rightOutRect.height - 85f);
                        UnityEngine.Rect resViewRect = new UnityEngine.Rect(0, 0, resOutRect.width - 20f, _globalSearchResults.Count * 65f);

                        Verse.Widgets.BeginScrollView(resOutRect, ref _globalSearchScroll, resViewRect);
                        float resY = 0f;
                        foreach (var res in _globalSearchResults)
                        {
                            UnityEngine.Rect rowRect = new UnityEngine.Rect(5f, resY, resViewRect.width - 5f, 60f);
                            Verse.Widgets.DrawBoxSolid(rowRect, new UnityEngine.Color(0.15f, 0.15f, 0.15f, 0.8f));
                            Verse.Widgets.DrawHighlightIfMouseover(rowRect);

                            // ✨ 點擊結果，直接載入該模組進入編輯模式！
                            if (Verse.Widgets.ButtonInvisible(rowRect))
                            {
                                _editingMod = res.Mod;
                                _isLoading = true;
                                _itemSearchText = res.TranslatedText;
                                System.Threading.Tasks.Task.Run(() => LoadRealData(res.Mod));
                            }

                            Verse.Text.Font = Verse.GameFont.Tiny;
                            UnityEngine.GUI.color = UnityEngine.Color.gray;
                            Verse.Widgets.Label(new UnityEngine.Rect(rowRect.x + 5f, rowRect.y + 2f, rowRect.width, 15f), $"[{res.Mod.Name}] {res.Key}");

                            Verse.Text.Font = Verse.GameFont.Small;
                            UnityEngine.GUI.color = UnityEngine.Color.white;
                            Verse.Widgets.Label(new UnityEngine.Rect(rowRect.x + 5f, rowRect.y + 20f, rowRect.width - 10f, 35f), res.TranslatedText);

                            resY += 65f;
                        }
                        Verse.Widgets.EndScrollView();
                    }
                    else if (!string.IsNullOrWhiteSpace(_globalSearchText))
                    {
                        Verse.Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
                        UnityEngine.GUI.color = UnityEngine.Color.gray;
                        string notFoundMsg = "ATC_Workbench_GlobalSearchNoResult".CanTranslate() ? "ATC_Workbench_GlobalSearchNoResult".Translate().ToString() : "找不到相符的翻譯文本。";
                        Verse.Widgets.Label(new UnityEngine.Rect(rightOutRect.x, rightOutRect.y + 90f, rightOutRect.width, 100f), notFoundMsg);
                        UnityEngine.GUI.color = UnityEngine.Color.white;
                        Verse.Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
                    }
                }
                // ==========================================
                // 狀態 B：已選擇模組，進入「編輯工作台」
                // ==========================================
                else
                {
                    UnityEngine.Rect backBtnRect = new UnityEngine.Rect(leftOutRect.x + 5f, leftOutRect.y + 5f, leftOutRect.width - 10f, 35f);
                    UnityEngine.GUI.color = new UnityEngine.Color(1f, 0.7f, 0.7f);
                    if (Verse.Widgets.ButtonText(backBtnRect, "🔙 " + "ATC_Workbench_BackToList".Translate()))
                    {
                        _editingMod = null;
                        _categorizedData.Clear();
                        InitTranslatedModsCache();
                        return; // ✨ 加上這一行：立刻中斷 UI 繪製，防止下方程式碼報錯！
                    }
                    UnityEngine.GUI.color = UnityEngine.Color.white;

                    Verse.Widgets.DrawLineHorizontal(leftOutRect.x, leftOutRect.y + 45f, leftOutRect.width);

                    if (_isLoading)
                    {
                        Verse.Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
                        Verse.Widgets.Label(new UnityEngine.Rect(leftOutRect.x, leftOutRect.y + 50f, leftOutRect.width, 100f), "🔄 " + "ATC_UploadPreview_Loading".Translate());
                        Verse.Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
                    }
                    else
                    {
                        UnityEngine.Rect catOutRect = new UnityEngine.Rect(leftOutRect.x, leftOutRect.y + 50f, leftOutRect.width, leftOutRect.height - 50f);
                        UnityEngine.Rect catViewRect = new UnityEngine.Rect(0, 0, catOutRect.width - 20f, _categorizedData.Count * 35f);
                        Verse.Widgets.BeginScrollView(catOutRect, ref _catListScroll, catViewRect);
                        try
                        {
                            float curY = 0f;
                            foreach (var category in _categorizedData.Keys)
                            {
                                UnityEngine.Rect rowRect = new UnityEngine.Rect(5f, curY, catViewRect.width - 5f, 30f);
                                if (_selectedCategory == category) Verse.Widgets.DrawHighlightSelected(rowRect);
                                else Verse.Widgets.DrawHighlightIfMouseover(rowRect);

                                if (Verse.Widgets.ButtonInvisible(rowRect))
                                {
                                    _selectedCategory = category;
                                    _itemScroll = UnityEngine.Vector2.zero;
                                }

                                Verse.Widgets.Label(rowRect, $"{category} ({_categorizedData[category].Count})");
                                curY += 35f;
                            }
                        }
                        finally
                        {
                            Verse.Widgets.EndScrollView();
                        }
                    }

                    UnityEngine.Rect headerRect = new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 5f, rightOutRect.width - 20f, 30f);
                    Verse.Text.Font = Verse.GameFont.Medium;
                    Verse.Widgets.Label(headerRect, _editingMod.Name);
                    Verse.Text.Font = Verse.GameFont.Small;

                    UnityEngine.Rect itemSearchRect = new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 40f, rightOutRect.width - 160f, 30f);
                    _itemSearchText = Verse.Widgets.TextField(itemSearchRect, _itemSearchText);
                    if (string.IsNullOrEmpty(_itemSearchText))
                    {
                        UnityEngine.GUI.color = UnityEngine.Color.gray;
                        Verse.Widgets.Label(new UnityEngine.Rect(itemSearchRect.x + 5f, itemSearchRect.y + 2f, itemSearchRect.width, itemSearchRect.height), "🔍 " + "ATC_Workbench_SearchHint".Translate());
                        UnityEngine.GUI.color = UnityEngine.Color.white;
                    }

                    UnityEngine.Rect saveBtnRect = new UnityEngine.Rect(rightOutRect.xMax - 140f, rightOutRect.y + 40f, 130f, 30f);
                    UnityEngine.GUI.color = new UnityEngine.Color(0.4f, 1f, 0.4f);
                    if (Verse.Widgets.ButtonText(saveBtnRect, "💾 " + "ATC_Workbench_SaveBtn".Translate()))
                    {
                        SaveModifications();
                        Verse.Messages.Message("ATC_Workbench_SaveSuccess".Translate(), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                    }
                    UnityEngine.GUI.color = UnityEngine.Color.white;

                    Verse.Widgets.DrawLineHorizontal(rightOutRect.x, rightOutRect.y + 80f, rightOutRect.width);

                    if (!_isLoading && !string.IsNullOrEmpty(_selectedCategory) && _categorizedData.ContainsKey(_selectedCategory))
                    {
                        var items = _categorizedData[_selectedCategory];
                        if (!string.IsNullOrEmpty(_itemSearchText))
                        {
                            string searchLower = _itemSearchText.ToLower();
                            items = items.Where(i =>
                                (i.OriginalText != null && i.OriginalText.ToLower().Contains(searchLower)) ||
                                (i.TranslatedText != null && i.TranslatedText.ToLower().Contains(searchLower)) ||
                                (i.Key != null && i.Key.ToLower().Contains(searchLower))
                            ).ToList();
                        }

                        float rowHeight = 90f;
                        UnityEngine.Rect itemsOutRect = new UnityEngine.Rect(rightOutRect.x, rightOutRect.y + 85f, rightOutRect.width, rightOutRect.height - 85f);
                        UnityEngine.Rect itemsViewRect = new UnityEngine.Rect(0, 0, itemsOutRect.width - 20f, items.Count * rowHeight);
                        Verse.Widgets.BeginScrollView(itemsOutRect, ref _itemScroll, itemsViewRect);

                        try
                        {
                            float editY = 0f;
                            float halfWidth = (itemsViewRect.width - 10f) / 2f;

                            foreach (var item in items)
                            {
                                UnityEngine.Rect itemRect = new UnityEngine.Rect(5f, editY, itemsViewRect.width - 10f, rowHeight - 5f);
                                Verse.Widgets.DrawHighlightIfMouseover(itemRect);

                                Verse.Text.Font = Verse.GameFont.Tiny;
                                UnityEngine.GUI.color = UnityEngine.Color.gray;
                                Verse.Widgets.Label(new UnityEngine.Rect(itemRect.x, itemRect.y, itemRect.width, 15f), item.Key);

                                Verse.Text.Font = Verse.GameFont.Small;
                                UnityEngine.GUI.color = new UnityEngine.Color(0.8f, 0.8f, 0.8f);
                                UnityEngine.Rect originalRect = new UnityEngine.Rect(itemRect.x, itemRect.y + 15f, halfWidth - 5f, itemRect.height - 15f);

                                // 🛡️ 終極防彈裝甲：防止毒瘤原文摧毀 UI
                                try { Verse.Widgets.Label(originalRect, item.OriginalText ?? ""); }
                                catch { Verse.Widgets.Label(originalRect, "[Error: Invalid Rich Text]"); }

                                UnityEngine.GUI.color = UnityEngine.Color.white;
                                UnityEngine.Rect transRect = new UnityEngine.Rect(itemRect.x + halfWidth + 5f, itemRect.y + 15f, halfWidth - 5f, itemRect.height - 15f);

                                // 🛡️ 終極防彈裝甲：防止玩家輸入毒瘤文字
                                string newText = item.TranslatedText ?? "";
                                try { newText = Verse.Widgets.TextArea(transRect, newText); }
                                catch { newText = Verse.Widgets.TextField(transRect, newText); }

                                if (newText != item.TranslatedText)
                                {
                                    item.TranslatedText = newText;
                                    item.IsModified = true;
                                }

                                editY += rowHeight;
                            }
                        }
                        finally
                        {
                            Verse.Widgets.EndScrollView();
                        }
                    }
                }
            }

            private static void LoadRealData(Verse.ModMetaData targetMod)
            {
                var resultData = new Dictionary<string, List<WorkbenchItem>>();
                var langRoots = AutoTranslatorScanner.GetAllEffectiveLangPaths(targetMod);
                var defsRoots = AutoTranslatorScanner.GetAllEffectiveDefsPaths(targetMod);

                // ✨ 咪咪修復：讀取時也必須使用 TargetLang！
                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);

                var engKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var transKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var langRoot in langRoots)
                {
                    string engKeyedPath = System.IO.Path.Combine(langRoot, "English", "Keyed");
                    if (System.IO.Directory.Exists(engKeyedPath))
                    {
                        var dict = AutoTranslatorScanner.LoadXmlFilesToDict(engKeyedPath);
                        foreach (var kv in dict) engKeyed[kv.Key] = kv.Value;
                    }
                    string modTransKeyedPath = System.IO.Path.Combine(langRoot, targetLangFolder, "Keyed");
                    if (System.IO.Directory.Exists(modTransKeyedPath))
                    {
                        var dict = AutoTranslatorScanner.LoadXmlFilesToDict(modTransKeyedPath);
                        foreach (var kv in dict) transKeyed[kv.Key] = kv.Value;
                    }
                }

                string packKeyedDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder, "Keyed");
                if (System.IO.Directory.Exists(packKeyedDir))
                {
                    string idMatch = targetMod.PackageId.Replace(".", "_").ToLower();
                    foreach (var file in System.IO.Directory.GetFiles(packKeyedDir, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        if (System.IO.Path.GetFileName(file).ToLower().Contains(idMatch))
                        {
                            var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                            foreach (var kv in d) transKeyed[kv.Key] = kv.Value;
                        }
                    }
                }

                string workspaceKeyedDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Upload_Workspace", targetMod.PackageId, targetLangFolder, "Keyed");
                if (System.IO.Directory.Exists(workspaceKeyedDir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(workspaceKeyedDir, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                        foreach (var kv in d) transKeyed[kv.Key] = kv.Value;
                    }
                }

                if (engKeyed.Count > 0)
                {
                    var list = new List<WorkbenchItem>();
                    foreach (var kv in engKeyed)
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = transKeyed.ContainsKey(kv.Key) ? transKeyed[kv.Key] : "" });
                    resultData["Keyed"] = list;
                }

                var engDefs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var transDefs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var defRoot in defsRoots)
                {
                    var dict = AutoTranslatorScanner.ExtractEnglishFromRawDefs(defRoot);
                    foreach (var typeKv in dict)
                    {
                        if (!engDefs.ContainsKey(typeKv.Key)) engDefs[typeKv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in typeKv.Value) engDefs[typeKv.Key][kv.Key] = kv.Value;
                    }
                }

                string packDefDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder, "DefInjected");
                if (System.IO.Directory.Exists(packDefDir))
                {
                    foreach (var typeDir in System.IO.Directory.GetDirectories(packDefDir))
                    {
                        string defType = System.IO.Path.GetFileName(typeDir);
                        foreach (var file in System.IO.Directory.GetFiles(typeDir, "*.xml"))
                        {
                            if (System.IO.Path.GetFileName(file).ToLower().Contains(targetMod.PackageId.Replace(".", "_").ToLower()))
                            {
                                if (!transDefs.ContainsKey(defType)) transDefs[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                foreach (var kv in d) transDefs[defType][kv.Key] = kv.Value;
                            }
                        }
                    }
                }

                string workspaceDefDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Upload_Workspace", targetMod.PackageId, targetLangFolder, "DefInjected");
                if (System.IO.Directory.Exists(workspaceDefDir))
                {
                    foreach (var typeDir in System.IO.Directory.GetDirectories(workspaceDefDir))
                    {
                        string defType = System.IO.Path.GetFileName(typeDir);
                        if (!transDefs.ContainsKey(defType)) transDefs[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in System.IO.Directory.GetFiles(typeDir, "*.xml"))
                        {
                            var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                            foreach (var kv in d) transDefs[defType][kv.Key] = kv.Value;
                        }
                    }
                }

                foreach (var typeKv in engDefs)
                {
                    string defType = typeKv.Key;
                    var list = new List<WorkbenchItem>();
                    foreach (var kv in typeKv.Value)
                    {
                        string translated = "";
                        if (transDefs.ContainsKey(defType) && transDefs[defType].ContainsKey(kv.Key))
                            translated = transDefs[defType][kv.Key];
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = translated });
                    }
                    if (list.Count > 0) resultData[defType] = list;
                }

                ATC_Dispatcher.RunOnMainThread(() => {
                    _categorizedData = resultData;
                    _selectedCategory = _categorizedData.Keys.FirstOrDefault() ?? "";
                    _isLoading = false;
                });
            }

            private static void SaveModifications()
            {
                if (_editingMod == null) return;

                // ✨ 咪咪修復：存檔時也必須使用 TargetLang！
                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string cleanPackageId = _editingMod.PackageId.Replace(".", "_").ToLower();
                string workspaceBaseDir = System.IO.Path.Combine(packPath, "Upload_Workspace");
                int savedCount = 0;

                foreach (var categoryPair in _categorizedData)
                {
                    string category = categoryPair.Key;
                    string targetDir = category == "Keyed"
                        ? System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "DefInjected", category);
                    string workspaceDir = category == "Keyed"
                        ? System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "DefInjected", category);

                    System.IO.Directory.CreateDirectory(targetDir);
                    System.IO.Directory.CreateDirectory(workspaceDir);

                    foreach (var oldFile in System.IO.Directory.GetFiles(workspaceDir, "*.xml")) System.IO.File.Delete(oldFile);
                    foreach (var oldFile in System.IO.Directory.GetFiles(targetDir, "*.xml"))
                    {
                        if (System.IO.Path.GetFileName(oldFile).ToLower().Contains(cleanPackageId))
                        {
                            System.IO.File.SetAttributes(oldFile, System.IO.FileAttributes.Normal);
                            System.IO.File.Delete(oldFile);
                        }
                    }

                    string targetFile = System.IO.Path.Combine(targetDir, $"{cleanPackageId}_AutoTranslated.xml");
                    string workspaceFile = System.IO.Path.Combine(workspaceDir, $"{cleanPackageId}_AutoTranslated.xml");

                    Dictionary<string, string> fullDictToSave = new Dictionary<string, string>();
                    foreach (var item in categoryPair.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(item.TranslatedText)) fullDictToSave[item.Key] = item.TranslatedText;
                        if (item.IsModified) { item.IsModified = false; savedCount++; }
                    }

                    if (fullDictToSave.Count > 0)
                    {
                        AutoTranslatorScanner.SaveXml(targetFile, fullDictToSave);
                        AutoTranslatorScanner.SaveXml(workspaceFile, fullDictToSave);
                    }
                }

                AutoTranslatorSettings.AddLog("💾 " + "ATC_Log_WorkbenchSaved".Translate(savedCount));
                AutoTranslatorScanner.RequestMemoryDrop();
                UIInterceptor.ClearUICache();

                InitTranslatedModsCache();
                _translatedPackageIds.Add(_editingMod.PackageId);
            }
        }
    }
        // =====================================================================
        // 🚀 ATC V5.2 全新視窗：上傳安檢預覽、編輯與更新日誌編寫綜合控制台
        // =====================================================================
        public class Window_UploadPreview : Window
        {
            private ModMetaData _mod;
            private string _targetLangFolder;
            private string _sourceDir;
            private string _modName;
            private Vector2 _leftScrollPos = Vector2.zero;
            private Vector2 _rightScrollPos = Vector2.zero;
            private bool _isLoading = true;
            private bool _isEditable = false; // ✨ 控制左右兩側預設不可修改的狀態開關

            private class PreviewItem
            {
                public string Key;
                public string OriginalText;
                public string TranslatedText;
                public bool IsModified;
            }

            private Dictionary<string, List<PreviewItem>> _categorizedData = new Dictionary<string, List<PreviewItem>>();
            private string _selectedCategory = "";
            private string _updateLogText = ""; // ✨ 用來裝更新說明的暫存字串

            public override Vector2 InitialSize => new Vector2(1000f, 780f);

            public Window_UploadPreview(ModMetaData mod, string targetLangFolder, string sourceDir, string modName)
            {
                _mod = mod;
                _targetLangFolder = targetLangFolder;
                _sourceDir = sourceDir;
                _modName = modName;
                this.doCloseButton = false;
                this.doCloseX = true;
                this.forcePause = true;
                this.absorbInputAroundWindow = true;

                System.Threading.Tasks.Task.Run(() => LoadPreviewData());
            }

            private void LoadPreviewData()
            {
                var resultData = new Dictionary<string, List<PreviewItem>>();

                // ✨ 咪咪雙重判定雷達
                string id1 = _mod.PackageId.ToLower();
                string id2 = _mod.PackageId.Replace(".", "_").ToLower();
                // 如果是從專屬工作室來的，無條件全部放行，不檢查檔名！
                bool isWorkspace = _sourceDir.Contains("Upload_Workspace");

                if (Directory.Exists(_sourceDir))
                {
                    // 1. 解析 Keyed 類別
                    string keyedDir = Path.Combine(_sourceDir, "Keyed");
                    if (Directory.Exists(keyedDir))
                    {
                        var list = new List<PreviewItem>();
                        foreach (var file in Directory.GetFiles(keyedDir, "*.xml", SearchOption.AllDirectories))
                        {
                            string fileName = Path.GetFileName(file).ToLower();
                            bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                            if (isValid)
                            {
                                var dict = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                foreach (var kv in dict)
                                    list.Add(new PreviewItem { Key = kv.Key, OriginalText = "ATC_Preview_ClickToSee".Translate(), TranslatedText = kv.Value });
                            }
                        }
                        if (list.Count > 0) resultData["Keyed"] = list;
                    }

                    // 2. 解析 DefInjected 類別
                    string defBaseDir = Path.Combine(_sourceDir, "DefInjected");
                    if (Directory.Exists(defBaseDir))
                    {
                        foreach (var typeDir in Directory.GetDirectories(defBaseDir))
                        {
                            string defType = Path.GetFileName(typeDir);
                            var list = new List<PreviewItem>();

                            // 🌟 關鍵修復：加入 SearchOption.AllDirectories，就算有 100 層子資料夾也照樣挖出來！
                            foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                            {
                                string fileName = Path.GetFileName(file).ToLower();
                                bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                                if (isValid)
                                {
                                    var dict = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                    foreach (var kv in dict)
                                        list.Add(new PreviewItem { Key = kv.Key, OriginalText = "ATC_Preview_ClickToSee".Translate(), TranslatedText = kv.Value });
                                }
                            }
                            if (list.Count > 0) resultData[defType] = list;
                        }
                    }
                }

                ATC_Dispatcher.RunOnMainThread(() => {
                    _categorizedData = resultData;
                    _selectedCategory = _categorizedData.Keys.FirstOrDefault() ?? "";
                    _isLoading = false;
                });
            }
            public override void DoWindowContents(Rect inRect)
            {
            // 🛡️ 咪咪的免死金牌：預覽畫面也必須保護原文！
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 35f), "🔍 " + "ATC_UploadPreview_Title".Translate(_mod.Name));
                Text.Font = GameFont.Small;
                Widgets.DrawLineHorizontal(0, 35f, inRect.width);

                if (_isLoading)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0, 0, inRect.width, inRect.height), "🔄 " + "ATC_UploadPreview_Loading".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    return;
                }
                // ✨ 加上防呆提示：如果過濾完發現根本沒有這個模組的翻譯
                if (_categorizedData.Count == 0)
                {
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0, 0, inRect.width, inRect.height), "⚠️ " + "ATC_UploadPreview_NoTranslation".Translate()); Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;

                    // 畫一個大大的取消按鈕讓他離開
                    if (Widgets.ButtonText(new Rect(inRect.width / 2f - 75f, inRect.height - 60f, 150f, 40f), "ATC_Btn_Cancel".Translate()))
                    {
                        this.Close();
                    }
                    return; // 🛡️ 直接提早結束，不畫底下的上傳按鈕，徹底防止空包彈！
                }
                // 頂部核心配置區
                float topOffset = 45f;
                float leftWidth = 220f;
                float spacing = 15f;
                float rightWidth = inRect.width - leftWidth - spacing;
                float contentHeight = inRect.height - topOffset - 150f; // 留 150px 給底部的更新日誌輸入格與按鈕

                Rect leftOutRect = new Rect(0, topOffset, leftWidth, contentHeight);
                Rect rightOutRect = new Rect(leftWidth + spacing, topOffset, rightWidth, contentHeight);

                // 👈 左側：分類選單
                Widgets.DrawBoxSolid(leftOutRect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
                Rect leftViewRect = new Rect(0, 0, leftOutRect.width - 20f, _categorizedData.Count * 35f);
                Widgets.BeginScrollView(leftOutRect, ref _leftScrollPos, leftViewRect);
                float curY = 0f;
                foreach (var category in _categorizedData.Keys)
                {
                    Rect rowRect = new Rect(5f, curY, leftViewRect.width - 5f, 30f);
                    if (_selectedCategory == category) Widgets.DrawHighlightSelected(rowRect);
                    else Widgets.DrawHighlightIfMouseover(rowRect);

                    if (Widgets.ButtonInvisible(rowRect)) { _selectedCategory = category; _rightScrollPos = Vector2.zero; }
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(rowRect, $"{category} ({_categorizedData[category].Count})");
                    Text.Anchor = TextAnchor.UpperLeft;
                    curY += 35f;
                }
                Widgets.EndScrollView();

                // 👉 右側：清單檢視區
                Widgets.DrawBoxSolid(rightOutRect, new Color(0.05f, 0.05f, 0.05f, 0.5f));
                if (!string.IsNullOrEmpty(_selectedCategory) && _categorizedData.ContainsKey(_selectedCategory))
                {
                    var items = _categorizedData[_selectedCategory];
                    float rowHeight = 70f;
                    Rect rightViewRect = new Rect(0, 0, rightOutRect.width - 20f, items.Count * rowHeight);
                    Widgets.BeginScrollView(rightOutRect, ref _rightScrollPos, rightViewRect);

                    float editY = 0f;
                    foreach (var item in items)
                    {
                        Rect itemRect = new Rect(5f, editY, rightViewRect.width - 10f, rowHeight - 5f);
                        Widgets.DrawHighlightIfMouseover(itemRect);

                        Text.Font = GameFont.Tiny;
                        GUI.color = Color.gray;
                        Widgets.Label(new Rect(itemRect.x, itemRect.y, itemRect.width, 15f), item.Key);

                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                        Rect transRect = new Rect(itemRect.x, itemRect.y + 15f, itemRect.width, itemRect.height - 15f);

                        // ✨ 智慧鎖：根據 _isEditable 旗標決定玩家能不能直接打字修改
                        if (_isEditable)
                        {
                            string newText = Widgets.TextArea(transRect, item.TranslatedText ?? "");
                            if (newText != item.TranslatedText) { item.TranslatedText = newText; item.IsModified = true; }
                        }
                        else
                        {
                            Widgets.Label(transRect, item.TranslatedText);
                        }
                        editY += rowHeight;
                    }
                    GUI.color = Color.white;
                    Widgets.EndScrollView();
                }

                // 📜 底部一整橫條：Steam 口味「更新日誌 / 填寫說明區」
                float logAreaY = topOffset + contentHeight + 10f;
                Rect logLabelRect = new Rect(0, logAreaY, inRect.width, 22f);
                Widgets.Label(logLabelRect, "📝 " + "ATC_Upload_LogLabel".Translate());

                Rect logInputRect = new Rect(0, logAreaY + 22f, inRect.width, 55f);
                _updateLogText = Widgets.TextArea(logInputRect, _updateLogText);
                if (string.IsNullOrEmpty(_updateLogText))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(logInputRect.x + 5f, logInputRect.y + 2f, logInputRect.width, logInputRect.height), "ATC_Upload_LogHint".Translate());
                    GUI.color = Color.white;
                }

                // 🔘 最底部：三顆經典交互按鈕
                float btnY = inRect.height - 35f;

                // 按鈕一 (最左邊)：取消上傳
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (Widgets.ButtonText(new Rect(0, btnY, 130f, 35f), "ATC_Btn_Cancel".Translate()))
                {
                    this.Close();
                }

                // 按鈕二 (中間)：修改內容
                if (_isEditable) GUI.color = Color.yellow;
                else GUI.color = new Color(0.7f, 0.7f, 1f);
                if (Widgets.ButtonText(new Rect(145f, btnY, 150f, 35f), _isEditable ? "✍️ " + "ATC_Upload_EditingMode".Translate() : "⚙️ " + "ATC_Upload_UnlockEdit".Translate()))
                {
                    _isEditable = !_isEditable; // 切換解鎖狀態
                }

                // 按鈕三 (最右邊)：安全檢查完畢，發射上傳！
                bool isAdmin = !string.IsNullOrEmpty(AutoTranslatorMod.Settings.CloudAdminToken);
                bool hasValidLog = !string.IsNullOrWhiteSpace(_updateLogText) && _updateLogText.Trim().Length >= 5;
                bool canUpload = isAdmin || hasValidLog; // 🌟 咪咪防禦網：沒有特權的普通玩家，必須乖乖寫滿 5 個字的更新日誌！

                GUI.color = canUpload ? new Color(0.4f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);

                if (Widgets.ButtonText(new Rect(inRect.width - 180f, btnY, 180f, 35f), "🚀 " + "ATC_Upload_ConfirmUploadBtn".Translate()))
                {
                    if (!canUpload)
                    {
                        // 拒絕上傳，並彈出本地化警告！
                        Verse.Messages.Message("ATC_Msg_UploadLogRequired".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    // 如果玩家有就地動手修改，先幫他儲存到磁碟中
                    SaveChangesIfAny();

                    // 喚醒原本的 CloudClient 上傳，並把我們打好的日誌一起打包丟去 Worker 資料庫
                    ExecuteActualUpload();
                    this.Close();
                }
                GUI.color = Color.white;
            }
            finally
            {
                // 🛡️ 收回免死金牌
                Patch_GUI_Label_GUIContent.BypassInterceptor = false;
            }
        }

        private void SaveChangesIfAny()
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                int saveCount = 0;
                foreach (var pair in _categorizedData)
                {
                    var modified = pair.Value.Where(i => i.IsModified).ToList();
                    if (modified.Count == 0) continue;

                    string fileDir = pair.Key == "Keyed"
                        ? Path.Combine(_sourceDir, "Keyed")
                        : Path.Combine(_sourceDir, "DefInjected", pair.Key);

                    Directory.CreateDirectory(fileDir);
                    string targetFile = Path.Combine(fileDir, $"{_mod.PackageId.Replace(".", "_").ToLower()}_AutoTranslated.xml");
                    var existing = AutoTranslatorScanner.LoadXmlFileToDict(targetFile);

                    foreach (var item in modified) { existing[item.Key] = item.TranslatedText; item.IsModified = false; saveCount++; }
                    AutoTranslatorScanner.SaveXml(targetFile, existing);
                }
                if (saveCount > 0) { AutoTranslatorScanner.RequestMemoryDrop(); UIInterceptor.ClearUICache(); }
            }

        private void ExecuteActualUpload()
        {
            Messages.Message("ATC_Msg_UploadStart".Translate(_mod.Name), MessageTypeDefOf.NeutralEvent, false);

            string pkgId = _mod.PackageId; string mLang = _targetLangFolder; string mName = _modName;
            string uNick = AutoTranslatorMod.Settings.CloudNickname; string uType = AutoTranslatorMod.Settings.CloudUploadType;
            string sDir = _sourceDir; string token = AutoTranslatorMod.Settings.CloudAdminToken; string finalLog = _updateLogText;

            System.Threading.Tasks.Task.Run(async () => {
                try
                {
                    if (!Directory.Exists(sDir)) return;
                    string stagingDir = Path.Combine(Path.GetTempPath(), "ATC_Upload_Pre_" + pkgId);
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                    Directory.CreateDirectory(stagingDir);

                    int fileCount = 0;
                    string id1 = pkgId.ToLower();
                    string id2 = pkgId.Replace(".", "_").ToLower();
                    bool isWorkspace = sDir.Contains("Upload_Workspace");

                    foreach (string file in Directory.GetFiles(sDir, "*.xml", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileName(file).ToLower();
                        bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                        if (isValid)
                        {
                            string relPath = file.Substring(sDir.Length).TrimStart('\\', '/');
                            string destPath = Path.Combine(stagingDir, relPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            File.Copy(file, destPath, true);
                            fileCount++;
                        }
                    }

                    if (fileCount == 0)
                    {
                        ATC_Dispatcher.RunOnMainThread(() => {
                            Messages.Message("ATC_Msg_UploadFailedNoFiles".Translate(), MessageTypeDefOf.RejectInput, false);
                        });
                        return;
                    }
                    string tempZipFile = Path.Combine(Path.GetTempPath(), $"{pkgId}_{mLang}_upload.zip");
                    if (File.Exists(tempZipFile)) File.Delete(tempZipFile);

                    // 🛡️ 核心修復 1：ZIP 壓縮強制套用 UTF-8
                    System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, tempZipFile, System.IO.Compression.CompressionLevel.Optimal, false, System.Text.Encoding.UTF8);
                    Directory.Delete(stagingDir, true);

                    byte[] zipBytes = File.ReadAllBytes(tempZipFile);
                    string base64File = Convert.ToBase64String(zipBytes);
                    File.Delete(tempZipFile);

                    string targetModVersion = "Unknown"; DateTime translationDate = DateTime.UtcNow; bool isSmartMerged = false; int mergedAiCount = 0;
                    string metaPath = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", mLang, $"{id2}_ATC_Meta.json");
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JsonConvert.DeserializeObject<LocalModMeta>(File.ReadAllText(metaPath));
                            if (meta != null) { targetModVersion = meta.TargetModVersion; translationDate = meta.TranslationDate; isSmartMerged = meta.IsSmartMerged; mergedAiCount = meta.MergedAiCount; }
                        }
                        catch { }
                    }

                    DateTime actualModUpdate = DateTime.UtcNow;
                    if (_mod.RootDir != null && Directory.Exists(_mod.RootDir.FullName)) actualModUpdate = new DirectoryInfo(_mod.RootDir.FullName).LastWriteTimeUtc;

                    var payload = new
                    {
                        PackageId = pkgId,
                        Language = mLang,
                        ModName = mName,
                        LatestVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild,
                        ModLastUpdated = actualModUpdate.ToString("O"),
                        UploaderID = UnityEngine.SystemInfo.deviceUniqueIdentifier,
                        Author = uNick,
                        TranslationType = uType,
                        FileBase64 = base64File,
                        AdminToken = token,
                        TargetModVersion = targetModVersion,
                        TranslationDate = translationDate.ToString("O"),
                        IsSmartMerged = isSmartMerged,
                        MergedAiCount = mergedAiCount,
                        UpdateLog = finalLog
                    };

                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                    byte[] payloadBytes = tolerantUtf8.GetBytes(jsonPayload);

                    // 🛡️ 核心修復 2：全面換裝 UnityWebRequest 網路引擎
                    var tcs = new TaskCompletionSource<bool>();
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            var request = new UnityEngine.Networking.UnityWebRequest($"{AutoTranslatorCloudClient.CloudApiBaseUrl}/upload", "POST");
                            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(payloadBytes);
                            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                            request.SetRequestHeader("Content-Type", "application/json");
                            request.timeout = 120;

                            var operation = request.SendWebRequest();
                            operation.completed += (op) =>
                            {
                                try
                                {
                                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                                        tcs.TrySetResult(true);
                                    else
                                        tcs.TrySetException(new Exception(request.error));
                                }
                                catch (Exception innerEx) { tcs.TrySetException(innerEx); }
                                finally { request.Dispose(); }
                            };
                        }
                        catch (Exception dispatchEx) { tcs.TrySetException(dispatchEx); }
                    });

                    bool uploadSuccess = false;
                    try { uploadSuccess = await tcs.Task; } catch (Exception ex) { Verse.Log.Error($"[ATC Cloud Preview] Upload Fail: {ex.Message}"); }

                    ATC_Dispatcher.RunOnMainThread(() => {
                        if (uploadSuccess)
                        {
                            Messages.Message("ATC_Msg_UploadSuccess".Translate(mName), MessageTypeDefOf.PositiveEvent, false);
                            AutoTranslatorSettings.HasFetchedCloudThisSession = false;
                        }
                        else
                        {
                            Messages.Message("ATC_Msg_UploadFailed".Translate(mName), MessageTypeDefOf.RejectInput, false);
                        }
                    });

                }
                catch (Exception ex)
                {
                    Verse.Log.Error($"[ATC Cloud Preview] Thread Fail: {ex.Message}");
                }
            });
        }
    }

    }