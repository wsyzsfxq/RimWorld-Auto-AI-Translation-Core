using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorCloudClient
    {

        public static async Task<List<CloudModRecord>> FetchRegistryAsync()
        {
            int maxRetries = 4;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                {
                    CloudApiBaseUrl = BackupApiBaseUrl;
                    LogCloudTranslatedWarning("ATC_Cloud_MainRouteCrashed");
                }

                try
                {
                    string url = $"{CloudApiBaseUrl}/registry?t={DateTime.UtcNow.Ticks}";

                    // 🌟 架構師核彈級替換：徹底捨棄 Mono 充滿 Bug 的 HttpClient！
                    // 改用 Unity 原生 C++ 網路引擎 (UnityWebRequest)，完全無視 Windows GBK 語系干擾！
                    var tcs = new TaskCompletionSource<string>();
                    int timeoutSeconds = 15 + attempt * 15;

                    // 必須在主執行緒發射 UnityWebRequest
                    // 必須在主執行緒發射 UnityWebRequest
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            var request = UnityEngine.Networking.UnityWebRequest.Get(url);
                            request.timeout = timeoutSeconds;

                            var operation = request.SendWebRequest();

                            // 利用 completed 回調，不需要寫 Coroutine 就能完成異步等待
                            operation.completed += (op) =>
                            {
                                try
                                {
                                    if (UnityWebRequestCompat.IsSuccess(request))
                                    {
                                        // 🌟 終極防禦：直接抓最純粹的 byte[]，用寬容模式解析 UTF-8！
                                        byte[] rawData = request.downloadHandler.data;
                                        if (rawData != null && rawData.Length > 0)
                                        {
                                            System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                                            string json = tolerantUtf8.GetString(rawData);
                                            tcs.TrySetResult(json);
                                        }
                                        else
                                        {
                                            tcs.TrySetResult(null);
                                        }
                                    }
                                    else
                                    {
                                        // 把網路錯誤拋出去，讓外層的 Catch 捕捉並進入指數退避重試
                                        tcs.TrySetException(new Exception(request.error));
                                    }
                                }
                                catch (Exception innerEx)
                                {
                                    tcs.TrySetException(innerEx);
                                }
                                finally
                                {
                                    request.Dispose(); // 絕對不能漏掉釋放記憶體
                                }
                            };
                        }
                        catch (Exception dispatchEx)
                        {
                            tcs.TrySetException(dispatchEx);
                        }
                    });

                    // 背景執行緒非阻塞等待主執行緒空投資料
                    string jsonResponse = await WaitForCloudTask(tcs.Task, timeoutSeconds + 10, "registry fetch");

                    if (!string.IsNullOrEmpty(jsonResponse))
                    {
                        var records = JsonConvert.DeserializeObject<List<CloudModRecord>>(jsonResponse);
                        return records ?? new List<CloudModRecord>();
                    }
                }
                catch (Exception ex)
                {
                    // 攔截到錯誤（包含 Timeout），如果已經是最後一次重試就報錯
                    if (attempt == maxRetries)
                    {
                        LogCloudTranslatedWarning("ATC_Cloud_ConnectionFailed", ex.Message);
                        return null;
                    }
                }

                // 觸發退避重試機制
                int delayMs = (int)Math.Pow(2, attempt + 1) * 1000 + new System.Random().Next(100, 500);
                LogCloudTranslatedMessage("ATC_Cloud_RetryAttemptLog", attempt + 1);
                await Task.Delay(delayMs);
            }
            return null;
        }
    }
}
