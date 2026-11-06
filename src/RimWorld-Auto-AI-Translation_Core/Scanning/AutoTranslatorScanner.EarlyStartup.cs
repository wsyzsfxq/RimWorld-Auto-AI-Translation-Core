using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {
        private static readonly object EarlyStartupGate = new object();
        private static LoadedLanguage _earlyStartupLanguage;
        private static MemoryDropPayload _earlyStartupPayload;
        private static volatile int _earlyInputVersion;
        private static bool _earlyStartupClosed;
        private static LoadedLanguage _earlyDefsToNormalize;

        // Called on the game thread after the first native language pass.
        internal static void PrepareEarlyStartupTranslations(LoadedLanguage language)
        {
            if (!UnityData.IsInMainThread || language == null || AutoTranslatorMod.Settings == null ||
                !ReferenceEquals(language, LanguageDatabase.activeLanguage)) return;
            int version;
            lock (EarlyStartupGate)
            {
                if (_earlyStartupLanguage != null || _earlyStartupClosed || AutoTranslatorSettings.IsCancellationRequested) return;
                _earlyStartupLanguage = language;
                version = _earlyInputVersion;
            }
            BindRuntimeTranslationLanguage(language);
            AutoTranslatorMod.TryAutoSyncLanguageWithGame(resetCaches: false, log: false, writeSettings: false);
            string packPath = GetLocalPackPath();
            string targetFolder = GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
            var order = new TranslationFileOrderSnapshot
            {
                GameOrder = GetGamePackageOrderSnapshot(refresh: true),
                ExplicitOrder = AutoTranslatorMod.Settings.CloudTranslationPriority?.ToList() ?? new List<string>()
            };
            Task.Run(() =>
            {
                try
                {
                    MemoryDropPayload payload = BuildMemoryDropPayload(false, null, null, packPath, targetFolder, order);
                    lock (EarlyStartupGate)
                    {
                        if (version == _earlyInputVersion && !_earlyStartupClosed)
                            _earlyStartupPayload = payload;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[AutoTranslationCore] Early translation preparation skipped: " + ex.Message);
                }
            });
        }

        // A file change makes any in-flight or already published payload obsolete.
        private static void NotifyEarlyStartupInputsChanged()
        {
            lock (EarlyStartupGate)
            {
                _earlyInputVersion++;
                _earlyStartupPayload = null;
            }
        }

        internal static void InvalidateEarlyStartupTranslations()
        {
            lock (EarlyStartupGate)
            {
                _earlyInputVersion++;
                _earlyStartupPayload = null;
                _earlyStartupClosed = true;
            }
        }

        internal static void ResetEarlyStartupTranslations()
        {
            lock (EarlyStartupGate)
            {
                _earlyInputVersion++;
                _earlyStartupPayload = null;
                _earlyStartupLanguage = null;
                _earlyStartupClosed = false;
            }
            DiscardMemoryDropForClearedPlayData();
        }

        // Consume a finished preparation once. The normal framed loader remains queued.
        internal static void ApplyReadyEarlyStartupTranslations(LoadedLanguage language)
        {
            if (!UnityData.IsInMainThread || language == null || !ReferenceEquals(language, LanguageDatabase.activeLanguage)) return;
            int version;
            MemoryDropPayload payload;
            lock (EarlyStartupGate)
            {
                if (_earlyStartupClosed) return;
                _earlyStartupClosed = true;
                version = _earlyInputVersion;
                payload = ReferenceEquals(language, _earlyStartupLanguage) ? _earlyStartupPayload : null;
                _earlyStartupPayload = null;
                _earlyStartupLanguage = null;
            }
            if (payload == null || _activeMemoryDropApply != null) return;

            var state = new MemoryDropApplyState { Payload = payload, Language = language, KeyedEntries = payload.Keyed.ToList() };
            try
            {
                bool current() => version == _earlyInputVersion && !AutoTranslatorSettings.IsCancellationRequested &&
                    ReferenceEquals(language, LanguageDatabase.activeLanguage) && AutoTranslatorMod.Settings != null &&
                    string.Equals(payload.TargetFolder, GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang), StringComparison.OrdinalIgnoreCase);
                while (current() && ApplyKeyedBatch(state, language)) { }
                while (current() && ApplyDefBatch(state, language)) { }
                if (state.NeedsDataInjection && current())
                {
                    using (var before = InjectChangedDefs(state, false)) while (current() && before.MoveNext()) { }
                    using (var after = InjectChangedDefs(state, true)) while (current() && after.MoveNext()) { }
                }
                if (current())
                {
                    ReportUnavailableRuntimeDefs(state);
                    AutoTranslatorSettings.AddLog("启动缓存译文提前应用：Keyed=" + state.InjectedKeyed + "；提交 Def=" +
                        state.InjectedDefs + "；暂不适用 Def=" + state.UnavailableDefs +
                        "；暂不适用字段路径=" + state.UnavailableDefPaths +
                        (state.InjectionFailed ? "；部分条目留待后续加载" : string.Empty));
                }
            }
            finally
            {
                if (state.NeedsDataInjection)
                {
                    _earlyDefsToNormalize = language;
                    ResetMemoryDropStamps();
                }
            }
        }
    }
}
