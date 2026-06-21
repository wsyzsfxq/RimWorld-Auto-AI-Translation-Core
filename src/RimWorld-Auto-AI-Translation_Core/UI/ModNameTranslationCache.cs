using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Verse;
// 這個檔案負責 模組名稱翻譯快取 相關邏輯，支援 Auto Translation Core 的執行流程。
// EN: This file contains mod name translation cache support code.

namespace AutoTranslator_Core
{
    // 這個類別負責 模組名稱翻譯快取 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ModNameTranslationCache.
    internal static class ModNameTranslationCache
    {
        // 這個常數定義 快取File名稱 的固定值。
        // EN: This constant defines the fixed value for cache file name.
        private const string CacheFileName = "ModNameTranslations.json";
        private static readonly TimeSpan QueueInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FailedRetryInterval = TimeSpan.FromSeconds(30);
        private static readonly object CacheLock = new object();
        private static readonly HashSet<string> QueuedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> FailedRetryAfterTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        // 這個欄位保存 next佇列AllowedTicks 的執行狀態或快取資料。
        // EN: This field stores next queue allowed ticks runtime state or cached data.
        private static long nextQueueAllowedTicks = 0;
        // 這個欄位保存 lastVisible佇列Key 的執行狀態或快取資料。
        // EN: This field stores last visible queue key runtime state or cached data.
        private static string lastVisibleQueueKey = "";
        // 這個欄位保存 loaded 的執行狀態或快取資料。
        // EN: This field stores loaded runtime state or cached data.
        private static bool loaded = false;
        private static bool loadInProgress = false;
        // 這個欄位保存 dirty 的執行狀態或快取資料。
        // EN: This field stores dirty runtime state or cached data.
        private static bool dirty = false;

        // 這個類別負責 快取Entry 的主要流程與狀態。
        // EN: This class manages the main workflow and state for CacheEntry.
        private class CacheEntry
        {
            // 這個欄位保存 目標語言 的執行狀態或快取資料。
            // EN: This field stores target language runtime state or cached data.
            public string TargetLanguage;
            // 這個欄位保存 PackageId 的執行狀態或快取資料。
            // EN: This field stores package id runtime state or cached data.
            public string PackageId;
            // 這個欄位保存 Source名稱 的執行狀態或快取資料。
            // EN: This field stores source name runtime state or cached data.
            public string SourceName;
            // 這個欄位保存 Translated名稱 的執行狀態或快取資料。
            // EN: This field stores translated name runtime state or cached data.
            public string TranslatedName;
            // 這個欄位保存 UpdatedUtc 的執行狀態或快取資料。
            // EN: This field stores updated UTC runtime state or cached data.
            public string UpdatedUtc;
        }

        // 這個方法負責嘗試執行 Get 並回報是否成功。
        // EN: This method tries to get and reports whether it succeeded.
        public static bool TryGet(ModMetaData mod, out string translated)
        {
            translated = "";
            if (!IsValidMod(mod)) return false;

            EnsureLoadStarted();
            lock (CacheLock)
            {
                if (!loaded) return false;
            }

            string key = BuildCacheKey(mod);
            lock (CacheLock)
            {
                if (!entries.TryGetValue(key, out CacheEntry entry)) return false;
                if (!MatchesCurrentMod(entry, mod)) return false;
                if (string.IsNullOrWhiteSpace(entry.TranslatedName)) return false;
                if (LanguageDetector.LooksLikePlaceholderTranslation(entry.TranslatedName, AutoTranslatorMod.Settings.TargetLang))
                {
                    entries.Remove(key);
                    dirty = true;
                    return false;
                }

                translated = LanguageDetector.NormalizeChineseVariant(entry.TranslatedName, AutoTranslatorMod.Settings.TargetLang);
                if (!string.Equals(translated, entry.TranslatedName, StringComparison.Ordinal))
                {
                    entry.TranslatedName = translated;
                    entry.UpdatedUtc = DateTime.UtcNow.ToString("o");
                    dirty = true;
                }
                return true;
            }
        }

        // 這個方法負責嘗試執行 MarkQueued 並回報是否成功。
        // EN: This method tries to mark queued and reports whether it succeeded.
        public static bool TryMarkQueued(ModMetaData mod)
        {
            if (!IsValidMod(mod)) return false;
            if (TryGet(mod, out _)) return false;

            string key = BuildCacheKey(mod);
            lock (CacheLock)
            {
                if (FailedRetryAfterTicks.TryGetValue(key, out long retryAfterTicks))
                {
                    if (DateTime.UtcNow.Ticks < retryAfterTicks) return false;
                    FailedRetryAfterTicks.Remove(key);
                }

                return QueuedKeys.Add(key);
            }
        }

        // 這個方法負責嘗試執行 BeginVisible佇列 並回報是否成功。
        // EN: This method tries to begin visible queue and reports whether it succeeded.
        public static bool TryBeginVisibleQueue(IEnumerable<ModMetaData> visibleMods)
        {
            string visibleKey = BuildVisibleKey(visibleMods);
            if (string.IsNullOrEmpty(visibleKey)) return false;

            long nowTicks = DateTime.UtcNow.Ticks;
            lock (CacheLock)
            {
                if (string.Equals(visibleKey, lastVisibleQueueKey, StringComparison.Ordinal) &&
                    nowTicks < nextQueueAllowedTicks)
                {
                    return false;
                }

                lastVisibleQueueKey = visibleKey;
                nextQueueAllowedTicks = nowTicks + QueueInterval.Ticks;
                return true;
            }
        }

        public static bool TryBeginVisibleQueue(IList<ModMetaData> mods, int firstVisible, int lastVisible)
        {
            string visibleKey = BuildVisibleKey(mods, firstVisible, lastVisible);
            if (string.IsNullOrEmpty(visibleKey)) return false;

            long nowTicks = DateTime.UtcNow.Ticks;
            lock (CacheLock)
            {
                if (string.Equals(visibleKey, lastVisibleQueueKey, StringComparison.Ordinal) &&
                    nowTicks < nextQueueAllowedTicks)
                {
                    return false;
                }

                lastVisibleQueueKey = visibleKey;
                nextQueueAllowedTicks = nowTicks + QueueInterval.Ticks;
                return true;
            }
        }

        // 這個方法負責標記 Failed 狀態。
        // EN: This method marks failed.
        public static void MarkFailed(IEnumerable<ModMetaData> mods)
        {
            if (mods == null) return;

            long retryAfterTicks = DateTime.UtcNow.Add(FailedRetryInterval).Ticks;
            lock (CacheLock)
            {
                foreach (var mod in mods)
                {
                    if (!IsValidMod(mod)) continue;
                    FailedRetryAfterTicks[BuildCacheKey(mod)] = retryAfterTicks;
                }
            }
        }

        // 這個方法負責處理 ReleaseQueued 相關流程。
        // EN: This method handles release queued.
        public static void ReleaseQueued(IEnumerable<ModMetaData> mods)
        {
            if (mods == null) return;

            lock (CacheLock)
            {
                foreach (var mod in mods)
                {
                    if (!IsValidMod(mod)) continue;
                    QueuedKeys.Remove(BuildCacheKey(mod));
                }
            }
        }

        // 這個方法負責處理 Store 相關流程。
        // EN: This method handles store.
        public static void Store(ModMetaData mod, string translated)
        {
            if (!IsValidMod(mod)) return;
            if (string.IsNullOrWhiteSpace(translated)) return;
            if (LanguageDetector.LooksLikePlaceholderTranslation(translated, AutoTranslatorMod.Settings.TargetLang)) return;

            translated = LanguageDetector.NormalizeChineseVariant(translated, AutoTranslatorMod.Settings.TargetLang);
            EnsureLoadStarted();
            string key = BuildCacheKey(mod);
            lock (CacheLock)
            {
                entries[key] = new CacheEntry
                {
                    TargetLanguage = AutoTranslatorMod.Settings.TargetLang.ToString(),
                    PackageId = mod.PackageId,
                    SourceName = mod.Name,
                    TranslatedName = translated.Trim(),
                    UpdatedUtc = DateTime.UtcNow.ToString("o")
                };
                dirty = true;
            }
        }

        // 這個方法負責清除 這段邏輯 資料。
        // EN: This method clears .
        public static void Clear()
        {
            lock (CacheLock)
            {
                entries.Clear();
                QueuedKeys.Clear();
                FailedRetryAfterTicks.Clear();
                dirty = false;
                loaded = true;
                loadInProgress = false;
            }

            try
            {
                string path = GetCacheFilePath();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Mod-name translation cache clear failed: {ex.Message}");
            }
        }

        // 這個方法負責保存 IfDirty 資料。
        // EN: This method saves if dirty.
        public static void SaveIfDirty()
        {
            Dictionary<string, CacheEntry> snapshot;
            lock (CacheLock)
            {
                if (!loaded)
                {
                    if (!loadInProgress) EnsureLoadStarted();
                    return;
                }

                if (!dirty) return;
                snapshot = new Dictionary<string, CacheEntry>(entries, StringComparer.OrdinalIgnoreCase);
                dirty = false;
            }

            try
            {
                string path = GetCacheFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            }
            catch (Exception ex)
            {
                lock (CacheLock)
                {
                    dirty = true;
                }
                Log.Warning($"[AutoTranslationCore] Mod-name translation cache save failed: {ex.Message}");
            }
        }

        // 這個方法負責確保 Loaded 已準備完成。
        // EN: This method ensures loaded is ready.
        public static void PreloadAsync()
        {
            EnsureLoadStarted();
        }

        private static void EnsureLoadStarted()
        {
            string path;
            lock (CacheLock)
            {
                if (loaded || loadInProgress) return;
                loadInProgress = true;
                path = GetCacheFilePath();
            }

            Task.Run(() =>
            {
                Dictionary<string, CacheEntry> loadedEntries = null;
                string error = null;
                try
                {
                    if (File.Exists(path))
                    {
                        loadedEntries = JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(File.ReadAllText(path));
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                lock (CacheLock)
                {
                    bool keepDirty = dirty;
                    if (loadedEntries != null)
                    {
                        Dictionary<string, CacheEntry> merged = new Dictionary<string, CacheEntry>(loadedEntries, StringComparer.OrdinalIgnoreCase);
                        foreach (var pair in entries)
                        {
                            merged[pair.Key] = pair.Value;
                        }
                        entries = merged;
                    }
                    else if (entries == null)
                    {
                        entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
                    }

                    dirty = keepDirty;
                    loaded = true;
                    loadInProgress = false;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    Log.Warning($"[AutoTranslationCore] Mod-name translation cache load failed: {error}");
                }
            });
        }

        private static void EnsureLoaded()
        {
            lock (CacheLock)
            {
                if (loaded) return;
                loaded = true;
                loadInProgress = false;

                try
                {
                    string path = GetCacheFilePath();
                    if (!File.Exists(path)) return;

                    var loadedEntries = JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(File.ReadAllText(path));
                    if (loadedEntries != null)
                    {
                        entries = loadedEntries;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoTranslationCore] Mod-name translation cache load failed: {ex.Message}");
                    entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        // 這個方法負責處理 MatchesCurrent模組 相關流程。
        // EN: This method handles matches current mod.
        private static bool MatchesCurrentMod(CacheEntry entry, ModMetaData mod)
        {
            return entry != null &&
                   string.Equals(entry.TargetLanguage, AutoTranslatorMod.Settings.TargetLang.ToString(), StringComparison.Ordinal) &&
                   string.Equals(entry.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(entry.SourceName, mod.Name, StringComparison.Ordinal);
        }

        // 這個方法負責判斷 IsValid模組 條件是否成立。
        // EN: This method checks is valid mod.
        private static bool IsValidMod(ModMetaData mod)
        {
            return mod != null &&
                   AutoTranslatorMod.Settings != null &&
                   !string.IsNullOrWhiteSpace(mod.PackageId) &&
                   !string.IsNullOrWhiteSpace(mod.Name);
        }

        // 這個方法負責建立 快取Key 所需資料。
        // EN: This method builds cache key.
        private static string BuildCacheKey(ModMetaData mod)
        {
            string raw = $"{AutoTranslatorMod.Settings.TargetLang}\n{mod.PackageId.ToLowerInvariant()}\n{mod.Name}";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

        // 這個方法負責建立 VisibleKey 所需資料。
        // EN: This method builds visible key.
        private static string BuildVisibleKey(IEnumerable<ModMetaData> visibleMods)
        {
            if (visibleMods == null) return "";

            StringBuilder builder = new StringBuilder();
            foreach (var mod in visibleMods)
            {
                if (!IsValidMod(mod)) continue;
                if (builder.Length > 0) builder.Append('|');
                builder.Append(mod.PackageId.ToLowerInvariant());
                builder.Append(':');
                builder.Append(mod.Name);
            }

            return builder.ToString();
        }

        private static string BuildVisibleKey(IList<ModMetaData> mods, int firstVisible, int lastVisible)
        {
            if (mods == null || mods.Count == 0) return "";

            StringBuilder builder = new StringBuilder();
            int first = Math.Max(0, firstVisible);
            int last = Math.Min(mods.Count - 1, lastVisible);
            for (int i = first; i <= last; i++)
            {
                var mod = mods[i];
                if (!IsValidMod(mod)) continue;
                if (builder.Length > 0) builder.Append('|');
                builder.Append(mod.PackageId.ToLowerInvariant());
                builder.Append(':');
                builder.Append(mod.Name);
            }

            return builder.ToString();
        }

        // 這個方法負責取得 快取File路徑 資料。
        // EN: This method gets cache file path.
        private static string GetCacheFilePath()
        {
            return Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Cache", CacheFileName);
        }
    }
}
