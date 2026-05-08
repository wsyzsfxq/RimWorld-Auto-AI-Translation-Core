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
        // 🌟 咪咪修復 2：先不要在這裡 Translate，等 UI 畫面的時候再處理！
        public string CurrentTaskName = "";

        public static Vector2 logScrollPos = Vector2.zero;
        public static List<string> RuntimeLogs = new List<string>();
        public static readonly object logLock = new object();

        public static void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";

            lock (logLock)
            {
                RuntimeLogs.Add(line);
                if (RuntimeLogs.Count > 500) RuntimeLogs.RemoveAt(0);
                logScrollPos.y = 99999f;
            }

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
                logScrollPos = Vector2.zero;
            }
            try
            {
                string path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "AutoTranslation_Log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, $"=== Auto Translation Core V4.4.2 Log [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n\n");
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

            // 🌟 咪咪修復 1：刪掉這裡手動印的標題，讓 RimWorld 自動幫我們印一次就好！才不會出現兩行！
            l.Gap(5f);

            Rect row1 = l.GetRect(30f);
            if (Widgets.ButtonText(new Rect(row1.x, row1.y, row1.width / 2 - 5f, row1.height), "ATC_TargetLang".Translate() + ": " + GetLangLabel(Settings.TargetLang)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                {
                    options.Add(new FloatMenuOption(GetLangLabel(lang), () => Settings.TargetLang = lang));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (Widgets.ButtonText(new Rect(row1.x + row1.width / 2 + 5f, row1.y, row1.width / 2 - 5f, row1.height), "ATC_CurrentProvider".Translate() + ": " + Settings.CurrentProvider))
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

            l.Label("ATC_ApiKey".Translate() + ":");
            Settings.ApiKey = l.TextEntry(Settings.ApiKey);
            l.Gap(5f);

            if (Settings.CurrentProvider != TranslatorProvider.Google)
            {
                l.Label("ATC_CustomBaseUrl".Translate() + " (預設請留空 / Default: Empty):");
                Settings.CustomBaseUrl = l.TextEntry(Settings.CustomBaseUrl);
                l.Gap(5f);
            }

            Rect row3 = l.GetRect(30f);
            Widgets.CheckboxLabeled(new Rect(row3.x, row3.y, row3.width * 0.5f, row3.height), "ATC_OnlyScanActive".Translate(), ref Settings.OnlyScanActiveMods);

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
                    string modelToTest = string.IsNullOrEmpty(Settings.SelectedModel) || Settings.SelectedModel.Contains("請") ? "Auto/Custom" : Settings.SelectedModel;
                    AutoTranslatorSettings.AddLog("ATC_Log_TestPulse".Translate(modelToTest));

                    Task.Run(async () => {
                        bool ok = await AutoTranslatorAPI.TestConnectionAsync();
                        if (ok) AutoTranslatorSettings.AddLog("ATC_Log_TestSuccess".Translate());
                        else AutoTranslatorSettings.AddLog("ATC_Log_TestFail".Translate());
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
                    }
                    else
                    {
                        // 🌟 若失敗，至少重置狀態，讓玩家可以重按
                        AutoTranslatorSettings.AddLog("❌ [System] 獲取清單失敗 (Fetch failed)，請檢查日誌與 API 設定。");
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

            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            if (l.ButtonText("🚀 " + "ATC_StartTranslation".Translate()))
            {
                AutoTranslatorSettings.ClearLog();
                AutoTranslatorScanner.StartFullScan();
            }
            GUI.color = Color.white;
            l.Gap(10f);

            // 🌟 咪咪修復 2：在這裡動態判斷，如果字串是空的，才叫出空閒中的本地化翻譯！
            string displayTask = string.IsNullOrEmpty(Settings.CurrentTaskName) ? "ATC_Idle".Translate().ToString() : Settings.CurrentTaskName;
            l.Label("ATC_CurrentTask".Translate() + $": {displayTask}");

            Rect barRect = l.GetRect(24f);
            Widgets.FillableBar(barRect, Settings.CurrentProgress);
            TextAnchor oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(barRect, $"{(Settings.CurrentProgress * 100):F0}%");
            Text.Anchor = oldAnchor;
            l.Gap(10f);

            l.Label("ATC_LogPanelTitle".Translate());

            float remainHeight = inRect.height - l.CurHeight;
            if (remainHeight > 50f)
            {
                Rect logRect = l.GetRect(remainHeight);
                Widgets.DrawBoxSolid(logRect, new Color(0.05f, 0.05f, 0.05f, 1f));
                Widgets.DrawBox(logRect, 1);

                List<string> displayLogs;
                lock (AutoTranslatorSettings.logLock) { displayLogs = AutoTranslatorSettings.RuntimeLogs.ToList(); }

                Text.Font = GameFont.Tiny;

                float totalHeight = 0f;
                List<float> heights = new List<float>();
                foreach (string log in displayLogs)
                {
                    float h = Text.CalcHeight(log, logRect.width - 20f);
                    heights.Add(h);
                    totalHeight += h;
                }

                float contentHeight = Mathf.Max(totalHeight, logRect.height);
                Rect viewRect = new Rect(0, 0, logRect.width - 20f, contentHeight);

                Widgets.BeginScrollView(logRect, ref AutoTranslatorSettings.logScrollPos, viewRect);
                float currentY = 0;

                for (int i = 0; i < displayLogs.Count; i++)
                {
                    string log = displayLogs[i];
                    float h = heights[i];
                    Rect lineRect = new Rect(5f, currentY, viewRect.width, h);

                    if (log.Contains("❌")) GUI.color = new Color(1f, 0.4f, 0.4f);
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
            l.End();
        }

        public override string SettingsCategory() => "ATC_ModTitle".Translate();
    }
}