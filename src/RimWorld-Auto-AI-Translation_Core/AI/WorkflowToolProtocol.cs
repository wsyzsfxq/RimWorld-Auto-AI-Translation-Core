// Protocol conversion follows the transport boundary used by NousResearch/hermes-agent.
// See docs/reports/hermes-tool-protocol-migration-2026-10-08.md for attribution and scope.
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTranslator_Core
{
    internal sealed class WorkflowToolDefinition
    {
        public string Name;
        public string Description;
        public JObject Parameters;
    }

    internal sealed class WorkflowToolCall
    {
        public string Id;
        public string Name;
        public string Arguments;
    }

    // A conversation is owned by one batch, never shared across parallel batches.
    // Native assistant messages are retained so signatures/provider metadata survive replay.
    internal sealed class WorkflowToolConversation
    {
        public ApiKeyConfig Configuration;
        public WorkflowToolDefinition Tool;
        public JArray Messages = new JArray();
        public JObject PendingAssistant;
        public List<WorkflowToolCall> PendingCalls = new List<WorkflowToolCall>();
        private string _model;
        private string _baseUrl;
        private string _key;
        private TranslatorProvider _provider;
        private string _initialPrompt;
        private string _correctionInstruction;
        public JObject LastToolReceipt;

        public string GetInitialPrompt(string prompt)
        {
            if (_initialPrompt == null) _initialPrompt = prompt ?? string.Empty;
            return _initialPrompt + (LastToolReceipt == null ? string.Empty :
                "\nPrevious translation tool result:\n" + LastToolReceipt.ToString(Formatting.None)) +
                (string.IsNullOrWhiteSpace(_correctionInstruction) ? string.Empty : "\n" + _correctionInstruction);
        }

        public void SwitchRoute(ApiKeyConfig configuration)
        {
            Configuration = configuration;
            Messages = new JArray();
            PendingAssistant = null;
            PendingCalls = new List<WorkflowToolCall>();
            _model = null; _baseUrl = null; _key = null;
        }

        public void BindRoute(ApiKeyConfig config, string model, string baseUrl, string key)
        {
            if (_model == null)
            {
                _model = model; _baseUrl = baseUrl; _key = key; _provider = config.Provider;
                return;
            }
            if (_model != model || _baseUrl != baseUrl || _key != key || _provider != config.Provider)
                throw new InvalidOperationException("API configuration changed during a tool conversation; restart this batch.");
        }

        public WorkflowToolConversation ForkForCorrection(string instruction)
        {
            if (PendingAssistant != null)
                throw new InvalidOperationException("Record the previous tool results before continuing.");
            WorkflowToolConversation fork = (WorkflowToolConversation)MemberwiseClone();
            fork.Messages = (JArray)Messages.DeepClone();
            fork.PendingCalls = new List<WorkflowToolCall>();
            fork._correctionInstruction = instruction;
            WorkflowProtocolRegistry.Resolve(Configuration.Provider).AppendUserMessage(fork, instruction);
            return fork;
        }
    }

    internal interface IWorkflowProtocol
    {
        string BuildUrl(string baseUrl, string model, string apiKey);
        JObject BuildJsonRequest(ApiKeyConfig config, string model, string prompt, int outputLimit);
        JObject BuildToolRequest(ApiKeyConfig config, string model, string prompt, int outputLimit,
            WorkflowToolConversation conversation);
        WorkflowRawModelResponse ParseResponse(JObject envelope);
        void AppendToolResult(WorkflowToolConversation conversation, WorkflowToolCall call, JObject result);
        void AppendUserMessage(WorkflowToolConversation conversation, string text);
        void PrepareEmptyJsonRetry(JObject payload, ApiKeyConfig config, bool removeResponseFormat);
    }

    internal static class WorkflowProtocolRegistry
    {
        private static readonly IWorkflowProtocol Gemini = new GeminiWorkflowProtocol();
        private static readonly IWorkflowProtocol Chat = new ChatCompletionsWorkflowProtocol();
        public static IWorkflowProtocol Resolve(TranslatorProvider provider)
        {
            if (provider == TranslatorProvider.DeepL)
                throw new InvalidOperationException("DeepL does not implement the workflow model protocol.");
            return provider == TranslatorProvider.Google ? Gemini : Chat;
        }

        public static long? TokenCount(JToken token)
        {
            long value;
            return token != null && long.TryParse(token.ToString(), out value) && value >= 0
                ? (long?)value : null;
        }

        public static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            if (token.Type != JTokenType.String)
                throw new InvalidOperationException("Model protocol text field is not a string.");
            return token.Value<string>();
        }

        public static bool IsThought(JObject block) =>
            block["thought"]?.Type == JTokenType.Boolean && block["thought"].Value<bool>();
    }

    internal sealed class ChatCompletionsWorkflowProtocol : IWorkflowProtocol
    {
        public string BuildUrl(string baseUrl, string model, string apiKey) => baseUrl + "/chat/completions";

        private static JObject BuildBase(ApiKeyConfig config, string model, int outputLimit, JArray messages)
        {
            JObject payload = new JObject
            {
                ["model"] = model, ["messages"] = messages, ["max_tokens"] = outputLimit
            };
            if (config.Provider == TranslatorProvider.DeepSeek)
                payload["thinking"] = new JObject { ["type"] = "disabled" };
            if (config.Provider == TranslatorProvider.OpenRouter)
                payload["provider"] = new JObject { ["require_parameters"] = false };
            return payload;
        }

        public JObject BuildJsonRequest(ApiKeyConfig config, string model, string prompt, int outputLimit)
        {
            JObject payload = BuildBase(config, model, outputLimit, new JArray
            {
                new JObject { ["role"] = "system", ["content"] = "Return valid JSON only and follow the supplied schema exactly." },
                new JObject { ["role"] = "user", ["content"] = prompt ?? string.Empty }
            });
            if (StructuredTranslationProviderAdapter.ResolveMode(config) != StructuredTranslationMode.PromptOnly)
                payload["response_format"] = new JObject { ["type"] = "json_object" };
            return payload;
        }

        public JObject BuildToolRequest(ApiKeyConfig config, string model, string prompt, int outputLimit,
            WorkflowToolConversation conversation)
        {
            if (conversation.Messages.Count == 0)
                conversation.Messages.Add(new JObject { ["role"] = "user", ["content"] = conversation.GetInitialPrompt(prompt) });
            JObject payload = BuildBase(config, model, outputLimit, (JArray)conversation.Messages.DeepClone());
            WorkflowToolDefinition tool = conversation.Tool;
            payload["tools"] = new JArray(new JObject
            {
                ["type"] = "function", ["function"] = new JObject
                {
                    ["name"] = tool.Name, ["description"] = tool.Description,
                    ["parameters"] = tool.Parameters.DeepClone()
                }
            });
            payload["tool_choice"] = new JObject
            {
                ["type"] = "function", ["function"] = new JObject { ["name"] = tool.Name }
            };
            return payload;
        }

        public WorkflowRawModelResponse ParseResponse(JObject envelope)
        {
            JObject message = envelope["choices"]?[0]?["message"] as JObject;
            WorkflowRawModelResponse result = new WorkflowRawModelResponse
            {
                Content = ReadContent(message?["content"]),
                Reasoning = WorkflowProtocolRegistry.Text(message?["reasoning_content"]),
                FinishReason = envelope["choices"]?[0]?["finish_reason"]?.ToString() ?? string.Empty,
                InputTokens = WorkflowProtocolRegistry.TokenCount(envelope["usage"]?["prompt_tokens"]),
                OutputTokens = WorkflowProtocolRegistry.TokenCount(envelope["usage"]?["completion_tokens"]),
                NativeAssistant = message == null ? null : (JObject)message.DeepClone()
            };
            foreach (JObject call in (message?["tool_calls"] as JArray ?? new JArray()).OfType<JObject>())
                result.ToolCalls.Add(new WorkflowToolCall
                {
                    Id = call["id"]?.ToString(), Name = call["function"]?["name"]?.ToString(),
                    Arguments = WorkflowProtocolRegistry.Text(call["function"]?["arguments"])
                });
            return result;
        }

        private static string ReadContent(JToken content)
        {
            if (!(content is JArray blocks)) return WorkflowProtocolRegistry.Text(content);
            return string.Concat(blocks.OfType<JObject>().Select(block =>
            {
                string type = block["type"]?.ToString() ?? string.Empty;
                if (WorkflowProtocolRegistry.IsThought(block) ||
                    type.Equals("thinking", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("reasoning", StringComparison.OrdinalIgnoreCase)) return string.Empty;
                if (type.Length > 0 && !type.Equals("text", StringComparison.OrdinalIgnoreCase) &&
                    !type.Equals("output_text", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unsupported non-text model response block.");
                return WorkflowProtocolRegistry.Text(block["text"] ?? block["content"]);
            }));
        }

        public void AppendToolResult(WorkflowToolConversation conversation, WorkflowToolCall call, JObject result)
        {
            if (string.IsNullOrWhiteSpace(call.Id))
                throw new InvalidOperationException("Tool call ID is missing; cannot pair its result.");
            conversation.Messages.Add(new JObject
            {
                ["role"] = "tool", ["tool_call_id"] = call.Id,
                ["content"] = result.ToString(Formatting.None)
            });
        }

        public void AppendUserMessage(WorkflowToolConversation conversation, string text) =>
            conversation.Messages.Add(new JObject { ["role"] = "user", ["content"] = text });

        public void PrepareEmptyJsonRetry(JObject payload, ApiKeyConfig config, bool removeResponseFormat)
        {
            if (config.Provider == TranslatorProvider.DeepSeek && removeResponseFormat) payload.Remove("response_format");
            JObject system = (payload["messages"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(message => message["role"]?.ToString() == "system");
            if (system != null) system["content"] = (system["content"]?.ToString() ?? string.Empty) +
                " Never return an empty response; emit the complete JSON object now.";
        }
    }

    internal sealed class GeminiWorkflowProtocol : IWorkflowProtocol
    {
        public string BuildUrl(string baseUrl, string model, string apiKey) =>
            baseUrl + "/models/" + model + ":generateContent?key=" + apiKey;

        public JObject BuildJsonRequest(ApiKeyConfig config, string model, string prompt, int outputLimit)
        {
            return new JObject
            {
                ["contents"] = new JArray(UserMessage(prompt)),
                ["generationConfig"] = new JObject { ["maxOutputTokens"] = outputLimit, ["responseMimeType"] = "application/json" }
            };
        }

        private static JObject UserMessage(string prompt) => new JObject
        {
            ["role"] = "user", ["parts"] = new JArray(new JObject { ["text"] = prompt ?? string.Empty })
        };

        public JObject BuildToolRequest(ApiKeyConfig config, string model, string prompt, int outputLimit,
            WorkflowToolConversation conversation)
        {
            if (conversation.Messages.Count == 0) conversation.Messages.Add(UserMessage(conversation.GetInitialPrompt(prompt)));
            WorkflowToolDefinition tool = conversation.Tool;
            return new JObject
            {
                ["contents"] = conversation.Messages.DeepClone(),
                ["generationConfig"] = new JObject { ["maxOutputTokens"] = outputLimit },
                ["tools"] = new JArray(new JObject
                {
                    ["functionDeclarations"] = new JArray(new JObject
                    {
                        ["name"] = tool.Name, ["description"] = tool.Description,
                        ["parameters"] = tool.Parameters.DeepClone()
                    })
                }),
                ["toolConfig"] = new JObject
                {
                    ["functionCallingConfig"] = new JObject
                    {
                        ["mode"] = "ANY", ["allowedFunctionNames"] = new JArray(tool.Name)
                    }
                }
            };
        }

        public WorkflowRawModelResponse ParseResponse(JObject envelope)
        {
            JObject content = envelope["candidates"]?[0]?["content"] as JObject;
            JArray parts = content?["parts"] as JArray ?? new JArray();
            WorkflowRawModelResponse result = new WorkflowRawModelResponse
            {
                Content = string.Concat(parts.OfType<JObject>().Where(part => !WorkflowProtocolRegistry.IsThought(part))
                    .Select(part => WorkflowProtocolRegistry.Text(part["text"]))),
                Reasoning = string.Concat(parts.OfType<JObject>().Where(WorkflowProtocolRegistry.IsThought)
                    .Select(part => WorkflowProtocolRegistry.Text(part["text"]))),
                FinishReason = envelope["candidates"]?[0]?["finishReason"]?.ToString() ?? string.Empty,
                InputTokens = WorkflowProtocolRegistry.TokenCount(envelope["usageMetadata"]?["promptTokenCount"]),
                OutputTokens = WorkflowProtocolRegistry.TokenCount(envelope["usageMetadata"]?["candidatesTokenCount"]),
                NativeAssistant = content == null ? null : (JObject)content.DeepClone()
            };
            foreach (JObject part in parts.OfType<JObject>())
                if (part["functionCall"] is JObject call)
                    result.ToolCalls.Add(new WorkflowToolCall
                    {
                        Id = call["id"]?.ToString(), Name = call["name"]?.ToString(),
                        Arguments = call["args"]?.ToString(Formatting.None) ?? string.Empty
                    });
            return result;
        }

        public void AppendToolResult(WorkflowToolConversation conversation, WorkflowToolCall call, JObject result)
        {
            JObject response = new JObject { ["name"] = call.Name, ["response"] = result.DeepClone() };
            if (!string.IsNullOrWhiteSpace(call.Id)) response["id"] = call.Id;
            JObject last = conversation.Messages.Last as JObject;
            JArray previousParts = last?["parts"] as JArray;
            if (last?["role"]?.ToString() == "user" && previousParts?.First?["functionResponse"] != null)
            {
                previousParts.Add(new JObject { ["functionResponse"] = response });
                return;
            }
            conversation.Messages.Add(new JObject
            {
                ["role"] = "user", ["parts"] = new JArray(new JObject { ["functionResponse"] = response })
            });
        }
        public void AppendUserMessage(WorkflowToolConversation conversation, string text) =>
            conversation.Messages.Add(UserMessage(text));
        public void PrepareEmptyJsonRetry(JObject payload, ApiKeyConfig config, bool removeResponseFormat) { }
    }
}
