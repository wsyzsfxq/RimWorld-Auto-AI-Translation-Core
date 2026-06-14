using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {

        // ==========================================
        // 🌟 咪咪特製：V5.0 智能縫合身分證更新器！(精準判定版)
        // ==========================================
        public static void UpdateLocalModMeta(string packageId, string targetLangFolder, int newAiCount)
        {
            try
            {
                string packPath = GetLocalPackPath();
                string extractRoot = Path.Combine(packPath, "Languages", targetLangFolder);
                Directory.CreateDirectory(extractRoot);

                string cleanPackageId = packageId.Replace(".", "_").ToLower();
                string metaPath = Path.Combine(extractRoot, $"{cleanPackageId}_ATC_Meta.json");

                LocalModMeta meta = new LocalModMeta
                {
                    OriginalRecordId = "",
                    TargetModVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild,
                    TranslationDate = DateTime.UtcNow,
                    IsSmartMerged = false,
                    MergedAiCount = 0
                };

                bool hasCloudIdentity = false;

                if (File.Exists(metaPath))
                {
                    try
                    {
                        var existing = JsonConvert.DeserializeObject<LocalModMeta>(File.ReadAllText(metaPath));
                        if (existing != null)
                        {
                            meta = existing;
                            if (!string.IsNullOrEmpty(meta.OriginalRecordId)) hasCloudIdentity = true;
                        }
                    }
                    catch { }
                }

                // ✨ 咪咪精準邏輯：只有繼承雲端大老的檔案，才算「智能縫合」並計數！
                if (hasCloudIdentity)
                {
                    meta.IsSmartMerged = true;
                    meta.MergedAiCount += newAiCount;
                }
                else
                {
                    // 純 AI 機翻，不掛縫合標籤，縫合次數保持 0
                    meta.IsSmartMerged = false;
                    meta.MergedAiCount = 0;
                }

                meta.TargetModVersion = RimWorld.VersionControl.CurrentVersionStringWithoutBuild;

                File.WriteAllText(metaPath, JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented));
                AutoTranslatorSettings.AddLog("📝 " + "ATC_Log_MetaUpdated".Translate(newAiCount));

                // ✨ 呼叫剛才寫好的廣播接口，通知全域編輯器「名單更新啦！」
                TranslationWorkbenchTab.RequestRefresh();
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] UpdateLocalModMeta error: {ex.Message}");
            }
        }       
        // ==========================================
        // 🌟 咪咪特製：V5.0 本機 LRU 備份引擎
        // ==========================================
        private static void CreateBackupBeforeClear(ModMetaData mod, List<string> filesToBackup)
        {
            if (filesToBackup == null || filesToBackup.Count == 0) return;
            try
            {
                string packPath = GetLocalPackPath();
                string backupRoot = Path.Combine(packPath, "Backups", mod.PackageId);
                Directory.CreateDirectory(backupRoot);

                // 建立一個暫存資料夾來放準備打包的檔案
                string stagingDir = Path.Combine(Path.GetTempPath(), "ATC_Backup_" + mod.PackageId);
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                Directory.CreateDirectory(stagingDir);

                string langsPath = Path.Combine(packPath, "Languages");

                // 把要刪除的檔案先複製一份到暫存區 (保留原本的資料夾結構)
                foreach (string file in filesToBackup)
                {
                    string relPath = file.Substring(langsPath.Length).TrimStart('\\', '/');
                    string destPath = Path.Combine(stagingDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                    File.Copy(file, destPath, true);
                }

                // 打包成 ZIP
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string zipPath = Path.Combine(backupRoot, $"{mod.PackageId}_{timestamp}.zip");

                if (File.Exists(zipPath)) File.Delete(zipPath);
                System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, zipPath);
                Directory.Delete(stagingDir, true);

                // ✨ LRU 最少使用淘汰演算法：永遠只保留最新的 3 個備份檔！
                var zipFiles = new DirectoryInfo(backupRoot).GetFiles("*.zip")
                                .OrderByDescending(f => f.CreationTimeUtc).ToList();

                for (int i = 3; i < zipFiles.Count; i++)
                {
                    zipFiles[i].Delete();
                }

                AutoTranslatorSettings.AddLog("📦 " + "ATC_Log_BackupSuccess".Translate(mod.Name));
            }
            catch (Exception ex)
            {
                Log.Warning($"[ATC Backup] Failed to backup {mod.PackageId}: {ex.Message}");
            }
        }


        // ==========================================
        // 🌟 咪咪特製：背景無感自動清理舊翻譯引擎 (帶備份版)
        // ==========================================
        public static void ClearOldTranslationFiles(List<ModMetaData> modsToClear)
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                foreach (var mod in modsToClear)
                {
                    string id1 = mod.PackageId.ToLower();
                    string id2 = mod.PackageId.Replace(".", "_").ToLower();

                    // ✨ 先準備一個清單，收集要刪除的檔案
                    List<string> filesToDelete = new List<string>();

                    foreach (var file in allXmls)
                    {
                        // 🛡️ 絕對防禦：如果檔案路徑包含 Upload_Workspace，直接跳過，神仙來了都不准刪！
                        if (file.Contains("Upload_Workspace")) continue;

                        string fileName = Path.GetFileName(file).ToLower();
                        if (fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + "."))
                        {
                            filesToDelete.Add(file);
                        }
                    }

                    // ✨ 如果有找到舊檔案，先備份，再執行物理刪除！
                    if (filesToDelete.Count > 0)
                    {
                        CreateBackupBeforeClear(mod, filesToDelete); // 呼叫 LRU 備份引擎

                        foreach (var file in filesToDelete)
                        {
                            // 🛡️ 強制爆破：剝奪唯讀權限後再刪除
                            System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                            File.Delete(file);
                            deletedFiles++;
                        }
                    }
                }

                if (deletedFiles > 0)
                {
                    AutoTranslatorSettings.AddLog("ATC_ClearCacheSuccess".Translate(deletedFiles));
                    Log.Message($"[AutoTranslationCore] Auto-cleared {deletedFiles} old files for updated mods (Backup created).");
                }
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog($"Auto Clear Error: {ex.Message}");
            }
        }


        public static int RestoreLatestBackups(List<ModMetaData> modsToRestore)
        {
            if (modsToRestore == null || modsToRestore.Count == 0) return 0;

            string packPath = GetLocalPackPath();
            string backupRoot = Path.Combine(packPath, "Backups");
            string langsPath = Path.Combine(packPath, "Languages");
            if (!Directory.Exists(backupRoot)) return 0;

            int restored = 0;
            foreach (var mod in modsToRestore)
            {
                if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;

                try
                {
                    string modBackupDir = Path.Combine(backupRoot, mod.PackageId);
                    if (!Directory.Exists(modBackupDir)) continue;

                    FileInfo latest = new DirectoryInfo(modBackupDir)
                        .GetFiles("*.zip")
                        .OrderByDescending(f => f.CreationTimeUtc)
                        .FirstOrDefault();

                    if (latest == null) continue;

                    Directory.CreateDirectory(langsPath);
                    using (var archive = System.IO.Compression.ZipFile.OpenRead(latest.FullName))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string destPath = Path.GetFullPath(Path.Combine(langsPath, entry.FullName));
                            string safeRoot = Path.GetFullPath(langsPath);
                            if (!destPath.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, true);
                        }
                    }

                    restored++;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[ATC Backup] Failed to restore {mod.PackageId}: {ex.Message}");
                }
            }

            if (restored > 0)
            {
                RequestMemoryDrop();
            }

            return restored;
        }
    }
}
