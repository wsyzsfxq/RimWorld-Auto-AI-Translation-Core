using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public enum ModTranslationStatus
    {
        Untranslated,
        Translated,
        PossiblyOutdated
    }

    public static class ModUpdateDetector
    {
        private const float UpdatedListCacheSeconds = 10f;
        private const float TranslatedFileCacheSeconds = 10f;
        private const float SourceFingerprintCacheSeconds = 30f;
        private const float StatusCacheSeconds = 5f;

        private static List<ModMetaData> _cachedMods = null;
        private static float _lastCheckTime = 0f;
        private static HashSet<string> _translatedFilePackageIds = null;
        private static Dictionary<string, long> _translatedFileLatestTicksByPackageId = null;
        private static TargetLanguage _translatedFileCacheLang;
        private static float _lastTranslatedFileCacheTime = 0f;
        private static readonly Dictionary<string, SourceSnapshot> _sourceSnapshotCache =
            new Dictionary<string, SourceSnapshot>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, StatusSnapshot> _statusCache =
            new Dictionary<string, StatusSnapshot>(StringComparer.OrdinalIgnoreCase);

        private class SourceSnapshot
        {
            public string Fingerprint = "";
            public long LatestTicks = 0L;
            public bool HasSourceFiles = false;
            public bool HasNativeTargetLanguage = false;
            public bool HasExternalTargetLanguagePatch = false;
            public float CachedAt = 0f;
        }

        private class StatusSnapshot
        {
            public ModTranslationStatus Status;
            public float CachedAt;
        }

        private static string GetModCacheKey(ModMetaData mod)
        {
            return $"{AutoTranslatorMod.Settings.TargetLang}|{mod.PackageId}";
        }

        private static bool TryGetSavedTick(string packageId, out long savedTicks)
        {
            savedTicks = 0L;
            var dict = AutoTranslatorMod.Settings.ModLastVerifiedTimes;
            if (dict == null || string.IsNullOrEmpty(packageId)) return false;
            if (dict.TryGetValue(packageId, out savedTicks)) return true;

            foreach (var kv in dict)
            {
                if (string.Equals(kv.Key, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    savedTicks = kv.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSavedFingerprint(string packageId, out string savedFingerprint)
        {
            savedFingerprint = null;
            var dict = AutoTranslatorMod.Settings.ModLastVerifiedFingerprints;
            if (dict == null || string.IsNullOrEmpty(packageId)) return false;
            if (dict.TryGetValue(packageId, out savedFingerprint)) return !string.IsNullOrEmpty(savedFingerprint);

            foreach (var kv in dict)
            {
                if (string.Equals(kv.Key, packageId, StringComparison.OrdinalIgnoreCase))
                {
                    savedFingerprint = kv.Value;
                    return !string.IsNullOrEmpty(savedFingerprint);
                }
            }

            return false;
        }

        private static string NormalizePackageForComparison(string packageId)
        {
            return (packageId ?? "").Trim().ToLowerInvariant().Replace(".", "_");
        }

        private static string ExtractPackagePrefixFromTranslationFile(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file) ?? "";
            int splitIdx = name.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
            if (splitIdx == -1) splitIdx = name.LastIndexOf('_');
            if (splitIdx > 0) name = name.Substring(0, splitIdx);
            return NormalizePackageForComparison(name);
        }

        private static HashSet<string> GetTranslatedFilePackageIdsCached()
        {
            TargetLanguage targetLang = AutoTranslatorMod.Settings.TargetLang;
            if (_translatedFilePackageIds != null &&
                _translatedFileCacheLang == targetLang &&
                Time.realtimeSinceStartup - _lastTranslatedFileCacheTime <= TranslatedFileCacheSeconds)
            {
                return _translatedFilePackageIds;
            }

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var latestTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(targetLang);
                string langRoot = Path.Combine(packPath, "Languages", targetLangFolder);
                if (Directory.Exists(langRoot))
                {
                    List<string> knownPackageIds = ModLister.AllInstalledMods
                        .Where(m => !string.IsNullOrEmpty(m.PackageId))
                        .Select(m => NormalizePackageForComparison(m.PackageId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(id => id.Length)
                        .ToList();

                    foreach (var file in Directory.GetFiles(langRoot, "*.xml", SearchOption.AllDirectories))
                    {
                        string packagePrefix = MatchPackagePrefixFromTranslationFile(file, knownPackageIds);
                        if (!string.IsNullOrEmpty(packagePrefix)) result.Add(packagePrefix);
                        if (!string.IsNullOrEmpty(packagePrefix))
                        {
                            long ticks = 0L;
                            try
                            {
                                ticks = new FileInfo(file).LastWriteTimeUtc.Ticks;
                            }
                            catch { }

                            if (!latestTicks.TryGetValue(packagePrefix, out long existingTicks) || ticks > existingTicks)
                            {
                                latestTicks[packagePrefix] = ticks;
                            }
                        }
                    }
                }
            }
            catch { }

            _translatedFilePackageIds = result;
            _translatedFileLatestTicksByPackageId = latestTicks;
            _translatedFileCacheLang = targetLang;
            _lastTranslatedFileCacheTime = Time.realtimeSinceStartup;
            return _translatedFilePackageIds;
        }

        private static string MatchPackagePrefixFromTranslationFile(string file, List<string> knownPackageIds)
        {
            string name = NormalizePackageForComparison(Path.GetFileNameWithoutExtension(file) ?? "");
            if (!string.IsNullOrEmpty(name) && knownPackageIds != null)
            {
                foreach (string packageId in knownPackageIds)
                {
                    if (string.IsNullOrEmpty(packageId)) continue;
                    if (name.Equals(packageId, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(packageId + "_", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(packageId + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        return packageId;
                    }
                }
            }

            return ExtractPackagePrefixFromTranslationFile(file);
        }

        public static void ClearStatusCache()
        {
            _cachedMods = null;
            _translatedFilePackageIds = null;
            _translatedFileLatestTicksByPackageId = null;
            _sourceSnapshotCache.Clear();
            _statusCache.Clear();
            AutoTranslatorScanner.ClearExternalTargetLanguagePatchCache();
        }

        public static bool HasLocalTranslationFiles(ModMetaData mod)
        {
            if (mod == null) return false;
            return GetTranslatedFilePackageIdsCached().Contains(NormalizePackageForComparison(mod.PackageId));
        }

        private static bool TryGetLocalTranslationLatestTicks(ModMetaData mod, out long latestTicks)
        {
            latestTicks = 0L;
            if (mod == null) return false;

            GetTranslatedFilePackageIdsCached();
            if (_translatedFileLatestTicksByPackageId == null) return false;

            return _translatedFileLatestTicksByPackageId.TryGetValue(
                NormalizePackageForComparison(mod.PackageId),
                out latestTicks);
        }

        private static SourceSnapshot GetSourceSnapshot(ModMetaData mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId)) return new SourceSnapshot();

            string cacheKey = GetModCacheKey(mod);
            if (_sourceSnapshotCache.TryGetValue(cacheKey, out SourceSnapshot cached) &&
                Time.realtimeSinceStartup - cached.CachedAt <= SourceFingerprintCacheSeconds)
            {
                return cached;
            }

            SourceSnapshot snapshot = BuildSourceSnapshot(mod);
            snapshot.CachedAt = Time.realtimeSinceStartup;
            _sourceSnapshotCache[cacheKey] = snapshot;
            return snapshot;
        }

        private static SourceSnapshot BuildSourceSnapshot(ModMetaData mod)
        {
            var snapshot = new SourceSnapshot();
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                AddIfExists(files, Path.Combine(mod.RootDir.FullName, "About", "About.xml"));
                AddIfExists(files, Path.Combine(mod.RootDir.FullName, "LoadFolders.xml"));

                foreach (string langRoot in AutoTranslatorScanner.GetAllEffectiveLangPaths(mod))
                {
                    AddXmlFiles(files, Path.Combine(langRoot, "English", "Keyed"));
                    AddXmlFiles(files, Path.Combine(langRoot, "English", "keyed"));
                    AddXmlFiles(files, Path.Combine(langRoot, "English", "DefInjected"));
                    AddXmlFiles(files, Path.Combine(langRoot, "English", "defInjected"));
                    if (!snapshot.HasNativeTargetLanguage)
                    {
                        snapshot.HasNativeTargetLanguage = AutoTranslatorScanner.HasNativeTargetLanguage(mod, AutoTranslatorMod.Settings.TargetLang);
                    }
                }

                snapshot.HasExternalTargetLanguagePatch =
                    AutoTranslatorScanner.HasActiveExternalTargetLanguagePatch(mod, AutoTranslatorMod.Settings.TargetLang);

                foreach (string defsRoot in AutoTranslatorScanner.GetAllEffectiveDefsPaths(mod))
                {
                    AddXmlFiles(files, defsRoot);
                }
            }
            catch { }

            snapshot.HasSourceFiles = files.Count > 0;
            if (files.Count == 0)
            {
                snapshot.Fingerprint = "no-source";
                return snapshot;
            }

            var builder = new StringBuilder();
            foreach (string file in files)
            {
                try
                {
                    FileInfo info = new FileInfo(file);
                    if (!info.Exists) continue;

                    snapshot.LatestTicks = Math.Max(snapshot.LatestTicks, info.LastWriteTimeUtc.Ticks);
                    string relative = MakeRelativePath(mod.RootDir.FullName, info.FullName);
                    builder.Append(relative.ToLowerInvariant())
                        .Append('|')
                        .Append(info.Length)
                        .Append('|')
                        .Append(info.LastWriteTimeUtc.Ticks)
                        .Append('\n');
                }
                catch { }
            }

            snapshot.Fingerprint = ComputeStableHash(builder.ToString());
            return snapshot;
        }

        private static void AddIfExists(ISet<string> files, string path)
        {
            if (File.Exists(path)) files.Add(Path.GetFullPath(path));
        }

        private static void AddXmlFiles(ISet<string> files, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        private static bool HasAnyXmlFile(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            try
            {
                return Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string MakeRelativePath(string root, string fullPath)
        {
            try
            {
                string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedPath = Path.GetFullPath(fullPath);
                if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedPath.Substring(normalizedRoot.Length);
                }
            }
            catch { }

            return Path.GetFileName(fullPath) ?? fullPath;
        }

        private static string ComputeStableHash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        public static ModTranslationStatus GetTranslationStatus(ModMetaData mod)
        {
            if (mod == null) return ModTranslationStatus.Untranslated;

            string cacheKey = GetModCacheKey(mod);
            if (_statusCache.TryGetValue(cacheKey, out StatusSnapshot cached) &&
                Time.realtimeSinceStartup - cached.CachedAt <= StatusCacheSeconds)
            {
                return cached.Status;
            }

            ModTranslationStatus status = GetTranslationStatusUncached(mod);
            _statusCache[cacheKey] = new StatusSnapshot
            {
                Status = status,
                CachedAt = Time.realtimeSinceStartup
            };
            return status;
        }

        private static ModTranslationStatus GetTranslationStatusUncached(ModMetaData mod)
        {
            SourceSnapshot source = GetSourceSnapshot(mod);
            bool hasLocalFiles = HasLocalTranslationFiles(mod);

            if (source.HasNativeTargetLanguage || source.HasExternalTargetLanguagePatch)
            {
                return ModTranslationStatus.Translated;
            }

            if (TryGetSavedFingerprint(mod.PackageId, out string savedFingerprint))
            {
                return string.Equals(source.Fingerprint, savedFingerprint, StringComparison.Ordinal)
                    ? ModTranslationStatus.Translated
                    : ModTranslationStatus.PossiblyOutdated;
            }

            if (TryGetSavedTick(mod.PackageId, out long savedTicks))
            {
                return source.LatestTicks > savedTicks
                    ? ModTranslationStatus.PossiblyOutdated
                    : ModTranslationStatus.Translated;
            }

            if (hasLocalFiles)
            {
                if (!source.HasSourceFiles ||
                    source.LatestTicks <= 0L ||
                    (TryGetLocalTranslationLatestTicks(mod, out long localTicks) && localTicks >= source.LatestTicks))
                {
                    return ModTranslationStatus.Translated;
                }

                return ModTranslationStatus.PossiblyOutdated;
            }

            return ModTranslationStatus.Untranslated;
        }

        public static string GetTranslationStatusLabelKey(ModTranslationStatus status)
        {
            switch (status)
            {
                case ModTranslationStatus.Translated: return "ATC_ModStatus_Translated";
                case ModTranslationStatus.PossiblyOutdated: return "ATC_ModStatus_Outdated";
                default: return "ATC_ModStatus_Untranslated";
            }
        }

        public static string GetTranslationStatusColorHex(ModTranslationStatus status)
        {
            switch (status)
            {
                case ModTranslationStatus.Translated: return "#76D66A";
                case ModTranslationStatus.PossiblyOutdated: return "#E6C35C";
                default: return "#9A9A9A";
            }
        }

        public static List<ModMetaData> GetUpdatedOrNewModsCached()
        {
            if (_cachedMods == null || Time.realtimeSinceStartup - _lastCheckTime > UpdatedListCacheSeconds)
            {
                _cachedMods = GetUpdatedOrNewModsForce();
                _lastCheckTime = Time.realtimeSinceStartup;
            }
            return _cachedMods;
        }

        private static List<ModMetaData> GetUpdatedOrNewModsForce()
        {
            var result = new List<ModMetaData>();
            AutoTranslatorSettings.FilteredModsCount = 0;

            foreach (var mod in ModLister.AllInstalledMods.Where(m => m.Active))
            {
                string pid = (mod.PackageId ?? "").ToLowerInvariant();

                if (pid == "auto.aitranslation.core" ||
                    pid == "aitranslation.pack" ||
                    pid.StartsWith("ludeon.rimworld") ||
                    AutoTranslatorScanner.IsTranslationPatchMod(mod))
                {
                    continue;
                }

                if (IsCodeOnlyMod(mod))
                {
                    AutoTranslatorSettings.FilteredModsCount++;
                    continue;
                }

                if (GetTranslationStatus(mod) != ModTranslationStatus.Translated)
                {
                    result.Add(mod);
                }
            }
            return result;
        }

        public static void MarkModAsTranslated(string packageId, string rootDir)
        {
            var meta = ModLister.AllInstalledMods.FirstOrDefault(m =>
                string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
            if (meta == null) return;

            SourceSnapshot source = BuildSourceSnapshot(meta);
            if (AutoTranslatorMod.Settings.ModLastVerifiedTimes == null)
            {
                AutoTranslatorMod.Settings.ModLastVerifiedTimes = new Dictionary<string, long>();
            }
            if (AutoTranslatorMod.Settings.ModLastVerifiedFingerprints == null)
            {
                AutoTranslatorMod.Settings.ModLastVerifiedFingerprints = new Dictionary<string, string>();
            }

            AutoTranslatorMod.Settings.ModLastVerifiedTimes[packageId] = source.LatestTicks;
            AutoTranslatorMod.Settings.ModLastVerifiedFingerprints[packageId] = source.Fingerprint;
            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

            ClearStatusCache();
        }
    }
}
