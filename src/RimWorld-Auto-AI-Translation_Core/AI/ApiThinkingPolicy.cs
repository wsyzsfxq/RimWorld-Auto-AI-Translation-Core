using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AutoTranslator_Core
{
    public enum ApiThinkingMode
    {
        Auto,
        PreferOff,
        Enabled
    }

    // Protocols and model exceptions are documented in thinking-protocol-audit-2026-10-30.md.
    // Unknown models keep the service default; a model name alone never identifies a gateway protocol.
    internal static class ApiThinkingPolicy
    {
        private sealed class Plan
        {
            public readonly JObject Fields = new JObject();
            public string Status = "此模型的思考控制能力尚未确认，沿用服务默认值。";
            public string Unsupported;
            public bool CompletionBudget;
        }

        private sealed class RouterModels
        {
            public string Endpoint;
            public readonly Dictionary<string, JObject> Models =
                new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        }

        private static readonly object RouterSync = new object();
        private static readonly ConditionalWeakTable<ApiKeyConfig, RouterModels> RouterCache =
            new ConditionalWeakTable<ApiKeyConfig, RouterModels>();

        public static JObject Prepare(JObject payload, ApiKeyConfig config, string endpoint, bool review)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            Plan plan = Resolve(config, endpoint, review);
            if (plan.Unsupported != null) throw new InvalidOperationException(plan.Unsupported);
            JObject result = (JObject)payload.DeepClone();
            if (plan.Fields.Count > 0 && plan.Fields["generationConfig"] == null)
            {
                // A repeated preparation must not retain a competing provider's control or an old effort.
                foreach (string name in new[] { "thinking", "reasoning", "reasoning_effort", "enable_thinking", "thinking_budget" })
                    result.Remove(name);
            }
            foreach (JProperty field in plan.Fields.Properties())
            {
                if (field.Name == "generationConfig")
                {
                    JObject generation = result["generationConfig"] as JObject ?? new JObject();
                    // Replace only thinkingConfig, retaining the caller's output cap and response format.
                    generation["thinkingConfig"] = field.Value["thinkingConfig"].DeepClone();
                    result["generationConfig"] = generation;
                }
                else result[field.Name] = field.Value.DeepClone();
            }
            if (plan.CompletionBudget && result["max_tokens"] != null)
            {
                if (result["max_completion_tokens"] == null)
                    result["max_completion_tokens"] = result["max_tokens"].DeepClone();
                result.Remove("max_tokens");
            }
            return result;
        }

        public static string GetStatus(ApiKeyConfig config, string endpoint, bool review)
        {
            Plan plan = Resolve(config, endpoint, review);
            return plan.Unsupported ?? plan.Status;
        }

        public static string GetUnsupportedReason(ApiKeyConfig config, string endpoint, bool review = false)
        {
            return Resolve(config, endpoint, review).Unsupported;
        }

        // Called with the response already obtained by model discovery; performs no network requests.
        public static void RememberModelCapabilities(ApiKeyConfig config, string responseBody)
        {
            if (config == null) return;
            var snapshot = new RouterModels { Endpoint = config.CustomBaseUrl ?? string.Empty };
            try
            {
                JArray models = JObject.Parse(responseBody ?? string.Empty)["data"] as JArray;
                if (models != null)
                {
                    foreach (JObject model in models.OfType<JObject>())
                    {
                        string id = model.Value<string>("id");
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            JObject reasoning = model["reasoning"] as JObject;
                            snapshot.Models[id] = reasoning == null ? null : (JObject)reasoning.DeepClone();
                        }
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // An unreadable capability response must not preserve an older model's disable permission.
            }
            lock (RouterSync)
            {
                RouterCache.Remove(config);
                RouterCache.Add(config, snapshot);
            }
        }

        public static void ForgetModelCapabilities(ApiKeyConfig config)
        {
            if (config == null) return;
            lock (RouterSync) RouterCache.Remove(config);
        }

        private static Plan Resolve(ApiKeyConfig config, string endpoint, bool review)
        {
            var plan = new Plan();
            if (config == null) return plan;
            if (Player2LocalApi.IsEnabled(config))
            {
                plan.Status = "Player2 由本地客户端选择模型，沿用客户端的思考设置。";
                return plan;
            }
            string provider = ResolveProvider(config, endpoint);
            string model = (config.SelectedModel ?? string.Empty).Trim().ToLowerInvariant();
            if (model.StartsWith("models/", StringComparison.Ordinal)) model = model.Substring(7);
            bool enabled = config.ThinkingMode == ApiThinkingMode.Enabled ||
                (config.ThinkingMode == ApiThinkingMode.Auto && review);
            switch (provider)
            {
                case "Google": Google(plan, model, enabled); break;
                case "OpenAI": OpenAI(plan, model, enabled); break;
                case "DeepSeek":
                    if (Is(model, "deepseek-flash", "deepseek-v4-pro", "deepseek-v4-flash"))
                    {
                        TypeSwitch(plan, enabled);
                        if (enabled) plan.Fields["reasoning_effort"] = "low";
                    }
                    else if (Is(model, "deepseek-chat", "deepseek-reasoner"))
                        plan.Status = "DeepSeek 官方已公告停用此旧模型名，请从模型列表重新选择。";
                    break;
                case "Grok":
                    if (Is(model, "grok-4.3", "grok-4.3-latest")) Effort(plan, enabled ? "low" : "none", false);
                    else if (Is(model, "grok-4.5", "grok-4.5-latest", "grok-4.6", "grok-4.6-latest", "grok-4.7", "grok-4.7-latest"))
                        Effort(plan, "low", !enabled);
                    else if (Is(model, "grok-3-mini", "grok-3-mini-latest", "grok-3-mini-fast", "grok-3-mini-fast-latest"))
                        plan.Status = "此旧型号的当前思考参数尚未确认，保留原请求；可从最新模型列表重新选择。";
                    break;
                case "GLM": Glm(plan, model, enabled); break;
                case "Alibaba": Alibaba(plan, model, enabled); break;
                case "OpenRouter": Router(plan, config, model, enabled); break;
                case "MiMo":
                    if (Is(model, "mimo-v2.6-flash", "mimo-v2.6-pro", "mimo-v2.6-pro-ultraspeed", "mimo-v2.5", "mimo-v2.5-pro"))
                    {
                        TypeSwitch(plan, enabled);
                        plan.CompletionBudget = true;
                    }
                    break;
                case "DeepL": plan.Status = "DeepL 不使用大模型思考参数。"; break;
                default: plan.Status = "自定义网关协议未确认，沿用服务默认值，不发送猜测的思考参数。"; break;
            }
            return plan;
        }

        private static void OpenAI(Plan plan, string model, bool enabled)
        {
            if (Is(model, "o1-pro", "o1-pro-2025-03-19", "o3-pro", "o3-pro-2025-06-10",
                "gpt-5.2-pro", "gpt-5.2-pro-2025-12-11", "gpt-5.4-pro", "gpt-5.4-pro-2026-03-05",
                "gpt-5.5-pro", "gpt-5.5-pro-2026-04-23"))
            {
                plan.Unsupported = "此模型只提供 Responses API；当前翻译请求使用 Chat Completions，请选择支持该接口的模型。";
                return;
            }
            if (Is(model, "gpt-6-astra", "gpt-6.1-sol", "o1", "o1-2024-12-17", "o3", "o3-2025-04-16", "o3-mini", "o3-mini-2025-01-31", "o4-mini", "o4-mini-2025-04-16"))
                Effort(plan, "low", !enabled);
            else if (Is(model, "gpt-5", "gpt-5-2025-08-07", "gpt-5-mini", "gpt-5-mini-2025-08-07", "gpt-5-nano", "gpt-5-nano-2025-08-07"))
                Effort(plan, enabled ? "low" : "minimal", !enabled);
            else if (Is(model, "gpt-5.1", "gpt-5.1-2025-11-13", "gpt-5.2", "gpt-5.2-2025-12-11", "gpt-5.4", "gpt-5.4-2026-03-05", "gpt-5.4-mini", "gpt-5.4-mini-2026-03-17", "gpt-5.4-nano", "gpt-5.4-nano-2026-03-17", "gpt-5.5", "gpt-5.5-2026-04-23", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-6.1-terra", "gpt-6-sol", "gpt-6-luna"))
                Effort(plan, enabled ? "low" : "none", false);
            else if (Is(model, "gpt-4o", "gpt-4o-mini", "gpt-4.1", "gpt-4.1-mini", "gpt-4.1-nano"))
                plan.Status = "此型号没有可切换的思考模式。";
            plan.CompletionBudget = plan.Fields["reasoning_effort"] != null;
        }

        private static void Google(Plan plan, string model, bool enabled)
        {
            var thinking = new JObject();
            if (Is(model, "gemini-2.5-pro"))
            {
                thinking["thinkingBudget"] = enabled ? -1 : 128;
                plan.Status = enabled ? "已请求动态思考。" : "此模型不能关闭思考，使用最低预算 128。";
            }
            else if (Is(model, "gemini-2.5-flash", "gemini-2.5-flash-lite", "gemini-2.5-flash-preview-05-20", "gemini-2.5-flash-lite-preview-06-17"))
            {
                thinking["thinkingBudget"] = enabled ? -1 : 0;
                plan.Status = enabled ? "已请求动态思考。" : "已请求关闭思考。";
            }
            else if (Is(model, "gemini-4-argon", "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.1-pro-preview", "gemini-3-pro-preview"))
            {
                thinking["thinkingLevel"] = "low";
                plan.Status = enabled ? "已请求 low 档思考。" : "此模型不能关闭思考，使用最低可用档 low。";
            }
            else if (Is(model, "gemini-3.6-flash", "gemini-3.5-flash", "gemini-3.5-flash-lite", "gemini-3.1-flash-lite", "gemini-3.1-flash-lite-preview", "gemini-3-flash-preview"))
            {
                thinking["thinkingLevel"] = enabled ? "low" : "minimal";
                plan.Status = enabled ? "已请求 low 档思考。" : "使用 minimal 档；该档仍可能产生少量思考。";
            }
            else return;
            plan.Fields["generationConfig"] = new JObject { ["thinkingConfig"] = thinking };
        }

        private static void Glm(Plan plan, string model, bool enabled)
        {
            if (Is(model, "glm-5.3", "glm-5.3-flash"))
            {
                TypeSwitch(plan, true);
                plan.Fields["reasoning_effort"] = "low";
                if (!enabled) plan.Status = "此模型不能关闭思考，使用最低可用档 low。";
            }
            else if (Is(model, "glm-5.3-flashx"))
            {
                // The documented effort values are explicit, but its disabled behavior is not.
                plan.Fields["reasoning_effort"] = "low";
                plan.Status = "使用已确认的 low 档；此型号关闭思考的能力尚未确认。";
            }
            else if (Is(model, "glm-5.2", "glm-5.1", "glm-5", "glm-5-turbo", "glm-5v-turbo", "glm-4.7", "glm-4.7-flash", "glm-4.6", "glm-4.6v", "glm-4.5", "glm-4.5v", "glm-4.5-air", "glm-4.5-x", "glm-4.5-airx", "glm-4.5-flash"))
            {
                TypeSwitch(plan, enabled);
                if (enabled && model == "glm-5.2") plan.Fields["reasoning_effort"] = "high";
            }
        }

        private static void Alibaba(Plan plan, string model, bool enabled)
        {
            if (Is(model, "qwen3.5-122b-a10b", "qwen3.5-27b", "qwen3.5-omni-plus", "qwen3-omni-flash"))
            {
                plan.Unsupported = "此百炼模型只支持流式输出；当前翻译使用非流式请求，请选择支持非流式输出的模型。";
                return;
            }
            if (model == "qwen3.8-omni-flash")
            {
                Effort(plan, enabled ? "low" : "none", false);
                return;
            }
            if (Is(model, "qwen3-235b-a22b", "qwen3-32b", "qwen3-30b-a3b", "qwen3-14b", "qwen3-8b"))
            {
                if (enabled && Is(model, "qwen3-235b-a22b", "qwen3-32b"))
                {
                    plan.Unsupported = "此旧版 Qwen3 开源型号开启思考需要流式输出；当前使用非流式请求，请优先关闭思考或选择其他型号。";
                    return;
                }
                plan.Fields["enable_thinking"] = enabled;
                plan.Status = enabled ? "已请求开启旧版 Qwen3 思考；此系列的开源服务可能要求流式输出。"
                    : "已请求关闭旧版 Qwen3 思考。";
                return;
            }
            if (Is(model, "qwen3-235b-a22b-instruct-2507", "qwen3-30b-a3b-instruct-2507", "qwen3-next-80b-a3b-instruct"))
            {
                plan.Status = "此 Instruct 型号没有可切换的思考模式。";
                return;
            }
            if (Is(model, "qwen3-235b-a22b-thinking-2507", "qwen3-30b-a3b-thinking-2507", "qwen3-next-80b-a3b-thinking"))
            {
                plan.Status = "此 Thinking 型号固定进行思考，沿用模型默认设置。";
                return;
            }
            if (model == "minimax/minimax-m3")
            {
                plan.Fields["thinking"] = new JObject { ["type"] = enabled ? "adaptive" : "disabled" };
                plan.Status = enabled ? "已请求 MiniMax 自适应思考。" : "已请求关闭思考。";
                return;
            }
            if (model == "minimax/minimax-m3.1-flash")
            {
                Effort(plan, "low", !enabled);
                return;
            }
            // These are distinct IDs in Model Studio's hosted-model catalog.
            // Their protocols come from that catalog, not from the name prefix.
            if (Is(model, "kimi-k3", "kimi/kimi-k3.1-pro", "kimi/kimi-k3.1-pro-highspeed",
                "kimi/kimi-k3", "kimi/kimi-k2.7-code-highspeed", "kimi/kimi-k2.7-code",
                "siliconflow/deepseek-r1-0528", "vanchin/deepseek-r1",
                "minimax/minimax-m2.7", "minimax/minimax-m2.5", "minimax/minimax-m2.1",
                "stepfun/step-3.7-flash", "stepfun/step-5-preview"))
            {
                plan.Status = "此百炼模型仅提供思考模式，沿用模型默认设置。";
                return;
            }
            if (Is(model, "kimi/kimi-k2.6", "kimi/kimi-k2.5",
                "siliconflow/deepseek-v3.2", "siliconflow/deepseek-v3.1-terminus",
                "vanchin/deepseek-v3.2-think", "vanchin/deepseek-v3.1-terminus",
                "antdigital/ling-3.1-flash"))
            {
                plan.Fields["enable_thinking"] = enabled;
                plan.Status = enabled ? "已请求开启思考。" : "已请求关闭思考。";
                return;
            }
            if (Is(model, "qwen3.8-2.4t-a95b", "qwen3.7-max-preview", "qwen3.7-max-2026-05-17", "glm-5.3", "kimi-k2.7-code", "minimax-m2.5", "deepseek-r1", "deepseek-r1-distill-qwen-32b", "deepseek-r1-distill-qwen-14b", "deepseek-r1-distill-qwen-7b", "deepseek-r1-distill-qwen-1.5b", "deepseek-r1-distill-llama-70b", "deepseek-r1-distill-llama-8b"))
            {
                plan.Status = "此百炼模型仅提供思考模式，沿用模型默认设置。";
                return;
            }
            if (!Is(model, "qwen3.8-max", "qwen3.8-flash", "qwen3.8-27b", "qwen3.7-max", "qwen3.7-max-2026-05-20", "qwen3.7-max-2026-06-08", "qwen3.7-plus", "qwen3.7-plus-2026-05-26", "qwen3.7-flash", "qwen3.7-flash-2026-07-15", "qwen3.6-plus", "qwen3.6-plus-2026-04-02", "qwen3.6-flash", "qwen3.6-flash-2026-04-16", "qwen3.5-plus", "qwen3.5-plus-2026-02-15", "qwen3.5-flash", "qwen3.5-flash-2026-02-23", "qwen-plus", "qwen-plus-latest", "qwen-plus-2025-04-28", "qwen-flash", "qwen-flash-2025-07-28", "deepseek-v4.1-flash", "deepseek-v4.1-flash-1017", "deepseek-v4-pro", "glm-5.2", "glm-5.2-fast-preview", "glm-5.1", "glm-5", "kimi-k2.6", "kimi-k2.5")) return;
            plan.Fields["enable_thinking"] = enabled;
            plan.Status = enabled ? "已请求开启思考。" : "已请求关闭思考。";
        }

        private static void Router(Plan plan, ApiKeyConfig config, string model, bool enabled)
        {
            if (Is(model, "openrouter/auto", "openrouter/free"))
            {
                plan.Status = "动态路由没有固定模型的思考能力，沿用路由默认值。";
                return;
            }
            JObject capability = null;
            bool found = false;
            lock (RouterSync)
            {
                RouterModels snapshot;
                if (RouterCache.TryGetValue(config, out snapshot) && snapshot.Endpoint == (config.CustomBaseUrl ?? string.Empty))
                    found = snapshot.Models.TryGetValue(model, out capability);
            }
            if (found && capability != null)
            {
                bool parsedMandatory;
                bool? mandatory = bool.TryParse(capability["mandatory"]?.ToString(), out parsedMandatory)
                    ? (bool?)parsedMandatory : null;
                JToken effortToken = capability["supported_efforts"];
                string[] efforts = effortToken is JArray
                    ? ((JArray)effortToken).Values<string>().ToArray()
                    : effortToken != null && effortToken.Type == JTokenType.Null
                        ? new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max" } : new string[0];
                bool canDisable = mandatory != true && (mandatory == false || efforts.Contains("none"));
                if (!enabled && canDisable)
                {
                    plan.Fields["reasoning"] = new JObject { ["enabled"] = false };
                    plan.Status = "已按 OpenRouter 模型能力请求关闭思考。";
                    return;
                }
                if (!enabled && mandatory != true)
                {
                    plan.Status = "模型元数据未确认可以关闭思考，沿用服务默认值。";
                    return;
                }
                string effort = new[] { "low", "minimal", "medium", "high", "xhigh", "max" }
                    .FirstOrDefault(value => efforts.Contains(value));
                if (!enabled) effort = new[] { "minimal", "low", "medium", "high", "xhigh", "max" }
                    .FirstOrDefault(value => efforts.Contains(value));
                var reasoning = new JObject { ["enabled"] = true };
                if (effort != null) reasoning["effort"] = effort;
                plan.Fields["reasoning"] = reasoning;
                plan.Status = enabled ? "已按 OpenRouter 模型能力请求开启思考。"
                    : effort == null ? "此模型必须思考，未提供可调档位，沿用服务默认强度。"
                    : "此模型必须思考，使用模型元数据允许的最低档。";
                return;
            }
            if (config.ModelSupportsParameter(model, "reasoning") || config.ModelSupportsParameter(model, "reasoning_effort"))
            {
                if (enabled)
                {
                    plan.Fields["reasoning"] = new JObject { ["enabled"] = true };
                    plan.Status = "已请求开启思考，强度沿用模型默认值。";
                }
                else plan.Status = "模型支持思考，但是否能关闭尚未确认，沿用服务默认值。";
            }
            else if (found) plan.Status = "模型列表未提供此型号的思考控制能力，沿用服务默认值。";
        }

        private static void TypeSwitch(Plan plan, bool enabled)
        {
            plan.Fields["thinking"] = new JObject { ["type"] = enabled ? "enabled" : "disabled" };
            plan.Status = enabled ? "已请求开启思考（是否实际思考由模型决定）。" : "已请求关闭思考。";
        }

        private static void Effort(Plan plan, string value, bool cannotDisable)
        {
            plan.Fields["reasoning_effort"] = value;
            plan.Status = cannotDisable ? "此模型不能完全关闭思考，使用最低可用档 " + value + "。"
                : value == "none" ? "已请求关闭思考。" : "已请求 " + value + " 档思考。";
        }

        private static bool Is(string model, params string[] values)
        {
            return Array.IndexOf(values, model) >= 0;
        }

        private static string ResolveProvider(ApiKeyConfig config, string endpoint)
        {
            if (config.Provider != TranslatorProvider.Custom_OpenAI) return config.Provider.ToString();
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri)) return "Custom_OpenAI";
            if (uri.Host.EndsWith(".maas.aliyuncs.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/compatible-mode/", StringComparison.OrdinalIgnoreCase)) return "Alibaba";
            switch (uri.Host.ToLowerInvariant())
            {
                case "api.openai.com": return "OpenAI";
                case "api.deepseek.com": return "DeepSeek";
                case "api.x.ai": return "Grok";
                case "open.bigmodel.cn": case "api.z.ai": return "GLM";
                case "dashscope.aliyuncs.com": case "dashscope-intl.aliyuncs.com": case "dashscope-us.aliyuncs.com": return "Alibaba";
                case "openrouter.ai": return "OpenRouter";
                case "api.xiaomimimo.com": return "MiMo";
                // Google's OpenAI-compatible endpoint is not its native generateContent protocol.
                default: return "Custom_OpenAI";
            }
        }
    }
}
