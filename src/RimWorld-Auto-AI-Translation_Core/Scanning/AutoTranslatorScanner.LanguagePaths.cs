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
// 這個檔案負責解析各版本 RimWorld 的語言路徑。
// EN: This file resolves language paths across supported RimWorld versions.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器掃描器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorScanner.
    public static partial class AutoTranslatorScanner
    {

        // 這個方法負責判斷 Is翻譯補丁模組 條件是否成立。
        // EN: This method checks is translation patch mod.
        public static bool IsTranslationPatchMod(ModMetaData mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.PackageId)) return false;
            string pid = mod.PackageId.ToLowerInvariant();
            string name = (mod.Name ?? "").ToLowerInvariant();

            if (pid.Contains("chinesepack") ||
                pid.Contains("chinese-pack") ||
                pid.StartsWith("rwzh.") ||
                name.Contains("zh-pack") ||
                name.Contains("chinese pack") ||
                name.Contains("chinesepack"))
            {
                return true;
            }


            string[] patchKeywords = { "漢化", "汉化", "翻譯", "翻译", "translation", "language", "l10n", "中文", "zh-tw", "zh-cn", "簡繁", "简繁", "繁簡", "繁简" };
            foreach (var kw in patchKeywords)
            {
                if (name.Contains(kw)) return true;
            }


            string[] pidSuffixes = { ".zh", "_zh", "-zh", "zh-pack", ".zhtc", "_zhtc", "-zhtc", ".zhcn", "_zhcn", "-zhcn", ".cn", "_cn", "-cn", ".tw", "_tw", "-tw", "l10n" };
            foreach (var suf in pidSuffixes)
            {

                if (pid.EndsWith(suf) || pid.Contains(suf + ".") || pid.Contains(suf + "_")) return true;
            }


            if (pid.EndsWith("zh")) return true;

            return false;
        }

        // 這個方法負責取得 Local翻譯包路徑 資料。
        // EN: This method gets local pack path.
        public static string GetLocalPackPath()
        {
            string rimWorldRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            return Path.Combine(rimWorldRoot, "Mods/!Translation_AI_Pack");
        }


        // 這個方法負責取得 Folder名稱By語言 資料。
        // EN: This method gets folder name by language.
        public static string GetFolderNameByLanguage(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ChineseTraditional";
                case TargetLanguage.Simplified: return "ChineseSimplified";
                case TargetLanguage.Japanese: return "Japanese";
                case TargetLanguage.Korean: return "Korean";
                case TargetLanguage.Russian: return "Russian";
                case TargetLanguage.Ukrainian: return "Ukrainian";
                case TargetLanguage.English: return "English";

                case TargetLanguage.French: return "French";
                case TargetLanguage.German: return "German";
                case TargetLanguage.Spanish: return "Spanish";
                case TargetLanguage.Italian: return "Italian";
                case TargetLanguage.Polish: return "Polish";
                case TargetLanguage.Portuguese: return "PortugueseBrazilian";
                case TargetLanguage.Turkish: return "Turkish";
                default: return "English";
            }
        }


        // 這個方法負責取得 SecondaryFolder名稱By語言 資料。
        // EN: This method gets secondary folder name by language.
        public static string GetSecondaryFolderNameByLanguage(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ChineseSimplified";
                case TargetLanguage.Simplified: return "ChineseTraditional";
                default: return null;
            }
        }


        // 這個方法負責判斷 Is語言FolderMatch 條件是否成立。
        // EN: This method checks is language folder match.
        private static bool IsLanguageFolderMatch(string folderName, string expectedFolder)
        {
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(expectedFolder)) return false;

            string compact = NormalizeLanguageFolderName(folderName);
            string expectedCompact = NormalizeLanguageFolderName(expectedFolder);
            if (compact.Equals(expectedCompact, StringComparison.OrdinalIgnoreCase)) return true;
            if (compact.StartsWith(expectedCompact, StringComparison.OrdinalIgnoreCase)) return true;

            if (expectedFolder.Equals("ChineseSimplified", StringComparison.OrdinalIgnoreCase))
            {
                return compact.StartsWith("SimplifiedChinese", StringComparison.OrdinalIgnoreCase)
                    || compact.IndexOf("ChineseSimplified", StringComparison.OrdinalIgnoreCase) >= 0
                    || compact.IndexOf("SimplifiedChinese", StringComparison.OrdinalIgnoreCase) >= 0
                    || compact.Contains("\u7b80\u4f53")
                    || compact.Contains("\u7c21\u9ad4")
                    || compact.Contains("\u7b80\u4f53\u4e2d\u6587")
                    || compact.Contains("\u7c21\u9ad4\u4e2d\u6587");
            }

            if (expectedFolder.Equals("ChineseTraditional", StringComparison.OrdinalIgnoreCase))
            {
                return compact.StartsWith("TraditionalChinese", StringComparison.OrdinalIgnoreCase)
                    || compact.IndexOf("ChineseTraditional", StringComparison.OrdinalIgnoreCase) >= 0
                    || compact.IndexOf("TraditionalChinese", StringComparison.OrdinalIgnoreCase) >= 0
                    || compact.Contains("\u7e41\u4f53")
                    || compact.Contains("\u7e41\u9ad4")
                    || compact.Contains("\u7e41\u4f53\u4e2d\u6587")
                    || compact.Contains("\u7e41\u9ad4\u4e2d\u6587");
            }

            return false;
        }

        // 這個方法負責清理並標準化 語言Folder名稱 內容。
        // EN: This method cleans and normalizes language folder name.
        private static string NormalizeLanguageFolderName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return "";
            return new string(folderName.Where(char.IsLetterOrDigit).ToArray());
        }


        // 這個方法負責處理 Resolve語言Folders 相關流程。
        // EN: This method handles resolve language folders.
        private static List<string> ResolveLanguageFolders(string langRoot, string folderName)
        {
            List<string> matches = new List<string>();
            if (string.IsNullOrEmpty(langRoot) || string.IsNullOrEmpty(folderName) || !Directory.Exists(langRoot)) return matches;

            string direct = Path.Combine(langRoot, folderName);
            if (Directory.Exists(direct)) matches.Add(direct);

            try
            {
                foreach (string dir in Directory.GetDirectories(langRoot))
                {
                    if (IsLanguageFolderMatch(Path.GetFileName(dir), folderName) && !matches.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    {
                        matches.Add(dir);
                    }
                }
            }
            catch { }

            return matches;
        }


        // 這個方法負責判斷 IsOld版本路徑 條件是否成立。
        // EN: This method checks is old version path.
        private static bool IsOldVersionPath(string modRoot, string fullPath)
        {
            string relative = fullPath.Substring(modRoot.Length).Replace('\\', '/');
            string[] parts = relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string version = NormalizeVersionFolder(part);
                if (!string.IsNullOrEmpty(version) &&
                    !string.Equals(version, CurrentRimWorldVersion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string CurrentRimWorldVersion
        {
            get
            {
#if RIMWORLD_1_5
                return "1.5";
#else
                return "1.6";
#endif
            }
        }

        // 這個方法負責取得 CurrentLoadFolderVersions 資料。
        // EN: This method gets current load folder versions.
        private static string[] GetCurrentLoadFolderVersions()
        {
#if RIMWORLD_1_5
            return new[] { "v1.5", "1.5" };
#else
            return new[] { "v1.6", "1.6" };
#endif
        }

        // 這個方法負責清理並標準化 版本Folder 內容。
        // EN: This method cleans and normalizes version folder.
        private static string NormalizeVersionFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return null;

            string candidate = folderName.Trim();
            if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate.Substring(1);
            }

            if (candidate.Length < 3) return null;
            if (!char.IsDigit(candidate[0]) || candidate[1] != '.' || !char.IsDigit(candidate[2])) return null;

            int end = 3;
            while (end < candidate.Length && (char.IsDigit(candidate[end]) || candidate[end] == '.'))
            {
                end++;
            }

            string version = candidate.Substring(0, end);
            return version == "1.0" ||
                   version == "1.1" ||
                   version == "1.2" ||
                   version == "1.3" ||
                   version == "1.4" ||
                   version == "1.5" ||
                   version == "1.6"
                ? version
                : null;
        }


        // 這個方法負責解析 LoadFolders 內容。
        // EN: This method parses load folders.
        private static List<string> ParseLoadFolders(ModMetaData mod)
        {
            List<string> activeFolders = new List<string>();
            string loadFolderXml = Path.Combine(mod.RootDir.FullName, "LoadFolders.xml");

            if (File.Exists(loadFolderXml))
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(loadFolderXml);

                    string[] versionsToCheck = GetCurrentLoadFolderVersions();
                    foreach (string ver in versionsToCheck)
                    {
                        XmlNode verNode = doc.SelectSingleNode($"//loadFolders/{ver}");
                        if (verNode != null)
                        {
                            foreach (XmlNode li in verNode.ChildNodes)
                            {
                                if (li.Name == "li" && !string.IsNullOrWhiteSpace(li.InnerText))
                                {
                                    string relativePath = li.InnerText.Trim().Replace('/', '\\');
                                    string folderPath = relativePath == "\\" || relativePath == ""
                                        ? mod.RootDir.FullName
                                        : Path.Combine(mod.RootDir.FullName, relativePath);

                                    if (Directory.Exists(folderPath)) activeFolders.Add(folderPath);
                                }
                            }
                            if (activeFolders.Count > 0) break;
                        }
                    }
                }
                catch { }
            }

            if (activeFolders.Count == 0)
            {
                activeFolders.Add(mod.RootDir.FullName);
            }

            return activeFolders.Distinct().ToList();
        }


        // 這個方法負責取得 AllEffective語言路徑 資料。
        // EN: This method gets all effective language paths.
        public static List<string> GetAllEffectiveLangPaths(ModMetaData mod)
        {
            List<string> result = new List<string>();
            var activeRoots = ParseLoadFolders(mod);

            foreach (var root in activeRoots)
            {
                try
                {
                    var dirs = Directory.GetDirectories(root, "Languages", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            if (result.Count == 0)
            {
                try
                {
                    var dirs = Directory.GetDirectories(mod.RootDir.FullName, "Languages", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            return result.Distinct().ToList();
        }

        // 這個方法負責判斷 HasNative目標語言 條件是否成立。
        // EN: This method checks has native target language.
        public static bool HasNativeTargetLanguage(ModMetaData mod, TargetLanguage targetLang)
        {
            if (mod == null) return false;

            string targetFolder = GetFolderNameByLanguage(targetLang);
            foreach (string langRoot in GetAllEffectiveLangPaths(mod))
            {
                try
                {
                    foreach (string targetRoot in ResolveLanguageFolders(langRoot, targetFolder))
                    {
                        if (Directory.GetFiles(targetRoot, "*.xml", SearchOption.AllDirectories).Length > 0)
                        {
                            return true;
                        }
                    }
                }
                catch { }
            }

            return false;
        }


        // 這個方法負責取得 AllEffectiveDefs路徑 資料。
        // EN: This method gets all effective defs paths.
        public static List<string> GetAllEffectiveDefsPaths(ModMetaData mod)
        {
            List<string> result = new List<string>();
            var activeRoots = ParseLoadFolders(mod);

            foreach (var root in activeRoots)
            {
                try
                {
                    string defPath = Path.Combine(root, "Defs");
                    if (Directory.Exists(defPath) && !IsOldVersionPath(mod.RootDir.FullName, defPath))
                    {
                        result.Add(defPath);
                    }
                }
                catch { }
            }

            if (result.Count == 0)
            {
                try
                {
                    var dirs = Directory.GetDirectories(mod.RootDir.FullName, "Defs", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        if (!IsOldVersionPath(mod.RootDir.FullName, dir)) result.Add(dir);
                    }
                }
                catch { }
            }

            return result.Distinct().ToList();
        }

    }
}
