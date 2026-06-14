using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace AutoTranslator_Core
{
    internal static class ModNameTranslationCache
    {
        private const string CacheFileName = "ModNameTranslations.json";
        private static readonly TimeSpan QueueInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FailedRetryInterval = TimeSpan.FromSeconds(30);
        private static readonly object CacheLock = new object();
        private static readonly HashSet<string> QueuedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, long> FailedRetryAfterTicks = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static long nextQueueAllowedTicks = 0;
        private static string lastVisibleQueueKey = "";
        private static bool loaded = false;
        private static bool dirty = false;

        private class CacheEntry
        {
            public string TargetLanguage;
            public string PackageId;
            public string SourceName;
            public string TranslatedName;
            public string UpdatedUtc;
        }

        public static bool TryGet(ModMetaData mod, out string translated)
        {
            translated = "";
            if (!IsValidMod(mod)) return false;

            EnsureLoaded();
            string key = BuildCacheKey(mod);
            lock (CacheLock)
            {
                if (!entries.TryGetValue(key, out CacheEntry entry)) return false;
                if (!MatchesCurrentMod(entry, mod)) return false;
                if (string.IsNullOrWhiteSpace(entry.TranslatedName)) return false;

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

        public static void Store(ModMetaData mod, string translated)
        {
            if (!IsValidMod(mod)) return;
            if (string.IsNullOrWhiteSpace(translated)) return;

            translated = LanguageDetector.NormalizeChineseVariant(translated, AutoTranslatorMod.Settings.TargetLang);
            EnsureLoaded();
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

        public static void Clear()
        {
            lock (CacheLock)
            {
                entries.Clear();
                QueuedKeys.Clear();
                FailedRetryAfterTicks.Clear();
                dirty = false;
                loaded = true;
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

        public static void SaveIfDirty()
        {
            Dictionary<string, CacheEntry> snapshot;
            lock (CacheLock)
            {
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

        private static void EnsureLoaded()
        {
            lock (CacheLock)
            {
                if (loaded) return;
                loaded = true;

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

        private static bool MatchesCurrentMod(CacheEntry entry, ModMetaData mod)
        {
            return entry != null &&
                   string.Equals(entry.TargetLanguage, AutoTranslatorMod.Settings.TargetLang.ToString(), StringComparison.Ordinal) &&
                   string.Equals(entry.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(entry.SourceName, mod.Name, StringComparison.Ordinal);
        }

        private static bool IsValidMod(ModMetaData mod)
        {
            return mod != null &&
                   AutoTranslatorMod.Settings != null &&
                   !string.IsNullOrWhiteSpace(mod.PackageId) &&
                   !string.IsNullOrWhiteSpace(mod.Name);
        }

        private static string BuildCacheKey(ModMetaData mod)
        {
            string raw = $"{AutoTranslatorMod.Settings.TargetLang}\n{mod.PackageId.ToLowerInvariant()}\n{mod.Name}";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return BitConverter.ToString(hash).Replace("-", "");
            }
        }

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

        private static string GetCacheFilePath()
        {
            return Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Cache", CacheFileName);
        }
    }
}
