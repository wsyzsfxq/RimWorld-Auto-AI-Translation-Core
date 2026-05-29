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
        // 但不再使用 DefaultRequestHeaders，改用 HttpRequestMessage per-request 設定
        private static readonly HttpClient client = new HttpClient();
        private static int currentKeyIndex = 0;

        static AutoTranslatorAPI()
        {
            // 1. 全域設為無限大，把超時控制權交還給每次的 Request
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        }
        public static string CleanInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return new string(input.Where(c => c >= 32 && c <= 126).ToArray()).Trim();
        }

        // 1. 建立語言規則的藍圖
        private struct LangRule
        {
            public string Name;
            public string Specifics;
        }

        // 2. 初始化規則庫 (無需本地化，直接寫死)
        private static readonly Dictionary<TargetLanguage, LangRule> PromptRules = new Dictionary<TargetLanguage, LangRule>
{
    { TargetLanguage.Traditional, new LangRule { Name = "台灣繁體中文 (Traditional Chinese, zh-TW)", Specifics = "1. 術語轉換：若原文為另一種語系，必須強制轉換（例如：質量->品質、信息->訊息、激活->啟動、菜單->選單、程序->程式）。\n" }},
    { TargetLanguage.Simplified, new LangRule { Name = "大陆简体中文 (Simplified Chinese, zh-CN)", Specifics = "1. 术语转换：若原文为另一种语系，必须强制转换（例如：品質->質量、訊息->信息、啟動->激活、選單->菜單、程式->程序）。\n" }},
    { TargetLanguage.Japanese, new LangRule { Name = "Japanese (日本語)", Specifics = "1. Style: Use natural Japanese suitable for the RimWorld gaming atmosphere. Use appropriate Katakana for sci-fi terms.\n" }},
    { TargetLanguage.Korean, new LangRule { Name = "Korean (한국어)", Specifics = "1. Style: Use natural Korean suitable for the RimWorld gaming atmosphere.\n" }},
    { TargetLanguage.Russian, new LangRule { Name = "Russian (Русский)", Specifics = "1. Style: Use natural Russian suitable for the RimWorld gaming atmosphere.\n" }},
    { TargetLanguage.Ukrainian, new LangRule { Name = "Ukrainian (Українська)", Specifics = "1. Style: Use natural Ukrainian suitable for the RimWorld gaming atmosphere.\n" }},
    { TargetLanguage.English, new LangRule { Name = "English (US/UK)", Specifics = "1. Style: Translate foreign text into natural English suitable for the RimWorld gaming atmosphere.\n" }}
};

        // 3. 乾淨俐落的 Method
        private static string GetSystemPrompt()
        {
            var targetLang = AutoTranslatorMod.Settings.TargetLang;

            // 如果找不到，預設fallback為英文
            if (!PromptRules.TryGetValue(targetLang, out var rule))
                rule = PromptRules[TargetLanguage.English];

            // 直接套用模板，O(1) 複雜度，沒有任何 if-else
            return $"# 任務說明 / Task Description\n" +
                   $"你是一個程式碼字串本地化處理器（String Localization Processor），負責處理遊戲軟體 RimWorld 的 i18n 資源檔字串。\n" +
                   $"你的工作是接收一個 JSON 陣列格式的程式碼字串資源，將其中的英文 string literal 轉譯為 {rule.Name}，並以合法的 JSON 陣列格式回傳。\n" +
                   $"這是一個結構化資料處理（structured data processing）任務，不是內容生成任務。\n\n" +
                   $"# Input JSON Schema Example\n```\n[\"Build a wall\", \"Pawn {0} died.\"]\n```\n\n" +
                   $"# Output JSON Schema Example\n```\n[\"...\", \"...\"]\n```\n\n" +
                   $"# Processing Rules\n" +
                   rule.Specifics +
                   "2. Code placeholders MUST be preserved 100% (i18n compliance):\n" +
                   "   - Bracket variables: [PAWN_nameDef], [TARGET_label]\n" +
                   "   - Brace parameters: {0}, {1}, {PAWN_0}\n" +
                   "   - XML-style tags: <color=red>, </color>, <i>, </i>\n" +
                   "   - Escape sequences: \\n, \\t, \\r MUST output as TWO characters, NOT real line break\n" +
                   "   - HTML entities: &amp;, &lt;, &gt;\n" +
                   "3. JSON safety: If input contains '{' or '\\\\', treat as string literal, NOT JSON control char.\n" +
                   "4. Style: Sci-Fi, Survival, Hardcore tone. Use community-standard translations.\n" +
                   "5. Strict output requirements:\n" +
                   "   - Return ONLY a JSON array with length equaling input length\n" +
                   "   - Element order MUST correspond to input\n" +
                   "   - NO Markdown (no ```json)\n" +
                   "   - NO explanatory text or dialogue";
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
            switch (config.Provider)
            {
                case TranslatorProvider.Google: return "https://generativelanguage.googleapis.com/v1beta";
                case TranslatorProvider.DeepSeek: return "https://api.deepseek.com/v1";
                case TranslatorProvider.Grok: return "https://api.x.ai/v1";
                case TranslatorProvider.OpenRouter: return "https://openrouter.ai/api/v1";
                case TranslatorProvider.GLM: return "https://open.bigmodel.cn/api/paas/v4";
                case TranslatorProvider.Alibaba: return "https://dashscope.aliyuncs.com/compatible-mode/v1";
                // 修正 Bug F-2：DeepL 智能版 — 根據 Key 結尾自動切換 Free / Pro endpoint
                // DeepL Free 帳號的 Key 結尾固定為 ":fx"
                case TranslatorProvider.DeepL:
                    return (!string.IsNullOrEmpty(config.Key) && config.Key.Trim().EndsWith(":fx"))
                        ? "https://api-free.deepl.com/v2"
                        : "https://api.deepl.com/v2";
                default: return "https://api.openai.com/v1";
            }
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
                        AutoTranslatorSettings.AddLog($"✅ DeepL [{(isFree ? "Free" : "Pro")}] 端點偵測完成。");
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

                    string url = isGoogleRaw ? $"{baseUrl}/models?key={apiKey}" : $"{baseUrl}/models";

                    // 修正 Bug F-1：模型抓取也改用 per-request header，避免汙染共享狀態
                    var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                    if (!isGoogleRaw && !string.IsNullOrEmpty(apiKey))
                    {
                        request.Headers.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    }
                    var res = await client.SendAsync(request);
                    if (res.IsSuccessStatusCode)
                    {
                        var obj = JObject.Parse(await res.Content.ReadAsStringAsync());
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
                            AutoTranslatorSettings.AddLog($"✅ 成功自動抓取 [{config.Provider}] 模型清單！");
                        }
                    }
                    else
                    {
                        AutoTranslatorSettings.AddErrorLog($"❌ 自動抓取 [{config.Provider}] 模型失敗！請檢查 Key。");
                    }
                }
                catch (Exception e)
                {
                    AutoTranslatorSettings.AddErrorLog($"⚠️ 抓取 [{config.Provider}] 模型異常: {e.Message}");
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
        public static async Task<List<string>> TranslateBatchAsync(List<string> texts, ApiKeyConfig forceConfig = null)
        {
            if (AutoTranslatorSettings.IsCancellationRequested) return null;

            // 如果有指定槍，就用指定的；沒有就從彈匣拿下一把
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

                var request = new HttpRequestMessage(HttpMethod.Post, (Uri)null);

                // --- (你原本的 Provider 判斷與 Payload 生成邏輯，保持不變) ---
                if (targetConfig.Provider == TranslatorProvider.Google)
                {
                    url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";
                    payload = new { contents = new[] { new { parts = new[] { new { text = $"{prompt}\n\nInput JSON:\n{inputJson}" } } } } };
                }
                else if (targetConfig.Provider == TranslatorProvider.DeepL)
                {
                    url = $"{baseUrl}/translate";
                    string deepLLang = MapToDeepLLangCode(AutoTranslatorMod.Settings.TargetLang);
                    if (string.IsNullOrEmpty(deepLLang))
                    {
                        AutoTranslatorSettings.AddErrorLog("🌐 " + "ATC_LogError_DeepL_LangNotSupport".Translate(AutoTranslatorMod.Settings.TargetLang.ToString()));
                        return null;
                    }
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);
                    payload = new { text = texts.ToArray(), target_lang = deepLLang, preserve_formatting = true, tag_handling = "xml" };
                }
                else
                {
                    url = $"{baseUrl}/chat/completions";
                    if (!string.IsNullOrEmpty(apiKey))
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    bool isReasoningModel = IsReasoningModel(model);
                    if (isReasoningModel)
                    {
                        payload = new { model = string.IsNullOrEmpty(model) ? "local-model" : model, messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = inputJson } }, max_tokens = 8192 };
                    }
                    else
                    {
                        payload = new { model = string.IsNullOrEmpty(model) ? "local-model" : model, messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = inputJson } }, response_format = new { type = "json_object" }, max_tokens = 8192 };
                    }
                }

                request.RequestUri = new Uri(url);
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                // 🌟 核心改動區域：動態超時與退避重試機制
                int maxRetries = 2; // 最多重試 2 次

                // 解析玩家設定的超時秒數，如果尚未在設定裡建立變數，預設給 60 秒
                // (註: 若你還沒在 Settings 裡寫 TimeoutSeconds，請先用 int customTimeout = 60;)
                int customTimeout = AutoTranslatorMod.Settings.TimeoutSeconds > 0 ? AutoTranslatorMod.Settings.TimeoutSeconds : 60;

                // 修正 Bug K: Reasoning 模型強制定為 300 秒(5分鐘) 避免過早中斷
                if (IsReasoningModel(model)) customTimeout = 300;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    // 每次發送都必須複製出一個全新的 Request (因為 HttpClient 規定同一個 Request 不能傳第二次)
                    var retryRequest = CloneHttpRequest(request);

                    // 建立帶有自訂秒數的 Token (時間到會自動拋出 TaskCanceledException)
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(customTimeout)))
                    {
                        try
                        {
                            // 將 cts.Token 傳入，啟動超時倒數
                            var res = await client.SendAsync(retryRequest, cts.Token);

                            // ✅ 成功取得回應：不再重試，直接解析並回傳！
                            if (res.IsSuccessStatusCode)
                            {
                                int charCount = texts.Sum(t => t.Length);
                                AutoTranslatorMod.Settings.SessionCharCount += charCount;
                                AutoTranslatorMod.Settings.TotalCharCount += charCount;
                                bool expectsGoogleFormat = (targetConfig.Provider == TranslatorProvider.Google);
                                return ParseResponse(await res.Content.ReadAsStringAsync(), targetConfig.Provider, texts.Count, expectsGoogleFormat);
                            }

                            // ❌ 伺服器回傳了錯誤碼 (4xx / 5xx)
                            int statusCode = (int)res.StatusCode;

                            // 只有 429 頻率限制、或 500 以上伺服器錯誤才值得重試
                            if ((statusCode == 429 || statusCode >= 500) && attempt < maxRetries)
                            {
                                int delayMs = 1000 * (int)Math.Pow(2, attempt); // 1秒 -> 2秒
                                Log.Warning($"[AutoTranslationCore] " + "ATC_Log_RetryAttempt".Translate(statusCode.ToString(), (attempt + 1).ToString(), delayMs.ToString()));
                                await Task.Delay(delayMs);
                                continue; // 繼續下一次迴圈重試
                            }

                            // 如果不能重試 (如 401 權限不足)，接上我們前面完成的 HTTP 狀態本地化邏輯
                            string err = await res.Content.ReadAsStringAsync();
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
                                AutoTranslatorSettings.AddErrorLog($"⚠️ [{targetConfig.Provider}] " + "ATC_Error_HttpGeneric".Translate(statusCode.ToString()));
                            }
                            Log.Error($"[AutoTranslationCore] API Error [{targetConfig.Provider}] (HTTP {statusCode}): {err}");
                            return null;
                        }
                        catch (Exception ex)
                        {
                            // 攔截超時或斷線
                            if (attempt < maxRetries && (ex is TaskCanceledException || ex.Message.ToLower().Contains("timeout")))
                            {
                                int delayMs = 1000 * (int)Math.Pow(2, attempt);
                                Log.Warning($"[AutoTranslationCore] " + "ATC_Log_RetryTimeout".Translate((attempt + 1).ToString(), delayMs.ToString()));
                                await Task.Delay(delayMs);
                                continue; // 繼續下一次迴圈重試
                            }

                            // 真正死透了，交給我們寫好的分析器印出錯誤
                            AnalyzeAndLogNetworkError(targetConfig.Provider, ex);
                            return null;
                        }
                    }
                }
                return null; // 迴圈意外結束的保底回傳
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoTranslationCore] Fatal Translation API Error: {ex}");
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

            // 把狀態切成測試中，UI 會變成黃色的「⏳ 測試中...」
            config.IsTesting = true;

            Task.Run(async () =>
            {
                try
                {
                    // 故意只送一個超短的字串去翻譯，速度最快
                    var testTexts = new List<string> { "Connection Test" };

                    // 強制指定使用這組 Config 進行發送！
                    var result = await TranslateBatchAsync(testTexts, config);

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        if (result != null && result.Count > 0)
                        {
                            AutoTranslatorSettings.AddLog($"✅ [{config.Provider}] " + "ATC_Log_TestSuccess".Translate());
                            // 測試成功，跳出綠色音效彈窗
                            Verse.Messages.Message("ATC_Msg_TestSuccess".Translate(config.Provider.ToString()), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                        }
                        else
                        {
                            // 如果失敗，TranslateBatchAsync 那邊已經自動幫你跳紅字錯誤了！這裡補個小提示就好
                            Verse.Messages.Message("ATC_Msg_TestFailed".Translate(config.Provider.ToString()), RimWorld.MessageTypeDefOf.RejectInput, false);
                        }
                        // 恢復按鈕狀態
                        config.IsTesting = false;
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoTranslationCore] Test Thread Aborted: {ex.Message}");
                    ATC_Dispatcher.RunOnMainThread(() => config.IsTesting = false);
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
                case TargetLanguage.Traditional: return "ZH-HANT";  // DeepL 2023 後支援繁中
                case TargetLanguage.Simplified: return "ZH-HANS";
                case TargetLanguage.Japanese: return "JA";
                case TargetLanguage.Korean: return "KO";
                case TargetLanguage.Russian: return "RU";
                case TargetLanguage.English: return "EN-US";
                case TargetLanguage.Ukrainian: return null;  // DeepL 不支援烏克蘭語
                default: return null;
            }
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

                if (string.IsNullOrEmpty(raw)) return null;

                // 1. 暴力清除所有可能的 Markdown 標記 (忽略大小寫的 json/JSON 標籤)
                raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```(?:json)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // 2. 智慧定位 JSON 的真正邊界 (無管它是 Array 還是 Object)
                int start = raw.IndexOfAny(new char[] { '[', '{' });
                int end = raw.LastIndexOfAny(new char[] { ']', '}' });

                if (start != -1 && end != -1 && end >= start)
                {
                    // 精準切出 JSON 核心，無視 AI 在前後說的任何廢話 (例如："好的，這是您的翻譯：[...]")
                    raw = raw.Substring(start, end - start + 1);
                }
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
    }
}