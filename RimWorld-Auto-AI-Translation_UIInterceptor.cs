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
        public static ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>();
        public static ConcurrentDictionary<string, bool> IgnoredCache = new ConcurrentDictionary<string, bool>();
        private static ConcurrentQueue<string> TranslationQueue = new ConcurrentQueue<string>();
        private static ConcurrentDictionary<string, bool> PendingTranslations = new ConcurrentDictionary<string, bool>();
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

            // 🌟 咪咪微創排毒手術！
            AutoTranslatorScanner.RunAdvancedDetoxScanner();

            // ==========================================
            // 🚀 2.0 核心革命：開機自動掛載隱形快取！
            // 玩家再也不用去 Mod 列表打勾那個 !Translation_AI_Pack 了！
            // ==========================================
            AutoTranslatorScanner.MemoryDrop_InjectNow();

            // 啟動 background 翻譯執行緒！
            Task.Run(() => BackgroundTranslationWorker());

            // 🌟 啟動 Harmony 霸王硬上弓！
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

        public static int GetQueueCount() { return TranslationQueue.Count; }

        // ==========================================
        // 🔄 咪咪特製：物理超渡前台 UI 緩存！
        // ==========================================
        public static void ClearUICache()
        {
            Patch_GUI_Label_GUIContent.ClearCache();
            IgnoredCache.Clear(); // 黑名單清空，重新給生詞一次機會
            Log.Message("[AutoTranslationCore] 🔄 UI 翻譯快取與忽略黑名單已完全清空！");
        }

        // ==========================================
        // 🚀 咪咪特製：一鍵熱重載注入總樞紐！
        // ==========================================
        public static void RequestHotReload()
        {
            try
            {
                // 1. 清空前台 UI 緩存與黑名單，強制重新翻譯
                ClearUICache();

                // 2. 重新讀取實體 XML 快取並灌入記憶體
                AutoTranslatorScanner.MemoryDrop_InjectNow();

                // 3. 右上角彈出成功提示
                // ✅ 完美修復 CS8957：加上 .ToString() 統一兩邊的資料類型為 string！
                Messages.Message("ATC_Message_HotReloadSuccess".CanTranslate()
                    ? "ATC_Message_HotReloadSuccess".Translate().ToString()
                    : "🪂 [記憶體空投] 翻譯已即時注入，UI 視窗快取已完全刷新！",
                    MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                Log.Error($"[AutoTranslationCore] Hot reload failed: {ex.Message}");
            }
        }

        // 🌟 把發現的野生生字丟進排隊區
        public static void QueueForTranslation(string text)
        {
            // 🛑 終極防爆閥：如果排隊超過 2000 單，直接踢掉不處理！
            // 避免報錯海嘯把記憶體塞爆，保護大哥的 120 FPS！
            if (TranslationQueue.Count > 2000) return;

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

    [HarmonyPatch(typeof(UnityEngine.GUI), "Label", new Type[] { typeof(UnityEngine.Rect), typeof(UnityEngine.GUIContent), typeof(UnityEngine.GUIStyle) })]
    public static class Patch_GUI_Label_GUIContent
    {
        public static bool BypassInterceptor = false;
        private static Dictionary<string, GUIContent> guiContentCache = new Dictionary<string, GUIContent>();

        // 🌟 咪咪特製：清空這個 Patch 獨享的 GUI 快取字典！
        public static void ClearCache()
        {
            guiContentCache.Clear();
        }

        // 🌟 咪咪極速瘦身：把又慢又卡的正則拔掉，主迴圈只做 O(1) 查表！
        public static void Prefix(UnityEngine.Rect position, ref UnityEngine.GUIContent content)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || BypassInterceptor) return;

            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string originalText = content.text;

                // 如果這是我們自己貼的「顯示原文」視窗，絕對不准攔截！
                if (originalText.StartsWith("\u200B")) return;

                // 🚀 終極光速通道 1：有快取直接換！(0 延遲，拯救 FPS)
                if (guiContentCache.TryGetValue(originalText, out GUIContent readyContent))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }
                    content = readyContent;
                    return;
                }

                // 🚀 終極光速通道 2：黑名單直接放行！(0 延遲)
                if (UIInterceptor.IgnoredCache.ContainsKey(originalText)) return;

                // 🔍 去查記憶體字典！
                if (UIInterceptor.Cache.TryGetValue(originalText, out string translated))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }

                    GUIContent newContent = new GUIContent(translated, content.image, content.tooltip);

                    // 📦 存入光速通道快取！
                    guiContentCache[originalText] = newContent;

                    content = newContent;
                }
                else
                {
                    // 沒查到？把純淨生肉丟進背景排隊區，讓 Task 去頭痛！
                    UIInterceptor.QueueForTranslation(originalText);
                }
            }
        }
    }
}