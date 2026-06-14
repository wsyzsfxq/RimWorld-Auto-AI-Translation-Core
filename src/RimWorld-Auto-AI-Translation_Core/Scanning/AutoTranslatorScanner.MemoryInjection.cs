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

        private static void ResetValidationStats()
        {
            lock (_validationStats) _validationStats.Reset();
        }


        private static void AddValidationStat(Action<TranslationValidationStats> update)
        {
            lock (_validationStats) update(_validationStats);
        }


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


        /// <summary>
        /// 主執行緒派發器（由 GameComponent 每 Tick 呼叫）
        /// </summary>
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


        // ==========================================
        // 🚀 手術 1：把咪咪的神級空投引擎放在這！
        // (放在 GetSecondaryFolderNameByLanguage 和 IsOldVersionPath 之間)
        // ==========================================
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

                if (!Directory.Exists(langRoot)) return; // 沒快取就不用空投

                // 🪂 1. 空投 Keyed 字串
                string keyedPath = Path.Combine(langRoot, "Keyed");
                if (Directory.Exists(keyedPath))
                {
                    var keyedDict = LoadXmlFilesToDict(keyedPath);
                    foreach (var kvp in keyedDict)
                    {
                        // ✅ 修復 CS0117: 拔掉 isModded
                        activeLang.keyedReplacements[kvp.Key] = new LoadedLanguage.KeyedReplacement
                        {
                            key = kvp.Key,
                            value = kvp.Value
                        };
                        injectedKeyed++;
                    }
                }

                // 🪂 2. 空投 DefInjected (構造官方包裹)
                string defPath = Path.Combine(langRoot, "DefInjected");
                if (Directory.Exists(defPath))
                {
                    foreach (var typeDir in Directory.GetDirectories(defPath))
                    {
                        string defTypeName = Path.GetFileName(typeDir);
                        Type defType = GenTypes.GetTypeInAnyAssembly(defTypeName);
                        if (defType == null) continue;

                        // 尋找或建立官方包裹
                        DefInjectionPackage package = activeLang.defInjections.FirstOrDefault(p => p.defType == defType);
                        if (package == null)
                        {
                            package = new DefInjectionPackage(defType);
                            activeLang.defInjections.Add(package);
                        }

                        // ✅ 修復 CS0029: injections 是 Dictionary 而不是 List
                        if (package.injections == null)
                            package.injections = new Dictionary<string, DefInjectionPackage.DefInjection>();

                        var defDict = LoadXmlFilesToDict(typeDir);
                        foreach (var kvp in defDict)
                        {
                            // ✅ 修復 CS1061: 既然是字典，就不需要 RemoveAll，直接覆寫！
                            package.injections[kvp.Key] = new DefInjectionPackage.DefInjection
                            {
                                path = kvp.Key,
                                injection = kvp.Value
                            };
                            injectedDefs++;
                        }
                    }
                }

                // 💥 3. 引爆空投！強制呼叫官方的神聖綁定儀式！
                activeLang.InjectIntoData_BeforeImpliedDefs();
                activeLang.InjectIntoData_AfterImpliedDefs();

                // 🔧 咪咪特製：強制刷新隱式配方 (RecipeDef)！專治工作台清單不翻譯的 RimWorld 底層大坑！
                foreach (var recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
                {
                    // 抓出由 recipeMaker 自動生成的配方 (通常命名為 Make_XXX，且有產物)
                    if (recipe.defName.StartsWith("Make_") && recipe.products != null && recipe.products.Count > 0)
                    {
                        var productDef = recipe.products[0].thingDef;
                        if (productDef != null && !string.IsNullOrEmpty(productDef.label))
                        {
                            // 強制用官方的翻譯格式重新綁定最新的中文物品名！
                            recipe.label = "RecipeMake".Translate(productDef.label).Resolve();
                            if (recipe.jobString != null)
                            {
                                recipe.jobString = "RecipeMakeJobString".Translate(productDef.label).Resolve();
                            }
                        }
                    }
                }
                // 🔧 咪咪特製：強制刷新動物與種族的衍生物品 (屍體、肉、專屬皮)！
                // 專治動態生成導致空投後依然是英文的 Bug！
                foreach (var thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    // 只要這個物件有「種族/動物屬性 (race)」，且它已經有了翻譯好的中文名字
                    if (thingDef.race != null && !string.IsNullOrEmpty(thingDef.label))
                    {
                        // 1. 刷新屍體名稱 (🌟 防呆：只改系統為牠專門生成的屍體，例如 Corpse_Human，絕對不碰共用資源！)
                        if (thingDef.race.corpseDef != null && thingDef.race.corpseDef.defName == "Corpse_" + thingDef.defName)
                        {
                            thingDef.race.corpseDef.label = "CorpseLabel".Translate(thingDef.label).Resolve();
                        }

                        // 2. 刷新肉類名稱 (🌟 防呆：邊緣世界裡機械族的肉是「鋼鐵」，絕不能把鋼鐵改名！)
                        if (thingDef.race.meatDef != null && thingDef.race.meatDef.defName == "Meat_" + thingDef.defName)
                        {
                            thingDef.race.meatDef.label = "MeatLabel".Translate(thingDef.label).Resolve();
                        }

                        // 3. 刷新皮革名稱 (🌟 防呆：只改專屬皮革，不碰原版的輕皮革、平原皮革等共用皮)
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
                    // 🌟 咪咪特製：全面本地化！
                    AutoTranslatorSettings.AddLog("🪂 " + AutoTranslatorAPI.TranslateText("ATC_Log_MemoryDropSuccess", injectedKeyed, injectedDefs));
                    Log.Message($"[AutoTranslationCore] Memory Drop Success: Injected {injectedKeyed} Keyed & {injectedDefs} Defs without restart.");
                }
            }
            catch (Exception ex)
            {
                // 🌟 咪咪特製：錯誤訊息也要本地化！
                AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_MemoryDropFailed", ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop Failed: {ex.Message}");
            }
            finally
            {
                timer.Stop();
                AutoTranslatorPerf.RecordMemoryDrop(timer.ElapsedMilliseconds, injectedKeyed, injectedDefs);
            }
        }

        private static void ClearGlobalTranslationDatabase()
        {
            GlobalPrimaryDefDict.Clear();
            GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear();
            GlobalSecondaryKeyedDict.Clear();
            AutoTranslatorSettings.AddLog("🧹 " + "ATC_Log_Clean".Translate());
        }


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

                    // ✨ 架構師改造：加上 lang 參數
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

                    // 下方呼叫時加上語系判定
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
