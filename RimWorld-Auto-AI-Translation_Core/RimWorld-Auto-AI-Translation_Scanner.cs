using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static class AutoTranslatorScanner
    {
        // 🌟 咪咪特製：絕對不可翻譯的黑名單 (包含官方 DLC 與大哥的新模組 ID)
        private static readonly HashSet<string> BlacklistedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
            "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey",
            "auto.aitranslation.core", "aitranslation.pack" 
        };

        // ===== 主執行緒分派器 (修正 P2-1) =====
        // Unity 限制：DefDatabase / LanguageDatabase 等 API 必須在主執行緒呼叫
        // 背景執行緒（Task.Run）發出的注入請求會被排隊，由 GameComponent 在下個 Tick 派發
        private static readonly object _pendingInjectLock = new object();
        private static bool _pendingMemoryDrop = false;
        private static Dictionary<string, string> GlobalPrimaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalPrimaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        public static string GetLocalPackPath()
        {
            string rimWorldRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            return Path.Combine(rimWorldRoot, "Mods/!Translation_AI_Pack");
        }

        public static string GetFolderNameByLanguage(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ChineseTraditional";
                case TargetLanguage.Simplified: return "ChineseSimplified";
                case TargetLanguage.Japanese: return "Japanese";
                case TargetLanguage.Korean: return "Korean";
                case TargetLanguage.Russian: return "Russian";
                case TargetLanguage.Ukrainian: return "Ukrainian";
                case TargetLanguage.English: return "English";
                default: return "English";
            }
        }

        public static string GetSecondaryFolderNameByLanguage(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ChineseSimplified";
                case TargetLanguage.Simplified: return "ChineseTraditional";
                default: return null;
            }
        }

        // ==========================================
        // 🚀 手術 1：把咪咪的神級空投引擎放在這！
        // (放在 GetSecondaryFolderNameByLanguage 和 IsOldVersionPath 之間)
        // ==========================================
        public static void MemoryDrop_InjectNow()
        {
            try
            {
                LoadedLanguage activeLang = LanguageDatabase.activeLanguage;
                if (activeLang == null) return;

                string packPath = GetLocalPackPath();
                string targetFolder = GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string langRoot = Path.Combine(packPath, "Languages", targetFolder);

                if (!Directory.Exists(langRoot)) return; // 沒快取就不用空投

                int injectedKeyed = 0;
                int injectedDefs = 0;

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

                if (injectedKeyed > 0 || injectedDefs > 0)
                {
                    // 🌟 咪咪特製：全面本地化！
                    AutoTranslatorSettings.AddLog("🪂 " + "ATC_Log_MemoryDropSuccess".Translate(injectedKeyed, injectedDefs));
                    Log.Message($"[AutoTranslationCore] Memory Drop Success: Injected {injectedKeyed} Keyed & {injectedDefs} Defs without restart.");
                }
            }
            catch (Exception ex)
            {
                // 🌟 咪咪特製：錯誤訊息也要本地化！
                AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_MemoryDropFailed".Translate(ex.Message));
                Log.Error($"[AutoTranslationCore] Memory Drop Failed: {ex.Message}");
            }
        }

        private static bool IsOldVersionPath(string modRoot, string fullPath)
        {
            string relative = fullPath.Substring(modRoot.Length).Replace('\\', '/');
            string[] parts = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (part == "1.0" || part.StartsWith("1.0-") ||
                    part == "1.1" || part.StartsWith("1.1-") ||
                    part == "1.2" || part.StartsWith("1.2-") ||
                    part == "1.3" || part.StartsWith("1.3-") ||
                    part == "1.4" || part.StartsWith("1.4-"))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<string> ParseLoadFolders(ModMetaData mod)
        {
            List<string> activeFolders = new List<string>();
            string loadFolderXml = Path.Combine(mod.RootDir.FullName, "LoadFolders.xml");

            if (File.Exists(loadFolderXml))
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(loadFolderXml);

                    string[] versionsToCheck = { "v1.6", "v1.5", "1.6", "1.5" };
                    foreach (string ver in versionsToCheck)
                    {
                        XmlNode verNode = doc.SelectSingleNode($"//loadFolders/{ver}");
                        if (verNode != null)
                        {
                            foreach (XmlNode li in verNode.ChildNodes)
                            {
                                if (li.Name == "li" && !string.IsNullOrWhiteSpace(li.InnerText))
                                {
                                    string relativePath = li.InnerText.Trim().Replace('/', '\\');
                                    string folderPath = relativePath == "\\" || relativePath == ""
                                        ? mod.RootDir.FullName
                                        : Path.Combine(mod.RootDir.FullName, relativePath);

                                    if (Directory.Exists(folderPath)) activeFolders.Add(folderPath);
                                }
                            }
                            if (activeFolders.Count > 0) break;
                        }
                    }
                }
                catch { }
            }

            if (activeFolders.Count == 0)
            {
                activeFolders.Add(mod.RootDir.FullName);
            }

            return activeFolders.Distinct().ToList();
        }

        private static List<string> GetAllEffectiveLangPaths(ModMetaData mod)
        {
            List<string> result = new List<string>();
            var activeRoots = ParseLoadFolders(mod);

            foreach (var root in activeRoots)
            {
                try
                {
                    var dirs = Directory.GetDirectories(root, "Languages", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            if (result.Count == 0)
            {
                try
                {
                    var dirs = Directory.GetDirectories(mod.RootDir.FullName, "Languages", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            return result.Distinct().ToList();
        }

        private static List<string> GetAllEffectiveDefsPaths(ModMetaData mod)
        {
            List<string> result = new List<string>();
            var activeRoots = ParseLoadFolders(mod);

            foreach (var root in activeRoots)
            {
                try
                {
                    string defPath = Path.Combine(root, "Defs");
                    if (Directory.Exists(defPath) && !IsOldVersionPath(mod.RootDir.FullName, defPath))
                    {
                        result.Add(defPath);
                    }
                }
                catch { }
            }

            if (result.Count == 0)
            {
                try
                {
                    var dirs = Directory.GetDirectories(mod.RootDir.FullName, "Defs", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            return result.Distinct().ToList();
        }

        public static void MigrateOldTranslations()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                foreach (var langDir in Directory.GetDirectories(langsPath))
                {
                    string defInjectedDir = Path.Combine(langDir, "DefInjected");
                    if (!Directory.Exists(defInjectedDir)) continue;

                    foreach (var maybePackageDir in Directory.GetDirectories(defInjectedDir))
                    {
                        string packageName = Path.GetFileName(maybePackageDir);
                        bool isOldPackageStructure = false;

                        foreach (var defTypeDir in Directory.GetDirectories(maybePackageDir))
                        {
                            string defType = Path.GetFileName(defTypeDir);
                            string oldFile = Path.Combine(defTypeDir, "AutoTranslated_Defs.xml");

                            if (File.Exists(oldFile))
                            {
                                isOldPackageStructure = true;
                                string newTargetDir = Path.Combine(defInjectedDir, defType);
                                Directory.CreateDirectory(newTargetDir);

                                string cleanPackageName = packageName.Replace(".", "_");
                                string newFile = Path.Combine(newTargetDir, $"{cleanPackageName}_AutoTranslated.xml");

                                if (File.Exists(newFile))
                                {
                                    var oldDict = LoadXmlFileToDict(oldFile);
                                    var newDict = LoadXmlFileToDict(newFile);
                                    foreach (var kv in oldDict) newDict[kv.Key] = kv.Value;
                                    SaveXml(newFile, newDict);
                                    File.Delete(oldFile);
                                }
                                else
                                {
                                    File.Move(oldFile, newFile);
                                }
                                AutoTranslatorSettings.AddLog("ATC_Log_MigrateSuccess".Translate(packageName, defType));
                            }
                        }

                        if (isOldPackageStructure)
                        {
                            try
                            {
                                if (Directory.GetFiles(maybePackageDir, "*", SearchOption.AllDirectories).Length == 0)
                                {
                                    Directory.Delete(maybePackageDir, true);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Migration system error: {ex.Message}");
            }
        }

        private static void CleanupSelfTranslations()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                string[] forbiddenPrefixes = { "auto_aitranslation_core", "aitranslation_pack" };

                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                foreach (var file in allXmls)
                {
                    string fileName = Path.GetFileName(file).ToLower();
                    foreach (var prefix in forbiddenPrefixes)
                    {
                        if (fileName.StartsWith(prefix))
                        {
                            File.Delete(file);
                            AutoTranslatorSettings.AddLog($"🗑️ [System] 發現並刪除誤翻譯檔案 / Deleted rogue file: {Path.GetFileName(file)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 清理自我翻譯檔時發生錯誤 / Self-cleanup error: {ex.Message}");
            }
        }

        public static void EnsurePackInitialized()
        {
            string packPath = GetLocalPackPath();
            string aboutPath = Path.Combine(packPath, "About/About.xml");
            if (!File.Exists(aboutPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(aboutPath));
                File.WriteAllText(aboutPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModMetaData><name>! AutoTranslation AI Pack</name><author>Auto Translator Core</author><packageId>AITranslation.Pack</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>");
            }

            CleanupSelfTranslations();
            MigrateOldTranslations();
            ApplyEmergencyHotfix();
            RunDetoxScanner(); // 這是原本清垃圾空白的
            RunAdvancedDetoxScanner(); // 🌟 咪咪新增：這是我們保護玩家錢包的高級手術！
        }
        public static async void StartSingleScan(ModMetaData targetMod)
        {
            try
            {
                AutoTranslatorSettings.IsRunning = true;
                EnsurePackInitialized();

                // ✨ 架構師手術：單選掃描前核彈洗地
                if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate)
                {
                    var updatedTracker = ModUpdateDetector.GetUpdatedOrNewModsCached();
                    if (updatedTracker.Any(m => m.PackageId == targetMod.PackageId))
                    {
                        ClearOldTranslationFiles(new List<ModMetaData> { targetMod });
                    }
                }
                var settings = AutoTranslatorMod.Settings;
                settings.CurrentProgress = 0f;
                settings.CurrentTaskName = $"Translating: {targetMod.Name}";
                AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_StartSingleMod".Translate(targetMod.Name));

                var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();
                BuildGlobalTranslationDatabase(activeMods);
                if (AutoTranslatorSettings.IsCancellationRequested) return;

                var langRoots = GetAllEffectiveLangPaths(targetMod);
                var defsRoots = GetAllEffectiveDefsPaths(targetMod);
                bool hasLang = langRoots.Count > 0;
                bool hasDefs = defsRoots.Count > 0;

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
                                await ProcessModKeyed(targetMod, englishKeyed);
                            }
                        }
                    }
                    if (AutoTranslatorSettings.IsCancellationRequested) return;
                    if (hasDefs || hasLang)
                    {
                        AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_DefScan".Translate());
                        await ProcessModDefInjected(targetMod, langRoots, defsRoots);
                    }
                }

                if (!AutoTranslatorSettings.IsCancellationRequested)
                {
                    settings.CurrentTaskName = "ATC_TaskDone".Translate();
                    // ✨ 打上時間戳記憶！下次就不會被當作未翻譯！
                    ModUpdateDetector.MarkModAsTranslated(targetMod.PackageId, targetMod.RootDir.FullName);
                    settings.CurrentProgress = 1f;
                    AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_SingleModDone".Translate());

                    // 修正 P2-1：用主執行緒守護器
                    RequestMemoryDrop();

                    // 修正 P2-3：用 ShowFinishPopup 旗標代替直接呼叫主執行緒 API
                    // 由 DoSettingsWindowContents 在下個 OnGUI 幀消費這個旗標
                    AutoTranslatorSettings.ShowFinishPopup = true;
                }
            }
            catch (Exception e)
            {
                AutoTranslatorSettings.AddLog("❌ " + "ATC_Log_TaskError".Translate(e.Message));
                Log.Error($"[AutoTranslationCore] Single translation task interrupted: {e.Message}");
            }
            finally
            {
                ClearGlobalTranslationDatabase();
                AutoTranslatorSettings.IsRunning = false;
            }
        }
        public static void StartFullScan() // ❌ 拔掉 async
        {
            AutoTranslatorMod.Settings.SessionCharCount = 0; // 🚀 大哥按下了按鈕，本次翻譯重新計數！
            AutoTranslatorSettings.IsRunning = true;
            EnsurePackInitialized();

            var settings = AutoTranslatorMod.Settings;
            // 🌟 在主執行緒安全地抓取模組清單，防閃退！
            var mods = ModLister.AllInstalledMods.Where(m =>
                !BlacklistedModules.Contains(m.PackageId.ToLower()) &&
                (!settings.OnlyScanActiveMods || m.Active)).ToList();
            AutoTranslatorSettings.AddLog("🌐 " + "ATC_Log_StartScan".Translate(mods.Count));

            // 🌟 進入背景執行緒做苦力
            Task.Run(async () =>
            {
                try
                {
                    BuildGlobalTranslationDatabase(mods);
                    int total = mods.Count;
                    int current = 0;
                    foreach (var mod in mods)
                    {
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
                            AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkippedMod".Translate(mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                            continue;
                        }

                        current++;
                        settings.CurrentProgress = (float)current / total;
                        settings.CurrentTaskName = $"Translating: {mod.Name}";
                        settings.SubProgress = 0f;
                        settings.SubTaskName = "ATC_SubTask_Scanning".Translate();
                        AutoTranslatorSettings.AddLog("🔍 " + "ATC_Log_ScanMod".Translate(mod.Name));

                        var langRoots = GetAllEffectiveLangPaths(mod);
                        var defsRoots = GetAllEffectiveDefsPaths(mod);
                        if (langRoots.Count == 0 && defsRoots.Count == 0)
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
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
                                    await ProcessModKeyed(mod, englishKeyed);
                                }
                            }
                        }

                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested) { AutoTranslatorSettings.IsSkipCurrentRequested = false; continue; }

                        if (defsRoots.Count > 0 || langRoots.Count > 0)
                        {
                            settings.SubTaskName = "ATC_SubTask_TranslatingDef".Translate();
                            await ProcessModDefInjected(mod, langRoots, defsRoots);
                        }

                        // ✨ 掃描完畢，如果沒被玩家按鈕跳過或停止，就標記此模組為已翻譯！
                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
                        }

                        // 🌟 咪咪特製：如果在 Def 或底層 API 階段被跳過，要在迴圈結束前攔截並把標籤洗掉！
                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkippedMod".Translate(mod.Name));
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
                        RequestMemoryDrop();
                        // 🌟 發送信號給主執行緒，讓它去彈窗！絕對不閃退！
                        AutoTranslatorSettings.ShowFinishPopup = true;
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
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
            AutoTranslatorSettings.IsRunning = true;
            EnsurePackInitialized();


            var settings = AutoTranslatorMod.Settings;
            int total = targetMods.Count;
            AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_MultiScanStart".Translate(total));
            // 🌟 在主執行緒安全地抓取啟動清單，防閃退！
            var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();

            Task.Run(async () =>
            {
                try
                {
                    BuildGlobalTranslationDatabase(activeMods);
                    int current = 0;
                    foreach (var mod in targetMods)
                    {
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
                            AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkippedMod".Translate(mod.Name));
                            AutoTranslatorSettings.IsSkipCurrentRequested = false;
                            continue;
                        }

                        current++;
                        settings.CurrentProgress = (float)current / total;
                        settings.CurrentTaskName = $"Translating: {mod.Name}";
                        settings.SubProgress = 0f;
                        settings.SubTaskName = "ATC_SubTask_Scanning".Translate();
                        AutoTranslatorSettings.AddLog("ATC_Log_ScanMod".Translate(mod.Name));

                        var langRoots = GetAllEffectiveLangPaths(mod);
                        var defsRoots = GetAllEffectiveDefsPaths(mod);
                        if (langRoots.Count == 0 && defsRoots.Count == 0)
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
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
                                    await ProcessModKeyed(mod, englishKeyed);
                                }
                            }
                        }

                        if (AutoTranslatorSettings.IsCancellationRequested) break;
                        if (AutoTranslatorSettings.IsSkipCurrentRequested) { AutoTranslatorSettings.IsSkipCurrentRequested = false; continue; }

                        if (defsRoots.Count > 0 || langRoots.Count > 0)
                        {
                            settings.SubTaskName = "ATC_SubTask_TranslatingDef".Translate();
                            await ProcessModDefInjected(mod, langRoots, defsRoots);
                        }

                        // ✨ 掃描完畢，如果沒被玩家按鈕跳過或停止，就標記此模組為已翻譯！
                        if (!AutoTranslatorSettings.IsSkipCurrentRequested && !AutoTranslatorSettings.IsCancellationRequested)
                        {
                            ModUpdateDetector.MarkModAsTranslated(mod.PackageId, mod.RootDir.FullName);
                        }

                        // 🌟 咪咪特製：如果在 Def 或底層 API 階段被跳過，要在迴圈結束前攔截並把標籤洗掉！
                        if (AutoTranslatorSettings.IsSkipCurrentRequested)
                        {
                            AutoTranslatorSettings.AddLog("⏭️ " + "ATC_Log_SkippedMod".Translate(mod.Name));
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
                        RequestMemoryDrop();  // 修正 P2-1：改用主執行緒守護器                        
                        // 🌟 發送信號給主執行緒！
                        AutoTranslatorSettings.ShowFinishPopup = true;
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
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
                settings.SubTaskName = "ATC_SubTask_BuildingDict".Translate(mod.Name);

                var langRoots = GetAllEffectiveLangPaths(mod);

                foreach (var langRoot in langRoots)
                {
                    var pKeyed = LoadXmlFilesToDict(Path.Combine(langRoot, targetFolder, "Keyed"));
                    foreach (var kv in pKeyed) GlobalPrimaryKeyedDict[kv.Key] = kv.Value;

                    if (!string.IsNullOrEmpty(otherFolder))
                    {
                        var sKeyed = LoadXmlFilesToDict(Path.Combine(langRoot, otherFolder, "Keyed"));
                        foreach (var kv in sKeyed) GlobalSecondaryKeyedDict[kv.Key] = kv.Value;
                    }

                    Action<string, Dictionary<string, string>> loadDef = (path, dict) => {
                        if (!Directory.Exists(path)) return;
                        foreach (var typeDir in Directory.GetDirectories(path))
                        {
                            string defType = Path.GetFileName(typeDir);
                            foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                            {
                                var d = LoadXmlFileToDict(file);
                                foreach (var kv in d) dict[$"{defType}/{kv.Key}"] = kv.Value;
                            }
                        }
                        foreach (var file in Directory.GetFiles(path, "*.xml"))
                        {
                            var d = LoadXmlFileToDict(file);
                            foreach (var kv in d) dict[$"General/{kv.Key}"] = kv.Value;
                        }
                    };

                    loadDef(Path.Combine(langRoot, targetFolder, "DefInjected"), GlobalPrimaryDefDict);

                    if (!string.IsNullOrEmpty(otherFolder))
                        loadDef(Path.Combine(langRoot, otherFolder, "DefInjected"), GlobalSecondaryDefDict);
                }
            }
            settings.SubProgress = 1f;
            settings.SubTaskName = "ATC_SubTask_DictDone".Translate();
            AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_InitDone".Translate(GlobalPrimaryDefDict.Count));
        }

        // 🌟 咪咪原本就在這的標籤清單（大哥，這段要留著喔！）
        private static readonly HashSet<string> ExactTextTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "label", "description", "jobString", "reportString", "text", "labelShort", "customLabel", "descriptionShort",
            "pawnLabel", "gerund", "verb", "deathMessage", "inspectString", "baseInspectString", "helpText",
            "letterLabel", "letterText", "message", "messageSuccess", "messageFailed", "rejectInputMessage",
            "skillLabel", "endMessage", "beginLetterLabel", "beginLetter", "recoveryMessage", "destroyedLabel",
            "pawnSingular", "pawnPlural", "leaderTitle", "adjective", "royalFavorLabel", "arrivalText", "arrivalTextEnemy",
            "logRulesInitiator", "logRulesRecipient", "useLabel", "ingestCommandString", "ingestReportString",
            "meatLabel", "corpseLabel", "discoverLetterTitle", "discoverLetterText", "letterLabelEnemy", "letterTextEnemy",
            "commandLabel", "commandDescription", "formatString", "outfitName", "labelNoun", "labelNounPretty"
        };

        // 🌟 咪咪新增：標籤黑名單
        // 🌟 咪咪新增：從開源模組移植過來的「絕對不能翻」標籤清單 ＋ 咪咪的 RimWorld 底層防禦包
        private static readonly HashSet<string> BlacklistedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // ===== 第一類：資源路徑與特效 =====
    "alienRace", "texPath", "graphicPath", "soundDef", "effecter",
    "iconPath", "shader", "soundCast", "soundCastTail", "soundInteract",
    "soundHitPawn", "soundMiss", "soundMeleeHit", "soundMeleeMiss",
    "soundAmbience", "linkSound", "fleckDef",

    // ===== 第二類：Def 引用 =====
    "thingDef", "itemDef", "pawnKindDef", "hediffDef", "recipeDef",
    "researchProjectDef", "terrainDef", "traitDef", "skillDef",
    "damageDef", "weaponDef", "apparelDef", "projectileDef",

    // ===== 第三類：底層程式變數 =====
    "defName", "dollName", "dollPartName", "methodName", "class", "worker",

    // ===== 第四類：NL Facial Animation 系列（修正 Bug A/G）=====
    // 解決：NL 系列種族頭部、眼部、嘴部貼圖消失問題
    "eyeTexPath", "browTexPath", "lidTexPath", "lashTexPath",
    "mouthTexPath", "noseTexPath", "earTexPath", "hairTexPath",
    "headTexPath", "bodyTexPath", "skinTexPath",
    "eyeballTexPath", "irisTexPath", "pupilTexPath",
    "expressionPath", "animationPath", "facialDef",

    // ===== 第五類：Humanoid Alien Races (HAR) 框架 =====
    // 解決：米莉拉、沃芬等人工種族身體頭部消失
    "bodyGraphicData", "headGraphicData", "graphicData",
    "bodyAddon", "bodyAddons", "headAddons", "bodyPart",
    "skinColorChannel", "hairColorChannel", "channelName",
    "linkedBodyPartsGroup", "renderNodeProperties",
    "maskPath", "shaderType", "subPath",

    // ===== 第六類：通用引用型欄位（防誤翻）=====
    "li_ref", "parent", "parentName", "abstract", "inherit",
    "compClass", "thingClass", "race", "category", "categories",
    "tradeTags", "weaponTags", "apparelTags", "tags",
    "linkFlags", "renderNodeTagDef", "tagDef"
};        // 🌟 咪咪新增：檔案格式偵測 Regex
        private static readonly Regex FilePathRegex = new Regex(@"\.(png|jpg|jpeg|wav|mp3|ogg|xml|txt|lua|tex|dds)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 🌟 咪咪升級版：過濾器本人
        // 🌟 咪咪升級版：結合標籤名稱與內容分析的終極過濾器
        private static bool IsTranslationTarget(string tagName, string value)
        {
            // 1. 基本檢查：太短、純數字、純符號的直接踢掉
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
            if (value.All(char.IsDigit) || Regex.IsMatch(value, @"^[^\w\s]+$")) return false;

            // ==========================================
            // 🌟 咪咪補破網：專殺假冒文字的底層變數！
            // 只要結尾是這幾個字，管他叫什麼名字，一律不准翻！
            // ==========================================
            string lower = tagName.ToLower(); // 👈 這裡宣告一次就好了！
            if (lower.EndsWith("defname") || lower.EndsWith("dollname") ||
                lower.EndsWith("dollpartname") || lower.EndsWith("methodname") ||
                lower.EndsWith("class") || lower.EndsWith("worker") || lower.EndsWith("def"))
                return false;

            // 2. 標籤黑名單：如果是 texPath, soundDef 這種直接拒絕
            if (BlacklistedFields.Contains(tagName)) return false;

            // 3. 內容特徵分析：
            // 如果包含斜線但沒有空格 -> 判定為路徑
            if ((value.Contains("/") || value.Contains("\\")) && !value.Contains(" ")) return false;

            // 如果包含底線但沒有空格 -> 判定為程式碼 ID (如 Apparel_Pants_Worker)
            if (value.Contains("_") && !value.Contains(" ")) return false;

            // 檢查是否包含檔案副檔名
            if (FilePathRegex.IsMatch(value)) return false;

            // 4. 最後判定：符合常用翻譯標籤或結尾才放行
            if (ExactTextTags.Contains(tagName)) return true;

            // 👈 這裡把重複宣告的 string lower 刪掉了，直接用！
            return lower.EndsWith("label") || lower.EndsWith("description") ||
                   lower.EndsWith("string") || lower.EndsWith("text") ||
                   lower.EndsWith("message") || lower.EndsWith("name") ||
                   lower.EndsWith("desc");
        }
        private static void TraverseDefNode(XmlNode node, string currentPath, string defType, Dictionary<string, Dictionary<string, string>> result)
        {
            int liIndex = 0;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.Name == "defName") continue;

                string childPath = currentPath;
                bool isListItem = child.Name == "li";

                if (isListItem)
                {
                    childPath = $"{currentPath}.{liIndex}";
                    liIndex++;
                }
                else
                {
                    childPath = $"{currentPath}.{child.Name}";
                }

                bool isPureText = false;
                if (child.ChildNodes.Count == 1)
                {
                    var cType = child.ChildNodes[0].NodeType;
                    if (cType == XmlNodeType.Text || cType == XmlNodeType.CDATA)
                    {
                        isPureText = true;
                    }
                }

                if (isPureText)
                {
                    string text = child.InnerText.Trim();

                    // 🌟 咪咪特製過濾垃圾
                    bool isGarbage = text.Length < 2 || Regex.IsMatch(text, @"^[\d\s\-\+\.\%]+$");

                    if (!isGarbage && !string.IsNullOrWhiteSpace(text) && !text.Contains(".xml") && !text.StartsWith("Tex/") && !text.StartsWith("UI/"))
                    {
                        // ✅ 修復 CS7036：這裡必須傳入兩個引數
                        bool shouldTranslate = IsTranslationTarget(child.Name, text);

                        // ✅ 修復 CS7036：這裡也要傳入兩個引數
                        if (isListItem && node.Name != null && (IsTranslationTarget(node.Name, text) || node.Name.ToLower().Contains("rule")))
                        {
                            shouldTranslate = true;
                        }

                        if (shouldTranslate)
                        {
                            if (!result.ContainsKey(defType)) result[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            result[defType][childPath] = text;
                        }
                    }
                }
                else if (child.HasChildNodes)
                {
                    TraverseDefNode(child, childPath, defType, result);
                }
            }
        }
        private static Dictionary<string, Dictionary<string, string>> ExtractEnglishFromRawDefs(string defsRoot)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(defsRoot)) return result;

            foreach (var file in Directory.GetFiles(defsRoot, "*.xml", SearchOption.AllDirectories))
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return result;

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);
                    if (doc.DocumentElement == null || doc.DocumentElement.Name.ToLower() != "defs") continue;

                    foreach (XmlNode defNode in doc.DocumentElement.ChildNodes)
                    {
                        if (defNode.NodeType != XmlNodeType.Element) continue;
                        string defType = defNode.Name;
                        string defName = "";

                        foreach (XmlNode child in defNode.ChildNodes)
                        {
                            if (child.NodeType == XmlNodeType.Element && child.Name == "defName")
                            {
                                defName = child.InnerText;
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(defName)) continue;

                        TraverseDefNode(defNode, defName, defType, result);
                    }
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// 智慧清理 AI 翻譯結果
        /// 規則：
        /// 1. 如果原文沒有 \n，但翻譯結果有 → 移除（AI 自作多情加的）
        /// 2. 如果原文有 \n，翻譯結果也要保留（不動）
        /// 3. 清掉開頭/結尾多餘的空白與換行
        /// 4. 把字面字元 \\n 統一為單一 \n（避免雙重轉義）
        /// </summary>
        private static string SanitizeTranslationResult(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated)) return translated;

            // 規則 1：原文沒 \n，翻譯不該有
            bool originalHasNewline = original.Contains("\\n") || original.Contains("\n");
            bool translatedHasNewline = translated.Contains("\\n") || translated.Contains("\n");

            if (!originalHasNewline && translatedHasNewline)
            {
                // AI 亂加的，移除所有 \n
                translated = translated.Replace("\\n", " ");
                translated = translated.Replace("\n", " ");
                translated = translated.Replace("\r", " ");
            }

            // 規則 2：處理 AI 把 \n 寫成真實換行的情況
            // 如果原文是 "\n"（字面兩字元），AI 可能回成真實換行
            if (original.Contains("\\n") && !translated.Contains("\\n"))
            {
                // 把翻譯結果中的真實換行還原成字面 \n
                translated = translated.Replace("\r\n", "\\n");
                translated = translated.Replace("\n", "\\n");
                translated = translated.Replace("\r", "\\n");
            }

            // 規則 3：清掉首尾空白
            translated = translated.Trim();

            // 規則 4：避免雙重轉義（\\\\n → \\n）
            translated = translated.Replace("\\\\n", "\\n");

            // 規則 5：合併連續多個空白為單一空白（中文不需要連續空白）
            translated = System.Text.RegularExpressions.Regex.Replace(
                translated,
                @" {2,}",
                " "
            );

            return translated;
        }
        private static async Task ProcessModKeyed(ModMetaData mod, string englishPath)
        {
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string packKeyedDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "Keyed");

            // 修正 Bug D：標籤本地化 + 後續送 AI 前會剝離，避免污染翻譯結果
            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional)
                secondaryTag = "ATC_Tag_FromSimplified".Translate().ToString();
            else if (settings.TargetLang == TargetLanguage.Simplified)
                secondaryTag = "ATC_Tag_FromTraditional".Translate().ToString();
            //✨ 咪咪特製：加入檔案數量計算，並實時更新小進度條！
            string[] files = Directory.GetFiles(englishPath, "*.xml", SearchOption.AllDirectories);
            int totalFiles = files.Length;
            int currentFile = 0;

            foreach (string file in files)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                // 實時更新進度！
                currentFile++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentFile / totalFiles;

                try
                {
                    string modIdClean = mod.PackageId.Replace(".", "_");
                    string targetFileName = $"{modIdClean}_{Path.GetFileName(file)}";
                    string targetFile = Path.Combine(packKeyedDir, targetFileName);

                    var packDict = LoadXmlFileToDict(targetFile);

                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);

                    Dictionary<string, string> finalData = new Dictionary<string, string>();
                    List<string> keysToAI = new List<string>();
                    List<string> valuesToAI = new List<string>();

                    if (doc.DocumentElement == null) continue;

                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element || string.IsNullOrEmpty(node.InnerText)) continue;
                        string key = node.Name;

                        if (packDict.TryGetValue(key, out string packVal)) finalData[key] = packVal;
                        else if (GlobalPrimaryKeyedDict.TryGetValue(key, out string pVal)) finalData[key] = pVal;
                        else if (GlobalSecondaryKeyedDict.TryGetValue(key, out string sVal) && !string.IsNullOrEmpty(secondaryTag))
                        {
                            // 修正 Bug D：不再把 tag 拼進送 AI 的字串
                            // 改為：送純淨文本給 AI，後處理時再判斷是否要加 tag
                            keysToAI.Add(key);
                            valuesToAI.Add(sVal);  // 只送純淨內容，不帶 [來自簡中]
                        }
                        else { keysToAI.Add(key); valuesToAI.Add(node.InnerText); }
                    }

                    if (keysToAI.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_FoundMissing".Translate("Keyed", keysToAI.Count)); // 🔌代表通訊
                        var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / {Path.GetFileName(file)}"); if (res != null)
                        {
                            for (int i = 0; i < keysToAI.Count; i++)
                            {
                                string k = keysToAI[i];
                                string v = res[i];

                                // 新增：AI 回應的智慧清理
                                v = SanitizeTranslationResult(v, valuesToAI[i]);

                                // 🌟 咪咪的防玩家靠北魔法：遇到 label 結尾就換符號！(Keyed專用區)
                                if (k.ToLower().EndsWith("label"))
                                {
                                    v = v.Replace("[", "【").Replace("]", "】").Replace("{", "（").Replace("}", "）");
                                }

                                finalData[k] = v;
                            }
                            // 🌟 這裡修好啦！Keyed 沒有 defType，所以直接傳字串 "Keyed"
                            AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_AIFinish".Translate("Keyed")); // ✨代表成功完成
                        }
                        else AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_AIFail".Translate("Keyed")); // ⚠️代表警告失敗
                    }
                    AutoTranslatorSettings.AddLog("✅ " + "ATC_Log_NoMissing".Translate(Path.GetFileName(file))); // ✅代表不缺字串

                    if (finalData.Count > 0) SaveXml(targetFile, finalData);
                }
                catch (XmlException xmlEx)
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_Format".Translate(mod.Name, GetShortPath(file)));
                    Log.Warning($"[AutoTranslationCore] XML Format Error ({mod.Name}): {xmlEx.Message}");
                }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_Unknown".Translate(mod.Name, GetShortPath(file)));
                    Log.Warning($"[AutoTranslationCore] Process Error ({mod.Name}): {ex.Message}");
                }
            }
        }

        private static async Task ProcessModDefInjected(ModMetaData mod, List<string> langRoots, List<string> defsRoots)
        {
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);
            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional)
                secondaryTag = "ATC_Tag_FromSimplified".Translate().ToString();
            else if (settings.TargetLang == TargetLanguage.Simplified)
                secondaryTag = "ATC_Tag_FromTraditional".Translate().ToString();
            string packDefBaseDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "DefInjected");

            // 修正 Bug E：分離三層字典，分別記錄不同來源，避免混淆
            // 1. 英文原文（從 Defs 提取 + 從 English/DefInjected 提取）→ 用於確認哪些 key 需要被翻譯
            // 2. 模組自帶的目標語言（例如選簡中時的 ChineseSimplified）→ 優先採用，跳過 AI
            // 3. 模組自帶的次級語言（簡↔繁互填）→ 次優先，標記 tag
            var englishKeys = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var modSelfTargetLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var modSelfSecondaryLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            Action<string, Dictionary<string, Dictionary<string, string>>> LoadDefsToDict = (path, targetDict) => {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                foreach (var typeDir in Directory.GetDirectories(path))
                {
                    string defType = Path.GetFileName(typeDir);
                    if (!targetDict.ContainsKey(defType))
                        targetDict[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                    {
                        var d = LoadXmlFileToDict(file);
                        foreach (var kv in d) targetDict[defType][kv.Key] = kv.Value;
                    }
                }
                foreach (var file in Directory.GetFiles(path, "*.xml"))
                {
                    if (!targetDict.ContainsKey("General"))
                        targetDict["General"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var d = LoadXmlFileToDict(file);
                    foreach (var kv in d) targetDict["General"][kv.Key] = kv.Value;
                }
            };

            // 1. 從 Defs 提取英文原文
            foreach (var dRoot in defsRoots)
            {
                var extracted = ExtractEnglishFromRawDefs(dRoot);
                foreach (var kv in extracted)
                {
                    if (!englishKeys.ContainsKey(kv.Key))
                        englishKeys[kv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inner in kv.Value) englishKeys[kv.Key][inner.Key] = inner.Value;
                }
            }

            // 2. 從 Languages 載入各語言來源到對應的字典
            foreach (var lRoot in langRoots)
            {
                LoadDefsToDict(Path.Combine(lRoot, "English", "DefInjected"), englishKeys);
                LoadDefsToDict(Path.Combine(lRoot, targetFolder, "DefInjected"), modSelfTargetLang);
                if (!string.IsNullOrEmpty(otherFolder))
                {
                    LoadDefsToDict(Path.Combine(lRoot, otherFolder, "DefInjected"), modSelfSecondaryLang);
                }
            }

            if (englishKeys.Count == 0 && modSelfTargetLang.Count == 0)
                return;

            // 修正 Bug E：統計模組自帶翻譯量，給玩家看
            int modSelfTargetCount = modSelfTargetLang.Sum(kv => kv.Value.Count);
            if (modSelfTargetCount > 0)
            {
                AutoTranslatorSettings.AddLog("✅ " +
                    "ATC_Log_SkipExistingTranslation".Translate(mod.Name, modSelfTargetCount));
            }

            // 合併 englishKeys 與 modSelfTargetLang 的所有 defType（取聯集）
            var allDefTypes = new HashSet<string>(englishKeys.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in modSelfTargetLang.Keys) allDefTypes.Add(k);

            int totalDefs = allDefTypes.Count;
            int currentDef = 0;

            foreach (var defType in allDefTypes)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                currentDef++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentDef / totalDefs;

                // 收集這個 defType 下所有 key（英文原文的 key 集合）
                var keysForThisType = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (englishKeys.TryGetValue(defType, out var engDict))
                    foreach (var k in engDict.Keys) keysForThisType.Add(k);
                if (modSelfTargetLang.TryGetValue(defType, out var selfDict))
                    foreach (var k in selfDict.Keys) keysForThisType.Add(k);

                if (keysForThisType.Count == 0) continue;

                string cleanPackageId = mod.PackageId.Replace(".", "_");
                string targetFile = Path.Combine(packDefBaseDir, defType, $"{cleanPackageId}_AutoTranslated.xml");
                var packDict = LoadXmlFileToDict(targetFile);

                Dictionary<string, string> finalData = new Dictionary<string, string>();
                List<string> keysToAI = new List<string>();
                List<string> valuesToAI = new List<string>();

                foreach (var key in keysForThisType)
                {
                    string globalKey = $"{defType}/{key}";
                    string globalKeyGen = $"General/{key}";

                    // 修正 Bug E：優先級重排
                    // 優先級 1：本機 Pack 已翻譯（玩家上次跑出來的結果）
                    if (packDict.TryGetValue(key, out string packVal))
                    {
                        finalData[key] = packVal;
                    }
                    // 優先級 2：模組自帶目標語言翻譯（例如模組原作者寫的簡中）
                    //          這是修正核心：直接採用，不送 AI
                    else if (selfDict != null && selfDict.TryGetValue(key, out string selfVal)
                             && !string.IsNullOrWhiteSpace(selfVal))
                    {
                        finalData[key] = selfVal;
                    }
                    // 優先級 3：全域字典（其他模組或全域 Languages 的目標語言）
                    else if (GlobalPrimaryDefDict.TryGetValue(globalKey, out string pVal)
                             || GlobalPrimaryDefDict.TryGetValue(globalKeyGen, out pVal))
                    {
                        finalData[key] = pVal;
                    }
                    // 優先級 4：模組自帶次級語言（簡↔繁互填，送 AI 做語系轉換）
                    else if (modSelfSecondaryLang.TryGetValue(defType, out var secDict)
                             && secDict.TryGetValue(key, out string secVal)
                             && !string.IsNullOrEmpty(secondaryTag))
                    {
                        keysToAI.Add(key);
                        valuesToAI.Add(secVal);  // 純淨送出，不帶 tag
                    }
                    // 優先級 5：全域次級語言字典
                    else if ((GlobalSecondaryDefDict.TryGetValue(globalKey, out string sVal)
                              || GlobalSecondaryDefDict.TryGetValue(globalKeyGen, out sVal))
                             && !string.IsNullOrEmpty(secondaryTag))
                    {
                        keysToAI.Add(key);
                        valuesToAI.Add(sVal);  // 純淨送出
                    }
                    // 優先級 6：用英文原文送 AI
                    else if (engDict != null && engDict.TryGetValue(key, out string engVal)
                             && !string.IsNullOrEmpty(engVal))
                    {
                        keysToAI.Add(key);
                        valuesToAI.Add(engVal);
                    }
                }

                if (keysToAI.Count > 0)
                {
                    AutoTranslatorSettings.AddLog("🔌 " + "ATC_Log_FoundMissing".Translate(defType, keysToAI.Count));
                    var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / Defs: {defType}");
                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;
                    if (res != null)
                    {
                        for (int i = 0; i < keysToAI.Count; i++)
                        {
                            string k = keysToAI[i];
                            string v = res[i];

                            // 新增：AI 回應的智慧清理
                            v = SanitizeTranslationResult(v, valuesToAI[i]);

                            if (k.ToLower().EndsWith("label"))
                            {
                                v = v.Replace("[", "【").Replace("]", "】").Replace("{", "（").Replace("}", "）");
                            }
                            finalData[k] = v;
                        }
                        AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_AIFinish".Translate(defType));
                    }
                    else AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_AIFail".Translate(defType));
                }
                else
                {
                    AutoTranslatorSettings.AddLog("✅ " + "ATC_Log_NoMissing".Translate($"Def:{defType}"));
                }

                if (finalData.Count > 0) SaveXml(targetFile, finalData);
            }
        }
        // 🌟 咪咪特製終極版翻譯引擎：分塊 + 併發 + 去重 + 指數退避 (全本地化 + 精準報錯版)！
        private static async Task<List<string>> SafeTranslateBatch(List<string> texts, string contextInfo)
        {
            if (texts == null || texts.Count == 0) return new List<string>();

            var uniqueTexts = texts.Distinct().ToList();
            var translatedDict = new Dictionary<string, string>();

            int chunkSize = 40;
            int maxConcurrency = AutoTranslatorMod.Settings.MaxThreads;
            List<Task> tasks = new List<Task>();

            using (SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency))
            {
                for (int i = 0; i < uniqueTexts.Count; i += chunkSize)
                {
                    int chunkIndex = i;
                    int currentChunkSize = Math.Min(chunkSize, uniqueTexts.Count - chunkIndex);
                    List<string> chunk = uniqueTexts.GetRange(chunkIndex, currentChunkSize);

                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            List<string> chunkRes = null;
                            bool hasRetried = false;

                            // 【修改後】✨ 咪咪特製：加上 IsSkipCurrentRequested 攔截！馬上跳出！
                            for (int r = 0; r < 3; r++)
                            {
                                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                                chunkRes = await AutoTranslatorAPI.TranslateBatchAsync(chunk);

                                if (chunkRes != null && chunkRes.Count == chunk.Count)
                                {
                                    if (hasRetried)
                                    {
                                        // 🌟 1. 連線恢復 (本地化)
                                        AutoTranslatorSettings.AddLog("✅ " + "ATC_Log_ApiRecovered".Translate());
                                    }
                                    break;
                                }

                                hasRetried = true;
                                int delay = (int)Math.Pow(2, r) * 1000;
                                // 🌟 2. 繁忙重試 (本地化，帶入秒數變數)
                                AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_ApiRetry".Translate(delay / 1000));
                                await Task.Delay(delay);
                            }

                            if (chunkRes == null || chunkRes.Count != chunk.Count)
                            {
                                // 🌟 3. 降級單挑模式 (本地化)
                                AutoTranslatorSettings.AddLog("🔄 " + "ATC_Log_ApiFallback".Translate());

                                chunkRes = new List<string>();
                                bool loggedErrorForThisChunk = false;

                                foreach (var t in chunk)
                                {
                                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;
                                    var single = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { t });

                                    if (single != null && single.Count > 0)
                                    {
                                        chunkRes.Add(single[0]);
                                    }
                                    else
                                    {
                                        chunkRes.Add(t);

                                        if (!loggedErrorForThisChunk)
                                        {
                                            // 🌟 4. 嚴重錯誤精準報錯 (本地化，帶入模組與檔案資訊)
                                            AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiCritical".Translate(contextInfo));
                                            loggedErrorForThisChunk = true;
                                        }
                                    }
                                }
                            }

                            lock (translatedDict)
                            {
                                for (int j = 0; j < chunk.Count; j++)
                                {
                                    translatedDict[chunk[j]] = chunkRes[j];
                                }
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
            }

            List<string> finalResults = new List<string>(texts.Count);
            foreach (var t in texts)
            {
                if (translatedDict.TryGetValue(t, out string translated))
                {
                    finalResults.Add(translated);
                }
                else
                {
                    finalResults.Add(t);
                }
            }

            return finalResults;
        }


        public static Dictionary<string, string> LoadXmlFilesToDict(string path)
        {
            var dict = new Dictionary<string, string>();
            if (!Directory.Exists(path)) return dict;
            foreach (var f in Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories))
            {
                var d = LoadXmlFileToDict(f);
                foreach (var p in d) dict[p.Key] = p.Value;
            }
            return dict;
        }
        public static Dictionary<string, string> LoadXmlFileToDict(string filePath)
        {
            var dict = new Dictionary<string, string>();
            if (!File.Exists(filePath)) return dict;
            try
            {
                XmlDocument d = new XmlDocument(); d.Load(filePath);
                if (d.DocumentElement == null) return dict;
                foreach (XmlNode n in d.DocumentElement.ChildNodes)
                {
                    if (n.NodeType == XmlNodeType.Element)
                    {
                        string val = n.InnerText;
                        if (!string.IsNullOrEmpty(val))
                        {
                            // 零成本記憶體淨化
                            val = val.Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");
                        }
                        dict[n.Name] = val;
                    }
                }
            }
            catch { }
            return dict;
        }
        // XML 名稱合法性檢查正則 (符合 W3C XML 1.0 NameStartChar / NameChar 規範簡化版)
        private static readonly Regex ValidXmlNameRegex = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_\-\.]*$",
            RegexOptions.Compiled
        );

        private static void SaveXml(string path, Dictionary<string, string> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XmlDocument d = new XmlDocument();
            XmlDeclaration dec = d.CreateXmlDeclaration("1.0", "utf-8", null);
            d.AppendChild(dec);
            XmlElement r = d.CreateElement("LanguageData");

            foreach (var p in data)
            {
                // 防禦 1：value 是空白/控制字元，跳過
                if (Regex.IsMatch(p.Value ?? "", @"^[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*$"))
                    continue;

                // 防禦 2：key 為空或不符合 XML 命名規則，跳過並記錄
                // 解決 Bug C：AI 回傳的 key 若包含 '{' (0x7B) 或其他非法字元，CreateElement 會炸
                if (string.IsNullOrWhiteSpace(p.Key) || !ValidXmlNameRegex.IsMatch(p.Key))
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_InvalidXmlKey".Translate(p.Key ?? "<null>"));
                    continue;
                }

                try
                {
                    XmlElement n = d.CreateElement(p.Key);
                    n.InnerText = p.Value;
                    r.AppendChild(n);
                }
                catch (XmlException ex)
                {
                    // 防禦 3：最後一道防線，CreateElement 仍可能因特殊 Unicode 拋例外
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_InvalidXmlKey".Translate($"{p.Key} ({ex.Message})"));
                    continue;
                }
            }

            d.AppendChild(r);
            d.Save(path);
        }
        private static string GetShortPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            string normalized = fullPath.Replace('\\', '/');
            int idx = normalized.IndexOf("294100/");
            if (idx == -1) idx = normalized.IndexOf("Mods/");
            return idx != -1 ? normalized.Substring(idx) : Path.GetFileName(fullPath);
        }
        // 🌟 咪咪特製：V4.7 終極物理超渡程式 V3.0！(全本地化版)
        // 🌟 咪咪特製：V4.7 終極物理超渡程式 V3.1！(帶免疫標記，絕對不再自爆！)
        public static void ApplyEmergencyHotfix()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                string markerFile = Path.Combine(packPath, "V4.7_Cleaned.marker"); // 🌟 免疫標記檔！

                // 🌟 第一道防線：如果已經有免疫標記，代表是乾淨的新版本，直接下莊，絕對不刪！
                if (File.Exists(markerFile)) return;

                if (Directory.Exists(langsPath))
                {
                    string[] allXmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                    if (allXmlFiles.Length > 0)
                    {
                        // 🌟 發現舊版，投下核彈！
                        Directory.Delete(langsPath, true);

                        // 🌟 本地化日誌輸出
                        AutoTranslatorSettings.AddErrorLog("🚨 " + "ATC_LogError_OldToxicFiles".Translate());
                        AutoTranslatorSettings.AddLog("🗑️ " + "ATC_Log_OldFilesDeleted".Translate());
                        AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_AutoRebuildStart".Translate());
                    }
                }

                // 🌟 建立免疫標記！跟系統說「我已經洗乾淨了，不要再殺我了！」
                Directory.CreateDirectory(packPath);
                File.WriteAllText(markerFile, "This marker prevents V4.7 from deleting the cleaned translation pack.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Delete failed (請手動刪除 !Translation_AI_Pack/Languages): {ex.Message}");
            }
        }
        // 🌟 咪咪特製：V4.8 終極排毒機制 (微創手術，不刪玩家檔案！)
        public static void RunDetoxScanner()
            {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;
                int removedTags = 0;

                // 🌟 神奇放大鏡：精準抓出 <tag></tag> (含空白與零寬字元) 或 <tag/>
                // 🌟 神奇放大鏡 (終極加強版)：加入 \u00A0 (不換行空白) 與 \x00-\x1F (控制字元)
                // 只要標籤裡面只有這些垃圾，一律判定為劇毒！
                Regex badTagRegex = new Regex(@"(?m)^\s*<([a-zA-Z0-9_\-\.]+)>[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*<\/\1>\s*$|(?m)^\s*<[a-zA-Z0-9_\-\.]+\s*\/>\s*$", RegexOptions.Multiline);

                foreach (var file in xmlFiles)
                {
                    string content = File.ReadAllText(file);
                    if (badTagRegex.IsMatch(content))
                    {
                        // 🌟 顯影劑：把抓到的毒瘤印在後台，大哥你明天睡醒看 Log 就知道是誰在搞事了！
                        MatchCollection matches = badTagRegex.Matches(content);
                        foreach (Match match in matches)
                        {
                            Log.Warning($"[AutoTranslationCore] ⚠️ 抓到劇毒標籤，準備物理切除：{match.Value.Trim()} (來自檔案: {Path.GetFileName(file)})");
                        }

                        int matchCount = matches.Count;
                        // 🌟 物理切除：把有毒的那整行替換成空字串
                        string cleanContent = badTagRegex.Replace(content, "");
                        // 🌟 順手把多餘的空行修掉，保持版面整潔
                        cleanContent = Regex.Replace(cleanContent, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);

                        File.WriteAllText(file, cleanContent);
                        fixedFiles++;
                        removedTags += matchCount;
                    }
                }
                if (fixedFiles > 0)
                {
                    // ✨ 修復 CS0019：RimWorld 的 Translate() 回傳的是 TaggedString，不能直接用 ??
                    // 改用 CanTranslate() 來做標準判定，並補上 .ToString() 轉回普通字串！
                    string logMsg = "ATC_Log_DetoxSuccess".CanTranslate()
                        ? "ATC_Log_DetoxSuccess".Translate(fixedFiles, removedTags).ToString()
                        : $"[排毒系統] 修復了 {fixedFiles} 個檔案，切除 {removedTags} 個劇毒標籤！";

                    AutoTranslatorSettings.AddLog($"🛡️ {logMsg}");
                    Log.Message($"[AutoTranslationCore] Detox complete: {fixedFiles} files fixed, {removedTags} tags removed.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 排毒系統異常: {ex.Message}");
            }

        }



        // 🌟 咪咪特製：V4.9 玩家資產保衛戰！微創排毒手術 (精準刪除違規標籤，保留正常翻譯)
        public static void RunAdvancedDetoxScanner()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;
                int removedTags = 0;

                foreach (var file in xmlFiles)
                {
                    XmlDocument doc = new XmlDocument();
                    try { doc.Load(file); } catch { continue; } // 如果檔案壞了就跳過，不引發報錯

                    if (doc.DocumentElement == null || doc.DocumentElement.Name != "LanguageData") continue;

                    List<XmlNode> nodesToKill = new List<XmlNode>();

                    // 遍歷檔案裡所有的翻譯標籤
                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element) continue;

                        string tagName = node.Name;
                        bool shouldKill = false;

                        // 🔍 檢查這個標籤有沒有中我們的黑名單 (例如 Weapon_Rifle.soundInteract)
                        foreach (var badField in BlacklistedFields)
                        {
                            // 只要標籤名稱「等於」或是「結尾是 .黑名單」，就判定為惡性腫瘤！
                            if (tagName.EndsWith("." + badField, StringComparison.OrdinalIgnoreCase) ||
                                tagName.Equals(badField, StringComparison.OrdinalIgnoreCase))
                            {
                                shouldKill = true;
                                break;
                            }
                        }

                        if (shouldKill)
                        {
                            nodesToKill.Add(node);
                        }
                    }

                    // 🔪 執行物理切除！
                    if (nodesToKill.Count > 0)
                    {
                        foreach (var node in nodesToKill)
                        {
                            doc.DocumentElement.RemoveChild(node);
                            removedTags++;
                        }
                        doc.Save(file); // 存檔覆蓋
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {
                    // 🌟 本地化日誌：報告手術成果！
                    AutoTranslatorSettings.AddLog($"🩺 [ATC System] " + "ATC_Log_AdvancedDetoxSuccess".Translate(removedTags, fixedFiles));
                    Log.Message($"[AutoTranslationCore] Advanced Detox complete: Removed {removedTags} bad tags across {fixedFiles} files, saving player's API costs!");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 高級排毒系統異常: {ex.Message}");
            }
        }
        // ==========================================
        // 🌟 咪咪特製：背景無感自動清理舊翻譯引擎
        // ==========================================
        // ==========================================
        // 🌟 咪咪特製：V2.1 歷史業障超渡器 (全自動換行修復)
        // ==========================================
        public static void RunNewlineDetoxScanner()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;

                foreach (var file in xmlFiles)
                {
                    // 把檔案當作純文字直接讀出來 (超高速)
                    string content = File.ReadAllText(file);

                    // 偵測是否感染了 \n 病毒
                    if (content.Contains("\\n") || content.Contains("\\r") || content.Contains("/n"))
                    {
                        // 物理替換！將字面上的 \n 轉為真實換行
                        content = content.Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");

                        // 存回硬碟覆蓋舊檔案
                        File.WriteAllText(file, content);
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {
                    // 本地化日誌：報告超渡成果
                    AutoTranslatorSettings.AddLog($"✨ [ATC System] 歷史業障超渡成功！共修復了 {fixedFiles} 個帶有換行 Bug 的舊檔案！");
                    Log.Message($"[AutoTranslationCore] Newline Detox complete: Fixed {fixedFiles} legacy files.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 換行排毒系統異常: {ex.Message}");
            }
        }
        // ==========================================
        // 🌟 咪咪特製：背景無感自動清理舊翻譯引擎 (核彈)
        // ==========================================
        public static void ClearOldTranslationFiles(List<ModMetaData> modsToClear)
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                foreach (var mod in modsToClear)
                {
                    // 雙重雷達：涵蓋 {id}_ 和 {id}. 兩種格式
                    string id1 = mod.PackageId.ToLower();
                    string id2 = mod.PackageId.Replace(".", "_").ToLower();

                    foreach (var file in allXmls)
                    {
                        string fileName = Path.GetFileName(file).ToLower();
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            File.Delete(file);
                            deletedFiles++;
                        }
                    }
                }

                if (deletedFiles > 0)
                {
                    AutoTranslatorSettings.AddLog("ATC_ClearCacheSuccess".Translate(deletedFiles));
                    Log.Message($"[AutoTranslationCore] Auto-cleared {deletedFiles} old files for updated mods.");
                }
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog($"Auto Clear Error: {ex.Message}");
            }
        }
    }
}
    namespace AutoTranslator_Core
        {
        /// <summary>
        /// 主執行緒派發器：每個 Tick 檢查是否有待處理的跨執行緒請求
        /// (修正 P2-1：MemoryDrop 主執行緒守護)
        /// </summary>
        public class AutoTranslator_MainThreadDispatcher : GameComponent
        {
            // 為了讓 Component 在主選單也能跑（沒有 Game 物件時），
            // 我們另外用 LongEventHandler 做雙保險
            public AutoTranslator_MainThreadDispatcher(Game game) { }

            public override void GameComponentUpdate()
            {
                AutoTranslatorScanner.PumpMainThreadDispatcher();
            }
        }
        [StaticConstructorOnStartup]
        public static class AutoTranslator_StartupHook
        {
            static AutoTranslator_StartupHook()
            {
                // 註冊一個無限循環的 LongEvent 來定期 Pump 主執行緒佇列
                // 這個技巧確保即使在主選單（沒有 Game 物件），佇列也能被處理
                Verse.LongEventHandler.QueueLongEvent(() =>
                {
                    // 開機只執行一次，掛載 Update 鉤子
                    UnityEngine.Object hook = new UnityEngine.GameObject("ATC_MainThreadPump");
                    UnityEngine.Object.DontDestroyOnLoad(hook);
                    ((UnityEngine.GameObject)hook).AddComponent<AutoTranslator_PumpBehaviour>();
                }, null, false, null);
            }
        }

        /// <summary>
        /// MonoBehaviour 主執行緒派發器（覆蓋主選單階段）
        /// </summary>
        public class AutoTranslator_PumpBehaviour : UnityEngine.MonoBehaviour
        {
            private float _accumulator = 0f;

            private void Update()
            {
                // 每 0.5 秒檢查一次佇列，避免每幀都 lock
                _accumulator += UnityEngine.Time.unscaledDeltaTime;
                if (_accumulator >= 0.5f)
                {
                    _accumulator = 0f;
                    AutoTranslatorScanner.PumpMainThreadDispatcher();
                }
            }
        }
    }  