using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core
{
    public static class AutoTranslatorScanner
    {
        private static readonly HashSet<string> OfficialModules = new HashSet<string> {
            "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
            "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey"
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
                case TargetLanguage.English: return "English"; // 🌟 V4.6 新增英文
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

        private static string GetEffectiveLangPath(ModMetaData mod)
        {
            string[] searchPaths = {
                Path.Combine(mod.RootDir.FullName, "1.6/Languages"),
                Path.Combine(mod.RootDir.FullName, "1.5/Languages"),
                Path.Combine(mod.RootDir.FullName, "Common/Languages"),
                Path.Combine(mod.RootDir.FullName, "Languages")
            };
            foreach (var path in searchPaths) if (Directory.Exists(path)) return path;
            return null;
        }

        private static string GetEffectiveDefsPath(ModMetaData mod)
        {
            string[] searchPaths = {
                Path.Combine(mod.RootDir.FullName, "1.6/Defs"),
                Path.Combine(mod.RootDir.FullName, "1.5/Defs"),
                Path.Combine(mod.RootDir.FullName, "Common/Defs"),
                Path.Combine(mod.RootDir.FullName, "Defs")
            };
            foreach (var path in searchPaths) if (Directory.Exists(path)) return path;
            return null;
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
        }

        // 🌟 V4.6 新增：單獨翻譯模組引擎
        public static async void StartSingleScan(ModMetaData targetMod)
        {
            try
            {
                AutoTranslatorSettings.IsRunning = true;
                EnsurePackInitialized();
                var settings = AutoTranslatorMod.Settings;

                settings.CurrentProgress = 0f;
                settings.CurrentTaskName = $"Translating: {targetMod.Name}";

                AutoTranslatorSettings.AddLog($"🎯 [System] 開始單獨翻譯模組: {targetMod.Name}");

                // 單一模組也稍微建一下全域池（拿所有啟用的模組來建，確保能參考）
                var activeMods = ModLister.AllInstalledMods.Where(m => m.Active && !OfficialModules.Contains(m.PackageId.ToLower())).ToList();
                BuildGlobalTranslationDatabase(activeMods);

                if (AutoTranslatorSettings.IsCancellationRequested) return;

                string langRoot = GetEffectiveLangPath(targetMod) ?? "";
                string defsRoot = GetEffectiveDefsPath(targetMod) ?? "";

                bool hasLang = !string.IsNullOrEmpty(langRoot);
                bool hasDefs = !string.IsNullOrEmpty(defsRoot);

                if (!hasLang && !hasDefs)
                {
                    AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
                }
                else
                {
                    if (hasLang)
                    {
                        string englishKeyed = Path.Combine(langRoot, "English/Keyed");
                        if (Directory.Exists(englishKeyed))
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_KeyedScan".Translate());
                            await ProcessModKeyed(targetMod, englishKeyed);
                        }
                    }

                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    if (hasDefs || hasLang)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_DefScan".Translate());
                        await ProcessModDefInjected(targetMod, langRoot, defsRoot);
                    }
                }

                if (!AutoTranslatorSettings.IsCancellationRequested)
                {
                    settings.CurrentTaskName = "ATC_TaskDone".Translate();
                    settings.CurrentProgress = 1f;
                    AutoTranslatorSettings.AddLog("🎉 單獨翻譯任務完美收工！");
                    AutoTranslatorSettings.RequestReload(5f); // 單一模組翻得快，5秒就夠了
                }
            }
            catch (Exception e)
            {
                AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
                Log.Error($"[AutoTranslationCore] 單一產線意外中斷: {e.Message}");
            }
            finally
            {
                ClearGlobalTranslationDatabase();
                AutoTranslatorSettings.IsRunning = false;
                if (AutoTranslatorSettings.IsCancellationRequested)
                {
                    AutoTranslatorSettings.AddLog("🛑 [System] 產線已安全中斷。");
                    AutoTranslatorMod.Settings.CurrentTaskName = "已中斷";
                }
            }
        }

        public static async void StartFullScan()
        {
            try
            {
                AutoTranslatorSettings.IsRunning = true;
                EnsurePackInitialized();
                var settings = AutoTranslatorMod.Settings;
                var mods = ModLister.AllInstalledMods.Where(m =>
                    !OfficialModules.Contains(m.PackageId.ToLower()) &&
                    m.PackageId.ToLower() != "autotranslator.core" &&
                    m.PackageId.ToLower() != "aitranslation.pack" &&
                    (!settings.OnlyScanActiveMods || m.Active)).ToList();

                int total = mods.Count;
                int current = 0;

                AutoTranslatorSettings.AddLog("ATC_Log_StartScan".Translate(total));

                BuildGlobalTranslationDatabase(mods);

                foreach (var mod in mods)
                {
                    // 🌟 V4.6 緊急煞車檢查
                    if (AutoTranslatorSettings.IsCancellationRequested) break;

                    current++;
                    settings.CurrentProgress = (float)current / total;
                    settings.CurrentTaskName = $"Translating: {mod.Name}";

                    AutoTranslatorSettings.AddLog("ATC_Log_ScanMod".Translate(mod.Name));

                    string langRoot = GetEffectiveLangPath(mod) ?? "";
                    string defsRoot = GetEffectiveDefsPath(mod) ?? "";

                    bool hasLang = !string.IsNullOrEmpty(langRoot);
                    bool hasDefs = !string.IsNullOrEmpty(defsRoot);

                    if (!hasLang && !hasDefs)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_SkipMod".Translate());
                        continue;
                    }

                    if (hasLang)
                    {
                        string englishKeyed = Path.Combine(langRoot, "English/Keyed");
                        if (Directory.Exists(englishKeyed))
                        {
                            AutoTranslatorSettings.AddLog("ATC_Log_KeyedScan".Translate());
                            await ProcessModKeyed(mod, englishKeyed);
                        }
                    }

                    if (AutoTranslatorSettings.IsCancellationRequested) break;

                    if (hasDefs || hasLang)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_DefScan".Translate());
                        await ProcessModDefInjected(mod, langRoot, defsRoot);
                    }
                }

                if (!AutoTranslatorSettings.IsCancellationRequested)
                {
                    settings.CurrentTaskName = "ATC_TaskDone".Translate();
                    settings.CurrentProgress = 1f;
                    AutoTranslatorSettings.AddLog("ATC_Log_TaskDone".Translate());

                    AutoTranslatorSettings.AddLog("🎉 " + "ATC_Log_AllTranslationWritten".Translate());
                    AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_StartingHotReload".Translate());
                    AutoTranslatorSettings.RequestReload(10f);
                }
            }
            catch (Exception e)
            {
                AutoTranslatorSettings.AddLog("ATC_Log_TaskError".Translate(e.Message));
                Log.Error($"[AutoTranslationCore] 產線意外中斷: {e.Message}");
            }
            finally
            {
                ClearGlobalTranslationDatabase();
                AutoTranslatorSettings.IsRunning = false;
                if (AutoTranslatorSettings.IsCancellationRequested)
                {
                    AutoTranslatorSettings.AddLog("🛑 [System] 產線已安全中斷，已翻譯的內容已保留。");
                    AutoTranslatorMod.Settings.CurrentTaskName = "已中斷";
                }
            }
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

            GlobalPrimaryDefDict.Clear(); GlobalSecondaryDefDict.Clear();
            GlobalPrimaryKeyedDict.Clear(); GlobalSecondaryKeyedDict.Clear();

            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);

            foreach (var mod in mods)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return;

                string langRoot = GetEffectiveLangPath(mod);
                if (string.IsNullOrEmpty(langRoot)) continue;

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

            if (lower.EndsWith("defname") || lower.EndsWith("class") || lower == "defname") return false;

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

                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 1 && !text.Contains(".xml") && !text.StartsWith("Tex/") && !text.StartsWith("UI/"))
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
                catch { /* 遇到損壞的 XML 自動略過 */ }
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

            foreach (string file in Directory.GetFiles(englishPath, "*.xml", SearchOption.AllDirectories))
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return;

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
                        var res = await SafeTranslateBatch(valuesToAI);
                        if (res != null)
                        {
                            for (int i = 0; i < keysToAI.Count; i++) finalData[keysToAI[i]] = res[i];
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
                    Log.Warning($"[AutoTranslationCore] XML 解析錯誤 ({mod.Name}): {xmlEx.Message}");
                    continue;
                }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_Unknown".Translate(mod.Name, GetShortPath(file)));
                    Log.Warning($"[AutoTranslationCore] 檔案處理異常 ({mod.Name}): {ex.Message}");
                    continue;
                }
            }
        }

        private static async Task ProcessModDefInjected(ModMetaData mod, string langRoot, string defsRoot)
        {
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);

            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional) secondaryTag = "[來自簡中]";
            else if (settings.TargetLang == TargetLanguage.Simplified) secondaryTag = "[來自繁中]";

            string packDefBaseDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "DefInjected", mod.PackageId);

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

            if (!string.IsNullOrEmpty(defsRoot) && Directory.Exists(defsRoot))
            {
                var extracted = ExtractEnglishFromRawDefs(defsRoot);
                foreach (var kv in extracted)
                {
                    if (!allKnownDefs.ContainsKey(kv.Key)) allKnownDefs[kv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inner in kv.Value) allKnownDefs[kv.Key][inner.Key] = inner.Value;
                }
            }

            if (!string.IsNullOrEmpty(langRoot))
            {
                AddToAllDefs(Path.Combine(langRoot, "English", "DefInjected"));
                AddToAllDefs(Path.Combine(langRoot, targetFolder, "DefInjected"));
                if (!string.IsNullOrEmpty(otherFolder))
                {
                    AddToAllDefs(Path.Combine(langRoot, otherFolder, "DefInjected"));
                }
            }

            if (allKnownDefs.Count == 0) return;

            foreach (var defGroup in allKnownDefs)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return;

                string defType = defGroup.Key;
                var currentDict = defGroup.Value;
                if (currentDict.Count == 0) continue;

                string targetFile = Path.Combine(packDefBaseDir, defType, "AutoTranslated_Defs.xml");
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
                    var res = await SafeTranslateBatch(valuesToAI);

                    if (AutoTranslatorSettings.IsCancellationRequested) return;

                    if (res != null)
                    {
                        for (int i = 0; i < keysToAI.Count; i++) finalData[keysToAI[i]] = res[i];
                        AutoTranslatorSettings.AddLog("ATC_Log_AIFinish".Translate(defType));
                    }
                    else AutoTranslatorSettings.AddLog("ATC_Log_AIFail".Translate(defType));
                }
                else AutoTranslatorSettings.AddLog("ATC_Log_NoMissing".Translate($"Def:{defType}"));

                if (finalData.Count > 0) SaveXml(targetFile, finalData);
            }
        }

        private static async Task<List<string>> SafeTranslateBatch(List<string> texts)
        {
            for (int r = 0; r < 3; r++)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return null;

                var res = await AutoTranslatorAPI.TranslateBatchAsync(texts);
                if (res != null && res.Count == texts.Count) return res;
                await Task.Delay(1000);
            }
            var list = new List<string>();
            foreach (var t in texts)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return null;

                var single = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { t });
                list.Add((single != null && single.Count > 0) ? single[0] : t);
            }
            return list;
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
                Log.Warning($"[AutoTranslationCore] XML 解析錯誤 ({Path.GetFileName(filePath)}): {ex.Message}");
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
    }
}