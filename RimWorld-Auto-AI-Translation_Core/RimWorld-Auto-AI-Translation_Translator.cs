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
    public static class AutoTranslatorAPI
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
        public struct ProviderDef
        {
            public string BaseUrl;
            public string ListModelsUrl;
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

        // 🌟 咪咪特製：容錯型位元組讀取器 (專治 Illegal byte sequence 報錯)
        // 🌟 咪咪特製：容錯型位元組讀取器 (專治 Illegal byte sequence 報錯)
        private static async Task<string> SafeReadContentAsync(HttpContent content)
        {
            try
            {
                // 🌟 終極核彈級防禦：直接抽底層 Stream，無視 Mono 文字快取與 GBK 雞婆解碼
                using (var stream = await content.ReadAsStreamAsync())
                using (var reader = new System.IO.StreamReader(stream, new System.Text.UTF8Encoding(false, false)))
                {
                    return await reader.ReadToEndAsync();
                }
            }
            catch
            {
                // 真的爛到連 Stream 都讀不出來就給空字串，死命保護大哥的遊戲不閃退！
                return "";
            }
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

        // 1. 建立語言規則的藍圖
        private struct LangRule
        {
            public string Name;
            public string Specifics;
        }

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
        // 3. 乾淨俐落的 Method
        private static string GetSystemPrompt()
        {
            var targetLang = AutoTranslatorMod.Settings.TargetLang;

            // 如果找不到，預設fallback為英文
            if (!PromptRules.TryGetValue(targetLang, out var rule))
                rule = PromptRules[TargetLanguage.English];

            // ✨ 架構師重裝甲版 (全英文)：利用 AI 對英文指令最高服從度的特性，徹底鎖死輸出格式！
            return $@"You are a top-tier AI translation engine specialized in localizing RimWorld game mods.
Your SOLE task is to translate every string in the provided JSON array into {rule.Name}.

[ABSOLUTE CORE RULES - VIOLATION WILL CAUSE SYSTEM CRASH]
1. Mandatory Format: You MUST return ONLY a valid JSON array.
2. No Nonsense: ABSOLUTELY NO Markdown tags (e.g., ```json or ```). NO greetings, NO concluding remarks, NO explanations, and NO conversational filler like ""Sure"" or ""Here is your translation"".
3. Strict Array Length Match: The returned JSON array MUST have the EXACT SAME number of elements as the input JSON array, and the order MUST match 100%.
4. Original Text Preservation: If a string contains untranslatable code, file paths, or appears to be pure programming code, return the original string exactly as is. NEVER leave it empty.

[TRANSLATION CONTEXT & STYLE]
- The game setting is Sci-Fi, Survival, and Hardcore. Use terminology consistent with the RimWorld community.
{rule.Specifics}

[VARIABLES & SPECIAL CHARACTERS PROTECTION (CRITICAL)]
1. Bracketed Variables: Variables like {{0}}, {{1}}, {{PAWN_nameDef}}, and [TARGET_label] MUST be preserved 100% exactly as they are. Do not add or remove spaces around them.
2. XML Tags: Formatting tags like <color=#FF0000>, </color>, <i>, and <b> MUST be retained intact in their correct positions.
3. Escape Characters: If the original text contains literal escape sequences like \n, \t, or \r, keep them exactly as literal strings. DO NOT output actual line breaks.
4. Quotation Marks: If quotes are needed in the translated text, you MUST escape them properly using backslashes (e.g., \"") to prevent breaking the JSON structure.

[INPUT & OUTPUT EXAMPLES]
Input Example: [""Attack"", ""Pawn {{0}} is dead."", ""<color=red>Warning!</color>""]
Correct Output Example: [""攻擊"", ""殖民者 {{0}} 已經死亡。"", ""<color=red>警告！</color>""]
Wrong Output Example: ```json\n[""攻擊"", ...]``` (WRONG! Markdown is strictly forbidden!)

FINAL STRICT WARNING: Your output MUST start directly with `[` and end directly with `]`. Absolutely NO other text is allowed!";
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

        // 🌟 咪咪特製：動態自動獲取模型！傳入特定的配置進行獲取
        public static void AutoFetchForConfig(ApiKeyConfig config)
        {
            config.IsFetching = true;

            // 修正 Bug F-2：DeepL 沒有「模型」概念，直接根據 Key 結尾填入虛擬模型名
            if (config.Provider == TranslatorProvider.DeepL)
            {
                Task.Run(() =>
                {
                    try
                    {
                        bool isFree = !string.IsNullOrEmpty(config.Key) && config.Key.Trim().EndsWith(":fx");
                        string virtualModel = isFree
                            ? "ATC_Lang_DeepL_FreeNote".Translate().ToString()
                            : "ATC_Lang_DeepL_ProNote".Translate().ToString();

                        config.FetchedModels = new List<string> { virtualModel };
                        config.SelectedModel = virtualModel;
                        AutoTranslatorSettings.AddLog("ATC_Log_DeepL_EndpointDetected".Translate(isFree ? "Free" : "Pro"));
                    }
                    finally
                    {
                        config.IsFetching = false;
                    }
                });
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    string apiKey = CleanInput(config.Key);
                    string baseUrl = GetBaseUrl(config);
                    bool isGoogleRaw = (config.Provider == TranslatorProvider.Google && string.IsNullOrEmpty(config.CustomBaseUrl));

                    string url;
                    // 玩家有自訂 BaseUrl → 只能拼 /models
                    if (!string.IsNullOrEmpty(config.CustomBaseUrl))
                    {
                        url = $"{baseUrl}/models";
                    }
                    // 沒自訂 → 直接使用 ProviderRegistry 預先登記的 ListModelsUrl
                    else if (ProviderRegistry.TryGetValue(config.Provider, out var def))
                    {
                        url = def.ListModelsUrl;
                    }
                    else
                    {
                        url = $"{baseUrl}/models"; // fallback
                    }
                    // Google 需要把 API Key 放在 Query String
                    if (isGoogleRaw)
                    {
                        url += $"?key={apiKey}";
                    }

                    // 🌟 咪咪特製：替抓取模型加上「自動退避重試」！DeepSeek 伺服器太擠了，失敗是家常便饭，必須重試！
                    // 🌟 咪咪特製：替抓取模型加上「自動退避重試」！並全面升級為 UnityWebRequest 引擎，物理免疫編碼衝突！
                    int maxRetries = 2;
                    for (int attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        var tcs = new TaskCompletionSource<ATC_WebResponse>();

                        // 必須在主執行緒發射 UnityWebRequest
                        ATC_Dispatcher.RunOnMainThread(() =>
                        {
                            try
                            {
                                var request = UnityEngine.Networking.UnityWebRequest.Get(url);
                                request.timeout = 15;
                                request.SetRequestHeader("Accept", "application/json");

                                if (!isGoogleRaw && !string.IsNullOrEmpty(apiKey))
                                {
                                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
                                }

                                var operation = request.SendWebRequest();
                                operation.completed += (op) =>
                                {
                                    try
                                    {
                                        var webRes = new ATC_WebResponse
                                        {
                                            IsSuccess = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success,
                                            HttpCode = request.responseCode,
                                            ErrorText = request.error ?? ""
                                        };

                                        // 🌟 終極防禦：直接抓最純粹的 byte[]，用寬容模式解析 UTF-8！
                                        byte[] rawData = request.downloadHandler.data;
                                        if (rawData != null && rawData.Length > 0)
                                        {
                                            System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                                            webRes.ResponseBody = tolerantUtf8.GetString(rawData);
                                        }
                                        else
                                        {
                                            webRes.ResponseBody = "";
                                        }

                                        // 檢查是否回傳了非 JSON 的垃圾網頁 (例如 Cloudflare 阻擋頁面)
                                        string contentType = request.GetResponseHeader("Content-Type");
                                        if (webRes.IsSuccess && (contentType == null || !contentType.Contains("application/json")))
                                        {
                                            // 強制標記為失敗，讓它進去重試或報錯
                                            webRes.IsSuccess = false;
                                            webRes.ErrorText = "Non-JSON Response";
                                        }

                                        tcs.TrySetResult(webRes);
                                    }
                                    catch (Exception ex) { tcs.TrySetException(ex); }
                                    finally { request.Dispose(); }
                                };
                            }
                            catch (Exception ex) { tcs.TrySetException(ex); }
                        });

                        // 背景執行緒乖乖等主執行緒的空投
                        var resHolder = await tcs.Task;

                        if (resHolder.IsSuccess)
                        {
                            string rawResponse = resHolder.ResponseBody;

                            var obj = JObject.Parse(rawResponse);
                            var list = new List<string>();

                            if (isGoogleRaw)
                            {
                                var models = obj["models"];
                                if (models != null)
                                    foreach (var m in models)
                                        if (m["supportedGenerationMethods"]?.ToString().Contains("generateContent") == true)
                                            list.Add(m["name"].ToString().Replace("models/", ""));
                            }
                            else
                            {
                                var data = obj["data"];
                                if (data != null) foreach (var m in data) list.Add(m["id"].ToString());
                            }

                            if (list.Count > 0)
                            {
                                config.FetchedModels = list.OrderBy(x => x).ToList();
                                if (string.IsNullOrEmpty(config.SelectedModel)) config.SelectedModel = config.FetchedModels[0];
                                AutoTranslatorSettings.AddLog("ATC_Log_FetchModelsSuccess".Translate(config.Provider.ToString()));
                            }
                            return; // 成功就跳出迴圈
                        }
                        else
                        {
                            int statusCode = (int)resHolder.HttpCode;

                            // ✨ 原有的重試機制保留：如果是 429 或是 500 以上，且還沒超過重試次數，就等待後重試
                            if ((statusCode == 429 || statusCode >= 500 || statusCode == 0) && attempt < maxRetries)
                            {
                                // 🌟 架構師升級：加入隨機抖動 (Jitter)，打散併發線程的重試時間
                                int baseDelay = 1000 * (int)Math.Pow(2, attempt);
                                int jitter = new System.Random().Next(100, 800);
                                int delayMs = baseDelay + jitter;
                                Verse.Log.Warning($"[AutoTranslationCore] " + "ATC_Log_FetchModelsRetry".Translate(statusCode.ToString(), (attempt + 1).ToString()));
                                await Task.Delay(delayMs);
                                continue;
                            }

                            // ❌ 徹底失敗了，套用大哥提供的新版精準報錯機制！
                            string errorBody = resHolder.ResponseBody;
                            string safeError = errorBody.Length > 200 ? errorBody.Substring(0, 200) + "..." : errorBody;

                            // 寫入開發者日誌
                            Verse.Log.Error($"[AutoTranslationCore] Fetch Models Error [{config.Provider}] HTTP {statusCode}: {errorBody} | ErrorText: {resHolder.ErrorText}");

                            // 友善本地化 Key
                            string friendlyKey = "ATC_Error_FetchModels_Unknown";
                            switch (statusCode)
                            {
                                case 401: friendlyKey = "ATC_Error_FetchModels_Unauthorized"; break;
                                case 403: friendlyKey = "ATC_Error_FetchModels_Forbidden"; break;
                                case 429: friendlyKey = "ATC_Error_FetchModels_RateLimit"; break;
                                default:
                                    if (statusCode >= 500) friendlyKey = "ATC_Error_FetchModels_ServerError";
                                    else if (resHolder.ErrorText == "Non-JSON Response") friendlyKey = "ATC_Error_FetchModels_NonJson";
                                    break;
                            }

                            AutoTranslatorSettings.AddErrorLog(friendlyKey.Translate(
                                config.Provider.ToString(), statusCode.ToString(), safeError));

                            break; // 徹底失敗跳出迴圈
                        }
                    }
                }
                
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddErrorLog("ATC_Error_FetchModelsException".Translate(config.Provider.ToString(), e.Message));
                }
                finally
                {
                    config.IsFetching = false;
                }
            });
        }
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
                .Where(c => !string.IsNullOrEmpty(c.Key) && !string.IsNullOrEmpty(c.SelectedModel))
                .ToList();

            if (validConfigs.Count == 0) return null;
            if (validConfigs.Count == 1) return validConfigs[0];

            int idx = System.Threading.Interlocked.Increment(ref currentKeyIndex);
            return validConfigs[Math.Abs(idx) % validConfigs.Count];
        }

        public static async Task<bool> TestConnectionAsync()
        {
            var res = await TranslateBatchAsync(new List<string> { "Connection Test" });
            return res != null && res.Count > 0;
        }

        // 1. 這是你要用來取代原本 TranslateBatchAsync 的全新完整方法
        // 1. 這是你要用來取代原本 TranslateBatchAsync 的全新完整方法 (純血 UnityWebRequest ＋ 安全日誌版)
        public static async Task<List<string>> TranslateBatchAsync(List<string> texts, ApiKeyConfig forceConfig = null)
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
                string prompt = GetSystemPrompt();
                string inputJson = JsonConvert.SerializeObject(texts);

                if (targetConfig.Provider == TranslatorProvider.Google)
                {
                    url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";
                    payload = new { contents = new[] { new { parts = new[] { new { text = $"{prompt}\n\nInput JSON:\n{inputJson}" } } } } };
                }
                else if (targetConfig.Provider == TranslatorProvider.DeepL)
                {
                    url = $"{baseUrl}/translate";
                    string deepLLang = MapToDeepLLangCode(AutoTranslatorMod.Settings.TargetLang);
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
                        payload = new { model = string.IsNullOrEmpty(model) ? "local-model" : model, messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = inputJson } }, response_format = new { type = "json_object" }, max_tokens = safeMaxTokens };
                    }
                }
                string jsonPayload = JsonConvert.SerializeObject(payload);
                int maxRetries = 2;
                int customTimeout = AutoTranslatorMod.Settings.TimeoutSeconds > 0 ? AutoTranslatorMod.Settings.TimeoutSeconds : 60;
                if (IsReasoningModel(model)) customTimeout = 300;

                if (targetConfig.Provider == TranslatorProvider.Custom_OpenAI || targetConfig.Provider == TranslatorProvider.DeepSeek)
                {
                    customTimeout = Math.Max(customTimeout, 300);
                }

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (AutoTranslatorSettings.IsCancellationRequested) return null;

                    var tcs = new TaskCompletionSource<ATC_WebResponse>();

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        ATC_WebRequestEngine.Instance.FireRequest(url, jsonPayload, apiKey, targetConfig.Provider, customTimeout, tcs);
                    });

                    var resHolder = await tcs.Task;

                    if (resHolder.IsSuccess)
                    {
                        int charCount = texts.Sum(t => t.Length);
                        AutoTranslatorMod.Settings.SessionCharCount += charCount;
                        AutoTranslatorMod.Settings.TotalCharCount += charCount;
                        bool expectsGoogleFormat = (targetConfig.Provider == TranslatorProvider.Google);

                        if (texts.Count == 1 && texts[0] == "Connection Test")
                        {
                            return new List<string> { "Connection OK" };
                        }

                        return ParseResponse(resHolder.ResponseBody, targetConfig.Provider, texts.Count, expectsGoogleFormat);
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

                        await Task.Delay(delayMs);
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
        // 2. 這是重試機制必備的輔助方法 (請直接貼在 TranslateBatchAsync 下方)
        // 因為 HttpClient 不允許你把「已經發送過」的 Request 再次丟進去，我們必須提供一個複製功能。
        private static HttpRequestMessage CloneHttpRequest(HttpRequestMessage req)
        {
            var clone = new HttpRequestMessage(req.Method, req.RequestUri);
            if (req.Content != null)
            {
                // 重新讀取原本的 Payload 內容
                var contentStr = req.Content.ReadAsStringAsync().Result;
                clone.Content = new StringContent(contentStr, Encoding.UTF8, "application/json");
            }
            foreach (var header in req.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }
        private static void AnalyzeAndLogNetworkError(TranslatorProvider provider, Exception ex)
        {
            string msg = ex.Message.ToLower();
            string friendlyError = "ATC_Error_Unknown".Translate(); // 翻譯：未知錯誤

            // 超時診斷
            if (ex is TaskCanceledException || msg.Contains("timeout") || msg.Contains("timed out"))
            {
                friendlyError = "ATC_Error_Timeout".Translate();
            }
            // DNS / 連線失敗診斷 (將 provider 傳入 XML 參數)
            else if (msg.Contains("cannot connect") || msg.Contains("connection refused") || msg.Contains("name resolution"))
            {
                friendlyError = "ATC_Error_Connection".Translate(provider.ToString());
            }
            // 憑證/權限診斷
            else if (msg.Contains("401") || msg.Contains("403") || msg.Contains("unauthorized"))
            {
                friendlyError = "ATC_Error_Unauthorized".Translate();
            }
            // 其他常規 HTTP 異常
            else
            {
                friendlyError = ex.Message;
            }

            // 保持圖示與模組變數框架，僅本地化 "連線異常" 字眼
            AutoTranslatorSettings.AddErrorLog($"⚠️ [{provider}] {"ATC_Error_NetworkAbnormal".Translate()}: {friendlyError}");
            Log.Error($"[AutoTranslationCore] Detailed Exception [{provider}]: {ex}");
        }

        // 🌟 專用 API 連線測試器
        public static void RunConnectionTest(ApiKeyConfig config)
        {
            if (config == null) return;
            config.IsTesting = true;

            // ✨ 架構師修復：解除全域封鎖狀態！
            // 避免玩家按下「停止翻譯」後，殘留的取消標記把連線測試也一起秒殺！
            AutoTranslatorSettings.IsCancellationRequested = false;

            Task.Run(async () =>
            {
                try
                {
                    var testTexts = new List<string> { "Connection Test" };
                    var result = await TranslateBatchAsync(testTexts, config);

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        if (result != null && result.Count > 0)
                        {
                            AutoTranslatorSettings.AddLog($"✅ [{config.Provider}] " + "ATC_Log_TestSuccess".Translate());
                            Verse.Messages.Message("ATC_Msg_TestSuccess".Translate(config.Provider.ToString()), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                        }
                        else
                        {
                            // ✅ 失敗時使用本地化訊息，並提示玩家查看日誌
                            AutoTranslatorSettings.AddErrorLog("ATC_Log_TestFailed_Detail".Translate(config.Provider.ToString()));
                            Verse.Messages.Message("ATC_Msg_TestFailed".Translate(config.Provider.ToString()), RimWorld.MessageTypeDefOf.RejectInput, false);
                        }
                        config.IsTesting = false;
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoTranslationCore] Test Thread Aborted: {ex.Message}");
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        AutoTranslatorSettings.AddErrorLog("ATC_Log_TestException".Translate(config.Provider.ToString(), ex.Message));
                        Verse.Messages.Message("ATC_Msg_TestFailed".Translate(config.Provider.ToString()), RimWorld.MessageTypeDefOf.RejectInput, false);
                        config.IsTesting = false;
                    });
                }
            });
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

        /// <summary>
        /// 將內部 TargetLanguage 映射為 DeepL API 的語言代碼
        /// 回傳 null 代表 DeepL 不支援該語系
        /// </summary>
        private static string MapToDeepLLangCode(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ZH-HANT";
                case TargetLanguage.Simplified: return "ZH-HANS";
                case TargetLanguage.Japanese: return "JA";
                case TargetLanguage.Korean: return "KO";
                case TargetLanguage.Russian: return "RU";
                case TargetLanguage.English: return "EN-US";
                case TargetLanguage.Ukrainian: return null;  // DeepL 不支援烏克蘭文
                // ✨ 架構師擴充：支援 DeepL 新語系
                case TargetLanguage.French: return "FR";
                case TargetLanguage.German: return "DE";
                case TargetLanguage.Spanish: return "ES";
                case TargetLanguage.Italian: return "IT";
                case TargetLanguage.Polish: return "PL";
                case TargetLanguage.Portuguese: return "PT-BR"; // 巴西葡文
                case TargetLanguage.Turkish: return "TR";
                default: return null;
            }
        }
        // 🌟 架構師特製：無敵 JSON 榨汁機核心算法（移植自 RimChat 概念）
        private static string ExtractCleanJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 尋找 JSON 的真正起點
            int start = text.IndexOfAny(new char[] { '[', '{' });
            if (start == -1) return text; // 找不到就退回，讓後續報錯機制處理

            char openChar = text[start];
            char closeChar = openChar == '[' ? ']' : '}';
            int depth = 0;
            bool inString = false;
            bool escape = false;

            // 遍歷字串，利用深度計算精準找到真正的結尾
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == openChar) depth++;
                else if (c == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        // 完美捕捉！無視後面的廢話
                        return text.Substring(start, i - start + 1);
                    }
                }
            }
            return text.Substring(start); // 括號沒對齊的保底機制
        }

        private static List<string> ParseResponse(string json, TranslatorProvider p, int count, bool expectsGoogleFormat)
        {
            try
            {
                var obj = JObject.Parse(json);

                // 修正 Bug F-2：DeepL 有自己的回應格式，不走 LLM 解析流程
                if (p == TranslatorProvider.DeepL)
                {
                    var translations = obj["translations"];
                    if (translations == null) return null;

                    var result = new List<string>();
                    foreach (var item in translations)
                    {
                        result.Add(item["text"]?.ToString() ?? "");
                    }
                    return (result.Count == count) ? result : null;
                }

                string raw = expectsGoogleFormat
                                    ? obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()
                                    : obj["choices"]?[0]?["message"]?["content"]?.ToString();

                // 🛡️ 官方認證 Bug 防禦：如果真的拿到空值，印出警告並回傳 null，讓上層自動進行重試！
                if (string.IsNullOrWhiteSpace(raw))
                {
                    Log.Warning($"[AutoTranslationCore] 解析到空內容 (API 可能觸發了空包彈 Bug)，準備觸發重試機制...");
                    return null;
                }

                // 1. 暴力清除所有可能的 Markdown 標記 (忽略大小寫的 json/JSON 標籤)
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"json?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // 2. 智慧定位 JSON 的真正邊界 (無管它是 Array 還是 Object)
                // 🌟 架構師升級：呼叫無敵 JSON 榨汁機！
                raw = ExtractCleanJson(raw);
                List<string> list = null;
                if (raw.StartsWith("{"))
                {
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(raw);
                    list = dict.Values.FirstOrDefault();
                }
                else
                {
                    list = JsonConvert.DeserializeObject<List<string>>(raw);
                }

                // ✨ 淨化 AI 新吐出來的翻譯換行
                if (list != null && list.Count == count)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(list[i]))
                        {
                            list[i] = list[i].Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");
                        }
                    }
                    return list;
                }
                return null;
            }

            catch (Exception e)
            {
                // UI 面板顯示給玩家看的本地化錯誤
                AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_Error_ParseFailed".Translate());

                // 控制台顯示給開發者看的詳細錯誤，並附上 AI 吐出來的原文(最多印出前 200 字避免洗頻)
                string preview = string.IsNullOrEmpty(json) ? "NULL" : (json.Length > 200 ? json.Substring(0, 200) + "..." : json);
                Log.Error($"[AutoTranslationCore] JSON 解析失敗 (AI 回傳格式異常): {e.Message}\n異常 Payload: {preview}");

                return null;
            }
        }
        // 🌟 架構師縫合：跨執行緒網頁回應載體
        public class ATC_WebResponse
        {
            public bool IsSuccess;
            public long HttpCode;
            public string ErrorText;
            public string ResponseBody;
        }

        // 🌟 架構師縫合：RimChat 同款的「不死發射引擎 (Singleton)」
        // 完美解決場景切換或剛開機時找不到 GameObject 的 Bug！
        public class ATC_WebRequestEngine : UnityEngine.MonoBehaviour
        {
            private static ATC_WebRequestEngine _instance;
            private static readonly object _instanceLock = new object();

            public static ATC_WebRequestEngine Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        lock (_instanceLock)
                        {
                            if (_instance == null)
                            {
                                var go = new UnityEngine.GameObject("ATC_WebRequestEngine_Unkillable");
                                UnityEngine.Object.DontDestroyOnLoad(go);
                                _instance = go.AddComponent<ATC_WebRequestEngine>();
                            }
                        }
                    }
                    return _instance;
                }
            }

            // 核心協程：完全照搬 RimChat 的 UnityWebRequest 處理邏輯
            public void FireRequest(string url, string jsonBody, string apiKey, TranslatorProvider provider, int timeoutSeconds, TaskCompletionSource<ATC_WebResponse> tcs)
            {
                StartCoroutine(ExecuteRequestCoroutine(url, jsonBody, apiKey, provider, timeoutSeconds, tcs));
            }

            private System.Collections.IEnumerator ExecuteRequestCoroutine(
                string url, string jsonBody, string apiKey, TranslatorProvider provider, int timeoutSeconds, TaskCompletionSource<ATC_WebResponse> tcs)
            {
                using (var webRequest = new UnityEngine.Networking.UnityWebRequest(url, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    string trimmedApiKey = apiKey?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(trimmedApiKey))
                    {
                        if (provider == TranslatorProvider.DeepL)
                        {
                            webRequest.SetRequestHeader("Authorization", $"DeepL-Auth-Key {trimmedApiKey}");
                        }
                        // 🌟 咪咪特製修復：Google (Gemini) 已經把 Key 塞在網址裡了，絕對不能送 Bearer 標頭，否則會被當成錯誤的 OAuth2 憑證！
                        else if (provider != TranslatorProvider.Google)
                        {
                            webRequest.SetRequestHeader("Authorization", $"Bearer {trimmedApiKey}");
                        }
                        // (如果玩家有填自訂網址的 Google 代理站，也是直接走網址參數，所以這裡一律排除)
                    }
                    // ... 這裡上方是發射網頁請求的設定 
                    webRequest.timeout = timeoutSeconds;

                    // 倒傳入 Unity 原生無感異步等待，絕不卡死主執行緒
                    yield return webRequest.SendWebRequest();

                    string safeText = string.Empty;
                    if (webRequest.downloadHandler != null)
                    {
                        // 🌟 咪咪大腦縫合點：不讀取會炸裂的 .text，直接抓取底層最純粹的原始 byte 陣列
                        byte[] rawData = webRequest.downloadHandler.data;
                        if (rawData != null && rawData.Length > 0)
                        {
                            try
                            {
                                // 🌟 終極防禦：建立一個「絕對不拋出例外、遇到壞字元自動替換成安全字元」的寬容 UTF-8 解碼器
                                // 這樣就能在代碼層面徹底擺脫 Windows 系統語系編碼（Big5/GBK）對 Mono 運行時的干擾！
                                System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                                safeText = tolerantUtf8.GetString(rawData);
                            }
                            catch
                            {
                                // 萬一極端狀況下連 byte 轉碼都失敗，再用原廠 text 進行保底，死守遊戲不閃退
                                safeText = webRequest.downloadHandler.text ?? string.Empty;
                            }
                        }
                    }

                    // 打包執行結果
                    var response = new ATC_WebResponse
                    {
                        HttpCode = webRequest.responseCode,
                        ErrorText = webRequest.error ?? "",
                        ResponseBody = safeText //  將我們排毒淨化後的安全字串傳回去
                    };
                    response.IsSuccess = (webRequest.result == UnityEngine.Networking.UnityWebRequest.Result.Success);

                    // 將結果彈回背景執行緒，完美喚醒 4.9 版的高速多線程產線
                    tcs.SetResult(response);
                }
            }
        }
    }
}