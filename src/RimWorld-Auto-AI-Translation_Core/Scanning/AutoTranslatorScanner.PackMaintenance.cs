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
// 這個檔案負責翻譯包維護與舊資料整理。
// EN: This file maintains the local translation pack and legacy data layout.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {
        private static readonly object PackMaintenanceLock = new object();
        // 這個欄位保存 packMaintenanceQueued 的執行狀態或快取資料。
        // EN: This field stores pack maintenance queued runtime state or cached data.
        private static int _packMaintenanceQueued;
        // 這個欄位保存 packMaintenanceRunning 的執行狀態或快取資料。
        // EN: This field stores pack maintenance running runtime state or cached data.
        private static int _packMaintenanceRunning;

        // 這個方法負責處理 MigrateOldTranslations 相關流程。
        // EN: This method handles migrate old translations.
        public static void MigrateOldTranslations()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                foreach (var langDir in Directory.GetDirectories(langsPath))
                {
                    string defInjectedDir = Path.Combine(langDir, "DefInjected");
                    if (!Directory.Exists(defInjectedDir)) continue;

                    foreach (var maybePackageDir in Directory.GetDirectories(defInjectedDir))
                    {
                        string packageName = Path.GetFileName(maybePackageDir);
                        bool isOldPackageStructure = false;

                        foreach (var defTypeDir in Directory.GetDirectories(maybePackageDir))
                        {
                            string defType = Path.GetFileName(defTypeDir);
                            string oldFile = Path.Combine(defTypeDir, "AutoTranslated_Defs.xml");

                            if (File.Exists(oldFile))
                            {
                                isOldPackageStructure = true;
                                string newTargetDir = Path.Combine(defInjectedDir, defType);
                                Directory.CreateDirectory(newTargetDir);

                                string cleanPackageName = packageName.Replace(".", "_");
                                string newFile = Path.Combine(newTargetDir, $"{cleanPackageName}_AutoTranslated.xml");

                                if (File.Exists(newFile))
                                {
                                    var oldDict = LoadXmlFileToDict(oldFile);
                                    var newDict = LoadXmlFileToDict(newFile);
                                    foreach (var kv in oldDict) newDict[kv.Key] = kv.Value;
                                    SaveXml(newFile, newDict);
                                    File.Delete(oldFile);
                                }
                                else
                                {
                                    File.Move(oldFile, newFile);
                                }
                                AutoTranslatorSettings.AddLog("ATC_Log_MigrateSuccess".Translate(packageName, defType));
                            }
                        }

                        if (isOldPackageStructure)
                        {
                            try
                            {
                                if (Directory.GetFiles(maybePackageDir, "*", SearchOption.AllDirectories).Length == 0)
                                {
                                    Directory.Delete(maybePackageDir, true);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Migration system error: {ex.Message}");
            }
        }


        // 這個方法負責清理並標準化 upSelfTranslations 內容。
        // EN: This method cleans and normalizes up self translations.
        private static void CleanupSelfTranslations()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                string[] forbiddenPrefixes = { "auto_aitranslation_core", "aitranslation_pack" };

                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                foreach (var file in allXmls)
                {
                    string fileName = Path.GetFileName(file).ToLower();
                    foreach (var prefix in forbiddenPrefixes)
                    {
                        if (fileName.StartsWith(prefix))
                        {
                            File.Delete(file);
                            AutoTranslatorSettings.AddLog($"🗑️ [System] 發現並刪除誤翻譯檔案 / Deleted rogue file: {Path.GetFileName(file)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 清理自我翻譯檔時發生錯誤 / Self-cleanup error: {ex.Message}");
            }
        }


        // 這個方法負責清理並標準化 up補丁模組Twins 內容。
        // EN: This method cleans and normalizes up patch mod twins.
        public static void CleanupPatchModTwins()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);


                string[] ghostTokens = {
                    ".zh", "_zh", "-zh", "zh-pack", ".zhtc", "_zhtc", "-zhtc", ".zhcn", "_zhcn",
                    "-zhcn", ".cn", "_cn", "-cn", ".tw", "_tw", "-tw", "l10n",
                    "漢化", "汉化", "翻譯", "翻译", "translation", "language", "l10n", "中文",
                    "zh-tw", "zh-cn", "簡繁", "简繁", "繁簡", "繁简"
                };

                foreach (var file in allXmls)
                {

                    if (file.Contains("Upload_Workspace")) continue;

                    string fileName = Path.GetFileName(file).ToLower();
                    bool isGhost = false;


                    foreach (var token in ghostTokens)
                    {
                        if (fileName.Contains(token.ToLower()))
                        {
                            isGhost = true;
                            break;
                        }
                    }


                    if (!isGhost && (fileName.StartsWith("zh_") || fileName.StartsWith("zhtc_") || fileName.StartsWith("zhcn_")))
                    {
                        isGhost = true;
                    }


                    if (isGhost)
                    {

                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        deletedFiles++;
                    }
                }

                if (deletedFiles > 0)
                {

                    AutoTranslatorSettings.AddLog("🧹 " + "ATC_Log_CleanedGhostTwins".Translate(deletedFiles));
                    Verse.Log.Message($"[AutoTranslationCore] Cleaned up {deletedFiles} ghost twin translation files.");
                }
            }
            catch (Exception ex)
            {

                Verse.Log.Warning($"[AutoTranslationCore] Ghost twin cleanup error: {ex.Message}");
            }
        }
        // 這個方法負責確保 翻譯包Initialized 已準備完成。
        // EN: This method ensures pack initialized is ready.
        public static void EnsurePackInitialized(bool runFullMaintenance = false)
        {
            EnsurePackSkeleton();

            if (runFullMaintenance)
            {
                RunPackMaintenance(waitForExisting: true);
            }
            else
            {
                QueueBackgroundPackMaintenanceOnce();
            }
        }

        // 這個方法負責確保 翻譯包Skeleton 已準備完成。
        // EN: This method ensures pack skeleton is ready.
        private static void EnsurePackSkeleton()
        {
            string packPath = GetLocalPackPath();
            string aboutPath = Path.Combine(packPath, "About/About.xml");
            if (!File.Exists(aboutPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(aboutPath));
                File.WriteAllText(aboutPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModMetaData><name>! AutoTranslation AI Pack</name><author>Auto Translator Core</author><packageId>AITranslation.Pack</packageId><supportedVersions><li>1.6</li></supportedVersions></ModMetaData>");
            }
        }

        // 這個方法負責排入 Background翻譯包MaintenanceOnce 佇列。
        // EN: This method queues background pack maintenance once.
        private static void QueueBackgroundPackMaintenanceOnce(int delayMs = 750)
        {
            if (Interlocked.Exchange(ref _packMaintenanceQueued, 1) == 1) return;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(0, delayMs));
                    RunPackMaintenance();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoTranslationCore] Background pack maintenance failed: {ex.Message}");
                }
            });
        }

        // 這個方法負責處理 Run翻譯包Maintenance 相關流程。
        // EN: This method handles run pack maintenance.
        private static void RunPackMaintenance(bool waitForExisting = false)
        {
            if (Interlocked.CompareExchange(ref _packMaintenanceRunning, 1, 0) != 0)
            {
                if (!waitForExisting) return;

                System.Threading.SpinWait.SpinUntil(
                    () => Interlocked.CompareExchange(ref _packMaintenanceRunning, 0, 0) == 0,
                    TimeSpan.FromSeconds(30));

                if (Interlocked.CompareExchange(ref _packMaintenanceRunning, 1, 0) != 0) return;
            }

            try
            {
                lock (PackMaintenanceLock)
                {
                    CleanupSelfTranslations();
                    MigrateOldTranslations();
                    ApplyEmergencyHotfix();
                    RunDetoxScanner();
                    RunAdvancedDetoxScanner();
                    AutoTranslatorLegacyRepairer.QueueBackgroundRepairOnce();
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Pack maintenance failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _packMaintenanceRunning, 0);
            }
        }
    }
}
