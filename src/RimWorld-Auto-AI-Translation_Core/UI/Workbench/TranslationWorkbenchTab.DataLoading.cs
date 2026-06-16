using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責 翻譯工作台分頁資料載入 相關邏輯，支援 Auto Translation Core 的執行流程。
// EN: This file contains translation workbench tab data loading support code.

namespace AutoTranslator_Core
{
        // 這個類別負責 翻譯工作台分頁 的主要流程與狀態。
        // EN: This class manages the main workflow and state for TranslationWorkbenchTab.
        public static partial class TranslationWorkbenchTab
        {

            // 這個方法負責讀取 Real資料 資料。
            // EN: This method loads real data.
            private static void LoadRealData(Verse.ModMetaData targetMod)
            {
                var resultData = new Dictionary<string, List<WorkbenchItem>>();
                var langRoots = AutoTranslatorScanner.GetAllEffectiveLangPaths(targetMod);
                var defsRoots = AutoTranslatorScanner.GetAllEffectiveDefsPaths(targetMod);


                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);

                var engKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var transKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var langRoot in langRoots)
                {
                    string engKeyedPath = System.IO.Path.Combine(langRoot, "English", "Keyed");
                    if (System.IO.Directory.Exists(engKeyedPath))
                    {
                        var dict = AutoTranslatorScanner.LoadXmlFilesToDict(engKeyedPath);
                        foreach (var kv in dict) engKeyed[kv.Key] = kv.Value;
                    }
                    string modTransKeyedPath = System.IO.Path.Combine(langRoot, targetLangFolder, "Keyed");
                    if (System.IO.Directory.Exists(modTransKeyedPath))
                    {
                        var dict = AutoTranslatorScanner.LoadXmlFilesToDict(modTransKeyedPath);
                        foreach (var kv in dict) transKeyed[kv.Key] = kv.Value;
                    }
                }

                string packKeyedDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder, "Keyed");
                if (System.IO.Directory.Exists(packKeyedDir))
                {
                    string idMatch = targetMod.PackageId.Replace(".", "_").ToLower();
                    foreach (var file in System.IO.Directory.GetFiles(packKeyedDir, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        if (System.IO.Path.GetFileName(file).ToLower().Contains(idMatch))
                        {
                            var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                            foreach (var kv in d) transKeyed[kv.Key] = kv.Value;
                        }
                    }
                }

                string workspaceKeyedDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Upload_Workspace", targetMod.PackageId, targetLangFolder, "Keyed");
                if (System.IO.Directory.Exists(workspaceKeyedDir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(workspaceKeyedDir, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                        foreach (var kv in d) transKeyed[kv.Key] = kv.Value;
                    }
                }

                if (engKeyed.Count > 0)
                {
                    var list = new List<WorkbenchItem>();
                    foreach (var kv in engKeyed)
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = transKeyed.ContainsKey(kv.Key) ? transKeyed[kv.Key] : "" });
                    resultData["Keyed"] = list;
                }

                var engDefs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var transDefs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var rawDefTypesAlreadyTarget = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rawDefLanguageSamples = new List<string>();

                foreach (var defRoot in defsRoots)
                {
                    var dict = AutoTranslatorScanner.ExtractEnglishFromRawDefs(defRoot);
                    foreach (var typeKv in dict)
                    {
                        rawDefLanguageSamples.AddRange(typeKv.Value.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(40));
                        string sample = string.Join("\n", typeKv.Value.Values.Where(v => !string.IsNullOrWhiteSpace(v)).Take(120).ToArray());
                        if (LanguageDetector.LooksLikeTargetLanguage(sample, AutoTranslatorMod.Settings.TargetLang))
                        {
                            rawDefTypesAlreadyTarget.Add(typeKv.Key);
                        }

                        if (!engDefs.ContainsKey(typeKv.Key)) engDefs[typeKv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in typeKv.Value) engDefs[typeKv.Key][kv.Key] = kv.Value;
                    }
                }
                bool rawDefsLookLikeTarget = LanguageDetector.LooksLikeTargetLanguage(
                    string.Join("\n", rawDefLanguageSamples.Take(240).ToArray()),
                    AutoTranslatorMod.Settings.TargetLang);

                string packDefDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder, "DefInjected");
                if (System.IO.Directory.Exists(packDefDir))
                {
                    foreach (var typeDir in System.IO.Directory.GetDirectories(packDefDir))
                    {
                        string defType = System.IO.Path.GetFileName(typeDir);
                        foreach (var file in System.IO.Directory.GetFiles(typeDir, "*.xml"))
                        {
                            if (System.IO.Path.GetFileName(file).ToLower().Contains(targetMod.PackageId.Replace(".", "_").ToLower()))
                            {
                                if (!transDefs.ContainsKey(defType)) transDefs[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                                var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                foreach (var kv in d) transDefs[defType][kv.Key] = kv.Value;
                            }
                        }
                    }
                }

                string workspaceDefDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Upload_Workspace", targetMod.PackageId, targetLangFolder, "DefInjected");
                if (System.IO.Directory.Exists(workspaceDefDir))
                {
                    foreach (var typeDir in System.IO.Directory.GetDirectories(workspaceDefDir))
                    {
                        string defType = System.IO.Path.GetFileName(typeDir);
                        if (!transDefs.ContainsKey(defType)) transDefs[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var file in System.IO.Directory.GetFiles(typeDir, "*.xml"))
                        {
                            var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                            foreach (var kv in d) transDefs[defType][kv.Key] = kv.Value;
                        }
                    }
                }

                foreach (var typeKv in engDefs)
                {
                    string defType = typeKv.Key;
                    var list = new List<WorkbenchItem>();
                    foreach (var kv in typeKv.Value)
                    {
                        string translated = "";
                        if (transDefs.ContainsKey(defType) && transDefs[defType].ContainsKey(kv.Key))
                            translated = transDefs[defType][kv.Key];
                        else if (rawDefsLookLikeTarget || rawDefTypesAlreadyTarget.Contains(defType) || LanguageDetector.LooksLikeTargetLanguage(kv.Value, AutoTranslatorMod.Settings.TargetLang))
                            translated = kv.Value;
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = translated });
                    }
                    if (list.Count > 0) resultData[defType] = list;
                }

                ATC_Dispatcher.RunOnMainThread(() => {
                    _categorizedData = resultData;
                    _selectedCategory = _categorizedData.Keys.FirstOrDefault() ?? "";
                    _isLoading = false;
                });
            }

        }
}
