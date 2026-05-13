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

    // 🌟 咪咪特製：API 彈匣配置類別
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

        public int MaxThreads = 3; // 🌟 咪咪特製：極速飆車最大線程數，預設為 3

        public List<ApiKeyConfig> ApiConfigs = new List<ApiKeyConfig>();

        public float CurrentProgress = 0f;
        public string CurrentTaskName = "";

        public float SubProgress = 0f;
        public string SubTaskName = "";
        public static bool ShouldAutoScroll = true;
        public static bool IsSkipCurrentRequested = false;

        // 🌟 咪咪特製：用來記錄每一幀真實的 UI 總高度
        public static float lastSettingsViewHeight = 1000f;

        // 🌟 咪咪特製：彈窗信號 (防止背景執行緒直接呼叫 UI 導致閃退)
        public static bool ShowFinishPopup = false;

        // 🌟 咪咪特製：全局視窗大滾輪！
        public static Vector2 mainScrollPos = Vector2.zero;

        public static Vector2 logScrollPos = Vector2.zero;
        public static List<string> RuntimeLogs = new List<string>();

        public static Vector2 errorScrollPos = Vector2.zero;
        public static List<string> ErrorLogs = new List<string>();

        public static readonly object logLock = new object();

        public static bool IsCancellationRequested = false;
        public static bool IsRunning = false;

        public static void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            lock (logLock)
            {
                RuntimeLogs.Add(line);
                if (RuntimeLogs.Count > 500) RuntimeLogs.RemoveAt(0);
                // ❌ 注意！這裡的 logScrollPos.y = 99999f; 已經被咪咪刪掉了！
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
                File.WriteAllText(path, $"=== Auto Translation Core V4.7 Ultimate [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n\n");
            }
            catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TargetLang, "TargetLang", TargetLanguage.Traditional);
            Scribe_Values.Look(ref OnlyScanActiveMods, "OnlyScanActiveMods", true);

            Scribe_Values.Look(ref MaxThreads, "MaxThreads", 3); // 🌟 儲存玩家設定的線程數

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
            // 【修改後】✨ 動態視窗高度追蹤！絕對不再破圖吃字！
            // ==========================================
            // 🌟 咪咪特製：動態視窗高度追蹤！
            // ==========================================
            // 直接使用上一幀記錄下來的真實高度來建立滾輪視窗
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, AutoTranslatorSettings.lastSettingsViewHeight);

            Widgets.BeginScrollView(inRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);

            if (AutoTranslatorSettings.ShowFinishPopup)
            {
                AutoTranslatorSettings.ShowFinishPopup = false;
                Find.WindowStack.Add(new Dialog_MessageBox("ATC_FinishMessage_Text".Translate(), "ATC_FinishMessage_OK".Translate(), null, null, null, "ATC_FinishMessage_Title".Translate()));
            }

            Listing_Standard l = new Listing_Standard();
            // 🌟 關鍵魔法：給 Listing_Standard 一個無限高 (99999f) 的大畫布，讓它自由發揮，絕對不會把裡面的東西壓扁！
            Rect listingRect = new Rect(0, 0, inRect.width - 20f, 99999f);
            l.Begin(listingRect); l.Gap(5f);

            Rect topBarRect = l.GetRect(30f);
            if (Widgets.ButtonText(new Rect(topBarRect.x, topBarRect.y, 250f, topBarRect.height), "📜 " + "ATC_UpdateLog_Btn".Translate()))
            {
                Find.WindowStack.Add(new UpdateLogWindow());
            }
            if (Widgets.ButtonText(new Rect(topBarRect.x + topBarRect.width - 250f, topBarRect.y, 250f, topBarRect.height), "📖 " + "ATC_Tutorial_Btn".Translate()))
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

            // 🌟 V4.7 動態多 API 配置區
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

                Rect modelRect = new Rect(rowB.x + rowB.width * 0.47f, rowB.y, rowB.width * 0.53f, rowB.height - 2f);
                string displayModel = config.IsFetching ? "📡 " + "ATC_FetchingModel".Translate().ToString() : (string.IsNullOrEmpty(config.SelectedModel) ? "ATC_NoModelWaitKey".Translate().ToString() : config.SelectedModel);

                if (config.IsFetching) GUI.color = Color.yellow;
                if (Widgets.ButtonText(modelRect, displayModel))
                {
                    if (config.FetchedModels.Count > 0 && !AutoTranslatorSettings.IsRunning && !config.IsFetching)
                    {
                        List<FloatMenuOption> opts = new List<FloatMenuOption>();
                        foreach (string m in config.FetchedModels) opts.Add(new FloatMenuOption(m, () => config.SelectedModel = m));
                        Find.WindowStack.Add(new FloatMenu(opts));
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

            // 🌟 執行區塊 (按鈕區)
            Rect actionRow = l.GetRect(40f);
            Rect singleModRect = new Rect(actionRow.x, actionRow.y, actionRow.width * 0.3f, actionRow.height);

            // 將右側空間切成兩塊，給「跳過」和「停止/開始」用
            Rect skipRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect stopRect = new Rect(actionRow.x + actionRow.width * 0.67f, actionRow.y, actionRow.width * 0.33f, actionRow.height);
            Rect startRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.68f, actionRow.height);

            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            if (Widgets.ButtonText(singleModRect, "ATC_TranslateMultiMod".Translate()))
            {
                if (!HasValidConfig()) Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                else if (!AutoTranslatorSettings.IsRunning) Find.WindowStack.Add(new ModSelectWindow()); // 呼叫多選視窗
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

            // 🌟 大進度條 (總任務進度)
            string displayTask = string.IsNullOrEmpty(Settings.CurrentTaskName) ? "ATC_Idle".Translate().ToString() : Settings.CurrentTaskName;
            l.Label("ATC_CurrentTask".Translate() + $": {displayTask}");
            Rect barRect = l.GetRect(25f);
            Widgets.FillableBar(barRect, Settings.CurrentProgress);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{(Settings.CurrentProgress * 100):F0}%");
            Text.Anchor = oldAnchor;

            // 🌟 小進度條 (當前模組 / 字典建構進度)
            Rect subBarRect = l.GetRect(15f);
            string displaySubTask = string.IsNullOrEmpty(Settings.SubTaskName) ? "" : Settings.SubTaskName;
            Widgets.FillableBar(subBarRect, Settings.SubProgress);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(subBarRect.x, subBarRect.y, subBarRect.width, subBarRect.height), $" {displaySubTask} ({(Settings.SubProgress * 100):F0}%)");
            Text.Font = GameFont.Small;
            l.Gap(15f);

            // 🌟 大哥專屬：超級豪華大空間日誌區！
            Rect headerRect = l.GetRect(24f);
            float leftWidth = headerRect.width * 0.6f;
            float rightWidth = headerRect.width * 0.4f - 10f;
            Widgets.Label(new Rect(headerRect.x, headerRect.y, leftWidth, headerRect.height), "ATC_LogPanelTitle".Translate());
            Widgets.Label(new Rect(headerRect.x + leftWidth + 10f, headerRect.y, rightWidth, headerRect.height), "ATC_ErrorLogTitle".Translate());

            // 給日誌區一個絕對不會被壓縮的 350f 固定高度！
            Rect logArea = l.GetRect(350f);
            Rect leftRect = new Rect(logArea.x, logArea.y, leftWidth, logArea.height);
            Rect rightRect = new Rect(logArea.x + leftWidth + 10f, logArea.y, rightWidth, logArea.height);

            Widgets.DrawBoxSolid(leftRect, new Color(0.05f, 0.05f, 0.05f, 1f));
            Widgets.DrawBox(leftRect, 1);
            DrawLogView(leftRect, AutoTranslatorSettings.RuntimeLogs, ref AutoTranslatorSettings.logScrollPos, false);

            // 【修改後】✨ 咪咪特製：瑞克搖物理超渡地雷！
            Widgets.DrawBoxSolid(rightRect, new Color(0.1f, 0.0f, 0.0f, 1f));
            Widgets.DrawBox(rightRect, 1);
            DrawLogView(rightRect, AutoTranslatorSettings.ErrorLogs, ref AutoTranslatorSettings.errorScrollPos, true);

            // 🌟 彩蛋放置區：藏在日誌框框的右下角下方
            // 【修改後】✨ 完美定位並存檔高度！
            // 🌟 彩蛋放置區：直接使用 l.CurHeight (當前總高度) 來定位，保證乖乖待在最底下！
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

            // 🌟 最終大魔法：把這次畫完的「真實總高度」記錄下來，讓下一個 Frame 的滾輪可以無縫適應！
            AutoTranslatorSettings.lastSettingsViewHeight = l.CurHeight + 50f;

            l.End();

            Widgets.EndScrollView();
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

                if (isErrorBox || log.Contains("❌") || log.Contains("⚠️")) GUI.color = new Color(1f, 0.4f, 0.4f);
                else if (log.Contains("✅") || log.Contains("✨") || log.Contains("🎉")) GUI.color = new Color(0.4f, 1f, 0.4f);
                else if (log.Contains("⚙️") || log.Contains("🔌") || log.Contains("🔄")) GUI.color = new Color(1f, 0.8f, 0.4f);
                else if (log.Contains("📦") || log.Contains("🌐") || log.Contains("🚀")) GUI.color = new Color(0.4f, 0.8f, 1f);
                else GUI.color = new Color(0.8f, 0.8f, 0.8f);

                Widgets.Label(lineRect, log);
                currentY += h;
            }

            // ✨ 咪咪特製：無視 Unity 的滑鼠焦點偏移，只要離底部夠近，就強制吸到底部！
            float viewHeight = heights.Sum();
            float maxScroll = Mathf.Max(0f, viewHeight - rect.height);

            // 把 Layout 事件限制拔掉，並且把容錯值拉高到 100f，防止滑鼠移開時的微小判定誤差
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
    // 🌟 V4.8 搜尋、排序與多選模組視窗
    // ==========================================
    public class ModSelectWindow : Window
    {
        private string searchText = "";
        private Vector2 scrollPos = Vector2.zero;
        private HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        public override Vector2 InitialSize => new Vector2(600f, 700f);
        // 🌟 咪咪特製：用來記錄滑動勾選的狀態 (是正在全選還是全取消)
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
            // 【修改後】✨ 咪咪特製：全選/全取消 ＋ 一鍵亂選大輪盤！
            if (string.IsNullOrEmpty(searchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_MultiSelect_Search".Translate());
                GUI.color = Color.white;
            }

            var allValidMods = ModLister.AllInstalledMods.Where(m => m.Active && m.PackageId.ToLower() != "autotranslator.core" && m.PackageId.ToLower() != "aitranslation.pack");
            if (!string.IsNullOrEmpty(searchText)) allValidMods = allValidMods.Where(m => m.Name.ToLower().Contains(searchText.ToLower()));
            var displayMods = allValidMods.OrderBy(m => m.Name).ToList();

            // 🌟 搞事按鈕列
            Rect btnRow = new Rect(0, 85f, inRect.width, 30f);

            // 👇 就是漏了這行啦！要先告訴電腦「什麼狀態叫做全選」
            bool isAllSelected = (selectedMods.Count > 0 && selectedMods.Count == displayMods.Count);

            // 🌟 智慧全選按鈕：根據狀態自動切換翻譯！
            string btnLabel = isAllSelected ? "ATC_DeselectAll".Translate() : "ATC_SelectAll".Translate();
            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, 120f, btnRow.height), btnLabel))
            {
                if (isAllSelected) selectedMods.Clear();
                else { selectedMods.Clear(); foreach (var m in displayMods) selectedMods.Add(m); }
            }

            // 🌟 一鍵亂選按鈕
            GUI.color = new Color(1f, 0.6f, 0.8f);
            if (Widgets.ButtonText(new Rect(btnRow.x + 130f, btnRow.y, 120f, btnRow.height), "ATC_One_click_chaos".Translate()))
            {
                selectedMods.Clear();
                var rand = new System.Random();
                foreach (var m in displayMods)
                {
                    if (rand.NextDouble() > 0.5) selectedMods.Add(m);
                }
            }
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(0, 120f, inRect.width);
            // 列表的 Y 軸往下推一點，讓出空間給按鈕
            Rect listOutRect = new Rect(0, 130f, inRect.width, inRect.height - 180f);
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, displayMods.Count * 30f);
            Widgets.BeginScrollView(listOutRect, ref scrollPos, viewRect);

            // 【修改後】✨ 咪咪特製：滑動勾選無雙模式！
            // 🌟 只要滑鼠左鍵放開，就清空拖曳狀態
            // 🌟 只要滑鼠左鍵放開，就清空拖曳狀態
            if (Event.current.type == EventType.MouseUp)
            {
                dragTargetState = null;
            }

            float currentY = 0f;
            foreach (var mod in displayMods)
            {
                Rect rowRect = new Rect(0, currentY, viewRect.width, 30f);
                bool isChecked = selectedMods.Contains(mod);

                // 🌟 咪咪的極致絲滑改良版：手動攔截並接管滑鼠事件！
                if (Mouse.IsOver(rowRect))
                {
                    // 當滑鼠「按下去」的瞬間，立刻反轉狀態！並記錄下來準備拖曳
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                    {
                        isChecked = !isChecked; // 瞬間打勾或取消
                        dragTargetState = isChecked;
                        Event.current.Use(); // 🌟 關鍵魔法：把這個點擊事件「吃掉」，不要讓下面的 Checkbox 搶走！
                    }
                    // 當滑鼠「按住並拖曳」經過這個框框時，強制套用狀態
                    else if (Event.current.type == EventType.MouseDrag && dragTargetState.HasValue)
                    {
                        isChecked = dragTargetState.Value;
                    }
                }

                // 畫出勾選框 (因為點擊事件可能被我們吃掉了，它現在乖乖當個純顯示的 UI 就好)
                Widgets.CheckboxLabeled(rowRect, mod.Name, ref isChecked);

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
                    AutoTranslatorScanner.StartMultiScan(selectedMods.ToList()); // 呼叫多選掃描
                    this.Close();
                }
            }
            GUI.color = Color.white;
        }
    }

}