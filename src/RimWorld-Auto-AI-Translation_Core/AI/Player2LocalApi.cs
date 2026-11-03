using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AutoTranslator_Core
{
    internal static class Player2LocalApi
    {
        internal const string DefaultBaseUrl = "http://127.0.0.1:4315/v1";
        // For diagnostics and request identity only; never sent as a model ID.
        internal const string ClientSelectedModel = "player2-client-selected";

        internal static bool IsEnabled(ApiKeyConfig config)
        {
            return config != null && config.Provider == TranslatorProvider.Custom_OpenAI &&
                   config.UsePlayer2Protocol;
        }

        internal static bool TryGetGameClientId(string value, out string clientId)
        {
            clientId = (value ?? string.Empty).Trim();
            if (clientId.Length == 0) return false;
            // Official clients have used both names and dashboard client IDs.
            // Preserve the registered value instead of imposing an undocumented UUID format.
            foreach (char c in clientId)
                if (c < 32 || c > 126) { clientId = string.Empty; return false; }
            return true;
        }

        internal static string GetConfigurationProblem(ApiKeyConfig config)
        {
            if (!IsEnabled(config)) return null;
            string clientId;
            if (!TryGetGameClientId(config.Player2GameClientId, out clientId))
                return AutoTranslatorMod.WfText(
                    "请填写此 Mod 的 Player2 Game Client ID。",
                    "Enter this mod's Player2 Game Client ID.");
            string baseUrl;
            if (!TryNormalizeBaseUrl(config.CustomBaseUrl, out baseUrl))
                return AutoTranslatorMod.WfText(
                    "Player2 本地模式需要本机客户端地址，例如 http://127.0.0.1:4315/v1；Web API 的授权方式不同。",
                    "Player2 local mode requires the desktop client's loopback URL, for example http://127.0.0.1:4315/v1; the Web API uses different authentication.");
            return null;
        }

        internal static string NormalizeBaseUrl(string value)
        {
            string result;
            if (!TryNormalizeBaseUrl(value, out result))
                throw new InvalidOperationException("Invalid Player2 local API URL.");
            return result;
        }

        private static bool TryNormalizeBaseUrl(string value, out string result)
        {
            result = DefaultBaseUrl;
            string input = (value ?? string.Empty).Trim();
            if (input.Length == 0) return true;
            if (input.IndexOf("://", StringComparison.Ordinal) < 0) input = "http://" + input;
            Uri uri;
            if (!Uri.TryCreate(input, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - "/chat/completions".Length);
            else if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - "/models".Length);
            if (path.Length == 0) path = "/v1";
            if (!path.Equals("/v1", StringComparison.OrdinalIgnoreCase)) return false;
            var builder = new UriBuilder(uri) { Path = "/v1" };
            if (builder.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) builder.Host = "127.0.0.1";
            result = builder.Uri.AbsoluteUri.TrimEnd('/');
            return true;
        }

        // https://github.com/elefant-ai/player2-ai-npc-defold#chat-completion-api
        internal static JObject PrepareChatPayload(JObject payload)
        {
            JObject result = (JObject)payload.DeepClone();
            result.Remove("model");
            result["stream"] = false;
            return result;
        }
    }

    internal static class ApiRequestHeaders
    {
        internal static Dictionary<string, string> Build(
            TranslatorProvider provider, string apiKey,
            string gameClientId = null, bool usePlayer2Protocol = false)
        {
            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" } };
            if (provider == TranslatorProvider.Custom_OpenAI && usePlayer2Protocol)
            {
                string id;
                if (!Player2LocalApi.TryGetGameClientId(gameClientId, out id))
                    throw new InvalidOperationException("Player2 requires this mod's Game Client ID.");
                headers.Add("player2-game-key", id);
                return headers;
            }
            string key = (apiKey ?? string.Empty).Trim();
            if (key.Length > 0 && provider != TranslatorProvider.Google)
            {
                if (provider == TranslatorProvider.MiMo) headers.Add("api-key", key);
                else headers.Add("Authorization", (provider == TranslatorProvider.DeepL ? "DeepL-Auth-Key " : "Bearer ") + key);
            }
            return headers;
        }
    }
}
