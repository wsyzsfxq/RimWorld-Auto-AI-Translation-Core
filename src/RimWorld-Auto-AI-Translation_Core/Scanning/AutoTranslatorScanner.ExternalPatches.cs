using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;
// 這個檔案負責 自動翻譯器掃描器ExternalPatches 相關邏輯，支援 Auto Translation Core 的執行流程。
// EN: This file contains auto translator scanner external patches support code.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        // 這個類別負責 External目標語言補丁 的主要流程與狀態。
        // EN: This class manages the main workflow and state for ExternalTargetLanguagePatch.
        private class ExternalTargetLanguagePatch
        {
            // 這個欄位保存 補丁PackageId 的執行狀態或快取資料。
            // EN: This field stores patch package id runtime state or cached data.
            public string PatchPackageId = "";
            // 這個欄位保存 補丁名稱 的執行狀態或快取資料。
            // EN: This field stores patch name runtime state or cached data.
            public string PatchName = "";
        }

        private static readonly object ExternalTargetLanguagePatchLock = new object();
        // 這個欄位保存 external目標語言補丁快取語言 的執行狀態或快取資料。
        // EN: This field stores external target language patch cache language runtime state or cached data.
        private static TargetLanguage _externalTargetLanguagePatchCacheLang;
        // 這個欄位保存 external目標語言補丁快取 的執行狀態或快取資料。
        // EN: This field stores external target language patch cache runtime state or cached data.
        private static Dictionary<string, ExternalTargetLanguagePatch> _externalTargetLanguagePatchCache = null;

        // 這個方法負責清除 External目標語言補丁快取 資料。
        // EN: This method clears external target language patch cache.
        public static void ClearExternalTargetLanguagePatchCache()
        {
            lock (ExternalTargetLanguagePatchLock)
            {
                _externalTargetLanguagePatchCache = null;
            }
        }

        // 這個方法負責判斷 HasActiveExternal目標語言補丁 條件是否成立。
        // EN: This method checks has active external target language patch.
        public static bool HasActiveExternalTargetLanguagePatch(ModMetaData mod, TargetLanguage targetLang)
        {
            return TryGetActiveExternalTargetLanguagePatch(mod, targetLang, out _, out _);
        }

        public static bool TryGetActiveExternalTargetLanguagePatch(
            ModMetaData mod,
            TargetLanguage targetLang,
            out string patchName,
            out string patchPackageId)
        {
            patchName = "";
            patchPackageId = "";
            if (mod == null || string.IsNullOrWhiteSpace(mod.PackageId)) return false;

            EnsureExternalTargetLanguagePatchCache(targetLang);
            string targetPackageId = NormalizeExternalPatchPackageId(mod.PackageId);

            lock (ExternalTargetLanguagePatchLock)
            {
                if (_externalTargetLanguagePatchCache != null &&
                    _externalTargetLanguagePatchCache.TryGetValue(targetPackageId, out ExternalTargetLanguagePatch patch))
                {
                    patchName = patch.PatchName;
                    patchPackageId = patch.PatchPackageId;
                    return true;
                }
            }

            return false;
        }

        // 這個方法負責確保 External目標語言補丁快取 已準備完成。
        // EN: This method ensures external target language patch cache is ready.
        private static void EnsureExternalTargetLanguagePatchCache(TargetLanguage targetLang)
        {
            lock (ExternalTargetLanguagePatchLock)
            {
                if (_externalTargetLanguagePatchCache != null &&
                    _externalTargetLanguagePatchCacheLang == targetLang)
                {
                    return;
                }
            }

            var cache = new Dictionary<string, ExternalTargetLanguagePatch>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (ModMetaData patchMod in ModLister.AllInstalledMods.Where(m =>
                             m != null &&
                             m.Active &&
                             IsTranslationPatchMod(m) &&
                             HasTranslationPatchTargetLanguage(m, targetLang)))
                {
                    string patchPackageId = NormalizeExternalPatchPackageId(patchMod.PackageId);

                    foreach (string targetPackageId in GetReferencedTargetPackageIds(patchMod))
                    {
                        if (string.IsNullOrWhiteSpace(targetPackageId)) continue;
                        if (string.Equals(targetPackageId, patchPackageId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (cache.ContainsKey(targetPackageId)) continue;

                        cache[targetPackageId] = new ExternalTargetLanguagePatch
                        {
                            PatchName = patchMod.Name ?? "",
                            PatchPackageId = patchMod.PackageId ?? ""
                        };
                    }
                }
            }
            catch { }

            lock (ExternalTargetLanguagePatchLock)
            {
                _externalTargetLanguagePatchCache = cache;
                _externalTargetLanguagePatchCacheLang = targetLang;
            }
        }

        // 這個方法負責取得 Referenced目標PackageIds 資料。
        // EN: This method gets referenced target package ids.
        private static bool HasTranslationPatchTargetLanguage(ModMetaData patchMod, TargetLanguage targetLang)
        {
            if (patchMod == null) return false;
            return HasTranslationPatchTargetLanguage(patchMod.PackageId, patchMod.RootDir != null ? patchMod.RootDir.FullName : "", targetLang);
        }

        public static bool HasTranslationPatchTargetLanguage(string packageId, string rootDir, TargetLanguage targetLang)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) return false;
            string targetFolder = GetFolderNameByLanguage(targetLang);
            foreach (string langRoot in GetAllTranslationPatchLangPaths(packageId, rootDir))
            {
                try
                {
                    foreach (string targetRoot in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        if (ContainsTranslationXmlFiles(targetRoot))
                        {
                            return true;
                        }
                    }
                }
                catch { }
            }

            return false;
        }

        private static IEnumerable<string> GetReferencedTargetPackageIds(ModMetaData patchMod)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (patchMod == null) return result;

            foreach (string packageId in GetReferencedTargetPackageIdsFromRoot(patchMod.RootDir != null ? patchMod.RootDir.FullName : ""))
            {
                result.Add(packageId);
            }

            return result;
        }

        public static IEnumerable<string> GetReferencedTargetPackageIdsFromRoot(string rootDir)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rootDir)) return result;

            string aboutXml = Path.Combine(rootDir, "About", "About.xml");
            if (File.Exists(aboutXml))
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(aboutXml);

                    string[] dependencyPaths =
                    {
                        "//loadAfter/li",
                        "//forceLoadAfter/li",
                        "//loadBefore/li",
                        "//forceLoadBefore/li",
                        "//modDependencies//packageId",
                        "//modDependenciesByVersion//packageId"
                    };

                    foreach (string xpath in dependencyPaths)
                    {
                        XmlNodeList nodes = doc.SelectNodes(xpath);
                        if (nodes == null) continue;

                        foreach (XmlNode node in nodes)
                        {
                            foreach (string packageId in ExtractPackageIdTokens(node.InnerText))
                            {
                                result.Add(packageId);
                            }
                        }
                    }
                }
                catch { }
            }

            string loadFoldersXml = Path.Combine(rootDir, "LoadFolders.xml");
            if (File.Exists(loadFoldersXml))
            {
                try
                {
                    XmlDocument loadDoc = new XmlDocument();
                    loadDoc.Load(loadFoldersXml);

                    XmlNodeList nodes = loadDoc.SelectNodes("//*[@IfModActive]");
                    if (nodes != null)
                    {
                        foreach (XmlNode node in nodes)
                        {
                            XmlAttribute attr = node.Attributes?["IfModActive"];
                            if (attr == null) continue;

                            foreach (string packageId in ExtractPackageIdTokens(attr.Value))
                            {
                                result.Add(packageId);
                            }
                        }
                    }
                }
                catch { }
            }

            return result;
        }

        // 這個方法負責處理 ExtractPackageIdTokens 相關流程。
        // EN: This method handles extract package id tokens.
        private static IEnumerable<string> ExtractPackageIdTokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;

            foreach (Match match in Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9][a-z0-9._-]*[a-z0-9]"))
            {
                string token = NormalizeExternalPatchPackageId(match.Value);
                if (!string.IsNullOrWhiteSpace(token)) yield return token;
            }
        }

        // 這個方法負責清理並標準化 External補丁PackageId 內容。
        // EN: This method cleans and normalizes external patch package id.
        private static string NormalizeExternalPatchPackageId(string packageId)
        {
            return (packageId ?? "").Trim().ToLowerInvariant();
        }
    }
}
