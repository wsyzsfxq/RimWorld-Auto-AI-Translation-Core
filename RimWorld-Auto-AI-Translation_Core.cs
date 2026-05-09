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
    public enum TargetLanguage { Traditional, Simplified, Japanese, Korean }
    public enum TranslatorProvider { Google, OpenAI, DeepSeek, Grok, GLM, Alibaba, OpenRouter, Custom_OpenAI }

    public class AutoTranslatorSettings : ModSettings
    {
        public TranslatorProvider CurrentProvider = TranslatorProvider.Google;
        public TargetLanguage TargetLang = TargetLanguage.Traditional;
        public string SelectedModel = "";
        public string ApiKey = "";
        public string CustomBaseUrl = "";
        public bool OnlyScanActiveMods = true;

        public List<string> FetchedModels = new List<string>();
        public bool IsFetchingModels = false;
        public bool IsTesting = false;

        public float CurrentProgress = 0f;
        public string CurrentTaskName = "";

        // 🌟 雙螢幕儀表板：分別管理正常日誌與異常報告
        public static Vector2 logScrollPos = Vector2.zero;
        public static List<string> RuntimeLogs = new List<string>();

        public static Vector2 errorScrollPos = Vector2.zero;
        public static List<string> ErrorLogs = new List<string>();

        public static readonly object logLock = new object();

        public static float ReloadTimer = -1f;
        public static bool IsReloadingRequested = false;

        // 🌟 修復 1：提前攔截防呆 (87 咪咪謝罪版)
        public static void RequestReload(float seconds)
        {
            // 🛑 防呆機制：在跳出倒數視窗「前」就先檢查！
            bool isPackActive = ModLister.GetActiveModWithIdentifier("AITranslation.Pack", false) != null;

            if (!isPackActive)
            {
                // 沒啟用就直接彈出警告，絕對不倒數！
                AddLog("⚠️ " + "ATC_FirstTime_Notice".Translate());
                Find.WindowStack.Add(new Dialog_MessageBox("ATC_FirstTime_Dialog".Translate().ToString()));
                return;
            }

            // 有啟用才給他倒數！
            AddLog($"⚠️ [System] 觸發語言熱重載，將於 {seconds:F0} 秒後執行。");
            Find.WindowStack.Add(new AutoReloadCountdownWindow(seconds));
        }
        public static void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            lock (logLock)
            {
                RuntimeLogs.Add(line);
                if (RuntimeLogs.Count > 500) RuntimeLogs.RemoveAt(0);
                logScrollPos.y = 99999f;
            }
            WriteLogToFile(line);
        }

        // 🌟 新增：專門印在右邊視窗的錯誤日誌
        // 🌟 修復 2：過濾重複的異常日誌
        public static void AddErrorLog(string msg)
        {
            lock (logLock)
            {
                // 🛑 防洗頻機制：如果錯誤清單裡面已經有這句話了，就直接跳過不印！
                if (ErrorLogs.Any(x => x.Contains(msg))) return;

                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                ErrorLogs.Add(line);
                if (ErrorLogs.Count > 100) ErrorLogs.RemoveAt(0);
                errorScrollPos.y = 99999f;

                // 寫入實體檔案
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
                File.WriteAllText(path, $"=== Auto Translation Core V4.5 Ultimate [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n\n");
            }
            catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref CurrentProvider, "CurrentProvider", TranslatorProvider.Google);
            Scribe_Values.Look(ref TargetLang, "TargetLang", TargetLanguage.Traditional);
            Scribe_Values.Look(ref SelectedModel, "SelectedModel", "");
            Scribe_Values.Look(ref ApiKey, "ApiKey", "");
            Scribe_Values.Look(ref CustomBaseUrl, "CustomBaseUrl", "");
            Scribe_Values.Look(ref OnlyScanActiveMods, "OnlyScanActiveMods", true);
            Scribe_Collections.Look(ref FetchedModels, "FetchedModels", LookMode.Value);
        }
    }

    public class AutoTranslatorMod : Mod
    {
        public static AutoTranslatorSettings Settings;

        public AutoTranslatorMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AutoTranslatorSettings>();
            AutoTranslatorScanner.EnsurePackInitialized();
        }

        private string GetLangLabel(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ATC_Lang_Traditional".Translate();
                case TargetLanguage.Simplified: return "ATC_Lang_Simplified".Translate();
                case TargetLanguage.Japanese: return "ATC_Lang_Japanese".Translate();
                case TargetLanguage.Korean: return "ATC_Lang_Korean".Translate();
                default: return lang.ToString();
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard l = new Listing_Standard();
            l.Begin(inRect);
            l.Gap(5f);

            Rect topBarRect = l.GetRect(30f);
            if (Widgets.ButtonText(new Rect(topBarRect.x + topBarRect.width - 250f, topBarRect.y, 250f, topBarRect.height), "ATC_Tutorial_Btn".Translate()))
            {
                Find.WindowStack.Add(new TutorialWindow());
            }
            l.Gap(10f);

            Rect row1 = l.GetRect(30f);
            Rect langRect = new Rect(row1.x, row1.y, row1.width / 2 - 5f, row1.height);
            if (Mouse.IsOver(langRect)) TooltipHandler.TipRegion(langRect, "【目標語言】\n設定 AI 要將外語模組翻譯成什麼語言。");
            if (Widgets.ButtonText(langRect, "ATC_TargetLang".Translate() + ": " + GetLangLabel(Settings.TargetLang)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                {
                    options.Add(new FloatMenuOption(GetLangLabel(lang), () => Settings.TargetLang = lang));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Rect providerRect = new Rect(row1.x + row1.width / 2 + 5f, row1.y, row1.width / 2 - 5f, row1.height);
            if (Mouse.IsOver(providerRect)) TooltipHandler.TipRegion(providerRect, "【AI 服務商】\n選擇你要使用的 AI 模型供應商。強烈推薦使用 Google (Gemini) 或 OpenAI。");
            if (Widgets.ButtonText(providerRect, "ATC_CurrentProvider".Translate() + ": " + Settings.CurrentProvider))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TranslatorProvider p in Enum.GetValues(typeof(TranslatorProvider)))
                {
                    options.Add(new FloatMenuOption(p.ToString(), () => {
                        Settings.CurrentProvider = p;
                        Settings.FetchedModels.Clear();
                        Settings.SelectedModel = "ATC_PlzFetchModel".Translate();
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            l.Gap(5f);

            Rect keyLabelRect = l.GetRect(24f);
            Widgets.Label(keyLabelRect, "ATC_ApiKey".Translate() + ":");
            Settings.ApiKey = l.TextEntry(Settings.ApiKey);
            l.Gap(5f);

            if (Settings.CurrentProvider != TranslatorProvider.Google)
            {
                Rect urlLabelRect = l.GetRect(24f);
                Widgets.Label(urlLabelRect, "ATC_CustomBaseUrl".Translate() + " (預設請留空 / Default: Empty):");
                Settings.CustomBaseUrl = l.TextEntry(Settings.CustomBaseUrl);
                l.Gap(5f);
            }

            Rect row3 = l.GetRect(30f);
            Rect activeScanRect = new Rect(row3.x, row3.y, row3.width * 0.5f, row3.height);
            Widgets.CheckboxLabeled(activeScanRect, "ATC_OnlyScanActive".Translate(), ref Settings.OnlyScanActiveMods);

            Rect testRect = new Rect(row3.x + row3.width * 0.55f, row3.y, row3.width * 0.45f, row3.height);
            if (!Settings.IsTesting && Widgets.ButtonText(testRect, "🔌 " + "ATC_TestConnection".Translate()))
            {
                if (string.IsNullOrEmpty(Settings.ApiKey) && string.IsNullOrEmpty(Settings.CustomBaseUrl) && Settings.CurrentProvider != TranslatorProvider.Custom_OpenAI)
                {
                    Messages.Message("ATC_Msg_EmptyKey".Translate(), MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Settings.IsTesting = true;
                    AutoTranslatorSettings.AddLog("ATC_Log_TestPulse".Translate());
                    Task.Run(async () => {
                        bool ok = await AutoTranslatorAPI.TestConnectionAsync();
                        if (ok) AutoTranslatorSettings.AddLog("ATC_Log_TestSuccess".Translate());
                        else AutoTranslatorSettings.AddErrorLog("ATC_Log_TestFail".Translate());
                        Settings.IsTesting = false;
                    });
                }
            }
            if (Settings.IsTesting)
            {
                GUI.color = Color.yellow;
                Widgets.Label(testRect, "🔄 " + "ATC_TestingPulse".Translate());
                GUI.color = Color.white;
            }
            l.Gap(5f);

            Rect row4 = l.GetRect(30f);
            Rect fetchRect = new Rect(row4.x, row4.y, row4.width * 0.35f, row4.height);
            Rect modelRect = new Rect(row4.x + row4.width * 0.4f, row4.y, row4.width * 0.6f, row4.height);

            if (!Settings.IsFetchingModels && Widgets.ButtonText(fetchRect, "🔍 " + "ATC_FetchModels".Translate()))
            {
                Settings.IsFetchingModels = true;
                Task.Run(async () => {
                    var models = await AutoTranslatorAPI.FetchRemoteModelsAsync();
                    if (models != null && models.Count > 0)
                    {
                        Settings.FetchedModels = models;
                        Settings.SelectedModel = models[0];
                        AutoTranslatorSettings.AddLog("✅ [System] 獲取模型清單成功！");
                    }
                    else
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ [System] 獲取清單失敗，請檢查金鑰。");
                    }
                    Settings.IsFetchingModels = false;
                });
            }
            if (Settings.IsFetchingModels) Widgets.Label(fetchRect, "📡 " + "ATC_Fetching".Translate());

            string displayModel = string.IsNullOrEmpty(Settings.SelectedModel) ? "ATC_PlzFetchModel".Translate().ToString() : Settings.SelectedModel;
            if (Widgets.ButtonText(modelRect, "ATC_SelectModel".Translate() + $": {displayModel}"))
            {
                if (Settings.FetchedModels.Count > 0)
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (string m in Settings.FetchedModels) options.Add(new FloatMenuOption(m, () => Settings.SelectedModel = m));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            l.Gap(15f);

            Rect actionRow = l.GetRect(30f);
            Rect reloadRect = new Rect(actionRow.x, actionRow.y, actionRow.width * 0.3f, actionRow.height);
            if (Widgets.ButtonText(reloadRect, "🔄 " + "ATC_ManualReload".Translate()))
            {
                ExecuteHotReload();
            }

            Rect startRect = new Rect(actionRow.x + actionRow.width * 0.35f, actionRow.y, actionRow.width * 0.65f, actionRow.height);
            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            if (Widgets.ButtonText(startRect, "🚀 " + "ATC_StartTranslation".Translate()))
            {
                AutoTranslatorSettings.ClearLog();
                AutoTranslatorScanner.StartFullScan();
            }
            GUI.color = Color.white;
            l.Gap(10f);

            string displayTask = string.IsNullOrEmpty(Settings.CurrentTaskName) ? "ATC_Idle".Translate().ToString() : Settings.CurrentTaskName;
            l.Label("ATC_CurrentTask".Translate() + $": {displayTask}");

            Rect barRect = l.GetRect(24f);
            Widgets.FillableBar(barRect, Settings.CurrentProgress);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{(Settings.CurrentProgress * 100):F0}%");
            Text.Anchor = oldAnchor;
            l.Gap(10f);

            // 🌟 V4.5 雙螢幕儀表板標題
            Rect headerRect = l.GetRect(24f);
            float leftWidth = headerRect.width * 0.6f;
            float rightWidth = headerRect.width * 0.4f - 10f;
            Widgets.Label(new Rect(headerRect.x, headerRect.y, leftWidth, headerRect.height), "ATC_LogPanelTitle".Translate());
            Widgets.Label(new Rect(headerRect.x + leftWidth + 10f, headerRect.y, rightWidth, headerRect.height), "ATC_ErrorLogTitle".Translate());
            float remainHeight = inRect.height - l.CurHeight;
            if (remainHeight > 50f)
            {
                Rect logArea = l.GetRect(remainHeight);
                Rect leftRect = new Rect(logArea.x, logArea.y, leftWidth, logArea.height);
                Rect rightRect = new Rect(logArea.x + leftWidth + 10f, logArea.y, rightWidth, logArea.height);

                // --- 繪製左側：正常產線監控 ---
                Widgets.DrawBoxSolid(leftRect, new Color(0.05f, 0.05f, 0.05f, 1f));
                Widgets.DrawBox(leftRect, 1);
                DrawLogView(leftRect, AutoTranslatorSettings.RuntimeLogs, ref AutoTranslatorSettings.logScrollPos, false);

                // --- 繪製右側：異常爛檔報告 ---
                Widgets.DrawBoxSolid(rightRect, new Color(0.1f, 0.0f, 0.0f, 1f)); // 紅底色
                Widgets.DrawBox(rightRect, 1);
                DrawLogView(rightRect, AutoTranslatorSettings.ErrorLogs, ref AutoTranslatorSettings.errorScrollPos, true);
            }
            l.End();
        }

        // 🌟 獨立抽出來的滾動視窗繪製器，給左右兩邊共用
        private void DrawLogView(Rect rect, List<string> logs, ref Vector2 scrollPos, bool isErrorBox)
        {
            List<string> displayLogs;
            lock (AutoTranslatorSettings.logLock) { displayLogs = logs.ToList(); }

            Text.Font = GameFont.Tiny;
            float totalHeight = 0f;
            List<float> heights = new List<float>();
            foreach (string log in displayLogs)
            {
                float h = Text.CalcHeight(log, rect.width - 20f);
                heights.Add(h);
                totalHeight += h;
            }

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

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Widgets.EndScrollView();
        }

        public static void ExecuteHotReload()
        {
            bool isPackActive = ModLister.GetActiveModWithIdentifier("AITranslation.Pack", false) != null;

            if (!isPackActive)
            {
                AutoTranslatorSettings.AddLog("⚠️ " + "ATC_FirstTime_Notice".Translate());
                Find.WindowStack.Add(new Dialog_MessageBox("ATC_FirstTime_Dialog".Translate().ToString()));
                return;
            }

            AutoTranslatorSettings.AddLog("🚀 [System] " + "ATC_ReloadingLog".Translate());

            LongEventHandler.QueueLongEvent(() =>
            {
                LanguageDatabase.activeLanguage.LoadData();
                AutoTranslatorSettings.AddLog("✅ [System] " + "ATC_ReloadDoneLog".Translate());
            }, "ATC_ReloadingMessage".Translate(), true, null);
        }

        public override string SettingsCategory() => "ATC_ModTitle".Translate();
    }

    public class AutoReloadCountdownWindow : Window
    {
        private float timer;
        public override Vector2 InitialSize => new Vector2(700f, 90f);
        protected override float Margin => 10f;

        public AutoReloadCountdownWindow(float seconds)
        {
            this.timer = seconds;
            this.preventCameraMotion = false;
            this.doCloseX = false;
            this.doCloseButton = false;
            this.closeOnClickedOutside = false;
            this.closeOnCancel = false;
            this.forcePause = false;
            this.drawShadow = true;
            this.layer = WindowLayer.Super;
        }

        public override void DoWindowContents(Rect inRect)
        {
            timer -= Time.deltaTime;

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 0.3f, 0.3f);

            string msg = "ATC_ReloadingCountdown".Translate() + $" {timer:F0} " + "ATC_Seconds".Translate();
            Widgets.Label(inRect, "⚠️ " + msg);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            if (timer <= 0)
            {
                this.Close();
                AutoTranslatorMod.ExecuteHotReload();
            }
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            this.windowRect.x = (UI.screenWidth - this.InitialSize.x) / 2f;
            this.windowRect.y = 50f;
        }
    }

    // 教學視窗
    public class TutorialWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 InitialSize => new Vector2(750f, 700f);

        public TutorialWindow()
        {
            this.doCloseButton = true;
            this.doCloseX = true;
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "📖 " + "ATC_Tutorial_Title".Translate());
            Text.Font = GameFont.Small;

            Widgets.DrawLineHorizontal(0, 35f, inRect.width);

            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 100f);
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, 1200f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);

            Text.Font = GameFont.Medium;
            l.Label("🚀 " + "ATC_Tutorial_StepsTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap(5f);
            l.Label("1. " + "ATC_Tutorial_Step1".Translate());
            l.Label("2. " + "ATC_Tutorial_Step2".Translate());
            l.Label("3. " + "ATC_Tutorial_Step3".Translate());
            l.Label("4. " + "ATC_Tutorial_Step4".Translate());
            l.Gap(5f);
            GUI.color = new Color(1f, 0.8f, 0.4f);
            l.Label("5. " + "ATC_Tutorial_Step5".Translate());
            GUI.color = Color.white;
            l.Gap(15f);

            Text.Font = GameFont.Medium;
            l.Label("💡 " + "ATC_Tutorial_FAQTitle".Translate());
            Text.Font = GameFont.Small;
            l.Gap(5f);

            GUI.color = new Color(0.4f, 0.8f, 1f);
            l.Label("Q: " + "ATC_FAQ_Q1".Translate());
            GUI.color = Color.white;
            l.Label("A: " + "ATC_FAQ_A1".Translate());
            l.GapLine();

            GUI.color = new Color(0.4f, 0.8f, 1f);
            l.Label("Q: " + "ATC_FAQ_Q2".Translate());
            GUI.color = Color.white;
            l.Label("A: " + "ATC_FAQ_A2".Translate());
            l.GapLine();

            GUI.color = new Color(0.4f, 0.8f, 1f);
            l.Label("Q: " + "ATC_FAQ_Q3".Translate());
            GUI.color = Color.white;
            l.Label("A: " + "ATC_FAQ_A3".Translate());
            l.GapLine();

            GUI.color = new Color(0.4f, 0.8f, 1f);
            l.Label("Q: " + "ATC_FAQ_Q4".Translate());
            GUI.color = Color.white;
            l.Label("A: " + "ATC_FAQ_A4".Translate());
            l.GapLine();

            GUI.color = new Color(0.4f, 0.8f, 1f);
            l.Label("Q: " + "ATC_FAQ_Q5".Translate());
            GUI.color = Color.white;
            l.Label("A: " + "ATC_FAQ_A5".Translate());

            l.End();
            Widgets.EndScrollView();
        }
    }
}