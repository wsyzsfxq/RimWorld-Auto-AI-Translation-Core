using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public partial class AutoTranslatorMod : Mod
    {
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

        // ==========================================
        // 🚀 批量空投引擎 (背景執行，不卡畫面) 語言隔離 + 本地化版
        // ==========================================
        private void ExecuteBatchDownload(string targetType)
        {
            ATC_Dispatcher.EnsureAlive();
            var localMods = Verse.ModLister.AllInstalledMods.Where(m => m.Active && !ShouldSkipCloudSharingMod(m)).ToList();

            // ✨ 取得當前設定的語言，確保只下載符合玩家語系的翻譯！
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

            // 找出本地有安裝，且雲端符合目標類型的模組
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
                // ✨ 架構師優化：鎖定系統狀態，讓主畫面的進度條開始運作
                AutoTranslatorSettings.IsRunning = true;

                for (int i = 0; i < modsToDownload.Count; i++)
                {
                    var mod = modsToDownload[i];

                    // ✨ 實時更新進度條與任務名稱
                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Downloading".Translate(mod.Name);
                    AutoTranslatorMod.Settings.CurrentProgress = (float)i / modsToDownload.Count;

                    // 找出這個 mod 對應的雲端紀錄
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
                    // ✨ 任務結束，解除鎖定並歸零進度條
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
        // ==========================================
        // 🚀 批量上傳引擎 (專為漢化組打造，全自動掃描工作區)
        // ==========================================
        private void ExecuteBatchUpload()
        {
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string workspaceRoot = System.IO.Path.Combine(packPath, "Upload_Workspace");
            string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(Settings.CloudTargetLang);
            string uNickname = Settings.CloudNickname;
            string uToken = Settings.CloudAdminToken;
            string uploadType = NormalizeCloudUploadType(Settings.CloudUploadType, !string.IsNullOrWhiteSpace(uToken));
            string uploadTypeLabel = GetCloudUploadTypeLabel(uploadType);
            string updateLog = (Settings.CloudBatchUploadLog ?? "").Trim();

            Settings.CloudUploadType = uploadType;
            WriteSettings();

            // 1. 檢查總工作區存不存在
            if (!System.IO.Directory.Exists(workspaceRoot))
            {
                Messages.Message("ATC_Msg_WorkspaceEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            // 2. 獲取裡面所有的資料夾 (每一個資料夾名稱就是一個 PackageId)
            int skippedPatchModCount = 0;
            var modDirs = System.IO.Directory.GetDirectories(workspaceRoot)
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
                .ToArray();
            if (skippedPatchModCount > 0)
            {
                Messages.Message("ATC_Msg_CloudBatchPatchModsSkipped".Translate(skippedPatchModCount), MessageTypeDefOf.NeutralEvent, false);
            }
            if (modDirs.Length == 0)
            {
                Messages.Message("ATC_Msg_WorkspaceEmpty".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Messages.Message("ATC_Msg_BatchUploadStart".Translate(modDirs.Length), MessageTypeDefOf.NeutralEvent, false);

            System.Threading.Tasks.Task.Run(async () =>
            {
                int successCount = 0;
                // ✨ 架構師優化：鎖定系統狀態
                AutoTranslatorSettings.IsRunning = true;

                for (int i = 0; i < modDirs.Length; i++)
                {
                    var modDir = modDirs[i];
                    // 資料夾名稱即為 PackageId
                    string packageId = System.IO.Path.GetFileName(modDir);

                    // 嘗試從本地模組清單抓取模組名稱
                    var tempMeta = Verse.ModLister.AllInstalledMods.FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                    string displayName = tempMeta != null ? tempMeta.Name : packageId;

                    // ✨ 實時更新進度條與任務名稱
                    AutoTranslatorMod.Settings.CurrentTaskName = "ATC_Cloud_Uploading".Translate(displayName);
                    AutoTranslatorMod.Settings.CurrentProgress = (float)i / modDirs.Length;

                    // 組裝語言資料夾路徑: Upload_Workspace / packageId / ChineseTraditional
                    string langDir = System.IO.Path.Combine(modDir, targetLangFolder);

                    // 如果這個語言資料夾不存在，或是裡面沒有 xml 檔，就跳過
                    if (!System.IO.Directory.Exists(langDir)) continue;
                    if (System.IO.Directory.GetFiles(langDir, "*.xml", System.IO.SearchOption.AllDirectories).Length == 0) continue;

                    string modName = tempMeta != null ? tempMeta.Name : packageId;

                    // 🚀 發射到雲端！
                    bool success = await AutoTranslatorCloudClient.UploadTranslationAsync(packageId, targetLangFolder, modName, uNickname, uploadType, langDir, uToken, updateLog);

                    if (success)
                    {
                        successCount++;
                        // ✨ 神級細節：上傳成功後，順便把檔案複製到遊戲真正的 Languages 目錄
                        string liveLangDir = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
                        foreach (string file in System.IO.Directory.GetFiles(langDir, "*.xml", System.IO.SearchOption.AllDirectories))
                        {
                            string relPath = file.Substring(langDir.Length).TrimStart('\\', '/');

                            // ✨ 架構師修復：批量上傳的本地複製，一樣強制冠上前綴！
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
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    // ✨ 任務結束，解除鎖定並歸零進度條
                    AutoTranslatorSettings.IsRunning = false;
                    AutoTranslatorMod.Settings.CurrentTaskName = "";
                    AutoTranslatorMod.Settings.CurrentProgress = 0f;

                    Verse.Messages.Message("ATC_Msg_BatchUploadSuccess".Translate(successCount, uploadTypeLabel), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                    AutoTranslatorSettings.HasFetchedCloudThisSession = false; // 強制下次重整清單，讓 UI 變金色！
                });
            });
        }

        internal static string NormalizeCloudUploadType(string uploadType, bool allowOfficial = true)
        {
            if (uploadType == "Manual") return uploadType;
            if (uploadType == "Official_Group" && allowOfficial) return uploadType;
            return "AI_Auto";
        }

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
