using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core
{
    // Copies official localization metadata on the game thread. Workers only read
    // these immutable records, never LoadedLanguage's live dictionaries.
    internal static class OfficialKeyedCatalog
    {
        internal sealed class Entry
        {
            internal string Key, Source, File, PackageId;
            internal int Line;
            internal bool Missing;
            internal Regex SourcePattern, PseudoPattern;
            internal string[] FormatArguments;
        }
        internal sealed class Snapshot
        {
            internal LoadedLanguage Language;
            internal Entry[] Entries = new Entry[0];
            internal Dictionary<string, Entry> ByKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
            internal Dictionary<string, List<Entry>> Pseudo = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            internal Dictionary<string, List<Entry>> Plain = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            internal Dictionary<string, List<Template>> Templates = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
            internal HashSet<char> TemplateStarts = new HashSet<char>();
            internal int TemplateAnchorLengths;
            internal Dictionary<string, Entry[]> ByPackage = new Dictionary<string, Entry[]>(StringComparer.OrdinalIgnoreCase);
            internal Dictionary<string, string> SourceFingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            internal HashSet<string> AmbiguousContexts = new HashSet<string>(StringComparer.Ordinal);
        }
        internal sealed class Template
        {
            internal Entry Entry;
            internal Regex Pattern;
            internal string[] Arguments;
            internal bool RequiresOwner;
        }
        private sealed class Origin
        {
            internal string Key;
            internal Dictionary<string, string> Arguments;
        }
        private static readonly object OriginGate = new object();
        private static ConditionalWeakTable<string, List<Origin>> _origins = new ConditionalWeakTable<string, List<Origin>>();
        private static Snapshot _snapshot = new Snapshot();
        [ThreadStatic] private static bool _resolving;
        private static readonly Regex Argument = new Regex(@"(?<!\{)\{([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)(?:,[^{}:]*)?(?::[^{}]*)?\}(?!\})", RegexOptions.Compiled);

        internal static Snapshot Current => Volatile.Read(ref _snapshot);

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
            var snapshot = new Snapshot { Language = active };
            var entries = new List<Entry>();
            foreach (var pair in source.keyedReplacements)
            {
                var replacement = pair.Value;
                if (replacement == null || replacement.isPlaceholder || string.IsNullOrEmpty(replacement.value)) continue;
                string file = NormalizePath(replacement.fileSourceFullPath);
                string package = roots.FirstOrDefault(mod => file.StartsWith(mod.Root, StringComparison.OrdinalIgnoreCase))?.Id;
                if (AutoTranslatorScanner.IsNonTranslatableSystemPackage(package)) continue;
                var entry = new Entry { Key = pair.Key, Source = replacement.value, File = file,
                    Line = replacement.fileSourceLine, PackageId = package ?? string.Empty,
                    Missing = !active.HaveTextForKey(pair.Key, false) };
                entries.Add(entry);
                if (Argument.IsMatch(entry.Source))
                {
                    entry.SourcePattern = BuildPattern(entry.Source, out entry.FormatArguments);
                    string[] ignored;
                    entry.PseudoPattern = BuildPattern(Pseudo(entry.Source), out ignored);
                }
                snapshot.ByKey[entry.Key] = entry;
                Add(snapshot.Pseudo, Pseudo(entry.Source), entry);
                Add(snapshot.Plain, entry.Source, entry);
                RegisterOrigin(entry.Source, entry.Key, null);
                AddTemplate(snapshot, entry, Pseudo(entry.Source));
                if (!string.Equals(Pseudo(entry.Source), entry.Source, StringComparison.Ordinal))
                    AddTemplate(snapshot, entry, entry.Source);
            }
            snapshot.Entries = entries.ToArray();
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
            // Keep weak provenance for strings still held by third-party caches.
            // After a language reload those same strings resolve by key again.
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

        internal static bool IsKnownDisplay(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            Snapshot snapshot = Current;
            if (!ReferenceEquals(snapshot.Language, LanguageDatabase.activeLanguage)) return false;
            lock (OriginGate) if (_origins.TryGetValue(text, out _)) return true;
            return snapshot.Pseudo.TryGetValue(text, out List<Entry> entries) &&
                entries.Any(entry => !string.Equals(entry.Source, text, StringComparison.Ordinal));
        }

        // Record actual returned object identity. A cached field keeps this very
        // string; no write to that field or internal key is needed later.
        internal static void Observe(string key, string rendered, bool formatted)
        {
            if (_resolving || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(rendered)) return;
            Snapshot snapshot = Current;
            if (!ReferenceEquals(snapshot.Language, LanguageDatabase.activeLanguage)) return;
            if (!snapshot.ByKey.TryGetValue(key, out Entry entry)) return;
            Dictionary<string, string> args = null;
            if (formatted)
            {
                if (!TryArguments(entry.SourcePattern, entry.FormatArguments, rendered, out args) &&
                    !TryArguments(entry.PseudoPattern, entry.FormatArguments, rendered, out args)) return;
            }
            RegisterOrigin(rendered, key, args);
        }

        private static void RegisterOrigin(string rendered, string key, Dictionary<string, string> arguments)
        {
            lock (OriginGate)
            {
                List<Origin> origins = _origins.GetOrCreateValue(rendered);
                if (!origins.Any(origin => origin.Key == key)) origins.Add(new Origin { Key = key, Arguments = arguments });
            }
        }

        internal static bool TryResolve(string displayed, out string translated)
        {
            translated = null;
            if (_resolving || !UnityData.IsInMainThread || string.IsNullOrEmpty(displayed) ||
                Patch_GUI_Label_GUIContent.BypassInterceptor) return false;
            Snapshot snapshot = Current;
            if (!ReferenceEquals(snapshot.Language, LanguageDatabase.activeLanguage)) return false;
            _resolving = true;
            try
            {
                List<Origin> origins;
                lock (OriginGate)
                    origins = _origins.TryGetValue(displayed, out List<Origin> known) ? known.ToList() : null;
                if (origins != null && ResolveOrigins(snapshot, origins, null, out translated))
                    return !string.Equals(displayed, translated, StringComparison.Ordinal);

                if (snapshot.Pseudo.TryGetValue(displayed, out List<Entry> entries))
                {
                    if (AlreadyDisplayed(snapshot, entries, displayed)) return false;
                    bool changed = entries.Any(entry => !string.Equals(entry.Source, displayed, StringComparison.Ordinal));
                    string owner = changed ? null : UIInterceptor.ResolveDisplayedSourcePackage();
                    if ((changed || !string.IsNullOrEmpty(owner)) &&
                        ResolveOrigins(snapshot, entries.Select(entry => new Origin { Key = entry.Key }), owner, out translated))
                        return !string.Equals(displayed, translated, StringComparison.Ordinal);
                }
                // Plain text equality is insufficient: require the calling Mod's
                // ownership as well. This never replaces ordinary internal strings.
                if (snapshot.Plain.TryGetValue(displayed, out entries))
                {
                    if (AlreadyDisplayed(snapshot, entries, displayed)) return false;
                    string owner = UIInterceptor.ResolveDisplayedSourcePackage();
                    if (!string.IsNullOrEmpty(owner) && ResolveOrigins(snapshot,
                        entries.Select(entry => new Origin { Key = entry.Key }), owner, out translated))
                        return !string.Equals(displayed, translated, StringComparison.Ordinal);
                }
                var templates = new HashSet<Template>();
                for (int index = 0; index < displayed.Length; index++)
                {
                    if (!snapshot.TemplateStarts.Contains(displayed[index])) continue;
                    for (int length = 1; length <= 4 && index + length <= displayed.Length; length++)
                        if ((snapshot.TemplateAnchorLengths & (1 << length)) != 0 &&
                            snapshot.Templates.TryGetValue(displayed.Substring(index, length), out List<Template> matches))
                            foreach (Template match in matches) templates.Add(match);
                }
                if (templates.Count > 0)
                {
                    var candidates = new List<Origin>();
                    string owner = null;
                    foreach (Template template in templates)
                    {
                        Match match = template.Pattern.Match(displayed);
                        if (!match.Success) continue;
                        if (template.RequiresOwner)
                        {
                            if (owner == null) owner = UIInterceptor.ResolveDisplayedSourcePackage() ?? string.Empty;
                            if (!string.Equals(owner, template.Entry.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
                        }
                        candidates.Add(new Origin { Key = template.Entry.Key,
                            Arguments = template.Arguments.Select((name, index) => new { name, index })
                                .ToDictionary(item => item.name, item => match.Groups["p" + item.index].Value) });
                    }
                    if (candidates.Count > 0 && ResolveOrigins(snapshot, candidates, null, out translated))
                        return !string.Equals(displayed, translated, StringComparison.Ordinal);
                }
                return false;
            }
            finally { _resolving = false; }
        }

        private static bool ResolveOrigins(Snapshot snapshot, IEnumerable<Origin> origins,
            string requiredOwner, out string translated)
        {
            translated = null;
            var values = new List<KeyValuePair<Origin, string>>();
            foreach (Origin origin in origins)
            {
                if (!snapshot.ByKey.TryGetValue(origin.Key, out Entry entry) ||
                    requiredOwner != null && !string.Equals(entry.PackageId, requiredOwner, StringComparison.OrdinalIgnoreCase)) continue;
                TaggedString current;
                string value = snapshot.Language.TryGetTextFromKey(origin.Key, out current)
                    ? current.ToString() : Prefs.DevMode ? Pseudo(entry.Source) : entry.Source;
                if (origin.Arguments != null)
                    value = RenderTemplate(value, origin.Arguments);
                values.Add(new KeyValuePair<Origin, string>(origin, value));
            }
            string[] distinct = values.Select(value => value.Value).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length == 1) { translated = distinct[0]; return true; }
            if (distinct.Length <= 1) return false;
            if (requiredOwner != null)
            {
                RecordAmbiguity(snapshot, values.Select(value => value.Key), requiredOwner);
                return false;
            }
            // Two keys can have the same English text and different translations.
            // Resolve the actual displaying Mod before choosing either one.
            string owner = UIInterceptor.ResolveDisplayedSourcePackage();
            if (string.IsNullOrEmpty(owner))
            {
                RecordAmbiguity(snapshot, values.Select(value => value.Key), string.Empty);
                return false;
            }
            return ResolveOrigins(snapshot, values.Select(value => value.Key), owner, out translated);
        }

        private static void RecordAmbiguity(Snapshot snapshot, IEnumerable<Origin> origins, string packageId)
        {
            string keys = string.Join(",", origins.Select(origin => origin.Key).Distinct().OrderBy(key => key, StringComparer.Ordinal));
            string context = packageId + "|" + keys;
            if (snapshot.AmbiguousContexts.Add(context))
                AutoTranslatorSettings.AddDebugLog("Keyed 文字暂未替换：同文对应不同译文，无法确认显示位置；Mod=" +
                    packageId + "；键=" + keys);
        }

        private static void Add(Dictionary<string, List<Entry>> index, string value, Entry entry)
        {
            if (!index.TryGetValue(value, out List<Entry> list)) index[value] = list = new List<Entry>();
            list.Add(entry);
        }
        private static bool AlreadyDisplayed(Snapshot snapshot, IEnumerable<Entry> entries, string displayed)
        {
            return entries.All(entry => snapshot.Language.TryGetTextFromKey(entry.Key, out TaggedString text) &&
                string.Equals(text.ToString(), displayed, StringComparison.Ordinal));
        }
        private static string Prefix(string value) => value.Length < 4 ? value : value.Substring(0, 4);
        private static void AddTemplate(Snapshot snapshot, Entry entry, string source)
        {
            Match first = Argument.Match(source);
            if (!first.Success) return;
            string literal = string.Empty;
            int previous = 0;
            foreach (Match match in Argument.Matches(source))
            {
                string part = UnescapeLiteral(source.Substring(previous, match.Index - previous));
                if (part.Length > literal.Length) literal = part;
                previous = match.Index + match.Length;
            }
            string tail = UnescapeLiteral(source.Substring(previous));
            if (tail.Length > literal.Length) literal = tail;
            if (literal.Length == 0) return;
            string[] arguments = entry.FormatArguments;
            Regex pattern = string.Equals(source, entry.Source, StringComparison.Ordinal)
                ? entry.SourcePattern : entry.PseudoPattern;
            string prefix = Prefix(literal);
            if (!snapshot.Templates.TryGetValue(prefix, out List<Template> list))
                snapshot.Templates[prefix] = list = new List<Template>();
            snapshot.TemplateStarts.Add(prefix[0]);
            snapshot.TemplateAnchorLengths |= 1 << prefix.Length;
            list.Add(new Template { Entry = entry, Pattern = pattern, Arguments = arguments,
                RequiresOwner = string.Equals(source, entry.Source, StringComparison.Ordinal) });
        }
        private static Regex BuildPattern(string source, out string[] arguments)
        {
            var indices = new List<string>();
            var pattern = new StringBuilder("\\A");
            int previous = 0;
            foreach (Match match in Argument.Matches(source))
            {
                pattern.Append(Regex.Escape(UnescapeLiteral(source.Substring(previous, match.Index - previous))));
                string name = match.Groups[1].Value;
                int index = indices.IndexOf(name);
                bool first = index < 0;
                if (first) { index = indices.Count; indices.Add(name); }
                pattern.Append(first ? "(?<p" + index + ">.*?)" : "\\k<p" + index + ">");
                previous = match.Index + match.Length;
            }
            pattern.Append(Regex.Escape(UnescapeLiteral(source.Substring(previous)))).Append("\\z");
            arguments = indices.ToArray();
            return new Regex(pattern.ToString(), RegexOptions.Singleline);
        }
        private static bool TryArguments(Regex pattern, string[] indices, string rendered, out Dictionary<string, string> arguments)
        {
            if (pattern == null) { arguments = null; return false; }
            Match match = pattern.Match(rendered);
            arguments = match.Success ? indices.Select((name, index) => new { name, index })
                .ToDictionary(item => item.name, item => match.Groups["p" + item.index].Value) : null;
            return match.Success;
        }

        private static string UnescapeLiteral(string value) => value.Replace("{{", "{").Replace("}}", "}");

        private static string RenderTemplate(string template, Dictionary<string, string> arguments)
        {
            var rendered = new StringBuilder();
            int previous = 0;
            foreach (Match match in Argument.Matches(template))
            {
                rendered.Append(UnescapeLiteral(template.Substring(previous, match.Index - previous)));
                rendered.Append(arguments.TryGetValue(match.Groups[1].Value,
                    out string value) ? value : match.Value);
                previous = match.Index + match.Length;
            }
            return rendered.Append(UnescapeLiteral(template.Substring(previous))).ToString();
        }

        // Exact mapping in both local 1.6 and 1.7 Translator.PseudoTranslated IL.
        // Braced placeholders are deliberately left untouched, as in the game.
        internal static string Pseudo(string source)
        {
            const string replacements = "àþçðèƑğĥìĵкſṁƞòṗqṟșṭùṽẅẋýž";
            var result = new StringBuilder(source.Length);
            bool argument = false;
            foreach (char value in source)
            {
                if (value == '{') argument = true;
                else if (value == '}') argument = false;
                result.Append(!argument && value >= 'a' && value <= 'z' ? replacements[value - 'a'] : value);
            }
            return result.ToString();
        }
    }
}
