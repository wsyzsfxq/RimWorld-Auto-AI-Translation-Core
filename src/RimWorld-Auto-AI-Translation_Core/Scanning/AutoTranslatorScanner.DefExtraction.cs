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

        // 🌟 咪咪升級版：過濾器本人
        // 🌟 咪咪升級版：結合標籤名稱與內容分析的終極過濾器
        private static bool IsTranslationTarget(string tagName, string value)
        {
            // 1. 基本檢查：太短、純數字、純符號的直接踢掉
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2) return false;
            if (value.All(char.IsDigit) || Regex.IsMatch(value, @"^[^\w\s]+$")) return false;

            // ==========================================
            // 🌟 咪咪補破網：專殺假冒文字的底層變數！
            // 只要結尾是這幾個字，管他叫什麼名字，一律不准翻！
            // ==========================================
            string lower = tagName.ToLower(); // 👈 這裡宣告一次就好了！
            if (lower.EndsWith("defname") || lower.EndsWith("dollname") ||
                lower.EndsWith("dollpartname") || lower.EndsWith("methodname") ||
                lower.EndsWith("class") || lower.EndsWith("worker") || lower.EndsWith("def"))
                return false;

            // 2. 標籤黑名單：如果是 texPath, soundDef 這種直接拒絕
            if (BlacklistedFields.Contains(tagName)) return false;

            // 3. 內容特徵分析：
            // 如果包含斜線但沒有空格 -> 判定為路徑
            if ((value.Contains("/") || value.Contains("\\")) && !value.Contains(" ")) return false;

            // 如果包含底線但沒有空格 -> 判定為程式碼 ID (如 Apparel_Pants_Worker)
            if (value.Contains("_") && !value.Contains(" ")) return false;

            // 檢查是否包含檔案副檔名
            if (FilePathRegex.IsMatch(value)) return false;

            // 4. 最後判定：符合常用翻譯標籤或結尾才放行
            if (ExactTextTags.Contains(tagName)) return true;

            // 👈 這裡把重複宣告的 string lower 刪掉了，直接用！
            return lower.EndsWith("label") || lower.EndsWith("description") ||
                   lower.EndsWith("string") || lower.EndsWith("text") ||
                   lower.EndsWith("message") || lower.EndsWith("name") ||
                   lower.EndsWith("desc");
        }

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

        private static bool ShouldForceTranslateListItem(XmlNode parentNode, string currentPath, string text)
        {
            if (parentNode == null) return false;
            string parentName = parentNode.Name ?? "";
            string parentLower = parentName.ToLowerInvariant();

            if (IsProtectedDefPath(currentPath) || BlacklistedFields.Contains(parentName)) return false;
            if (LooksLikeDefReferenceValue(text)) return false;

            return IsTranslationTarget(parentName, text) || parentLower.Contains("rule");
        }


        private static bool IsKnownTranslatablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            string lower = path.ToLowerInvariant();
            return lower.EndsWith(".jobstring") ||
                   lower.EndsWith(".customsummary") ||
                   lower.EndsWith(".summary") ||
                   lower.EndsWith(".filter.customsummary") ||
                   lower.Contains(".ingredients.") && lower.EndsWith(".filter.customsummary");
        }

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

                    // 🌟 咪咪特製過濾垃圾
                    bool isGarbage = text.Length < 2 || Regex.IsMatch(text, @"^[\d\s\-\+\.\%]+$");

                    if (!isGarbage && !string.IsNullOrWhiteSpace(text) && !text.Contains(".xml") && !text.StartsWith("Tex/") && !text.StartsWith("UI/"))
                    {
                        // ✅ 修復 CS7036：這裡必須傳入兩個引數
                        bool isKnownTranslatablePath = IsKnownTranslatablePath(childPath);
                        bool shouldTranslate = !IsProtectedDefPath(childPath) &&
                                               (isKnownTranslatablePath || !LooksLikeDefReferenceValue(text)) &&
                                               (isKnownTranslatablePath || IsTranslationTarget(child.Name, text));

                        // ✅ 修復 CS7036：這裡也要傳入兩個引數
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
        public static Dictionary<string, Dictionary<string, string>> ExtractEnglishFromRawDefs(string defsRoot)
        {
            var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(defsRoot)) return result;

            foreach (var file in Directory.GetFiles(defsRoot, "*.xml", SearchOption.AllDirectories))
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
