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
    public class CloudModRecord
    {
        public string RecordId { get; set; }
        public string PackageId { get; set; }
        public string Language { get; set; }
        public string ModName { get; set; }
        public string LatestVersion { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime ModLastUpdated { get; set; }
        public string UploaderID { get; set; }
        public string Author { get; set; }
        public string TranslationType { get; set; }
        public bool IsVerified { get; set; }
        public string FileUrl { get; set; }
        public string TargetModVersion { get; set; }
        public DateTime TranslationDate { get; set; }
        public bool IsSmartMerged { get; set; }
        public int MergedAiCount { get; set; }
        public string UpdateLog { get; set; }
    }

    public class LocalModMeta
    {
        public string OriginalRecordId { get; set; }
        public string TargetModVersion { get; set; }
        public DateTime TranslationDate { get; set; }
        public bool IsSmartMerged { get; set; }
        public int MergedAiCount { get; set; }
    }

    public static class AutoTranslatorCloudClient
    {
        public const string PrimaryApiBaseUrl = "https://api.anln666-nas.xyz/api/v1";
        public const string BackupApiBaseUrl = "https://cn-api.anln666-nas.xyz/api/v1";
        public static string CloudApiBaseUrl = PrimaryApiBaseUrl;

        public static readonly HttpClient cloudClient = new HttpClient()
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        static AutoTranslatorCloudClient()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            cloudClient.DefaultRequestHeaders.Add("User-Agent", "RimWorld-ATC-CloudClient/5.0");
            cloudClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            cloudClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            cloudClient.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
        }

        public static async Task<List<CloudModRecord>> FetchRegistryAsync()
        {
            int maxRetries = 4;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                {
                    CloudApiBaseUrl = BackupApiBaseUrl;
                    Verse.Log.Warning("[ATC Cloud] " + "ATC_Cloud_MainRouteCrashed".Translate());
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
                                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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
                    string jsonResponse = await tcs.Task;

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
                        Verse.Log.Warning($"[ATC Cloud] " + "ATC_Cloud_ConnectionFailed".Translate(ex.Message));
                        return null;
                    }
                }

                // 觸發退避重試機制
                int delayMs = (int)Math.Pow(2, attempt + 1) * 1000 + new System.Random().Next(100, 500);
                Verse.Log.Message("[ATC Cloud] " + "ATC_Cloud_RetryAttemptLog".Translate(attempt + 1));
                await Task.Delay(delayMs);
            }
            return null;
        }
        public static async Task<bool> DownloadAndInjectAsync(string packageId, string targetLangFolder, CloudModRecord targetRecord = null)
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
                                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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

                    zipBytes = await tcs.Task;
                    if (zipBytes != null && zipBytes.Length > 0) break;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        Verse.Log.Error("[ATC Cloud] " + "ATC_Cloud_DownloadRetryFailed".Translate(ex.Message));
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

                AutoTranslatorScanner.RequestMemoryDrop();
                return true;
            }
            catch (Exception ex)
            {
                if (targetMod != null) AutoTranslatorScanner.ClearOldTranslationFiles(new List<Verse.ModMetaData> { targetMod });
                string fallbackZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{packageId}_{targetLangFolder}_cloud.zip");
                if (System.IO.File.Exists(fallbackZip)) System.IO.File.Delete(fallbackZip);

                AutoTranslatorSettings.AddErrorLog("ATC_LogError_DownloadCorrupted".Translate(packageId, ex.Message));
                return false;
            }
        }

        public static async Task<bool> UploadTranslationAsync(string packageId, string language, string modName, string author, string translationType, string sourceFolder, string adminToken)
        {
            try
            {
                if (!System.IO.Directory.Exists(sourceFolder)) return false;

                string stagingDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ATC_Upload_" + packageId);
                if (System.IO.Directory.Exists(stagingDir)) System.IO.Directory.Delete(stagingDir, true);
                System.IO.Directory.CreateDirectory(stagingDir);

                string id1 = packageId.ToLower();
                string id2 = packageId.Replace(".", "_").ToLower();
                int fileCount = 0;
                bool isWorkspace = sourceFolder.Contains("Upload_Workspace");

                foreach (string file in System.IO.Directory.GetFiles(sourceFolder, "*.xml", System.IO.SearchOption.AllDirectories))
                {
                    string fileName = System.IO.Path.GetFileName(file).ToLower();
                    bool shouldPack = isWorkspace;

                    if (!shouldPack)
                    {
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            shouldPack = true;
                        }
                    }

                    if (shouldPack)
                    {
                        string relPath = file.Substring(sourceFolder.Length).TrimStart('\\', '/');
                        string justFileName = System.IO.Path.GetFileName(file).ToLower();
                        if (!justFileName.StartsWith(id1 + "_") && !justFileName.StartsWith(id1 + ".") &&
                            !justFileName.StartsWith(id2 + "_") && !justFileName.StartsWith(id2 + "."))
                        {
                            string dirName = System.IO.Path.GetDirectoryName(relPath);
                            string newFileName = $"{id2}_{System.IO.Path.GetFileName(file)}";
                            relPath = string.IsNullOrEmpty(dirName) ? newFileName : System.IO.Path.Combine(dirName, newFileName);
                        }

                        string destPath = System.IO.Path.Combine(stagingDir, relPath);
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath));
                        System.IO.File.Copy(file, destPath);
                        fileCount++;
                    }
                }

                if (fileCount == 0)
                {
                    System.IO.Directory.Delete(stagingDir, true);
                    return false;
                }

                string tempZipFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{packageId}_{language}_upload.zip");
                if (System.IO.File.Exists(tempZipFile)) System.IO.File.Delete(tempZipFile);
                System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, tempZipFile, System.IO.Compression.CompressionLevel.Optimal, false, System.Text.Encoding.UTF8); System.IO.Directory.Delete(stagingDir, true);

                byte[] zipBytes = System.IO.File.ReadAllBytes(tempZipFile);
                string base64File = Convert.ToBase64String(zipBytes);
                System.IO.File.Delete(tempZipFile);

                string metaPath = System.IO.Path.Combine(stagingDir, $"{id2}_ATC_Meta.json");
                string targetModVersion = "Unknown";
                DateTime translationDate = DateTime.UtcNow;
                bool isSmartMerged = false;
                int mergedAiCount = 0;

                if (System.IO.File.Exists(metaPath))
                {
                    try
                    {
                        var meta = JsonConvert.DeserializeObject<LocalModMeta>(System.IO.File.ReadAllText(metaPath));
                        if (meta != null)
                        {
                            targetModVersion = meta.TargetModVersion;
                            translationDate = meta.TranslationDate;
                            isSmartMerged = meta.IsSmartMerged;
                            mergedAiCount = meta.MergedAiCount;
                        }
                    }
                    catch { }
                }

                Verse.ModMetaData tempMeta = null;
                foreach (var m in Verse.ModLister.AllInstalledMods)
                {
                    if (m.PackageId.ToLower() == packageId.ToLower())
                    {
                        tempMeta = m;
                        break;
                    }
                }

                DateTime actualModUpdate = DateTime.UtcNow;
                if (tempMeta != null && tempMeta.RootDir != null && System.IO.Directory.Exists(tempMeta.RootDir.FullName))
                {
                    actualModUpdate = new System.IO.DirectoryInfo(tempMeta.RootDir.FullName).LastWriteTimeUtc;
                }

                var payload = new
                {
                    PackageId = packageId,
                    Language = language,
                    ModName = modName,
                    LatestVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild,
                    ModLastUpdated = actualModUpdate.ToString("O"),
                    UploaderID = UnityEngine.SystemInfo.deviceUniqueIdentifier,
                    Author = author,
                    TranslationType = translationType,
                    FileBase64 = base64File,
                    AdminToken = adminToken,
                    TargetModVersion = targetModVersion,
                    TranslationDate = translationDate.ToString("O"),
                    IsSmartMerged = isSmartMerged,
                    MergedAiCount = mergedAiCount
                };

                string jsonPayload = JsonConvert.SerializeObject(payload);
                System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                byte[] payloadBytes = tolerantUtf8.GetBytes(jsonPayload);

                int maxRetries = 4;

                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                    {
                        CloudApiBaseUrl = BackupApiBaseUrl;
                        Verse.Log.Warning("[ATC Cloud] " + "ATC_Cloud_UploadFallback".Translate());
                    }

                    try
                    {
                        var tcs = new TaskCompletionSource<bool>();
                        ATC_Dispatcher.RunOnMainThread(() =>
                        {
                            try
                            {
                                var request = new UnityEngine.Networking.UnityWebRequest($"{CloudApiBaseUrl}/upload", "POST");
                                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(payloadBytes);
                                request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                                request.SetRequestHeader("Content-Type", "application/json");
                                request.timeout = 120;

                                var operation = request.SendWebRequest();
                                operation.completed += (op) =>
                                {
                                    try
                                    {
                                        if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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

                        bool success = await tcs.Task;
                        if (success) return true;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == maxRetries)
                        {
                            Verse.Log.Error($"[ATC Cloud] " + "ATC_Cloud_UploadFailed".Translate(ex.Message)); return false;
                        }
                    }
                    int delay = (int)Math.Pow(2, attempt + 1) * 1000 + new System.Random().Next(100, 500);
                    await Task.Delay(delay);
                }

                return false;
            }
            catch (Exception ex)
            {
                Verse.Log.Error($"[ATC Cloud] " + "ATC_Cloud_UploadFailed".Translate(ex.Message)); return false;
            }
        }

        public static async Task<bool> DeleteCloudRecordAsync(string packageId, string language, string recordId, string adminToken)
        {
            int maxRetries = 4;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt >= 3 && CloudApiBaseUrl == PrimaryApiBaseUrl)
                {
                    CloudApiBaseUrl = BackupApiBaseUrl;
                    Verse.Log.Warning("[ATC Cloud] " + "ATC_Cloud_DeleteFallback".Translate());
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
                                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
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

                    bool success = await tcs.Task;
                    if (success) return true;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        Verse.Log.Error($"[ATC Cloud] " + "ATC_Cloud_DeleteFailed".Translate(ex.Message));
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