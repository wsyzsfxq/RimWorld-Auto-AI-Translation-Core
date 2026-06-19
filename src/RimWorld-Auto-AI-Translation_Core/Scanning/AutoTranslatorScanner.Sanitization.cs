using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責翻譯結果清理與安全檢查。
// EN: This file sanitizes translated text and validates unsafe output.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {


        // 這個方法負責清理並標準化 翻譯Result 內容。
        // EN: This method cleans and normalizes translation result.
        private static string SanitizeTranslationResult(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated)) return translated;


            bool originalHasNewline = original.Contains("\\n") || original.Contains("\n");
            bool translatedHasNewline = translated.Contains("\\n") || translated.Contains("\n");

            if (!originalHasNewline && translatedHasNewline)
            {

                translated = translated.Replace("\\n", " ");
                translated = translated.Replace("\n", " ");
                translated = translated.Replace("\r", " ");
                AddValidationStat(s => s.NewlineFixed++);
            }


            if (original.Contains("\\n") && !translated.Contains("\\n"))
            {

                translated = translated.Replace("\r\n", "\\n");
                translated = translated.Replace("\n", "\\n");
                translated = translated.Replace("\r", "\\n");
                AddValidationStat(s => s.NewlineFixed++);
            }


            translated = translated.Trim();


            translated = System.Text.RegularExpressions.Regex.Replace(translated, @"^(?:\\n|\\r|\s)+", "");
            translated = System.Text.RegularExpressions.Regex.Replace(translated, @"(?:\\n|\\r|\s)+$", "");


            translated = translated.Replace("\\\\n", "\\n");


            translated = System.Text.RegularExpressions.Regex.Replace(
                translated,
                @" {2,}",
                " "
            );

            translated = RestoreGrammarRulePrefix(translated, original);
            translated = RestoreProtectedTokens(translated, original);
            translated = RestoreUntranslatableGrammarRule(translated, original);
            translated = LanguageDetector.NormalizeChineseVariant(translated, AutoTranslatorMod.Settings.TargetLang);

            return translated;
        }


        // 這個方法負責處理 RestoreGrammar規則Prefix 相關流程。
        // EN: This method handles restore grammar rule prefix.
        private static string RestoreGrammarRulePrefix(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated) || string.IsNullOrEmpty(original)) return translated;

            if (!TrySplitGrammarRule(original, out string originalPrefix, out _, out _)) return translated;
            int translatedArrow = translated.IndexOf("->", StringComparison.Ordinal);

            if (translatedArrow >= 0)
            {
                string translatedRight = translated.Substring(translatedArrow + 2).TrimStart();
                if (!translated.StartsWith(originalPrefix, StringComparison.Ordinal))
                {
                    AddValidationStat(s => s.RulePrefixFixed++);
                }
                return originalPrefix + translatedRight;
            }

            AddValidationStat(s => s.RulePrefixFixed++);
            return originalPrefix + translated.TrimStart();
        }


        private static string RestoreUntranslatableGrammarRule(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated) || string.IsNullOrEmpty(original)) return translated;
            return IsUntranslatableGrammarRule(original) ? original : translated;
        }


        private static bool TrySplitGrammarRule(string text, out string prefix, out string ruleName, out string rightSide)
        {
            prefix = "";
            ruleName = "";
            rightSide = "";

            if (string.IsNullOrEmpty(text)) return false;

            int arrow = text.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) return false;

            prefix = text.Substring(0, arrow + 2);
            rightSide = text.Substring(arrow + 2);

            string leftSide = text.Substring(0, arrow).Trim();
            int metadataStart = leftSide.IndexOf('(');
            ruleName = metadataStart >= 0 ? leftSide.Substring(0, metadataStart).Trim() : leftSide;
            return !string.IsNullOrEmpty(ruleName);
        }


        private static bool IsUntranslatableGrammarRule(string text)
        {
            if (!TrySplitGrammarRule(text, out _, out string ruleName, out string rightSide)) return false;
            return !ShouldTranslateGrammarRuleRightSide(ruleName, rightSide);
        }


        private static bool ShouldTranslateGrammarRuleRightSide(string ruleName, string rightSide)
        {
            string normalizedRuleName = (ruleName ?? "").Trim();
            if (normalizedRuleName.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                normalizedRuleName.Equals("middle", StringComparison.OrdinalIgnoreCase) ||
                normalizedRuleName.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string sample = NormalizeGrammarRightSideForDecision(rightSide);
            if (sample.Length == 0) return false;
            if (Regex.IsMatch(sample, @"^[A-Za-z]{1,2}-?$") && !HasEnglishSignal(sample)) return false;
            if (Regex.IsMatch(sample, @"^[A-Za-z0-9_]+$") && sample.Contains("_")) return false;

            CountResidualScripts(sample, out _, out _, out _, out _, out int latinCount, out int letterCount);
            return (letterCount >= 3 && latinCount >= 3) || HasEnglishSignal(sample);
        }


        private static string NormalizeGrammarRightSideForDecision(string rightSide)
        {
            if (string.IsNullOrWhiteSpace(rightSide)) return "";

            string sample = rightSide
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\\t", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");

            sample = Regex.Replace(sample, @"<[^>]+>", " ");
            sample = ProtectedTokenRegex.Replace(sample, " ");
            sample = Regex.Replace(sample, @"\$[A-Za-z0-9_]+|%[A-Za-z]", " ");
            sample = Regex.Replace(sample, @"[_/\\]+", " ");
            sample = Regex.Replace(sample, @"[^A-Za-z\u00C0-\u024F\s'\-]", " ");
            sample = Regex.Replace(sample, @"\s+", " ");
            return sample.Trim();
        }


        // 這個方法負責處理 RestoreProtectedTokens 相關流程。
        // EN: This method handles restore protected tokens.
        private static string RestoreProtectedTokens(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated) || string.IsNullOrEmpty(original)) return translated;

            bool missingToken = false;
            string result = translated;
            var tokens = ProtectedTokenRegex.Matches(original)
                .Cast<Match>()
                .Select(m => m.Value)
                .Distinct()
                .ToList();

            foreach (string token in tokens)
            {
                if (result.Contains(token)) continue;

                string inner = token.Substring(1, token.Length - 2);
                string[] alternatives = token[0] == '{'
                    ? new[] { "[" + inner + "]", "【" + inner + "】", "［" + inner + "］", "(" + inner + ")", "（" + inner + "）" }
                    : new[] { "{" + inner + "}", "【" + inner + "】", "［" + inner + "］", "(" + inner + ")", "（" + inner + "）" };

                foreach (string alt in alternatives)
                {
                    if (result.Contains(alt))
                    {
                        result = result.Replace(alt, token);
                        AddValidationStat(s => s.TokenFixed++);
                    }
                }

                if (!result.Contains(token))
                {
                    missingToken = true;
                }
            }

            if (missingToken && RequiresProtectedTokenParity(original))
            {
                AddValidationStat(s => s.StructureFallback++);
                return original;
            }

            return result;
        }

        private static bool HasProtectedTokenMismatch(string translated, string original)
        {
            if (string.IsNullOrEmpty(original)) return false;
            if (string.IsNullOrEmpty(translated)) return ProtectedTokenRegex.IsMatch(original);

            var tokens = ProtectedTokenRegex.Matches(original)
                .Cast<Match>()
                .GroupBy(m => m.Value)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            foreach (var pair in tokens)
            {
                int translatedCount = Regex.Matches(translated, Regex.Escape(pair.Key)).Count;
                if (translatedCount != pair.Value) return true;
            }

            return false;
        }

        private static bool HasFormatArgumentMismatch(string translated, string original)
        {
            if (string.IsNullOrEmpty(original)) return false;
            if (string.IsNullOrEmpty(translated)) return FormatArgumentRegex.IsMatch(original);

            var originalArgs = FormatArgumentRegex.Matches(original)
                .Cast<Match>()
                .GroupBy(m => m.Groups[1].Value)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            var translatedArgs = FormatArgumentRegex.Matches(translated)
                .Cast<Match>()
                .GroupBy(m => m.Groups[1].Value)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            if (originalArgs.Count != translatedArgs.Count) return true;

            foreach (var pair in originalArgs)
            {
                if (!translatedArgs.TryGetValue(pair.Key, out int count) || count != pair.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresProtectedTokenParity(string original)
        {
            return ProtectedTokenRegex.IsMatch(original ?? "") || FormatArgumentRegex.IsMatch(original ?? "");
        }


        // 這個方法負責判斷 IsStructureSensitiveText 條件是否成立。
        // EN: This method checks is structure sensitive text.
        private static bool IsStructureSensitiveText(string original)
        {
            if (string.IsNullOrEmpty(original)) return false;
            return original.Contains("->") ||
                   original.IndexOf("[INITIATOR_", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("[RECIPIENT_", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("{PAWN", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("[PAWN_", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf('{') >= 0 ||
                   original.IndexOf('[') >= 0;
        }


        // 這個方法負責處理 翻譯HasLikelyEnglishResidual 相關流程。
        // EN: This method handles translation has likely english residual.
        private static bool TranslationHasLikelyEnglishResidual(string translated, string original, bool recordStat)
        {
            if (!HasLikelyEnglishResidual(translated, original)) return false;

            if (recordStat)
            {
                AddValidationStat(s => s.EnglishResidualDetected++);
            }
            return true;
        }


        // 這個方法負責判斷 HasLikelyEnglishResidual 條件是否成立。
        // EN: This method checks has likely english residual.
        private static bool HasLikelyEnglishResidual(string translated, string original)
        {
            TargetLanguage targetLang = AutoTranslatorMod.Settings.TargetLang;
            if (targetLang == TargetLanguage.English) return false;

            string sample = NormalizeResidualLanguageSample(translated);
            string sourceSample = NormalizeResidualLanguageSample(original);
            if ((sample.Length < 2 || sourceSample.Length < 2) && !(HasEnglishSignal(sample) && HasEnglishSignal(sourceSample))) return false;
            if (!HasTranslatableLatinSource(sourceSample)) return false;

            CountResidualScripts(sample, out int hanCount, out int kanaCount, out int hangulCount, out int cyrillicCount, out int latinCount, out int letterCount);
            bool shortEnglishSignal = HasEnglishSignal(sample) && HasEnglishSignal(sourceSample);
            if ((letterCount < 3 || latinCount < 3) && !shortEnglishSignal) return false;
            if (IsShortUppercaseToken(sample)) return false;

            bool unchanged = string.Equals(sample, sourceSample, StringComparison.OrdinalIgnoreCase);
            bool targetPresent = LanguageDetector.LooksLikeTargetLanguage(sample, targetLang);
            int latinPercent = Percent(latinCount, letterCount);

            if (IsLatinTargetLanguage(targetLang))
            {
                return unchanged && HasEnglishSignal(sample);
            }

            if (unchanged && (latinPercent >= 70 || HasEnglishSignal(sample)))
            {
                return true;
            }

            if (targetPresent && latinPercent < 45)
            {
                return false;
            }

            if (!targetPresent && latinPercent >= 80)
            {
                return true;
            }

            return latinPercent >= 65 && HasEnglishSignal(sample);
        }


        // 這個方法負責清理並標準化 Residual語言Sample 內容。
        // EN: This method cleans and normalizes residual language sample.
        private static string NormalizeResidualLanguageSample(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            if (TrySplitGrammarRule(text, out _, out string ruleName, out string rightSide))
            {
                if (!ShouldTranslateGrammarRuleRightSide(ruleName, rightSide)) return "";
                text = rightSide;
            }

            string sample = text
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\\t", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Replace("\t", " ");

            sample = Regex.Replace(sample, @"<[^>]+>", " ");
            sample = ProtectedTokenRegex.Replace(sample, " ");
            sample = Regex.Replace(sample, @"\$[A-Za-z0-9_]+|%[A-Za-z]", " ");
            sample = Regex.Replace(sample, @"https?://\S+|[A-Za-z]:[\\/]\S+|[A-Za-z0-9_\-./\\]+\.(?:png|jpg|jpeg|dds|tex|wav|mp3|ogg|xml|txt|dll)\b", " ");
            sample = Regex.Replace(sample, @"[_/\\]+", " ");
            sample = Regex.Replace(sample, @"\s+", " ");
            return sample.Trim();
        }


        // 這個方法負責判斷 HasTranslatableLatinSource 條件是否成立。
        // EN: This method checks has translatable latin source.
        private static bool HasTranslatableLatinSource(string sample)
        {
            CountResidualScripts(sample, out _, out _, out _, out _, out int latinCount, out int letterCount);
            if (letterCount < 3 || latinCount < 3) return HasEnglishSignal(sample);
            if (Regex.IsMatch(sample, @"^[A-Z0-9 .'\-]{2,6}$") && sample.ToUpperInvariant() == sample) return false;
            return true;
        }


        // 這個方法負責判斷 HasEnglishSignal 條件是否成立。
        // EN: This method checks has english signal.
        private static bool HasEnglishSignal(string sample)
        {
            if (string.IsNullOrWhiteSpace(sample)) return false;
            if (Regex.IsMatch(sample, @"(?<!\p{L})(the|and|for|with|from|this|that|your|you|not|can|will|when|while|after|before|into|has|have|are|was|were|is|of|to|in|on|a|an)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }

            return Regex.IsMatch(sample, @"^[A-Za-z][A-Za-z '\-]{2,}$");
        }


        // 這個方法負責判斷 IsLatin目標語言 條件是否成立。
        // EN: This method checks is latin target language.
        private static bool IsLatinTargetLanguage(TargetLanguage targetLang)
        {
            return targetLang == TargetLanguage.French ||
                   targetLang == TargetLanguage.German ||
                   targetLang == TargetLanguage.Spanish ||
                   targetLang == TargetLanguage.Italian ||
                   targetLang == TargetLanguage.Polish ||
                   targetLang == TargetLanguage.Portuguese ||
                   targetLang == TargetLanguage.Turkish;
        }


        // 這個方法負責判斷 IsShortUppercaseToken 條件是否成立。
        // EN: This method checks is short uppercase token.
        private static bool IsShortUppercaseToken(string sample)
        {
            return Regex.IsMatch(sample, @"^[A-Z0-9]{2,6}$") && sample.ToUpperInvariant() == sample;
        }


        // 這個方法負責處理 CountResidualScripts 相關流程。
        // EN: This method handles count residual scripts.
        private static void CountResidualScripts(string text, out int hanCount, out int kanaCount, out int hangulCount, out int cyrillicCount, out int latinCount, out int letterCount)
        {
            hanCount = 0;
            kanaCount = 0;
            hangulCount = 0;
            cyrillicCount = 0;
            latinCount = 0;
            letterCount = 0;

            if (string.IsNullOrEmpty(text)) return;

            foreach (char c in text)
            {
                if (!char.IsLetter(c)) continue;
                letterCount++;

                if (IsHan(c)) hanCount++;
                else if (IsKana(c)) kanaCount++;
                else if (IsHangul(c)) hangulCount++;
                else if (IsCyrillic(c)) cyrillicCount++;
                else if (IsLatin(c)) latinCount++;
            }
        }


        // 這個方法負責判斷 IsHan 條件是否成立。
        // EN: This method checks is han.
        private static bool IsHan(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF')
                || (c >= '\u4E00' && c <= '\u9FFF')
                || (c >= '\uF900' && c <= '\uFAFF');
        }


        // 這個方法負責判斷 IsKana 條件是否成立。
        // EN: This method checks is kana.
        private static bool IsKana(char c)
        {
            return (c >= '\u3040' && c <= '\u30FF')
                || (c >= '\u31F0' && c <= '\u31FF')
                || (c >= '\uFF66' && c <= '\uFF9F');
        }


        // 這個方法負責判斷 IsHangul 條件是否成立。
        // EN: This method checks is hangul.
        private static bool IsHangul(char c)
        {
            return (c >= '\u1100' && c <= '\u11FF')
                || (c >= '\u3130' && c <= '\u318F')
                || (c >= '\uAC00' && c <= '\uD7AF');
        }


        // 這個方法負責判斷 IsCyrillic 條件是否成立。
        // EN: This method checks is cyrillic.
        private static bool IsCyrillic(char c)
        {
            return (c >= '\u0400' && c <= '\u04FF')
                || (c >= '\u0500' && c <= '\u052F');
        }


        // 這個方法負責判斷 IsLatin 條件是否成立。
        // EN: This method checks is latin.
        private static bool IsLatin(char c)
        {
            return (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '\u00C0' && c <= '\u024F');
        }


        // 這個方法負責處理 Percent 相關流程。
        // EN: This method handles percent.
        private static int Percent(int part, int total)
        {
            if (total <= 0) return 0;
            return (int)((part * 100.0) / total);
        }
    }
}
