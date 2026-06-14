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
        // 🌟 咪咪特製：V4.7 終極物理超渡程式 V3.0！(全本地化版)
        // 🌟 咪咪特製：V4.7 終極物理超渡程式 V3.1！(帶免疫標記，絕對不再自爆！)
        public static void ApplyEmergencyHotfix()
        {
            try
            {
                string packPath = GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                string markerFile = Path.Combine(packPath, "V4.7_Cleaned.marker"); // 🌟 免疫標記檔！

                // 🌟 第一道防線：如果已經有免疫標記，代表是乾淨的新版本，直接下莊，絕對不刪！
                if (File.Exists(markerFile)) return;

                if (Directory.Exists(langsPath))
                {
                    string[] allXmlFiles = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);
                    if (allXmlFiles.Length > 0)
                    {
                        // 🌟 發現舊版，投下核彈！
                        Directory.Delete(langsPath, true);

                        // 🌟 本地化日誌輸出
                        AutoTranslatorSettings.AddErrorLog("🚨 " + "ATC_LogError_OldToxicFiles".Translate());
                        AutoTranslatorSettings.AddLog("🗑️ " + "ATC_Log_OldFilesDeleted".Translate());
                        AutoTranslatorSettings.AddLog("🚀 " + "ATC_Log_AutoRebuildStart".Translate());
                    }
                }

                // 🌟 建立免疫標記！跟系統說「我已經洗乾淨了，不要再殺我了！」
                Directory.CreateDirectory(packPath);
                File.WriteAllText(markerFile, "This marker prevents V4.7 from deleting the cleaned translation pack.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Delete failed (請手動刪除 !Translation_AI_Pack/Languages): {ex.Message}");
            }
        }
        // 🌟 咪咪特製：V4.8 終極排毒機制 (微創手術，不刪玩家檔案！)
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

                // 🌟 神奇放大鏡：精準抓出 <tag></tag> (含空白與零寬字元) 或 <tag/>
                // 🌟 神奇放大鏡 (終極加強版)：加入 \u00A0 (不換行空白) 與 \x00-\x1F (控制字元)
                // 只要標籤裡面只有這些垃圾，一律判定為劇毒！
                Regex badTagRegex = new Regex(@"(?m)^\s*<([a-zA-Z0-9_\-\.]+)>[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*<\/\1>\s*$|(?m)^\s*<[a-zA-Z0-9_\-\.]+\s*\/>\s*$", RegexOptions.Multiline);

                foreach (var file in xmlFiles)
                {
                    string content = File.ReadAllText(file);
                    if (badTagRegex.IsMatch(content))
                    {
                        // 🌟 顯影劑：把抓到的毒瘤印在後台，大哥你明天睡醒看 Log 就知道是誰在搞事了！
                        MatchCollection matches = badTagRegex.Matches(content);
                        foreach (Match match in matches)
                        {
                            Log.Warning($"[AutoTranslationCore] ⚠️ 抓到劇毒標籤，準備物理切除：{match.Value.Trim()} (來自檔案: {Path.GetFileName(file)})");
                        }

                        int matchCount = matches.Count;
                        // 🌟 物理切除：把有毒的那整行替換成空字串
                        string cleanContent = badTagRegex.Replace(content, "");
                        // 🌟 順手把多餘的空行修掉，保持版面整潔
                        cleanContent = Regex.Replace(cleanContent, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);

                        File.WriteAllText(file, cleanContent);
                        fixedFiles++;
                        removedTags += matchCount;
                    }
                }
                if (fixedFiles > 0)
                {
                    // ✨ 修復 CS0019：RimWorld 的 Translate() 回傳的是 TaggedString，不能直接用 ??
                    // 改用 CanTranslate() 來做標準判定，並補上 .ToString() 轉回普通字串！
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


        // 🌟 咪咪特製：V4.9 玩家資產保衛戰！微創排毒手術 (精準刪除違規標籤，保留正常翻譯)
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
                    // 🛡️ 第一道防線：直接把高危險的臉部與貼圖翻譯資料夾物理切除！
                    // 只要資料夾名字跟臉部定義有關，不廢話，直接刪除，讓遊戲讀取原本正常的英文/圖形。
                    string dirName = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(file)).Name.ToLower();
                    if (dirName.Contains("facedef") || dirName.Contains("eyedef") || dirName.Contains("browdef") ||
                        dirName.Contains("liddef") || dirName.Contains("lashdef") || dirName.Contains("mouthdef") ||
                        dirName.Contains("nosedef") || dirName.Contains("eardef") || dirName.Contains("skindef") ||
                        dirName.Contains("facialanimation"))
                    {
                        // 剝奪唯讀屬性並強制刪除
                        System.IO.File.SetAttributes(file, System.IO.FileAttributes.Normal);
                        System.IO.File.Delete(file);
                        fixedFiles++;
                        continue;
                    }

                    XmlDocument doc = new XmlDocument();
                    try { doc.Load(file); } catch { continue; } // 如果檔案壞了就跳過，不引發報錯

                    if (doc.DocumentElement == null || doc.DocumentElement.Name != "LanguageData") continue;

                    List<XmlNode> nodesToKill = new List<XmlNode>();

                    // 遍歷檔案裡所有的翻譯標籤
                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                    {
                        if (node.NodeType != XmlNodeType.Element) continue;

                        string tagName = node.Name;
                        bool shouldKill = false;

                        // 🔍 檢查這個標籤有沒有中我們的黑名單 (例如 Weapon_Rifle.soundInteract)
                        foreach (var badField in BlacklistedFields)
                        {
                            // 只要標籤名稱「等於」或是「結尾是 .黑名單」，就判定為惡性腫瘤！
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

                    // 🔪 執行物理切除！
                    if (nodesToKill.Count > 0)
                    {
                        foreach (var node in nodesToKill)
                        {
                            doc.DocumentElement.RemoveChild(node);
                            removedTags++;
                        }
                        doc.Save(file); // 存檔覆蓋
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {
                    // 🌟 本地化日誌：報告手術成果！
                    AutoTranslatorSettings.AddLog($"🩺 [ATC System] " + "ATC_Log_AdvancedDetoxSuccess".Translate(removedTags, fixedFiles));
                    Log.Message($"[AutoTranslationCore] Advanced Detox complete: Removed {removedTags} bad tags across {fixedFiles} files, saving player's API costs!");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] 高級排毒系統異常: {ex.Message}");
            }
        }
        // ==========================================
        // 🌟 咪咪特製：背景無感自動清理舊翻譯引擎
        // ==========================================
        // ==========================================
        // 🌟 咪咪特製：V2.1 歷史業障超渡器 (全自動換行修復)
        // ==========================================
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
                    // 把檔案當作純文字直接讀出來 (超高速)
                    string content = File.ReadAllText(file);

                    // 偵測是否感染了 \n 病毒
                    if (content.Contains("\\n") || content.Contains("\\r") || content.Contains("/n"))
                    {
                        // 物理替換！將字面上的 \n 轉為真實換行
                        content = content.Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");

                        // 存回硬碟覆蓋舊檔案
                        File.WriteAllText(file, content);
                        fixedFiles++;
                    }
                }

                if (fixedFiles > 0)
                {
                    // 本地化日誌：報告超渡成果
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
