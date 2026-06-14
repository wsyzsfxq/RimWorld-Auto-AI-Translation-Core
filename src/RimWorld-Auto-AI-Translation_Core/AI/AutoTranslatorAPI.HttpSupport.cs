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
    }

    internal static class UnityWebRequestCompat
    {
        public static bool IsSuccess(UnityEngine.Networking.UnityWebRequest request)
        {
            if (request == null) return false;
#if RIMWORLD_1_5
            return !request.isNetworkError && !request.isHttpError;
#else
            return request.result == UnityEngine.Networking.UnityWebRequest.Result.Success;
#endif
        }
    }
}
