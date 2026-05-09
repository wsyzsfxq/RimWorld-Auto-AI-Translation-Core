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

        // 🌟 V4.5 新增：靜態建構函式，初始化設定 5 分鐘超時防禦！
        static AutoTranslatorAPI()
        {
            client.Timeout = TimeSpan.FromMinutes(5); // 大幅度放寬超時限制，防止批次翻譯崩潰
        }

        public static string CleanInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return new string(input.Where(c => c >= 32 && c <= 126).ToArray()).Trim();
        }

        private static string GetSystemPrompt()
        {
            var targetLang = AutoTranslatorMod.Settings.TargetLang;
            string targetLanguageName = "";
            string languageSpecificRules = "";

            // 🌟 咪咪特製：順便幫大哥把俄語和烏克蘭語的 AI 提示詞也寫進去了！
            switch (targetLang)
            {
                case TargetLanguage.Traditional:
                    targetLanguageName = "台灣繁體中文 (Traditional Chinese, zh-TW)";
                    languageSpecificRules = "1. 術語轉換：若原文為另一種語系或包含標記，必須強制進行術語轉換（如：質量->品質、信息->訊息、激活->啟動、程序->程式、數據->資料、設置->設定、菜單->選單、屏幕->螢幕、激光->雷射）。\n";
                    break;
                case TargetLanguage.Simplified:
                    targetLanguageName = "大陸簡體中文 (Simplified Chinese, zh-CN)";
                    languageSpecificRules = "1. 術語轉換：若原文為另一種語系或包含標記，必須強制進行術語轉換（如：品質->質量、訊息->信息、啟動->激活、程式->程序、資料->數據、設定->設置、選單->菜單、屏幕->螢幕、激光->雷射）。\n";
                    break;
                case TargetLanguage.Japanese:
                    targetLanguageName = "日本語 (Japanese)";
                    languageSpecificRules = "1. 翻譯風格：請使用符合 RimWorld 遊戲氛圍的自然日語，專有名詞請參考日本玩家常用的片假名或漢字翻譯。\n";
                    break;
                case TargetLanguage.Korean:
                    targetLanguageName = "한국어 (Korean)";
                    languageSpecificRules = "1. 翻譯風格：請使用符合 RimWorld 遊戲氛圍的自然韓語，專有名詞請參考韓國玩家的習慣用語。\n";
                    break;
                case TargetLanguage.Russian:
                    targetLanguageName = "俄語 (Russian, ru-RU)";
                    languageSpecificRules = "1. 翻譯風格：請使用符合 RimWorld 遊戲氛圍的自然俄語，確保名詞的性別和格數正確。\n";
                    break;
                case TargetLanguage.Ukrainian:
                    targetLanguageName = "烏克蘭語 (Ukrainian, uk-UA)";
                    languageSpecificRules = "1. 翻譯風格：請使用符合 RimWorld 遊戲氛圍的自然烏克蘭語，專有名詞請參考烏克蘭玩家的習慣用語。\n";
                    break;
            }

            return $"你是一個只執行翻譯任務並返回純淨 JSON 陣列的 API 端點，專門為《邊緣世界》(RimWorld) 進行在地化工作。\n" +
                   $"任務：將輸入的 JSON 陣列中的字串，翻譯或轉換為風格符合「科幻、生存、硬核」的 {targetLanguageName}。\n\n" +
                   "【嚴格規則】\n" +
                   languageSpecificRules +
                   "2. 佔位符保護：絕對保留所有代碼佔位符！文本中任何由 []、{} 或 <> 包裹的代碼（例如 [PAWN_nameDef], {0}, {1} 等）必須原樣保留，嚴禁更動或翻譯。\n" +
                   "3. 輸出格式：僅返回一個 JSON 陣列 (Array)，順序與長度必須與輸入完全一致。不要任何 Markdown 標記 (如 ```json)，不要多餘的對話或解釋。";
        }

        private static string GetBaseUrl(TranslatorProvider p)
        {
            var s = AutoTranslatorMod.Settings;
            string custom = CleanInput(s.CustomBaseUrl);

            if (!string.IsNullOrEmpty(custom))
            {
                if (custom.EndsWith("/")) custom = custom.Substring(0, custom.Length - 1);

                if (!custom.StartsWith("http://") && !custom.StartsWith("https://"))
                {
                    custom = "https://" + custom;
                }
                return custom;
            }

            switch (p)
            {
                case TranslatorProvider.DeepSeek: return "https://" + "api.deepseek.com/v1";
                case TranslatorProvider.Grok: return "https://" + "api.x.ai/v1";
                case TranslatorProvider.OpenRouter: return "https://" + "openrouter.ai/api/v1";
                case TranslatorProvider.GLM: return "https://" + "open.bigmodel.cn/api/paas/v4";
                case TranslatorProvider.Alibaba: return "https://" + "dashscope.aliyuncs.com/compatible-mode/v1";
                default: return "https://" + "api.openai.com/v1";
            }

        }

        private static Uri CreateAbsoluteUri(string url)
        {
            string safeUrl = url.Trim();
            if (!safeUrl.StartsWith("http://") && !safeUrl.StartsWith("https://"))
            {
                safeUrl = "https://" + safeUrl;
            }
            return new Uri(safeUrl, UriKind.Absolute);
        }

        public static async Task<List<string>> FetchRemoteModelsAsync()
        {
            var s = AutoTranslatorMod.Settings;
            try
            {
                string apiKey = CleanInput(s.ApiKey);

                string url = (s.CurrentProvider == TranslatorProvider.Google)
                    ? $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}"
                    : $"{GetBaseUrl(s.CurrentProvider)}/models";

                url = CleanInput(url);

                Uri requestUri;
                try
                {
                    requestUri = new Uri(url, UriKind.Absolute);
                }
                catch (Exception uriEx)
                {
                    Log.Error($"[AutoTranslationCore] URI 解析致命錯誤！被隱形字元污染？組合出的網址為: '{url}'。錯誤詳情: {uriEx.Message}");
                    return null;
                }

                client.DefaultRequestHeaders.Clear();
                if (s.CurrentProvider != TranslatorProvider.Google && !string.IsNullOrEmpty(apiKey))
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                var res = await client.GetAsync(requestUri);

                if (!res.IsSuccessStatusCode)
                {
                    string err = await res.Content.ReadAsStringAsync();
                    Log.Error($"[AutoTranslationCore] 獲取模型清單失敗 (HTTP {res.StatusCode}): {err}");
                    return null;
                }

                var obj = JObject.Parse(await res.Content.ReadAsStringAsync());
                var list = new List<string>();

                if (s.CurrentProvider == TranslatorProvider.Google)
                {
                    var models = obj["models"];
                    if (models != null)
                    {
                        foreach (var m in models)
                            if (m["supportedGenerationMethods"]?.ToString().Contains("generateContent") == true)
                                list.Add(m["name"].ToString().Replace("models/", ""));
                    }
                }
                else
                {
                    var data = obj["data"];
                    if (data != null)
                    {
                        foreach (var m in data) list.Add(m["id"].ToString());
                    }
                }
                return list.OrderBy(x => x).ToList();
            }
            catch (Exception e)
            {
                Log.Error($"[AutoTranslationCore] 獲取模型清單內部錯誤: {e.Message}");
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
            var s = AutoTranslatorMod.Settings;
            string apiKey = CleanInput(s.ApiKey);

            if (s.CurrentProvider == TranslatorProvider.Google && string.IsNullOrEmpty(apiKey)) return null;

            string model = CleanInput(s.SelectedModel);

            try
            {
                string url = "";
                object payload = null;
                string prompt = GetSystemPrompt();
                string inputJson = JsonConvert.SerializeObject(texts);

                if (s.CurrentProvider == TranslatorProvider.Google)
                {
                    url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
                    payload = new
                    {
                        contents = new[] {
                            new { parts = new[] { new { text = $"{prompt}\n\n現在處理輸入:\n{inputJson}" } } }
                        }
                    };
                }
                else
                {
                    url = $"{GetBaseUrl(s.CurrentProvider)}/chat/completions";
                    client.DefaultRequestHeaders.Clear();
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    }

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

                url = CleanInput(url);

                Uri requestUri;
                try
                {
                    requestUri = new Uri(url, UriKind.Absolute);
                }
                catch (Exception uriEx)
                {
                    Log.Error($"[AutoTranslationCore] URI 解析錯誤！被隱形字元污染？組合出的網址為: '{url}'。錯誤詳情: {uriEx.Message}");
                    return null;
                }

                var res = await client.PostAsync(requestUri, new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));

                if (!res.IsSuccessStatusCode)
                {
                    string err = await res.Content.ReadAsStringAsync();
                    Log.Error($"[AutoTranslationCore] API 請求錯誤 (HTTP {res.StatusCode}): {err}");
                    return null;
                }

                return ParseResponse(await res.Content.ReadAsStringAsync(), s.CurrentProvider, texts.Count);
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException || ex.Message.Contains("canceled") || ex.Message.Contains("aborted"))
                {
                    // 寫在開發者日誌的保留原樣（大哥用來 debug 看的）
                    Log.Warning("[AutoTranslationCore] 翻譯通訊超時 (Timeout)！API 伺服器無回應，系統準備重試。");

                    // 🌟 寫在玩家面板的換成在地化
                    AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_NetworkRetry".Translate());
                }
                else
                {
                    // 🌟 寫在玩家面板異常區的換成在地化
                    AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_LogError_ApiConn".Translate(ex.Message));
                    Log.Error($"[AutoTranslationCore] 翻譯通訊異常: {ex.Message}");
                }

                return null;
            }
        }
        private static List<string> ParseResponse(string json, TranslatorProvider p, int count)
        {
            try
            {
                var obj = JObject.Parse(json);
                string raw = (p == TranslatorProvider.Google)
                    ? obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString()
                    : obj["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(raw)) return null;

                raw = raw.Replace("```json", "").Replace("```", "").Trim();

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
                Log.Error($"[AutoTranslationCore] 解析回應出錯: {e.Message}");
                return null;
            }
        }
    }
}