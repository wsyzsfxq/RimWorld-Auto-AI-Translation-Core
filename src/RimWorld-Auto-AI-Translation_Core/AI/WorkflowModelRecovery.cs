using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace AutoTranslator_Core
{
    internal sealed class WorkflowModelRequestException : InvalidOperationException
    {
        public bool CanTryConfiguredFallback { get; }
        public WorkflowRawModelResponse Response { get; }
        public WorkflowModelRequestException(string message, bool canTryConfiguredFallback,
            WorkflowRawModelResponse response = null) : base(message)
        {
            CanTryConfiguredFallback = canTryConfiguredFallback;
            Response = response;
        }
    }

    // Mirrors Hermes' order: targeted compatibility recovery, classified configured fallback,
    // then a terminal error. It never turns a tool request into a text/JSON request.
    internal static class WorkflowModelRecovery
    {
        public static bool TryPrepareCompatibilityRetry(AutoTranslatorAPI.ATC_WebResponse response, JObject payload)
        {
            if (response == null || response.HttpCode != 400) return false;
            string error = ((response.ErrorText ?? "") + " " + (response.ResponseBody ?? "")).ToLowerInvariant();
            if ((error.Contains("json-schema-to-grammar") || error.Contains("llama.cpp")) &&
                (error.Contains("pattern") || error.Contains("format")))
                return StripSchemaKeywords(payload["tools"]) > 0;
            if (payload["thinking"] != null && error.Contains("thinking") &&
                (error.Contains("unsupported") || error.Contains("not supported") || error.Contains("unknown parameter")))
            {
                payload.Remove("thinking");
                return true;
            }
            return false;
        }

        private static int StripSchemaKeywords(JToken token)
        {
            if (token == null) return 0;
            int count = 0;
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties().ToList())
                {
                    if (property.Name == "pattern" || property.Name == "format")
                    {
                        property.Remove(); count++;
                    }
                    else count += StripSchemaKeywords(property.Value);
                }
            }
            else if (token is JArray array)
                foreach (JToken child in array) count += StripSchemaKeywords(child);
            return count;
        }

        public static WorkflowModelRequestException Failure(AutoTranslatorAPI.ATC_WebResponse response)
        {
            if (response == null) return new WorkflowModelRequestException("模型请求未返回响应。", false);
            if (response.BudgetDenied)
                return new WorkflowModelRequestException("模型请求被本地用量预算停止：" + response.BudgetDenialReason, false);
            string error = ((response.ErrorText ?? "") + " " + (response.ResponseBody ?? "")).ToLowerInvariant();
            string category;
            bool fallback;
            if (response.FailureKind == AutoTranslatorAPI.TranslationRequestFailureKind.Cancelled)
            { category = "请求已取消"; fallback = false; }
            else if (response.FailureKind == AutoTranslatorAPI.TranslationRequestFailureKind.LocalDispatch)
            { category = "本地主线程请求调度失败"; fallback = false; }
            else if (error.Contains("context_length") || error.Contains("maximum context") ||
                     error.Contains("max_tokens") || error.Contains("maxoutputtokens"))
            { category = "请求超过模型输入或输出上限"; fallback = false; }
            else if (response.HttpCode == 401 || response.HttpCode == 403)
            { category = "身份认证或访问权限失败"; fallback = true; }
            else if (response.HttpCode == 429 || response.HttpCode == 402)
            { category = "服务商限流或额度不足"; fallback = true; }
            else if (response.HttpCode >= 500 || response.FailureKind == AutoTranslatorAPI.TranslationRequestFailureKind.Transport ||
                     response.FailureKind == AutoTranslatorAPI.TranslationRequestFailureKind.ResponseTimeout ||
                     response.FailureKind == AutoTranslatorAPI.TranslationRequestFailureKind.UnityTransportStall)
            { category = "服务商不可用或请求超时"; fallback = true; }
            else if (response.HttpCode == 400 || response.HttpCode == 404 || response.HttpCode == 422)
            { category = "模型、接口或工具协议参数不兼容"; fallback = true; }
            else
            { category = "模型请求失败"; fallback = false; }
            // Do not echo arbitrary upstream bodies/URLs, which may include credentials.
            return new WorkflowModelRequestException(category + "（HTTP " + response.HttpCode +
                "，阶段 " + (response.FailureStage ?? "unknown") +
                (response.TimeoutSeconds > 0 ? "，响应超时 " + response.TimeoutSeconds + " 秒" : "") + "）", fallback);
        }
    }
}
