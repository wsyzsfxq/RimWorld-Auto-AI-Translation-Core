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
                logScrollPos.y = 99999f;
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
                case TargetLanguage.Russian: return "ATC_Lang_Russian".Translate();
                case TargetLanguage.Ukrainian: return "ATC_Lang_Ukrainian".Translate();
                case TargetLanguage.English: return "ATC_Lang_English".Translate();
                default: return lang.ToString();
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // ==========================================
            // 🌟 大哥的天才發想：全局大滾輪計算！🌟
            // ==========================================
            // 基礎介面高度大約 300f，每個 API 佔 90f，日誌區給它豪華的 350f 固定高度！
            float totalRequiredHeight = 300f + (Settings.ApiConfigs.Count * 90f) + 350f + 50f;

            // 如果視窗夠大就不滾動，如果內容超過視窗就開啟全局滾動
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, Mathf.Max(totalRequiredHeight, inRect.height));

            // 開啟整個設定介面的超級大滾輪！
            Widgets.BeginScrollView(inRect, ref AutoTranslatorSettings.mainScrollPos, viewRect);

            Listing_Standard l = new Listing_Standard();
            l.Begin(viewRect);
            l.Gap(5f);

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
            Rect startRect = new Rect(actionRow.x + actionRow.width * 0.32f, actionRow.y, actionRow.width * 0.68f, actionRow.height);

            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            if (Widgets.ButtonText(singleModRect, "🎯 " + "ATC_TranslateSingleMod".Translate()))
            {
                if (!HasValidConfig())
                {
                    Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                }
                else if (!AutoTranslatorSettings.IsRunning)
                {
                    List<FloatMenuOption> modOptions = new List<FloatMenuOption>();
                    var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && m.PackageId.ToLower() != "autotranslator.core" && m.PackageId.ToLower() != "aitranslation.pack").ToList();
                    foreach (var mod in activeMods)
                        modOptions.Add(new FloatMenuOption(mod.Name, () => { AutoTranslatorSettings.ClearLog(); AutoTranslatorSettings.IsCancellationRequested = false; AutoTranslatorScanner.StartSingleScan(mod); }));
                    Find.WindowStack.Add(new FloatMenu(modOptions));
                }
            }

            if (AutoTranslatorSettings.IsRunning)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (Widgets.ButtonText(startRect, "🛑 " + "ATC_EmergencyStop".Translate()))
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
                    if (!HasValidConfig())
                    {
                        Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        AutoTranslatorSettings.ClearLog();
                        AutoTranslatorSettings.IsCancellationRequested = false;
                        AutoTranslatorScanner.StartFullScan();
                    }
                }
                GUI.color = Color.white;
            }
            l.Gap(15f);

            // 進度條
            string displayTask = string.IsNullOrEmpty(Settings.CurrentTaskName) ? "ATC_Idle".Translate().ToString() : Settings.CurrentTaskName;
            l.Label("ATC_CurrentTask".Translate() + $": {displayTask}");
            Rect barRect = l.GetRect(30f);
            Widgets.FillableBar(barRect, Settings.CurrentProgress);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{(Settings.CurrentProgress * 100):F0}%");
            Text.Anchor = oldAnchor;
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

            Widgets.DrawBoxSolid(rightRect, new Color(0.1f, 0.0f, 0.0f, 1f));
            Widgets.DrawBox(rightRect, 1);
            DrawLogView(rightRect, AutoTranslatorSettings.ErrorLogs, ref AutoTranslatorSettings.errorScrollPos, true);

            l.End();

            // 結束全局大滾輪
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
}