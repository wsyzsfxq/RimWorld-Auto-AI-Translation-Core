using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTranslator_Core.Workflow.AI
{
    public sealed class AutoTranslatorApiModelTransport : ILanguageModelTransport
    {
        public async Task<ModelInvocationResult> InvokeAsync(
            ModelInvocationRequest request,
            CancellationToken cancellationToken)
        {
            WorkflowRawModelResponse response;
            using (TranslationUsageCoordinator.PushRequestContext(
                request.PackageId,
                request.RequestPurpose,
                request.RequestScope,
                request.SourceCharacters,
                exempt: request.IsSimulation,
                itemCount: request.ItemCount))
            {
                response = request.ToolConversation == null
                    ? await AutoTranslatorAPI.InvokeWorkflowJsonAsync(request.Prompt, cancellationToken)
                    : await AutoTranslatorAPI.InvokeWorkflowToolAsync(request.Prompt, cancellationToken, request.ToolConversation);
            }
            return new ModelInvocationResult
            {
                Content = response.Content,
                ToolCalls = response.ToolCalls,
                ToolConversation = request.ToolConversation,
                ToolItemIndexes = request.ToolItemIndexes,
                FinishReason = response.FinishReason,
                ProviderName = response.ProviderName,
                ModelName = response.ModelName,
                ActualOutputTokenLimit = response.ActualOutputTokenLimit,
                ConfiguredOutputTokenLimit = response.ConfiguredOutputTokenLimit,
                KnownModelOutputTokenLimit = response.KnownModelOutputTokenLimit,
                Usage = new ModelTokenUsage
                {
                    InputTokens = response.InputTokens ?? response.EstimatedInputTokens,
                    OutputTokens = response.OutputTokens ?? ApproximateTokenEstimator.Estimate(
                        response.Content + response.Reasoning + string.Concat(
                            response.ToolCalls.ConvertAll(call => call.Arguments))),
                    IsEstimated = response.UsageIsEstimated || !response.InputTokens.HasValue || !response.OutputTokens.HasValue
                }
            };
        }
    }
}
