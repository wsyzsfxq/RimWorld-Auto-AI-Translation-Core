using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    public partial class AutoTranslatorMod : Mod
    {
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
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUINewTranslation".Translate(), ref Settings.EnableUINewTranslation);
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_EnableUIErrorLogInterception".Translate(), ref Settings.EnableUIErrorLogInterception);
            GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
            Widgets.CheckboxLabeled(l.GetRect(30f), "ATC_TranslateWorkbenchModNames".Translate(), ref Settings.TranslateWorkbenchModNames);
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
                    {
                        TargetLanguage capturedLang = lang;
                        options.Add(new FloatMenuOption(GetLangLabel(lang), () => SetTargetLanguage(capturedLang)));
                    }
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

            DrawRuntimeProfilePanel(l, viewRect);
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

                Rect noteRow = l.GetRect(28f);
                Rect noteRect = new Rect(noteRow.x, noteRow.y, noteRow.width * 0.66f, noteRow.height - 2f);
                Rect enabledRect = new Rect(noteRow.x + noteRow.width * 0.69f, noteRow.y, noteRow.width * 0.31f, noteRow.height - 2f);
                config.Label = Widgets.TextField(noteRect, config.Label ?? "");
                if (string.IsNullOrEmpty(config.Label))
                {
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(noteRect.x + 5f, noteRect.y + 4f, noteRect.width - 10f, noteRect.height), "ATC_ApiKeyNoteHint".Translate());
                    Text.Font = GameFont.Small;
                    GUI.color = AutoTranslatorSettings.IsRunning ? Color.grey : Color.white;
                }
                Widgets.CheckboxLabeled(enabledRect, "ATC_ApiKeyEnabled".Translate(), ref config.Enabled);
                GUI.color = config.Enabled
                    ? (AutoTranslatorSettings.IsRunning ? Color.grey : Color.white)
                    : new Color(0.55f, 0.55f, 0.55f, 0.85f);

                Rect rowA = l.GetRect(30f);
                Rect providerRect = new Rect(rowA.x, rowA.y, rowA.width * 0.3f, rowA.height - 2f);
                if (Widgets.ButtonText(providerRect, "ATC_Provider".Translate() + ": " + config.Provider))
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
                string fetchFingerprint = AutoTranslatorAPI.GetModelFetchFingerprint(config);
                if (config.IsFetching && config.FetchStartedUtcTicks > 0)
                {
                    double elapsedSeconds = (DateTime.UtcNow.Ticks - config.FetchStartedUtcTicks) / (double)TimeSpan.TicksPerSecond;
                    if (elapsedSeconds > 45.0)
                    {
                        AutoTranslatorAPI.ResetModelFetchState(config);
                        AutoTranslatorSettings.AddErrorLog(AutoTranslatorAPI.TranslateText("ATC_Error_FetchModelsWatchdogReleased", config.Provider.ToString()));
                    }
                }

                bool shouldAutoFetchModels =
                    fetchFingerprint != config.lastFetchedKey &&
                    AutoTranslatorAPI.CleanInput(config.Key).Length > 10 &&
                    !config.IsFetching &&
                    !AutoTranslatorSettings.IsRunning &&
                    config.Enabled &&
                    (string.IsNullOrEmpty(config.PendingFetchFingerprint) ||
                     config.PendingFetchFingerprint != fetchFingerprint ||
                     AutoTranslatorAPI.CanAutoRetryModelFetch(config, fetchFingerprint));

                if (shouldAutoFetchModels)
                {
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
                Rect refetchBtnRect = new Rect(testBtnRect.xMax + 10f, rowC.y + 2f, 140f, rowC.height);

                if (Widgets.ButtonText(refetchBtnRect, "↻ " + "ATC_RefetchModels".Translate()))
                {
                    if (!config.Enabled)
                    {
                        Messages.Message("ATC_Msg_ApiKeyDisabled".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                    else if (string.IsNullOrEmpty(config.Key) || config.Key.Length <= 10)
                    {
                        Messages.Message("ATC_EmptyConfigWarning".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                    }
                    else if (!AutoTranslatorSettings.IsRunning)
                    {
                        AutoTranslatorAPI.AutoFetchForConfig(config, true);
                    }
                }

                if (config.IsTesting && config.TestStartedUtcTicks > 0)
                {
                    double elapsedSeconds = (DateTime.UtcNow.Ticks - config.TestStartedUtcTicks) / (double)TimeSpan.TicksPerSecond;
                    if (elapsedSeconds > Math.Max(30, AutoTranslatorMod.Settings.TimeoutSeconds + 15))
                    {
                        config.IsTesting = false;
                        config.TestStartedUtcTicks = 0L;
                        config.TestGeneration++;
                        AutoTranslatorSettings.AddErrorLog(AutoTranslatorAPI.TranslateText("ATC_Error_TestConnectionTimeout", config.Provider.ToString()));
                    }
                }

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
                        if (!config.Enabled)
                        {
                            Messages.Message("ATC_Msg_ApiKeyDisabled".Translate().ToString(), MessageTypeDefOf.RejectInput, false);
                        }
                        else if (string.IsNullOrEmpty(config.Key) || string.IsNullOrEmpty(config.SelectedModel))
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

            Rect repairLegacyBtnRect = l.GetRect(35f);
            GUI.color = new Color(0.6f, 0.9f, 0.75f);
            if (Widgets.ButtonText(repairLegacyBtnRect, "🧰 " + "ATC_Btn_RepairLegacyTranslations".Translate()))
            {
                var summary = AutoTranslatorLegacyRepairer.RepairCurrentLanguagePack(requestMemoryDrop: true);
                Messages.Message(
                    "ATC_Msg_RepairLegacyTranslationsDone".Translate(summary.FilesTouched, summary.EntriesFixed, summary.StructureWarnings),
                    summary.FilesTouched > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                    false);
            }
            l.Gap(5f);

            Rect restoreBtnRect = l.GetRect(35f);
            GUI.color = new Color(0.5f, 0.8f, 1f);
            if (Widgets.ButtonText(restoreBtnRect, "↩ " + "ATC_Btn_RestoreLatestBackup".Translate()))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "ATC_Msg_ConfirmRestoreLatestBackup".Translate(),
                    "ATC_Btn_Confirm".Translate(),
                    () => {
                        var mods = Verse.ModLister.AllInstalledMods.Where(m => m.Active).ToList();
                        int restored = AutoTranslatorScanner.RestoreLatestBackups(mods);
                        Messages.Message("ATC_Msg_RestoreLatestBackupDone".Translate(restored), restored > 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent, false);
                    },
                    "ATC_Btn_Cancel".Translate(),
                    null,
                    "ATC_Btn_RestoreLatestBackup".Translate()
                ));
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


private void DrawRuntimeProfilePanel(Listing_Standard l, Rect viewRect)
        {
            var profile = AutoTranslatorAPI.GetCurrentRuntimeProfile();
            Rect panelRect = l.GetRect(78f);
            Widgets.DrawBoxSolid(panelRect, new Color(0.06f, 0.07f, 0.08f, 0.85f));
            Widgets.DrawBox(panelRect, 1);

            Text.Font = GameFont.Tiny;
            Rect left = new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width * 0.5f - 10f, panelRect.height - 8f);
            Rect right = new Rect(panelRect.x + panelRect.width * 0.52f, panelRect.y + 6f, panelRect.width * 0.48f - 10f, panelRect.height - 8f);

            string profileLine = "ATC_Profile_Current".Translate(
                profile.BatchSize.ToString(),
                profile.FormatRetries.ToString(),
                profile.TimeoutFloorSeconds.ToString());

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
                AutoTranslatorMod.Settings.ModLastVerifiedFingerprints.Clear();
                LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

                // 4. 重建基礎資料夾與免疫標記，防止系統崩潰
                AutoTranslatorScanner.EnsurePackInitialized(runFullMaintenance: true);

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
    }
}
