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
        // 共享的 HttpClient 維持單例（避免 socket exhaustion）
        // 🌟 咪咪終極換血：拔除 Mono 引擎充滿 Bug 的自動解壓縮功能！
        private static readonly HttpClient client = new HttpClient();
        private static int currentKeyIndex = 0;

        static AutoTranslatorAPI()
        {
            // 🌟 咪咪特製修復 1：強制啟用 TLS 1.2，防止 RimWorld 底層 Mono 引擎加密協定太舊被 DeepSeek 踢掉
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            // 1. 全域設為無限大，把超時控制權交還給每次的 Request
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

            // 🌟 架構師終極修復：拔除假 Chrome 偽裝！
            // 原因：Cloudflare 抓機器人非常嚴格，偽裝成 Chrome 但底層 TLS 指紋不符，會被 100% 判定為惡意爬蟲並阻斷連線。
            // 解法：老實坦白我們是 API 客戶端，或者給一個專屬名稱，Cloudflare 反而會直接放行。
            client.DefaultRequestHeaders.Add("User-Agent", "RimWorld-ATC-Client/4.9");

            // 🌟 咪咪特製修復 3：主動告訴伺服器「我只接收 JSON」，避免伺服器出錯時塞整頁 HTML 網頁過来導致解析崩潰
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // 🌟 咪咪特製修復 4：強制要求伺服器回傳「無壓縮」的純文字 (identity)
            // 徹底繞過 RimWorld 底層 Mono 引擎解壓縮失敗導致的 Illegal byte sequence 報錯！
            client.DefaultRequestHeaders.AcceptEncoding.Clear();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
        }
        public static readonly Dictionary<TranslatorProvider, ProviderDef> ProviderRegistry = new Dictionary<TranslatorProvider, ProviderDef>
        {
            { TranslatorProvider.Google, new ProviderDef { BaseUrl = "https://generativelanguage.googleapis.com/v1beta", ListModelsUrl = "https://generativelanguage.googleapis.com/v1beta/models" } },
            { TranslatorProvider.DeepSeek, new ProviderDef { BaseUrl = "https://api.deepseek.com/v1", ListModelsUrl = "https://api.deepseek.com/models" } },
            { TranslatorProvider.Grok, new ProviderDef { BaseUrl = "https://api.x.ai/v1", ListModelsUrl = "https://api.x.ai/v1/models" } },
            { TranslatorProvider.OpenRouter, new ProviderDef { BaseUrl = "https://openrouter.ai/api/v1", ListModelsUrl = "https://openrouter.ai/api/v1/models" } },
            { TranslatorProvider.GLM, new ProviderDef { BaseUrl = "https://open.bigmodel.cn/api/paas/v4", ListModelsUrl = "https://open.bigmodel.cn/api/paas/v4/models" } },
            { TranslatorProvider.Alibaba, new ProviderDef { BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", ListModelsUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/models" } }
        };
        // 2. 初始化規則庫 (加入 7 種歐美主流語言)
        private static readonly Dictionary<TargetLanguage, LangRule> PromptRules = new Dictionary<TargetLanguage, LangRule>
        {
            { TargetLanguage.Traditional, new LangRule { Name = "台灣繁體中文 (Traditional Chinese, zh-TW)", Specifics = "1. 術語轉換：若原文為另一種語系，必須強制轉換（例如：質量->品質、信息->訊息、激活->啟動、菜單->選單、程序->程式）。\n" }},
            { TargetLanguage.Simplified, new LangRule { Name = "大陆简体中文 (Simplified Chinese, zh-CN)", Specifics = "1. 术语转换：若原文为另一种语系，必须强制转换（例如：品質->質量、訊息->信息、啟動->激活、選單->菜單、程式->程序）。\n" }},
            { TargetLanguage.Japanese, new LangRule { Name = "Japanese (日本語)", Specifics = "1. Style: Use natural Japanese suitable for the RimWorld gaming atmosphere. Use appropriate Katakana for sci-fi terms.\n" }},
            { TargetLanguage.Korean, new LangRule { Name = "Korean (한국어)", Specifics = "1. Style: Use natural Korean suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Russian, new LangRule { Name = "Russian (Русский)", Specifics = "1. Style: Use natural Russian suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Ukrainian, new LangRule { Name = "Ukrainian (Українська)", Specifics = "1. Style: Use natural Ukrainian suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.English, new LangRule { Name = "English (US/UK)", Specifics = "1. Style: Translate foreign text into natural English suitable for the RimWorld gaming atmosphere.\n" }},
            // ✨ 架構師擴充：新用語系規則
            { TargetLanguage.French, new LangRule { Name = "French (Français)", Specifics = "1. Style: Use natural French suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.German, new LangRule { Name = "German (Deutsch)", Specifics = "1. Style: Use natural German suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Spanish, new LangRule { Name = "Spanish (Español)", Specifics = "1. Style: Use natural Spanish suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Italian, new LangRule { Name = "Italian (Italiano)", Specifics = "1. Style: Use natural Italian suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Polish, new LangRule { Name = "Polish (Polski)", Specifics = "1. Style: Use natural Polish suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Portuguese, new LangRule { Name = "Brazilian Portuguese (Português do Brasil)", Specifics = "1. Style: Use natural Brazilian Portuguese suitable for the RimWorld gaming atmosphere.\n" }},
            { TargetLanguage.Turkish, new LangRule { Name = "Turkish (Türkçe)", Specifics = "1. Style: Use natural Turkish suitable for the RimWorld gaming atmosphere.\n" }}
        };
        /*
            ██╗     ██╗████████╗███████╗
            ██║     ██║╚══██╔══╝██╔════╝
            ██║   █╗ ██║   ██║   █████╗  
            ██║███╗██║   ██║   ██╔══╝  
            ╚███╔███╔╝   ██║   ██║     
             ╚══╝╚══╝    ╚═╝   ╚═╝     
                What The F*** is going on here?! 
*/
        // 🌟 咪咪特製：無限彈匣輪詢發牌器
        public static ApiKeyConfig GetNextConfig()
        {
            var validConfigs = AutoTranslatorMod.Settings.ApiConfigs
                .Where(IsConfigReady)
                .ToList();

            if (validConfigs.Count == 0) return null;
            if (validConfigs.Count == 1) return validConfigs[0];

            int idx = System.Threading.Interlocked.Increment(ref currentKeyIndex);
            return validConfigs[Math.Abs(idx) % validConfigs.Count];
        }

        public static bool IsConfigReady(ApiKeyConfig config)
        {
            return config != null &&
                   config.Enabled &&
                   !string.IsNullOrEmpty(config.Key) &&
                   !string.IsNullOrEmpty(config.SelectedModel);
        }

        public static bool HasAnyReadyConfig()
        {
            return AutoTranslatorMod.Settings.ApiConfigs != null &&
                   AutoTranslatorMod.Settings.ApiConfigs.Any(IsConfigReady);
        }
        // 1. 這是你要用來取代原本 TranslateBatchAsync 的全新完整方法
        // 1. 這是你要用來取代原本 TranslateBatchAsync 的全新完整方法 (純血 UnityWebRequest ＋ 安全日誌版)
        private static async Task<bool> DelayWithPipelineCancellationAsync(int delayMs)
        {
            int remaining = Math.Max(0, delayMs);
            while (remaining > 0)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return false;

                int slice = Math.Min(remaining, 100);
                await Task.Delay(slice);
                remaining -= slice;
            }

            return !AutoTranslatorSettings.IsCancellationRequested;
        }

        private static ATC_WebResponse CreateRequestTimeoutResponse(TranslatorProvider provider, int timeoutSeconds)
        {
            return new ATC_WebResponse
            {
                IsSuccess = false,
                HttpCode = 0,
                ErrorText = $"Request timed out after {timeoutSeconds}s [{provider}]",
                ResponseBody = string.Empty
            };
        }

        private static async Task<bool> WaitForTranslationResponseAsync(Task<ATC_WebResponse> responseTask, int timeoutSeconds)
        {
            int timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            while (!responseTask.IsCompleted)
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return false;
                if (timer.ElapsedMilliseconds >= timeoutMs) return false;

                await Task.Delay(100);
            }

            return true;
        }

        public static async Task<List<string>> TranslateBatchAsync(List<string> texts, ApiKeyConfig forceConfig = null, bool suppressFinalParseError = false)
        {
            if (AutoTranslatorSettings.IsCancellationRequested) return null;

            ApiKeyConfig targetConfig = forceConfig ?? GetNextConfig();
            if (targetConfig == null) return null;

            string apiKey = CleanInput(targetConfig.Key);
            string model = CleanInput(targetConfig.SelectedModel);
            string baseUrl = GetBaseUrl(targetConfig);

            try
            {
                string url;
                object payload;
                TargetLanguage requestTargetLang = AutoTranslatorMod.Settings.TargetLang;
                string prompt = GetSystemPrompt(requestTargetLang);
                string inputJson = JsonConvert.SerializeObject(texts);

                if (targetConfig.Provider == TranslatorProvider.Google)
                {
                    url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";
                    payload = new { contents = new[] { new { parts = new[] { new { text = $"{prompt}\n\nInput JSON:\n{inputJson}" } } } } };
                }
                else if (targetConfig.Provider == TranslatorProvider.DeepL)
                {
                    url = $"{baseUrl}/translate";
                    string deepLLang = MapToDeepLLangCode(requestTargetLang);
                    if (string.IsNullOrEmpty(deepLLang)) return null;
                    payload = new { text = texts.ToArray(), target_lang = deepLLang, preserve_formatting = true, tag_handling = "xml" };
                }
                else
                {
                    url = $"{baseUrl}/chat/completions";
                    bool isReasoningModel = IsReasoningModel(model);
                    int safeMaxTokens = 4096;

                    if (isReasoningModel || targetConfig.Provider == TranslatorProvider.Custom_OpenAI || targetConfig.Provider == TranslatorProvider.DeepSeek)
                    {
                        payload = new { model = string.IsNullOrEmpty(model) ? "local-model" : model, messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = inputJson } }, max_tokens = safeMaxTokens };
                    }
                    else
                    {
                        payload = new { model = string.IsNullOrEmpty(model) ? "local-model" : model, messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = inputJson } }, max_tokens = safeMaxTokens };
                    }
                }
                string jsonPayload = JsonConvert.SerializeObject(payload);
                var profile = GetRuntimeProfile(targetConfig.Provider, model);
                bool isConnectionTestRequest = texts.Count == 1 && texts[0] == "Connection Test";
                int maxRetries = isConnectionTestRequest ? 0 : 2;
                int maxFormatRetries = profile.FormatRetries;
                int formatRetryCount = 0;
                bool hadFormatRetry = false;
                int customTimeout = AutoTranslatorMod.Settings.TimeoutSeconds > 0 ? AutoTranslatorMod.Settings.TimeoutSeconds : 60;
                if (IsReasoningModel(model)) customTimeout = 300;
                customTimeout = Math.Max(customTimeout, profile.TimeoutFloorSeconds);

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (AutoTranslatorSettings.IsCancellationRequested) return null;

                    var tcs = new TaskCompletionSource<ATC_WebResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                    int requestId = -1;
                    System.Diagnostics.Stopwatch requestTimer = System.Diagnostics.Stopwatch.StartNew();
                    AutoTranslatorPerf.BeginApiRequest();
                    ATC_WebResponse resHolder = null;
                    try
                    {
                        ATC_Dispatcher.RunOnMainThread(() =>
                        {
                            if (tcs.Task.IsCompleted)
                            {
                                return;
                            }

                            if (AutoTranslatorSettings.IsCancellationRequested)
                            {
                                tcs.TrySetResult(CreateRequestTimeoutResponse(targetConfig.Provider, 0));
                                return;
                            }

                            try
                            {
                                requestId = ATC_WebRequestEngine.Instance.FireRequest(url, jsonPayload, apiKey, targetConfig.Provider, customTimeout, tcs);
                            }
                            catch (Exception ex)
                            {
                                tcs.TrySetResult(new ATC_WebResponse
                                {
                                    IsSuccess = false,
                                    HttpCode = 0,
                                    ErrorText = ex.Message,
                                    ResponseBody = string.Empty
                                });
                            }
                        });

                        int hardTimeoutSeconds = Math.Max(customTimeout + 10, 30);
                        bool responseReady = await WaitForTranslationResponseAsync(tcs.Task, hardTimeoutSeconds);
                        if (responseReady)
                        {
                            resHolder = await tcs.Task;
                        }
                        else if (AutoTranslatorSettings.IsCancellationRequested)
                        {
                            tcs.TrySetResult(CreateRequestTimeoutResponse(targetConfig.Provider, 0));
                            AbortTranslationRequest(requestId, "Translation request cancelled");
                            return null;
                        }
                        else
                        {
                            tcs.TrySetResult(CreateRequestTimeoutResponse(targetConfig.Provider, hardTimeoutSeconds));
                            AbortTranslationRequest(requestId, "Translation request timed out");
                            resHolder = CreateRequestTimeoutResponse(targetConfig.Provider, hardTimeoutSeconds);
                        }
                    }
                    finally
                    {
                        requestTimer.Stop();
                        AutoTranslatorPerf.EndApiRequest(requestTimer.ElapsedMilliseconds, resHolder != null && resHolder.IsSuccess);
                    }

                    if (AutoTranslatorSettings.IsCancellationRequested) return null;
                    if (resHolder == null) return null;

                    if (resHolder.IsSuccess)
                    {
                        bool expectsGoogleFormat = (targetConfig.Provider == TranslatorProvider.Google);

                        if (isConnectionTestRequest)
                        {
                            return new List<string> { "Connection OK" };
                        }

                        List<string> parsed = ParseResponse(resHolder.ResponseBody, targetConfig.Provider, texts.Count, expectsGoogleFormat);
                        if (parsed != null && parsed.Count == texts.Count)
                        {
                            parsed = NormalizeBatchForTargetLanguage(parsed, requestTargetLang);
                            int charCount = texts.Sum(t => t.Length);
                            AutoTranslatorMod.Settings.SessionCharCount += charCount;
                            AutoTranslatorMod.Settings.TotalCharCount += charCount;

                            if (hadFormatRetry)
                            {
                                AutoTranslatorSettings.AddLog("✅ " + "ATC_Log_AIFormatRecovered".Translate());
                            }

                            return parsed;
                        }

                        if (formatRetryCount < maxFormatRetries && attempt < maxRetries)
                        {
                            hadFormatRetry = true;
                            formatRetryCount++;
                            int baseDelay = 1000 * (int)Math.Pow(2, attempt);
                            int jitter = new System.Random().Next(100, 800);
                            int delayMs = baseDelay + jitter;

                            AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_AIFormatRetry".Translate(formatRetryCount.ToString()));
                            ATC_Dispatcher.RunOnMainThread(() =>
                                Verse.Log.Warning($"[AutoTranslationCore] " + "ATC_Log_AIFormatRetry".Translate(formatRetryCount.ToString()))
                            );

                            if (!await DelayWithPipelineCancellationAsync(delayMs)) return null;
                            continue;
                        }

                        if (!suppressFinalParseError)
                        {
                            AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_Error_ParseFailed".Translate());
                        }
                        return null;
                    }

                    int statusCode = (int)resHolder.HttpCode;

                    if ((statusCode == 429 || statusCode >= 500 || resHolder.HttpCode == 0) && attempt < maxRetries)
                    {
                        int baseDelay = 1000 * (int)Math.Pow(2, attempt);
                        int jitter = new System.Random().Next(100, 800);
                        int delayMs = baseDelay + jitter;

                        // 🛡️ 安全寫入開發者日誌
                        ATC_Dispatcher.RunOnMainThread(() =>
                            Verse.Log.Warning($"[AutoTranslationCore] " + "ATC_Log_RetryAttempt".Translate(statusCode.ToString(), (attempt + 1).ToString(), delayMs.ToString()))
                        );

                        if (!await DelayWithPipelineCancellationAsync(delayMs)) return null;
                        continue;
                    }

                    string errText = resHolder.ErrorText;
                    if (statusCode == 401 || statusCode == 403)
                    {
                        AutoTranslatorSettings.AddErrorLog($"🔒 [{targetConfig.Provider}] " + "ATC_Error_Unauthorized".Translate());
                    }
                    else if (statusCode == 429)
                    {
                        AutoTranslatorSettings.AddErrorLog($"⏱️ [{targetConfig.Provider}] " + "ATC_Error_RateLimit".Translate());
                    }
                    else if (statusCode >= 500)
                    {
                        AutoTranslatorSettings.AddErrorLog($"🔥 [{targetConfig.Provider}] " + "ATC_Error_ServerError".Translate());
                    }
                    else
                    {
                        AutoTranslatorSettings.AddErrorLog($"⚠️ [{targetConfig.Provider}] " + "ATC_Error_HttpGeneric".Translate(statusCode.ToString()) + $" ({errText})");
                    }

                    // 🛡️ 安全寫入開發者日誌
                    ATC_Dispatcher.RunOnMainThread(() =>
                        Verse.Log.Error($"[AutoTranslationCore] UnityWebRequest Package Lost [{targetConfig.Provider}] (HTTP {statusCode}): {errText}\nBody: {resHolder.ResponseBody}")
                    );

                    return null;
                }
                return null;
            }
            catch (Exception ex)
            {
                // 🛡️ 安全寫入開發者日誌
                ATC_Dispatcher.RunOnMainThread(() =>
                    Verse.Log.Error($"[AutoTranslationCore] Fatal Translation Bridge Error: {ex}")
                );
                return null;
            }
        }
    }
        // Translator support methods are split into partial files in AI/AutoTranslatorAPI.*.cs.
}
