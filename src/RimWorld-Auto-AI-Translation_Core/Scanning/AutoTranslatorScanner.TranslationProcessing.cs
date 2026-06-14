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
        private static async Task<int> ProcessModKeyed(ModMetaData mod, string englishPath)
        {
            int aiTranslatedCount = 0; // ✨ 咪咪特製：AI 翻譯計數器
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
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return aiTranslatedCount;

                // 實時更新進度！
                currentFile++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentFile / totalFiles;

                try
                {
                    string modIdClean = mod.PackageId.Replace(".", "_");
                    string targetFileName = $"{modIdClean}_{Path.GetFileName(file)}";
                    string targetFile = Path.Combine(packKeyedDir, targetFileName);

                    var packDict = LoadXmlFileToDict(targetFile);
                    Dictionary<string, string> nativeTargetDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string relativeKeyedFile = "";
                    string englishKeyedMarker = Path.Combine("English", "Keyed");
                    int englishKeyedIndex = file.IndexOf(englishKeyedMarker, StringComparison.OrdinalIgnoreCase);
                    if (englishKeyedIndex >= 0)
                    {
                        relativeKeyedFile = file.Substring(englishKeyedIndex + englishKeyedMarker.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }

                    string langRoot = englishKeyedIndex >= 0 ? file.Substring(0, englishKeyedIndex).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : "";
                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        string targetKeyedFile = Path.Combine(targetLangDir, "Keyed", relativeKeyedFile);
                        foreach (var kv in LoadXmlFileToDict(targetKeyedFile, settings.TargetLang))
                        {
                            nativeTargetDict[kv.Key] = kv.Value;
                        }
                    }

                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);

                    Dictionary<string, string> finalData = new Dictionary<string, string>();
                    List<string> keysToAI = new List<string>();
                    List<string> valuesToAI = new List<string>();

                    if (doc.DocumentElement == null) continue;
                    string keyedLanguageSample = string.Join("\n", doc.DocumentElement.ChildNodes
                        .Cast<XmlNode>()
                        .Where(n => n.NodeType == XmlNodeType.Element && !string.IsNullOrWhiteSpace(n.InnerText))
                        .Select(n => n.InnerText)
                        .Take(80)
                        .ToArray());
                    bool keyedFileLooksLikeTarget = LanguageDetector.LooksLikeTargetLanguage(keyedLanguageSample, settings.TargetLang);

                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element || string.IsNullOrEmpty(node.InnerText)) continue;
                        string key = node.Name;

                        if (nativeTargetDict.TryGetValue(key, out string nativeVal)) finalData[key] = nativeVal;
                        else if (packDict.TryGetValue(key, out string packVal))
                            UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, packVal, node.InnerText);
                        else if (GlobalPrimaryKeyedDict.TryGetValue(key, out string pVal))
                            UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, pVal, node.InnerText);
                        else if (GlobalSecondaryKeyedDict.TryGetValue(key, out string sVal) && !string.IsNullOrEmpty(secondaryTag))
                        {
                            // 修正 Bug D：不再把 tag 拼進送 AI 的字串
                            // 改為：送純淨文本給 AI，後處理時再判斷是否要加 tag
                            keysToAI.Add(key);
                            valuesToAI.Add(sVal);  // 只送純淨內容，不帶 [來自簡中]
                        }
                        else if (keyedFileLooksLikeTarget || LanguageDetector.LooksLikeTargetLanguage(node.InnerText, settings.TargetLang))
                        {
                            finalData[key] = node.InnerText;
                        }
                        else { keysToAI.Add(key); valuesToAI.Add(node.InnerText); }
                    }

                    if (keysToAI.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("🔌 " + AutoTranslatorAPI.TranslateText("ATC_Log_FoundMissing", "Keyed", keysToAI.Count)); // 🔌代表通訊
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
                            aiTranslatedCount += keysToAI.Count; // ✨ 累加成功翻譯的數量
                        }
                        else AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_AIFail".Translate("Keyed")); // ⚠️代表警告失敗
                    }
                    AutoTranslatorSettings.AddLog("✅ " + AutoTranslatorAPI.TranslateText("ATC_Log_NoMissing", Path.GetFileName(file))); // ✅代表不缺字串

                    if (finalData.Count > 0 && nativeTargetDict.Count == 0) SaveXml(targetFile, finalData);
                }
                catch (XmlException xmlEx)
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + AutoTranslatorAPI.TranslateText("ATC_LogError_Format", mod.Name, GetShortPath(file)));
                    Log.Warning($"[AutoTranslationCore] XML Format Error ({mod.Name}): {xmlEx.Message}");
                }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + AutoTranslatorAPI.TranslateText("ATC_LogError_Unknown", mod.Name, GetShortPath(file)));
                    Log.Warning($"[AutoTranslationCore] Process Error ({mod.Name}): {ex.Message}");
                }
            }
            return aiTranslatedCount; // ✨ 回傳總數量
        }


        private static async Task<int> ProcessModDefInjected(ModMetaData mod, List<string> langRoots, List<string> defsRoots)
        {
            int aiTranslatedCount = 0; // ✨ 咪咪特製：AI 翻譯計數器
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
            var rawDefTypesAlreadyTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawDefLanguageSamples = new List<string>();

            // ✨ 架構師改造：加上 lang 參數
            Action<string, Dictionary<string, Dictionary<string, string>>, TargetLanguage?> LoadDefsToDict = (path, targetDict, lang) => {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                foreach (var typeDir in Directory.GetDirectories(path))
                {
                    string defType = Path.GetFileName(typeDir);
                    if (!targetDict.ContainsKey(defType))
                        targetDict[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                    {
                        var d = LoadXmlFileToDict(file, lang);
                        foreach (var kv in d) targetDict[defType][kv.Key] = kv.Value;
                    }
                }
                foreach (var file in Directory.GetFiles(path, "*.xml"))
                {
                    if (!targetDict.ContainsKey("General"))
                        targetDict["General"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var d = LoadXmlFileToDict(file, lang);
                    foreach (var kv in d) targetDict["General"][kv.Key] = kv.Value;
                }
            };
            // 1. 從 Defs 提取英文原文
            foreach (var dRoot in defsRoots)
            {
                var extracted = ExtractEnglishFromRawDefs(dRoot);
                foreach (var kv in extracted)
                {
                    rawDefLanguageSamples.AddRange(kv.Value.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(40));
                    string rawDefSample = string.Join("\n", kv.Value.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(120).ToArray());
                    if (LanguageDetector.LooksLikeTargetLanguage(rawDefSample, settings.TargetLang))
                    {
                        rawDefTypesAlreadyTarget.Add(kv.Key);
                    }

                    if (!englishKeys.ContainsKey(kv.Key))
                        englishKeys[kv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var inner in kv.Value) englishKeys[kv.Key][inner.Key] = inner.Value;
                }
            }
            bool rawDefsLookLikeTarget = LanguageDetector.LooksLikeTargetLanguage(
                string.Join("\n", rawDefLanguageSamples.Take(240).ToArray()),
                settings.TargetLang);

            // 在第 545 行附近的呼叫改為：
            foreach (var lRoot in langRoots)
            {
                LoadDefsToDict(Path.Combine(lRoot, "English", "DefInjected"), englishKeys, null); // 英文不檢查
                LoadDefsToDict(Path.Combine(lRoot, targetFolder, "DefInjected"), modSelfTargetLang, settings.TargetLang); // 檢查目標語言
                if (!string.IsNullOrEmpty(otherFolder))
                {
                    TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                    LoadDefsToDict(Path.Combine(lRoot, otherFolder, "DefInjected"), modSelfSecondaryLang, secLang); // 檢查次級語言
                }
            }

            foreach (var lRoot in langRoots)
            {
                foreach (string targetLangDir in ResolveLanguageFolders(lRoot, targetFolder))
                {
                    LoadDefsToDict(Path.Combine(targetLangDir, "DefInjected"), modSelfTargetLang, settings.TargetLang);
                }

                if (!string.IsNullOrEmpty(otherFolder))
                {
                    TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                    foreach (string secondaryLangDir in ResolveLanguageFolders(lRoot, otherFolder))
                    {
                        LoadDefsToDict(Path.Combine(secondaryLangDir, "DefInjected"), modSelfSecondaryLang, secLang);
                    }
                }
            }

            if (englishKeys.Count == 0 && modSelfTargetLang.Count == 0)
                return aiTranslatedCount;

            // 修正 Bug E：統計模組自帶翻譯量，給玩家看
            int modSelfTargetCount = modSelfTargetLang.Sum(kv => kv.Value.Count);
            if (modSelfTargetCount > 0)
            {
                AutoTranslatorSettings.AddLog("✅ " +
                    AutoTranslatorAPI.TranslateText("ATC_Log_SkipExistingTranslation", mod.Name, modSelfTargetCount));
            }

            // 合併 englishKeys 與 modSelfTargetLang 的所有 defType（取聯集）
            var allDefTypes = new HashSet<string>(englishKeys.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in modSelfTargetLang.Keys) allDefTypes.Add(k);

            int totalDefs = allDefTypes.Count;
            int currentDef = 0;

            foreach (var defType in allDefTypes)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return aiTranslatedCount;

                currentDef++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentDef / totalDefs;

                // 🛡️ 臉部模組終極防禦：這幾個 DefType 絕對不准翻譯，否則必定破圖！
                string defTypeLower = defType.ToLower();
                if (defTypeLower.Contains("facedef") || defTypeLower.Contains("eyedef") ||
                    defTypeLower.Contains("browdef") || defTypeLower.Contains("liddef") ||
                    defTypeLower.Contains("lashdef") || defTypeLower.Contains("mouthdef") ||
                    defTypeLower.Contains("nosedef") || defTypeLower.Contains("eardef") ||
                    defTypeLower.Contains("skindef") || defTypeLower.Contains("facialanimation"))
                {
                    AutoTranslatorSettings.AddLog($"🛡️ [System] 已攔截並保護高危險臉部模型：{defType}");
                    continue;
                }

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
                    if (selfDict != null && selfDict.TryGetValue(key, out string selfVal)
                             && !string.IsNullOrWhiteSpace(selfVal))
                    {
                        finalData[key] = selfVal;
                    }
                    // 優先級 2：模組自帶目標語言翻譯（例如模組原作者寫的簡中）
                    //          這是修正核心：直接採用，不送 AI
                    else if (packDict.TryGetValue(key, out string packVal))
                    {
                        UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, packVal, engDict != null && engDict.TryGetValue(key, out string packSourceVal) ? packSourceVal : "");
                    }
                    // 優先級 3：全域字典（其他模組或全域 Languages 的目標語言）
                    else if (GlobalPrimaryDefDict.TryGetValue(globalKey, out string pVal)
                             || GlobalPrimaryDefDict.TryGetValue(globalKeyGen, out pVal))
                    {
                        UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, pVal, engDict != null && engDict.TryGetValue(key, out string globalSourceVal) ? globalSourceVal : "");
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
                        if (rawDefsLookLikeTarget || rawDefTypesAlreadyTarget.Contains(defType) || LanguageDetector.LooksLikeTargetLanguage(engVal, settings.TargetLang))
                        {
                            finalData[key] = engVal;
                        }
                        else
                        {
                            keysToAI.Add(key);
                            valuesToAI.Add(engVal);
                        }
                    }
                }

                if (keysToAI.Count > 0)
                {
                    AutoTranslatorSettings.AddLog("🔌 " + AutoTranslatorAPI.TranslateText("ATC_Log_FoundMissing", defType, keysToAI.Count));
                    var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / Defs: {defType}");
                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return aiTranslatedCount;
                    if (res != null)
                    {
                        for (int i = 0; i < keysToAI.Count; i++)
                        {
                            string k = keysToAI[i];
                            string v = res[i];

                            // 新增：AI 回應的智慧清理
                            v = SanitizeTranslationResult(v, valuesToAI[i]);

                            finalData[k] = v;
                        }
                        AutoTranslatorSettings.AddLog("✨ " + AutoTranslatorAPI.TranslateText("ATC_Log_AIFinish", defType));
                        aiTranslatedCount += keysToAI.Count; // ✨ 累加成功翻譯的數量
                    }
                    else AutoTranslatorSettings.AddLog("⚠️ " + AutoTranslatorAPI.TranslateText("ATC_Log_AIFail", defType));
                }
                else
                {
                    AutoTranslatorSettings.AddLog("✅ " + AutoTranslatorAPI.TranslateText("ATC_Log_NoMissing", $"Def:{defType}"));
                }

                if (finalData.Count > 0) SaveXml(targetFile, finalData);
            }
            return aiTranslatedCount; // ✨ 回傳總數量
        }
        // 🌟 咪咪特製終極版翻譯引擎：分塊 + 併發 + 去重 + 指數退避 (全本地化 + 精準報錯版)！
        // 🌟 咪咪特製終極版翻譯引擎：分塊 + 併發 + 去重 + 指數退避 (全本地化 + 安全日誌防護版)！
        private static async Task<List<string>> SafeTranslateBatch(List<string> texts, string contextInfo)
        {
            if (texts == null || texts.Count == 0) return new List<string>();

            var uniqueTexts = texts.Distinct().ToList();
            var translatedDict = new Dictionary<string, string>();

            int chunkSize = Math.Max(1, AutoTranslatorAPI.GetCurrentRuntimeProfile().BatchSize);
            int maxConcurrency = Math.Max(1, AutoTranslatorMod.Settings.MaxThreads);
            List<Task> tasks = new List<Task>();

            using (SemaphoreSlim semaphore = new SemaphoreSlim(maxConcurrency))
            {
                for (int i = 0; i < uniqueTexts.Count; i += chunkSize)
                {
                    int chunkIndex = i;
                    int currentChunkSize = Math.Min(chunkSize, uniqueTexts.Count - chunkIndex);
                    List<string> chunk = SafeSlice(uniqueTexts, chunkIndex, currentChunkSize);
                    if (chunk.Count == 0) continue;

                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            List<string> chunkRes = null;
                            bool hasRetried = false;

                            for (int r = 0; r < 3; r++)
                            {
                                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                                chunkRes = await AutoTranslatorAPI.TranslateBatchAsync(chunk, suppressFinalParseError: true);

                                if (chunkRes != null && chunkRes.Count == chunk.Count)
                                {
                                    if (hasRetried)
                                    {
                                        AutoTranslatorSettings.AddLog("✅ " + "ATC_Log_ApiRecovered".Translate());
                                    }
                                    break;
                                }

                                hasRetried = true;
                                int baseDelay = (int)Math.Pow(2, r) * 1000;
                                int jitter = new System.Random().Next(50, 600);
                                int delayMs = baseDelay + jitter;

                                AutoTranslatorSettings.AddLog("⚠️ " + AutoTranslatorAPI.TranslateText("ATC_Log_ApiRetry", baseDelay / 1000));

                                // 🛡️ 安全寫入開發者日誌
                                ATC_Dispatcher.RunOnMainThread(() =>
                                    Verse.Log.Warning($"[AutoTranslationCore] " + AutoTranslatorAPI.TranslateText("ATC_Log_ApiRetry", baseDelay / 1000))
                                );

                                int remainingDelay = delayMs;
                                while (remainingDelay > 0)
                                {
                                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;

                                    int slice = Math.Min(remainingDelay, 100);
                                    await Task.Delay(slice);
                                    remainingDelay -= slice;
                                }
                            }

                            if (chunkRes == null || chunkRes.Count != chunk.Count)
                            {
                                chunkRes = await TranslateAdaptiveSmallChunks(chunk, contextInfo);
                            }

                            if (chunkRes == null || chunkRes.Count != chunk.Count)
                            {
                                AutoTranslatorSettings.AddLog("🔄 " + "ATC_Log_ApiFallback".Translate());

                                chunkRes = new List<string>();
                                bool loggedErrorForThisChunk = false;

                                foreach (var t in chunk)
                                {
                                    if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return;
                                    var single = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { t }, suppressFinalParseError: true);

                                    if (single != null && single.Count > 0)
                                    {
                                        chunkRes.Add(single[0]);
                                    }
                                    else
                                    {
                                        chunkRes.Add(t);

                                        if (!loggedErrorForThisChunk)
                                        {
                                            AutoTranslatorSettings.AddErrorLog("❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_ApiCritical", contextInfo));

                                            // 🛡️ 安全寫入開發者日誌
                                            ATC_Dispatcher.RunOnMainThread(() =>
                                                Verse.Log.Error($"[AutoTranslationCore] ❌ " + AutoTranslatorAPI.TranslateText("ATC_LogError_ApiCritical", contextInfo))
                                            );
                                            loggedErrorForThisChunk = true;
                                        }
                                    }
                                }
                            }

                            if (chunkRes != null && chunkRes.Count == chunk.Count)
                            {
                                chunkRes = await RetryLikelyEnglishResiduals(chunk, chunkRes, contextInfo);
                            }

                            if (chunkRes == null || chunkRes.Count != chunk.Count)
                            {
                                return;
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

        private static List<string> SafeSlice(List<string> source, int start, int count)
        {
            if (source == null || start < 0 || count <= 0 || start >= source.Count)
            {
                return new List<string>();
            }

            int safeCount = Math.Min(count, source.Count - start);
            if (safeCount <= 0)
            {
                return new List<string>();
            }

            var result = new List<string>(safeCount);
            for (int i = 0; i < safeCount; i++)
            {
                result.Add(source[start + i]);
            }

            return result;
        }


        private static void UseExistingOrQueueForAI(Dictionary<string, string> finalData, List<string> keysToAI, List<string> valuesToAI, string key, string existingTranslation, string sourceText)
        {
            if (!string.IsNullOrWhiteSpace(sourceText) && TranslationHasLikelyEnglishResidual(existingTranslation, sourceText, true))
            {
                keysToAI.Add(key);
                valuesToAI.Add(sourceText);
                return;
            }

            finalData[key] = existingTranslation;
        }


        private static async Task<List<string>> TranslateAdaptiveSmallChunks(List<string> chunk, string contextInfo)
        {
            if (chunk == null || chunk.Count <= 1) return null;

            int smallChunkSize = Math.Min(4, chunk.Count);
            if (smallChunkSize <= 0) return null;

            List<string> merged = new List<string>(chunk.Count);

            for (int i = 0; i < chunk.Count; i += smallChunkSize)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return null;

                List<string> smallChunk = SafeSlice(chunk, i, Math.Min(smallChunkSize, chunk.Count - i));
                if (smallChunk.Count == 0) return null;

                List<string> smallResult = await AutoTranslatorAPI.TranslateBatchAsync(smallChunk, suppressFinalParseError: true);
                if (smallResult == null || smallResult.Count != smallChunk.Count)
                {
                    return null;
                }

                smallResult = await RetryLikelyEnglishResiduals(smallChunk, smallResult, contextInfo);
                if (smallResult == null || smallResult.Count != smallChunk.Count)
                {
                    return null;
                }

                merged.AddRange(smallResult);
            }

            AutoTranslatorSettings.AddLog("[API] Adaptive small-batch retry succeeded.");
            return merged;
        }


        private static async Task<List<string>> RetryLikelyEnglishResiduals(List<string> sourceTexts, List<string> translatedTexts, string contextInfo)
        {
            if (sourceTexts == null || translatedTexts == null || sourceTexts.Count != translatedTexts.Count)
            {
                return translatedTexts;
            }

            for (int i = 0; i < translatedTexts.Count; i++)
            {
                string sanitized = SanitizeTranslationResult(translatedTexts[i], sourceTexts[i]);
                if (!TranslationHasLikelyEnglishResidual(sanitized, sourceTexts[i], true))
                {
                    translatedTexts[i] = sanitized;
                    continue;
                }

                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested)
                {
                    return translatedTexts;
                }

                AddValidationStat(s => s.EnglishResidualRetried++);
                List<string> single = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { sourceTexts[i] }, suppressFinalParseError: true);
                if (single != null && single.Count > 0)
                {
                    string singleSanitized = SanitizeTranslationResult(single[0], sourceTexts[i]);
                    if (!TranslationHasLikelyEnglishResidual(singleSanitized, sourceTexts[i], false))
                    {
                        translatedTexts[i] = singleSanitized;
                        continue;
                    }
                }

                AddValidationStat(s => s.EnglishResidualFallback++);
                AutoTranslatorSettings.AddLog($"🩺 [Validation] English residual still unresolved after retry: {contextInfo}");
                translatedTexts[i] = sanitized;
            }

            return translatedTexts;
        }
    }
}
