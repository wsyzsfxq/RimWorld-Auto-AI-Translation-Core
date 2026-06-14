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
        private static readonly object PackMaintenanceLock = new object();
        private static int _packMaintenanceQueued;
        private static int _packMaintenanceRunning;

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


        // 🌟 AI 架構師特製：清理過去不小心幫「漢化補丁」生成的雙胞胎殘留檔案！(V2 無敵除靈版)
        public static void CleanupPatchModTwins()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                var allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                // ✨ 咪咪的黑名單特徵碼：只要檔名含有這些，絕對是漢化包的幽靈殘留！
                string[] ghostTokens = {
                    ".zh", "_zh", "-zh", "zh-pack", ".zhtc", "_zhtc", "-zhtc", ".zhcn", "_zhcn",
                    "-zhcn", ".cn", "_cn", "-cn", ".tw", "_tw", "-tw", "l10n",
                    "漢化", "汉化", "翻譯", "翻译", "translation", "language", "l10n", "中文", 
                    "zh-tw", "zh-cn", "簡繁", "简繁", "繁簡", "繁简"
                };

                foreach (var file in allXmls)
                {
                    // 🛡️ 絕對防禦：工作區的檔案神仙來了都不准刪
                    if (file.Contains("Upload_Workspace")) continue;

                    string fileName = Path.GetFileName(file).ToLower();
                    bool isGhost = false;

                    // 🔍 暴力比對：檔名是不是中了毒瘤特徵？
                    foreach (var token in ghostTokens)
                    {
                        if (fileName.Contains(token.ToLower()))
                        {
                            isGhost = true;
                            break;
                        }
                    }

                    // 另外加一個保底防禦：如果檔名剛好是 zh_autotranslated.xml 這種極端情況
                    if (!isGhost && (fileName.StartsWith("zh_") || fileName.StartsWith("zhtc_") || fileName.StartsWith("zhcn_")))
                    {
                        isGhost = true;
                    }

                    // 🔪 只要判定是幽靈檔案，就地正法！
                    if (isGhost)
                    {
                        // 剝奪唯讀屬性並強制刪除
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        deletedFiles++;
                    }
                }

                if (deletedFiles > 0)
                {
                    // ✨ 咪咪知錯了：完美呼叫本地化接口！
                    AutoTranslatorSettings.AddLog("🧹 " + "ATC_Log_CleanedGhostTwins".Translate(deletedFiles));
                    Verse.Log.Message($"[AutoTranslationCore] Cleaned up {deletedFiles} ghost twin translation files.");
                }
            }
            catch (Exception ex)
            {
                // ✨ 開發者日誌（Player.log）保持全英文，方便未來除錯
                Verse.Log.Warning($"[AutoTranslationCore] Ghost twin cleanup error: {ex.Message}");
            }
        }
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
                    RunDetoxScanner(); // 這是原本清垃圾空白的
                    RunAdvancedDetoxScanner(); // 🌟 咪咪新增：這是我們保護玩家錢包的高級手術！
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
