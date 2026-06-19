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
// 這個檔案負責 Keyed 與 DefInjected 翻譯處理。
// EN: This file processes Keyed and DefInjected translation data.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        // 這個方法負責處理 模組Keyed 流程。
        // EN: This method processes mod Keyed.
        private static async Task<int> ProcessModKeyedSources(ModMetaData mod, string langRoot)
        {
            int aiTranslatedCount = 0;
            var settings = AutoTranslatorMod.Settings;
            List<string> keyedSourcePaths = GetTranslatableLanguageBucketPaths(langRoot, settings.TargetLang, "Keyed", false);
            if (keyedSourcePaths.Count == 0) return 0;

            AutoTranslatorSettings.AddLog("⚙️ " + "ATC_Log_KeyedScan".Translate());
            HashSet<string> processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string keyedPath in keyedSourcePaths)
            {
                aiTranslatedCount += await ProcessModKeyed(mod, langRoot, keyedPath, processedKeys);
            }

            return aiTranslatedCount;
        }

        private static async Task<int> ProcessModKeyed(ModMetaData mod, string langRoot, string sourceKeyedPath, HashSet<string> processedKeys)
        {
            int aiTranslatedCount = 0;
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string packKeyedDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "Keyed");


            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional)
                secondaryTag = "ATC_Tag_FromSimplified".Translate().ToString();
            else if (settings.TargetLang == TargetLanguage.Simplified)
                secondaryTag = "ATC_Tag_FromTraditional".Translate().ToString();

            List<string> files = GetXmlFilesCached(sourceKeyedPath, SearchOption.AllDirectories);
            int totalFiles = files.Count;
            int currentFile = 0;
            string sourceKeyedRoot = Path.GetFullPath(sourceKeyedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (string file in files)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return aiTranslatedCount;


                currentFile++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentFile / totalFiles;

                try
                {
                    string modIdClean = mod.PackageId.Replace(".", "_");
                    string targetFileName = $"{modIdClean}_{Path.GetFileName(file)}";
                    string targetFile = Path.Combine(packKeyedDir, targetFileName);

                    var packDict = LoadXmlFileToDict(targetFile);
                    Dictionary<string, string> nativeTargetDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string relativeKeyedFile = Path.GetFileName(file);
                    string fullFile = Path.GetFullPath(file);
                    if (fullFile.StartsWith(sourceKeyedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        fullFile.StartsWith(sourceKeyedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        relativeKeyedFile = fullFile.Substring(sourceKeyedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }

                    foreach (string targetLangDir in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        foreach (string targetKeyedDir in GetLanguageBucketPaths(targetLangDir, "Keyed"))
                        {
                            string targetKeyedFile = Path.Combine(targetKeyedDir, relativeKeyedFile);
                            foreach (var kv in LoadXmlFileToDict(targetKeyedFile, settings.TargetLang))
                            {
                                nativeTargetDict[kv.Key] = kv.Value;
                            }
                        }
                    }

                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);

                    Dictionary<string, string> finalData = new Dictionary<string, string>(packDict, StringComparer.OrdinalIgnoreCase);
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
                        if (processedKeys != null && processedKeys.Contains(key)) continue;

                        if (nativeTargetDict.TryGetValue(key, out string nativeVal)) finalData[key] = nativeVal;
                        else if (packDict.TryGetValue(key, out string packVal))
                            UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, packVal, node.InnerText);
                        else if (GlobalPrimaryKeyedDict.TryGetValue(key, out string pVal))
                            UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, pVal, node.InnerText);
                        else if (GlobalSecondaryKeyedDict.TryGetValue(key, out string sVal) && !string.IsNullOrEmpty(secondaryTag))
                        {


                            keysToAI.Add(key);
                            valuesToAI.Add(PrepareSecondaryTranslationSource(sVal, node.InnerText));
                        }
                        else if (keyedFileLooksLikeTarget || LanguageDetector.LooksLikeTargetLanguage(node.InnerText, settings.TargetLang))
                        {
                            finalData[key] = node.InnerText;
                        }
                        else { keysToAI.Add(key); valuesToAI.Add(node.InnerText); }

                        if (processedKeys != null && finalData.ContainsKey(key)) processedKeys.Add(key);
                    }

                    if (keysToAI.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("🔌 " + AutoTranslatorAPI.TranslateText("ATC_Log_FoundMissing", "Keyed", keysToAI.Count));
                        var res = await SafeTranslateBatch(valuesToAI, $"{mod.Name} / {Path.GetFileName(file)}"); if (res != null)
                        {
                            int acceptedCount = 0;
                            for (int i = 0; i < keysToAI.Count; i++)
                            {
                                string k = keysToAI[i];
                                string v = res[i];


                                if (!TryAcceptTranslatedValue(v, valuesToAI[i], out v))
                                {
                                    continue;
                                }

                                finalData[k] = v;
                                if (processedKeys != null) processedKeys.Add(k);
                                acceptedCount++;
                            }

                            AutoTranslatorSettings.AddLog("✨ " + "ATC_Log_AIFinish".Translate("Keyed"));
                            aiTranslatedCount += acceptedCount;
                        }
                        else AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_AIFail".Translate("Keyed"));
                    }
                    AutoTranslatorSettings.AddLog("✅ " + AutoTranslatorAPI.TranslateText("ATC_Log_NoMissing", Path.GetFileName(file)));

                    if (finalData.Count > 0) SaveXml(targetFile, finalData);
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
            return aiTranslatedCount;
        }


        // 這個方法負責處理 模組DefInjected 流程。
        // EN: This method processes mod Def Injected.
        private static async Task<int> ProcessModDefInjected(ModMetaData mod, List<string> langRoots, List<string> defsRoots)
        {
            int aiTranslatedCount = 0;
            var settings = AutoTranslatorMod.Settings;
            string targetFolder = GetFolderNameByLanguage(settings.TargetLang);
            string otherFolder = GetSecondaryFolderNameByLanguage(settings.TargetLang);
            string secondaryTag = "";
            if (settings.TargetLang == TargetLanguage.Traditional)
                secondaryTag = "ATC_Tag_FromSimplified".Translate().ToString();
            else if (settings.TargetLang == TargetLanguage.Simplified)
                secondaryTag = "ATC_Tag_FromTraditional".Translate().ToString();
            string packDefBaseDir = Path.Combine(GetLocalPackPath(), "Languages", targetFolder, "DefInjected");


            var englishKeys = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var modSelfTargetLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var modSelfSecondaryLang = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);


            Action<string, Dictionary<string, Dictionary<string, string>>, TargetLanguage?> LoadDefsToDict = (path, targetDict, lang) => {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
                foreach (var typeDir in Directory.GetDirectories(path))
                {
                    string defType = Path.GetFileName(typeDir);
                    if (!targetDict.ContainsKey(defType))
                        targetDict[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in GetXmlFilesCached(typeDir, SearchOption.AllDirectories))
                    {
                        var d = LoadXmlFileToDict(file, lang);
                        foreach (var kv in d) targetDict[defType][kv.Key] = kv.Value;
                    }
                }
                foreach (var file in GetXmlFilesCached(path, SearchOption.TopDirectoryOnly))
                {
                    if (!targetDict.ContainsKey("General"))
                        targetDict["General"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var d = LoadXmlFileToDict(file, lang);
                    foreach (var kv in d) targetDict["General"][kv.Key] = kv.Value;
                }
            };

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


            foreach (var lRoot in langRoots)
            {
                List<string> sourceDefDirs = GetTranslatableLanguageBucketPaths(lRoot, settings.TargetLang, "DefInjected", false);
                for (int i = sourceDefDirs.Count - 1; i >= 0; i--)
                {
                    LoadDefsToDict(sourceDefDirs[i], englishKeys, null);
                }

                foreach (string targetLangDir in ResolveLanguageFolders(lRoot, targetFolder))
                {
                    foreach (string targetDefDir in GetLanguageBucketPaths(targetLangDir, "DefInjected"))
                    {
                        LoadDefsToDict(targetDefDir, modSelfTargetLang, settings.TargetLang);
                    }
                }

                if (!string.IsNullOrEmpty(otherFolder))
                {
                    TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                    foreach (string secondaryLangDir in ResolveLanguageFolders(lRoot, otherFolder))
                    {
                        foreach (string secondaryDefDir in GetLanguageBucketPaths(secondaryLangDir, "DefInjected"))
                        {
                            LoadDefsToDict(secondaryDefDir, modSelfSecondaryLang, secLang);
                        }
                    }
                }
            }

            foreach (var lRoot in langRoots)
            {
                foreach (string targetLangDir in ResolveLanguageFolders(lRoot, targetFolder))
                {
                    foreach (string targetDefDir in GetLanguageBucketPaths(targetLangDir, "DefInjected"))
                    {
                        LoadDefsToDict(targetDefDir, modSelfTargetLang, settings.TargetLang);
                    }
                }

                if (!string.IsNullOrEmpty(otherFolder))
                {
                    TargetLanguage secLang = settings.TargetLang == TargetLanguage.Traditional ? TargetLanguage.Simplified : TargetLanguage.Traditional;
                    foreach (string secondaryLangDir in ResolveLanguageFolders(lRoot, otherFolder))
                    {
                        foreach (string secondaryDefDir in GetLanguageBucketPaths(secondaryLangDir, "DefInjected"))
                        {
                            LoadDefsToDict(secondaryDefDir, modSelfSecondaryLang, secLang);
                        }
                    }
                }
            }

            if (englishKeys.Count == 0 && modSelfTargetLang.Count == 0)
                return aiTranslatedCount;


            int modSelfTargetCount = modSelfTargetLang.Sum(kv => kv.Value.Count);
            if (modSelfTargetCount > 0)
            {
                AutoTranslatorSettings.AddLog("✅ " +
                    AutoTranslatorAPI.TranslateText("ATC_Log_SkipExistingTranslation", mod.Name, modSelfTargetCount));
            }


            var allDefTypes = new HashSet<string>(englishKeys.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var k in modSelfTargetLang.Keys) allDefTypes.Add(k);

            int totalDefs = allDefTypes.Count;
            int currentDef = 0;

            foreach (var defType in allDefTypes)
            {
                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested) return aiTranslatedCount;

                currentDef++;
                AutoTranslatorMod.Settings.SubProgress = (float)currentDef / totalDefs;


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


                    if (selfDict != null && selfDict.TryGetValue(key, out string selfVal)
                             && !string.IsNullOrWhiteSpace(selfVal))
                    {
                        finalData[key] = selfVal;
                    }


                    else if (packDict.TryGetValue(key, out string packVal))
                    {
                        UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, packVal, engDict != null && engDict.TryGetValue(key, out string packSourceVal) ? packSourceVal : "");
                    }

                    else if (GlobalPrimaryDefDict.TryGetValue(globalKey, out string pVal)
                             || GlobalPrimaryDefDict.TryGetValue(globalKeyGen, out pVal))
                    {
                        UseExistingOrQueueForAI(finalData, keysToAI, valuesToAI, key, pVal, engDict != null && engDict.TryGetValue(key, out string globalSourceVal) ? globalSourceVal : "");
                    }

                    else if (modSelfSecondaryLang.TryGetValue(defType, out var secDict)
                             && secDict.TryGetValue(key, out string secVal)
                             && !string.IsNullOrEmpty(secondaryTag))
                    {
                        keysToAI.Add(key);
                        valuesToAI.Add(PrepareSecondaryTranslationSource(secVal, engDict != null && engDict.TryGetValue(key, out string secondarySourceVal) ? secondarySourceVal : ""));
                    }

                    else if ((GlobalSecondaryDefDict.TryGetValue(globalKey, out string sVal)
                              || GlobalSecondaryDefDict.TryGetValue(globalKeyGen, out sVal))
                             && !string.IsNullOrEmpty(secondaryTag))
                    {
                        keysToAI.Add(key);
                        valuesToAI.Add(PrepareSecondaryTranslationSource(sVal, engDict != null && engDict.TryGetValue(key, out string globalSecondarySourceVal) ? globalSecondarySourceVal : ""));
                    }

                    else if (engDict != null && engDict.TryGetValue(key, out string engVal)
                             && !string.IsNullOrEmpty(engVal))
                    {
                        if (LanguageDetector.LooksLikeTargetLanguage(engVal, settings.TargetLang))
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
                        int acceptedCount = 0;
                        for (int i = 0; i < keysToAI.Count; i++)
                        {
                            string k = keysToAI[i];
                            string v = res[i];


                            if (!TryAcceptTranslatedValue(v, valuesToAI[i], out v))
                            {
                                continue;
                            }

                            finalData[k] = v;
                            acceptedCount++;
                        }
                        AutoTranslatorSettings.AddLog("✨ " + AutoTranslatorAPI.TranslateText("ATC_Log_AIFinish", defType));
                        aiTranslatedCount += acceptedCount;
                    }
                    else AutoTranslatorSettings.AddLog("⚠️ " + AutoTranslatorAPI.TranslateText("ATC_Log_AIFail", defType));
                }
                else
                {
                    AutoTranslatorSettings.AddLog("✅ " + AutoTranslatorAPI.TranslateText("ATC_Log_NoMissing", $"Def:{defType}"));
                }

                if (finalData.Count > 0) SaveXml(targetFile, finalData);
            }
            return aiTranslatedCount;
        }


        // 這個方法負責處理 Safe翻譯Batch 相關流程。
        // EN: This method handles safe translate batch.
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

        // 這個方法負責處理 SafeSlice 相關流程。
        // EN: This method handles safe slice.
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


        // 這個方法負責處理 UseExistingOr佇列ForAI 相關流程。
        // EN: This method handles use existing or queue for AI.
        private static void UseExistingOrQueueForAI(Dictionary<string, string> finalData, List<string> keysToAI, List<string> valuesToAI, string key, string existingTranslation, string sourceText)
        {
            if (!string.IsNullOrWhiteSpace(sourceText) && IsUntranslatableGrammarRule(sourceText))
            {
                finalData[key] = sourceText;
                return;
            }

            string candidate = existingTranslation;
            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                candidate = SanitizeTranslationResult(existingTranslation, sourceText);
            }

            if (!string.IsNullOrWhiteSpace(sourceText) &&
                (HasProtectedTokenMismatch(candidate, sourceText) || HasFormatArgumentMismatch(candidate, sourceText)))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchDetected++);
                keysToAI.Add(key);
                valuesToAI.Add(sourceText);
                return;
            }

            if (!string.IsNullOrWhiteSpace(sourceText) && TranslationHasLikelyEnglishResidual(candidate, sourceText, true))
            {
                keysToAI.Add(key);
                valuesToAI.Add(sourceText);
                return;
            }

            finalData[key] = candidate;
        }

        private static string PrepareSecondaryTranslationSource(string secondaryTranslation, string primarySourceText)
        {
            if (string.IsNullOrWhiteSpace(primarySourceText)) return secondaryTranslation;
            if (IsUntranslatableGrammarRule(primarySourceText)) return primarySourceText;

            string candidate = SanitizeTranslationResult(secondaryTranslation, primarySourceText);
            if (HasProtectedTokenMismatch(candidate, primarySourceText) || HasFormatArgumentMismatch(candidate, primarySourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchDetected++);
                return primarySourceText;
            }

            return candidate;
        }


        // 這個方法負責翻譯 AdaptiveSmallChunks 內容。
        // EN: This method translates adaptive small chunks.
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


        // 這個方法負責處理 RetryLikelyEnglishResiduals 相關流程。
        // EN: This method handles retry likely english residuals.
        private static async Task<List<string>> RetryLikelyEnglishResiduals(List<string> sourceTexts, List<string> translatedTexts, string contextInfo)
        {
            if (sourceTexts == null || translatedTexts == null || sourceTexts.Count != translatedTexts.Count)
            {
                return translatedTexts;
            }

            for (int i = 0; i < translatedTexts.Count; i++)
            {
                string sanitized = SanitizeTranslationResult(translatedTexts[i], sourceTexts[i]);
                bool tokenMismatch = HasProtectedTokenMismatch(sanitized, sourceTexts[i]) ||
                    HasFormatArgumentMismatch(sanitized, sourceTexts[i]);
                bool englishResidual = false;
                if (tokenMismatch)
                {
                    AddValidationStat(s => s.ProtectedTokenMismatchDetected++);
                }
                else
                {
                    englishResidual = TranslationHasLikelyEnglishResidual(sanitized, sourceTexts[i], true);
                    if (!englishResidual)
                    {
                        translatedTexts[i] = sanitized;
                        continue;
                    }
                }

                if (AutoTranslatorSettings.IsCancellationRequested || AutoTranslatorSettings.IsSkipCurrentRequested)
                {
                    return translatedTexts;
                }

                if (englishResidual)
                {
                    AddValidationStat(s => s.EnglishResidualRetried++);
                }
                if (tokenMismatch)
                {
                    AddValidationStat(s => s.ProtectedTokenMismatchRetried++);
                }
                List<string> single = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { sourceTexts[i] }, suppressFinalParseError: true);
                if (single != null && single.Count > 0)
                {
                    string singleSanitized = SanitizeTranslationResult(single[0], sourceTexts[i]);
                    if (!HasProtectedTokenMismatch(singleSanitized, sourceTexts[i]) &&
                        !HasFormatArgumentMismatch(singleSanitized, sourceTexts[i]) &&
                        !TranslationHasLikelyEnglishResidual(singleSanitized, sourceTexts[i], false))
                    {
                        translatedTexts[i] = singleSanitized;
                        continue;
                    }
                }

                if (TrySplitGrammarRule(sourceTexts[i], out string grammarPrefix, out string grammarRuleName, out string grammarRightSide) &&
                    ShouldTranslateGrammarRuleRightSide(grammarRuleName, grammarRightSide))
                {
                    List<string> rightSideOnly = await AutoTranslatorAPI.TranslateBatchAsync(new List<string> { grammarRightSide.Trim() }, suppressFinalParseError: true);
                    if (rightSideOnly != null && rightSideOnly.Count > 0)
                    {
                        string merged = grammarPrefix + rightSideOnly[0].TrimStart();
                        string mergedSanitized = SanitizeTranslationResult(merged, sourceTexts[i]);
                        if (!HasProtectedTokenMismatch(mergedSanitized, sourceTexts[i]) &&
                            !HasFormatArgumentMismatch(mergedSanitized, sourceTexts[i]) &&
                            !TranslationHasLikelyEnglishResidual(mergedSanitized, sourceTexts[i], false))
                        {
                            translatedTexts[i] = mergedSanitized;
                            continue;
                        }
                    }
                }

                if (englishResidual)
                {
                    MarkEnglishResidualRejected(contextInfo);
                    translatedTexts[i] = sanitized;
                    continue;
                }
                if (tokenMismatch)
                {
                    AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                    if (!string.IsNullOrWhiteSpace(contextInfo))
                    {
                        AutoTranslatorSettings.AddLog(
                            AutoTranslatorAPI.TranslateText("ATC_Log_ProtectedTokenMismatchRejected", contextInfo));
                    }
                }
                translatedTexts[i] = null;
            }

            return translatedTexts;
        }

        // 這個方法負責嘗試執行 AcceptTranslatedValue 並回報是否成功。
        // EN: This method tries to accept translated value and reports whether it succeeded.
        private static bool TryAcceptTranslatedValue(string translated, string sourceText, out string sanitized)
        {
            sanitized = SanitizeTranslationResult(translated, sourceText);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return false;
            }

            if (HasProtectedTokenMismatch(sanitized, sourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                return false;
            }

            if (HasFormatArgumentMismatch(sanitized, sourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                return false;
            }

            if (RequiresProtectedTokenParity(sourceText) &&
                !LanguageDetector.LooksLikeTargetLanguage(sourceText, AutoTranslatorMod.Settings.TargetLang) &&
                string.Equals(sanitized, sourceText, StringComparison.Ordinal))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                return false;
            }

            if (!TranslationHasLikelyEnglishResidual(sanitized, sourceText, false))
            {
                return true;
            }

            return !IsUnchangedLikelyEnglishSource(sanitized, sourceText);
        }

        private static bool IsUnchangedLikelyEnglishSource(string translated, string sourceText)
        {
            if (string.IsNullOrWhiteSpace(translated) || string.IsNullOrWhiteSpace(sourceText)) return false;
            if (!string.Equals(translated.Trim(), sourceText.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (LanguageDetector.LooksLikeTargetLanguage(sourceText, AutoTranslatorMod.Settings.TargetLang)) return false;
            return TranslationHasLikelyEnglishResidual(translated, sourceText, false);
        }

        // 這個方法負責標記 EnglishResidualRejected 狀態。
        // EN: This method marks english residual rejected.
        private static void MarkEnglishResidualRejected(string contextInfo)
        {
            AddValidationStat(s => s.EnglishResidualFallback++);
            if (!string.IsNullOrWhiteSpace(contextInfo))
            {
                bool shouldLog;
                lock (_loggedEnglishResidualContexts)
                {
                    shouldLog = _loggedEnglishResidualContexts.Add(contextInfo);
                }

                if (shouldLog)
                {
                    AutoTranslatorSettings.AddLog("🩺 " +
                        AutoTranslatorAPI.TranslateText("ATC_Log_EnglishResidualRejected", contextInfo));
                }
            }
        }
    }
}
