using System;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AutoTranslator_Core.Workflow
{
    public static class WorkflowIdentity
    {
        public const string CandidateIdentitySchemaVersion = "atc-candidate-v1";
        // xml-2 stores the frozen v3.0 XML candidate corpus in the V4 database and
        // invalidates results produced by the earlier V4 all-leaf analyzer.
        public const string XmlAnalyzerVersion = "xml-2";
        public const string DllAnalyzerVersion = "dll-3.03";
        public const string AiReviewVersion = "ai-review-3";
        private const string CandidateIdPrefix = "atc1_";

        public static string CreateCandidateId(
            string modIdentity,
            CandidateSourceDomain sourceDomain,
            string entryKind,
            string logicalLocator)
        {
            StringBuilder canonical = new StringBuilder();
            AppendCanonicalField(canonical, "version", CandidateIdentitySchemaVersion);
            AppendCanonicalField(canonical, "modIdentity", NormalizeIdentityPart(modIdentity));
            AppendCanonicalField(canonical, "sourceDomain", sourceDomain.ToString().ToLowerInvariant());
            AppendCanonicalField(canonical, "entryKind", NormalizeSemanticPart(entryKind));
            AppendCanonicalField(canonical, "logicalLocator", NormalizeLocator(logicalLocator));
            return CandidateIdPrefix + Sha256(canonical.ToString());
        }

        public static string CreateReadableEntryIdentity(
            CandidateSourceDomain sourceDomain,
            string entryKind,
            string logicalLocator)
        {
            return sourceDomain.ToString().ToLowerInvariant() + ":" +
                   NormalizeSemanticPart(entryKind) + ":" + NormalizeLocator(logicalLocator);
        }

        public static string CreateModIdentity(
            string packageId,
            string author,
            string displayName,
            string fallbackRootName)
        {
            if (!string.IsNullOrWhiteSpace(packageId))
                return "pkg:" + NormalizeIdentityPart(packageId);

            StringBuilder fallback = new StringBuilder();
            AppendCanonicalField(fallback, "author", NormalizeIdentityPart(author));
            AppendCanonicalField(fallback, "displayName", NormalizeIdentityPart(displayName));
            AppendCanonicalField(fallback, "rootName", NormalizeIdentityPart(fallbackRootName));
            string fallbackMaterial = fallback.ToString();
            return "local:" + Sha256(fallbackMaterial);
        }

        public static string HashText(string value)
        {
            return Sha256(NormalizeNewLines(value ?? string.Empty));
        }

        public static string CreateContentFingerprint(string sourceText, string context)
        {
            return Sha256(NormalizeNewLines(sourceText ?? string.Empty) + "\n" + NormalizeNewLines(context ?? string.Empty));
        }

        public static string CreateAnalysisFingerprint(
            string analyzerVersion,
            string modVersionFingerprint,
            string settingsFingerprint)
        {
            return Sha256(string.Join("\n", new[]
            {
                NormalizeIdentityPart(analyzerVersion),
                NormalizeIdentityPart(modVersionFingerprint),
                NormalizeNewLines(settingsFingerprint ?? string.Empty)
            }));
        }

        public static string CreateAnalysisResultFingerprint(
            IEnumerable<CandidateRecord> candidates)
        {
            StringBuilder material = new StringBuilder();
            foreach (CandidateRecord candidate in (candidates ?? Enumerable.Empty<CandidateRecord>())
                         .Where(candidate => candidate != null)
                         .OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal))
            {
                AppendCanonicalField(material, "candidateId", candidate.CandidateId);
                AppendCanonicalField(material, "sourceHash", candidate.SourceTextHash);
                AppendCanonicalField(material, "contentFingerprint", candidate.ContentFingerprint);
                AppendCanonicalField(
                    material, "classificationFlags", candidate.ClassificationFlags.ToString());
                AppendCanonicalField(material, "contextHash", HashText(candidate.ContextJson));
                AppendCanonicalField(material, "outputPath", candidate.DefaultOutputFileRelativePath);
                AppendCanonicalField(material, "outputKey", candidate.TranslationEntryKey);
                AppendCanonicalField(material, "xmlReason", candidate.XmlReasonCode);
                AppendCanonicalField(material, "dllReason", candidate.DllReasonCode);
            }
            return Sha256(material.ToString());
        }

        public static bool IsTranslationCurrent(CandidateRecord candidate)
        {
            if (candidate == null || candidate.TranslationState != CandidateTranslationState.Translated) return false;
            return !string.IsNullOrEmpty(candidate.SourceTextHash) &&
                   string.Equals(candidate.SourceTextHash, candidate.SourceTextHashAtTranslation, StringComparison.Ordinal);
        }

        public static string CreateAiReviewFingerprint(CandidateRecord candidate)
        {
            if (candidate == null) return string.Empty;
            return HashText(
                AiReviewVersion + "\n" + candidate.CandidateId + "\n" +
                candidate.SourceTextHash + "\n" + candidate.LogicalLocator + "\n" +
                candidate.ContextJson);
        }

        public static bool IsAiReviewCurrent(CandidateRecord candidate)
        {
            if (candidate == null ||
                ClassificationFlagsCodec.Get(
                    candidate.ClassificationFlags, ClassificationLayer.AiReview) ==
                CandidateClassification.NotAnalyzed)
                return false;
            return string.Equals(
                       candidate.AiReviewVersion, AiReviewVersion, StringComparison.Ordinal) &&
                   string.Equals(
                       candidate.AiReviewFingerprint,
                       CreateAiReviewFingerprint(candidate),
                       StringComparison.Ordinal);
        }

        private static string NormalizeLocator(string value)
        {
            return NormalizeSemanticPart((value ?? string.Empty).Replace('\\', '/'));
        }

        private static string NormalizeIdentityPart(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormC);
        }

        private static string NormalizeSemanticPart(string value)
        {
            return (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
        }

        private static void AppendCanonicalField(StringBuilder output, string name, string value)
        {
            value = value ?? string.Empty;
            int byteLength = Encoding.UTF8.GetByteCount(value);
            output.Append(name.Length).Append(':').Append(name)
                .Append('=').Append(byteLength).Append(':').Append(value).Append(';');
        }

        private static string NormalizeNewLines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n').Normalize(NormalizationForm.FormC);
        }

        private static string Sha256(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder output = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) output.Append(hash[i].ToString("x2"));
                return output.ToString();
            }
        }
    }
}
