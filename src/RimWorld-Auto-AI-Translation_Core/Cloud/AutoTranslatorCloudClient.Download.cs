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
        public static async Task<bool> DownloadAndInjectAsync(string packageId, string targetLangFolder, CloudModRecord targetRecord = null, bool requestMemoryDrop = true)
        {
            Verse.ModMetaData targetMod = null;
            int maxRetries = 4;
            byte[] zipBytes = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                {
                    CloudApiBaseUrl = BackupApiBaseUrl;
                }

                try
                {
                    string url = $"{CloudApiBaseUrl}/download/{packageId}/{targetLangFolder}";
                    if (targetRecord != null && !string.IsNullOrEmpty(targetRecord.RecordId))
                    {
                        url += $"?recordId={targetRecord.RecordId}";
                    }

                    var tcs = new TaskCompletionSource<byte[]>();
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            var request = UnityEngine.Networking.UnityWebRequest.Get(url);
                            request.timeout = 120 + attempt * 60;
                            var operation = request.SendWebRequest();
                            operation.completed += (op) =>
                            {
                                try
                                {
                                    if (UnityWebRequestCompat.IsSuccess(request))
                                        tcs.TrySetResult(request.downloadHandler.data);
                                    else
                                        tcs.TrySetException(new Exception(request.error));
                                }
                                catch (Exception innerEx) { tcs.TrySetException(innerEx); }
                                finally { request.Dispose(); }
                            };
                        }
                        catch (Exception dispatchEx) { tcs.TrySetException(dispatchEx); }
                    });

                    int timeoutSeconds = 120 + attempt * 60;
                    zipBytes = await WaitForCloudTask(tcs.Task, timeoutSeconds + 10, "cloud download");
                    if (zipBytes != null && zipBytes.Length > 0) break;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        LogCloudTranslatedError("ATC_Cloud_DownloadRetryFailed", ex.Message);
                        return false;
                    }
                }

                int delayMs = (int)Math.Pow(2, attempt + 1) * 1000 + new System.Random().Next(100, 500);
                await Task.Delay(delayMs);
            }

            if (zipBytes == null || zipBytes.Length == 0) return false;

            try
            {
                foreach (var m in Verse.ModLister.AllInstalledMods)
                {
                    if (m.PackageId.ToLower() == packageId.ToLower()) { targetMod = m; break; }
                }
                if (targetMod != null)
                {
                    List<ModMetaData> listToClear = new List<ModMetaData>();
                    listToClear.Add(targetMod);
                    AutoTranslatorScanner.ClearOldTranslationFiles(listToClear);
                }

                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string extractRoot = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
                System.IO.Directory.CreateDirectory(extractRoot);

                string workspaceDir = System.IO.Path.Combine(packPath, "Upload_Workspace", packageId, targetLangFolder);
                if (System.IO.Directory.Exists(workspaceDir))
                {
                    System.IO.Directory.Delete(workspaceDir, true);
                }
                System.IO.Directory.CreateDirectory(workspaceDir);

                string tempZipFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{packageId}_{targetLangFolder}_cloud.zip");
                System.IO.File.WriteAllBytes(tempZipFile, zipBytes);

                using (var archive = System.IO.Compression.ZipFile.OpenRead(tempZipFile))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        string destPath = System.IO.Path.Combine(extractRoot, entry.FullName);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, true);

                        string wsDestPath = System.IO.Path.Combine(workspaceDir, entry.FullName);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(wsDestPath));
                        entry.ExtractToFile(wsDestPath, true);
                    }
                }
                System.IO.File.Delete(tempZipFile);

                if (targetRecord != null)
                {
                    var meta = new LocalModMeta
                    {
                        OriginalRecordId = targetRecord.RecordId,
                        TargetModVersion = targetRecord.TargetModVersion ?? "Unknown",
                        TranslationDate = targetRecord.TranslationDate,
                        IsSmartMerged = targetRecord.IsSmartMerged,
                        MergedAiCount = targetRecord.MergedAiCount
                    };
                    string cleanPackageId = packageId.Replace(".", "_").ToLower();
                    string metaPath = System.IO.Path.Combine(extractRoot, $"{cleanPackageId}_ATC_Meta.json");
                    System.IO.File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
                }

                if (requestMemoryDrop)
                {
                    AutoTranslatorLegacyRepairer.RepairPackage(packageId, targetLangFolder, requestMemoryDrop: false);
                    AutoTranslatorScanner.RequestMemoryDrop();
                }
                return true;
            }
            catch (Exception ex)
            {
                if (targetMod != null) AutoTranslatorScanner.ClearOldTranslationFiles(new List<Verse.ModMetaData> { targetMod });
                string fallbackZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{packageId}_{targetLangFolder}_cloud.zip");
                if (System.IO.File.Exists(fallbackZip)) System.IO.File.Delete(fallbackZip);

                ATC_Dispatcher.RunOnMainThread(() => AutoTranslatorSettings.AddErrorLog("ATC_LogError_DownloadCorrupted".Translate(packageId, ex.Message)));
                return false;
            }
        }

    }
}
