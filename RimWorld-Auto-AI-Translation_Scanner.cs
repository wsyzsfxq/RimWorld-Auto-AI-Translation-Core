using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading.Tasks;
using System.Threading;
using System.Text.RegularExpressions;
using Verse;
using RimWorld; // 🌟 咪咪提醒：呼叫彈窗視窗必須要有這行喔！

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

        private static Dictionary<string, string> GlobalPrimaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalPrimaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                    var dirs = Directory.GetDirectories(root, "Defs", SearchOption.AllDirectories);
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
        }

        public static async void StartSingleScan(ModMetaData targetMod)
        {
            try
            {
                AutoTranslatorSettings.IsRunning = true;
                EnsurePackInitialized();
                var settings = AutoTranslatorMod.Settings;

                settings.CurrentProgress = 0f;
                settings.CurrentTaskName = $"Translating: {targetMod.Name}";

                AutoTranslatorSettings.AddLog("ATC_Log_StartSingleMod".Translate(targetMod.Name));

                var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !BlacklistedModules.Contains(m.PackageId.ToLower())).ToList();
                BuildGlobalTranslationDatabase(activeMods);

                if (AutoTranslatorSettings.IsCancellationRequested) return;

                var langRoots = GetAllEffectiveLangPaths(targetMod);
                var defsRoots = GetAllEffectiveDefsPaths(targetMod);

                bool hasLang = langRoots.Count > 0;
                bool hasDefs = defsRoots.Count > 0;

                if (!hasLang && !hasDefs)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
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
                                AutoTranslatorSettings.AddLog("ATC_Log_KeyedScan".Translate());
                                await ProcessModKeyed(targetMod, englishKeyed);
                            }
                        }
                    }

                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    if (hasDefs || hasLang)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_DefScan".Translate());
                        await ProcessModDefInjected(targetMod, langRoots, defsRoots);
                    }
                }

                if (!AutoTranslatorSettings.IsCancellationRequested)
                {
                    settings.CurrentTaskName = "ATC_TaskDone".Translate();
                    settings.CurrentProgress = 1f;
                    AutoTranslatorSettings.AddLog("ATC_Log_SingleModDone".Translate());

                    // 🌟 咪咪特製：翻譯完成的本地化彈窗！
                    string finishMessage = "ATC_FinishMessage_Text".Translate();
                    string okButton = "ATC_FinishMessage_OK".Translate();
                    string title = "ATC_FinishMessage_Title".Translate();
                    Find.WindowStack.Add(new Dialog_MessageBox(finishMessage, okButton, null, null, null, title));
                }
            }
            catch (Exception e)
            {
                AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
                Log.Error($"[AutoTranslationCore] Single translation task interrupted: {e.Message}");
            }
            finally
            {
                ClearGlobalTranslationDatabase();
                AutoTranslatorSettings.IsRunning = false;
                if (AutoTranslatorSettings.IsCancellationRequested)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_ProcessAborted".Translate());
                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_TaskAborted".Translate();
                }
            }
        }

        public static void StartFullScan() // ❌ 拔掉 async
        {
            AutoTranslatorSettings.IsRunning = true;
            EnsurePackInitialized();
            var settings = AutoTranslatorMod.Settings;

            // 🌟 在主執行緒安全地抓取模組清單，防閃退！
            var mods = ModLister.AllInstalledMods.Where(m =>
                !BlacklistedModules.Contains(m.PackageId.ToLower()) &&
                (!settings.OnlyScanActiveMods || m.Active)).ToList();

            AutoTranslatorSettings.AddLog("ATC_Log_StartScan".Translate(mods.Count));

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
                        AutoTranslatorSettings.AddLog("ATC_Log_TaskDone".Translate());
                        AutoTranslatorSettings.AddLog("🎉 " + "ATC_Log_AllTranslationWritten".Translate());

                        // 🌟 發送信號給主執行緒，讓它去彈窗！絕對不閃退！
                        AutoTranslatorSettings.ShowFinishPopup = true;
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
                    // ❌ 已經拔掉 Unity 的 Log.Error，改用純文字紀錄防閃退
                    AutoTranslatorSettings.AddLog($"[CRITICAL ERROR] {e.Message}");
                }
                finally
                {
                    ClearGlobalTranslationDatabase();
                    AutoTranslatorSettings.IsRunning = false;

                    // 🌟 咪咪特製：如果玩家按了緊急停止，把儀表板 (進度條跟任務名) 全部洗白歸零！
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

                    // 🌟 咪咪特製：如果玩家按了緊急停止，把儀表板 (進度條跟任務名) 全部洗白歸零！
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
            AutoTranslatorSettings.AddLog("ATC_Log_Clean".Translate());
        }

        private static void BuildGlobalTranslationDatabase(List<ModMetaData> mods)
        {
            AutoTranslatorSettings.AddLog("ATC_Log_Init".Translate());

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
            AutoTranslatorSettings.AddLog("ATC_Log_InitDone".Translate(GlobalPrimaryDefDict.Count));
        }

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

        private static bool IsTranslationTarget(string tagName)
        {
            if (ExactTextTags.Contains(tagName)) return true;
            string lower = tagName.ToLower();

            // 🌟 咪咪特製防爆濾網：這些結尾的標籤絕對不能翻譯！(包含大哥抓到的 dollName)
            if (lower == "defname" || lower.EndsWith("defname") ||
                lower.EndsWith("class") ||
                lower.EndsWith("texpath") ||
                lower.EndsWith("dollname") ||
                lower.EndsWith("dollpartname") ||
                lower.EndsWith("sound") ||
                lower.EndsWith("worker") ||
                lower.EndsWith("def"))
                return false;

            /*
    ██╗      ██╗████████╗███████╗
    ██║      ██║╚══██╔══╝██╔════╝
    ██║ █╗ ██║     ██║   █████╗  
    ██║███╗██║   ██║   ██╔══╝  
    ╚███╔███╔╝   ██║   ██║     
     ╚══╝╚══╝      ╚═╝   ╚═╝     
    What The F*** is going on here?! 
*/

            // 只有符合這些結尾的才允許翻譯
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

                    // 🌟 咪咪的新增：過濾無意義的「垃圾字串」 (純符號、純數字、或太短)
                    bool isGarbage = text.Length < 2 || Regex.IsMatch(text, @"^[\d\s\-\+\.\%]+$");

                    if (!isGarbage && !string.IsNullOrWhiteSpace(text) && !text.Contains(".xml") && !text.StartsWith("Tex/") && !text.StartsWith("UI/"))
                    {
                        bool shouldTranslate = IsTranslationTarget(child.Name);

                        if (isListItem && node.Name != null && (IsTranslationTarget(node.Name) || node.Name.ToLower().Contains("rule")))
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

        private static async Task ProcessModKeyed(ModMetaData mod, string englishPath)
        {
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string packKeyedDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "Keyed");

            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional) secondaryTag = "[來自簡中]";
            else if (settings.TargetLang == TargetLanguage.Simplified) secondaryTag = "[來自繁中]";

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
                        { keysToAI.Add(key); valuesToAI.Add($"{secondaryTag} {sVal}"); }
                        else { keysToAI.Add(key); valuesToAI.Add(node.InnerText); }
                    }

                    if (keysToAI.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_FoundMissing".Translate("Keyed", keysToAI.Count));
                        var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / {Path.GetFileName(file)}"); if (res != null)
                        {
                            for (int i = 0; i < keysToAI.Count; i++)
                            {
                                string k = keysToAI[i];
                                string v = res[i];

                                // 🌟 咪咪的防玩家靠北魔法：遇到 label 結尾就換符號！(Keyed專用區)
                                if (k.ToLower().EndsWith("label"))
                                {
                                    v = v.Replace("[", "【").Replace("]", "】").Replace("{", "（").Replace("}", "）");
                                }

                                finalData[k] = v;
                            }
                            // 🌟 這裡修好啦！Keyed 沒有 defType，所以直接傳字串 "Keyed"
                            AutoTranslatorSettings.AddLog("ATC_Log_AIFinish".Translate("Keyed"));
                        }
                        else AutoTranslatorSettings.AddLog("ATC_Log_AIFail".Translate("Keyed"));
                    }
                    else AutoTranslatorSettings.AddLog("ATC_Log_NoMissing".Translate(Path.GetFileName(file)));

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
            if (settings.TargetLang == TargetLanguage.Traditional) secondaryTag = "[來自簡中]";
            else if (settings.TargetLang == TargetLanguage.Simplified) secondaryTag = "[來自繁中]";

            string packDefBaseDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "DefInjected");

            Dictionary<string, Dictionary<string, string>> allKnownDefs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            Action<string> AddToAllDefs = (path) => {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                foreach (var typeDir in Directory.GetDirectories(path))
                {
                    string defType = Path.GetFileName(typeDir);
                    if (!allKnownDefs.ContainsKey(defType)) allKnownDefs[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                    {
                        var d = LoadXmlFileToDict(file);
                        foreach (var kv in d) allKnownDefs[defType][kv.Key] = kv.Value;
                    }
                }
                foreach (var file in Directory.GetFiles(path, "*.xml"))
                {
                    if (!allKnownDefs.ContainsKey("General")) allKnownDefs["General"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var d = LoadXmlFileToDict(file);
                    foreach (var kv in d) allKnownDefs["General"][kv.Key] = kv.Value;
                }
            };

            foreach (var dRoot in defsRoots)
            {
                var extracted = ExtractEnglishFromRawDefs(dRoot);
                foreach (var kv in extracted)
                {
                    if (!allKnownDefs.ContainsKey(kv.Key)) allKnownDefs[kv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inner in kv.Value) allKnownDefs[kv.Key][inner.Key] = inner.Value;
                }
            }

            foreach (var lRoot in langRoots)
            {
                AddToAllDefs(Path.Combine(lRoot, "English", "DefInjected"));
                AddToAllDefs(Path.Combine(lRoot, targetFolder, "DefInjected"));
                if (!string.IsNullOrEmpty(otherFolder))
                {
                    AddToAllDefs(Path.Combine(lRoot, otherFolder, "DefInjected"));
                }
            }

            if (allKnownDefs.Count == 0) return;

            // 【修改後】✨ 咪咪特製：加入 Def 分類數量計算，並實時更新小進度條！
            int totalDefs = allKnownDefs.Count;
            int currentDef = 0;

            foreach (var defGroup in allKnownDefs)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                // 實時更新進度！
                currentDef++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentDef / totalDefs;

                string defType = defGroup.Key; // 🌟 這裡才有 defType
                var currentDict = defGroup.Value;
                if (currentDict.Count == 0) continue;

                string cleanPackageId = mod.PackageId.Replace(".", "_");
                string targetFile = Path.Combine(packDefBaseDir, defType, $"{cleanPackageId}_AutoTranslated.xml");
                var packDict = LoadXmlFileToDict(targetFile);

                Dictionary<string, string> finalData = new Dictionary<string, string>();
                List<string> keysToAI = new List<string>();
                List<string> valuesToAI = new List<string>();

                foreach (var kv in currentDict)
                {
                    string key = kv.Key;
                    string engVal = kv.Value;

                    string globalKey = $"{defType}/{key}";
                    string globalKeyGen = $"General/{key}";

                    if (packDict.TryGetValue(key, out string packVal)) finalData[key] = packVal;
                    else if (GlobalPrimaryDefDict.TryGetValue(globalKey, out string pVal) || GlobalPrimaryDefDict.TryGetValue(globalKeyGen, out pVal)) finalData[key] = pVal;
                    else if ((GlobalSecondaryDefDict.TryGetValue(globalKey, out string sVal) || GlobalSecondaryDefDict.TryGetValue(globalKeyGen, out sVal)) && !string.IsNullOrEmpty(secondaryTag))
                    { keysToAI.Add(key); valuesToAI.Add($"{secondaryTag} {sVal}"); }
                    else if (!string.IsNullOrEmpty(engVal)) { keysToAI.Add(key); valuesToAI.Add(engVal); }
                }


                if (keysToAI.Count > 0)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_FoundMissing".Translate(defType, keysToAI.Count));
                    var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / Defs: {defType}");
                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                    if (res != null)
                    {
                        for (int i = 0; i < keysToAI.Count; i++)
                        {
                            string k = keysToAI[i];
                            string v = res[i];

                            // 🌟 咪咪的防玩家靠北魔法：遇到 label 結尾就換符號！(DefInjected專用區)
                            if (k.ToLower().EndsWith("label"))
                            {
                                v = v.Replace("[", "【").Replace("]", "】").Replace("{", "（").Replace("}", "）");
                            }

                            finalData[k] = v;
                        }
                        AutoTranslatorSettings.AddLog("ATC_Log_AIFinish".Translate(defType));
                    }
                    else AutoTranslatorSettings.AddLog("ATC_Log_AIFail".Translate(defType));
                }
                else AutoTranslatorSettings.AddLog("ATC_Log_NoMissing".Translate($"Def:{defType}"));

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


        private static Dictionary<string, string> LoadXmlFilesToDict(string path)
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

        private static Dictionary<string, string> LoadXmlFileToDict(string filePath)
        {
            var dict = new Dictionary<string, string>();
            if (!File.Exists(filePath)) return dict;
            try
            {
                XmlDocument d = new XmlDocument(); d.Load(filePath);
                if (d.DocumentElement == null) return dict;
                foreach (XmlNode n in d.DocumentElement.ChildNodes) if (n.NodeType == XmlNodeType.Element) dict[n.Name] = n.InnerText;
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_FileCorrupted".Translate(GetShortPath(filePath)));
                Log.Warning($"[AutoTranslationCore] XML Parse Error ({Path.GetFileName(filePath)}): {ex.Message}");
            }
            return dict;
        }

        private static void SaveXml(string path, Dictionary<string, string> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XmlDocument d = new XmlDocument();
            XmlDeclaration dec = d.CreateXmlDeclaration("1.0", "utf-8", null); d.AppendChild(dec);
            XmlElement r = d.CreateElement("LanguageData");
            foreach (var p in data) { XmlElement n = d.CreateElement(p.Key); n.InnerText = p.Value; r.AppendChild(n); }
            d.AppendChild(r); d.Save(path);
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
    }
}