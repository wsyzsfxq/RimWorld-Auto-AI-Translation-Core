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
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public enum TargetLanguage { Traditional, Simplified, Japanese, Korean, Russian, Ukrainian, English }
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
                default: return lang.ToString();
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // 🛡️ 免死金牌：設定頁面絕不被攔截器影響
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                if (AutoTranslatorSettings.ShowFinishPopup)
                {
                    AutoTranslatorSettings.ShowFinishPopup = false;
                    Find.WindowStack.Add(new Dialog_MessageBox("ATC_FinishMessage_Text".Translate(), "ATC_FinishMessage_OK".Translate(), null, null, null, "ATC_FinishMessage_Title".Translate()));
                }

                // 🌟 V2.2 介面革命：繪製頂部分頁標籤 (Tabs)
                List<TabRecord> tabs = new List<TabRecord>
                {
                    new TabRecord("ATC_Tab_Main".Translate(), () => AutoTranslatorSettings.ActiveTab = 0, AutoTranslatorSettings.ActiveTab == 0),
                    new TabRecord("ATC_Tab_Settings".Translate(), () => AutoTranslatorSettings.ActiveTab = 1, AutoTranslatorSettings.ActiveTab == 1)
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
                else
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
            !IsCodeOnlyMod(m) // ✨ 同步隱形：純代碼模組從 UI 澈底蒸發！
            );
            if (!string.IsNullOrEmpty(searchText))
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
                        string fileName = Path.GetFileName(file).ToLower();
                        // 只要中任何一種命名規則，格殺勿論！
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            File.Delete(file);
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
                        IsCodeOnlyMod(mod))  // ✨ 架構師絕殺：純代碼模組直接在源頭物理消滅，永不進入更新名單！
                        continue;
                    long currentTicks = GetLatestTick(mod);
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

            // 取得所有支援當前版本 (例如 1.4/1.5) 的活動資料夾
            // 使用 RimWorld 內建的 LoadFoldersForVersion，完美相容多版本結構
            var folders = mod.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild);

            // 如果找不到特定版本資料夾，就使用預設的根目錄
            if (folders == null || !folders.Any())
            {
                string rootDef = Path.Combine(mod.RootDir.FullName, "Defs");
                string rootPatch = Path.Combine(mod.RootDir.FullName, "Patches");
                string rootLang = Path.Combine(mod.RootDir.FullName, "Languages");
                return !Directory.Exists(rootDef) && !Directory.Exists(rootPatch) && !Directory.Exists(rootLang);
            }

            // 迴圈比對有效資料夾內，是否有需要翻譯的東西
            foreach (var folder in folders)
            {
                // 組合出絕對路徑
                string basePath = Path.Combine(mod.RootDir.FullName, folder.folderName);

                string defPath = Path.Combine(basePath, "Defs");
                string patchPath = Path.Combine(basePath, "Patches");
                string langPath = Path.Combine(basePath, "Languages");

                // 只要這三個只要中其中一個，就「不是」純代碼模組
                if (Directory.Exists(defPath) || Directory.Exists(patchPath) || Directory.Exists(langPath))
                {
                    return false;
                }
            }

            // 全部找完都沒有上述資料夾，確認是純代碼 / 純素材模組，回傳 true 準備過濾！
            return true;
        }
    }
}