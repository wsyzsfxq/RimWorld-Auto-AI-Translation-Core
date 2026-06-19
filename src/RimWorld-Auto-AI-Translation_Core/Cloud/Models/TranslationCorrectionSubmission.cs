using System;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    // EN: Payload for one in-game correction submission.
    public class TranslationCorrectionSubmission
    {
        public string ClientSubmissionId { get; set; }
        public string PackageId { get; set; }
        public string Language { get; set; }
        public string ModName { get; set; }
        public string GameVersion { get; set; }
        public string ModLastUpdated { get; set; }
        public string ScopeType { get; set; }
        public string EntryType { get; set; }
        public string EntryKey { get; set; }
        public string SourceText { get; set; }
        public string CurrentTranslation { get; set; }
        public string ProposedTranslation { get; set; }
        public string Reason { get; set; }
        public string ContributorId { get; set; }
        public string ContributorName { get; set; }
        public string QualityTier { get; set; }
        public string StatusHint { get; set; }
        public bool IsOfficialGameText { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
