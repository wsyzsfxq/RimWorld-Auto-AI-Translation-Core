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
        public long? InputTokens;
        public long? OutputTokens;
        public string FinishReason = string.Empty;
        public string ProviderName = string.Empty;
        public string ModelName = string.Empty;
        public int ConfiguredOutputTokenLimit;
        public int? KnownModelOutputTokenLimit;
        public int ActualOutputTokenLimit;
    }

    public static partial class AutoTranslatorAPI
    {
        internal static async Task<WorkflowRawModelResponse> InvokeWorkflowJsonAsync(
            string prompt,
            CancellationToken cancellationToken)
        {
            ApiKeyConfig config = GetNextWorkflowConfig();
            if (config == null) throw new InvalidOperationException("No enabled API configuration is available.");

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
            string url;
            JObject payload;
            if (config.Provider == TranslatorProvider.Google)
            {
                url = baseUrl + "/models/" + model + ":generateContent?key=" + apiKey;
                payload = new JObject
                {
                    ["contents"] = new JArray
                    {
                        new JObject
                        {
                            ["parts"] = new JArray { new JObject { ["text"] = prompt ?? string.Empty } }
                        }
                    },
                    ["generationConfig"] = new JObject
                    {
                        ["maxOutputTokens"] = Math.Max(1, maximumOutputTokens),
                        ["responseMimeType"] = "application/json"
                    }
                };
            }
            else
            {
                url = baseUrl + "/chat/completions";
                payload = new JObject
                {
                    ["model"] = model,
                    ["messages"] = new JArray
                    {
                        new JObject { ["role"] = "system", ["content"] = "Return valid JSON only and follow the supplied schema exactly." },
                        new JObject { ["role"] = "user", ["content"] = prompt ?? string.Empty }
                    },
                    ["max_tokens"] = Math.Max(1, maximumOutputTokens)
                };
                StructuredTranslationMode structuredMode = StructuredTranslationProviderAdapter.ResolveMode(config);
                if (structuredMode != StructuredTranslationMode.PromptOnly)
                    payload["response_format"] = new JObject { ["type"] = "json_object" };
                if (config.Provider == TranslatorProvider.DeepSeek)
                {
                    // Workflow steps need the final JSON, not chain-of-thought. DeepSeek V4 enables
                    // thinking by default; a small structured-output budget can otherwise be spent
                    // entirely on reasoning and leave message.content empty.
                    payload["thinking"] = new JObject { ["type"] = "disabled" };
                    payload["max_tokens"] = Math.Max(1, maximumOutputTokens);
                }
                if (config.Provider == TranslatorProvider.OpenRouter)
                    payload["provider"] = new JObject { ["require_parameters"] = false };
            }

            int timeoutSeconds = TranslationPolicyAgentTimeout.Resolve(
                AutoTranslatorMod.Settings != null ? AutoTranslatorMod.Settings.TimeoutSeconds : 120, 0);
            AutoTranslatorSettings.AddLog(
                "模型请求参数：响应超时 " + timeoutSeconds + " 秒；超过该时间仍未收到完整响应将判定为超时");
            Func<bool> cancelled = () => cancellationToken.IsCancellationRequested ||
                                         AutoTranslatorSettings.IsCancellationRequested;
            string lastFinishReason = string.Empty;
            const int maximumStructuredAttempts = 3;
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

                JObject envelope = JObject.Parse(response.ResponseBody ?? string.Empty);
                WorkflowRawModelResponse result = ExtractWorkflowResponse(envelope, config.Provider);
                lastFinishReason = ReadWorkflowFinishReason(envelope, config.Provider);
                result.FinishReason = lastFinishReason;
                result.ProviderName = config.Provider.ToString();
                result.ModelName = model;
                result.ConfiguredOutputTokenLimit = configuredOutputTokenLimit;
                result.KnownModelOutputTokenLimit = knownModelOutputTokenLimit;
                result.ActualOutputTokenLimit = maximumOutputTokens;
                if (IsWorkflowLengthTermination(lastFinishReason)) return result;
                if (!string.IsNullOrWhiteSpace(result.Content)) return result;

                AutoTranslatorSettings.AddDebugLog(
                    "workflow.model empty-content provider=" + config.Provider +
                    " model=" + model + " finishReason=" + lastFinishReason +
                    " attempt=" + (structuredAttempt + 1));
                if (structuredAttempt < maximumStructuredAttempts - 1)
                    PrepareEmptyStructuredResponseRetry(
                        payload,
                        config.Provider,
                        removeResponseFormat: structuredAttempt == 1);
            }

            throw new InvalidOperationException(
                "The model returned an empty structured response" +
                (string.IsNullOrWhiteSpace(lastFinishReason)
                    ? "."
                    : " (finish reason: " + lastFinishReason + ")."));
        }

        private static WorkflowRawModelResponse ExtractWorkflowResponse(
            JObject envelope,
            TranslatorProvider provider)
        {
            WorkflowRawModelResponse result = new WorkflowRawModelResponse();
            if (provider == TranslatorProvider.Google)
            {
                JArray parts = envelope["candidates"]?[0]?["content"]?["parts"] as JArray;
                result.Content = parts?
                    .OfType<JObject>()
                    .Where(part => part["thought"]?.Type != JTokenType.Boolean || !part["thought"].Value<bool>())
                    .Select(part => part["text"]?.ToString())
                    .LastOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
                result.InputTokens = ReadNullableTokenCount(envelope["usageMetadata"]?["promptTokenCount"]);
                result.OutputTokens = ReadNullableTokenCount(envelope["usageMetadata"]?["candidatesTokenCount"]);
            }
            else
            {
                result.Content = ExtractOpenAiCompatibleContent(envelope["choices"]?[0]?["message"]?["content"]);
                result.InputTokens = ReadNullableTokenCount(envelope["usage"]?["prompt_tokens"]);
                result.OutputTokens = ReadNullableTokenCount(envelope["usage"]?["completion_tokens"]);
            }
            return result;
        }

        private static string ExtractOpenAiCompatibleContent(JToken content)
        {
            if (content == null || content.Type == JTokenType.Null) return string.Empty;
            if (content.Type == JTokenType.String) return content.ToString();
            if (content is JArray blocks)
            {
                return blocks.OfType<JObject>()
                    .Select(block => block["text"]?.ToString() ?? block["content"]?.ToString())
                    .LastOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
            }
            return content.ToString();
        }

        private static string ReadWorkflowFinishReason(JObject envelope, TranslatorProvider provider)
        {
            return provider == TranslatorProvider.Google
                ? envelope["candidates"]?[0]?["finishReason"]?.ToString() ?? string.Empty
                : envelope["choices"]?[0]?["finish_reason"]?.ToString() ?? string.Empty;
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

        private static void PrepareEmptyStructuredResponseRetry(
            JObject payload,
            TranslatorProvider provider,
            bool removeResponseFormat)
        {
            if (provider == TranslatorProvider.Google)
            {
                return;
            }

            if (provider == TranslatorProvider.DeepSeek && removeResponseFormat)
            {
                // DeepSeek documents an occasional empty response in JSON Output mode.
                // Keep response_format for the first retry. Only the final attempt falls
                // back to the prompt-level JSON contract.
                payload.Remove("response_format");
            }
            JArray messages = payload["messages"] as JArray;
            JObject system = messages?.OfType<JObject>()
                .FirstOrDefault(message => string.Equals(message["role"]?.ToString(), "system", StringComparison.Ordinal));
            if (system != null)
                system["content"] = (system["content"]?.ToString() ?? string.Empty) +
                                    " Never return an empty response; emit the complete JSON object now.";
        }

        private static ApiKeyConfig GetNextWorkflowConfig()
        {
            if (AutoTranslatorMod.Settings?.ApiConfigs == null) return null;
            var ready = AutoTranslatorMod.Settings.ApiConfigs
                .Where(config => IsConfigReady(config) && config.Provider != TranslatorProvider.DeepL)
                .ToList();
            var eligible = TranslationTaskTierRouter.SelectEligible(ready, TranslationTaskTier.Precision);
            if (eligible.Count == 0) eligible = ready;
            if (eligible.Count == 0) return null;
            if (eligible.Count == 1) return eligible[0];
            int index = System.Threading.Interlocked.Increment(ref currentKeyIndex);
            return eligible[(index & int.MaxValue) % eligible.Count];
        }
    }
}
