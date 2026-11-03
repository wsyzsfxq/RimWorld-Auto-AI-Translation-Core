using Newtonsoft.Json.Linq;

namespace AutoTranslator_Core
{
    internal static class MiMoApi
    {
        internal const string BaseUrl = "https://api.xiaomimimo.com/v1";
        internal const string DefaultModel = "mimo-v2.6-flash";

        // https://mimo.mi.com/docs/en-US/api/chat/openai-api
        // Thinking is applied separately by ApiThinkingPolicy after provider conversion.
        internal static JObject PrepareChatPayload(JObject payload)
        {
            JObject result = (JObject)payload.DeepClone();
            if (result["max_completion_tokens"] == null && result["max_tokens"] != null)
                result["max_completion_tokens"] = result["max_tokens"].DeepClone();
            result.Remove("max_tokens");
            result["stream"] = false;
            return result;
        }
    }

    public static partial class AutoTranslatorAPI
    {
        internal static JObject PrepareProviderChatPayload(
            JObject payload, TranslatorProvider provider, bool usePlayer2Protocol)
        {
            if (provider == TranslatorProvider.MiMo) return MiMoApi.PrepareChatPayload(payload);
            if (provider == TranslatorProvider.Custom_OpenAI && usePlayer2Protocol)
                return Player2LocalApi.PrepareChatPayload(payload);
            return payload;
        }
    }
}
