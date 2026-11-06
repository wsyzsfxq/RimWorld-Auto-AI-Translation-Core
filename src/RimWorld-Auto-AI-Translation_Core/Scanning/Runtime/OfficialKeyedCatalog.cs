using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core
{
    // Analysis metadata only. Display and measurement do not query this catalog.
    // Copy on the game thread so workers never read live language dictionaries.
    internal static class OfficialKeyedCatalog
    {
        internal sealed class Entry
        {
            internal string Key, Source, File, PackageId;
            internal int Line;
            internal bool Missing;
        }

        private sealed class Snapshot
        {
            internal Dictionary<string, Entry[]> ByPackage = new Dictionary<string, Entry[]>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, string> SourceFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static Snapshot _snapshot = new Snapshot();
        private static Snapshot Current => Volatile.Read(ref _snapshot);

        internal static Task CaptureAsync(CancellationToken token)
        {
            if (UnityData.IsInMainThread) { token.ThrowIfCancellationRequested(); Capture(); return Task.CompletedTask; }
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                try { token.ThrowIfCancellationRequested(); Capture(); completion.TrySetResult(true); }
                catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            });
            return completion.Task;
        }

        internal static void Capture()
        {
            if (!UnityData.IsInMainThread) return;
            LoadedLanguage source = LanguageDatabase.defaultLanguage;
            LoadedLanguage active = LanguageDatabase.activeLanguage;
            if (source?.keyedReplacements == null || active == null) return;
            var roots = ModsConfig.ActiveModsInLoadOrder.Where(mod => mod?.RootDir != null)
                .Select(mod => new { Root = NormalizePath(mod.RootDir.FullName).TrimEnd('/') + "/", Id = mod.PackageId })
                .OrderByDescending(mod => mod.Root.Length).ToList();
            var snapshot = new Snapshot();
            var entries = new List<Entry>();
            foreach (var pair in source.keyedReplacements)
            {
                var replacement = pair.Value;
                if (replacement == null || replacement.isPlaceholder || string.IsNullOrEmpty(replacement.value)) continue;
                string file = NormalizePath(replacement.fileSourceFullPath);
                string package = roots.FirstOrDefault(mod => file.StartsWith(mod.Root, StringComparison.OrdinalIgnoreCase))?.Id;
                if (AutoTranslatorScanner.IsNonTranslatableSystemPackage(package)) continue;
                entries.Add(new Entry { Key = pair.Key, Source = replacement.value, File = file,
                    Line = replacement.fileSourceLine, PackageId = package ?? string.Empty,
                    Missing = !active.HaveTextForKey(pair.Key, false) });
            }
            foreach (var group in entries.Where(entry => !string.IsNullOrEmpty(entry.PackageId))
                .GroupBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                snapshot.ByPackage[group.Key] = group.ToArray();
                snapshot.SourceFingerprints[group.Key] = Workflow.WorkflowIdentity.HashText(string.Join("\n",
                    group.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Key + "\n" + entry.Source + "\n" + entry.File)));
            }
            Volatile.Write(ref _snapshot, snapshot);
        }

        internal static void Reset()
        {
            Volatile.Write(ref _snapshot, new Snapshot());
        }

        internal static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

        internal static IEnumerable<Entry> ForPackage(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && Current.ByPackage.TryGetValue(packageId, out Entry[] entries)
                ? entries : new Entry[0];
        }

        internal static string SourceFingerprint(string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && Current.SourceFingerprints.TryGetValue(packageId, out string value)
                ? value : "catalog-empty";
        }
    }
}
