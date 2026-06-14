using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Verse;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {
        private static bool _externalPatchCoveredOverrideCleanupQueued;

        public static void QueueExternalPatchCoveredOverrideCleanup()
        {
            if (_externalPatchCoveredOverrideCleanupQueued) return;
            _externalPatchCoveredOverrideCleanupQueued = true;

            LongEventHandler.QueueLongEvent(CleanupExternalPatchCoveredOverrides, null, false, null);
        }

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
