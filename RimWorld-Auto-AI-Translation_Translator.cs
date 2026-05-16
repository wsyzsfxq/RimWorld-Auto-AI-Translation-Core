using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Verse;
using System.Linq;

namespace AutoTranslator_Core
{
    public static class AutoTranslatorAPI
    {
        private static readonly HttpClient client = new HttpClient();

        // 🌟 咪咪特製：輪詢計數器 (負載平衡發牌器)
        private static int currentKeyIndex = 0;

        static AutoTranslatorAPI()
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        }

        public static string CleanInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return new string(input.Where(c => c >= 32 && c <= 126).ToArray()).Trim();
        }

        private static string GetSystemPrompt()
        {
            var targetLang = AutoTranslatorMod.Settings.TargetLang;

            if (targetLang == TargetLanguage.Traditional || targetLang == TargetLanguage.Simplified)
            {
                string targetLanguageName = targetLang == TargetLanguage.Traditional ? "台灣繁體中文 (Traditional Chinese, zh-TW)" : "大陸簡體中文 (Simplified Chinese, zh-CN)";
                string rules = targetLang == TargetLanguage.Traditional ?
                    "1. 術語轉換：若原文為另一種語系，必須強制轉換（例如：質量->品質、信息->訊息、激活->啟動、菜單->選單、程序->程式）。\n" :
                    "1. 术语转换：若原文为另一种语系，必须强制转换（例如：品質->質量、訊息->信息、啟動->激活、選單->菜單、程式->程序）。\n";

                return $"你是一個只執行翻譯任務並返回純淨 JSON 陣列的 API 端點，專門為《邊緣世界》(RimWorld) 進行在地化工作。\n" +
                       $"任務：將輸入的 JSON 陣列中的字串，翻譯或轉換為風格符合「科幻、生存、硬核、黑色幽默」的 {targetLanguageName}。\n\n" +
                       "【嚴格規則】\n" + rules +
                       "2. 佔位符保護：絕對保留所有代碼佔位符！文本中任何由 []、{} 或 <> 包裹的代碼（例如 [PAWN_nameDef], {0}, {1} 等）必須原樣保留，嚴禁更動或翻譯。\n" +
                       "3. 保持語氣：對話應簡短有力，UI 標籤應精確。如果原文含有遊戲術語，請使用對應語系的慣用譯名。\n" +
                       "4. 輸出格式：僅返回一個 JSON 陣列 (Array)，順序與長度必須與輸入完全一致。不要任何 Markdown 標記 (如 ```json)，不要多餘的對話或解釋。";
            }
            else
            {
                string targetLanguageName = "";
                string specificRules = "";

                switch (targetLang)
                {
                    case TargetLanguage.Japanese: targetLanguageName = "Japanese (日本語)"; specificRules = "1. Style: Use natural Japanese suitable for the RimWorld gaming atmosphere. Use appropriate Katakana for sci-fi terms.\n"; break;
                    case TargetLanguage.Korean: targetLanguageName = "Korean (한국어)"; specificRules = "1. Style: Use natural Korean suitable for the RimWorld gaming atmosphere.\n"; break;
                    case TargetLanguage.Russian: targetLanguageName = "Russian (Русский)"; specificRules = "1. Style: Use natural Russian suitable for the RimWorld gaming atmosphere.\n"; break;
                    case TargetLanguage.Ukrainian: targetLanguageName = "Ukrainian (Українська)"; specificRules = "1. Style: Use natural Ukrainian suitable for the RimWorld gaming atmosphere.\n"; break;
                    case TargetLanguage.English: targetLanguageName = "English (US/UK)"; specificRules = "1. Style: Translate foreign text into natural English suitable for the RimWorld gaming atmosphere.\n"; break;
                    default: targetLanguageName = "English"; break;
                }

                return $"You are an API endpoint that ONLY performs translation tasks and returns a pure JSON array. You specialize in localizing the game 'RimWorld'.\n" +
                       $"Task: Translate the strings in the input JSON array into {targetLanguageName}, matching a 'Sci-Fi, Survival, Hardcore' tone.\n\n" +
                       "[STRICT RULES]\n" +
                       specificRules +
                       "2. Placeholder Protection: ABSOLUTELY PRESERVE all code placeholders! (e.g., [PAWN_nameDef], {0}, {1}) MUST be kept exactly as is.\n" +
                       "3. Output Format: Return ONLY a valid JSON Array. The length and order MUST exactly match the input. NO Markdown formatting, NO extra dialogue.";
            }
        }

        private static string GetBaseUrl(ApiKeyConfig config)
        {
            string custom = CleanInput(config.CustomBaseUrl);

            if (!string.IsNullOrEmpty(custom))
            {
                // 🌟 防呆第一步：先把結尾多餘的斜線清掉
                if (custom.EndsWith("/")) custom = custom.Substring(0, custom.Length - 1);

                // 🌟 防呆第二步：補上 http:// (本地端通常是 http)
                if (!custom.StartsWith("http://") && !custom.StartsWith("https://"))
                    custom = "http://" + custom;

                // 🌟 防呆第三步：自動補 /v1 (如果不是 Google 官方，且結尾沒帶版本號)
                if (config.Provider != TranslatorProvider.Google &&
                    !custom.EndsWith("/v1") &&
                    !custom.EndsWith("/v1beta") &&
                    !custom.EndsWith("/v4") &&
                    !custom.EndsWith("/api"))
                {
                    custom += "/v1";
                }

                // 🌟 寫回 config，讓 UI 瞬間自動更新！
                config.CustomBaseUrl = custom;
                return custom;
            }

            switch (config.Provider)
            {
                case TranslatorProvider.Google: return "https://generativelanguage.googleapis.com/v1beta";
                case TranslatorProvider.DeepSeek: return "https://api.deepseek.com/v1";
                case TranslatorProvider.Grok: return "https://api.x.ai/v1";
                case TranslatorProvider.OpenRouter: return "https://openrouter.ai/api/v1";
                case TranslatorProvider.GLM: return "https://open.bigmodel.cn/api/paas/v4";
                case TranslatorProvider.Alibaba: return "https://dashscope.aliyuncs.com/compatible-mode/v1";
                default: return "https://api.openai.com/v1";
            }
        }

        // 🌟 咪咪特製：動態自動獲取模型！傳入特定的配置進行獲取
        public static void AutoFetchForConfig(ApiKeyConfig config)
        {
            config.IsFetching = true;
            Task.Run(async () =>
            {
                try
                {
                    string apiKey = CleanInput(config.Key);
                    string baseUrl = GetBaseUrl(config);
                    bool isGoogleRaw = (config.Provider == TranslatorProvider.Google && string.IsNullOrEmpty(config.CustomBaseUrl));

                    string url = isGoogleRaw ? $"{baseUrl}/models?key={apiKey}" : $"{baseUrl}/models";

                    client.DefaultRequestHeaders.Clear();
                    if (!isGoogleRaw && !string.IsNullOrEmpty(apiKey))
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    var res = await client.GetAsync(new Uri(url));
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

        public static async Task<List<string>> TranslateBatchAsync(List<string> texts)
        {
            if (AutoTranslatorSettings.IsCancellationRequested) return null;

            // 🌟 透過發牌器拿到這次要用的 API 配置
            ApiKeyConfig targetConfig = GetNextConfig();
            if (targetConfig == null) return null;

            string apiKey = CleanInput(targetConfig.Key);
            string model = CleanInput(targetConfig.SelectedModel);
            string baseUrl = GetBaseUrl(targetConfig);

            try
            {
                string url = "";
                object payload = null;
                string prompt = GetSystemPrompt();
                string inputJson = JsonConvert.SerializeObject(texts);


                // 【修改後】✨ 只要是 Google，不管是不是中轉站，都用 Google 格式！
                if (targetConfig.Provider == TranslatorProvider.Google)
                {
                    url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";
                    payload = new
                    {
                        contents = new[] { new { parts = new[] { new { text = $"{prompt}\n\nInput JSON:\n{inputJson}" } } } }
                    };
                }
                else
                {
                    url = $"{baseUrl}/chat/completions";
                    client.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(apiKey))
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                    payload = new
                    {
                        model = string.IsNullOrEmpty(model) ? "local-model" : model,
                        messages = new[] {
                            new { role = "system", content = prompt },
                            new { role = "user", content = inputJson }
                        },
                        response_format = new { type = "json_object" }
                    };
                }

                var res = await client.PostAsync(new Uri(url), new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

                if (!res.IsSuccessStatusCode)
                {
                    string err = await res.Content.ReadAsStringAsync();
                    Log.Error($"[AutoTranslationCore] API Error [{targetConfig.Provider}] (HTTP {res.StatusCode}): {err}");
                    return null;
                }

                // ==========================================
                // 🌟 咪咪計數器啟動！(加在這裡，API 成功回傳才算字數！)
                // ==========================================
                int charCount = texts.Sum(t => t.Length);
                AutoTranslatorMod.Settings.SessionCharCount += charCount;
                AutoTranslatorMod.Settings.TotalCharCount += charCount;
                // ==========================================

                // 【修改後】✨ 傳遞是否為 Google 格式的布林值
                bool expectsGoogleFormat = (targetConfig.Provider == TranslatorProvider.Google);
                return ParseResponse(await res.Content.ReadAsStringAsync(), targetConfig.Provider, texts.Count, expectsGoogleFormat);

            }
            catch (Exception ex)
            {
                Log.Error($"[AutoTranslationCore] Translation API communication error / 翻譯通訊異常: {ex.Message}");
                return null;
            }
        }
        private static List<string> ParseResponse(string json, TranslatorProvider p, int count, bool expectsGoogleFormat)
        {
            try
            {
                var obj = JObject.Parse(json);
                string raw = expectsGoogleFormat
                    ? obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()
                    : obj["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(raw)) return null;

                raw = raw.Replace("```json", "").Replace("```", "").Trim();
                int start = raw.IndexOf('[');
                int end = raw.LastIndexOf(']');
                if (start != -1 && end != -1 && end > start)
                {
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

                return (list != null && list.Count == count) ? list : null;
            }
            catch (Exception e)
            {
                Log.Error($"[AutoTranslationCore] AI response parsing failed / 解析 AI 回應失敗: {e.Message}");
                return null;
            }
        }
    }
}