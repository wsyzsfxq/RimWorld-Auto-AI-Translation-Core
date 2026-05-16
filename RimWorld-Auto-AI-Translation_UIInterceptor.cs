using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;

namespace AutoTranslator_Core
{
    // 🌟 咪咪特製：在遊戲啟動時自動掛載，這就是我們的「攔截器總機」！
    [StaticConstructorOnStartup]
    public static class UIInterceptor
    {

        // 記憶體快取字典：前台瞬間查表，速度跟光一樣快！O(1)
        public static ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>();

        // 🌟 咪咪的無間地獄終結者：這輩子都不准再問 AI 的黑名單字典！
        public static ConcurrentDictionary<string, bool> IgnoredCache = new ConcurrentDictionary<string, bool>();

        // 排隊等候翻譯的清單
        private static ConcurrentQueue<string> TranslationQueue = new ConcurrentQueue<string>();

        // 防重複發送的標記（避免同一個 UI 文字在 60 幀裡被排隊 60 次）
        private static ConcurrentDictionary<string, bool> PendingTranslations = new ConcurrentDictionary<string, bool>();

        // 存檔路徑 (存在名揚的 Mod 資料夾裡)
        private static readonly string CacheFilePath;

        // ==========================================
        // 🌟 咪咪的效能核彈：把每次都要重新建立的正則表達式，變成預先編譯的靜態機關槍！
        // ==========================================
        private static readonly System.Text.RegularExpressions.Regex LetterRegex = new System.Text.RegularExpressions.Regex(@"\p{L}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex EnglishRegex = new System.Text.RegularExpressions.Regex(@"[a-zA-Z]", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex CyrillicRegex = new System.Text.RegularExpressions.Regex(@"\p{IsCyrillic}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex KanaRegex = new System.Text.RegularExpressions.Regex(@"\p{IsHiragana}|\p{IsKatakana}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex HangulRegex = new System.Text.RegularExpressions.Regex(@"\p{IsHangulSyllables}", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex CJKRegex = new System.Text.RegularExpressions.Regex(@"\p{IsCJKUnifiedIdeographs}", System.Text.RegularExpressions.RegexOptions.Compiled);

        static UIInterceptor()
        {
            CacheFilePath = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "UI_Hardcoded_Cache.json");
            LoadCache();

            // ==========================================
            // 🌟 咪咪特製：遊戲啟動瞬間，直接發動高級微創排毒手術！
            // ==========================================
            AutoTranslatorScanner.RunAdvancedDetoxScanner();

            // 啟動背景翻譯執行緒！
            Task.Run(() => BackgroundTranslationWorker());

            // 🌟 啟動 Harmony 霸王硬上弓，直接劫持 Unity 的底層 GUI！
            var harmony = new Harmony("MingYang.AutoTranslation.UIInterceptor");
            harmony.PatchAll(typeof(UIInterceptor).Assembly);

            Log.Message("[AutoTranslationCore] 🛡️ " + "ATC_Log_UIInterceptorStarted".Translate());
        }

        private static void LoadCache()
        {
            try
            {
                if (File.Exists(CacheFilePath))
                {
                    string json = File.ReadAllText(CacheFilePath);
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded) Cache[kvp.Key] = kvp.Value;
                    }
                    Log.Message("[AutoTranslationCore] 📦 " + "ATC_Log_UICacheLoaded".Translate(Cache.Count));
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] ⚠️ " + "ATC_LogError_UICacheLoadFailed".Translate(ex.Message));
            }
        }

        public static void SaveCache()
        {
            try
            {
                var dictToSave = new Dictionary<string, string>(Cache);
                string json = JsonConvert.SerializeObject(dictToSave, Formatting.Indented);
                File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] ⚠️ " + "ATC_LogError_UICacheSaveFailed".Translate(ex.Message));
            }
        }

        // 🌟 咪咪小工具：讓設定面板能看到目前還有多少人在排隊
        public static int GetQueueCount()
        {
            return TranslationQueue.Count;
        }

        // 🌟 把發現的野生生字丟進排隊區
        public static void QueueForTranslation(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;

            // 🚀 超高速攔截：如果在黑名單裡，看都不看直接踢掉！
            if (IgnoredCache.ContainsKey(text)) return;

            // 已經在排隊了也踢掉
            if (PendingTranslations.ContainsKey(text)) return;

            // 如果純數字或連個字母都沒有，拉黑！
            if (text.All(char.IsDigit) || !LetterRegex.IsMatch(text))
            {
                IgnoredCache[text] = true;
                return;
            }

            var targetLang = AutoTranslatorMod.Settings.TargetLang;
            bool isForeignText = false;

            // 🔍 使用預先編譯的超高速正則表達式！
            bool hasEnglish = EnglishRegex.IsMatch(text);
            bool hasCyrillic = CyrillicRegex.IsMatch(text);
            bool hasKana = KanaRegex.IsMatch(text);
            bool hasHangul = HangulRegex.IsMatch(text);
            bool hasCJK = CJKRegex.IsMatch(text);

            switch (targetLang)
            {
                case TargetLanguage.Traditional:
                case TargetLanguage.Simplified:
                    if (hasEnglish || hasCyrillic || hasKana || hasHangul) isForeignText = true;
                    break;
                case TargetLanguage.Japanese:
                    if (hasEnglish || hasCyrillic || hasHangul || (hasCJK && !hasKana)) isForeignText = true;
                    break;
                case TargetLanguage.Korean:
                    if (hasEnglish || hasCyrillic || hasKana || hasCJK) isForeignText = true;
                    break;
                case TargetLanguage.English:
                    if (hasCJK || hasKana || hasHangul || hasCyrillic) isForeignText = true;
                    break;
                case TargetLanguage.Russian:
                case TargetLanguage.Ukrainian:
                    if (hasEnglish || hasCJK || hasKana || hasHangul) isForeignText = true;
                    break;
                default:
                    if (hasEnglish || hasCJK) isForeignText = true;
                    break;
            }

            // 🛑 如果判斷這不是外語生肉，加進黑名單！這輩子都別再浪費效能檢查它了！
            if (!isForeignText)
            {
                IgnoredCache[text] = true;
                return;
            }

            if (PendingTranslations.TryAdd(text, true))
            {
                TranslationQueue.Enqueue(text);
            }
        }

        // 🌟 咪咪特製背景工人：默默打包、默默翻譯、默默存檔 (絕對不卡 FPS)
        private static async Task BackgroundTranslationWorker()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(2000);

                    if (TranslationQueue.Count > 0 && AutoTranslatorMod.Settings.ApiConfigs.Any(c => !string.IsNullOrEmpty(c.Key)))
                    {
                        List<string> batch = new List<string>();

                        while (batch.Count < 20 && TranslationQueue.TryDequeue(out string text))
                        {
                            batch.Add(text);
                        }

                        if (batch.Count > 0)
                        {
                            var translatedBatch = await AutoTranslatorAPI.TranslateBatchAsync(batch);

                            if (translatedBatch != null && translatedBatch.Count == batch.Count)
                            {
                                bool hasNewCache = false;
                                for (int i = 0; i < batch.Count; i++)
                                {
                                    string original = batch[i];
                                    string translated = translatedBatch[i];

                                    // 如果有成功翻譯且不一樣，就存進字典！
                                    if (!string.IsNullOrEmpty(translated) && original != translated)
                                    {
                                        Cache[original] = translated;
                                        hasNewCache = true;
                                    }
                                    else
                                    {
                                        // 🛑 核心防禦：如果 AI 翻出來一模一樣（代表不用翻），或者回傳空字串
                                        // 直接加進黑名單！這輩子不要再丟給 AI 了，避免無限套娃死循環！
                                        IgnoredCache[original] = true;
                                    }

                                    // 翻譯完了，把排隊標記拔掉
                                    PendingTranslations.TryRemove(original, out _);
                                }

                                if (hasNewCache) SaveCache();
                            }
                            else
                            {
                                // 如果 API 失敗，移除排隊標記，下次遇到再說
                                foreach (var t in batch) PendingTranslations.TryRemove(t, out _);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 背景報錯默默吞掉
                }
            }
        }
    }

    // ==========================================
    // 🌟 終極外掛區：攔截 Unity 底層畫 UI 的瞬間
    // ==========================================
    [HarmonyPatch(typeof(UnityEngine.GUI), "Label", new Type[] { typeof(UnityEngine.Rect), typeof(UnityEngine.GUIContent), typeof(UnityEngine.GUIStyle) })]
    public static class Patch_GUI_Label_GUIContent
    {
        // ✨ 咪咪的免死金牌開關！
        public static bool BypassInterceptor = false;

        // 🚀 光速通道快取：只要縫合過一次的字串，直接存在這裡！
        private static Dictionary<string, GUIContent> guiContentCache = new Dictionary<string, GUIContent>();

        private static readonly System.Text.RegularExpressions.Regex prefixRegex =
            new System.Text.RegularExpressions.Regex(@"^(\s*(?:\(\d+\)\s*)?\[\d{2}:\d{2}:\d{2}\]\s*)(.*)$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex suffixRegex =
            new System.Text.RegularExpressions.Regex(@"^(.*?)(\s*[\(（][0-9\.,\/\s%]+[\)）]|\s*[:：]\s*[\+\-]?[0-9\.,]+%?|\s*[xXｘＸ]\s*[0-9\.,]+|\s*[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})$",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        public static void Prefix(UnityEngine.Rect position, ref UnityEngine.GUIContent content)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || BypassInterceptor) return;

            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string originalText = content.text;

                // 🌟 咪咪的終極隱形斗篷：如果這是我們自己貼的「顯示原文」視窗（開頭有零寬字元），絕對不准攔截！
                if (originalText.StartsWith("\u200B")) return;

                // 🚀 終極光速通道 1：這段字已經有完整的翻譯結果了，直接替換！(0 延遲，拯救 FPS)
                if (guiContentCache.TryGetValue(originalText, out GUIContent readyContent))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        // 🌟 補上零寬字元 \u200B 作為隱形記號！
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }
                    content = readyContent;
                    return;
                }

                // 🚀 終極光速通道 2：這段字已經被判定為黑名單 (不用翻)，直接放行！(0 延遲)
                if (UIInterceptor.IgnoredCache.ContainsKey(originalText)) return;

                string textToTranslate = originalText;
                string stackTracePart = "";

                // 🔪 咪咪神級手術：報錯分離術！(把人話跟鬼話切開)
                int stackIndex = textToTranslate.IndexOf("\n  at ");
                if (stackIndex == -1) stackIndex = textToTranslate.IndexOf("\n[Ref ");
                if (stackIndex == -1) stackIndex = textToTranslate.IndexOf("System.NullReferenceException");

                if (stackIndex > 0)
                {
                    stackTracePart = textToTranslate.Substring(stackIndex);
                    textToTranslate = textToTranslate.Substring(0, stackIndex);
                }

                // 🛡️ 如果切完之後的「人話」還是太長(>500)或太短，拉黑！
                int len = textToTranslate.Length;
                if (len < 2 || len > 500)
                {
                    UIInterceptor.IgnoredCache[originalText] = true;
                    return;
                }

                string prefix = "";
                string suffix = "";

                // 🔪 第一刀 (頭部)
                var prefixMatch = prefixRegex.Match(textToTranslate);
                if (prefixMatch.Success)
                {
                    prefix = prefixMatch.Groups[1].Value;
                    textToTranslate = prefixMatch.Groups[2].Value;
                }

                // 🔪 第二刀 (尾部)
                var suffixMatch = suffixRegex.Match(textToTranslate);
                if (suffixMatch.Success)
                {
                    textToTranslate = suffixMatch.Groups[1].Value;
                    suffix = suffixMatch.Groups[2].Value;
                }

                if (string.IsNullOrWhiteSpace(textToTranslate))
                {
                    UIInterceptor.IgnoredCache[originalText] = true;
                    return;
                }

                // 🚀 檢查純淨生肉是不是在黑名單
                if (UIInterceptor.IgnoredCache.ContainsKey(textToTranslate))
                {
                    UIInterceptor.IgnoredCache[originalText] = true;
                    return;
                }

                // 🔍 去查記憶體字典！
                if (UIInterceptor.Cache.TryGetValue(textToTranslate, out string translated))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        // 🌟 這裡也補上零寬字元 \u200B 作為隱形記號！
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }

                    // 🩹 手術縫合：把切下來的頭、尾、翻譯好的人話，還有底層的代碼全部黏回去！
                    string finalTranslatedText = prefix + translated + suffix + stackTracePart;

                    GUIContent newContent = new GUIContent(finalTranslatedText, content.image, content.tooltip);

                    // 📦 存入光速通道快取！
                    guiContentCache[originalText] = newContent;

                    content = newContent;
                }
                else
                {
                    // 沒查到？把純淨生肉丟進背景排隊區！
                    UIInterceptor.QueueForTranslation(textToTranslate);
                }
            }
        }
    }
}