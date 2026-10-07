using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace AutoTranslator_Core.Workflow.AI
{
    internal sealed class TranslationToolArguments
    {
        public Dictionary<int, string> Translations { get; } = new Dictionary<int, string>();
        public Dictionary<int, string> Errors { get; } = new Dictionary<int, string>();
    }
    // Owns tool-argument parsing and delegates validation/persistence to the translation service.
    // A successful execution terminates locally; it does not require a model acknowledgement.
    internal static class TranslationSubmissionTool
    {
        public const string Name = "submit_translation_results";

        public static WorkflowToolConversation CreateConversation()
        {
            return new WorkflowToolConversation
            {
                Tool = new WorkflowToolDefinition
                {
                    Name = Name,
                    Description = "Submit translations for the requested item indexes. The tool validates and saves accepted translations and reports rejected items.",
                    Parameters = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject
                        {
                            ["items"] = new JObject
                            {
                                ["type"] = "array",
                                ["items"] = new JObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JObject
                                    {
                                        ["itemIndex"] = new JObject { ["type"] = "integer" },
                                        ["translation"] = new JObject { ["type"] = "string" }
                                    },
                                    ["required"] = new JArray("itemIndex", "translation")
                                }
                            }
                        },
                        ["required"] = new JArray("items")
                    }
                }
            };
        }

        public static TranslationToolArguments ParseArguments(WorkflowToolCall call, IList<int> expectedIndexes)
        {
            TranslationToolArguments parsed = new TranslationToolArguments();
            if (call == null || call.Name != Name)
                return RejectAll(expectedIndexes, "Use " + Name + " to submit translations.");
            JObject arguments;
            try { arguments = JObject.Parse(call.Arguments ?? string.Empty); }
            catch (JsonException) { return RejectAll(expectedIndexes, "Tool arguments must be a valid JSON object."); }
            if (!(arguments["items"] is JArray items))
                return RejectAll(expectedIndexes, "items must be an array.");
            HashSet<int> expected = new HashSet<int>(expectedIndexes);
            HashSet<int> seen = new HashSet<int>();
            foreach (JToken token in items)
            {
                JObject item = token as JObject;
                long index;
                if (item?["itemIndex"]?.Type != JTokenType.Integer ||
                    !long.TryParse(item["itemIndex"].ToString(), out index) || index < 0 || index > int.MaxValue)
                    return RejectAll(expectedIndexes, "Each item requires an integer itemIndex within the requested input range.");
                int itemIndex = (int)index;
                // Previously accepted / unrelated indexes may not be written by this correction.
                if (!expected.Contains(itemIndex)) continue;
                if (!seen.Add(itemIndex))
                {
                    parsed.Translations.Remove(itemIndex);
                    parsed.Errors[itemIndex] = "Duplicate itemIndex; submit this item exactly once.";
                    continue;
                }
                if (item["translation"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace(item["translation"].Value<string>()))
                    parsed.Errors[itemIndex] = "translation must be a nonempty string.";
                else parsed.Translations[itemIndex] = item["translation"].Value<string>();
            }
            foreach (int index in expected)
                if (!parsed.Translations.ContainsKey(index) && !parsed.Errors.ContainsKey(index))
                    parsed.Errors[index] = "Missing item; submit its translation.";
            return parsed;
        }

        public static TranslationToolArguments RejectAll(IEnumerable<int> indexes, string error)
        {
            TranslationToolArguments parsed = new TranslationToolArguments();
            foreach (int index in indexes) parsed.Errors[index] = error;
            return parsed;
        }

        public static void RecordResults(WorkflowToolConversation conversation, IList<JObject> results)
        {
            if (conversation == null || conversation.PendingAssistant == null ||
                conversation.PendingCalls.Count != results.Count)
                throw new InvalidOperationException("Tool results must match the pending assistant tool calls.");
            IWorkflowProtocol protocol = WorkflowProtocolRegistry.Resolve(conversation.Configuration.Provider);
            ValidatePendingCalls(conversation);
            conversation.Messages.Add(conversation.PendingAssistant.DeepClone());
            for (int i = 0; i < results.Count; i++)
                protocol.AppendToolResult(conversation, conversation.PendingCalls[i], results[i]);
            conversation.LastToolReceipt = results.Count == 0 ? null : (JObject)results[0].DeepClone();
            conversation.PendingAssistant = null;
            conversation.PendingCalls.Clear();
        }

        public static void ValidatePendingCalls(WorkflowToolConversation conversation)
        {
            if (conversation?.PendingAssistant == null || conversation.PendingCalls.Count == 0)
                throw new InvalidOperationException("The model did not return a tool call.");
            // Validate IDs before mutating the history; never leave a half-appended turn.
            if (conversation.Configuration.Provider != TranslatorProvider.Google)
            {
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (WorkflowToolCall call in conversation.PendingCalls)
                    if (string.IsNullOrWhiteSpace(call.Id) || !ids.Add(call.Id))
                        throw new InvalidOperationException("Tool call IDs must be present and unique.");
            }
        }
    }
}
