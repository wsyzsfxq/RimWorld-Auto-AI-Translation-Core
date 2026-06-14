using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
        public partial class Window_UploadPreview : Window
        {

        private void ExecuteActualUpload()
        {
            if (AutoTranslatorMod.ShouldSkipCloudSharingMod(_mod, _mod != null ? _mod.PackageId : null, _modName))
            {
                Messages.Message("ATC_Msg_CloudUploadPatchModBlocked".Translate(_modName), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("ATC_Msg_UploadStart".Translate(_mod.Name), MessageTypeDefOf.NeutralEvent, false);

            string pkgId = _mod.PackageId; string mLang = _targetLangFolder; string mName = _modName;
            string token = AutoTranslatorMod.Settings.CloudAdminToken;
            string uNick = AutoTranslatorMod.Settings.CloudNickname;
            string uType = AutoTranslatorMod.NormalizeCloudUploadType(AutoTranslatorMod.Settings.CloudUploadType, !string.IsNullOrWhiteSpace(token));
            string sDir = _sourceDir; string finalLog = _updateLogText;

            System.Threading.Tasks.Task.Run(async () => {
                try
                {
                    if (!Directory.Exists(sDir)) return;
                    string stagingDir = Path.Combine(Path.GetTempPath(), "ATC_Upload_Pre_" + pkgId);
                    if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                    Directory.CreateDirectory(stagingDir);

                    int fileCount = 0;
                    string id1 = pkgId.ToLower();
                    string id2 = pkgId.Replace(".", "_").ToLower();
                    bool isWorkspace = sDir.Contains("Upload_Workspace");

                    foreach (string file in Directory.GetFiles(sDir, "*.xml", SearchOption.AllDirectories))
                    {
                        string fileName = Path.GetFileName(file).ToLower();
                        bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                        if (isValid)
                        {
                            string relPath = file.Substring(sDir.Length).TrimStart('\\', '/');
                            string destPath = Path.Combine(stagingDir, relPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            File.Copy(file, destPath, true);
                            fileCount++;
                        }
                    }

                    if (fileCount == 0)
                    {
                        ATC_Dispatcher.RunOnMainThread(() => {
                            Messages.Message("ATC_Msg_UploadFailedNoFiles".Translate(), MessageTypeDefOf.RejectInput, false);
                        });
                        return;
                    }
                    string tempZipFile = Path.Combine(Path.GetTempPath(), $"{pkgId}_{mLang}_upload.zip");
                    if (File.Exists(tempZipFile)) File.Delete(tempZipFile);

                    // 🛡️ 核心修復 1：ZIP 壓縮強制套用 UTF-8
                    System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, tempZipFile, System.IO.Compression.CompressionLevel.Optimal, false, System.Text.Encoding.UTF8);
                    Directory.Delete(stagingDir, true);

                    byte[] zipBytes = File.ReadAllBytes(tempZipFile);
                    string base64File = Convert.ToBase64String(zipBytes);
                    File.Delete(tempZipFile);

                    string targetModVersion = "Unknown"; DateTime translationDate = DateTime.UtcNow; bool isSmartMerged = false; int mergedAiCount = 0;
                    string metaPath = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", mLang, $"{id2}_ATC_Meta.json");
                    if (File.Exists(metaPath))
                    {
                        try
                        {
                            var meta = JsonConvert.DeserializeObject<LocalModMeta>(File.ReadAllText(metaPath));
                            if (meta != null) { targetModVersion = meta.TargetModVersion; translationDate = meta.TranslationDate; isSmartMerged = meta.IsSmartMerged; mergedAiCount = meta.MergedAiCount; }
                        }
                        catch { }
                    }

                    DateTime actualModUpdate = DateTime.UtcNow;
                    if (_mod.RootDir != null && Directory.Exists(_mod.RootDir.FullName)) actualModUpdate = new DirectoryInfo(_mod.RootDir.FullName).LastWriteTimeUtc;

                    var payload = new
                    {
                        PackageId = pkgId,
                        Language = mLang,
                        ModName = mName,
                        LatestVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild,
                        ModLastUpdated = actualModUpdate.ToString("O"),
                        UploaderID = UnityEngine.SystemInfo.deviceUniqueIdentifier,
                        Author = uNick,
                        TranslationType = uType,
                        FileBase64 = base64File,
                        AdminToken = token,
                        TargetModVersion = targetModVersion,
                        TranslationDate = translationDate.ToString("O"),
                        IsSmartMerged = isSmartMerged,
                        MergedAiCount = mergedAiCount,
                        UpdateLog = finalLog
                    };

                    string jsonPayload = JsonConvert.SerializeObject(payload);
                    System.Text.Encoding tolerantUtf8 = new System.Text.UTF8Encoding(false, false);
                    byte[] payloadBytes = tolerantUtf8.GetBytes(jsonPayload);

                    // 🛡️ 核心修復 2：全面換裝 UnityWebRequest 網路引擎
                    var tcs = new TaskCompletionSource<bool>();
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            var request = new UnityEngine.Networking.UnityWebRequest($"{AutoTranslatorCloudClient.CloudApiBaseUrl}/upload", "POST");
                            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(payloadBytes);
                            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
                            request.SetRequestHeader("Content-Type", "application/json");
                            request.timeout = 120;

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

                    bool uploadSuccess = false;
                    try
                    {
                        Task completedTask = await Task.WhenAny(tcs.Task, Task.Delay(130000));
                        if (completedTask == tcs.Task) uploadSuccess = await tcs.Task;
                        else Verse.Log.Error("[ATC Cloud Preview] Upload Fail: upload timed out after 130s");
                    }
                    catch (Exception ex) { Verse.Log.Error($"[ATC Cloud Preview] Upload Fail: {ex.Message}"); }

                    ATC_Dispatcher.RunOnMainThread(() => {
                        if (uploadSuccess)
                        {
                            Messages.Message("ATC_Msg_UploadSuccess".Translate(mName), MessageTypeDefOf.PositiveEvent, false);
                            AutoTranslatorSettings.HasFetchedCloudThisSession = false;
                        }
                        else
                        {
                            Messages.Message("ATC_Msg_UploadFailed".Translate(mName), MessageTypeDefOf.RejectInput, false);
                        }
                    });

                }
                catch (Exception ex)
                {
                    Verse.Log.Error($"[ATC Cloud Preview] Thread Fail: {ex.Message}");
                }
            });
        }
        }
}
