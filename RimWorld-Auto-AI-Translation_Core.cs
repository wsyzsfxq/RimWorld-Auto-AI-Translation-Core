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

using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using System.IO;

namespace AutoTranslator_Core
{
    public enum TargetLanguage { Traditional, Simplified, Japanese, Korean, Russian, Ukrainian, English }
    public enum TranslatorProvider { Google, OpenAI, DeepSeek, Grok, GLM, Alibaba, OpenRouter, Custom_OpenAI }

    public class ApiKeyConfig : IExposable
    {
        public TranslatorProvider Provider = TranslatorProvider.Google;
        public string Key = "";
        public string CustomBaseUrl = "";
        public string SelectedModel = "";

        public List<string> FetchedModels = new List<string>();

        [NonSerialized] public bool IsFetching = false;
        [NonSerialized] public string lastFetchedKey = "";

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

        public long TotalCharCount = 0; // 總共翻譯字數 (持久化儲存)
        [NonSerialized] public long SessionCharCount = 0; // 本次掃描字數 (按開始掃描時歸零)

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
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (ApiConfigs == null || ApiConfigs.Count == 0)
                {
                    ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
                }
            }
        }
    }

    public class AutoTranslatorMod : Mod
    {
        public static AutoTranslatorSettings Settings;

        public AutoTranslatorMod(ModContentPack content) : base(content)
        {
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

            Patch_GUI_Label_GUIContent.BypassInterceptor = true;

            try
            {
                Rect viewRect = new Rect(0, 0, inRect.width - 20f, AutoTranslatorSettings.lastSettingsViewHeight);

            Widgets.BeginScrollView(inRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);

            if (AutoTranslatorSettings.ShowFinishPopup)
            {
                AutoTranslatorSettings.ShowFinishPopup = false;
                Find.WindowStack.Add(new Dialog_MessageBox("ATC_FinishMessage_Text".Translate(), "ATC_FinishMessage_OK".Translate(), null, null, null, "ATC_FinishMessage_Title".Translate()));
            }

            Listing_Standard l = new Listing_Standard();
            // 🌟 咪咪找回來的關鍵魔法！沒有這一行畫面就會崩潰！
            // 把原本的 l.Begin(viewRect); 改成這樣：
            Rect listRect = new Rect(0, 0, viewRect.width, 99999f);
            l.Begin(listRect);
            l.Gap(5f);

            Rect topBarRect = l.GetRect(30f);
            float btnWidth = (topBarRect.width - 20f) / 3f;

            if (Widgets.ButtonText(new Rect(topBarRect.x, topBarRect.y, btnWidth, topBarRect.height), "📜 " + "ATC_UpdateLog_Btn".Translate()))
            {
                Find.WindowStack.Add(new UpdateLogWindow());
            }

            if (Widgets.ButtonText(new Rect(topBarRect.x + btnWidth + 10f, topBarRect.y, btnWidth, topBarRect.height), "🗑️ " + "ATC_DeleteModTrans_Btn".Translate()))
            {
                Find.WindowStack.Add(new DeleteTranslationWindow());
            }

            if (Widgets.ButtonText(new Rect(topBarRect.x + (btnWidth + 10f) * 2, topBarRect.y, btnWidth, topBarRect.height), "📖 " + "ATC_Tutorial_Btn".Translate()))
            {
                Find.WindowStack.Add(new TutorialWindow());
            }
            l.Gap(10f);

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

            Rect threadRow = l.GetRect(30f);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            Settings.MaxThreads = (int)Widgets.HorizontalSlider(
                threadRow,
                Settings.MaxThreads,
                1f,
                30f,
                false,
                $"{"ATC_MaxThreads".Translate()}: {Settings.MaxThreads}  ({"ATC_MaxThreadsTip".Translate()})",
                "1",
                "30"
            );
            GUI.color = Color.white;
            l.Gap(15f);

            Rect interceptorRow = l.GetRect(30f);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            Widgets.CheckboxLabeled(interceptorRow, "ATC_EnableUIInterceptor".Translate(), ref Settings.EnableUIInterceptor);
            GUI.color = Color.white;
            l.Gap(5f);

            // ==========================================
            // ✨ 咪咪補上的「顯示原文」打勾框
            // ==========================================
            Rect showOriginalRow = l.GetRect(30f);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            // 只有在攔截器開啟時，這個選項才有意義，所以我們做一點防呆
            if (!Settings.EnableUIInterceptor) GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

            Widgets.CheckboxLabeled(showOriginalRow, "ATC_ShowOriginalUI".Translate(), ref Settings.ShowOriginalUI);
            GUI.color = Color.white;
            l.Gap(15f);

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
                string oldKey = config.Key;
                config.Key = Widgets.TextField(keyRect, config.Key);
                if (string.IsNullOrEmpty(config.Key)) Widgets.Label(keyRect, "  " + "ATC_PasteKey".Translate());

                if (config.Key != config.lastFetchedKey && config.Key.Length > 10 && !config.IsFetching && !AutoTranslatorSettings.IsRunning)
                {
                    config.lastFetchedKey = config.Key;
                    AutoTranslatorAPI.AutoFetchForConfig(config);
                }

                // ✅ 咪咪特製雙用版：左邊可以手打字，右邊可以按下拉選單！
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
                    // 讓玩家可以自由手動打字！
                    config.SelectedModel = Widgets.TextField(modelInputRect, config.SelectedModel);

                    // 如果是空的，顯示個浮水印提示
                    if (string.IsNullOrEmpty(config.SelectedModel))
                    {
                        GUI.color = Color.gray;
                        Text.Font = GameFont.Tiny; // 字體小一點才塞得下
                        Widgets.Label(new Rect(modelInputRect.x + 5f, modelInputRect.y + 2f, modelInputRect.width, modelInputRect.height), "ATC_InputOrSelectModel".Translate()); Text.Font = GameFont.Small;
                        GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                    }
                }

                // 右邊小小的下拉選單按鈕
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
                        // 抓不到列表時，給個小提示，但還是能讓他在旁邊手動打字！
                        Messages.Message("ATC_Msg_NoModelListManualInput".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
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

            l.Gap(15f);
            Widgets.DrawLineHorizontal(0, l.CurHeight, viewRect.width);
            l.Gap(15f);

            Rect actionRow = l.GetRect(40f);
            Rect singleModRect = new Rect(actionRow.x, actionRow.y, actionRow.width * 0.3f, actionRow.height);

            Rect skipRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect stopRect = new Rect(actionRow.x + actionRow.width * 0.67f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect startRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.68f, actionRow.height);

            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            if (Widgets.ButtonText(singleModRect, "ATC_TranslateMultiMod".Translate()))
            {
                if (!HasValidConfig()) Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                else if (!AutoTranslatorSettings.IsRunning) Find.WindowStack.Add(new ModSelectWindow());
            }

            if (AutoTranslatorSettings.IsRunning)
            {
                GUI.color = new Color(1f, 0.8f, 0.4f);
                if (Widgets.ButtonText(skipRect, "ATC_SkipCurrentMod".Translate()))
                {
                    AutoTranslatorSettings.IsSkipCurrentRequested = true;
                    AutoTranslatorSettings.AddLog("⏭️ [System] 玩家請求跳過當前模組...");
                }

                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (Widgets.ButtonText(stopRect, "🛑 " + "ATC_EmergencyStop".Translate()))
                {
                    AutoTranslatorSettings.IsCancellationRequested = true;
                    AutoTranslatorSettings.AddLog("⚠️ [System] " + "ATC_CancelRequested".Translate());
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

            l.Gap(10f);
            Rect statsRect = l.GetRect(50f);
            Rect statsLeft = new Rect(statsRect.x, statsRect.y, statsRect.width * 0.48f, statsRect.height);
            Rect statsRight = new Rect(statsRect.xMax - statsRect.width * 0.48f, statsRect.y, statsRect.width * 0.48f, statsRect.height);


            // 🌟 顯示掃描統計
            string sessionText = "ATC_Stats_Session".Translate(Settings.SessionCharCount);
            string totalText = "ATC_Stats_Total".Translate(Settings.TotalCharCount);
            Widgets.Label(statsLeft, $"📊 {sessionText}\n📈 {totalText}");

            // 🌟 顯示 UI 攔截器狀態 (不打擾玩家的精髓！)
            if (Settings.EnableUIInterceptor)
            {
                string uiQueue = UIInterceptor.GetQueueCount().ToString();
                string uiCache = UIInterceptor.Cache.Count.ToString();
                Widgets.Label(statsRight, $"🛡️ " + "ATC_Stats_UIQueue".Translate(uiQueue) + $"\n📦 " + "ATC_Stats_UICache".Translate(uiCache));
            }

            l.Gap(10f);
            Rect headerRect = l.GetRect(24f);
            float leftWidth = headerRect.width * 0.6f; float rightWidth = headerRect.width * 0.4f - 10f;
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

            Rect eggRect = new Rect(inRect.width - 150f, l.CurHeight + 5f, 140f, 20f);
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(eggRect, "ATC_Rickroll".Translate());

            if (Widgets.ButtonInvisible(eggRect))
            {
                if (AutoTranslatorMod.Settings.TargetLang == TargetLanguage.Simplified)
                    Application.OpenURL("https://www.bilibili.com/video/BV1GJ411x7h7");
                else
                    Application.OpenURL("https://www.youtube.com/watch?v=dQw4w9WgXcQ");
            }
            Text.Font = GameFont.Small;
            GUI.color = Color.white;

            AutoTranslatorSettings.lastSettingsViewHeight = l.CurHeight + 50f;

            l.End();
            Widgets.EndScrollView();
        }
            finally
    {
        // 🌟 畫完設定面板了，把免死金牌收回來，恢復遊戲正常的攔截功能！
        Patch_GUI_Label_GUIContent.BypassInterceptor = false;
    }
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

        public ModSelectWindow()
        {
            this.doCloseButton = false;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
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

            var allValidMods = ModLister.AllInstalledMods.Where(m => m.Active && m.PackageId.ToLower() != "autotranslator.core" && m.PackageId.ToLower() != "aitranslation.pack");

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

            var allValidMods = ModLister.AllInstalledMods.Where(m => m.PackageId.ToLower() != "autotranslator.core" && m.PackageId.ToLower() != "aitranslation.pack");
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
    }
}