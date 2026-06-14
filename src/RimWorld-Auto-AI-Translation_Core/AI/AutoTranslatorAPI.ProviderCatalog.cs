using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorAPI
    {
        public struct ProviderDef
        {
            public string BaseUrl;
            public string ListModelsUrl;
        }


        public struct ProviderRuntimeProfile
        {
            public int BatchSize;
            public int FormatRetries;
            public int TimeoutFloorSeconds;
            public string QualityHintKey;
        }


        public static ProviderRuntimeProfile GetRuntimeProfile(TranslatorProvider provider, string model = null)
        {
            bool reasoning = !string.IsNullOrEmpty(model) && IsReasoningModel(model);

            switch (provider)
            {
                case TranslatorProvider.DeepSeek:
                    return new ProviderRuntimeProfile { BatchSize = reasoning ? 6 : 12, FormatRetries = 1, TimeoutFloorSeconds = 300, QualityHintKey = "ATC_Profile_DeepSeek" };
                case TranslatorProvider.Google:
                    return new ProviderRuntimeProfile { BatchSize = 40, FormatRetries = 1, TimeoutFloorSeconds = 60, QualityHintKey = "ATC_Profile_Google" };
                case TranslatorProvider.DeepL:
                    return new ProviderRuntimeProfile { BatchSize = 50, FormatRetries = 0, TimeoutFloorSeconds = 60, QualityHintKey = "ATC_Profile_DeepL" };
                case TranslatorProvider.Custom_OpenAI:
                    return new ProviderRuntimeProfile { BatchSize = reasoning ? 12 : 24, FormatRetries = 1, TimeoutFloorSeconds = 300, QualityHintKey = "ATC_Profile_Custom" };
                case TranslatorProvider.OpenRouter:
                    return new ProviderRuntimeProfile { BatchSize = reasoning ? 12 : 28, FormatRetries = 1, TimeoutFloorSeconds = reasoning ? 300 : 90, QualityHintKey = "ATC_Profile_OpenRouter" };
                default:
                    return new ProviderRuntimeProfile { BatchSize = reasoning ? 16 : 32, FormatRetries = 1, TimeoutFloorSeconds = reasoning ? 300 : 90, QualityHintKey = "ATC_Profile_Default" };
            }
        }


        public static ProviderRuntimeProfile GetCurrentRuntimeProfile()
        {
            ApiKeyConfig config = AutoTranslatorMod.Settings.ApiConfigs
                .FirstOrDefault(IsConfigReady);
            if (config == null)
            {
                return new ProviderRuntimeProfile { BatchSize = 32, FormatRetries = 1, TimeoutFloorSeconds = 90, QualityHintKey = "ATC_Profile_Default" };
            }
            return GetRuntimeProfile(config.Provider, config.SelectedModel);
        }

        private static string GetBaseUrl(ApiKeyConfig config)
        {
            string custom = CleanInput(config.CustomBaseUrl);
            if (!string.IsNullOrEmpty(custom))
            {
                // 借鑑 RimChat 的 Uri 驗證器，自動處理 schema 與格式
                if (!custom.StartsWith("http://") && !custom.StartsWith("https://"))
                    custom = "http://" + custom;

                if (Uri.TryCreate(custom, UriKind.Absolute, out Uri validUri))
                {
                    // Uri 類別會自動幫你處理乾淨所有的結尾斜線跟不合法的空白
                    string cleanUrl = validUri.AbsoluteUri.TrimEnd('/');

                    // 🌟 咪咪終極防呆：大哥們超愛把整串 /chat/completions 貼進來！我們幫他切掉！
                    if (cleanUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanUrl = cleanUrl.Substring(0, cleanUrl.Length - 17); // 切除尾巴
                    }
                    else if (cleanUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanUrl = cleanUrl.Substring(0, cleanUrl.Length - 7);
                    }

                    // 依然保留你的 v1 自動補全規則
                    if (config.Provider != TranslatorProvider.Google &&
                        !cleanUrl.EndsWith("/v1") && !cleanUrl.EndsWith("/v1beta") &&
                        !cleanUrl.EndsWith("/v4") && !cleanUrl.EndsWith("/api"))
                    {
                        cleanUrl += "/v1";
                    }
                    config.CustomBaseUrl = cleanUrl;
                    return cleanUrl;
                }
                else
                {
                    // 將 custom 網址字串當作參數 {0} 傳入 XML
                    Log.Warning($"[AutoTranslationCore] " + "ATC_Warning_InvalidUrlFallback".Translate(custom));
                    return custom;
                }
            }
            // 1. 先處理 DeepL 這個異類 (因為它的網址要看 Key 結尾動態決定)
            if (config.Provider == TranslatorProvider.DeepL)
            {
                return (!string.IsNullOrEmpty(config.Key) && config.Key.Trim().EndsWith(":fx"))
                    ? "https://api-free.deepl.com/v2"
                    : "https://api.deepl.com/v2";
            }

            // 2. 剩下的直接查表！找不到就預設給 OpenAI
            if (ProviderRegistry.TryGetValue(config.Provider, out var def))
            {
                return def.BaseUrl;
            }
            return "https://api.openai.com/v1";
        }

        public static string CleanInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // 🌟 咪咪借鑑 RimChat 的絕招：無差別抹除所有空白！
            // 防止玩家複製 Key 或網址時，中間不小心夾帶了半形空白或不可見字元
            var builder = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (!char.IsWhiteSpace(c) && c >= 32 && c <= 126)
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// 判定是否為 reasoning model（回應慢、不支援 response_format 強制）
        /// 修正 Bug K：DeepSeek Pro / OpenAI o1 系列適配
        /// </summary>
        private static bool IsReasoningModel(string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            string lower = modelName.ToLower();
            return lower.Contains("reasoner") ||      // deepseek-reasoner (DS Pro)
                   lower.Contains("o1-") ||            // OpenAI o1-preview / o1-mini
                   lower.StartsWith("o1") ||
                   lower.Contains("-thinking") ||      // Gemini thinking models
                   lower.Contains("qwq") ||            // 阿里雲 QwQ reasoning
                   lower.Contains("r1");               // DeepSeek R1 / 其他 R1 系列
        }

    }
}
