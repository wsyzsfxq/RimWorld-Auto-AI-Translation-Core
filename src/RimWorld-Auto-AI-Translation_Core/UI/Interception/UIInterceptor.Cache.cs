using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class UIInterceptor
    {
        private static void LoadCache()
        {
            if (!EnsureCacheFileReadable(CacheFilePath)) return;

            try
            {
                if (File.Exists(CacheFilePath))
                {
                    string json = File.ReadAllText(CacheFilePath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        bool droppedUnsafeEntries = false;
                        foreach (var kvp in loaded)
                        {
                            if (!TryGetOriginalTextFromCacheKey(kvp.Key, out string original, out TargetLanguage? keyLanguage))
                            {
                                droppedUnsafeEntries = true;
                                continue;
                            }

                            if (keyLanguage.HasValue && keyLanguage.Value != AutoTranslatorMod.Settings.TargetLang)
                            {
                                continue;
                            }

                            string cleanOriginal = GetTranslationLookupText(original);
                            string cleanStoredValue = GetTranslationLookupText(kvp.Value);
                            if (!ShouldLoadCachedText(original) || ShouldSkipUITranslationText(cleanStoredValue))
                            {
                                if (!string.IsNullOrWhiteSpace(cleanOriginal)) RememberIgnored(cleanOriginal);
                                droppedUnsafeEntries = true;
                                continue;
                            }

                            string cleanTranslated = SanitizeUITranslationResult(cleanOriginal, cleanStoredValue);
                            if (string.IsNullOrWhiteSpace(cleanTranslated) || string.Equals(cleanOriginal, cleanTranslated, StringComparison.Ordinal))
                            {
                                RememberIgnored(cleanOriginal);
                                droppedUnsafeEntries = true;
                                continue;
                            }

                            string cleanKey = BuildCacheKey(cleanOriginal);
                            Cache[cleanKey] = cleanTranslated;
                            if (!string.Equals(cleanKey, kvp.Key, StringComparison.Ordinal) ||
                                !string.Equals(cleanTranslated, kvp.Value, StringComparison.Ordinal))
                            {
                                droppedUnsafeEntries = true;
                            }
                        }
                        if (droppedUnsafeEntries) _cacheDirty = true;
                    }
                    Log.Message("[AutoTranslationCore] 📦 " + "ATC_Log_UICacheLoaded".Translate(Cache.Count));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] ⚠️ " + "ATC_LogError_UICacheLoadFailed".Translate(ex.Message));
            }
        }

        private static void LoadIgnoredCache()
        {
            try
            {
                if (!File.Exists(IgnoredCacheFilePath)) return;

                string json = File.ReadAllText(IgnoredCacheFilePath);
                var loaded = JsonConvert.DeserializeObject<List<string>>(json);
                if (loaded == null) return;

                foreach (string key in loaded)
                {
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    if (IgnoredCache.Count >= MaxIgnoredCacheSize) break;

                    if (TryGetOriginalTextFromCacheKey(key, out _, out TargetLanguage? keyLanguage) &&
                        (!keyLanguage.HasValue || keyLanguage.Value == AutoTranslatorMod.Settings.TargetLang))
                    {
                        IgnoredCache[key] = true;
                    }
                }

                Log.Message("[AutoTranslationCore] UI ignored cache loaded: " + IgnoredCache.Count);
            }
            catch (Exception ex)
            {
                QuarantineBrokenCacheFile(IgnoredCacheFilePath);
                Log.Warning("[AutoTranslationCore] UI ignored cache load failed: " + ex.Message);
            }
        }


        public static void SaveCache()
        {
            try
            {
                var dictToSave = LoadExistingCacheFileForMerge(CacheFilePath, false);
                RemoveCurrentLanguageEntries(dictToSave);
                foreach (var kvp in Cache)
                {
                    dictToSave[kvp.Key] = kvp.Value;
                }

                string json = JsonConvert.SerializeObject(dictToSave, Formatting.Indented);
                WriteAllTextAtomic(CacheFilePath, json);
                _cacheDirty = false;
                System.Threading.Interlocked.Exchange(ref _lastCacheSaveTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] ⚠️ " + "ATC_LogError_UICacheSaveFailed".Translate(ex.Message));
            }
        }

        private static void SaveIgnoredCache()
        {
            try
            {
                var merged = LoadExistingIgnoredCacheFileForMerge(IgnoredCacheFilePath);
                RemoveCurrentLanguageEntries(merged);
                foreach (string key in IgnoredCache.Keys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        merged[key] = true;
                    }
                }

                var keys = merged.Keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .Take(MaxIgnoredCacheSize)
                    .ToList();

                string json = JsonConvert.SerializeObject(keys, Formatting.Indented);
                WriteAllTextAtomic(IgnoredCacheFilePath, json);
                _ignoredCacheDirty = false;
                System.Threading.Interlocked.Exchange(ref _lastCacheSaveTicks, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] UI ignored cache save failed: " + ex.Message);
            }
        }

        private static Dictionary<string, string> LoadExistingCacheFileForMerge(string path, bool quarantineOnError)
        {
            try
            {
                if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);

                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                return loaded != null
                    ? new Dictionary<string, string>(loaded, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                if (quarantineOnError) QuarantineBrokenCacheFile(path);
                Log.Warning("[AutoTranslationCore] UI cache merge read failed: " + ex.Message);
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private static Dictionary<string, bool> LoadExistingIgnoredCacheFileForMerge(string path)
        {
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);

            try
            {
                if (!File.Exists(path)) return result;

                var loaded = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path));
                if (loaded == null) return result;

                foreach (string key in loaded)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        result[key] = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] UI ignored cache merge read failed: " + ex.Message);
            }

            return result;
        }

        private static void RemoveCurrentLanguageEntries<T>(IDictionary<string, T> dict)
        {
            string prefix = AutoTranslatorMod.Settings.TargetLang + "|";
            var keysToRemove = dict.Keys
                .Where(k => string.IsNullOrEmpty(k) || k.StartsWith(prefix, StringComparison.Ordinal) || !HasLanguagePrefix(k))
                .ToList();

            foreach (string key in keysToRemove)
            {
                dict.Remove(key);
            }
        }

        private static bool HasLanguagePrefix(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey)) return false;

            foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
            {
                if (cacheKey.StartsWith(lang + "|", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteAllTextAtomic(string path, string text)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tempPath, path);
        }

        private static bool EnsureCacheFileReadable(string path)
        {
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                return true;
            }
            catch (Exception ex)
            {
                QuarantineBrokenCacheFile(path);
                Log.Warning("[AutoTranslationCore] UI cache file was unreadable: " + ex.Message);
                return false;
            }
        }

        private static void QuarantineBrokenCacheFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                string backupPath = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.Move(path, backupPath);
                Log.Warning("[AutoTranslationCore] UI cache was unreadable and has been backed up: " + backupPath);
            }
            catch (Exception moveEx)
            {
                Log.Warning("[AutoTranslationCore] UI cache quarantine failed: " + moveEx.Message);
            }
        }

        public static void FlushCache()
        {
            if (_cacheDirty)
            {
                SaveCache();
            }

            if (_ignoredCacheDirty)
            {
                SaveIgnoredCache();
            }
        }


        public static int GetQueueCount() { return Math.Max(0, System.Threading.Volatile.Read(ref _queuedApproxCount)); }
        public static int GetPendingCount() { return PendingTranslations.Count; }
        public static int GetIgnoredCount() { return IgnoredCache.Count; }

        public static string BuildCacheKey(string text)
        {
            return $"{AutoTranslatorMod.Settings.TargetLang}|{text}";
        }

        private static bool TryGetOriginalTextFromCacheKey(string cacheKey, out string original)
        {
            return TryGetOriginalTextFromCacheKey(cacheKey, out original, out _);
        }

        private static bool TryGetOriginalTextFromCacheKey(string cacheKey, out string original, out TargetLanguage? keyLanguage)
        {
            original = cacheKey;
            keyLanguage = null;
            if (string.IsNullOrEmpty(cacheKey)) return false;

            foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
            {
                string prefix = lang + "|";
                if (cacheKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    original = cacheKey.Substring(prefix.Length);
                    keyLanguage = lang;
                    return true;
                }
            }

            return true;
        }

        public static bool IsIgnored(string text)
        {
            return IgnoredCache.ContainsKey(BuildCacheKey(GetTranslationLookupText(text)));
        }

        public static void ReloadForLanguageChange()
        {
            Cache.Clear();
            IgnoredCache.Clear();
            Patch_GUI_Label_GUIContent.ClearCache();
            while (TranslationQueue.TryDequeue(out _)) { }
            PendingTranslations.Clear();
            System.Threading.Interlocked.Exchange(ref _queuedApproxCount, 0);
            _cacheDirty = false;
            _ignoredCacheDirty = false;

            LoadCache();
            LoadIgnoredCache();
        }

        public static bool TryGetCachedTranslation(string text, out string translated)
        {
            translated = null;
            if (!ShouldInterceptText(text))
            {
                return false;
            }

            string lookupText = GetTranslationLookupText(text);
            string cacheKey = BuildCacheKey(lookupText);
            if (Cache.TryGetValue(cacheKey, out translated))
            {
                if (TryNormalizeCachedTranslation(cacheKey, lookupText, translated, out translated))
                {
                    translated = RestoreTranslationDisplayText(text, translated);
                    return true;
                }

                return false;
            }

            if (Cache.TryGetValue(lookupText, out translated)
                && IsCachedTranslationCompatibleWithCurrentLanguage(translated))
            {
                if (TryNormalizeCachedTranslation(lookupText, lookupText, translated, out translated))
                {
                    Cache[cacheKey] = translated;
                    _cacheDirty = true;
                    translated = RestoreTranslationDisplayText(text, translated);
                    return true;
                }
            }

            return false;
        }

        private static bool TryNormalizeCachedTranslation(string cacheKey, string original, string translated, out string normalized)
        {
            normalized = SanitizeUITranslationResult(original, translated);
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(original, normalized, StringComparison.Ordinal))
            {
                Cache.TryRemove(cacheKey, out _);
                RememberIgnored(original);
                _cacheDirty = true;
                return false;
            }

            normalized = LanguageDetector.NormalizeChineseVariant(normalized, AutoTranslatorMod.Settings.TargetLang);
            if (ShouldSkipUITranslationText(normalized))
            {
                Cache.TryRemove(cacheKey, out _);
                RememberIgnored(original);
                _cacheDirty = true;
                return false;
            }

            if (!string.Equals(normalized, translated, StringComparison.Ordinal))
            {
                Cache[cacheKey] = normalized;
                _cacheDirty = true;
            }
            return true;
        }

        private static bool IsCachedTranslationCompatibleWithCurrentLanguage(string translated)
        {
            if (string.IsNullOrWhiteSpace(translated)) return false;

            if (AutoTranslatorMod.Settings.TargetLang == TargetLanguage.Traditional)
            {
                return !LanguageDetector.LooksLikeSimplified(translated);
            }

            if (AutoTranslatorMod.Settings.TargetLang == TargetLanguage.Simplified)
            {
                return !LanguageDetector.LooksLikeTraditional(translated);
            }

            return true;
        }


        private static void RememberIgnored(string text)
        {
            if (IgnoredCache.Count < MaxIgnoredCacheSize)
            {
                string cacheKey = BuildCacheKey(GetTranslationLookupText(text));
                if (IgnoredCache.TryAdd(cacheKey, true))
                {
                    _ignoredCacheDirty = true;
                }
            }
        }


        private static void SaveCacheIfDue(bool force = false)
        {
            if (!_cacheDirty && !_ignoredCacheDirty) return;

            long last = System.Threading.Interlocked.Read(ref _lastCacheSaveTicks);
            if (!force && DateTime.UtcNow.Ticks - last < TimeSpan.FromSeconds(10).Ticks) return;

            if (_cacheDirty) SaveCache();
            if (_ignoredCacheDirty) SaveIgnoredCache();
        }


        // ==========================================
        // 🔄 咪咪特製：物理超渡前台 UI 緩存！(含實體檔案刪除)
        // ==========================================
        public static void ClearUICache()
        {
            // 1. 清空記憶體裡的所有字典 (包含翻譯結果、黑名單、視窗快取)
            Cache.Clear();
            IgnoredCache.Clear();
            Patch_GUI_Label_GUIContent.ClearCache();
            while (TranslationQueue.TryDequeue(out _)) { }
            PendingTranslations.Clear();
            System.Threading.Interlocked.Exchange(ref _queuedApproxCount, 0);
            _cacheDirty = false;
            _ignoredCacheDirty = false;

            // 2. 物理超渡硬碟裡的實體字典檔案！
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    File.Delete(CacheFilePath);
                }

                if (File.Exists(IgnoredCacheFilePath))
                {
                    File.Delete(IgnoredCacheFilePath);
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Warning($"[AutoTranslationCore] " + "ATC_LogError_DeleteUICacheFailed".Translate(ex.Message));
            }

            Verse.Log.Message("[AutoTranslationCore] 🔄 " + "ATC_Log_UICacheClearedFull".Translate());
        }
        // ==========================================
        // 🚀 咪咪特製：一鍵熱重載注入總樞紐！
        // ==========================================
        public static void RefreshRuntimeUICache()
        {
            Patch_GUI_Label_GUIContent.ClearCache();
            DiscardQueuedTranslations();
            SaveCacheIfDue(true);

            Verse.Log.Message("[AutoTranslationCore] UI render cache refreshed without deleting saved translations.");
        }

        public static void RequestHotReload()
        {
            try
            {
                // 1. 清空前台 UI 緩存與黑名單，強制重新翻譯
                RefreshRuntimeUICache();

                // 2. 重新讀取實體 XML 快取並灌入記憶體
                AutoTranslatorScanner.MemoryDrop_InjectNow();

                // 3. 右上角彈出成功提示
                // ✅ 完美修復 CS8957：加上 .ToString() 統一兩邊的資料類型為 string！
                Messages.Message("ATC_Message_HotReloadSuccess".CanTranslate()
                    ? "ATC_Message_HotReloadSuccess".Translate().ToString()
                    : "🪂 [記憶體空投] 翻譯已即時注入，UI 視窗快取已完全刷新！",
                    MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoTranslationCore] Hot reload failed: {ex.Message}");
            }
        }

    }
}
