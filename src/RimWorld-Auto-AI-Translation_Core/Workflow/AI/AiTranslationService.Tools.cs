using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTranslator_Core.Workflow.AI
{
    internal sealed class TranslationCorrectionContext
    {
        public WorkflowToolConversation Conversation;
        public List<CandidateRecord> Candidates;
        public List<int> ItemIndexes;
    }

    internal sealed partial class AiTranslationService
    {
        private AiTranslationApplicationSummary ApplyTranslationOutput(
            IList<CandidateRecord> input,
            ModelInvocationResult modelResult,
            string targetLanguage,
            AiStepEstimate estimate,
            string aiRunId,
            AiWorkflowStageReporter reporter,
            int batchIndex,
            bool persistValidationFailures)
        {
            if (modelResult?.ToolConversation == null)
                throw new InvalidOperationException("Translation requires a tool conversation.");

            WorkflowToolConversation conversation = modelResult.ToolConversation;
            TranslationSubmissionTool.ValidatePendingCalls(conversation);
            IList<int> indexes = modelResult.ToolItemIndexes ?? Enumerable.Range(0, input.Count).ToList();
            if (indexes.Count != input.Count || indexes.Distinct().Count() != input.Count)
                throw new InvalidOperationException("Translation tool input index mapping is inconsistent.");
            TranslationToolArguments arguments = modelResult.ToolCalls.Count == 1
                ? TranslationSubmissionTool.ParseArguments(modelResult.ToolCalls[0], indexes)
                : TranslationSubmissionTool.RejectAll(indexes,
                    "Call submit_translation_results exactly once with all requested items.");
            List<CandidateRecord> validInput = new List<CandidateRecord>();
            List<string> translatedValues = new List<string>();
            for (int i = 0; i < input.Count; i++)
            {
                if (!arguments.Translations.TryGetValue(indexes[i], out string translated)) continue;
                translatedValues.Add(translated);
                validInput.Add(input[i]);
            }

            // Only the tool invokes the existing validator/save transaction. Protocol parsing
            // never writes data, and rejected arguments never become translation text.
            AiTranslationApplicationSummary applied = validInput.Count == 0
                ? new AiTranslationApplicationSummary()
                : ApplyTranslationOutputCore(validInput, translatedValues, modelResult,
                    targetLanguage, estimate, aiRunId, reporter, batchIndex, persistValidationFailures);

            for (int i = 0; i < input.Count; i++)
            {
                if (!arguments.Errors.TryGetValue(indexes[i], out string error)) continue;
                CandidateRecord candidate = input[i];
                applied.RejectedCandidates.Add(candidate);
                applied.RejectionReasons[candidate.CandidateId] = error;
                applied.ValidationRejectedCount++;
                if (persistValidationFailures)
                {
                    _repository.SetTranslationValidationFailure(candidate.CandidateId, targetLanguage,
                        error, string.Empty, modelResult.ProviderName, modelResult.ModelName,
                        WorkflowIdentity.AiTranslationPromptVersion, aiRunId, batchIndex);
                    AiReviewService.AddStepError(estimate, candidate.ModIdentity, error);
                }
            }
            JArray accepted = new JArray();
            JArray rejected = new JArray();
            for (int i = 0; i < input.Count; i++)
            {
                if (applied.RejectionReasons.TryGetValue(input[i].CandidateId, out string error))
                    rejected.Add(new JObject { ["itemIndex"] = indexes[i], ["error"] = error });
                else accepted.Add(indexes[i]);
            }
            JObject receipt = new JObject
            {
                ["success"] = rejected.Count == 0,
                ["savedItemIndexes"] = accepted,
                ["errors"] = rejected,
                ["instruction"] = "Saved items are complete and must not be resubmitted. Correct only the items requested in the next message."
            };
            TranslationSubmissionTool.RecordResults(conversation,
                modelResult.ToolCalls.Select(call => (JObject)receipt.DeepClone()).ToList());
            return applied;
        }

        private static TranslationCorrectionContext CreateCorrectionContext(
            WorkflowToolConversation conversation, IList<CandidateRecord> original,
            IList<CandidateRecord> rejected)
        {
            List<int> indexes = rejected.Select(candidate => original.IndexOf(candidate)).ToList();
            return new TranslationCorrectionContext
            {
                Candidates = rejected.ToList(),
                ItemIndexes = indexes,
                Conversation = conversation.ForkForCorrection(
                    "Correct ONLY itemIndexes " + string.Join(",", indexes) +
                    " using the preceding tool errors. Keep these original itemIndexes. " +
                    "Submit all these items in one submit_translation_results call. " +
                    "This is the final correction attempt; do not resubmit any other item.")
            };
        }
    }
}
