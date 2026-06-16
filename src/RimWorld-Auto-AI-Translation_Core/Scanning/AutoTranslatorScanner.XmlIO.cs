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
// 這個檔案負責翻譯 XML 的讀寫與輸出驗證。
// EN: This file reads, writes, and validates translation XML.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {


        // 這個方法負責讀取 XmlFilesToDict 資料。
        // EN: This method loads XML files to dict.
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

        // 這個方法負責讀取 XmlFileToDict 資料。
        // EN: This method loads XML file to dict.
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


                if (expectedLang.HasValue && LanguageDetector.IsFakeLanguage(dict, expectedLang.Value))
                {
                    AutoTranslatorSettings.AddLog($"🕵️ [System] " + "ATC_Log_FakeLanguageDetected".Translate(Path.GetFileName(filePath)).ToString());
                    return new Dictionary<string, string>();
                }
            }
            catch { }
            return dict;
        }

        // 這個方法負責保存 Xml 資料。
        // EN: This method saves XML.
        public static void SaveXml(string path, Dictionary<string, string> data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            XmlDocument d = new XmlDocument();
            XmlDeclaration dec = d.CreateXmlDeclaration("1.0", "utf-8", null);
            d.AppendChild(dec);
            XmlElement r = d.CreateElement("LanguageData");

            foreach (var p in data)
            {

                if (Regex.IsMatch(p.Value ?? "", @"^[\s\u200B\u200C\u200D\uFEFF\u00A0\x00-\x1F]*$"))
                    continue;


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

                    AddValidationStat(s => s.XmlKeySkipped++);
                    AutoTranslatorSettings.AddErrorLog("⚠️ " + "ATC_LogError_InvalidXmlKey".Translate($"{p.Key} ({ex.Message})"));
                    continue;
                }
            }

            d.AppendChild(r);
            d.Save(path);
        }
        // 這個方法負責取得 Short路徑 資料。
        // EN: This method gets short path.
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
