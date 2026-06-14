using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {
        private class ExternalTargetLanguagePatch
        {
            public string PatchPackageId = "";
            public string PatchName = "";
        }

        private static readonly object ExternalTargetLanguagePatchLock = new object();
        private static TargetLanguage _externalTargetLanguagePatchCacheLang;
        private static Dictionary<string, ExternalTargetLanguagePatch> _externalTargetLanguagePatchCache = null;

        public static void ClearExternalTargetLanguagePatchCache()
        {
            lock (ExternalTargetLanguagePatchLock)
            {
                _externalTargetLanguagePatchCache = null;
            }
        }

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
                             HasNativeTargetLanguage(m, targetLang)))
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

        private static IEnumerable<string> GetReferencedTargetPackageIds(ModMetaData patchMod)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (patchMod == null) return result;

            string aboutXml = Path.Combine(patchMod.RootDir.FullName, "About", "About.xml");
            if (!File.Exists(aboutXml)) return result;

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

            return result;
        }

        private static IEnumerable<string> ExtractPackageIdTokens(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;

            foreach (Match match in Regex.Matches(value.ToLowerInvariant(), @"[a-z0-9][a-z0-9._-]*[a-z0-9]"))
            {
                string token = NormalizeExternalPatchPackageId(match.Value);
                if (!string.IsNullOrWhiteSpace(token)) yield return token;
            }
        }

        private static string NormalizeExternalPatchPackageId(string packageId)
        {
            return (packageId ?? "").Trim().ToLowerInvariant();
        }
    }
}
