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

        static AutoTranslatorAPI()
        {
            // 考慮到 AI 回應可能很慢，咪咪把超時設長一點
            client.Timeout = TimeSpan.FromMinutes(5);
        }

        public static string CleanInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return new string(input.Where(c => c >= 32 && c <= 126).ToArray()).Trim();
        }

        // 🌟 咪咪 V4.6 終極多語系 Prompt (包含原本完整的規則)
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
                    case TargetLanguage.Japanese:
                        targetLanguageName = "Japanese (日本語)";
                        specificRules = "1. Style: Use natural Japanese suitable for the RimWorld gaming atmosphere. Use appropriate Katakana for sci-fi terms.\n";
                        break;
                    case TargetLanguage.Korean:
                        targetLanguageName = "Korean (한국어)";
                        specificRules = "1. Style: Use natural Korean suitable for the RimWorld gaming atmosphere.\n";
                        break;
                    case TargetLanguage.Russian:
                        targetLanguageName = "Russian (Русский)";
                        specificRules = "1. Style: Use natural Russian suitable for the RimWorld gaming atmosphere.\n";
                        break;
                    case TargetLanguage.Ukrainian:
                        targetLanguageName = "Ukrainian (Українська)";
                        specificRules = "1. Style: Use natural Ukrainian suitable for the RimWorld gaming atmosphere.\n";
                        break;
                    case TargetLanguage.English:
                        targetLanguageName = "English (US/UK)";
                        specificRules = "1. Style: Translate foreign text into natural English suitable for the RimWorld gaming atmosphere.\n";
                        break;
                    default:
                        targetLanguageName = "English";
                        break;
                }

                return $"You are an API endpoint that ONLY performs translation tasks and returns a pure JSON array. You specialize in localizing the game 'RimWorld'.\n" +
                       $"Task: Translate the strings in the input JSON array into {targetLanguageName}, matching a 'Sci-Fi, Survival, Hardcore' tone.\n\n" +
                       "[STRICT RULES]\n" +
                       specificRules +
                       "2. Placeholder Protection: ABSOLUTELY PRESERVE all code placeholders! (e.g., [PAWN_nameDef], {0}, {1}) MUST be kept exactly as is.\n" +
                       "3. Output Format: Return ONLY a valid JSON Array. The length and order MUST exactly match the input. NO Markdown formatting, NO extra dialogue.";
            }
        }

        private static string GetBaseUrl(TranslatorProvider p)
        {
            var s = AutoTranslatorMod.Settings;
            string custom = CleanInput(s.CustomBaseUrl);

            // 🌟 只要大哥有填自定義網址，咪咪就優先用它
            if (!string.IsNullOrEmpty(custom))
            {
                if (custom.EndsWith("/")) custom = custom.Substring(0, custom.Length - 1);
                if (!custom.StartsWith("http://") && !custom.StartsWith("https://")) custom = "https://" + custom;
                return custom;
            }

            switch (p)
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

        public static async Task<List<string>> FetchRemoteModelsAsync()
        {
            var s = AutoTranslatorMod.Settings;
            try
            {
                string apiKey = CleanInput(s.ApiKey);
                string baseUrl = GetBaseUrl(s.CurrentProvider);

                // 判斷是否為 Google 官方格式請求
                bool isGoogleRaw = (s.CurrentProvider == TranslatorProvider.Google && string.IsNullOrEmpty(s.CustomBaseUrl));

                string url = isGoogleRaw
                    ? $"{baseUrl}/models?key={apiKey}"
                    : $"{baseUrl}/models";

                client.DefaultRequestHeaders.Clear();
                if (!isGoogleRaw && !string.IsNullOrEmpty(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var res = await client.GetAsync(new Uri(url));
                if (!res.IsSuccessStatusCode) return null;

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
                return list.OrderBy(x => x).ToList();
            }
            catch (Exception e)
            {
                Log.Error($"[AutoTranslationCore] 獲取模型清單異常: {e.Message}");
                return null;
            }
        }

        public static async Task<bool> TestConnectionAsync()
        {
            var res = await TranslateBatchAsync(new List<string> { "Connection Test" });
            return res != null && res.Count > 0;
        }

        public static async Task<List<string>> TranslateBatchAsync(List<string> texts)
        {
            if (AutoTranslatorSettings.IsCancellationRequested) return null;

            var s = AutoTranslatorMod.Settings;
            string apiKey = CleanInput(s.ApiKey);
            string model = CleanInput(s.SelectedModel);
            string baseUrl = GetBaseUrl(s.CurrentProvider);

            // 針對本地模型或自定義中轉的寬鬆檢查
            if (string.IsNullOrEmpty(apiKey) && string.IsNullOrEmpty(s.CustomBaseUrl) && s.CurrentProvider != TranslatorProvider.Custom_OpenAI)
                return null;

            try
            {
                string url = "";
                object payload = null;
                string prompt = GetSystemPrompt();
                string inputJson = JsonConvert.SerializeObject(texts);

                // 核心修復：如果選擇 Google 但填了中轉站，改走 OpenAI 兼容格式 (中轉站通常是這樣跑的)
                if (s.CurrentProvider == TranslatorProvider.Google && string.IsNullOrEmpty(s.CustomBaseUrl))
                {
                    url = $"{baseUrl}/models/{model}:generateContent?key={apiKey}";
                    payload = new
                    {
                        contents = new[] {
                            new { parts = new[] { new { text = $"{prompt}\n\nInput JSON:\n{inputJson}" } } }
                        }
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
                    Log.Error($"[AutoTranslationCore] API 錯誤 (HTTP {res.StatusCode}): {err}");
                    return null;
                }

                return ParseResponse(await res.Content.ReadAsStringAsync(), s.CurrentProvider, texts.Count);
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoTranslationCore] 翻譯通訊異常: {ex.Message}");
                return null;
            }
        }

        private static List<string> ParseResponse(string json, TranslatorProvider p, int count)
        {
            try
            {
                var obj = JObject.Parse(json);
                var s = AutoTranslatorMod.Settings;

                // 判斷是否走官方 Google 格式
                bool isGoogleRaw = (p == TranslatorProvider.Google && string.IsNullOrEmpty(s.CustomBaseUrl));

                string raw = isGoogleRaw
                    ? obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()
                    : obj["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(raw)) return null;

                // 🌟 咪咪強效清理：只提取 JSON 陣列部分
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
                    // 某些 AI 會返回 { "translations": [...] }
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
                Log.Error($"[AutoTranslationCore] 解析 AI 回應失敗: {e.Message}");
                return null;
            }
        }
    }
}