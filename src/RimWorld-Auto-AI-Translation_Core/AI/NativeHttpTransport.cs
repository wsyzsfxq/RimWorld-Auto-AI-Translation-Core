#if RIMWORLD_1_7
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static AutoTranslator_Core.AutoTranslatorAPI;

namespace AutoTranslator_Core
{
    internal static class NativeHttpTransport
    {
        private static readonly HttpClient Client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = false
        }) { Timeout = Timeout.InfiniteTimeSpan };

        // Archive URLs may redirect to storage. Keep that existing download behavior
        // separate from model requests carrying provider credentials.
        private static readonly HttpClient DownloadClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            AllowAutoRedirect = true
        }) { Timeout = Timeout.InfiniteTimeSpan };

        internal static Task<HttpResponseMessage> OpenDownloadResponseAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return DownloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        private static readonly ConcurrentDictionary<long, CancellationTokenSource> ActiveTranslations =
            new ConcurrentDictionary<long, CancellationTokenSource>();
        private static long nextId;

        // Streaming callers own their deadline, response body and disposal. They are not translations.
        internal static Task<HttpResponseMessage> OpenResponseAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        internal static async Task<ATC_WebResponse> SendAsync(
            string url, string method, byte[] body, Dictionary<string, string> headers,
            int timeoutSeconds, Func<bool> cancelled = null, bool translationRequest = false,
            bool requireJson = false, Action onSendStarted = null)
        {
            int seconds = Math.Max(1, timeoutSeconds);
            long id = Interlocked.Increment(ref nextId);
            var result = new ATC_WebResponse
            {
                ErrorText = string.Empty,
                ResponseBody = string.Empty,
                TimeoutSeconds = seconds,
                RequestBodyBytes = body != null ? body.LongLength : 0L,
                FailureStage = "NativeDispatching",
                UnityResult = "HttpClientPending"
            };
            bool sendStarted = false;
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;
            Task<HttpResponseMessage> sendTask = null;
            using (var stopped = new CancellationTokenSource())
            using (var timeout = new CancellationTokenSource())
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token, timeout.Token))
            using (var monitorStopped = new CancellationTokenSource())
            {
                Task monitor = Task.CompletedTask;
                try
                {
                    if (translationRequest) ActiveTranslations.TryAdd(id, stopped);
                    timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                    monitor = MonitorCancellationAsync(cancelled, stopped, monitorStopped.Token);
                    linked.Token.ThrowIfCancellationRequested();

                    request = new HttpRequestMessage(new HttpMethod(method), url);
                    if (body != null)
                    {
                        request.Content = new ByteArrayContent(body);
                        request.Content.Headers.ContentType =
                            new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                    }
                    if (headers != null)
                    {
                        foreach (var header in headers)
                        {
                            // These callers supply UTF-8 JSON bytes; content headers belong to Content.
                            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                            request.Headers.Add(header.Key, header.Value);
                        }
                    }

                    linked.Token.ThrowIfCancellationRequested();
                    // A synchronous dispatch failure has not handed a task to the transport.
                    // A returned task counts as an attempt even if it is already faulted.
                    sendTask = OpenResponseAsync(request, linked.Token);
                    sendStarted = true;
                    result.FailureStage = "NativeWaitingResponseHeaders";
                    onSendStarted?.Invoke();
                    response = await sendTask.ConfigureAwait(false);
                    result.HttpCode = (long)response.StatusCode;

                    result.FailureStage = "NativeReadingResponseBody";
                    byte[] data = await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
                    result.DownloadedBytes = data.LongLength;
                    linked.Token.ThrowIfCancellationRequested();
                    result.FailureStage = "NativeDecodingResponse";
                    // Do not use the gateway's charset declaration to reinterpret JSON bytes.
                    result.ResponseBody = new UTF8Encoding(false, true).GetString(data);
                    linked.Token.ThrowIfCancellationRequested();

                    if (!response.IsSuccessStatusCode)
                    {
                        result.FailureKind = TranslationRequestFailureKind.Http;
                        result.ErrorText = "HTTP " + (int)response.StatusCode;
                        result.FailureStage = "NativeHttpResponse";
                        result.UnityResult = "HttpClientHttpError";
                    }
                    else if (requireJson && !string.Equals(response.Content.Headers.ContentType?.MediaType,
                                 "application/json", StringComparison.OrdinalIgnoreCase))
                    {
                        result.FailureKind = TranslationRequestFailureKind.InvalidResponse;
                        result.ErrorText = "Non-JSON Response";
                        result.FailureStage = "NativeResponseContentType";
                        result.UnityResult = "HttpClientInvalidResponse";
                    }
                    else
                    {
                        result.IsSuccess = true;
                        result.FailureStage = "NativeCompleted";
                        result.UnityResult = "HttpClientSuccess";
                    }
                }
                catch (OperationCanceledException)
                {
                    if (stopped.IsCancellationRequested)
                    {
                        result.FailureKind = TranslationRequestFailureKind.Cancelled;
                        result.ErrorText = "Request cancelled";
                        result.UnityResult = "HttpClientCancelled";
                    }
                    else if (timeout.IsCancellationRequested)
                    {
                        result.FailureKind = TranslationRequestFailureKind.ResponseTimeout;
                        result.ErrorText = "Request timed out after " + seconds + "s";
                        result.UnityResult = "HttpClientTimeout";
                    }
                    else
                    {
                        result.FailureKind = sendStarted
                            ? TranslationRequestFailureKind.Transport : TranslationRequestFailureKind.LocalDispatch;
                        result.ErrorText = "HTTP operation ended unexpectedly";
                        result.UnityResult = "HttpClientInterrupted";
                    }
                }
                catch (DecoderFallbackException)
                {
                    result.FailureKind = TranslationRequestFailureKind.InvalidResponse;
                    result.ErrorText = "The JSON response contains invalid UTF-8 bytes";
                    result.UnityResult = "HttpClientInvalidResponse";
                }
                catch (HttpRequestException error)
                {
                    result.FailureKind = sendStarted
                        ? TranslationRequestFailureKind.Transport : TranslationRequestFailureKind.LocalDispatch;
                    // Exception messages can include URLs or credentials. The framework enum cannot.
                    result.ErrorText = "HTTP transport failed (" + error.HttpRequestError + ")";
                    result.UnityResult = "HttpClientTransportError";
                }
                catch (Exception error)
                {
                    result.FailureKind = sendStarted
                        ? TranslationRequestFailureKind.Transport : TranslationRequestFailureKind.LocalDispatch;
                    result.ErrorText = (sendStarted ? "HTTP request failed (" : "HTTP dispatch failed (") +
                                       error.GetType().Name + ")";
                    result.UnityResult = sendStarted ? "HttpClientTransportError" : "HttpClientDispatchError";
                }
                finally
                {
                    // If the send-start callback fails, the transport still owns a real task.
                    // Cancel and await it before releasing request resources or the caller's slot.
                    if (sendTask != null && response == null)
                    {
                        CancelQuietly(stopped);
                        try { response = await sendTask.ConfigureAwait(false); }
                        catch { /* The failure above already describes the operation. */ }
                        if (response != null) result.HttpCode = (long)response.StatusCode;
                    }
                    bool responseDisposed = DisposeResource(response);
                    bool requestDisposed = DisposeResource(request);
                    if ((!responseDisposed || !requestDisposed) && result.IsSuccess)
                    {
                        result.IsSuccess = false;
                        result.FailureKind = TranslationRequestFailureKind.Transport;
                        result.ErrorText = "HTTP resource cleanup failed";
                        result.FailureStage = "NativeCleanup";
                        result.UnityResult = "HttpClientCleanupError";
                    }
                    CancelQuietly(monitorStopped);
                    await monitor.ConfigureAwait(false);
                    ActiveTranslations.TryRemove(id, out _);
                }
            }
            // HttpClient exposes no reliable per-request upload progress here. Keep those
            // counters at zero/unknown, rather than presenting payload length as bytes sent.
            return result;
        }

        private static async Task MonitorCancellationAsync(
            Func<bool> cancelled, CancellationTokenSource stopped, CancellationToken monitorStopped)
        {
            if (cancelled == null) return;
            try
            {
                while (!monitorStopped.IsCancellationRequested)
                {
                    bool shouldStop;
                    try { shouldStop = cancelled(); }
                    catch { shouldStop = true; }
                    if (shouldStop) { CancelQuietly(stopped); return; }
                    await Task.Delay(50, monitorStopped).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        internal static void AbortTranslations()
        {
            foreach (var request in ActiveTranslations.Values) CancelQuietly(request);
        }

        private static void CancelQuietly(CancellationTokenSource cancellation)
        {
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            catch (AggregateException) { /* Cancellation remains signalled if a callback fails. */ }
        }

        private static bool DisposeResource(IDisposable resource)
        {
            try { resource?.Dispose(); return true; }
            catch { return false; }
        }
    }
}
#endif
