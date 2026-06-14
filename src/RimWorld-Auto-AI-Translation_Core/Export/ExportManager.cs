using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace AutoTranslator_Core
{
    public static class ExportManager
    {
        public static void ExecuteExport(List<ExportableModInfo> mods)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string exportRoot = Path.Combine(desktopPath, $"RimWorld_Translations_{timestamp}");

            AutoTranslatorSettings.AddLog("ATC_Log_ExportStart".Translate(mods.Count));

            try
            {
                Directory.CreateDirectory(exportRoot);
                WriteReadme(exportRoot, mods);
                WriteConsentRecord(exportRoot);

                int totalFiles = 0;
                foreach (var mod in mods)
                {
                    totalFiles += ExportSingleMod(mod, exportRoot);
                }

                // 開啟資料夾
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exportRoot,
                        UseShellExecute = true
                    });
                }
                catch (Exception openEx)
                {
                    Log.Warning($"[AutoTranslationCore] Cannot open folder: {openEx.Message}");
                }

                AutoTranslatorSettings.AddLog("ATC_Log_ExportComplete".Translate(mods.Count, totalFiles));

                // P3 第二階段：記錄冷卻
                ExportCooldownManager.RecordExport();

                // P3 第二階段：彈出帶「聯絡作者」按鈕的成功訊息
                ShowSuccessDialogWithContactOption(mods, totalFiles, exportRoot);
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("ATC_Log_ExportFailed".Translate(ex.Message));
                Messages.Message("ATC_Export_Failed_Message".Translate(ex.Message),
                    MessageTypeDefOf.RejectInput, false);
                Log.Error($"[AutoTranslationCore] Export failed: {ex}");
            }
        }

        /// <summary>
        /// 顯示完成訊息，並提供「聯絡原作者」入口
        /// </summary>
        private static void ShowSuccessDialogWithContactOption(
            List<ExportableModInfo> mods, int totalFiles, string exportPath)
        {
            string message = "ATC_Export_Success_Message".Translate(mods.Count, totalFiles, exportPath);
            string title = "ATC_Export_Success_Title".Translate();

            Find.WindowStack.Add(new Dialog_MessageBox(
                text: message,
                buttonAText: "ATC_Export_Success_ContactAuthorBtn".Translate(),
                buttonAAction: () =>
                {
                    Find.WindowStack.Add(new Dialog_ContactAuthor(mods));
                },
                buttonBText: "ATC_Export_Success_CloseBtn".Translate(),
                buttonBAction: null,
                title: title
            ));
        }
        /// <summary>
        /// 導出單一模組，回傳檔案數
        /// </summary>
        private static int ExportSingleMod(ExportableModInfo mod, string exportRoot)
        {
            int fileCount = 0;
            string targetFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
            string sourcePackPath = AutoTranslatorScanner.GetLocalPackPath();
            string sourceLangsRoot = Path.Combine(sourcePackPath, "Languages");

            // 建立目標資料夾結構
            // {exportRoot}/{cleanModName}/1.6/Languages/{TargetLang}/...
            string safeModName = MakeSafeFolderName(mod.ModName);
            string modExportRoot = Path.Combine(exportRoot, $"AutoTrans_{safeModName}");
            string targetLangPath = Path.Combine(modExportRoot, "1.6", "Languages", targetFolder);

            Directory.CreateDirectory(targetLangPath);

            // 複製 + 加水印 DefInjected
            foreach (var langDir in Directory.GetDirectories(sourceLangsRoot))
            {
                string defInjectedDir = Path.Combine(langDir, "DefInjected");
                if (!Directory.Exists(defInjectedDir)) continue;

                foreach (var typeDir in Directory.GetDirectories(defInjectedDir))
                {
                    string defType = Path.GetFileName(typeDir);
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml"))
                    {
                        if (!IsFileForThisMod(file, mod)) continue;

                        string targetTypeDir = Path.Combine(targetLangPath, "DefInjected", defType);
                        Directory.CreateDirectory(targetTypeDir);
                        string targetFile = Path.Combine(targetTypeDir, Path.GetFileName(file));
                        WriteFileWithWatermark(file, targetFile, mod);
                        fileCount++;
                    }
                }

                // Keyed
                string keyedDir = Path.Combine(langDir, "Keyed");
                if (Directory.Exists(keyedDir))
                {
                    foreach (var file in Directory.GetFiles(keyedDir, "*.xml"))
                    {
                        if (!IsFileForThisMod(file, mod)) continue;

                        string targetKeyedDir = Path.Combine(targetLangPath, "Keyed");
                        Directory.CreateDirectory(targetKeyedDir);
                        string targetFile = Path.Combine(targetKeyedDir, Path.GetFileName(file));
                        WriteFileWithWatermark(file, targetFile, mod);
                        fileCount++;
                    }
                }
            }

            // 寫入 About.xml
            WriteAboutXml(modExportRoot, mod);
            fileCount++;

            // 寫入 LoadFolders.xml
            WriteLoadFoldersXml(modExportRoot);
            fileCount++;

            return fileCount;
        }

        /// <summary>
        /// 判斷一個翻譯檔是否屬於指定模組
        /// </summary>
        private static bool IsFileForThisMod(string filePath, ExportableModInfo mod)
        {
            string fileName = Path.GetFileName(filePath).ToLower();
            string id1 = mod.PackageId.ToLower();
            string id2 = mod.PackageIdWithUnderscore.ToLower();
            return fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                   fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");
        }

        /// <summary>
        /// 將原檔案複製到目標位置，並在開頭插入水印註解
        /// </summary>
        private static void WriteFileWithWatermark(string sourceFile, string targetFile, ExportableModInfo mod)
        {
            string content = File.ReadAllText(sourceFile, Encoding.UTF8);
            string watermark = ExportTemplates.GetXmlWatermark(mod);

            // 在 XML 宣告之後插入水印註解
            // 找到 ?> 後插入
            int declarationEnd = content.IndexOf("?>");
            string result;
            if (declarationEnd > 0)
            {
                int insertPos = declarationEnd + 2;
                result = content.Substring(0, insertPos) + "\n" + watermark + content.Substring(insertPos);
            }
            else
            {
                // 沒有 XML 宣告，直接前置
                result = watermark + content;
            }

            File.WriteAllText(targetFile, result, Encoding.UTF8);
        }

        private static void WriteAboutXml(string modExportRoot, ExportableModInfo mod)
        {
            string aboutDir = Path.Combine(modExportRoot, "About");
            Directory.CreateDirectory(aboutDir);
            string aboutPath = Path.Combine(aboutDir, "About.xml");

            string content = ExportTemplates.GetAboutXml(mod);
            File.WriteAllText(aboutPath, content, Encoding.UTF8);
        }

        private static void WriteLoadFoldersXml(string modExportRoot)
        {
            string path = Path.Combine(modExportRoot, "LoadFolders.xml");
            string content = @"<?xml version=""1.0"" encoding=""utf-8""?>
<loadFolders>
  <v1.6>
    <li>1.6</li>
  </v1.6>
</loadFolders>";
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        private static void WriteReadme(string exportRoot, List<ExportableModInfo> mods)
        {
            string path = Path.Combine(exportRoot, "README_IMPORTANT.txt");
            string content = ExportTemplates.GetReadme(mods);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        private static void WriteConsentRecord(string exportRoot)
        {
            string path = Path.Combine(exportRoot, "EULA_Consent_Record.txt");
            var settings = AutoTranslatorMod.Settings;
            string content = ExportTemplates.GetConsentRecord(
                settings.EulaAcceptedTimestamp,
                settings.EulaAcceptedVersion,
                settings.EulaAcceptCount
            );
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        /// <summary>
        /// 將模組名稱轉為安全的資料夾名稱（去除非法字元）
        /// </summary>
        private static string MakeSafeFolderName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "UnnamedMod" : result;
        }
    }
}
