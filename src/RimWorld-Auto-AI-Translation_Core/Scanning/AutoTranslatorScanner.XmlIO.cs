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

        // ✨ 架構師改造：向下傳遞 expectedLang
        public static Dictionary<string, string> LoadXmlFilesToDict(string path, TargetLanguage? expectedLang = null)
        {
            var dict = new Dictionary<string, string>();
            if (!Directory.Exists(path)) return dict;
            foreach (var f in Directory.GetFiles(path, "*.xml", SearchOption.AllDirectories))
            {
                var d = LoadXmlFileToDict(f, expectedLang);
                foreach (var p in d) dict[p.Key] = p.Value;
            }
            return dict;
        }
        // ✨ 架構師改造：加入 expectedLang 參數
        public static Dictionary<string, string> LoadXmlFileToDict(string filePath, TargetLanguage? expectedLang = null)
        {
            var dict = new Dictionary<string, string>();
            if (!File.Exists(filePath)) return dict;
            try
            {
                XmlDocument d = new XmlDocument(); d.Load(filePath);
                if (d.DocumentElement == null) return dict;
                foreach (XmlNode n in d.DocumentElement.ChildNodes)
                {
                    if (n.NodeType == XmlNodeType.Element)
                    {
                        string val = n.InnerText;
                        if (!string.IsNullOrEmpty(val))
                        {
                            val = val.Replace("\\n", "\n").Replace("\\r", "\r").Replace("/n", "\n");
                        }
                        dict[n.Name] = val;
                    }
                }

                // ✨ 海關檢查：如果是假語言檔，直接沒收整份檔案！
                if (expectedLang.HasValue && LanguageDetector.IsFakeLanguage(dict, expectedLang.Value))
                {
                    AutoTranslatorSettings.AddLog($"🕵️ [System] " + "ATC_Log_FakeLanguageDetected".Translate(Path.GetFileName(filePath)).ToString());
                    return new Dictionary<string, string>(); // 回傳空字典，假裝這個檔案不存在
                }
            }
            catch { }
            return dict;
        }

        public static void SaveXml(string path, Dictionary<string, string> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XmlDocument d = new XmlDocument();
            XmlDeclaration dec = d.CreateXmlDeclaration("1.0", "utf-8", null);
            d.AppendChild(dec);
            XmlElement r = d.CreateElement("LanguageData");

            foreach (var p in data)
            {
                // 防禦 1：value 是空白/控制字元，跳過
                if (Regex.IsMatch(p.Value ?? "", @"^[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*$"))
                    continue;

                // 防禦 2：key 為空或不符合 XML 命名規則，跳過並記錄
                // 解決 Bug C：AI 回傳的 key 若包含 '{' (0x7B) 或其他非法字元，CreateElement 會炸
                if (string.IsNullOrWhiteSpace(p.Key) || !ValidXmlNameRegex.IsMatch(p.Key))
                {
                    AddValidationStat(s => s.XmlKeySkipped++);
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_InvalidXmlKey".Translate(p.Key ?? "<null>"));
                    continue;
                }

                try
                {
                    XmlElement n = d.CreateElement(p.Key);
                    n.InnerText = p.Value;
                    r.AppendChild(n);
                }
                catch (XmlException ex)
                {
                    // 防禦 3：最後一道防線，CreateElement 仍可能因特殊 Unicode 拋例外
                    AddValidationStat(s => s.XmlKeySkipped++);
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_InvalidXmlKey".Translate($"{p.Key} ({ex.Message})"));
                    continue;
                }
            }

            d.AppendChild(r);
            d.Save(path);
        }
        private static string GetShortPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return "";
            string normalized = fullPath.Replace('\\', '/');
            int idx = normalized.IndexOf("294100/");
            if (idx == -1) idx = normalized.IndexOf("Mods/");
            return idx != -1 ? normalized.Substring(idx) : Path.GetFileName(fullPath);
        }
    }
}
