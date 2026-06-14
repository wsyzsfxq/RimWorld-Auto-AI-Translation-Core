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

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {

        /// <summary>
        /// 智慧清理 AI 翻譯結果
        /// 規則：
        /// 1. 如果原文沒有 \n，但翻譯結果有 → 移除（AI 自作多情加的）
        /// 2. 如果原文有 \n，翻譯結果也要保留（不動）
        /// 3. 清掉開頭/結尾多餘的空白與換行
        /// 4. 把字面字元 \\n 統一為單一 \n（避免雙重轉義）
        /// </summary>
        private static string SanitizeTranslationResult(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated)) return translated;

            // 規則 1：原文沒 \n，翻譯不該有
            bool originalHasNewline = original.Contains("\\n") || original.Contains("\n");
            bool translatedHasNewline = translated.Contains("\\n") || translated.Contains("\n");

            if (!originalHasNewline && translatedHasNewline)
            {
                // AI 亂加的，移除所有 \n
                translated = translated.Replace("\\n", " ");
                translated = translated.Replace("\n", " ");
                translated = translated.Replace("\r", " ");
                AddValidationStat(s => s.NewlineFixed++);
            }

            // 規則 2：處理 AI 把 \n 寫成真實換行的情況
            // 如果原文是 "\n"（字面兩字元），AI 可能回成真實換行
            if (original.Contains("\\n") && !translated.Contains("\\n"))
            {
                // 把翻譯結果中的真實換行還原成字面 \n
                translated = translated.Replace("\r\n", "\\n");
                translated = translated.Replace("\n", "\\n");
                translated = translated.Replace("\r", "\\n");
                AddValidationStat(s => s.NewlineFixed++);
            }

            // 規則 3：清掉首尾空白
            translated = translated.Trim();

            // ✨ 咪咪的黑洞消除術：AI 常常自作聰明在結尾加上好幾個 \n，導致遊戲內出現超大片黑色空白！
            // 這裡用正則表達式，把開頭跟結尾的字面換行符號 (\n, \r) 徹底切除！
            translated = System.Text.RegularExpressions.Regex.Replace(translated, @"^(?:\\n|\\r|\s)+", "");
            translated = System.Text.RegularExpressions.Regex.Replace(translated, @"(?:\\n|\\r|\s)+$", "");

            // 規則 4：避免雙重轉義（\\\\n → \\n）
            translated = translated.Replace("\\\\n", "\\n");

            // 規則 5：合併連續多個空白為單一空白（中文不需要連續空白）
            translated = System.Text.RegularExpressions.Regex.Replace(
                translated,
                @" {2,}",
                " "
            );

            translated = RestoreGrammarRulePrefix(translated, original);
            translated = RestoreProtectedTokens(translated, original);
            translated = LanguageDetector.NormalizeChineseVariant(translated, AutoTranslatorMod.Settings.TargetLang);

            return translated;
        }


        private static string RestoreGrammarRulePrefix(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated) || string.IsNullOrEmpty(original)) return translated;

            int originalArrow = original.IndexOf("->", StringComparison.Ordinal);
            if (originalArrow < 0) return translated;

            string originalPrefix = original.Substring(0, originalArrow + 2);
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

            if (missingToken && IsStructureSensitiveText(original))
            {
                AddValidationStat(s => s.StructureFallback++);
                return original;
            }

            return result;
        }


        private static bool IsStructureSensitiveText(string original)
        {
            if (string.IsNullOrEmpty(original)) return false;
            return original.Contains("->") ||
                   original.IndexOf("[INITIATOR_", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("[RECIPIENT_", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("{PAWN", StringComparison.Ordinal) >= 0 ||
                   original.IndexOf("[PAWN_", StringComparison.Ordinal) >= 0;
        }


        private static bool TranslationHasLikelyEnglishResidual(string translated, string original, bool recordStat)
        {
            if (!HasLikelyEnglishResidual(translated, original)) return false;

            if (recordStat)
            {
                AddValidationStat(s => s.EnglishResidualDetected++);
            }
            return true;
        }


        private static bool HasLikelyEnglishResidual(string translated, string original)
        {
            TargetLanguage targetLang = AutoTranslatorMod.Settings.TargetLang;
            if (targetLang == TargetLanguage.English) return false;

            string sample = NormalizeResidualLanguageSample(translated);
            string sourceSample = NormalizeResidualLanguageSample(original);
            if (sample.Length < 2 || sourceSample.Length < 2) return false;
            if (!HasTranslatableLatinSource(sourceSample)) return false;

            CountResidualScripts(sample, out int hanCount, out int kanaCount, out int hangulCount, out int cyrillicCount, out int latinCount, out int letterCount);
            if (letterCount < 3 || latinCount < 3) return false;
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


        private static string NormalizeResidualLanguageSample(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

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


        private static bool HasTranslatableLatinSource(string sample)
        {
            CountResidualScripts(sample, out _, out _, out _, out _, out int latinCount, out int letterCount);
            if (letterCount < 3 || latinCount < 3) return false;
            if (Regex.IsMatch(sample, @"^[A-Z0-9 .'\-]{2,6}$") && sample.ToUpperInvariant() == sample) return false;
            return true;
        }


        private static bool HasEnglishSignal(string sample)
        {
            if (string.IsNullOrWhiteSpace(sample)) return false;
            if (Regex.IsMatch(sample, @"(?<!\p{L})(the|and|for|with|from|this|that|your|you|not|can|will|when|while|after|before|into|has|have|are|was|were|is|of|to|in|on)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }

            return Regex.IsMatch(sample, @"^[A-Za-z][A-Za-z '\-]{2,}$");
        }


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


        private static bool IsShortUppercaseToken(string sample)
        {
            return Regex.IsMatch(sample, @"^[A-Z0-9]{2,6}$") && sample.ToUpperInvariant() == sample;
        }


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


        private static bool IsHan(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF')
                || (c >= '\u4E00' && c <= '\u9FFF')
                || (c >= '\uF900' && c <= '\uFAFF');
        }


        private static bool IsKana(char c)
        {
            return (c >= '\u3040' && c <= '\u30FF')
                || (c >= '\u31F0' && c <= '\u31FF')
                || (c >= '\uFF66' && c <= '\uFF9F');
        }


        private static bool IsHangul(char c)
        {
            return (c >= '\u1100' && c <= '\u11FF')
                || (c >= '\u3130' && c <= '\u318F')
                || (c >= '\uAC00' && c <= '\uD7AF');
        }


        private static bool IsCyrillic(char c)
        {
            return (c >= '\u0400' && c <= '\u04FF')
                || (c >= '\u0500' && c <= '\u052F');
        }


        private static bool IsLatin(char c)
        {
            return (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '\u00C0' && c <= '\u024F');
        }


        private static int Percent(int part, int total)
        {
            if (total <= 0) return 0;
            return (int)((part * 100.0) / total);
        }
    }
}
