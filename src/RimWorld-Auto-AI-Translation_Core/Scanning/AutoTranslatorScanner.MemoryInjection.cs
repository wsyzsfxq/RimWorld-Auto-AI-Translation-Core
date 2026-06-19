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
// 這個檔案負責把翻譯資料注入記憶體與主執行緒。
// EN: This file injects translation data into memory through the main thread.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {

        // 這個方法負責重置 ValidationStats 狀態。
        // EN: This method resets validation stats.
        private static void ResetValidationStats()
        {
            lock (_validationStats) _validationStats.Reset();
        }


        // 這個方法負責處理 AddValidationStat 相關流程。
        // EN: This method handles add validation stat.
        private static void AddValidationStat(Action<TranslationValidationStats> update)
        {
            lock (_validationStats) update(_validationStats);
        }


        // 這個方法負責處理 LogValidationSummary 相關流程。
        // EN: This method handles log validation summary.
        private static void LogValidationSummary()
        {
            TranslationValidationStats snapshot;
            lock (_validationStats)
            {
                snapshot = new TranslationValidationStats
                {
                    NewlineFixed = _validationStats.NewlineFixed,
                    RulePrefixFixed = _validationStats.RulePrefixFixed,
                    TokenFixed = _validationStats.TokenFixed,
                    StructureFallback = _validationStats.StructureFallback,
                    XmlKeySkipped = _validationStats.XmlKeySkipped,
                    EnglishResidualDetected = _validationStats.EnglishResidualDetected,
                    EnglishResidualRetried = _validationStats.EnglishResidualRetried,
                    EnglishResidualFallback = _validationStats.EnglishResidualFallback
                };
            }

            int total = snapshot.NewlineFixed + snapshot.RulePrefixFixed + snapshot.TokenFixed + snapshot.StructureFallback + snapshot.XmlKeySkipped
                + snapshot.EnglishResidualDetected + snapshot.EnglishResidualRetried + snapshot.EnglishResidualFallback;
            if (total <= 0)
            {
                AutoTranslatorSettings.AddLog("🩺 " + "ATC_Log_ValidationClean".Translate());
                return;
            }

            AutoTranslatorSettings.AddLog("🩺 " + "ATC_Log_ValidationSummary".Translate(
                snapshot.NewlineFixed,
                snapshot.RulePrefixFixed,
                snapshot.TokenFixed,
                snapshot.StructureFallback,
                snapshot.XmlKeySkipped));

            if (snapshot.EnglishResidualDetected > 0 || snapshot.EnglishResidualRetried > 0 || snapshot.EnglishResidualFallback > 0)
            {
                AutoTranslatorSettings.AddLog($"🩺 [Validation] English residual scan: detected {snapshot.EnglishResidualDetected}, retried {snapshot.EnglishResidualRetried}, still unresolved {snapshot.EnglishResidualFallback}.");
            }
        }


        // 這個方法負責送出 記憶體Drop 請求。
        // EN: This method requests memory drop.
        public static void RequestMemoryDrop()
        {
            if (UnityData.IsInMainThread)
            {
                MemoryDrop_InjectNow();
            }
            else
            {
                lock (_pendingInjectLock)
                {
                    _pendingMemoryDrop = true;
                }
                AutoTranslatorSettings.AddLog("🪂 " + "ATC_Log_MemoryDropQueued".Translate());
            }
        }


        // 這個方法負責處理 Pump主畫面執行緒派發器 相關流程。
        // EN: This method handles pump main thread dispatcher.
        internal static void PumpMainThreadDispatcher()
        {
            bool shouldInject = false;
            lock (_pendingInjectLock)
            {
                if (_pendingMemoryDrop)
                {
                    shouldInject = true;
                    _pendingMemoryDrop = false;
                }
            }
            if (shouldInject)
            {
                MemoryDrop_InjectNow();
            }
        }


        // 這個方法負責處理 記憶體DropInjectNow 相關流程。
        // EN: This method handles memory drop inject now.
        public static void MemoryDrop_InjectNow()
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            int injectedKeyed = 0;
            int injectedDefs = 0;
            try
            {
                LoadedLanguage activeLang = LanguageDatabase.activeLanguage;
                if (activeLang == null) return;

                string packPath = GetLocalPackPath();
                string targetFolder = GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string langRoot = Path.Combine(packPath, "Languages", targetFolder);

                if (!Directory.Exists(langRoot)) return;


                string keyedPath = Path.Combine(langRoot, "Keyed");
                if (Directory.Exists(keyedPath))
                {
                    var keyedDict = LoadXmlFilesToDict(keyedPath);
                    foreach (var kvp in keyedDict)
                    {

                        activeLang.keyedReplacements[kvp.Key] = new LoadedLanguage.KeyedReplacement
                        {
                            key = kvp.Key,
                            value = kvp.Value
                        };
                        injectedKeyed++;
                    }
                }


                string defPath = Path.Combine(langRoot, "DefInjected");
                if (Directory.Exists(defPath))
                {
                    foreach (var typeDir in Directory.GetDirectories(defPath))
                    {
                        string defTypeName = Path.GetFileName(typeDir);
                        Type defType = GenTypes.GetTypeInAnyAssembly(defTypeName);
                        if (defType == null) continue;


                        DefInjectionPackage package = activeLang.defInjections.FirstOrDefault(p => p.defType == defType);
                        if (package == null)
                        {
                            package = new DefInjectionPackage(defType);
                            activeLang.defInjections.Add(package);
                        }


                        if (package.injections == null)
                            package.injections = new Dictionary<string, DefInjectionPackage.DefInjection>();

                        var defDict = LoadXmlFilesToDict(typeDir);
                        foreach (var kvp in defDict)
                        {

                            package.injections[kvp.Key] = new DefInjectionPackage.DefInjection
                            {
                                path = kvp.Key,
                                injection = kvp.Value
                            };
                            injectedDefs++;
                        }
                    }
                }


                activeLang.InjectIntoData_BeforeImpliedDefs();
                activeLang.InjectIntoData_AfterImpliedDefs();


                foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
                {

                    if (recipe.defName.StartsWith("Make_") && recipe.products != null && recipe.products.Count > 0)
                    {
                        var productDef = recipe.products[0].thingDef;
                        if (productDef != null && !string.IsNullOrEmpty(productDef.label))
                        {

                            recipe.label = "RecipeMake".Translate(productDef.label).Resolve();
                            if (recipe.jobString != null)
                            {
                                recipe.jobString = "RecipeMakeJobString".Translate(productDef.label).Resolve();
                            }
                        }
                    }
                }


                foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
                {

                    if (thingDef.race != null && !string.IsNullOrEmpty(thingDef.label))
                    {

                        if (thingDef.race.corpseDef != null && thingDef.race.corpseDef.defName == "Corpse_" + thingDef.defName)
                        {
                            thingDef.race.corpseDef.label = "CorpseLabel".Translate(thingDef.label).Resolve();
                        }


                        if (thingDef.race.meatDef != null && thingDef.race.meatDef.defName == "Meat_" + thingDef.defName)
                        {
                            thingDef.race.meatDef.label = "MeatLabel".Translate(thingDef.label).Resolve();
                        }


                        if (thingDef.race.leatherDef != null && !string.IsNullOrEmpty(thingDef.race.leatherDef.label) && thingDef.race.leatherDef.defName == "Leather_" + thingDef.defName)
                        {
                            string engName = thingDef.defName;
                            if (thingDef.race.leatherDef.label.IndexOf(engName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                thingDef.race.leatherDef.label = System.Text.RegularExpressions.Regex.Replace(
                                    thingDef.race.leatherDef.label,
                                    engName,
                                    thingDef.label,
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                );
                            }
                        }
                    }
                }
                if (injectedKeyed > 0 || injectedDefs > 0)
                {

                    AutoTranslatorSettings.AddLog("🪂 " + AutoTranslatorAPI.TranslateText("ATC_Log_MemoryDropSuccess", injectedKeyed, injectedDefs));
                    Log.Message($"[AutoTranslationCore] Memory Drop Success: Injected {injectedKeyed} Keyed & {injectedDefs} Defs without restart.");
                }
            }
            catch (Exception ex)
            {

                AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_MemoryDropFailed", ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop Failed: {ex.Message}");
            }
            finally
            {
                timer.Stop();
                AutoTranslatorPerf.RecordMemoryDrop(timer.ElapsedMilliseconds, injectedKeyed, injectedDefs);
            }
        }

        // 這個方法負責清除 Global翻譯Database 資料。
        // EN: This method clears global translation database.
        private static void ClearGlobalTranslationDatabase()
        {
            GlobalPrimaryDefDict.Clear();
            GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear();
            GlobalSecondaryKeyedDict.Clear();
            AutoTranslatorSettings.AddLog("🧹 " + "ATC_Log_Clean".Translate());
        }


        // 這個方法負責建立 Global翻譯Database 所需資料。
        // EN: This method builds global translation database.
        private static void BuildGlobalTranslationDatabase(List<ModMetaData> mods)
        {
            AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_Init".Translate());

            var settings = AutoTranslatorMod.Settings;
            settings.SubTaskName = "ATC_SubTask_AnalyzingDict".Translate();
            settings.SubProgress = 0f;

            GlobalPrimaryDefDict.Clear(); GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear(); GlobalSecondaryKeyedDict.Clear();

            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);

            for (int i = 0; i < mods.Count; i++)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return;

                var mod = mods[i];

                settings.SubProgress = (float)i / mods.Count;
                settings.SubTaskName = AutoTranslatorAPI.TranslateText("ATC_SubTask_BuildingDict", mod.Name);

                var langRoots = GetAllEffectiveLangPaths(mod);

                foreach (var langRoot in langRoots)
                {
                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        var pKeyed = LoadXmlFilesToDict(Path.Combine(targetLangDir, "Keyed"));
                        foreach (var kv in pKeyed) GlobalPrimaryKeyedDict[kv.Key] = kv.Value;
                    }

                    if (!string.IsNullOrEmpty(otherFolder))
                    {
                        foreach (string secondaryLangDir in ResolveLanguageFolders(langRoot, otherFolder))
                        {
                            var sKeyed = LoadXmlFilesToDict(Path.Combine(secondaryLangDir, "Keyed"));
                            foreach (var kv in sKeyed) GlobalSecondaryKeyedDict[kv.Key] = kv.Value;
                        }
                    }


                    Action<string, Dictionary<string, string>, TargetLanguage?> loadDef = (path, dict, lang) => {
                        if (!Directory.Exists(path)) return;
                        foreach (var typeDir in Directory.GetDirectories(path))
                        {
                            string defType = Path.GetFileName(typeDir);
                            foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                            {
                                var d = LoadXmlFileToDict(file, lang);
                                foreach (var kv in d) dict[$"{defType}/{kv.Key}"] = kv.Value;
                            }
                        }
                        foreach (var file in Directory.GetFiles(path, "*.xml"))
                        {
                            var d = LoadXmlFileToDict(file, lang);
                            foreach (var kv in d) dict[$"General/{kv.Key}"] = kv.Value;
                        }
                    };


                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        loadDef(Path.Combine(targetLangDir, "DefInjected"), GlobalPrimaryDefDict, settings.TargetLang);
                    }

                    if (!string.IsNullOrEmpty(otherFolder))
                    {
                        TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                        foreach (string secondaryLangDir in ResolveLanguageFolders(langRoot, otherFolder))
                        {
                            loadDef(Path.Combine(secondaryLangDir, "DefInjected"), GlobalSecondaryDefDict, secLang);
                        }
                    }
                }
            settings.SubProgress = 1f;
            settings.SubTaskName = "ATC_SubTask_DictDone".Translate();
            AutoTranslatorSettings.AddLog("✨ " + AutoTranslatorAPI.TranslateText("ATC_Log_InitDone", GlobalPrimaryDefDict.Count));
            }
        }

    }
}
