using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;
// 這個檔案負責清理被外部補丁覆蓋的本機翻譯檔。
// EN: This file removes local AI overrides covered by active external translation patches.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        // 這個欄位保存 external補丁CoveredOverride清理Queued 的執行狀態或快取資料。
        // EN: This field stores external patch covered override cleanup queued runtime state or cached data.
        private static bool _externalPatchCoveredOverrideCleanupQueued;

        // 這個方法負責排入 External補丁CoveredOverride清理 佇列。
        // EN: This method queues external patch covered override cleanup.
        public static void QueueExternalPatchCoveredOverrideCleanup()
        {
            if (_externalPatchCoveredOverrideCleanupQueued) return;
            _externalPatchCoveredOverrideCleanupQueued = true;

            AutoTranslator_LongEventCompat.ExecuteWhenFinished(CleanupExternalPatchCoveredOverrides);
        }

        // 這個方法負責清理並標準化 upExternal補丁CoveredOverrides 內容。
        // EN: This method cleans and normalizes up external patch covered overrides.
        public static void CleanupExternalPatchCoveredOverrides()
        {
            try
            {
                if (AutoTranslatorMod.Settings == null) return;

                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                TargetLanguage targetLang = AutoTranslatorMod.Settings.TargetLang;
                List<ModMetaData> modsToClear = ModLister.AllInstalledMods
                    .Where(m => m != null &&
                                m.Active &&
                                !string.IsNullOrEmpty(m.PackageId) &&
                                !IsTranslationPatchMod(m) &&
                                HasActiveExternalTargetLanguagePatch(m, targetLang) &&
                                ModUpdateDetector.HasLocalTranslationFiles(m))
                    .ToList();

                if (modsToClear.Count == 0) return;

                ClearOldTranslationFiles(modsToClear);
                ModUpdateDetector.ClearStatusCache();
                RequestMemoryDrop();

                Log.Message($"[AutoTranslationCore] Cleaned {modsToClear.Count} local AI overrides covered by active external translation patches.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] External translation patch override cleanup error: {ex.Message}");
            }
        }
    }
}
