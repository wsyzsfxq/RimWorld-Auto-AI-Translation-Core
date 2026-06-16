using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責 翻譯工作台分頁搜尋 相關邏輯，支援 Auto Translation Core 的執行流程。
// EN: This file contains translation workbench tab search support code.

namespace AutoTranslator_Core
{
        // 這個類別負責 翻譯工作台分頁 的主要流程與狀態。
        // EN: This class manages the main workflow and state for TranslationWorkbenchTab.
        public static partial class TranslationWorkbenchTab
        {

            // 這個方法負責送出 Refresh 請求。
            // EN: This method requests refresh.
            public static void RequestRefresh()
            {
                _translatedPackageIds = null;
                _cachedModSelectionList = null;
                _translatedModsCacheError = null;
            }


            // 這個方法負責處理 InitTranslated模組快取 相關流程。
            // EN: This method handles init translated mods cache.
            private static void InitTranslatedModsCache()
            {
                if (_translatedPackageIds != null) return;
                if (_isTranslatedModsCacheLoading) return;

                _isTranslatedModsCacheLoading = true;
                _translatedModsCacheError = null;
                System.Threading.Tasks.Task.Run(() =>
                {
                    HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string error = null;
                    try
                    {
                        BuildTranslatedModsCache(result);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        _translatedPackageIds = result;
                        _translatedModsCacheError = error;
                        _cachedModSelectionList = null;
                        _isTranslatedModsCacheLoading = false;
                        if (!string.IsNullOrEmpty(error))
                        {
                            Verse.Log.Warning($"[AutoTranslationCore] Workbench translated-mod cache failed: {error}");
                        }
                    });
                });
            }

            // 這個方法負責取得 TranslatedPackageIdsSafe 資料。
            // EN: This method gets translated package ids safe.
            private static HashSet<string> GetTranslatedPackageIdsSafe()
            {
                InitTranslatedModsCache();
                return _translatedPackageIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // 這個方法負責建立 Translated模組快取 所需資料。
            // EN: This method builds translated mods cache.
            private static void BuildTranslatedModsCache(HashSet<string> result)
            {
                if (result == null) return;
                string packPath = AutoTranslatorScanner.GetLocalPackPath();


                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string langRoot = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);

                if (System.IO.Directory.Exists(langRoot))
                {
                    foreach (var file in System.IO.Directory.GetFiles(langRoot, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        string pid = ExtractPackageIdFromTranslationFile(file);
                        if (!string.IsNullOrEmpty(pid)) result.Add(pid);
                    }
                }

            }

            // 這個方法負責處理 ExtractPackageIdFrom翻譯File 相關流程。
            // EN: This method handles extract package id from translation file.
            private static string ExtractPackageIdFromTranslationFile(string file)
            {
                string fileName = System.IO.Path.GetFileName(file);
                int splitIdx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
                if (splitIdx == -1) splitIdx = fileName.LastIndexOf('_');
                return splitIdx > 0 && splitIdx < fileName.Length
                    ? fileName.Substring(0, splitIdx).Replace("_", ".")
                    : "";
            }


            // 這個方法負責執行 Global搜尋 動作。
            // EN: This method executes global search.
            private static void ExecuteGlobalSearch(string keyword, TargetLanguage? langFilter)
            {
                _isGlobalSearching = true;
                _globalSearchProgress = 0f;
                _globalSearchResults.Clear();

                System.Threading.Tasks.Task.Run(() => {
                    var results = new List<GlobalSearchResult>();
                    string searchLower = keyword.ToLower();
                    var activeMods = Verse.ModLister.AllInstalledMods.Where(m => m != null && m.Active).ToList();
                    var activeModsByPackageId = activeMods
                        .Where(m => !string.IsNullOrEmpty(m.PackageId))
                        .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                    string targetFolderFilter = langFilter.HasValue ? AutoTranslatorScanner.GetFolderNameByLanguage(langFilter.Value) : null;


                    var filesToScan = new List<(string FilePath, Verse.ModMetaData Mod)>();


                    string packPath = AutoTranslatorScanner.GetLocalPackPath();
                    string langsRoot = System.IO.Path.Combine(packPath, "Languages");
                    if (System.IO.Directory.Exists(langsRoot))
                    {
                        string[] searchDirs = targetFolderFilter != null
                            ? (System.IO.Directory.Exists(System.IO.Path.Combine(langsRoot, targetFolderFilter)) ? new[] { System.IO.Path.Combine(langsRoot, targetFolderFilter) } : new string[0])
                            : System.IO.Directory.GetDirectories(langsRoot);

                        foreach (var dir in searchDirs)
                        {
                            var allXmls = System.IO.Directory.GetFiles(dir, "*.xml", System.IO.SearchOption.AllDirectories);
                            foreach (var file in allXmls)
                            {
                                string fileName = System.IO.Path.GetFileName(file);
                                int splitIdx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
                                if (splitIdx == -1) splitIdx = fileName.LastIndexOf('_');

                                if (splitIdx > 0)
                                {
                                    string pid = fileName.Substring(0, splitIdx).Replace("_", ".");
                                    activeModsByPackageId.TryGetValue(pid, out var targetMod);
                                    if (targetMod != null) filesToScan.Add((file, targetMod));
                                }
                            }
                        }
                    }


                    foreach (var mod in activeMods)
                    {
                        string packageId = mod.PackageId ?? "";
                        if (packageId.Equals("auto.aitranslation.core", StringComparison.OrdinalIgnoreCase) ||
                            packageId.Equals("aitranslation.pack", StringComparison.OrdinalIgnoreCase)) continue;

                        var modLangRoots = AutoTranslatorScanner.GetAllEffectiveLangPaths(mod);
                        foreach (var langRoot in modLangRoots)
                        {
                            string[] searchDirs = targetFolderFilter != null
                                ? (System.IO.Directory.Exists(System.IO.Path.Combine(langRoot, targetFolderFilter)) ? new[] { System.IO.Path.Combine(langRoot, targetFolderFilter) } : new string[0])
                                : System.IO.Directory.GetDirectories(langRoot);

                            foreach (var dir in searchDirs)
                            {
                                var allXmls = System.IO.Directory.GetFiles(dir, "*.xml", System.IO.SearchOption.AllDirectories);
                                foreach (var file in allXmls) filesToScan.Add((file, mod));
                            }
                        }
                    }


                    int totalFiles = filesToScan.Count;
                    if (totalFiles == 0) goto SearchDone;

                    for (int i = 0; i < totalFiles; i++)
                    {
                        var item = filesToScan[i];


                        _globalSearchProgress = (float)i / totalFiles;

                        var dict = AutoTranslatorScanner.LoadXmlFileToDict(item.FilePath);
                        foreach (var kv in dict)
                        {
                            if ((kv.Value != null && kv.Value.ToLower().Contains(searchLower)) ||
                                (kv.Key != null && kv.Key.ToLower().Contains(searchLower)))
                            {
                                if (!results.Any(r => r.Mod == item.Mod && r.Key == kv.Key))
                                {
                                    results.Add(new GlobalSearchResult { Mod = item.Mod, Key = kv.Key, TranslatedText = kv.Value });
                                }
                                if (results.Count >= 200) goto SearchDone;
                            }
                        }
                    }

                SearchDone:

                    _globalSearchProgress = 1f;

                    ATC_Dispatcher.RunOnMainThread(() => {
                        _globalSearchResults = results;
                        _isGlobalSearching = false;
                    });
                });
            }
        }
}
