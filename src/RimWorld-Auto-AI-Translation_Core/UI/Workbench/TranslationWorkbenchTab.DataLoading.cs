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
                WorkbenchModSnapshot snapshot = CreateWorkbenchModSnapshot(targetMod);
                if (snapshot == null)
                {
                    ATC_Dispatcher.RunOnMainThread(() => _isLoading = false);
                    return;
                }

                LoadRealData(snapshot);
            }

            private static WorkbenchModSnapshot CreateWorkbenchModSnapshot(Verse.ModMetaData targetMod)
            {
                if (targetMod == null || string.IsNullOrWhiteSpace(targetMod.PackageId)) return null;

                return new WorkbenchModSnapshot
                {
                    Mod = targetMod,
                    PackageId = targetMod.PackageId,
                    ModName = targetMod.Name ?? "",
                    RootDir = targetMod.RootDir != null ? targetMod.RootDir.FullName : "",
                    TargetLang = AutoTranslatorMod.Settings.TargetLang,
                    TargetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang)
                };
            }

            private static void StartLoadingModForEditing(Verse.ModMetaData targetMod, string initialSearchText)
            {
                StartLoadingModForEditing(targetMod, initialSearchText, null);
            }

            private static void StartLoadingModForEditing(Verse.ModMetaData targetMod, WorkbenchFocusRequest focusRequest)
            {
                StartLoadingModForEditing(targetMod, focusRequest != null ? focusRequest.SearchText : "", focusRequest);
            }

            private static void StartLoadingModForEditing(Verse.ModMetaData targetMod, string initialSearchText, WorkbenchFocusRequest focusRequest)
            {
                WorkbenchModSnapshot snapshot = CreateWorkbenchModSnapshot(targetMod);
                if (snapshot == null) return;

                _editingMod = targetMod;
                _isLoading = true;
                _itemSearchText = initialSearchText ?? "";
                _pendingWorkbenchFocus = focusRequest;
                _activeWorkbenchFocus = null;
                _itemScroll = UnityEngine.Vector2.zero;
                InvalidateVisibleItemCache();
                Task.Run(() => LoadRealData(snapshot));
            }

            private static void LoadRealData(WorkbenchModSnapshot targetMod)
            {
                var resultData = new Dictionary<string, List<WorkbenchItem>>();
                var langRoots = AutoTranslatorScanner.GetAllEffectiveLangPaths(targetMod.PackageId, targetMod.RootDir);
                var defsRoots = AutoTranslatorScanner.GetAllEffectiveDefsPaths(targetMod.PackageId, targetMod.RootDir);


                string targetLangFolder = targetMod.TargetLangFolder;

                var engKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var transKeyed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var langRoot in langRoots)
                {
                    foreach (string sourceKeyedPath in AutoTranslatorScanner.GetTranslatableLanguageBucketPaths(langRoot, targetMod.TargetLang, "Keyed", false))
                    {
                        var dict = AutoTranslatorScanner.LoadXmlFilesToDict(sourceKeyedPath);
                        foreach (var kv in dict)
                        {
                            if (!engKeyed.ContainsKey(kv.Key)) engKeyed[kv.Key] = kv.Value;
                        }
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
                    foreach (var file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(packKeyedDir, System.IO.SearchOption.AllDirectories))
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
                    foreach (var file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(workspaceKeyedDir, System.IO.SearchOption.AllDirectories))
                    {
                        var d = AutoTranslatorScanner.LoadXmlFileToDict(file);
                        foreach (var kv in d) transKeyed[kv.Key] = kv.Value;
                    }
                }

                if (engKeyed.Count > 0)
                {
                    var list = new List<WorkbenchItem>();
                    foreach (var kv in engKeyed)
                    {
                        string translated = transKeyed.ContainsKey(kv.Key) ? transKeyed[kv.Key] : "";
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = translated, OriginalTranslatedText = translated });
                    }
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
                        if (LanguageDetector.LooksLikeTargetLanguage(sample, targetMod.TargetLang))
                        {
                            rawDefTypesAlreadyTarget.Add(typeKv.Key);
                        }

                        if (!engDefs.ContainsKey(typeKv.Key)) engDefs[typeKv.Key] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in typeKv.Value) engDefs[typeKv.Key][kv.Key] = kv.Value;
                    }
                }
                bool rawDefsLookLikeTarget = LanguageDetector.LooksLikeTargetLanguage(
                    string.Join("\n", rawDefLanguageSamples.Take(240).ToArray()),
                    targetMod.TargetLang);

                string packDefDir = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder, "DefInjected");
                if (System.IO.Directory.Exists(packDefDir))
                {
                    foreach (var typeDir in System.IO.Directory.GetDirectories(packDefDir))
                    {
                        string defType = System.IO.Path.GetFileName(typeDir);
                        foreach (var file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(typeDir, System.IO.SearchOption.TopDirectoryOnly))
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
                        foreach (var file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(typeDir, System.IO.SearchOption.TopDirectoryOnly))
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
                        else if (rawDefsLookLikeTarget || rawDefTypesAlreadyTarget.Contains(defType) || LanguageDetector.LooksLikeTargetLanguage(kv.Value, targetMod.TargetLang))
                            translated = kv.Value;
                        list.Add(new WorkbenchItem { Key = kv.Key, OriginalText = kv.Value, TranslatedText = translated, OriginalTranslatedText = translated });
                    }
                    if (list.Count > 0) resultData[defType] = list;
                }

                ATC_Dispatcher.RunOnMainThread(() => {
                    if (_editingMod != targetMod.Mod) return;
                    _categorizedData = resultData;
                    WorkbenchFocusRequest focus = _pendingWorkbenchFocus;
                    string selectedCategory = _categorizedData.Keys.FirstOrDefault() ?? "";
                    if (focus != null && !string.IsNullOrWhiteSpace(focus.Category) && _categorizedData.ContainsKey(focus.Category))
                    {
                        selectedCategory = focus.Category;
                    }

                    _selectedCategory = selectedCategory;
                    _activeWorkbenchFocus = focus;
                    _pendingWorkbenchFocus = null;
                    _itemScroll = new UnityEngine.Vector2(0f, GetInitialItemScrollForFocus(focus, selectedCategory));
                    _catListScroll = new UnityEngine.Vector2(0f, GetInitialCategoryScrollForFocus(selectedCategory));
                    _categorizedDataVersion++;
                    _cachedVisibleItems = null;
                    _isLoading = false;
                });
            }

            private static float GetInitialItemScrollForFocus(WorkbenchFocusRequest focus, string selectedCategory)
            {
                if (focus == null || string.IsNullOrWhiteSpace(focus.Key)) return 0f;
                if (string.IsNullOrWhiteSpace(selectedCategory)) return 0f;
                if (!_categorizedData.TryGetValue(selectedCategory, out List<WorkbenchItem> items) || items == null) return 0f;

                int index = items.FindIndex(i => i != null && string.Equals(i.Key, focus.Key, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return 0f;
                int visibleIndex = index;
                if (!string.IsNullOrWhiteSpace(focus.SearchText))
                {
                    int matchedBefore = 0;
                    for (int i = 0; i < index; i++)
                    {
                        if (DoesWorkbenchItemMatchSearch(items[i], focus.SearchText)) matchedBefore++;
                    }

                    visibleIndex = matchedBefore;
                }

                return Mathf.Max(0f, visibleIndex * WorkbenchItemRowHeight - WorkbenchItemRowHeight);
            }

            private static float GetInitialCategoryScrollForFocus(string selectedCategory)
            {
                if (string.IsNullOrWhiteSpace(selectedCategory) || _categorizedData == null || _categorizedData.Count == 0) return 0f;

                int index = 0;
                foreach (string category in _categorizedData.Keys)
                {
                    if (string.Equals(category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        return Mathf.Max(0f, index * WorkbenchCategoryRowHeight - WorkbenchCategoryRowHeight);
                    }

                    index++;
                }

                return 0f;
            }

        }
}
