using System;
namespace AutoTranslator_Core
{
    // Shared mechanical validation used by the workflow, imports and manual edits.
    public static partial class AutoTranslatorScanner
    {

        // 這個方法負責嘗試執行 AcceptTranslatedValue 並回報是否成功。
        // EN: This method tries to accept translated value and reports whether it succeeded.
        internal static bool TryAcceptTranslatedValue(
            string translated,
            string sourceText,
            out string sanitized,
            out string failureReason,
            out string failureDetail)
        {
            failureReason = string.Empty;
            failureDetail = string.Empty;
            sanitized = SanitizeTranslationResult(translated, sourceText);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                failureReason = TranslationValidationReasons.EmptyResponse;
                failureDetail = "The provider returned an empty or unusable translation.";
                return false;
            }

            if (HasTranslatableTitleTagMismatch(sanitized, sourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                failureReason = TranslationValidationReasons.TitleTagMismatch;
                failureDetail = "A [title:] tag was changed or lost.";
                return false;
            }

            if (HasProtectedTokenMismatch(sanitized, sourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                failureReason = TranslationValidationReasons.ProtectedTokenMismatch;
                failureDetail = "A protected token was changed or lost.";
                return false;
            }

            if (HasFormatArgumentMismatch(sanitized, sourceText))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                failureReason = TranslationValidationReasons.FormatArgumentMismatch;
                failureDetail = "A format argument such as {0} was changed or lost.";
                return false;
            }

            if (RequiresProtectedTokenParity(sourceText) &&
                !LanguageDetector.LooksLikeTargetLanguage(sourceText, AutoTranslatorMod.Settings.TargetLang) &&
                string.Equals(sanitized, sourceText, StringComparison.Ordinal))
            {
                AddValidationStat(s => s.ProtectedTokenMismatchFallback++);
                failureReason = TranslationValidationReasons.ProtectedTokenMismatch;
                failureDetail = "The provider returned the unchanged protected-token source text.";
                return false;
            }

            if (LanguageDetector.HasWrongChineseVariant(
                    sanitized,
                    AutoTranslatorMod.Settings.TargetLang))
            {
                failureReason = TranslationValidationReasons.WrongChineseVariant;
                failureDetail = "The result uses the wrong Chinese writing variant.";
                return false;
            }

            if (TranslationResultLanguagePolicy.HasUnexpectedScriptResidual(
                    sanitized,
                    sourceText,
                    AutoTranslatorMod.Settings.TargetLang))
            {
                failureReason = TranslationValidationReasons.WrongTargetLanguage;
                failureDetail = "The result contains unexpected text from another writing system.";
                return false;
            }

            if (!TranslationResultLanguagePolicy.ShouldAccept(
                    sanitized,
                    sourceText,
                    AutoTranslatorMod.Settings.TargetLang))
            {
                if (TranslationResultLanguagePolicy.HasLikelyEnglishResidual(
                        sanitized,
                        sourceText,
                        AutoTranslatorMod.Settings.TargetLang))
                {
                    AddValidationStat(s => s.EnglishResidualFallback++);
                    failureReason = TranslationValidationReasons.EnglishResidual;
                    failureDetail = "The result still appears to contain untranslated English.";
                }
                else
                {
                    failureReason = TranslationValidationReasons.Unknown;
                    failureDetail = "The result did not pass language-quality validation.";
                }
                return false;
            }

            return true;
        }
    }
}
