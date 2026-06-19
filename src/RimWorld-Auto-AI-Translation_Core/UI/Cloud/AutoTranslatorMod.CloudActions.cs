using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責雲端翻譯服務的 自動翻譯器模組雲端Actions，處理 registry、上傳、下載或刪除流程。
// EN: This file contains auto translator mod cloud actions support code.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器模組 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorMod.
    public partial class AutoTranslatorMod : Mod
    {
        private enum CloudBatchUploadSource
        {
            Workspace,
            LocalPack
        }

        // 這個方法負責判斷 ShouldSkip雲端Sharing模組 條件是否成立。
        // EN: This method checks should skip cloud sharing mod.
        internal static bool ShouldSkipCloudSharingMod(ModMetaData mod, string packageId = null, string displayName = null)
        {
            if (mod != null && AutoTranslatorScanner.IsTranslationPatchMod(mod)) return true;

            string pid = (packageId ?? (mod != null ? mod.PackageId : "") ?? "").Trim().ToLowerInvariant();
            string name = (displayName ?? (mod != null ? mod.Name : "") ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(pid) && string.IsNullOrEmpty(name)) return false;

            string[] nameKeywords =
            {
                "translation", "translations", "localization", "localisation", "language pack", "l10n",
                "chinese translation", "traditional chinese", "simplified chinese",
                "\u6f22\u5316", "\u6c49\u5316", "\u4e2d\u6587", "\u7e41\u9ad4", "\u7e41\u4f53", "\u7c21\u9ad4", "\u7b80\u4f53", "\u7ffb\u8b6f", "\u7ffb\u8bd1",
                "\u65e5\u672c\u8a9e\u5316", "\u65e5\u672c\u8a9e", "\u7ffb\u8a33",
                "\ud55c\uad6d\uc5b4", "\ubc88\uc5ed",
                "\u043f\u0435\u0440\u0435\u0432\u043e\u0434", "\u0440\u0443\u0441\u0438\u0444\u0438\u043a\u0430\u0442\u043e\u0440",
                "\u0443\u043a\u0440\u0430\u0457\u043d\u0441\u044c\u043a\u0430", "traduction", "traduccion", "traducci\u00f3n",
                "traduzione", "traducao", "tradu\u00e7\u00e3o", "ubersetzung", "\u00fcbersetzung", "tlumaczenie", "t\u0142umaczenie"
            };
            foreach (string keyword in nameKeywords)
            {
                if (!string.IsNullOrEmpty(keyword) && name.Contains(keyword)) return true;
            }

            string[] pidMarkers =
            {
                ".zh", "_zh", "-zh", "zh-pack",
                ".zhtc", "_zhtc", "-zhtc", ".zhtw", "_zhtw", "-zhtw",
                ".zhcn", "_zhcn", "-zhcn", ".zh-cn", "_zh-cn", "-zh-cn", ".zh-tw", "_zh-tw", "-zh-tw",
                ".cn", "_cn", "-cn", ".tw", "_tw", "-tw",
                ".translation", "_translation", "-translation", ".translations", "_translations", "-translations",
                ".localization", "_localization", "-localization", ".localisation", "_localisation", "-localisation",
                ".language", "_language", "-language", ".l10n", "_l10n", "-l10n"
            };
            foreach (string marker in pidMarkers)
            {
                if (pid.EndsWith(marker) || pid.Contains(marker + ".") || pid.Contains(marker + "_") || pid.Contains(marker + "-")) return true;
            }

            return pid.EndsWith("zh") || pid.EndsWith("zhtw") || pid.EndsWith("zhcn");
        }


        // 這個方法負責執行 Batch下載 動作。
        // EN: This method executes batch download.
        private void ExecuteBatchDownload(string targetType)
        {
            ATC_Dispatcher.EnsureAlive();
            var localMods = Verse.ModLister.AllInstalledMods.Where(m => m.Active && !ShouldSkipCloudSharingMod(m)).ToList();


            string targetLangStr = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.CloudTargetLang);

            var recordsByPackage = AutoTranslatorSettings.CloudRegistry
                .Where(c => c.Language == targetLangStr && (c.TranslationType == targetType || (targetType == "Official_Group" && c.IsVerified)))
                .GroupBy(c => c.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => {
                        if (AutoTranslatorSettings.SelectedCloudVersion.TryGetValue(g.Key, out CloudModRecord selected)
                            && selected != null
                            && string.Equals(selected.Language, targetLangStr, StringComparison.OrdinalIgnoreCase))
                        {
                            return selected;
                        }
                        return g.OrderByDescending(c => c.LastUpdated).First();
                    },
                    StringComparer.OrdinalIgnoreCase);


            var modsToDownload = new List<Verse.ModMetaData>();
            foreach (var record in recordsByPackage.Values)
            {
                var mod = localMods.FirstOrDefault(m => string.Equals(m.PackageId, record.PackageId, StringComparison.OrdinalIgnoreCase));
                if (mod != null) modsToDownload.Add(mod);
            }

            if (modsToDownload.Count == 0)
            {
                Verse.Messages.Message("ATC_Msg_BatchNoMods".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                return;
            }

            Verse.Messages.Message("ATC_Msg_BatchStart".Translate(modsToDownload.Count), RimWorld.MessageTypeDefOf.NeutralEvent, false);

            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                int failCount = 0;
                List<string> failedMods = new List<string>();
                List<string> repairedPackages = new List<string>();

                AutoTranslatorSettings.IsRunning = true;

                for (int i = 0; i < modsToDownload.Count; i++)
                {
                    var mod = modsToDownload[i];


                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Downloading".Translate(mod.Name);
                    AutoTranslatorMod.Settings.CurrentProgress = (float)i / modsToDownload.Count;


                    recordsByPackage.TryGetValue(mod.PackageId, out CloudModRecord record);

                    bool success = await AutoTranslatorCloudClient.DownloadAndInjectAsync(mod.PackageId, targetLangStr, record, requestMemoryDrop: false);
                    if (success)
                    {
                        successCount++;
                        repairedPackages.Add(mod.PackageId);
                    }
                    else
                    {
                        failCount++;
                        failedMods.Add(mod.Name);
                    }
                }

                if (successCount > 0)
                {
                    AutoTranslatorLegacyRepairer.RepairPackages(repairedPackages, targetLangStr, requestMemoryDrop: false);
                    AutoTranslatorScanner.RequestMemoryDrop();
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {

                    AutoTranslatorSettings.IsRunning = false;
                    AutoTranslatorMod.Settings.CurrentTaskName = "";
                    AutoTranslatorMod.Settings.CurrentProgress = 0f;
                    AutoTranslatorSettings.AddLog("☁️ " + "ATC_Log_BatchDownloadSummary".Translate(successCount, failCount, modsToDownload.Count));
                    if (failedMods.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_BatchDownloadFailedList".Translate(string.Join(", ", failedMods.Take(5).ToArray())));
                    }
                    Verse.Messages.Message("ATC_Msg_BatchSuccess".Translate(successCount, modsToDownload.Count), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                });
            });
        }


        // 這個方法負責執行 Batch上傳 動作。
        // EN: This method executes batch upload.
        private void ExecuteBatchUpload(CloudBatchUploadSource source)
        {
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string workspaceRoot = System.IO.Path.Combine(packPath, "Upload_Workspace");
            string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(Settings.CloudTargetLang);
            string liveLangDir = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
            string uNickname = Settings.CloudNickname;
            string uToken = Settings.CloudAdminToken;
            string uploadType = NormalizeCloudUploadType(Settings.CloudUploadType, !string.IsNullOrWhiteSpace(uToken));
            string uploadTypeLabel = GetCloudUploadTypeLabel(uploadType);
            string updateLog = (Settings.CloudBatchUploadLog ?? "").Trim();

            Settings.CloudUploadType = uploadType;
            WriteSettings();


            int skippedPatchModCount = 0;
            var uploadSources = new List<string>();

            if (source == CloudBatchUploadSource.Workspace)
            {
                var modDirs = System.IO.Directory.Exists(workspaceRoot)
                    ? System.IO.Directory.GetDirectories(workspaceRoot)
                    .Where(modDir =>
                    {
                        string packageId = System.IO.Path.GetFileName(modDir);
                        var tempMeta = Verse.ModLister.AllInstalledMods.FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                        string displayName = tempMeta != null ? tempMeta.Name : packageId;
                        if (ShouldSkipCloudSharingMod(tempMeta, packageId, displayName))
                        {
                            skippedPatchModCount++;
                            return false;
                        }
                        return true;
                    })
                    .ToArray()
                    : new string[0];

                foreach (string modDir in modDirs)
                {
                    string packageId = System.IO.Path.GetFileName(modDir);
                    string langDir = System.IO.Path.Combine(modDir, targetLangFolder);
                    if (!HasUploadableTranslationFiles(langDir, packageId, true)) continue;
                    uploadSources.Add(modDir);
                }
            }
            else
            {
                foreach (var mod in Verse.ModLister.AllInstalledMods)
                {
                    if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;
                    if (ShouldSkipCloudSharingMod(mod))
                    {
                        skippedPatchModCount++;
                        continue;
                    }
                    if (!HasUploadableTranslationFiles(liveLangDir, mod.PackageId, false)) continue;

                    uploadSources.Add(mod.PackageId + "|" + liveLangDir);
                }
            }

            if (skippedPatchModCount > 0)
            {
                Messages.Message("ATC_Msg_CloudBatchPatchModsSkipped".Translate(skippedPatchModCount), MessageTypeDefOf.NeutralEvent, false);
            }
            if (uploadSources.Count == 0)
            {
                string emptyKey = source == CloudBatchUploadSource.Workspace
                    ? "ATC_Msg_WorkspaceEmpty"
                    : "ATC_Msg_BatchUploadPackEmpty";
                Messages.Message(emptyKey.Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("ATC_Msg_BatchUploadStart".Translate(uploadSources.Count), MessageTypeDefOf.NeutralEvent, false);

            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                int failCount = 0;
                List<string> failedMods = new List<string>();

                AutoTranslatorSettings.IsRunning = true;

                try
                {
                    for (int i = 0; i < uploadSources.Count; i++)
                    {
                        var modDir = uploadSources[i];

                        string packageId = System.IO.Path.GetFileName(modDir);
                        string sourceOverride = null;
                        int sourceSplit = modDir.IndexOf('|');
                        if (sourceSplit >= 0)
                        {
                            packageId = modDir.Substring(0, sourceSplit);
                            sourceOverride = modDir.Substring(sourceSplit + 1);
                        }

                        var tempMeta = Verse.ModLister.AllInstalledMods.FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                        string displayName = tempMeta != null ? tempMeta.Name : packageId;

                        try
                        {
                            AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Uploading".Translate(displayName);
                            AutoTranslatorMod.Settings.CurrentProgress = (float)i / uploadSources.Count;

                            string langDir = sourceOverride ?? System.IO.Path.Combine(modDir, targetLangFolder);

                            if (!System.IO.Directory.Exists(langDir))
                            {
                                failCount++;
                                failedMods.Add(displayName);
                                AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_BatchUploadMissingFolder".Translate(displayName));
                                continue;
                            }

                            if (AutoTranslatorScanner.GetXmlFilesForTranslationCache(langDir, System.IO.SearchOption.AllDirectories).Count == 0)
                            {
                                failCount++;
                                failedMods.Add(displayName);
                                AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_BatchUploadNoXml".Translate(displayName));
                                continue;
                            }

                            string modName = tempMeta != null ? tempMeta.Name : packageId;

                            bool success = await AutoTranslatorCloudClient.UploadTranslationAsync(packageId, targetLangFolder, modName, uNickname, uploadType, langDir, uToken, updateLog);

                            if (success)
                            {
                                successCount++;
                                if (sourceOverride != null) continue;

                                foreach (string file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(langDir, System.IO.SearchOption.AllDirectories))
                                {
                                    string relPath = file.Substring(langDir.Length).TrimStart('\\', '/');

                                    string justFileName = System.IO.Path.GetFileName(file);
                                    string justFileNameLower = justFileName.ToLower();
                                    string id1 = packageId.ToLower();
                                    string id2 = packageId.Replace(".", "_").ToLower();

                                    if (!justFileNameLower.StartsWith(id1 + "_") && !justFileNameLower.StartsWith(id1 + ".") &&
                                        !justFileNameLower.StartsWith(id2 + "_") && !justFileNameLower.StartsWith(id2 + "."))
                                    {
                                        string dirName = System.IO.Path.GetDirectoryName(relPath);
                                        string newFileName = $"{id2}_{justFileName}";
                                        relPath = string.IsNullOrEmpty(dirName) ? newFileName : System.IO.Path.Combine(dirName, newFileName);
                                    }

                                    string destPath = System.IO.Path.Combine(liveLangDir, relPath);
                                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath));
                                    System.IO.File.Copy(file, destPath, true);
                                }
                            }
                            else
                            {
                                failCount++;
                                failedMods.Add(displayName);
                            }
                        }
                        catch (Exception itemEx)
                        {
                            failCount++;
                            failedMods.Add(displayName);
                            AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_Log_BatchUploadItemFailed".Translate(displayName, itemEx.Message));
                            Log.Warning($"[AutoTranslationCore] Batch upload skipped {displayName} ({packageId}): {itemEx}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddErrorLog("❌ " + "ATC_Log_BatchUploadWorkerFailed".Translate(ex.Message));
                    Log.Warning("[AutoTranslationCore] Batch upload worker failed: " + ex);
                }
                finally
                {
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {

                        AutoTranslatorSettings.IsRunning = false;
                        AutoTranslatorMod.Settings.CurrentTaskName = "";
                        AutoTranslatorMod.Settings.CurrentProgress = 0f;

                        Verse.Messages.Message("ATC_Msg_BatchUploadSuccess".Translate(successCount, uploadTypeLabel), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                        if (failCount > 0)
                        {
                            AutoTranslatorSettings.AddLog("⚠️ " + "ATC_Log_BatchUploadFailedList".Translate(failCount, string.Join(", ", failedMods.Take(8).ToArray())));
                        }

                        AutoTranslatorSettings.HasFetchedCloudThisSession = false;
                        ModUpdateDetector.ClearStatusCache();
                        TranslationWorkbenchTab.RequestRefresh();
                    });
                }
            });
        }

        // 這個方法負責判斷 HasUploadable翻譯Files 條件是否成立。
        // EN: This method checks has uploadable translation files.
        private static bool HasUploadableTranslationFiles(string sourceDir, string packageId, bool isWorkspace)
        {
            if (string.IsNullOrEmpty(sourceDir) || string.IsNullOrEmpty(packageId) || !System.IO.Directory.Exists(sourceDir)) return false;
            string id1 = packageId.ToLower();
            string id2 = packageId.Replace(".", "_").ToLower();

            foreach (string file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(sourceDir, System.IO.SearchOption.AllDirectories))
            {
                if (isWorkspace) return true;

                string fileName = System.IO.Path.GetFileName(file).ToLower();
                if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                    fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                {
                    return true;
                }
            }

            return false;
        }
        // 這個方法負責清理並標準化 雲端上傳Type 內容。
        // EN: This method cleans and normalizes cloud upload type.
        internal static string NormalizeCloudUploadType(string uploadType, bool allowOfficial = true)
        {
            if (uploadType == "Manual") return uploadType;
            if (uploadType == "Official_Group" && allowOfficial) return uploadType;
            return "AI_Auto";
        }

        // 這個方法負責取得 雲端上傳TypeLabel 資料。
        // EN: This method gets cloud upload type label.
        private static string GetCloudUploadTypeLabel(string uploadType)
        {
            switch (NormalizeCloudUploadType(uploadType))
            {
                case "Official_Group": return "ATC_Type_Official".Translate().ToString();
                case "Manual": return "ATC_Type_Manual".Translate().ToString();
                default: return "ATC_Type_AI".Translate().ToString();
            }
        }

    }
}
