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

        public static async Task<bool> DeleteCloudRecordAsync(string packageId, string language, string recordId, string adminToken)
        {
            int maxRetries = 4;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                {
                    CloudApiBaseUrl = BackupApiBaseUrl;
                    LogCloudTranslatedWarning("ATC_Cloud_DeleteFallback");
                }

                try
                {
                    string url = $"{CloudApiBaseUrl}/delete/{packageId}/{language}?recordId={recordId}";

                    var tcs = new TaskCompletionSource<bool>();
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            var request = UnityEngine.Networking.UnityWebRequest.Delete(url);
                            request.SetRequestHeader("X-Admin-Token", adminToken);
                            request.timeout = 30;

                            var operation = request.SendWebRequest();
                            operation.completed += (op) =>
                            {
                                try
                                {
                                    if (UnityWebRequestCompat.IsSuccess(request))
                                        tcs.TrySetResult(true);
                                    else
                                        tcs.TrySetException(new Exception(request.error));
                                }
                                catch (Exception innerEx) { tcs.TrySetException(innerEx); }
                                finally { request.Dispose(); }
                            };
                        }
                        catch (Exception dispatchEx) { tcs.TrySetException(dispatchEx); }
                    });

                    bool success = await WaitForCloudTask(tcs.Task, 40, "cloud delete");
                    if (success) return true;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        LogCloudTranslatedError("ATC_Cloud_DeleteFailed", ex.Message);
                        return false;
                    }
                }
                int delay = (int)Math.Pow(2, attempt + 1) * 1000 + new System.Random().Next(100, 500);
                await Task.Delay(delay);
            }
            return false;
        }
    }
}
