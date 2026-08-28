using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
// 這個檔案負責設定分頁的 UI 與參數編輯。
// EN: This file draws the settings tab and edits runtime options.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器模組 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorMod.
    public partial class AutoTranslatorMod : Mod
    {
        private static bool _settingsLegacyRepairRunning;
        private static bool _settingsRestoreBackupRunning;
        private static bool _settingsCompatibilityExpanded;


        // 這個方法負責繪製 設定分頁 介面。
        // EN: This method draws config tab.
        private void DrawConfigTab(Listing_Standard l, Rect viewRect)
        {
            DrawSettingsSectionHeader(l, WfText("常用与界面显示", "General and display"));
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_ShowWorldMainButton".Translate(), ref Settings.ShowWorldMainButton);
            l.Gap(5f);
            Rect row1 = l.GetRect(30f);
            Rect langRect = new Rect(row1.x, row1.y, row1.width, row1.height);
            if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;
            if (Mouse.IsOver(langRect)) TooltipHandler.TipRegion(langRect, "ATC_Tooltip_TargetLang".Translate());
            if (WorkflowUiStyle.Button(langRect,
                    "ATC_TargetLang".Translate() + ": " + GetLangLabel(Settings.TargetLang) + "  ▾",
                    WorkflowButtonStyle.Dropdown, !AutoTranslatorSettings.IsRunning))
            {
                if (!AutoTranslatorSettings.IsRunning)
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                    {
                        TargetLanguage capturedLang = lang;
                        options.Add(new FloatMenuOption(GetLangLabel(lang), () => SetTargetLanguage(capturedLang)));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            }
            GUI.color = Color.white;
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_TranslateWorkbenchModNames".Translate(), ref Settings.TranslateWorkbenchModNames);
            l.Gap(15f);

            DrawSettingsSectionHeader(l, WfText("AI 接口", "AI providers"));
            DrawTranslationUsageBudgetSettings(l);
            l.Gap(15f);
            Widgets.Label(l.GetRect(24f), "🔧 " + "ATC_ApiConfigTitle".Translate());

            for (int i = 0; i < Settings.ApiConfigs.Count; i++)
            {
                var config = Settings.ApiConfigs[i];
                Rect apiCard = l.GetRect(216f);
                Widgets.DrawBoxSolid(apiCard, WorkflowUiStyle.Panel);
                WorkflowUiStyle.DrawBorder(apiCard, new Color(0.31f, 0.35f, 0.38f));
                Widgets.DrawBoxSolid(
                    new Rect(apiCard.x, apiCard.y, 4f, apiCard.height),
                    config.Enabled ? WorkflowUiStyle.GoodText : WorkflowUiStyle.MutedText);
                Listing_Standard apiListing = new Listing_Standard();
                apiListing.Begin(apiCard.ContractedBy(10f));
                if (AutoTranslatorSettings.IsRunning) GUI.color = Color.grey;

                Rect noteRow = apiListing.GetRect(28f);
                const float headerIconWidth = 42f;
                const float headerIconGap = 6f;
                Rect noteRect = new Rect(
                    noteRow.x,
                    noteRow.y,
                    noteRow.width - headerIconWidth * 2f - headerIconGap * 2f,
                    noteRow.height - 2f);
                Rect enabledRect = new Rect(
                    noteRect.xMax + headerIconGap,
                    noteRow.y,
                    headerIconWidth,
                    noteRow.height - 2f);
                Rect deleteRect = new Rect(
                    enabledRect.xMax + headerIconGap,
                    noteRow.y,
                    headerIconWidth,
                    noteRow.height - 2f);
                config.Label = Widgets.TextField(noteRect, config.Label ?? "");
                if (string.IsNullOrEmpty(config.Label))
                {
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(noteRect.x + 5f, noteRect.y + 4f, noteRect.width - 10f, noteRect.height), "ATC_ApiKeyNoteHint".Translate());
                    Text.Font = GameFont.Small;
                    GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                }
                if (WorkflowUiStyle.Button(
                        enabledRect,
                        config.Enabled ? "✓" : "✕",
                        config.Enabled ? WorkflowButtonStyle.Primary : WorkflowButtonStyle.Stop,
                        !AutoTranslatorSettings.IsRunning,
                        GameFont.Small))
                    config.Enabled = !config.Enabled;
                TooltipHandler.TipRegion(
                    enabledRect,
                    config.Enabled
                        ? WfText("已启用；点击停用此 API", "Enabled; click to disable this API")
                        : WfText("已停用；点击启用此 API", "Disabled; click to enable this API"));

                bool canDeleteApi = Settings.ApiConfigs.Count > 1 && !AutoTranslatorSettings.IsRunning;
                if (WorkflowUiStyle.Button(
                        deleteRect,
                        "🗑",
                        WorkflowButtonStyle.Stop,
                        canDeleteApi,
                        GameFont.Small))
                {
                    Settings.ApiConfigs.RemoveAt(i);
                    GUI.color = Color.white;
                    apiListing.End();
                    break;
                }
                TooltipHandler.TipRegion(
                    deleteRect,
                    Settings.ApiConfigs.Count > 1
                        ? WfText("删除此 API", "Delete this API")
                        : WfText("至少保留一个 API", "At least one API must remain"));
                GUI.color = config.Enabled
                    ? (AutoTranslatorSettings.IsRunning ? Color.grey : Color.white)
                    : new Color(0.55f, 0.55f, 0.55f, 0.85f);

                Rect rowA = apiListing.GetRect(30f);
                Rect providerRect = new Rect(rowA.x, rowA.y, rowA.width * 0.3f, rowA.height - 2f);
                if (WorkflowUiStyle.Button(providerRect,
                        "ATC_Provider".Translate() + ": " + config.Provider + "  ▾",
                        WorkflowButtonStyle.Dropdown, !AutoTranslatorSettings.IsRunning))
                {
                    if (!AutoTranslatorSettings.IsRunning)
                    {
                        List<FloatMenuOption> opts = new List<FloatMenuOption>();
                        foreach (TranslatorProvider p in Enum.GetValues(typeof(TranslatorProvider)))
                        {
                            opts.Add(new FloatMenuOption(p.ToString(), () =>
                            {
                                config.Provider = p;
                                config.SelectedModel = "";
                                AutoTranslatorAPI.ResetModelFetchState(config, clearModels: true);
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                }

                Rect urlRect = new Rect(rowA.x + rowA.width * 0.32f, rowA.y, rowA.width * 0.68f, rowA.height - 2f);
                if (config.Provider != TranslatorProvider.Google)
                {
                    config.CustomBaseUrl = Widgets.TextField(urlRect, config.CustomBaseUrl);
                    if (string.IsNullOrEmpty(config.CustomBaseUrl)) Widgets.Label(urlRect, "  " + "ATC_CustomUrlOptional".Translate());
                }

                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                Rect rowB = apiListing.GetRect(30f);
                Rect keyRect = new Rect(rowB.x, rowB.y, rowB.width * 0.45f, rowB.height - 2f);

                config.Key = Widgets.TextField(keyRect, config.Key);
                if (string.IsNullOrEmpty(config.Key)) Widgets.Label(keyRect, "  " + "ATC_PasteKey".Translate());

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

                if (WorkflowUiStyle.Button(modelBtnRect, "▼", WorkflowButtonStyle.Dropdown,
                        !AutoTranslatorSettings.IsRunning && !config.IsFetching, GameFont.Tiny))
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

                DrawStructuredOutputSelector(apiListing, config, !AutoTranslatorSettings.IsRunning);

                Rect outputLimitRow = apiListing.GetRect(30f);
                Rect outputLimitLabelRect = new Rect(
                    outputLimitRow.x, outputLimitRow.y, outputLimitRow.width * 0.48f, outputLimitRow.height);
                Rect outputLimitInputRect = new Rect(
                    outputLimitRow.x + outputLimitRow.width * 0.5f, outputLimitRow.y,
                    outputLimitRow.width * 0.2f, outputLimitRow.height - 2f);
                Rect outputCapabilityRect = new Rect(
                    outputLimitRow.x + outputLimitRow.width * 0.72f, outputLimitRow.y,
                    outputLimitRow.width * 0.28f, outputLimitRow.height);
                Widgets.Label(outputLimitLabelRect, "ATC_ApiMaxOutputTokens".Translate());
                Widgets.TextFieldNumeric(
                    outputLimitInputRect,
                    ref config.AtcMaxOutputTokens,
                    ref config.AtcMaxOutputTokensBuffer,
                    1,
                    int.MaxValue);
                config.AtcMaxOutputTokens = Math.Max(1, config.AtcMaxOutputTokens);
                int? knownOutputLimit = config.GetKnownOutputTokenLimit(config.SelectedModel);
                Text.Font = GameFont.Tiny;
                GUI.color = knownOutputLimit.HasValue && config.AtcMaxOutputTokens > knownOutputLimit.Value
                    ? WorkflowUiStyle.WarningText
                    : WorkflowUiStyle.MutedText;
                Widgets.Label(
                    outputCapabilityRect,
                    knownOutputLimit.HasValue
                        ? "ATC_ModelOutputCapabilityKnown".Translate(
                            knownOutputLimit.Value,
                            config.ResolveActualOutputTokenLimit(config.SelectedModel))
                        : "ATC_ModelOutputCapabilityUnknown".Translate());
                Text.Font = GameFont.Small;
                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                if (Mouse.IsOver(outputLimitRow))
                    TooltipHandler.TipRegion(outputLimitRow, "ATC_ApiMaxOutputTokensTooltip".Translate());

                Rect rowC = apiListing.GetRect(24f);
                const float apiBottomGap = 8f;
                const float testButtonWidth = 120f;
                const float refetchButtonWidth = 140f;
                float taskTierWidth = Mathf.Max(
                    240f,
                    rowC.width - testButtonWidth - refetchButtonWidth - apiBottomGap * 2f);
                Rect taskTierRect = new Rect(rowC.x, rowC.y + 2f, taskTierWidth, rowC.height);
                Rect testBtnRect = new Rect(
                    taskTierRect.xMax + apiBottomGap,
                    rowC.y + 2f,
                    testButtonWidth,
                    rowC.height);
                Rect refetchBtnRect = new Rect(
                    testBtnRect.xMax + apiBottomGap,
                    rowC.y + 2f,
                    refetchButtonWidth,
                    rowC.height);

                DrawTranslationTaskTierSelector(
                    taskTierRect,
                    config,
                    !AutoTranslatorSettings.IsRunning);

                if (WorkflowUiStyle.Button(refetchBtnRect, "↻ " + "ATC_RefetchModels".Translate(),
                        WorkflowButtonStyle.Quiet, !AutoTranslatorSettings.IsRunning, GameFont.Tiny))
                {
                    if (!config.Enabled)
                    {
                        Messages.Message("ATC_Msg_ApiKeyDisabled".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                    else if (string.IsNullOrEmpty(config.Key) || config.Key.Length <= 10)
                    {
                        Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                    else if (!AutoTranslatorSettings.IsRunning &&
                             !AutoTranslatorAPI.HasOutstandingTranslationWork)
                    {
                        AutoTranslatorAPI.AutoFetchForConfig(config, true);
                    }
                }

                if (config.IsTesting)
                {
                    GUI.color = Color.yellow;
                    Widgets.Label(testBtnRect, "⏳ " + "ATC_Testing".Translate());
                }
                else
                {
                    bool canTestConnection = !AutoTranslatorSettings.IsRunning &&
                                             !AutoTranslatorAPI.HasOutstandingTranslationWork;
                    GUI.color = canTestConnection ? new Color(0.6f, 0.9f, 0.6f) : Color.grey;
                    if (WorkflowUiStyle.Button(testBtnRect, "🔌 " + "ATC_TestConnection".Translate(),
                            WorkflowButtonStyle.Primary, canTestConnection, GameFont.Tiny))
                    {
                        if (!config.Enabled)
                        {
                            Messages.Message("ATC_Msg_ApiKeyDisabled".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                        }
                        else if (string.IsNullOrEmpty(config.Key) || string.IsNullOrEmpty(config.SelectedModel))
                        {
                            Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                        }
                        else if (canTestConnection)
                        {

                            AutoTranslatorAPI.RunConnectionTest(config);
                        }
                    }
                }
                GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;

                apiListing.End();
                l.Gap(14f);
            }

            Rect addApiRect = l.GetRect(35f);
            if (WorkflowUiStyle.Button(addApiRect, "＋ " + "ATC_AddApiBtn".Translate(),
                    WorkflowButtonStyle.Primary, !AutoTranslatorSettings.IsRunning))
            {
                Settings.ApiConfigs.Add(new ApiKeyConfig());
            }
            GUI.color = Color.white;


            l.Gap(20f);
            DrawSettingsSectionHeader(l, WfText("性能与诊断", "Performance and diagnostics"));
            DrawPerformanceAndDiagnosticsSettings(l, viewRect);
            l.Gap(20f);

            DrawSettingsSectionHeader(l, WfText("高级兼容模式", "Advanced compatibility"));
            DrawCompatibilitySettings(l);
            l.Gap(20f);

            DrawSettingsSectionHeader(l, WfText("维护与恢复", "Maintenance and recovery"));
            Widgets.Label(l.GetRect(24f), WfText("修复旧译文格式", "Repair legacy translation format"));
            Rect repairLegacyBtnRect = l.GetRect(35f);
            GUI.color = _settingsLegacyRepairRunning ? Color.grey : new Color(0.6f, 0.9f, 0.75f);
            string repairLegacyLabel = _settingsLegacyRepairRunning
                ? "ATC_CheckingModStatus".Translate().ToString()
                : "🧰 " + "ATC_Btn_RepairLegacyTranslations".Translate().ToString();
            if (WorkflowUiStyle.Button(repairLegacyBtnRect, repairLegacyLabel,
                    WorkflowButtonStyle.Quiet, !_settingsLegacyRepairRunning) && !_settingsLegacyRepairRunning)
            {
                QueueLegacyRepairFromSettings();
            }
            l.Gap(15f);

            Widgets.Label(l.GetRect(24f), WfText("恢复最近备份", "Restore latest backup"));
            Rect restoreBtnRect = l.GetRect(35f);
            GUI.color = _settingsRestoreBackupRunning ? Color.grey : new Color(0.5f, 0.8f, 1f);
            string restoreLabel = _settingsRestoreBackupRunning
                ? "ATC_CheckingModStatus".Translate().ToString()
                : "↩ " + "ATC_Btn_RestoreLatestBackup".Translate().ToString();
            if (WorkflowUiStyle.Button(restoreBtnRect, restoreLabel,
                    WorkflowButtonStyle.Quiet, !_settingsRestoreBackupRunning) && !_settingsRestoreBackupRunning)
            {
                Find.WindowStack.Add(new Window_AtcDialog(
                    "ATC_Msg_ConfirmRestoreLatestBackup".Translate(),
                    WfText("确认还原", "Restore backup"),
                    () => {
                        QueueRestoreLatestBackupsFromSettings();
                    },
                    "ATC_Btn_Cancel".Translate(),
                    null,
                    "ATC_Btn_RestoreLatestBackup".Translate()
                ));
            }
            GUI.color = Color.white;
        }

        private static void DrawSettingsSectionHeader(Listing_Standard l, string title)
        {
            l.Gap(10f);
            Rect headerRect = l.GetRect(40f);
            Widgets.DrawBoxSolid(headerRect, WorkflowUiStyle.Header);
            WorkflowUiStyle.DrawBorder(
                headerRect,
                new Color(0.31f, 0.35f, 0.38f, 1f));
            Widgets.DrawBoxSolid(
                new Rect(headerRect.x, headerRect.y, 4f, headerRect.height),
                WorkflowUiStyle.GoodText);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(
                new Rect(
                    headerRect.x + 15f,
                    headerRect.y + 1f,
                    headerRect.width - 25f,
                    headerRect.height - 2f),
                title ?? string.Empty);
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
            l.Gap(12f);
        }

        private void DrawPerformanceAndDiagnosticsSettings(Listing_Standard l, Rect viewRect)
        {
            bool canEdit = !AutoTranslatorSettings.IsRunning;
            if (!canEdit) GUI.color = Color.grey;
            Rect threadRow = l.GetRect(30f);
            Settings.MaxThreads = (int)Widgets.HorizontalSlider(
                threadRow, Settings.MaxThreads, 1f, 30f, false,
                WfText("AI API 并发请求数：", "Concurrent AI API requests: ") + Settings.MaxThreads,
                "1", "30");
            TooltipHandler.TipRegion(threadRow, "ATC_MaxThreadsTip".Translate());
            l.Gap(10f);

            Rect dllRow = l.GetRect(30f);
            int dllConcurrency = Math.Max(1, Math.Min(8, Settings.DllAnalysisMaxConcurrency));
            int selected = (int)Widgets.HorizontalSlider(
                dllRow, dllConcurrency, 1f, 8f, false,
                WfText("DLL 分析并发 Mod 数：", "Concurrent DLL-analysis Mods: ") + dllConcurrency,
                "1", "8");
            if (canEdit) Settings.DllAnalysisMaxConcurrency = Math.Max(1, Math.Min(8, selected));
            TooltipHandler.TipRegion(
                dllRow,
                WfText(
                    "限制同时分析的 Mod 数量；每个 Mod 内的 DLL 仍按顺序分析，运行时 Harmony 扫描仍全局串行。本设置不影响大模型 API 并发。",
                    "Limits concurrent Mods. DLLs inside a Mod remain sequential and runtime Harmony scanning remains globally serialized."));
            l.Gap(10f);

            Rect timeoutRow = l.GetRect(30f);
            Settings.TimeoutSeconds = (int)Widgets.HorizontalSlider(
                timeoutRow, Settings.TimeoutSeconds, 15f, 600f, false,
                "ATC_Setting_Timeout".Translate(Settings.TimeoutSeconds.ToString()), "15", "600");
            TooltipHandler.TipRegion(timeoutRow, "ATC_Setting_Timeout_Tooltip".Translate());
            l.Gap(10f);

            Rect logRow = l.GetRect(30f);
            if (WorkflowUiStyle.Button(
                    logRow,
                    "ATC_LogLevel".Translate() + ": " + GetLogLevelLabel(Settings.LogLevel) + "  ▾",
                    WorkflowButtonStyle.Dropdown, canEdit))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (AtcLogLevel level in Enum.GetValues(typeof(AtcLogLevel)))
                {
                    AtcLogLevel captured = level;
                    options.Add(new FloatMenuOption(GetLogLevelLabel(captured), () =>
                    {
                        Settings.LogLevel = captured;
                        Settings.EnableDevelopmentDebugLogging = captured == AtcLogLevel.Debug;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(logRow, "ATC_LogLevelTooltip".Translate());
            GUI.color = Color.white;

            if (Settings.LogLevel == AtcLogLevel.Debug)
            {
                l.Gap(10f);
                DrawRuntimeProfilePanel(l, viewRect);
            }
        }

        private void DrawCompatibilitySettings(Listing_Standard l)
        {
            Rect toggleRect = l.GetRect(32f);
            if (WorkflowUiStyle.Button(
                    toggleRect,
                    (_settingsCompatibilityExpanded ? "▼ " : "▶ ") +
                    WfText("显示兼容选项", "Show compatibility options"),
                    WorkflowButtonStyle.Quiet))
                _settingsCompatibilityExpanded = !_settingsCompatibilityExpanded;
            if (!_settingsCompatibilityExpanded) return;

            l.Gap(5f);
            bool previousInterceptor = Settings.EnableUIInterceptor;
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUIInterceptor".Translate(), ref Settings.EnableUIInterceptor);
            if (previousInterceptor != Settings.EnableUIInterceptor)
            {
                if (Settings.EnableUIInterceptor) Settings.EnableHardcodedUiPrototype = false;
                TargetedHardcodedUi.HardcodedUiTargetedPatchManager.RequestReload();
            }
            GUI.color = Settings.EnableUIInterceptor ? Color.white : Color.grey;
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUINewTranslation".Translate(), ref Settings.EnableUINewTranslation);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUIErrorLogInterception".Translate(), ref Settings.EnableUIErrorLogInterception);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_ShowOriginalUI".Translate(), ref Settings.ShowOriginalUI);
            GUI.color = Color.white;
            DrawHardcodedUiPrototypeSettings(l);

            Rect clearRect = l.GetRect(35f);
            GUI.color = new Color(1f, 0.7f, 0.3f);
            if (WorkflowUiStyle.Button(clearRect, "🧹 " + "ATC_Btn_ClearUICache".Translate(), WorkflowButtonStyle.Quiet))
            {
                UIInterceptor.ClearUICache();
                Messages.Message("ATC_Msg_UICacheCleared".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            GUI.color = Color.white;
        }

        private void DrawTranslationUsageBudgetSettings(Listing_Standard l)
        {
            bool canEdit = !AutoTranslatorSettings.IsRunning;
            bool enabled = Settings.EnableTranslationUsageBudget;
            long characters = Math.Min(
                10000000L,
                Math.Max(100000L, Settings.TranslationBudgetSourceCharactersPerRun));

            GUI.color = canEdit ? Color.white : Color.grey;
            Rect enableRect = l.GetRect(30f);
            Widgets.CheckboxLabeled(enableRect, "ATC_UsageBudget_Enable".Translate(), ref enabled);
            if (Mouse.IsOver(enableRect))
                TooltipHandler.TipRegion(enableRect, "ATC_UsageBudget_EnableTooltip".Translate());

            GUI.color = canEdit && enabled ? Color.white : Color.grey;
            Rect characterRect = l.GetRect(30f);
            float sliderValue = Widgets.HorizontalSlider(
                characterRect,
                characters,
                100000f,
                10000000f,
                false,
                "ATC_UsageBudget_Characters".Translate(characters),
                "100000",
                "10000000");
            characters = Math.Max(100000L, (long)Math.Round(sliderValue / 100000f) * 100000L);

            Text.Font = GameFont.Tiny;
            Widgets.Label(l.GetRect(44f), "ATC_UsageBudget_Notice".Translate());
            Text.Font = GameFont.Small;

            if (canEdit)
            {
                Settings.EnableTranslationUsageBudget = enabled;
                Settings.TranslationBudgetSourceCharactersPerRun = characters;
                Settings.TranslationBudgetEstimatedTokensPerRun = Math.Max(
                    1000L,
                    (characters * 5L + 8L) / 9L);
            }
            GUI.color = Color.white;
        }

        private static string GetLogLevelLabel(AtcLogLevel level)
        {
            switch (level)
            {
                case AtcLogLevel.Error: return "ATC_LogLevel_Error".Translate();
                case AtcLogLevel.Debug: return "ATC_LogLevel_Debug".Translate();
                default: return "ATC_LogLevel_Info".Translate();
            }
        }

        private void DrawStructuredOutputSelector(Listing_Standard l, ApiKeyConfig config, bool canEdit)
        {
            if (config == null) return;

            Rect row = l.GetRect(30f);
            Rect selectorRect = new Rect(row.x, row.y, row.width * 0.47f, row.height - 2f);
            Rect statusRect = new Rect(row.x + row.width * 0.49f, row.y + 3f, row.width * 0.51f, row.height - 2f);
            bool supported = config.Provider != TranslatorProvider.DeepL;
            GUI.color = canEdit && supported ? Color.white : Color.grey;

            string preferenceLabel = GetStructuredOutputPreferenceLabel(config.StructuredOutput);
            if (WorkflowUiStyle.Button(
                    selectorRect,
                    "ATC_StructuredOutput_Label".Translate() + ": " + preferenceLabel + "  ▾",
                    WorkflowButtonStyle.Dropdown, canEdit && supported, GameFont.Tiny) &&
                canEdit && supported)
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (StructuredOutputPreference value in Enum.GetValues(typeof(StructuredOutputPreference)))
                {
                    StructuredOutputPreference captured = value;
                    options.Add(new FloatMenuOption(
                        GetStructuredOutputPreferenceLabel(captured),
                        () => config.StructuredOutput = captured));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(
                statusRect,
                "ATC_StructuredOutput_Effective".Translate(GetEffectiveStructuredOutputLabel(config)));
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(row, "ATC_StructuredOutput_Tooltip".Translate());
            GUI.color = Color.white;
        }

        private void DrawTerminologySettings(Listing_Standard l)
        {
            bool canEdit = !AutoTranslatorSettings.IsRunning;
            bool enabled = Settings.EnableTerminologyConsistency;
            GUI.color = canEdit ? Color.white : Color.gray;
            Rect enableRect = l.GetRect(30f);
            Widgets.CheckboxLabeled(enableRect, "ATC_Terminology_Enable".Translate(), ref enabled);
            TooltipHandler.TipRegion(enableRect, "ATC_Terminology_EnableTooltip".Translate());
            if (canEdit) Settings.EnableTerminologyConsistency = enabled;

            GUI.color = canEdit && enabled ? Color.white : Color.gray;
            Rect configureRect = l.GetRect(34f);
            if (WorkflowUiStyle.Button(configureRect,
                    "ATC_Terminology_Configure".Translate(Settings.TerminologyEnabledPackageIds.Count),
                    WorkflowButtonStyle.Quiet, canEdit && enabled) &&
                canEdit && enabled)
                Find.WindowStack.Add(new Window_TerminologySettings());
            GUI.color = Color.white;
        }

        private void DrawTranslationTaskTierSelector(Rect row, ApiKeyConfig config, bool canEdit)
        {
            if (config == null) return;
            bool hasOtherBulkFoundation = Settings.ApiConfigs != null && Settings.ApiConfigs.Any(candidate =>
                !ReferenceEquals(candidate, config) &&
                AutoTranslatorAPI.IsConfigReady(candidate) &&
                candidate.TaskTier == TranslationTaskTier.Bulk);
            bool canSelectOptionalTier = hasOtherBulkFoundation;

            GUI.color = canEdit ? Color.white : Color.grey;
            if (WorkflowUiStyle.Button(
                    row,
                    "ATC_TaskTier_Label".Translate() + ": " + GetTranslationTaskTierLabel(config.TaskTier) + "  ▾",
                    WorkflowButtonStyle.Dropdown, canEdit, GameFont.Tiny) &&
                canEdit)
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(
                        GetTranslationTaskTierLabel(TranslationTaskTier.Bulk),
                        () => config.TaskTier = TranslationTaskTier.Bulk)
                };
                if (canSelectOptionalTier)
                {
                    options.Add(new FloatMenuOption(
                        GetTranslationTaskTierLabel(TranslationTaskTier.Standard),
                        () => config.TaskTier = TranslationTaskTier.Standard));
                    options.Add(new FloatMenuOption(
                        GetTranslationTaskTierLabel(TranslationTaskTier.Precision),
                        () => config.TaskTier = TranslationTaskTier.Precision));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            TooltipHandler.TipRegion(
                row,
                (canSelectOptionalTier
                    ? "ATC_TaskTier_Tooltip"
                    : "ATC_TaskTier_RequiresBulk").Translate());
            GUI.color = Color.white;
        }

        private static string GetTranslationTaskTierLabel(TranslationTaskTier tier)
        {
            switch (tier)
            {
                case TranslationTaskTier.Standard:
                    return "ATC_TaskTier_Standard".Translate();
                case TranslationTaskTier.Precision:
                    return "ATC_TaskTier_Precision".Translate();
                default:
                    return "ATC_TaskTier_Bulk".Translate();
            }
        }

        private static string GetStructuredOutputPreferenceLabel(StructuredOutputPreference preference)
        {
            switch (preference)
            {
                case StructuredOutputPreference.PromptOnly:
                    return "ATC_StructuredOutput_PromptOnly".Translate();
                case StructuredOutputPreference.JsonObject:
                    return "ATC_StructuredOutput_JsonObject".Translate();
                case StructuredOutputPreference.JsonSchema:
                    return "ATC_StructuredOutput_JsonSchema".Translate();
                default:
                    return "ATC_StructuredOutput_Auto".Translate();
            }
        }

        private static string GetEffectiveStructuredOutputLabel(ApiKeyConfig config)
        {
            if (config == null || config.Provider == TranslatorProvider.DeepL)
                return "ATC_StructuredOutput_Native".Translate();

            if (config.Provider == TranslatorProvider.DeepSeek)
            {
                string baseUrl = string.IsNullOrWhiteSpace(config.CustomBaseUrl)
                    ? DeepSeekProviderAdapter.OfficialBaseUrl
                    : config.CustomBaseUrl;
                PolicyStructuredMode mode = PolicyStructuredProviderAdapter.ResolveMode(config, baseUrl);
                return mode == PolicyStructuredMode.DeepSeekFunction
                    ? "ATC_StructuredOutput_StrictFunction".Translate().ToString()
                    : GetPolicyStructuredModeLabel(mode);
            }

            StructuredTranslationMode translationMode = StructuredTranslationProviderAdapter.ResolveMode(config);
            switch (translationMode)
            {
                case StructuredTranslationMode.JsonObject:
                    return "ATC_StructuredOutput_JsonObject".Translate();
                case StructuredTranslationMode.JsonSchema:
                    return "ATC_StructuredOutput_JsonSchema".Translate();
                case StructuredTranslationMode.GeminiSchema:
                    return "ATC_StructuredOutput_GeminiSchema".Translate();
                default:
                    return "ATC_StructuredOutput_PromptOnly".Translate();
            }
        }

        private static string GetPolicyStructuredModeLabel(PolicyStructuredMode mode)
        {
            switch (mode)
            {
                case PolicyStructuredMode.JsonObject:
                    return "ATC_StructuredOutput_JsonObject".Translate();
                case PolicyStructuredMode.JsonSchema:
                    return "ATC_StructuredOutput_JsonSchema".Translate();
                case PolicyStructuredMode.GeminiSchema:
                    return "ATC_StructuredOutput_GeminiSchema".Translate();
                case PolicyStructuredMode.DeepSeekFunction:
                    return "ATC_StructuredOutput_StrictFunction".Translate();
                default:
                    return "ATC_StructuredOutput_PromptOnly".Translate();
            }
        }

        private void DrawHardcodedUiPrototypeSettings(Listing_Standard l)
        {
            bool previousEnabled = Settings.EnableHardcodedUiPrototype;
            bool enabled = previousEnabled;
            Rect enableRect = l.GetRect(30f);
            Widgets.CheckboxLabeled(
                enableRect,
                WfText("应用已保存的 DLL UI 译文", "Apply saved DLL UI translations"),
                ref enabled);
            if (Mouse.IsOver(enableRect))
            {
                TooltipHandler.TipRegion(enableRect, "ATC_HardcodedUi_EnablePrototypeTooltip".Translate());
            }

            if (enabled != previousEnabled)
            {
                Settings.EnableHardcodedUiPrototype = enabled;
                if (enabled)
                    Settings.EnableUIInterceptor = false;
                TargetedHardcodedUi.HardcodedUiTargetedPatchManager.RequestReload();
                WriteSettings();
            }

            Text.Font = GameFont.Tiny;
            Widgets.Label(l.GetRect(24f), "ATC_HardcodedUi_Status".Translate(
                TargetedHardcodedUi.HardcodedUiTargetedPatchManager.GetStatusLine()));
            Text.Font = GameFont.Small;

        }

        private static void QueueLegacyRepairFromSettings()
        {
            if (_settingsLegacyRepairRunning) return;
            _settingsLegacyRepairRunning = true;

            Task.Run(() =>
            {
                AutoTranslatorLegacyRepairer.RepairSummary summary = null;
                Exception failure = null;
                try
                {
                    summary = AutoTranslatorLegacyRepairer.RepairCurrentLanguagePack(requestMemoryDrop: true);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    _settingsLegacyRepairRunning = false;
                    if (failure != null)
                    {
                        Log.Warning($"[AutoTranslationCore] Legacy repair failed: {failure.Message}");
                        AutoTranslatorSettings.AddErrorLog("Legacy repair failed: " + failure.Message);
                        return;
                    }

                    summary = summary ?? new AutoTranslatorLegacyRepairer.RepairSummary();
                    Messages.Message(
                        "ATC_Msg_RepairLegacyTranslationsDone".Translate(summary.FilesTouched, summary.EntriesFixed, summary.StructureWarnings),
                        summary.FilesTouched > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                        false);
                });
            });
        }

        private static void QueueRestoreLatestBackupsFromSettings()
        {
            if (_settingsRestoreBackupRunning) return;
            _settingsRestoreBackupRunning = true;

            List<AutoTranslatorScanner.LocalTranslationRestoreTarget> targets = Verse.ModLister.AllInstalledMods
                .Where(m => m != null && m.Active && !string.IsNullOrWhiteSpace(m.PackageId))
                .Select(m => new AutoTranslatorScanner.LocalTranslationRestoreTarget { PackageId = m.PackageId })
                .ToList();

            Task.Run(() =>
            {
                int restored = 0;
                Exception failure = null;
                try
                {
                    restored = AutoTranslatorScanner.RestoreLatestBackups(targets);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    _settingsRestoreBackupRunning = false;
                    if (failure != null)
                    {
                        Log.Warning($"[AutoTranslationCore] Restore latest backups failed: {failure.Message}");
                        AutoTranslatorSettings.AddErrorLog("Restore latest backups failed: " + failure.Message);
                        return;
                    }

                    Messages.Message("ATC_Msg_RestoreLatestBackupDone".Translate(restored), restored > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
                });
            });
        }


// 這個方法負責繪製 執行期ProfilePanel 介面。
// EN: This method draws runtime profile panel.
private void DrawRuntimeProfilePanel(Listing_Standard l, Rect viewRect)
        {
            var profile = AutoTranslatorAPI.GetCurrentRuntimeProfile();
            Rect panelRect = l.GetRect(78f);
            Widgets.DrawBoxSolid(panelRect, WorkflowUiStyle.RaisedPanel);
            WorkflowUiStyle.DrawBorder(panelRect, new Color(0.31f, 0.35f, 0.38f));

            Text.Font = GameFont.Tiny;
            Rect left = new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width * 0.5f - 10f, panelRect.height - 8f);
            Rect right = new Rect(panelRect.x + panelRect.width * 0.52f, panelRect.y + 6f, panelRect.width * 0.48f - 10f, panelRect.height - 8f);

            string profileLine = "ATC_Profile_Current".Translate(
                profile.BatchSize.ToString(),
                profile.FormatRetries.ToString(),
                Settings.TimeoutSeconds.ToString());

            Widgets.Label(left,
                "⚙️ " + profileLine + "\n" +
                "🧭 " + profile.QualityHintKey.Translate());

            Widgets.Label(right,
                "📡 " + "ATC_Perf_Api".Translate(
                    AutoTranslatorPerf.ActiveApiRequests.ToString(),
                    AutoTranslatorPerf.AverageApiMs.ToString(),
                    AutoTranslatorPerf.LastApiMs.ToString()) + "\n" +
                "🪂 " + "ATC_Perf_MemoryDrop".Translate(
                    AutoTranslatorPerf.LastMemoryDropMs.ToString(),
                    AutoTranslatorPerf.LastMemoryDropKeyed.ToString(),
                    AutoTranslatorPerf.LastMemoryDropDefs.ToString()) + "\n" +
                "🛡️ " + "ATC_Perf_UI".Translate(
                    UIInterceptor.GetQueueCount().ToString(),
                    UIInterceptor.GetPendingCount().ToString(),
                    UIInterceptor.GetIgnoredCount().ToString()));

            Text.Font = GameFont.Small;
        }
    }
}
