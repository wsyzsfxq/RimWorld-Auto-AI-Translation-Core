using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace AutoTranslator_Core.LegacyInterception
{
    public static partial class UIInterceptor
    {
        // Unlike the periodic best-effort saver, a requested backup must surface
        // an IO error instead of claiming an older disk file is the latest cache.
        internal static void FlushTranslationsForBackup()
        {
            lock (_cachePersistenceLock)
            {
                if (!_cacheDirty) return;
                var saved = File.Exists(CacheFilePath)
                    ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(CacheFilePath))
                    : new Dictionary<string, string>();
                if (saved == null) throw new InvalidDataException("UI 拦截译文文件不完整，手动备份尚未生成。");
                RemoveCurrentLanguageEntries(saved);
                foreach (var pair in Cache) saved[pair.Key] = pair.Value;
                WriteAllTextAtomic(CacheFilePath, JsonConvert.SerializeObject(saved, Formatting.Indented));
                _cacheDirty = false;
            }
        }
    }
}
