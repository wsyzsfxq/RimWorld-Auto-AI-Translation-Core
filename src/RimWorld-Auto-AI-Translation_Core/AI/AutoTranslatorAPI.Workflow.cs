using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoTranslator_Core.TranslationPolicy;
using System.Linq;
using System.Collections.Generic;

namespace AutoTranslator_Core
{
    internal sealed class WorkflowRawModelResponse
    {
        public string Content = string.Empty;
        public string Reasoning = string.Empty;
        public JObject NativeAssistant;
        public List<WorkflowToolCall> ToolCalls = new List<WorkflowToolCall>();
        public long? InputTokens;
        public long? OutputTokens;
        public string FinishReason = string.Empty;
        public string ProviderName = string.Empty;
        public string ModelName = string.Empty;
        public int ConfiguredOutputTokenLimit;
        public int? KnownModelOutputTokenLimit;
        public int ActualOutputTokenLimit;
        public long EstimatedInputTokens;
        public bool UsageIsEstimated;
    }

    public static partial class AutoTranslatorAPI
    {
        internal static Task<WorkflowRawModelResponse> InvokeWorkflowJsonAsync(
            string prompt, CancellationToken cancellationToken, ApiKeyConfig selectedConfiguration = null)
        {
            return InvokeWorkflowRequestAsync(prompt, cancellationToken, selectedConfiguration, null);
        }

        internal static async Task<WorkflowRawModelResponse> InvokeWorkflowToolAsync(
            string prompt, CancellationToken cancellationToken, WorkflowToolConversation conversation)
        {
            if (conversation == null) throw new ArgumentNullException(nameof(conversation));
            ApiKeyConfig initial = conversation.Configuration ?? GetNextWorkflowConfig();
            if (initial == null) throw new InvalidOperationException("No enabled API configuration is available.");
            List<ApiKeyConfig> routes = new List<ApiKeyConfig> { initial };
            routes.AddRange(GetEligibleWorkflowConfigs().Where(config => !ReferenceEquals(config, initial)));
            List<string> failures = new List<string>();
            long priorInput = 0;
            long priorOutput = 0;
            bool priorEstimated = false;
            foreach (ApiKeyConfig route in routes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AutoTranslatorSettings.IsCancellationRequested) throw new OperationCanceledException();
                if (conversation.Configuration != null && !ReferenceEquals(conversation.Configuration, route))
                    conversation.SwitchRoute(route);
                try
                {
                    WorkflowRawModelResponse result = await InvokeWorkflowRequestAsync(prompt, cancellationToken, route, conversation);
                    if (priorInput != 0 || priorOutput != 0)
                    {
                        result.UsageIsEstimated = priorEstimated || !result.InputTokens.HasValue || !result.OutputTokens.HasValue;
                        result.InputTokens = priorInput + (result.InputTokens ?? result.EstimatedInputTokens);
                        result.OutputTokens = priorOutput + (result.OutputTokens ?? EstimateWorkflowOutput(result));
                    }
                    return result;
                }
                catch (WorkflowModelRequestException ex)
                {
                    failures.Add(route.Provider + " / " + route.SelectedModel + ": " + ex.Message);
                    if (ex.Response != null)
                    {
                        priorInput += ex.Response.InputTokens ?? ex.Response.EstimatedInputTokens;
                        priorOutput += ex.Response.OutputTokens ?? EstimateWorkflowOutput(ex.Response);
                        priorEstimated |= !ex.Response.InputTokens.HasValue || !ex.Response.OutputTokens.HasValue;
                    }
                    if (!ex.CanTryConfiguredFallback) throw;
                    AutoTranslatorSettings.AddWarningLog("工具模型请求失败：" + failures[failures.Count - 1]);
                }
            }
            throw new InvalidOperationException("已配置的同档位模型均未能完成工具请求：" +
                string.Join("；", failures) + "。请检查 API 配置及模型工具调用支持。");
        }

        private static long EstimateWorkflowOutput(WorkflowRawModelResponse response) =>
            Workflow.AI.ApproximateTokenEstimator.Estimate(response.Content + response.Reasoning +
                string.Concat(response.ToolCalls.Select(call => call.Arguments)));

        private static async Task<WorkflowRawModelResponse> InvokeWorkflowRequestAsync(
            string prompt,
            CancellationToken cancellationToken,
            ApiKeyConfig selectedConfiguration = null,
            WorkflowToolConversation toolConversation = null)
        {
            ApiKeyConfig config = toolConversation?.Configuration ?? selectedConfiguration ?? GetNextWorkflowConfig();
            if (toolConversation != null) toolConversation.Configuration = config;
            if (config == null) throw new InvalidOperationException("No enabled API configuration is available.");
            if (!IsConfigReady(config))
                throw new InvalidOperationException("The selected API configuration is incomplete or disabled.");
            if (config.Provider == TranslatorProvider.DeepL)
                throw new InvalidOperationException("DeepL is a translation service and cannot execute the workflow JSON model protocol.");

            string apiKey = CleanInput(config.Key);
            string model = CleanInput(config.SelectedModel);
            int configuredOutputTokenLimit = Math.Max(1, config.AtcMaxOutputTokens);
            int? knownModelOutputTokenLimit = config.GetKnownOutputTokenLimit(model);
            int maximumOutputTokens = config.ResolveActualOutputTokenLimit(model);
            AutoTranslatorSettings.AddLog(
                "模型请求：提供方 " + config.Provider + "，模型 " + model +
                "，ATC 上限 " + configuredOutputTokenLimit +
                "，模型能力 " + (knownModelOutputTokenLimit.HasValue
                    ? knownModelOutputTokenLimit.Value.ToString()
                    : "未知") + "，实际请求上限 " + maximumOutputTokens);
            AutoTranslatorSettings.AddDebugLog(
                "workflow.model output_limit provider=" + config.Provider +
                " model=" + model + " configured_limit=" + configuredOutputTokenLimit +
                " known_model_limit=" + (knownModelOutputTokenLimit?.ToString() ?? "unknown") +
                " actual_limit=" + maximumOutputTokens);
            string baseUrl = GetBaseUrl(config).TrimEnd('/');
            toolConversation?.BindRoute(config, model, baseUrl, apiKey);
            IWorkflowProtocol protocol = WorkflowProtocolRegistry.Resolve(config.Provider);
            string url = protocol.BuildUrl(baseUrl, model, apiKey);
            JObject payload = toolConversation == null
                ? protocol.BuildJsonRequest(config, model, prompt, maximumOutputTokens)
                : protocol.BuildToolRequest(config, model, prompt, maximumOutputTokens, toolConversation);

            int timeoutSeconds = WorkflowRequestTimeout.Resolve(
                AutoTranslatorMod.Settings != null ? AutoTranslatorMod.Settings.TimeoutSeconds : 120);
            AutoTranslatorSettings.AddLog(
                "模型请求参数：响应超时 " + timeoutSeconds + " 秒；超过该时间仍未收到完整响应将判定为超时");
            Func<bool> cancelled = () => cancellationToken.IsCancellationRequested ||
                                         AutoTranslatorSettings.IsCancellationRequested;
            string lastFinishReason = string.Empty;
            int maximumStructuredAttempts = toolConversation == null ? 3 : 2;
            for (int structuredAttempt = 0; structuredAttempt < maximumStructuredAttempts; structuredAttempt++)
            {
                System.Diagnostics.Stopwatch requestTimer = System.Diagnostics.Stopwatch.StartNew();
                ATC_WebResponse response = await SendTranslationRequestWithConcurrencyRecoveryAsync(
                    () => SendJsonRequestAttemptAsync(
                        url, payload.ToString(Newtonsoft.Json.Formatting.None), apiKey,
                        config.Provider, timeoutSeconds, cancelled),
                    config,
                    cancelled);
                requestTimer.Stop();
                cancellationToken.ThrowIfCancellationRequested();
                if (response == null || !response.IsSuccess)
                {
                    if (toolConversation != null)
                    {
                        if (cancelled()) throw new OperationCanceledException();
                        if (structuredAttempt == 0 && WorkflowModelRecovery.TryPrepareCompatibilityRetry(response, payload))
                        {
                            AutoTranslatorSettings.AddLog("工具协议兼容参数已调整，本模型重试一次。");
                            continue;
                        }
                        throw WorkflowModelRecovery.Failure(response);
                    }
                    if (response != null && response.FailureKind == TranslationRequestFailureKind.ResponseTimeout)
                    {
                        int effectiveTimeoutSeconds = response.TimeoutSeconds > 0
                            ? response.TimeoutSeconds
                            : timeoutSeconds;
                        throw new InvalidOperationException(
                            "模型请求超时：配置超时时间=" + effectiveTimeoutSeconds +
                            " 秒；本次调用累计等待=" + requestTimer.ElapsedMilliseconds +
                            " 毫秒；超时阶段=" +
                            (string.IsNullOrWhiteSpace(response.FailureStage)
                                ? "WaitingResponse"
                                : response.FailureStage) +
                            "；底层错误=" +
                            (string.IsNullOrWhiteSpace(response.ErrorText)
                                ? "Request timeout"
                                : response.ErrorText));
                    }

                    throw new InvalidOperationException(response?.ErrorText ?? "No model response was returned.");
                }

                WorkflowRawModelResponse result;
                try
                {
                    JObject envelope = JObject.Parse(response.ResponseBody ?? string.Empty);
                    result = protocol.ParseResponse(envelope);
                }
                catch (Exception ex) when (toolConversation != null &&
                    (ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException || ex is ArgumentException))
                {
                    throw new WorkflowModelRequestException("服务商响应不符合所选工具协议。", true);
                }
                result.EstimatedInputTokens = Workflow.AI.ApproximateTokenEstimator.Estimate(
                    payload.ToString(Newtonsoft.Json.Formatting.None));
                lastFinishReason = result.FinishReason;
                result.FinishReason = lastFinishReason;
                result.ProviderName = config.Provider.ToString();
                result.ModelName = model;
                result.ConfiguredOutputTokenLimit = configuredOutputTokenLimit;
                result.KnownModelOutputTokenLimit = knownModelOutputTokenLimit;
                result.ActualOutputTokenLimit = maximumOutputTokens;
                if (toolConversation != null)
                {
                    if (!IsWorkflowLengthTermination(result.FinishReason) && result.ToolCalls.Count == 0 &&
                        string.Equals(result.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
                        throw new WorkflowModelRequestException("模型未返回要求的工具调用，可能不支持该工具协议。", true, result);
                    toolConversation.PendingAssistant = result.NativeAssistant;
                    toolConversation.PendingCalls = new List<WorkflowToolCall>(result.ToolCalls);
                    return result;
                }
                if (IsWorkflowLengthTermination(lastFinishReason)) return result;
                if (!string.IsNullOrWhiteSpace(result.Content)) return result;

                AutoTranslatorSettings.AddDebugLog(
                    "workflow.model empty-content provider=" + config.Provider +
                    " model=" + model + " finishReason=" + lastFinishReason +
                    " attempt=" + (structuredAttempt + 1));
                if (structuredAttempt < maximumStructuredAttempts - 1)
                    protocol.PrepareEmptyJsonRetry(
                        payload,
                        config,
                        removeResponseFormat: structuredAttempt == 1);
            }

            throw new InvalidOperationException(
                "The model returned an empty structured response" +
                (string.IsNullOrWhiteSpace(lastFinishReason)
                    ? "."
                    : " (finish reason: " + lastFinishReason + ")."));
        }

        private static bool IsWorkflowLengthTermination(string finishReason)
        {
            string value = (finishReason ?? string.Empty).Trim()
                .Replace('-', '_').Replace(' ', '_').ToUpperInvariant();
            return value == "LENGTH" || value == "MAX_TOKENS" ||
                   value == "MAX_OUTPUT_TOKENS" || value == "TOKEN_LIMIT" ||
                   (value.Contains("TOKEN") &&
                    (value.Contains("MAX") || value.Contains("LIMIT")));
        }

        private static List<ApiKeyConfig> GetEligibleWorkflowConfigs()
        {
            if (AutoTranslatorMod.Settings?.ApiConfigs == null) return new List<ApiKeyConfig>();
            var ready = AutoTranslatorMod.Settings.ApiConfigs
                .Where(config => IsConfigReady(config) && config.Provider != TranslatorProvider.DeepL)
                .ToList();
            var eligible = TranslationTaskTierRouter.SelectEligible(ready, TranslationTaskTier.Precision);
            if (eligible.Count == 0) eligible = ready;
            return eligible;
        }

        private static ApiKeyConfig GetNextWorkflowConfig()
        {
            List<ApiKeyConfig> eligible = GetEligibleWorkflowConfigs();
            if (eligible.Count == 0) return null;
            if (eligible.Count == 1) return eligible[0];
            int index = System.Threading.Interlocked.Increment(ref currentKeyIndex);
            return eligible[(index & int.MaxValue) % eligible.Count];
        }
    }
}
