using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorAPI
    {
        public class ATC_WebRequestEngine : MonoBehaviour
        {
            private static ATC_WebRequestEngine _instance;
            private static readonly object _instanceLock = new object();

            private readonly Dictionary<int, ActiveTranslationRequest> activeRequests = new Dictionary<int, ActiveTranslationRequest>();
            private int nextRequestId;

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
                                GameObject go = new GameObject("ATC_WebRequestEngine_Unkillable");
                                Object.DontDestroyOnLoad(go);
                                _instance = go.AddComponent<ATC_WebRequestEngine>();
                            }
                        }
                    }
                    return _instance;
                }
            }

            public int FireRequest(string url, string jsonBody, string apiKey, TranslatorProvider provider, int timeoutSeconds, TaskCompletionSource<ATC_WebResponse> tcs)
            {
                if (tcs == null) return -1;

                int requestId = ++nextRequestId;
                ActiveTranslationRequest active = new ActiveTranslationRequest
                {
                    Id = requestId,
                    Completion = tcs
                };

                activeRequests[requestId] = active;
                active.Coroutine = StartCoroutine(ExecuteRequestCoroutine(active, url, jsonBody, apiKey, provider, timeoutSeconds));
                return requestId;
            }

            public void AbortRequest(int requestId, string reason)
            {
                if (requestId <= 0) return;

                ActiveTranslationRequest active;
                if (!activeRequests.TryGetValue(requestId, out active)) return;

                FinishRequest(active, CreateCancelledResponse(reason), true);
            }

            public void AbortAllRequests(string reason)
            {
                List<ActiveTranslationRequest> requests = activeRequests.Values.ToList();
                for (int i = 0; i < requests.Count; i++)
                {
                    FinishRequest(requests[i], CreateCancelledResponse(reason), true);
                }
            }

            private IEnumerator ExecuteRequestCoroutine(ActiveTranslationRequest active, string url, string jsonBody, string apiKey, TranslatorProvider provider, int timeoutSeconds)
            {
                using (UnityWebRequest webRequest = new UnityWebRequest(url, "POST"))
                {
                    active.WebRequest = webRequest;

                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");

                    string trimmedApiKey = apiKey != null ? apiKey.Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(trimmedApiKey))
                    {
                        if (provider == TranslatorProvider.DeepL)
                        {
                            webRequest.SetRequestHeader("Authorization", "DeepL-Auth-Key " + trimmedApiKey);
                        }
                        else if (provider != TranslatorProvider.Google)
                        {
                            webRequest.SetRequestHeader("Authorization", "Bearer " + trimmedApiKey);
                        }
                    }

                    webRequest.timeout = timeoutSeconds > 0 ? timeoutSeconds : 60;

                    UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();
                    while (!operation.isDone)
                    {
                        if (active.IsCompleted)
                        {
                            yield break;
                        }

                        if (AutoTranslatorSettings.IsCancellationRequested)
                        {
                            FinishRequest(active, CreateCancelledResponse("Pipeline cancellation requested"), true);
                            yield break;
                        }

                        yield return null;
                    }

                    if (active.IsCompleted)
                    {
                        yield break;
                    }

                    string safeText = string.Empty;
                    if (webRequest.downloadHandler != null)
                    {
                        byte[] rawData = webRequest.downloadHandler.data;
                        if (rawData != null && rawData.Length > 0)
                        {
                            try
                            {
                                Encoding tolerantUtf8 = new UTF8Encoding(false, false);
                                safeText = tolerantUtf8.GetString(rawData);
                            }
                            catch
                            {
                                safeText = webRequest.downloadHandler.text ?? string.Empty;
                            }
                        }
                    }

                    ATC_WebResponse response = new ATC_WebResponse
                    {
                        HttpCode = webRequest.responseCode,
                        ErrorText = webRequest.error ?? string.Empty,
                        ResponseBody = safeText
                    };
                    response.IsSuccess = UnityWebRequestCompat.IsSuccess(webRequest);

                    FinishRequest(active, response, false);
                }
            }

            private void FinishRequest(ActiveTranslationRequest active, ATC_WebResponse response, bool abortWebRequest)
            {
                if (active == null || active.IsCompleted) return;
                active.IsCompleted = true;

                if (activeRequests.ContainsKey(active.Id))
                {
                    activeRequests.Remove(active.Id);
                }

                if (abortWebRequest && active.WebRequest != null)
                {
                    try { active.WebRequest.Abort(); } catch { }
                }

                if (abortWebRequest && active.Coroutine != null)
                {
                    try { StopCoroutine(active.Coroutine); } catch { }
                }

                active.Completion.TrySetResult(response);
            }

            private static ATC_WebResponse CreateCancelledResponse(string reason)
            {
                return new ATC_WebResponse
                {
                    IsSuccess = false,
                    HttpCode = 0,
                    ErrorText = reason ?? "Cancelled",
                    ResponseBody = string.Empty
                };
            }

            private class ActiveTranslationRequest
            {
                public int Id;
                public UnityWebRequest WebRequest;
                public Coroutine Coroutine;
                public TaskCompletionSource<ATC_WebResponse> Completion;
                public bool IsCompleted;
            }
        }

        public static void AbortActiveTranslationRequests(string reason)
        {
            if (UnityData.IsInMainThread)
            {
                ATC_WebRequestEngine.Instance.AbortAllRequests(reason);
                return;
            }

            ATC_Dispatcher.RunOnMainThread(() =>
            {
                ATC_WebRequestEngine.Instance.AbortAllRequests(reason);
            });
        }

        private static void AbortTranslationRequest(int requestId, string reason)
        {
            if (requestId <= 0) return;

            if (UnityData.IsInMainThread)
            {
                ATC_WebRequestEngine.Instance.AbortRequest(requestId, reason);
                return;
            }

            ATC_Dispatcher.RunOnMainThread(() =>
            {
                ATC_WebRequestEngine.Instance.AbortRequest(requestId, reason);
            });
        }
    }
}
