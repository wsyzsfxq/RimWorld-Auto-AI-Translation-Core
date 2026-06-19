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
// 這個檔案負責偵測模組是否更新或需要重新掃描。
// EN: This file detects mod updates and translation freshness.

namespace AutoTranslator_Core
{
    // 這個列舉定義 模組翻譯Status 可使用的固定選項。
    // EN: This enum defines the available mod translation status options.
    public enum ModTranslationStatus
    {
        Untranslated,
        Translated,
        PossiblyOutdated
    }

    // 這個類別負責 模組Update偵測器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ModUpdateDetector.
    public static class ModUpdateDetector
    {
        // 這個常數定義 UpdatedList快取Seconds 的固定值。
        // EN: This constant defines the fixed value for updated list cache seconds.
        private const float UpdatedListCacheSeconds = 10f;
        // 這個常數定義 TranslatedFile快取Seconds 的固定值。
        // EN: This constant defines the fixed value for translated file cache seconds.
        private const float TranslatedFileCacheSeconds = 10f;
        // 這個常數定義 SourceFingerprint快取Seconds 的固定值。
        // EN: This constant defines the fixed value for source fingerprint cache seconds.
        private const float SourceFingerprintCacheSeconds = 30f;
        // 這個常數定義 Status快取Seconds 的固定值。
        // EN: This constant defines the fixed value for status cache seconds.
        private const float StatusCacheSeconds = 5f;

        // 這個欄位保存 cached模組 的執行狀態或快取資料。
        // EN: This field stores cached mods runtime state or cached data.
        private static List<ModMetaData> _cachedMods = null;
        // 這個欄位保存 lastCheckTime 的執行狀態或快取資料。
        // EN: This field stores last check time runtime state or cached data.
        private static float _lastCheckTime = 0f;
        // 這個欄位保存 translatedFilePackageIds 的執行狀態或快取資料。
        // EN: This field stores translated file package ids runtime state or cached data.
        private static HashSet<string> _translatedFilePackageIds = null;
        // 這個欄位保存 translatedFileLatestTicksByPackageId 的執行狀態或快取資料。
        // EN: This field stores translated file latest ticks by package id runtime state or cached data.
        private static Dictionary<string, long> _translatedFileLatestTicksByPackageId = null;
        // 這個欄位保存 translatedFile快取語言 的執行狀態或快取資料。
        // EN: This field stores translated file cache language runtime state or cached data.
        private static TargetLanguage _translatedFileCacheLang;
        // 這個欄位保存 lastTranslatedFile快取Time 的執行狀態或快取資料。
        // EN: This field stores last translated file cache time runtime state or cached data.
        private static float _lastTranslatedFileCacheTime = 0f;
        // 這個欄位保存 sourceSnapshot快取 的執行狀態或快取資料。
        // EN: This field stores source snapshot cache runtime state or cached data.
        private static readonly Dictionary<string, SourceSnapshot> _sourceSnapshotCache =
            new Dictionary<string, SourceSnapshot>(StringComparer.OrdinalIgnoreCase);
        // 這個欄位保存 status快取 的執行狀態或快取資料。
        // EN: This field stores status cache runtime state or cached data.
        private static readonly Dictionary<string, StatusSnapshot> _statusCache =
            new Dictionary<string, StatusSnapshot>(StringComparer.OrdinalIgnoreCase);

        // 這個類別負責 SourceSnapshot 的主要流程與狀態。
        // EN: This class manages the main workflow and state for SourceSnapshot.
        private class SourceSnapshot
        {
            // 這個欄位保存 Fingerprint 的執行狀態或快取資料。
            // EN: This field stores fingerprint runtime state or cached data.
            public string Fingerprint = "";
            // 這個欄位保存 LatestTicks 的執行狀態或快取資料。
            // EN: This field stores latest ticks runtime state or cached data.
            public long LatestTicks = 0L;
            // 這個欄位保存 HasSourceFiles 的執行狀態或快取資料。
            // EN: This field stores has source files runtime state or cached data.
            public bool HasSourceFiles = false;
            // 這個欄位保存 HasNative目標語言 的執行狀態或快取資料。
            // EN: This field stores has native target language runtime state or cached data.
            public bool HasNativeTargetLanguage = false;
            // 這個欄位保存 HasExternal目標語言補丁 的執行狀態或快取資料。
            // EN: This field stores has external target language patch runtime state or cached data.
            public bool HasExternalTargetLanguagePatch = false;
            // 這個欄位保存 CachedAt 的執行狀態或快取資料。
            // EN: This field stores cached at runtime state or cached data.
            public float CachedAt = 0f;
        }

        // 這個類別負責 StatusSnapshot 的主要流程與狀態。
        // EN: This class manages the main workflow and state for StatusSnapshot.
        private class StatusSnapshot
        {
            // 這個欄位保存 Status 的執行狀態或快取資料。
            // EN: This field stores status runtime state or cached data.
            public ModTranslationStatus Status;
            // 這個欄位保存 CachedAt 的執行狀態或快取資料。
            // EN: This field stores cached at runtime state or cached data.
            public float CachedAt;
        }

        // 這個方法負責取得 模組快取Key 資料。
        // EN: This method gets mod cache key.
        private static string GetModCacheKey(ModMetaData mod)
        {
            return $"{AutoTranslatorMod.Settings.TargetLang}|{mod.PackageId}";
        }

        // 這個方法負責嘗試執行 GetSavedTick 並回報是否成功。
        // EN: This method tries to get saved tick and reports whether it succeeded.
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

        // 這個方法負責嘗試執行 GetSavedFingerprint 並回報是否成功。
        // EN: This method tries to get saved fingerprint and reports whether it succeeded.
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

        // 這個方法負責清理並標準化 PackageForComparison 內容。
        // EN: This method cleans and normalizes package for comparison.
        private static string NormalizePackageForComparison(string packageId)
        {
            return (packageId ?? "").Trim().ToLowerInvariant().Replace(".", "_");
        }

        // 這個方法負責處理 ExtractPackagePrefixFrom翻譯File 相關流程。
        // EN: This method handles extract package prefix from translation file.
        private static string ExtractPackagePrefixFromTranslationFile(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file) ?? "";
            int splitIdx = name.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
            if (splitIdx == -1) splitIdx = name.LastIndexOf('_');
            if (splitIdx > 0) name = name.Substring(0, splitIdx);
            return NormalizePackageForComparison(name);
        }

        // 這個方法負責取得 TranslatedFilePackageIdsCached 資料。
        // EN: This method gets translated file package ids cached.
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
                        RememberTranslationFile(result, latestTicks, MatchPackagePrefixFromTranslationFile(file, knownPackageIds), file);
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

        // 這個方法負責處理 Remember翻譯File 相關流程。
        // EN: This method handles remember translation file.
        private static void RememberTranslationFile(HashSet<string> result, Dictionary<string, long> latestTicks, string packagePrefix, string file)
        {
            if (string.IsNullOrEmpty(packagePrefix)) return;

            result.Add(packagePrefix);

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

        // 這個方法負責處理 MatchPackagePrefixFrom翻譯File 相關流程。
        // EN: This method handles match package prefix from translation file.
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

        // 這個方法負責清除 Status快取 資料。
        // EN: This method clears status cache.
        public static void ClearStatusCache()
        {
            _cachedMods = null;
            _translatedFilePackageIds = null;
            _translatedFileLatestTicksByPackageId = null;
            _sourceSnapshotCache.Clear();
            _statusCache.Clear();
            AutoTranslatorScanner.ClearExternalTargetLanguagePatchCache();
        }

        // 這個方法負責判斷 HasLocal翻譯Files 條件是否成立。
        // EN: This method checks has local translation files.
        public static bool HasLocalTranslationFiles(ModMetaData mod)
        {
            if (mod == null) return false;
            return GetTranslatedFilePackageIdsCached().Contains(NormalizePackageForComparison(mod.PackageId));
        }

        // 這個方法負責嘗試執行 GetLocal翻譯LatestTicks 並回報是否成功。
        // EN: This method tries to get local translation latest ticks and reports whether it succeeded.
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

        // 這個方法負責取得 SourceSnapshot 資料。
        // EN: This method gets source snapshot.
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

        // 這個方法負責建立 SourceSnapshot 所需資料。
        // EN: This method builds source snapshot.
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

        // 這個方法負責處理 AddIfExists 相關流程。
        // EN: This method handles add if exists.
        private static void AddIfExists(ISet<string> files, string path)
        {
            if (File.Exists(path)) files.Add(Path.GetFullPath(path));
        }

        // 這個方法負責處理 AddXmlFiles 相關流程。
        // EN: This method handles add XML files.
        private static void AddXmlFiles(ISet<string> files, string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories))
            {
                files.Add(Path.GetFullPath(file));
            }
        }

        // 這個方法負責判斷 HasAnyXmlFile 條件是否成立。
        // EN: This method checks has any XML file.
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

        // 這個方法負責處理 MakeRelative路徑 相關流程。
        // EN: This method handles make relative path.
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

        // 這個方法負責處理 ComputeStableHash 相關流程。
        // EN: This method handles compute stable hash.
        private static string ComputeStableHash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }

        // 這個方法負責取得 翻譯Status 資料。
        // EN: This method gets translation status.
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

        // 這個方法負責取得 翻譯StatusUncached 資料。
        // EN: This method gets translation status uncached.
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

        // 這個方法負責取得 翻譯StatusLabelKey 資料。
        // EN: This method gets translation status label key.
        public static string GetTranslationStatusLabelKey(ModTranslationStatus status)
        {
            switch (status)
            {
                case ModTranslationStatus.Translated: return "ATC_ModStatus_Translated";
                case ModTranslationStatus.PossiblyOutdated: return "ATC_ModStatus_Outdated";
                default: return "ATC_ModStatus_Untranslated";
            }
        }

        // 這個方法負責取得 翻譯StatusColorHex 資料。
        // EN: This method gets translation status color hex.
        public static string GetTranslationStatusColorHex(ModTranslationStatus status)
        {
            switch (status)
            {
                case ModTranslationStatus.Translated: return "#76D66A";
                case ModTranslationStatus.PossiblyOutdated: return "#E6C35C";
                default: return "#9A9A9A";
            }
        }

        // 這個方法負責取得 UpdatedOrNew模組Cached 資料。
        // EN: This method gets updated or new mods cached.
        public static List<ModMetaData> GetUpdatedOrNewModsCached()
        {
            if (_cachedMods == null || Time.realtimeSinceStartup - _lastCheckTime > UpdatedListCacheSeconds)
            {
                _cachedMods = GetUpdatedOrNewModsForce();
                _lastCheckTime = Time.realtimeSinceStartup;
            }
            return _cachedMods;
        }

        // 這個方法負責取得 UpdatedOrNew模組Force 資料。
        // EN: This method gets updated or new mods force.
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

        // 這個方法負責標記 模組AsTranslated 狀態。
        // EN: This method marks mod as translated.
        public static void MarkModAsTranslated(string packageId, string rootDir, bool refreshStatusCache = true)
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

            if (refreshStatusCache)
            {
                ClearStatusCache();
            }
        }
    }
}
