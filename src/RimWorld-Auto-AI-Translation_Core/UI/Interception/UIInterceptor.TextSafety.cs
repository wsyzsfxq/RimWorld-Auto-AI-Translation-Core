using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace AutoTranslator_Core
{
    public static partial class UIInterceptor
    {
        private struct UITranslationTextContext
        {
            public string OriginalText;
            public string TranslationText;
            public string Prefix;
            public bool HasLogTimestamp;
        }

        private static UITranslationTextContext BuildTextContext(string text)
        {
            UITranslationTextContext context = new UITranslationTextContext
            {
                OriginalText = text,
                TranslationText = text ?? string.Empty,
                Prefix = string.Empty,
                HasLogTimestamp = false
            };

            if (string.IsNullOrEmpty(text)) return context;

            var match = LogTimestampRegex.Match(text);
            if (match.Success)
            {
                context.Prefix = match.Groups["prefix"].Value;
                context.TranslationText = match.Groups["body"].Value ?? string.Empty;
                context.HasLogTimestamp = true;
            }

            return context;
        }

        internal static bool ShouldInterceptText(string text)
        {
            return ShouldInterceptText(text, true);
        }

        private static bool ShouldInterceptText(string text, bool rememberSkipped)
        {
            UITranslationTextContext context = BuildTextContext(text);
            if (context.HasLogTimestamp && !AutoTranslatorMod.Settings.EnableUIErrorLogInterception)
            {
                if (rememberSkipped) RememberIgnored(text);
                return false;
            }

            return !ShouldSkipUITranslationText(context.TranslationText);
        }

        private static bool ShouldLoadCachedText(string text)
        {
            UITranslationTextContext context = BuildTextContext(text);
            return !ShouldSkipUITranslationText(context.TranslationText);
        }

        internal static string GetTranslationLookupText(string text)
        {
            return BuildTextContext(text).TranslationText;
        }

        internal static string RestoreTranslationDisplayText(string original, string translated)
        {
            UITranslationTextContext context = BuildTextContext(original);
            return context.HasLogTimestamp ? context.Prefix + translated : translated;
        }

        private static bool ShouldSkipUITranslationText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            if (trimmed.Length < 2) return true;
            if (trimmed.StartsWith("\u200B", StringComparison.Ordinal)) return true;
            if (LooksLikeVolatileReadout(trimmed)) return true;
            if (LooksLikeStructuredData(trimmed)) return true;
            if (LooksLikeRimWorldRichText(trimmed)) return true;
            if (LooksLikeModListCompositeText(trimmed)) return true;
            if (LooksLikeStackCountReadout(trimmed)) return true;
            if (LooksLikeInternalKey(trimmed)) return true;
            if (LooksLikeNumericStatusText(trimmed)) return true;
            if (LooksLikeDeveloperLogText(trimmed)) return true;

            return false;
        }

        private static string SanitizeUITranslationResult(string original, string translated)
        {
            if (string.IsNullOrWhiteSpace(translated)) return null;

            string cleaned = GetTranslationLookupText(translated).Trim().Trim('\u200B');
            cleaned = TryExtractWrappedText(cleaned) ?? cleaned;
            cleaned = cleaned.Trim();

            if (string.IsNullOrWhiteSpace(cleaned)) return null;
            if (LooksLikeStructuredData(cleaned))
            {
                string unwrapped = TryExtractKeyValueText(cleaned);
                if (string.IsNullOrWhiteSpace(unwrapped) || LooksLikeStructuredData(unwrapped.Trim())) return null;
                cleaned = unwrapped.Trim();
            }

            return LanguageDetector.NormalizeChineseVariant(cleaned, AutoTranslatorMod.Settings.TargetLang);
        }

        internal static bool TrySanitizeUIReplacementText(string original, string translated, out string sanitized)
        {
            sanitized = null;

            string lookupOriginal = GetTranslationLookupText(original);
            string cleanTranslation = SanitizeUITranslationResult(lookupOriginal, translated);
            if (string.IsNullOrWhiteSpace(cleanTranslation)) return false;
            if (string.Equals(lookupOriginal, cleanTranslation, StringComparison.Ordinal)) return false;
            if (ShouldSkipUITranslationText(cleanTranslation)) return false;

            sanitized = RestoreTranslationDisplayText(original, cleanTranslation);
            return true;
        }

        private static bool LooksLikeStructuredData(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Length == 0) return false;

            if ((trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
                || (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)))
            {
                return true;
            }

            if ((trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.EndsWith(">", StringComparison.Ordinal))
                && trimmed.IndexOf(' ', 1) > 0)
            {
                return true;
            }

            if (DataKeyValueRegex.IsMatch(trimmed)) return true;
            if (trimmed.Contains("\":") || trimmed.Contains("':")) return true;

            return false;
        }

        private static bool LooksLikeVolatileReadout(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            return VolatileMetricRegex.IsMatch(trimmed)
                || TemperatureReadoutRegex.IsMatch(trimmed);
        }

        private static bool LooksLikeStackCountReadout(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (!StackCountRegex.IsMatch(text)) return false;

            string withoutCount = System.Text.RegularExpressions.Regex.Replace(text, @"[xX×]\s*\d{1,6}\s*$", "").Trim();
            if (LanguageDetector.LooksLikeTargetLanguage(withoutCount, AutoTranslatorMod.Settings.TargetLang)) return true;

            bool hasEnglish = EnglishRegex.IsMatch(withoutCount);
            switch (AutoTranslatorMod.Settings.TargetLang)
            {
                case TargetLanguage.Traditional:
                case TargetLanguage.Simplified:
                    return CJKRegex.IsMatch(withoutCount) && !hasEnglish;
                case TargetLanguage.Japanese:
                    return (CJKRegex.IsMatch(withoutCount) || KanaRegex.IsMatch(withoutCount)) && !hasEnglish;
                case TargetLanguage.Korean:
                    return HangulRegex.IsMatch(withoutCount) && !hasEnglish;
                case TargetLanguage.Russian:
                case TargetLanguage.Ukrainian:
                    return CyrillicRegex.IsMatch(withoutCount) && !hasEnglish;
                default:
                    return false;
            }
        }

        private static bool LooksLikeInternalKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Contains(" ")) return false;
            if (trimmed.Length > 80) return false;
            return InternalKeyRegex.IsMatch(trimmed);
        }

        private static bool LooksLikeNumericStatusText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Length < 8) return false;
            int numericMatches = NumericStatusRegex.Matches(trimmed).Count;
            if (trimmed.IndexOf('\n') >= 0 && numericMatches >= 2) return true;
            if (numericMatches >= 3) return true;
            if (numericMatches >= 2 && trimmed.IndexOf('=') >= 0) return true;
            if (trimmed.IndexOf("Stack amount", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (trimmed.IndexOf("Processed ", StringComparison.OrdinalIgnoreCase) >= 0 && trimmed.IndexOf(" amount", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            return false;
        }

        private static bool LooksLikeDeveloperLogText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            string trimmed = text.Trim();
            if (trimmed.Length > 220 && (
                trimmed.IndexOf("System.", StringComparison.Ordinal) >= 0 ||
                trimmed.IndexOf("Verse.", StringComparison.Ordinal) >= 0 ||
                trimmed.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("stack trace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("Source file:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf(".xml", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            if (trimmed.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("/steamapps/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (trimmed.IndexOf("PatchOperation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                trimmed.IndexOf("Could not resolve cross-reference", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeRimWorldRichText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            return ContainsIgnoreCase(text, "<color=")
                || ContainsIgnoreCase(text, "</color>")
                || ContainsIgnoreCase(text, "<size=")
                || ContainsIgnoreCase(text, "</size>")
                || ContainsIgnoreCase(text, "<b>")
                || ContainsIgnoreCase(text, "</b>")
                || ContainsIgnoreCase(text, "<i>")
                || ContainsIgnoreCase(text, "</i>");
        }

        private static bool LooksLikeModListCompositeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf('\n') < 0) return false;

            string[] lines = text
                .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            return lines.Any(IsPackageIdLike);
        }

        private static bool IsPackageIdLike(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            if (trimmed.Length < 4 || trimmed.Length > 140) return false;
            if (!trimmed.Contains(".")) return false;
            if (trimmed.Any(char.IsWhiteSpace)) return false;
            if (!trimmed.Any(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')) return false;

            return trimmed.All(c =>
                c >= 'A' && c <= 'Z' ||
                c >= 'a' && c <= 'z' ||
                c >= '0' && c <= '9' ||
                c == '.' ||
                c == '_' ||
                c == '-');
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TryExtractWrappedText(string text)
        {
            string fromJson = TryExtractJsonObjectText(text);
            if (!string.IsNullOrWhiteSpace(fromJson)) return fromJson;

            string fromKeyValue = TryExtractKeyValueText(text);
            if (!string.IsNullOrWhiteSpace(fromKeyValue)) return fromKeyValue;

            return null;
        }

        private static string TryExtractJsonObjectText(string text)
        {
            try
            {
                if (!text.TrimStart().StartsWith("{", StringComparison.Ordinal)) return null;

                JObject obj = JObject.Parse(text);
                foreach (string key in new[] { "text", "translation", "translated", "result", "value" })
                {
                    JToken token;
                    if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out token)
                        && token.Type == JTokenType.String)
                    {
                        return token.Value<string>();
                    }
                }

                if (obj.Count == 1)
                {
                    JProperty property = obj.Properties().FirstOrDefault();
                    if (property != null && property.Value.Type == JTokenType.String)
                    {
                        return property.Value.Value<string>();
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static string TryExtractKeyValueText(string text)
        {
            var match = DataKeyValueRegex.Match(text);
            if (!match.Success) return null;

            string value = text.Substring(match.Length).Trim();
            if (value.Length == 0) return null;

            char quote = value[0];
            if (quote == '"' || quote == '\'')
            {
                value = value.Substring(1);
                int endQuote = value.IndexOf(quote);
                if (endQuote >= 0) value = value.Substring(0, endQuote);
            }

            return value.Trim();
        }
    }
}
