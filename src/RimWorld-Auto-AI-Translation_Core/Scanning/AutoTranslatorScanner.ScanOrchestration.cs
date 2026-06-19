using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責單模組、多模組與全域掃描的流程控制。
// EN: This file orchestrates single-mod, multi-mod, and full translation scans.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        // 這個方法負責啟動 Single掃描 流程。
        // EN: This method starts single scan.
        public static void StartSingleScan(ModMetaData targetMod)
        {

            AutoTranslatorMod.Settings.SessionCharCount = 0;
            ResetValidationStats();

            AutoTranslatorSettings.IsRunning = true;

            var settings = AutoTranslatorMod.Settings;
            settings.CurrentProgress = 0f;
            settings.CurrentTaskName = $"Translating: {targetMod.Name}";
            AutoTranslatorSettings.AddLog("🚀 " + AutoTranslatorAPI.TranslateText("ATC_Log_StartSingleMod", targetMod.Name));


            var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();


            Task.Run(async () =>
            {
                try
                {
                    EnsurePackInitialized(runFullMaintenance: true);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate &&
                        !HasNativeOrExternalTargetLanguage(targetMod, AutoTranslatorMod.Settings.TargetLang))
                    {
                        var updatedTracker = ModUpdateDetector.GetUpdatedOrNewModsCached();
                        if (updatedTracker.Any(m => m.PackageId == targetMod.PackageId))
                        {
                            ClearOldTranslationFiles(new List<ModMetaData> { targetMod });
                        }
                    }

                    if (ShouldSkipNativeTargetTranslation(targetMod, settings.TargetLang, true))
                    {
                        ModUpdateDetector.MarkModAsTranslated(targetMod.PackageId, targetMod.RootDir.FullName, false);
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();
                        settings.CurrentProgress = 1f;
                        ModUpdateDetector.ClearStatusCache();
                        TranslationWorkbenchTab.RequestRefresh();
                        return;
                    }


                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return;
                    }


                    BuildGlobalTranslationDatabase(activeMods);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    var langRoots = GetAllEffectiveLangPaths(targetMod);
                    var defsRoots = GetAllEffectiveDefsPaths(targetMod);
                    bool hasLang = langRoots.Count > 0;
                    bool hasDefs = defsRoots.Count > 0;
                    int aiTranslatedCount = 0;

                    if (!hasLang && !hasDefs)
                    {
                        AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkipMod".Translate());
                    }
                    else
                    {
                        if (hasLang)
                        {
                            foreach (var langRoot in langRoots)
                            {
                                string englishKeyed = Path.Combine(langRoot, "English/Keyed");
                                if (Directory.Exists(englishKeyed))
                                {
                                    AutoTranslatorSettings.AddLog("⚙️ " + "ATC_Log_KeyedScan".Translate());
                                    aiTranslatedCount += await ProcessModKeyed(targetMod, englishKeyed);
                                }
                            }
                        }
                        if (AutoTranslatorSettings.IsCancellationRequested) return;
                        if (hasDefs || hasLang)
                        {
                            AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_DefScan".Translate());
                            aiTranslatedCount += await ProcessModDefInjected(targetMod, langRoots, defsRoots);
                        }


                        if (aiTranslatedCount > 0)
                        {
                            UpdateLocalModMeta(targetMod.PackageId, GetFolderNameByLanguage(settings.TargetLang), aiTranslatedCount);
                        }
                    }

                    if (!AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();

                        ModUpdateDetector.MarkModAsTranslated(targetMod.PackageId, targetMod.RootDir.FullName, false);
                        settings.CurrentProgress = 1f;
                        AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_SingleModDone".Translate());
                        LogValidationSummary();


                        RequestMemoryDrop();


                        AutoTranslatorSettings.ShowFinishPopup = true;
                        ModUpdateDetector.ClearStatusCache();
                        TranslationWorkbenchTab.RequestRefresh();
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_Log_TaskError", e.Message));
                    Log.Error($"[AutoTranslationCore] Single translation task interrupted: {e.Message}");
                }
                finally
                {
                    ClearGlobalTranslationDatabase();
                    AutoTranslatorSettings.IsRunning = false;
                }
            });
        }
        // 這個方法負責啟動 Full掃描 流程。
        // EN: This method starts full scan.
        public static void StartFullScan()
        {
            AutoTranslatorMod.Settings.SessionCharCount = 0;
            ResetValidationStats();
            AutoTranslatorSettings.IsRunning = true;

            var settings = AutoTranslatorMod.Settings;

            var mods = ModLister.AllInstalledMods.Where(m =>
                            !BlacklistedModules.Contains(m.PackageId.ToLower()) &&
                            (!settings.OnlyScanActiveMods || m.Active)).ToList();
            AutoTranslatorSettings.AddLog("🌐 " + AutoTranslatorAPI.TranslateText("ATC_Log_StartScan", mods.Count));


            Task.Run(async () =>
            {
                try
                {
                    EnsurePackInitialized(runFullMaintenance: true);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;


                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return;
                    }


                    BuildGlobalTranslationDatabase(mods);
                    int total = mods.Count;
                    int current = 0;
                    foreach (var mod in mods)
                    {


                        if (IsTranslationPatchMod(mod))
                        {
                            continue;
                        }

                        if (ShouldSkipNativeTargetTranslation(mod, settings.TargetLang, true))
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);
                            continue;
                        }


                        if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate)
                        {
                            var updatedTracker = ModUpdateDetector.GetUpdatedOrNewModsCached();
                            if (updatedTracker.Any(m => m.PackageId == mod.PackageId))
                            {
                                ClearOldTranslationFiles(new List<ModMetaData> { mod });
                            }
                        }
                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + AutoTranslatorAPI.TranslateText("ATC_Log_SkippedMod", mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                            continue;
                        }

                        current++;
                        settings.CurrentProgress = (float)current / total;
                        settings.CurrentTaskName = $"Translating: {mod.Name}";
                        settings.SubProgress = 0f;
                        settings.SubTaskName = "ATC_SubTask_Scanning".Translate();
                        AutoTranslatorSettings.AddLog("🔍 " + AutoTranslatorAPI.TranslateText("ATC_Log_ScanMod", mod.Name));

                        var langRoots = GetAllEffectiveLangPaths(mod);
                        var defsRoots = GetAllEffectiveDefsPaths(mod);
                        int aiTranslatedCount = 0;
                        if (langRoots.Count == 0 && defsRoots.Count == 0)
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);
                            continue;
                        }

                        if (langRoots.Count > 0)
                        {
                            foreach (var langRoot in langRoots)
                            {
                                string englishKeyed = Path.Combine(langRoot, "English/Keyed");
                                if (Directory.Exists(englishKeyed))
                                {
                                    settings.SubTaskName = "ATC_SubTask_TranslatingKeyed".Translate();
                                    aiTranslatedCount += await ProcessModKeyed(mod, englishKeyed);
                                }
                            }
                        }

                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested) { AutoTranslatorSettings.IsSkipCurrentRequested = false; continue; }

                        if (defsRoots.Count > 0 || langRoots.Count > 0)
                        {
                            settings.SubTaskName = "ATC_SubTask_TranslatingDef".Translate();
                            aiTranslatedCount += await ProcessModDefInjected(mod, langRoots, defsRoots);
                        }


                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);


                            if (aiTranslatedCount > 0)
                            {
                                UpdateLocalModMeta(mod.PackageId, GetFolderNameByLanguage(settings.TargetLang), aiTranslatedCount);
                            }
                        }

                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + AutoTranslatorAPI.TranslateText("ATC_Log_SkippedMod", mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                        }
                    }

                    if (!AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();
                        settings.CurrentProgress = 1f;
                        settings.SubTaskName = "";
                        settings.SubProgress = 1f;
                        AutoTranslatorSettings.AddLog("🎉 " + "ATC_Log_TaskDone".Translate());
                        AutoTranslatorSettings.AddLog("🎉 " + "ATC_Log_AllTranslationWritten".Translate());
                        LogValidationSummary();
                        RequestMemoryDrop();

                        AutoTranslatorSettings.ShowFinishPopup = true;
                        ModUpdateDetector.ClearStatusCache();
                        TranslationWorkbenchTab.RequestRefresh();
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog(AutoTranslatorAPI.TranslateText("ATC_Log_TaskError", e.Message));
                    AutoTranslatorSettings.AddLog($"[CRITICAL ERROR] {e.Message}");
                }
                finally
                {
                    ClearGlobalTranslationDatabase();
                    AutoTranslatorSettings.IsRunning = false;
                    if (AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "";
                        settings.CurrentProgress = 0f;
                        settings.SubTaskName = "";
                        settings.SubProgress = 0f;
                    }
                }
            });
        }


        // 這個方法負責啟動 Multi掃描 流程。
        // EN: This method starts multi scan.
        public static void StartMultiScan(List<ModMetaData> targetMods)
        {
            AutoTranslatorMod.Settings.SessionCharCount = 0;
            ResetValidationStats();
            AutoTranslatorSettings.IsRunning = true;


            var settings = AutoTranslatorMod.Settings;
            int total = targetMods.Count;
            AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_MultiScanStart".Translate(total));

            var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();

            Task.Run(async () =>
            {
                try
                {
                    EnsurePackInitialized(runFullMaintenance: true);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;


                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return;
                    }

                    BuildGlobalTranslationDatabase(activeMods);
                    int current = 0;
                    foreach (var mod in targetMods)
                    {
                        if (ShouldSkipNativeTargetTranslation(mod, settings.TargetLang, true))
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);
                            continue;
                        }


                        if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate)
                        {
                            var updatedTracker = ModUpdateDetector.GetUpdatedOrNewModsCached();
                            if (updatedTracker.Any(m => m.PackageId == mod.PackageId))
                            {
                                ClearOldTranslationFiles(new List<ModMetaData> { mod });
                            }
                        }
                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + AutoTranslatorAPI.TranslateText("ATC_Log_SkippedMod", mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                            continue;
                        }

                        current++;
                        settings.CurrentProgress = (float)current / total;
                        settings.CurrentTaskName = $"Translating: {mod.Name}";
                        settings.SubProgress = 0f;
                        settings.SubTaskName = "ATC_SubTask_Scanning".Translate();
                        AutoTranslatorSettings.AddLog(AutoTranslatorAPI.TranslateText("ATC_Log_ScanMod", mod.Name));

                        var langRoots = GetAllEffectiveLangPaths(mod);
                        var defsRoots = GetAllEffectiveDefsPaths(mod);
                        int aiTranslatedCount = 0;
                        if (langRoots.Count == 0 && defsRoots.Count == 0)
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);
                            continue;
                        }

                        if (langRoots.Count > 0)
                        {
                            foreach (var langRoot in langRoots)
                            {
                                string englishKeyed = Path.Combine(langRoot, "English/Keyed");
                                if (Directory.Exists(englishKeyed))
                                {
                                    settings.SubTaskName = "ATC_SubTask_TranslatingKeyed".Translate();
                                    aiTranslatedCount += await ProcessModKeyed(mod, englishKeyed);
                                }
                            }
                        }

                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested) { AutoTranslatorSettings.IsSkipCurrentRequested = false; continue; }

                        if (defsRoots.Count > 0 || langRoots.Count > 0)
                        {
                            settings.SubTaskName = "ATC_SubTask_TranslatingDef".Translate();
                            aiTranslatedCount += await ProcessModDefInjected(mod, langRoots, defsRoots);
                        }


                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName, false);
                        }


                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + AutoTranslatorAPI.TranslateText("ATC_Log_SkippedMod", mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                        }
                    }

                    if (!AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();
                        settings.CurrentProgress = 1f;
                        settings.SubTaskName = "";
                        settings.SubProgress = 1f;
                        AutoTranslatorSettings.AddLog("🎉 " + "ATC_Log_MultiScanDone".Translate());
                        LogValidationSummary();
                        RequestMemoryDrop();

                        AutoTranslatorSettings.ShowFinishPopup = true;
                        ModUpdateDetector.ClearStatusCache();
                        TranslationWorkbenchTab.RequestRefresh();
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog(AutoTranslatorAPI.TranslateText("ATC_Log_TaskError", e.Message));
                    AutoTranslatorSettings.AddLog($"[CRITICAL ERROR] {e.Message}");
                }
                finally
                {
                    ClearGlobalTranslationDatabase();
                    AutoTranslatorSettings.IsRunning = false;
                    if (AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "";
                        settings.CurrentProgress = 0f;
                        settings.SubTaskName = "";
                        settings.SubProgress = 0f;
                    }
                }
            });
        }

        // 這個方法負責判斷 ShouldSkipNative目標翻譯 條件是否成立。
        // EN: This method checks should skip native target translation.
        private static bool ShouldSkipNativeTargetTranslation(ModMetaData mod, TargetLanguage targetLang, bool clearLocalOverride)
        {
            if (mod == null) return true;
            if (IsTranslationPatchMod(mod)) return true;
            if (!HasNativeTargetLanguage(mod, targetLang))
            {
                if (TryGetActiveExternalTargetLanguagePatch(mod, targetLang, out string patchName, out string patchPackageId))
                {
                    AutoTranslatorSettings.AddLog($"[System] {mod.Name} is covered by translation patch {patchName} ({patchPackageId}); skipping AI overwrite.");
                    if (clearLocalOverride)
                    {
                        ClearOldTranslationFiles(new List<ModMetaData> { mod });
                    }
                    return true;
                }

                return false;
            }

            AutoTranslatorSettings.AddLog($"🛡️ [System] {mod.Name} already has native {GetFolderNameByLanguage(targetLang)} translations; skipping AI overwrite.");
            if (clearLocalOverride)
            {
                ClearOldTranslationFiles(new List<ModMetaData> { mod });
            }
            return true;
        }

        // 這個方法負責判斷 HasNativeOrExternal目標語言 條件是否成立。
        // EN: This method checks has native or external target language.
        private static bool HasNativeOrExternalTargetLanguage(ModMetaData mod, TargetLanguage targetLang)
        {
            if (mod == null) return false;
            if (HasNativeTargetLanguage(mod, targetLang)) return true;
            return HasActiveExternalTargetLanguagePatch(mod, targetLang);
        }
    }
}
