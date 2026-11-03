using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;
// 這個檔案負責翻譯模型清單抓取。
// EN: This file fetches available translation model lists.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器API 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorAPI.
    public static partial class AutoTranslatorAPI
    {
        // 這個常數定義 模型取得Max自動Retries 的固定值。
        // EN: This constant defines the fixed value for model fetch max auto retries.
        private const int ModelFetchMaxAutoRetries = 3;

        // 這個方法負責取得 模型取得Fingerprint 資料。
        // EN: This method gets model fetch fingerprint.
        public static string GetModelFetchFingerprint(ApiKeyConfig config)
        {
            if (config == null) return "";
            return $"{config.Provider}|{CleanInput(config.CustomBaseUrl)}|{CleanInput(config.Key)}|" +
                   $"{config.UsePlayer2Protocol}|{(config.Player2GameClientId ?? string.Empty).Trim()}";
        }

        // 這個方法負責處理 自動取得For設定 相關流程。
        // EN: This method handles auto fetch for config.
        public static void AutoFetchForConfig(ApiKeyConfig config)
        {
            AutoFetchForConfig(config, false);
        }

        // 這個方法負責處理 Maintain模型取得狀態 相關流程。
        // EN: This method handles maintain model fetch state.
        public static void MaintainModelFetchState()
        {
            if (AutoTranslatorMod.Settings == null) return;

            List<ApiKeyConfig> translationConfigs = AutoTranslatorMod.Settings.ApiConfigs;
            if (translationConfigs != null)
            {
                for (int i = 0; i < translationConfigs.Count; i++)
                {
                    MaintainModelFetchState(translationConfigs[i]);
                }
            }

        }

        // 這個方法負責處理 Maintain模型取得狀態 相關流程。
        // EN: This method handles maintain model fetch state.
        public static void MaintainModelFetchState(ApiKeyConfig config)
        {
            if (config == null) return;

            if (ShouldAutoFetchModels(config))
            {
                AutoFetchForConfig(config);
            }
        }

        // 這個方法負責判斷 Should自動取得Models 條件是否成立。
        // EN: This method checks should auto fetch models.
        private static bool ShouldAutoFetchModels(ApiKeyConfig config)
        {
            if (config == null) return false;
            if (!config.Enabled || config.IsFetching || AutoTranslatorSettings.IsRunning) return false;
            if (Player2LocalApi.IsEnabled(config)) return false;

            string fetchFingerprint = GetModelFetchFingerprint(config);
            return fetchFingerprint != config.lastFetchedKey &&
                   HasModelFetchCredential(config) &&
                   (string.IsNullOrEmpty(config.PendingFetchFingerprint) ||
                    config.PendingFetchFingerprint != fetchFingerprint ||
                    CanAutoRetryModelFetch(config, fetchFingerprint));
        }

        private static bool HasModelFetchCredential(ApiKeyConfig config)
        {
            if (config == null) return false;
            if (Player2LocalApi.IsEnabled(config)) return Player2LocalApi.GetConfigurationProblem(config) == null;
            if (!string.IsNullOrEmpty(CleanInput(config.Key))) return true;

            return config.Provider == TranslatorProvider.Custom_OpenAI &&
                   !string.IsNullOrWhiteSpace(config.CustomBaseUrl);
        }

        // 這個方法負責處理 自動取得For設定 相關流程。
        // EN: This method handles auto fetch for config.
        public static void AutoFetchForConfig(ApiKeyConfig config, bool force)
        {
            if (config == null) return;
            if (!config.Enabled) return;
            if (config.IsFetching && !force) return;
            if (Player2LocalApi.IsEnabled(config))
            {
                // The local client owns model selection; /models is not part of this protocol.
                ResetModelFetchState(config, clearModels: true);
                MarkModelFetchSuccess(config, GetModelFetchFingerprint(config));
                return;
            }
#if !RIMWORLD_1_7
            ATC_Dispatcher.EnsureAlive();
#endif

            if (force)
            {
                ResetModelFetchState(config, clearModels: true);
            }

            string fetchFingerprint = GetModelFetchFingerprint(config);
            if (string.IsNullOrEmpty(fetchFingerprint)) return;

            config.IsFetching = true;
            config.FetchStartedUtcTicks = DateTime.UtcNow.Ticks;
            config.PendingFetchFingerprint = fetchFingerprint;
            int fetchGeneration = ++config.FetchGeneration;
            Func<bool> fetchCancelled = () => config.FetchGeneration != fetchGeneration ||
                                               !config.IsFetching ||
                                               GetModelFetchFingerprint(config) != fetchFingerprint;
            config.FetchedModels.Clear();

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

                        if (config.FetchGeneration != fetchGeneration) return;
                        config.FetchedModels = new List<string> { virtualModel };
                        config.SelectedModel = virtualModel;
                        MarkModelFetchSuccess(config, fetchFingerprint);
                        AutoTranslatorSettings.AddLog(TranslateText("ATC_Log_DeepL_EndpointDetected", isFree ? "Free" : "Pro"));
                    }
                    finally
                    {
                        if (config.FetchGeneration == fetchGeneration)
                        {
                            config.IsFetching = false;
                            config.FetchStartedUtcTicks = 0L;
                        }
                    }
                });
                return;
            }

            Task.Run(async () =>
            {
                bool success = false;

                try
                {
                    if (fetchCancelled()) return;
                    string apiKey = CleanInput(config.Key);
                    string baseUrl = GetBaseUrl(config);
                    bool isGoogleRaw = config.Provider == TranslatorProvider.Google;
                    string url = BuildModelsUrl(config, baseUrl, apiKey, isGoogleRaw);

                    int maxRetries = 2;
                    for (int attempt = 0; attempt <= maxRetries; attempt++)
                    {
                        if (fetchCancelled()) return;
#if RIMWORLD_1_7
                        var headers = ApiRequestHeaders.Build(config.Provider, apiKey);
                        headers["Accept"] = "application/json";
                        var resHolder = await NativeHttpTransport.SendAsync(
                            url, "GET", null, headers, 15, fetchCancelled, false, true);
#else
                        var tcs = new TaskCompletionSource<ATC_WebResponse>();
                        var dispatchStarted = new TaskCompletionSource<bool>();

                        ATC_Dispatcher.RunOnMainThread(() =>
                        {
                            try
                            {
                                if (fetchCancelled())
                                {
                                    dispatchStarted.TrySetResult(false);
                                    return;
                                }

                                var request = UnityEngine.Networking.UnityWebRequest.Get(url);
                                request.timeout = 15;
                                request.SetRequestHeader("Accept", "application/json");

                                foreach (var header in ApiRequestHeaders.Build(config.Provider, apiKey))
                                    request.SetRequestHeader(header.Key, header.Value);

                                var operation = request.SendWebRequest();
                                dispatchStarted.TrySetResult(true);
                                operation.completed += (op) =>
                                {
                                    try
                                    {
                                        var webRes = new ATC_WebResponse
                                        {
                                            IsSuccess = UnityWebRequestCompat.IsSuccess(request),
                                            HttpCode = request.responseCode,
                                            ErrorText = request.error ?? "",
                                            ResponseBody = DecodeResponseBody(request.downloadHandler?.data)
                                        };

                                        string contentType = request.GetResponseHeader("Content-Type");
                                        if (webRes.IsSuccess && (contentType == null || !contentType.Contains("application/json")))
                                        {
                                            webRes.IsSuccess = false;
                                            webRes.ErrorText = "Non-JSON Response";
                                        }

                                        tcs.TrySetResult(webRes);
                                    }
                                    catch (Exception ex)
                                    {
                                        tcs.TrySetException(ex);
                                    }
                                    finally
                                    {
                                        request.Dispose();
                                    }
                                };
                            }
                            catch (Exception ex)
                            {
                                dispatchStarted.TrySetException(ex);
                                tcs.TrySetException(ex);
                            }
                        });

                        // Main-thread dispatch waiting is not network time. Keep the model
                        // lookup queued until Unity actually starts it, unless this fetch
                        // generation is explicitly cancelled/reset.
                        if (!await TranslationRequestSignalWaiter.WaitAsync(
                                dispatchStarted.Task,
                                fetchCancelled))
                        {
                            return;
                        }

                        try
                        {
                            bool requestStarted = await dispatchStarted.Task;
                            if (!requestStarted) return;
                        }
                        catch (Exception ex)
                        {
                            if (fetchCancelled()) return;
                            AutoTranslatorSettings.AddErrorLog(TranslateText("ATC_Error_FetchModelsException", config.Provider.ToString(), ex.Message));
                            ScheduleModelFetchRetry(config, fetchGeneration, fetchFingerprint);
                            break;
                        }

                        var resHolder = await tcs.Task;
#endif
                        if (fetchCancelled() || resHolder.FailureKind == TranslationRequestFailureKind.Cancelled)
                            return;

                        if (resHolder.IsSuccess)
                        {
                            List<string> models = ParseModelList(resHolder.ResponseBody, isGoogleRaw);
                            if (fetchCancelled()) return;
                            if (models.Count > 0)
                            {
                                CaptureModelCapabilities(config, resHolder.ResponseBody, isGoogleRaw);
                                ApiThinkingPolicy.RememberModelCapabilities(config, resHolder.ResponseBody);
                                config.FetchedModels = models.OrderBy(x => x).ToList();
                                if (string.IsNullOrEmpty(config.SelectedModel)) config.SelectedModel = config.FetchedModels[0];
                                MarkModelFetchSuccess(config, fetchFingerprint);
                                AutoTranslatorSettings.AddLog(TranslateText("ATC_Log_FetchModelsSuccess", config.Provider.ToString()));
                                success = true;
                                return;
                            }

                            AutoTranslatorSettings.AddErrorLog(TranslateText("ATC_Error_FetchModels_Unknown", config.Provider.ToString(), "200", "Empty model list"));
                            ScheduleModelFetchRetry(config, fetchGeneration, fetchFingerprint);
                            break;
                        }

                        int statusCode = (int)resHolder.HttpCode;
                        if ((statusCode == 429 || statusCode >= 500 || statusCode == 0) && attempt < maxRetries)
                        {
                            int baseDelay = 1000 * (int)Math.Pow(2, attempt);
                            int jitter = new System.Random().Next(100, 800);
                            Verse.Log.Warning("[AutoTranslationCore] " + TranslateText("ATC_Log_FetchModelsRetry", statusCode.ToString(), (attempt + 1).ToString()));
                            await Task.Delay(baseDelay + jitter);
                            continue;
                        }

                        LogModelFetchError(config, resHolder);
                        ScheduleModelFetchRetry(config, fetchGeneration, fetchFingerprint);
                        break;
                    }
                }
                catch (Exception e)
                {
                    if (fetchCancelled()) return;
                    AutoTranslatorSettings.AddErrorLog(TranslateText("ATC_Error_FetchModelsException", config.Provider.ToString(), e.Message));
                    ScheduleModelFetchRetry(config, fetchGeneration, fetchFingerprint);
                }
                finally
                {
                    if (config.FetchGeneration == fetchGeneration)
                    {
                        config.IsFetching = false;
                        config.FetchStartedUtcTicks = 0L;
                        if (!success && config.PendingFetchFingerprint == fetchFingerprint)
                        {
                            config.lastFetchedKey = "";
                        }
                    }
                }
            });
        }

        // 這個方法負責判斷 Can自動Retry模型取得 條件是否成立。
        // EN: This method checks can auto retry model fetch.
        public static bool CanAutoRetryModelFetch(ApiKeyConfig config, string fingerprint)
        {
            return config != null &&
                   !config.IsFetching &&
                   !string.IsNullOrEmpty(fingerprint) &&
                   fingerprint != config.lastFetchedKey &&
                   config.PendingFetchFingerprint == fingerprint &&
                   config.FetchRetryCount > 0 &&
                   config.FetchRetryCount <= ModelFetchMaxAutoRetries &&
                   config.NextModelFetchRetryUtcTicks > 0 &&
                   DateTime.UtcNow.Ticks >= config.NextModelFetchRetryUtcTicks;
        }

        // 這個方法負責重置 模型取得狀態 狀態。
        // EN: This method resets model fetch state.
        public static void ResetModelFetchState(ApiKeyConfig config, bool clearModels = false)
        {
            if (config == null) return;
            ApiThinkingPolicy.ForgetModelCapabilities(config);
            config.IsFetching = false;
            config.FetchStartedUtcTicks = 0L;
            config.FetchGeneration++;
            config.lastFetchedKey = "";
            config.PendingFetchFingerprint = "";
            config.FetchRetryCount = 0;
            config.NextModelFetchRetryUtcTicks = 0L;
            if (clearModels) config.FetchedModels.Clear();
            if (clearModels && config.FetchedModelSupportedParameters != null)
                config.FetchedModelSupportedParameters.Clear();
            if (clearModels && config.FetchedModelOutputTokenLimits != null)
                config.FetchedModelOutputTokenLimits.Clear();
        }

        // 這個方法負責建立 Models網址 所需資料。
        // EN: This method builds models URL.
        private static string BuildModelsUrl(ApiKeyConfig config, string baseUrl, string apiKey, bool isGoogleRaw)
        {
            string url;
            if (!string.IsNullOrEmpty(config.CustomBaseUrl))
            {
                url = $"{baseUrl}/models";
            }
            else if (ProviderRegistry.TryGetValue(config.Provider, out var def))
            {
                url = def.ListModelsUrl;
            }
            else
            {
                url = $"{baseUrl}/models";
            }

            if (isGoogleRaw)
            {
                url += $"?key={apiKey}";
            }

            return url;
        }

        // 這個方法負責處理 Decode回應Body 相關流程。
        // EN: This method handles decode response body.
        private static string SanitizeUrlForLog(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            int queryStart = url.IndexOf('?');
            return queryStart >= 0 ? url.Substring(0, queryStart) : url;
        }

        private static string DecodeResponseBody(byte[] rawData)
        {
            if (rawData == null || rawData.Length == 0) return "";
            return new System.Text.UTF8Encoding(false, false).GetString(rawData);
        }

        // 這個方法負責解析 模型List 內容。
        // EN: This method parses model list.
        private static List<string> ParseModelList(string rawResponse, bool isGoogleRaw)
        {
            var obj = JObject.Parse(rawResponse);
            var list = new List<string>();

            if (isGoogleRaw)
            {
                var models = obj["models"];
                if (models != null)
                {
                    foreach (var m in models)
                    {
                        if (m["supportedGenerationMethods"]?.ToString().Contains("generateContent") == true)
                        {
                            list.Add(m["name"].ToString().Replace("models/", ""));
                        }
                    }
                }
            }
            else
            {
                var data = obj["data"];
                if (data != null)
                {
                    foreach (var m in data)
                    {
                        list.Add(m["id"].ToString());
                    }
                }
            }

            return list;
        }

        private static void CaptureModelCapabilities(
            ApiKeyConfig config,
            string rawResponse,
            bool isGoogleRaw)
        {
            if (config == null) return;
            config.FetchedModelSupportedParameters =
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            config.FetchedModelOutputTokenLimits =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                JObject root = JObject.Parse(rawResponse ?? string.Empty);
                if (isGoogleRaw)
                {
                    JArray models = root["models"] as JArray;
                    if (models == null) return;
                    foreach (JToken item in models)
                    {
                        string id = item?["name"]?.ToString()?.Replace("models/", string.Empty);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        int? limit = ReadPositiveModelTokenLimit(item?["outputTokenLimit"]);
                        if (limit.HasValue) config.FetchedModelOutputTokenLimits[id] = limit.Value;
                    }
                    return;
                }
                if (config.Provider != TranslatorProvider.OpenRouter) return;
                JArray data = root["data"] as JArray;
                if (data == null) return;
                foreach (JToken item in data)
                {
                    string id = item?["id"]?.ToString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    List<string> parameters = (item["supported_parameters"] as JArray)?
                        .Select(value => value?.ToString())
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? new List<string>();
                    config.FetchedModelSupportedParameters[id] = parameters;
                    int? limit = ReadPositiveModelTokenLimit(
                        item?["top_provider"]?["max_completion_tokens"]);
                    if (limit.HasValue) config.FetchedModelOutputTokenLimits[id] = limit.Value;
                }
            }
            catch (Exception ex)
            {
                Verse.Log.Warning("[AutoTranslationCore] Could not read model capabilities: " + ex.Message);
            }
        }

        private static int? ReadPositiveModelTokenLimit(JToken token)
        {
            if (token == null) return null;
            if (!long.TryParse(token.ToString(), out long value) || value <= 0L) return null;
            return (int)Math.Min(int.MaxValue, value);
        }

        // 這個方法負責標記 模型取得Success 狀態。
        // EN: This method marks model fetch success.
        private static void MarkModelFetchSuccess(ApiKeyConfig config, string fetchFingerprint)
        {
            config.lastFetchedKey = fetchFingerprint;
            config.PendingFetchFingerprint = "";
            config.FetchRetryCount = 0;
            config.NextModelFetchRetryUtcTicks = 0L;
        }

        // 這個方法負責處理 Schedule模型取得Retry 相關流程。
        // EN: This method handles schedule model fetch retry.
        private static void ScheduleModelFetchRetry(ApiKeyConfig config, int fetchGeneration, string fetchFingerprint)
        {
            if (config == null) return;
            if (config.FetchGeneration != fetchGeneration || !config.IsFetching) return;

            string fingerprint = GetModelFetchFingerprint(config);
            if (string.IsNullOrEmpty(fingerprint) || fingerprint != fetchFingerprint) return;

            config.lastFetchedKey = "";
            config.PendingFetchFingerprint = fingerprint;

            if (config.FetchRetryCount >= ModelFetchMaxAutoRetries)
            {
                config.NextModelFetchRetryUtcTicks = 0L;
                return;
            }

            config.FetchRetryCount++;
            int delaySeconds = Math.Min(30, 5 * config.FetchRetryCount);
            config.NextModelFetchRetryUtcTicks = DateTime.UtcNow.AddSeconds(delaySeconds).Ticks;
            AutoTranslatorSettings.AddLog(TranslateText("ATC_Log_FetchModelsAutoRetryQueued", config.Provider.ToString(), delaySeconds.ToString(), config.FetchRetryCount.ToString()));
        }

        // 這個方法負責處理 Log模型取得Error 相關流程。
        // EN: This method handles log model fetch error.
        private static void LogModelFetchError(ApiKeyConfig config, ATC_WebResponse resHolder)
        {
            int statusCode = (int)resHolder.HttpCode;
            string errorBody = resHolder.ResponseBody ?? "";
            string safeError = errorBody.Length > 200 ? errorBody.Substring(0, 200) + "..." : errorBody;

            Verse.Log.Error($"[AutoTranslationCore] Fetch Models Error [{config.Provider}] HTTP {statusCode}: {errorBody} | ErrorText: {resHolder.ErrorText}");

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

            AutoTranslatorSettings.AddErrorLog(TranslateText(friendlyKey, config.Provider.ToString(), statusCode.ToString(), safeError));
        }

        // 這個方法負責翻譯 Text 內容。
        // EN: This method translates text.
        public static string TranslateText(string key, params object[] args)
        {
            string text = key.Translate().ToString();
            for (int i = 0; i < args.Length; i++)
            {
                text = text.Replace("{" + i + "}", args[i]?.ToString() ?? "");
            }

            return text;
        }
    }
}
