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
// 這個檔案負責從 Def 結構抽取可翻譯文字。
// EN: This file extracts translatable text from RimWorld Def structures.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {


        // 這個方法負責判斷 Is翻譯目標 條件是否成立。
        // EN: This method checks is translation target.
        private static bool IsTranslationTarget(string tagName, string value)
        {

            if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
            if (value.All(char.IsDigit) || Regex.IsMatch(value, @"^[^\w\s]+$")) return false;


            string lower = tagName.ToLower();
            if (lower.EndsWith("defname") || lower.EndsWith("dollname") ||
                lower.EndsWith("dollpartname") || lower.EndsWith("methodname") ||
                lower.EndsWith("class") || lower.EndsWith("worker") || lower.EndsWith("def"))
                return false;


            if (BlacklistedFields.Contains(tagName)) return false;


            if ((value.Contains("/") || value.Contains("\\")) && !value.Contains(" ")) return false;


            if (value.Contains("_") && !value.Contains(" ")) return false;


            if (FilePathRegex.IsMatch(value)) return false;


            if (ExactTextTags.Contains(tagName)) return true;


            return lower.EndsWith("label") || lower.EndsWith("description") ||
                   lower.EndsWith("string") || lower.EndsWith("text") ||
                   lower.EndsWith("message") || lower.Contains("message") ||
                   lower.EndsWith("name") || lower.EndsWith("desc") ||
                   lower.EndsWith("title") || lower.EndsWith("titleshort") ||
                   lower.EndsWith("theme") || lower.EndsWith("member");
        }

        // 這個方法負責判斷 IsProtectedDef路徑 條件是否成立。
        // EN: This method checks is protected Def path.
        private static bool IsProtectedDefPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string lower = path.ToLowerInvariant();
            return lower.Contains(".targetjobs") ||
                   lower.Contains(".animationframes") ||
                   lower.Contains(".bodyaddons") ||
                   lower.Contains(".headaddons") ||
                   lower.Contains(".bodygraphicdata") ||
                   lower.Contains(".headgraphicdata") ||
                   lower.Contains(".graphicdata") ||
                   lower.Contains(".offsets") ||
                   lower.Contains(".texpath") ||
                   lower.Contains(".graphicpath") ||
                   lower.Contains(".facial") ||
                   lower.Contains(".expression") ||
                   lower.Contains(".animation");
        }

        // 這個方法負責處理 LooksLikeDefReferenceValue 相關流程。
        // EN: This method handles looks like Def reference value.
        private static bool LooksLikeDefReferenceValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string trimmed = value.Trim();
            if (trimmed.Contains("/") || trimmed.Contains("\\")) return true;
            if (FilePathRegex.IsMatch(trimmed)) return true;
            if (Regex.IsMatch(trimmed, @"^[+-]?\d+(?:\.\d+)?(?:\s*,\s*[+-]?\d+(?:\.\d+)?){1,3}$")) return true;
            if (Regex.IsMatch(trimmed, @"^\(?\s*[+-]?\d+(?:\.\d+)?(?:\s*,\s*[+-]?\d+(?:\.\d+)?){1,3}\s*\)?$")) return true;
            if (Regex.IsMatch(trimmed, @"^[A-Za-z0-9_\.\-:]+$") && !trimmed.Contains(" ")) return true;
            return false;
        }

        // 這個方法負責判斷 ShouldForce翻譯ListItem 條件是否成立。
        // EN: This method checks should force translate list item.
        private static bool ShouldForceTranslateListItem(XmlNode parentNode, string currentPath, string text)
        {
            if (parentNode == null) return false;
            string parentName = parentNode.Name ?? "";
            string parentLower = parentName.ToLowerInvariant();
            string pathLower = (currentPath ?? "").ToLowerInvariant();

            if (IsProtectedDefPath(currentPath) || BlacklistedFields.Contains(parentName)) return false;

            bool isKnownTextList =
                parentLower == "rulesstrings" ||
                parentLower == "thoughtstagedescriptions" ||
                parentLower.EndsWith("stagedescriptions") ||
                pathLower.EndsWith(".thoughtstagedescriptions");

            if (!isKnownTextList && LooksLikeDefReferenceValue(text)) return false;
            if (parentLower == "rulesstrings" && IsUntranslatableGrammarRule(text)) return false;

            return isKnownTextList || IsTranslationTarget(parentName, text) || parentLower.Contains("rule");
        }


        // 這個方法負責判斷 IsKnownTranslatable路徑 條件是否成立。
        // EN: This method checks is known translatable path.
        private static bool IsKnownTranslatablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string lower = path.ToLowerInvariant();
            return lower.EndsWith(".jobstring") ||
                   lower.EndsWith(".customsummary") ||
                   lower.EndsWith(".summary") ||
                   lower.EndsWith(".filter.customsummary") ||
                   lower.Contains(".thoughtstagedescriptions.") ||
                   lower.Contains(".rulesstrings.") ||
                   lower.EndsWith(".resource.name") ||
                   lower.Contains(".ingredients.") && lower.EndsWith(".filter.customsummary");
        }

        // 這個方法負責處理 TraverseDefNode 相關流程。
        // EN: This method handles traverse Def node.
        private static void TraverseDefNode(XmlNode node, string currentPath, string defType, Dictionary<string, Dictionary<string, string>> result)
        {
            int liIndex = 0;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.Name == "defName") continue;

                string childPath = currentPath;
                bool isListItem = child.Name == "li";

                if (isListItem)
                {
                    childPath = $"{currentPath}.{liIndex}";
                    liIndex++;
                }
                else
                {
                    childPath = $"{currentPath}.{child.Name}";
                }

                bool isPureText = false;
                if (child.ChildNodes.Count == 1)
                {
                    var cType = child.ChildNodes[0].NodeType;
                    if (cType == XmlNodeType.Text || cType == XmlNodeType.CDATA)
                    {
                        isPureText = true;
                    }
                }

                if (isPureText)
                {
                    string text = child.InnerText.Trim();


                    bool isGarbage = text.Length < 2 || Regex.IsMatch(text, @"^[\d\s\-\+\.\%]+$");

                    if (!isGarbage && !string.IsNullOrWhiteSpace(text) && !text.Contains(".xml") && !text.StartsWith("Tex/") && !text.StartsWith("UI/"))
                    {
                        if (IsUntranslatableGrammarRule(text)) continue;

                        bool isKnownTranslatablePath = IsKnownTranslatablePath(childPath);
                        bool isExactTextTag = ExactTextTags.Contains(child.Name);
                        bool shouldTranslate = !IsProtectedDefPath(childPath) &&
                                               (isKnownTranslatablePath || isExactTextTag || !LooksLikeDefReferenceValue(text)) &&
                                               (isKnownTranslatablePath || IsTranslationTarget(child.Name, text));


                        if (isListItem && ShouldForceTranslateListItem(node, currentPath, text))
                        {
                            shouldTranslate = true;
                        }

                        if (shouldTranslate)
                        {
                            if (!result.ContainsKey(defType)) result[defType] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            result[defType][childPath] = text;
                        }
                    }
                }
                else if (child.HasChildNodes)
                {
                    TraverseDefNode(child, childPath, defType, result);
                }
            }
        }
        // 這個方法負責處理 ExtractEnglishFromRawDefs 相關流程。
        // EN: This method handles extract english from raw defs.
        public static Dictionary<string, Dictionary<string, string>> ExtractEnglishFromRawDefs(string defsRoot)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(defsRoot)) return result;

            foreach (var file in GetXmlFilesCached(defsRoot, SearchOption.AllDirectories))
            {
                if (AutoTranslatorSettings.IsCancellationRequested) return result;

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(file);
                    if (doc.DocumentElement == null || doc.DocumentElement.Name.ToLower() != "defs") continue;

                    foreach (XmlNode defNode in doc.DocumentElement.ChildNodes)
                    {
                        if (defNode.NodeType != XmlNodeType.Element) continue;
                        string defType = defNode.Name;
                        string defName = "";

                        foreach (XmlNode child in defNode.ChildNodes)
                        {
                            if (child.NodeType == XmlNodeType.Element && child.Name == "defName")
                            {
                                defName = child.InnerText;
                                break;
                            }
                        }

                        if (string.IsNullOrEmpty(defName)) continue;

                        TraverseDefNode(defNode, defName, defType, result);
                    }
                }
                catch { }
            }
            return result;
        }
    }
}
