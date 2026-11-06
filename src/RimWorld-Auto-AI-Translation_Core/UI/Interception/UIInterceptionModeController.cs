using System.Threading.Tasks;
using AutoTranslator_Core.TargetedHardcodedUi;

namespace AutoTranslator_Core
{
    // Switching preserves translations and preferences, but ends temporary capture.
    internal static class UIInterceptionModeController
    {
        private static bool _initialized;
        private static bool _legacyInitialized;
        private static bool _lastLegacySelected;
        private static bool _lastLegacyEnabled;
        private static volatile bool _legacyReady;
        private static bool _shuttingDown;
        private static volatile bool _translationDataPaused;

        internal static bool LegacySelected => AutoTranslatorMod.Settings?.UseLegacyUIInterception == true;
        internal static bool LegacyEnabled => !_translationDataPaused && _legacyReady && LegacySelected &&
            AutoTranslatorMod.Settings?.EnableLegacyUIInterceptor == true;
        internal static bool CaptureEnabled => !LegacySelected &&
            AutoTranslatorMod.Settings?.EnableUIInterceptor == true;
        internal static bool TargetedEnabled => !LegacySelected &&
            AutoTranslatorMod.Settings?.EnableHardcodedUiPrototype == true;

        // Called on the main thread, both after settings changes and at startup.
        internal static void Refresh(bool? selectedMode = null)
        {
            if (_shuttingDown || AutoTranslatorMod.Settings == null) return;
            bool selected = selectedMode ?? LegacySelected;
            bool enabled = selected && AutoTranslatorMod.Settings.EnableLegacyUIInterceptor;
            if (_initialized && selected == _lastLegacySelected && enabled == _lastLegacyEnabled) return;

            bool modeChanged = !_initialized || selected != _lastLegacySelected;
            _legacyReady = false;
            if (_legacyInitialized) LegacyInterception.UIInterceptor.PrepareForLanguageChange();
            // Stop the current handlers and save capture evidence before selecting
            // the next mode. Temporary capture must be started manually again.
            HardcodedUiRuntime.SetSuspended(true);
            if (selected != LegacySelected) AutoTranslatorMod.Settings.EnableUIInterceptor = false;
            HardcodedUiCaptureSession.NotifySettingsChanged(false);
            AutoTranslatorMod.Settings.UseLegacyUIInterception = selected;
            HardcodedUiRuntime.SetSuspended(selected);
            UIInterceptor.ResetProbeCaches();

            if (enabled)
            {
                if (_legacyInitialized) LegacyInterception.UIInterceptor.ReloadForLanguageChange();
                else
                {
                    EnsureLegacyDataInitialized();
                }
                _legacyReady = true;
            }
            _initialized = true;
            _lastLegacySelected = selected;
            _lastLegacyEnabled = enabled;
            if (modeChanged) HardcodedUiTargetedPatchManager.RequestReload();
        }

        internal static void EnsureLegacyDataInitialized()
        {
            if (_legacyInitialized) return;
            LegacyInterception.UIInterceptor.EnsureInitialized();
            _legacyInitialized = true;
        }

        internal static void RefreshLegacyRenderCache()
        {
            if (_legacyInitialized) LegacyInterception.UIInterceptor.RefreshRuntimeUICache();
        }

        internal static void PrepareForLanguageChange()
        {
            if (_legacyInitialized) LegacyInterception.UIInterceptor.PrepareForLanguageChange();
        }

        internal static void ReloadForLanguageChange()
        {
            if (_legacyInitialized) LegacyInterception.UIInterceptor.ReloadForLanguageChange();
        }

        internal static bool HasOutstandingWork => _legacyInitialized &&
            LegacyInterception.UIInterceptor.HasOutstandingTranslationWork;

        internal static void CancelPendingWork()
        {
            if (_legacyInitialized) LegacyInterception.UIInterceptor.PrepareForLanguageChange();
        }

        internal static Task WaitForWorkDrainAsync()
        {
            return _legacyInitialized ? LegacyInterception.UIInterceptor.WaitForPendingTranslationWorkDrainAsync()
                : Task.CompletedTask;
        }

        // The backup coordinator owns the global task lease while this gate is closed.
        internal static void PauseTranslationData()
        {
            _translationDataPaused = true;
        }

        internal static async Task DrainTranslationDataAsync()
        {
            if (!_legacyInitialized) return;
            LegacyInterception.UIInterceptor.PrepareForLanguageChange(requireTranslationFlush: true);
            await LegacyInterception.UIInterceptor.WaitForPendingTranslationWorkDrainAsync();
            LegacyInterception.UIInterceptor.FlushTranslationsForBackup();
        }

        internal static void ResumeTranslationData()
        {
            try
            {
                if (_legacyInitialized) LegacyInterception.UIInterceptor.ReloadForLanguageChange();
            }
            finally { _translationDataPaused = false; }
        }

        internal static void Shutdown()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            _legacyReady = false;
            if (_legacyInitialized) LegacyInterception.UIInterceptor.Shutdown();
            HardcodedUiRuntimeObservationStore.Flush();
        }
    }
}
