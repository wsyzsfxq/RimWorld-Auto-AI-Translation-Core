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


        private static List<string> ParseTranslationPayload(string raw, int count)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            raw = raw.Trim();
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"^\s*```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*```\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            raw = ExtractCleanJson(raw).Trim();

            JToken token = JToken.Parse(raw);
            if (token is JArray directArray)
            {
                return directArray.Select(x => x?.ToString() ?? "").ToList();
            }

            if (token is JObject obj)
            {
                string[] commonKeys = { "translations", "translation", "result", "results", "data", "items", "output" };
                foreach (string key in commonKeys)
                {
                    JToken child = obj[key];
                    if (child is JArray childArray)
                    {
                        return childArray.Select(x => x?.ToString() ?? "").ToList();
                    }
                }

                var numericValues = obj.Properties()
                    .Select(p =>
                    {
                        bool ok = int.TryParse(p.Name, out int n);
                        return new { Prop = p, IsNumber = ok, Number = ok ? n : int.MaxValue };
                    })
                    .Where(x => x.IsNumber)
                    .OrderBy(x => x.Number)
                    .Select(x => x.Prop.Value?.ToString() ?? "")
                    .ToList();

                if (numericValues.Count == count)
                {
                    return numericValues;
                }

                JArray firstArray = obj.Properties()
                    .Select(p => p.Value)
                    .OfType<JArray>()
                    .FirstOrDefault();

                if (firstArray != null)
                {
                    return firstArray.Select(x => x?.ToString() ?? "").ToList();
                }
            }

            return null;
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

                List<string> list = ParseTranslationPayload(raw, count);

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
                // Keep parse failures recoverable here; TranslateBatchAsync decides whether retries are exhausted.
                string preview = string.IsNullOrEmpty(json) ? "NULL" : (json.Length > 200 ? json.Substring(0, 200) + "..." : json);
                Log.Warning($"[AutoTranslationCore] JSON 解析失敗 (AI 回傳格式異常): {e.Message}\n異常 Payload: {preview}");

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

    }
}
