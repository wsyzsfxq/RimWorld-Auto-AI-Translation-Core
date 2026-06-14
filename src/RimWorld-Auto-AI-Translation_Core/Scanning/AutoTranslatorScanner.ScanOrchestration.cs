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

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {
        public static void StartSingleScan(ModMetaData targetMod) // ❌ 拔掉 async
        {
            // ✨ 順便補上：單次翻譯開始前，字數統計歸零
            AutoTranslatorMod.Settings.SessionCharCount = 0;
            ResetValidationStats();

            AutoTranslatorSettings.IsRunning = true;

            var settings = AutoTranslatorMod.Settings;
            settings.CurrentProgress = 0f;
            settings.CurrentTaskName = $"Translating: {targetMod.Name}";
            AutoTranslatorSettings.AddLog("🚀 " + AutoTranslatorAPI.TranslateText("ATC_Log_StartSingleMod", targetMod.Name));

            // 🌟 在主執行緒安全地抓取模組清單 (這個動作極快，不會卡頓)
            var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();

            // 🚀 架構師手術：把建置字典與翻譯的粗活，全部丟進背景執行緒，徹底解放主畫面 FPS！
            // 🚀 架構師手術：把建置字典與翻譯的粗活，全部丟進背景執行緒，徹底解放主畫面 FPS！
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
                        ModUpdateDetector.MarkModAsTranslated(targetMod.PackageId, targetMod.RootDir.FullName);
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();
                        settings.CurrentProgress = 1f;
                        return;
                    }

                    // ✨ 咪咪防呆結界：開工前先打電話給 API 查勤！
                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return; // 🛑 伺服器掛了，立刻煞車，絕對不准往下跑！
                    }

                    // 現在建置全域字典在背景跑了，UI 絕對不會再卡住！
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

                        // ✨ 如果這次有勞煩到 AI，立刻幫翻譯包更新身分證！
                        if (aiTranslatedCount > 0)
                        {
                            UpdateLocalModMeta(targetMod.PackageId, GetFolderNameByLanguage(settings.TargetLang), aiTranslatedCount);
                        }
                    }

                    if (!AutoTranslatorSettings.IsCancellationRequested)
                    {
                        settings.CurrentTaskName = "ATC_TaskDone".Translate();
                        // ✨ 打上時間戳記憶！下次就不會被當作未翻譯！
                        ModUpdateDetector.MarkModAsTranslated(targetMod.PackageId, targetMod.RootDir.FullName);
                        settings.CurrentProgress = 1f;
                        AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_SingleModDone".Translate());
                        LogValidationSummary();

                        // 修正 P2-1：用主執行緒守護器
                        RequestMemoryDrop();

                        // 修正 P2-3：用 ShowFinishPopup 旗標代替直接呼叫主執行緒 API
                        AutoTranslatorSettings.ShowFinishPopup = true;
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
        public static void StartFullScan() // ❌ 拔掉 async
        {
            AutoTranslatorMod.Settings.SessionCharCount = 0; // 🚀 大哥按下了按鈕，本次翻譯重新計數！
            ResetValidationStats();
            AutoTranslatorSettings.IsRunning = true;

            var settings = AutoTranslatorMod.Settings;
            // 🌟 在主執行緒安全地抓取模組清單，防閃退！
            var mods = ModLister.AllInstalledMods.Where(m =>
                            !BlacklistedModules.Contains(m.PackageId.ToLower()) &&
                            (!settings.OnlyScanActiveMods || m.Active)).ToList();
            AutoTranslatorSettings.AddLog("🌐 " + AutoTranslatorAPI.TranslateText("ATC_Log_StartScan", mods.Count));

            // 🌟 進入背景執行緒做苦力
            // 🌟 進入背景執行緒做苦力
            Task.Run(async () =>
            {
                try
                {
                    EnsurePackInitialized(runFullMaintenance: true);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    // ✨ 咪咪防呆結界：開工前先打電話給 API 查勤！
                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return; // 🛑 伺服器掛了，立刻煞車，絕對不准往下跑！
                    }

                    // 🧠 這裡傳入的 mods 包含漢化包！我們要在這裡把它們的翻譯精華全部吸進共用池！
                    BuildGlobalTranslationDatabase(mods);
                    int total = mods.Count;
                    int current = 0;
                    foreach (var mod in mods)
                    {
                        // 🛑 咪咪的微創攔截：大腦建完之後，準備要翻譯了！
                        // 如果這是漢化包，直接跳過不翻譯，避免產出雙胞胎 _zhtc_AutoTranslated.xml！
                        if (IsTranslationPatchMod(mod))
                        {
                            continue;
                        }

                        if (ShouldSkipNativeTargetTranslation(mod, settings.TargetLang, true))
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
                            continue;
                        }

                        // ✨ 架構師手術：多選/全掃描前核彈洗地                        // ✨ 架構師手術：多選/全掃描前核彈洗地
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
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
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

                        // ✨ 掃描完畢，如果沒被玩家按鈕跳過或停止，就標記此模組為已翻譯！
                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);

                            // ✨ 如果這是縫合怪，寫入身分證！
                            if (aiTranslatedCount > 0)
                            {
                                UpdateLocalModMeta(mod.PackageId, GetFolderNameByLanguage(settings.TargetLang), aiTranslatedCount);
                            }
                        }
                        // 🌟 咪咪特製：如果在 Def 或底層 API 階段被跳過，要在迴圈結束前攔截並把標籤洗掉！
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
                        // 🌟 發送信號給主執行緒，讓它去彈窗！絕對不閃退！
                        AutoTranslatorSettings.ShowFinishPopup = true;
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


        // 🌟 咪咪特製：專門處理 UI 多選的多模組非同步翻譯！
        public static void StartMultiScan(List<ModMetaData> targetMods) // ❌ 拔掉 async
        {
            AutoTranslatorMod.Settings.SessionCharCount = 0; // 🚀 大哥按下了按鈕，本次翻譯重新計數！
            ResetValidationStats();
            AutoTranslatorSettings.IsRunning = true;


            var settings = AutoTranslatorMod.Settings;
            int total = targetMods.Count;
            AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_MultiScanStart".Translate(total));
            // 🌟 在主執行緒安全地抓取啟動清單，防閃退！
            var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();

            Task.Run(async () =>
            {
                try
                {
                    EnsurePackInitialized(runFullMaintenance: true);
                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    // ✨ 咪咪防呆結界：開工前先打電話給 API 查勤！
                    settings.SubTaskName = "ATC_SubTask_TestingAPI".Translate();
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_PreflightCheck".Translate());

                    bool isApiAlive = await AutoTranslatorAPI.TestConnectionAsync();
                    if (!isApiAlive)
                    {
                        AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiDeadAbort".Translate());
                        settings.CurrentTaskName = "ATC_TaskFailed".Translate();
                        AutoTranslatorSettings.IsRunning = false;
                        return; // 🛑 伺服器掛了，立刻煞車，絕對不准往下跑！
                    }

                    BuildGlobalTranslationDatabase(activeMods);
                    int current = 0;
                    foreach (var mod in targetMods)
                    {
                        if (ShouldSkipNativeTargetTranslation(mod, settings.TargetLang, true))
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
                            continue;
                        }

                        // ✨ 架構師手術：多選/全掃描前核彈洗地
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
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
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

                        // ✨ 掃描完畢，如果沒被玩家按鈕跳過或停止，就標記此模組為已翻譯！
                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
                        }

                        // 🌟 咪咪特製：如果在 Def 或底層 API 階段被跳過，要在迴圈結束前攔截並把標籤洗掉！
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
                        RequestMemoryDrop();  // 修正 P2-1：改用主執行緒守護器                        
                        // 🌟 發送信號給主執行緒！
                        AutoTranslatorSettings.ShowFinishPopup = true;
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

        private static bool HasNativeOrExternalTargetLanguage(ModMetaData mod, TargetLanguage targetLang)
        {
            if (mod == null) return false;
            if (HasNativeTargetLanguage(mod, targetLang)) return true;
            return HasActiveExternalTargetLanguagePatch(mod, targetLang);
        }
    }
}
