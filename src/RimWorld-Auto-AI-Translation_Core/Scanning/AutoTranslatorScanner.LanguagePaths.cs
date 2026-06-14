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
        // 🌟 咪咪特製：漢化包與翻譯模組超高速過濾器！(V2 強化版)
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

            // 1. 檢查模組名稱常見的漢化關鍵字
            string[] patchKeywords = { "漢化", "汉化", "翻譯", "翻译", "translation", "language", "l10n", "中文", "zh-tw", "zh-cn", "簡繁", "简繁", "繁簡", "繁简" };
            foreach (var kw in patchKeywords)
            {
                if (name.Contains(kw)) return true;
            }

            // 2. 檢查 PackageId 常見的語言代碼後綴或特徵 (加入 zh-pack, _zh 等)
            string[] pidSuffixes = { ".zh", "_zh", "-zh", "zh-pack", ".zhtc", "_zhtc", "-zhtc", ".zhcn", "_zhcn", "-zhcn", ".cn", "_cn", "-cn", ".tw", "_tw", "-tw", "l10n" };
            foreach (var suf in pidSuffixes)
            {
                // 如果直接等於這些後綴，或者包含這些特徵
                if (pid.EndsWith(suf) || pid.Contains(suf + ".") || pid.Contains(suf + "_")) return true;
            }

            // 3. 終極防線：如果 PackageId 剛好就叫 zh (極少數情況)
            if (pid.EndsWith("zh")) return true;

            return false;
        }        // ===== 主執行緒分派器 (修正 P2-1) =====

        public static string GetLocalPackPath()
        {
            string rimWorldRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            return Path.Combine(rimWorldRoot, "Mods/!Translation_AI_Pack");
        }


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
                // ✨ 架構師升級：對應官方的語言資料夾名稱
                case TargetLanguage.French: return "French";
                case TargetLanguage.German: return "German";
                case TargetLanguage.Spanish: return "Spanish";
                case TargetLanguage.Italian: return "Italian";
                case TargetLanguage.Polish: return "Polish";
                case TargetLanguage.Portuguese: return "PortugueseBrazilian"; // 邊緣世界通常用巴西葡語
                case TargetLanguage.Turkish: return "Turkish";
                default: return "English";
            }
        }


        public static string GetSecondaryFolderNameByLanguage(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ChineseSimplified";
                case TargetLanguage.Simplified: return "ChineseTraditional";
                default: return null;
            }
        }


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

        private static string NormalizeLanguageFolderName(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return "";
            return new string(folderName.Where(char.IsLetterOrDigit).ToArray());
        }


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

        private static string[] GetCurrentLoadFolderVersions()
        {
#if RIMWORLD_1_5
            return new[] { "v1.5", "1.5" };
#else
            return new[] { "v1.6", "1.6" };
#endif
        }

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
