using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
        public static partial class TranslationWorkbenchTab
        {

            public static void RequestRefresh()
            {
                _translatedPackageIds = null;
                _cachedModSelectionList = null;
                _translatedModsCacheError = null;
            }


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

            private static HashSet<string> GetTranslatedPackageIdsSafe()
            {
                InitTranslatedModsCache();
                return _translatedPackageIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            private static void BuildTranslatedModsCache(HashSet<string> result)
            {
                if (result == null) return;
                string packPath = AutoTranslatorScanner.GetLocalPackPath();

                // ✨ 咪咪修復：必須使用 TargetLang (遊戲當前語系)，而不是雲端的 CloudTargetLang！
                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string langRoot = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);

                if (System.IO.Directory.Exists(langRoot))
                {
                    foreach (var file in System.IO.Directory.GetFiles(langRoot, "*.xml", System.IO.SearchOption.AllDirectories))
                    {
                        string fileName = System.IO.Path.GetFileName(file);
                        int splitIdx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
                        if (splitIdx == -1) splitIdx = fileName.LastIndexOf('_');

                        if (splitIdx > 0 && splitIdx < fileName.Length)
                        {
                            string pid = fileName.Substring(0, splitIdx).Replace("_", ".");
                            result.Add(pid);
                        }
                    }
                }
            }


            private static void ExecuteGlobalSearch(string keyword, TargetLanguage? langFilter)
            {
                _isGlobalSearching = true;
                _globalSearchProgress = 0f; // 歸零進度條
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

                    // ==================================================
                    // 🚀 第一階段：極速盤點總共有多少個檔案要掃描！(只搜集路徑不讀內容)
                    // ==================================================
                    var filesToScan = new List<(string FilePath, Verse.ModMetaData Mod)>();

                    // 1. 盤點 AI 翻譯包
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

                    // 2. 盤點所有原生模組自帶翻譯
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

                    // ==================================================
                    // 🚀 第二階段：開始正式開挖並更新進度條！
                    // ==================================================
                    int totalFiles = filesToScan.Count;
                    if (totalFiles == 0) goto SearchDone; // 沒東西就直接結束

                    for (int i = 0; i < totalFiles; i++)
                    {
                        var item = filesToScan[i];

                        // ✨ 實時更新進度條的百分比！
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
                    // 確保結束時進度條是滿的
                    _globalSearchProgress = 1f;

                    ATC_Dispatcher.RunOnMainThread(() => {
                        _globalSearchResults = results;
                        _isGlobalSearching = false;
                    });
                });
            }
        }
}
