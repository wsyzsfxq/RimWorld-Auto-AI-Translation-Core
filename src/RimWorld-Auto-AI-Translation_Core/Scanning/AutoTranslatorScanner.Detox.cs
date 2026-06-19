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
// 這個檔案負責危險標籤清理與舊檔排毒。
// EN: This file cleans dangerous XML tags and repairs legacy translation files.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {


        // 這個方法負責套用 EmergencyHotfix 設定。
        // EN: This method applies emergency hotfix.
        public static void ApplyEmergencyHotfix()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                string markerFile = Path.Combine(packPath, "V4.7_Cleaned.marker");


                if (File.Exists(markerFile)) return;

                if (Directory.Exists(langsPath))
                {
                    string[] allXmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                    if (allXmlFiles.Length > 0)
                    {

                        Directory.Delete(langsPath, true);


                        AutoTranslatorSettings.AddErrorLog("🚨 " + "ATC_LogError_OldToxicFiles".Translate());
                        AutoTranslatorSettings.AddLog("🗑️ " + "ATC_Log_OldFilesDeleted".Translate());
                        AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_AutoRebuildStart".Translate());
                    }
                }


                Directory.CreateDirectory(packPath);
                File.WriteAllText(markerFile, "This marker prevents V4.7 from deleting the cleaned translation pack.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Delete failed (請手動刪除 !Translation_AI_Pack/Languages): {ex.Message}");
            }
        }

        // 這個方法負責處理 RunDetox掃描器 相關流程。
        // EN: This method handles run detox scanner.
        public static void RunDetoxScanner()
            {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;
                int removedTags = 0;


                Regex badTagRegex = new Regex(@"(?m)^\s*<([a-zA-Z0-9_\-\.]+)>[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*<\/\1>\s*$|(?m)^\s*<[a-zA-Z0-9_\-\.]+\s*\/>\s*$", RegexOptions.Multiline);

                foreach (var file in xmlFiles)
                {
                    string content = File.ReadAllText(file);
                    if (badTagRegex.IsMatch(content))
                    {

                        MatchCollection matches = badTagRegex.Matches(content);
                        foreach (Match match in matches)
                        {
                            Log.Warning($"[AutoTranslationCore] ⚠️ 抓到劇毒標籤，準備物理切除：{match.Value.Trim()} (來自檔案: {Path.GetFileName(file)})");
                        }

                        int matchCount = matches.Count;

                        string cleanContent = badTagRegex.Replace(content, "");

                        cleanContent = Regex.Replace(cleanContent, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);

                        File.WriteAllText(file, cleanContent);
                        fixedFiles++;
                        removedTags += matchCount;
                    }
                }
                if (fixedFiles > 0)
                {


                    string logMsg = "ATC_Log_DetoxSuccess".CanTranslate()
                        ? "ATC_Log_DetoxSuccess".Translate(fixedFiles, removedTags).ToString()
                        : $"[排毒系統] 修復了 {fixedFiles} 個檔案，切除 {removedTags} 個劇毒標籤！";

                    AutoTranslatorSettings.AddLog($"🛡️ {logMsg}");
                    Log.Message($"[AutoTranslationCore] Detox complete: {fixedFiles} files fixed, {removedTags} tags removed.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 排毒系統異常: {ex.Message}");
            }

        }


        // 這個方法負責處理 RunAdvancedDetox掃描器 相關流程。
        // EN: This method handles run advanced detox scanner.
        public static void RunAdvancedDetoxScanner()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;
                int removedTags = 0;

                foreach (var file in xmlFiles)
                {


                    string dirName = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(file)).Name.ToLower();
                    if (dirName.Contains("facedef") || dirName.Contains("eyedef") || dirName.Contains("browdef") ||
                        dirName.Contains("liddef") || dirName.Contains("lashdef") || dirName.Contains("mouthdef") ||
                        dirName.Contains("nosedef") || dirName.Contains("eardef") || dirName.Contains("skindef") ||
                        dirName.Contains("facialanimation"))
                    {

                        System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                        System.IO.File.Delete(file);
                        fixedFiles++;
                        continue;
                    }

                    XmlDocument doc = new XmlDocument();
                    try { doc.Load(file); } catch { continue; }

                    if (doc.DocumentElement == null || doc.DocumentElement.Name != "LanguageData") continue;

                    List<XmlNode> nodesToKill = new List<XmlNode>();


                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element) continue;

                        string tagName = node.Name;
                        bool shouldKill = false;


                        foreach (var badField in BlacklistedFields)
                        {

                            if (tagName.EndsWith("." + badField, StringComparison.OrdinalIgnoreCase) ||
                                tagName.Equals(badField, StringComparison.OrdinalIgnoreCase))
                            {
                                shouldKill = true;
                                break;
                            }
                        }

                        if (shouldKill)
                        {
                            nodesToKill.Add(node);
                        }
                    }


                    if (nodesToKill.Count > 0)
                    {
                        foreach (var node in nodesToKill)
                        {
                            doc.DocumentElement.RemoveChild(node);
                            removedTags++;
                        }
                        doc.Save(file);
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {

                    AutoTranslatorSettings.AddLog($"🩺 [ATC System] " + "ATC_Log_AdvancedDetoxSuccess".Translate(removedTags, fixedFiles));
                    Log.Message($"[AutoTranslationCore] Advanced Detox complete: Removed {removedTags} bad tags across {fixedFiles} files, saving player's API costs!");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 高級排毒系統異常: {ex.Message}");
            }
        }


        // 這個方法負責處理 RunNewlineDetox掃描器 相關流程。
        // EN: This method handles run newline detox scanner.
        public static void RunNewlineDetoxScanner()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                var xmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                int fixedFiles = 0;

                foreach (var file in xmlFiles)
                {

                    string content = File.ReadAllText(file);


                    if (content.Contains("\\n") || content.Contains("\\r") || content.Contains("/n"))
                    {

                        content = content.Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");


                        File.WriteAllText(file, content);
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {

                    AutoTranslatorSettings.AddLog($"✨ [ATC System] 歷史業障超渡成功！共修復了 {fixedFiles} 個帶有換行 Bug 的舊檔案！");
                    Log.Message($"[AutoTranslationCore] Newline Detox complete: Fixed {fixedFiles} legacy files.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 換行排毒系統異常: {ex.Message}");
            }
        }

    }
}
