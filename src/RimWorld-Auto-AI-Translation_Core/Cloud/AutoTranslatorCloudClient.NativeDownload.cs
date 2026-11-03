#if RIMWORLD_1_7
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorCloudClient
    {
        private static async Task<CloudDownloadAttemptResult> DownloadCloudArchiveNativeAttemptAsync(
            string url,
            string tempFilePath,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            var result = new CloudDownloadAttemptResult
            {
                FilePath = tempFilePath,
                Error = string.Empty
            };
            int seconds = Math.Max(1, timeoutSeconds);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDeleteCloudTempFile(tempFilePath);
                using (var timeout = new CancellationTokenSource())
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                           cancellationToken, timeout.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                    try
                    {
                        bool archiveDownloaded = false;
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        using (HttpResponseMessage response = await NativeHttpTransport.OpenDownloadResponseAsync(
                                   request, linked.Token).ConfigureAwait(false))
                        {
                            linked.Token.ThrowIfCancellationRequested();
                            result.StatusCode = (long)response.StatusCode;
                            result.ServedRecordId = ReadCloudArchiveHeader(response, "X-ATC-Record-Id");
                            result.FallbackReason = ReadCloudArchiveHeader(response, "X-ATC-Record-Fallback");
                            if (!response.IsSuccessStatusCode)
                            {
                                result.Error = "HTTP " + (int)response.StatusCode;
                            }
                            else
                            {
                                using (Stream source = await response.Content.ReadAsStreamAsync(linked.Token)
                                           .ConfigureAwait(false))
                                using (var destination = new FileStream(tempFilePath, FileMode.Create,
                                           FileAccess.Write, FileShare.None, 81920, useAsync: true))
                                {
                                    await source.CopyToAsync(destination, 81920, linked.Token).ConfigureAwait(false);
                                    await destination.FlushAsync(linked.Token).ConfigureAwait(false);
                                }
                                archiveDownloaded = true;
                            }
                        }
                        // The archive and HTTP response are closed before the caller can extract or delete it.
                        linked.Token.ThrowIfCancellationRequested();
                        result.Success = archiveDownloaded;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // A failed body transfer is a transport failure even after a successful HTTP header.
                        // Report zero so the existing retry policy can retry this incomplete archive.
                        result.StatusCode = 0L;
                        result.Error = timeout.IsCancellationRequested
                            ? "Cloud download timed out after " + seconds + "s"
                            : "Cloud download failed (" + error.GetType().Name + ")";
                    }
                }
                return result;
            }
            finally
            {
                if (!result.Success) TryDeleteCloudTempFile(tempFilePath);
            }
        }

        private static string ReadCloudArchiveHeader(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        }
    }
}
#endif
