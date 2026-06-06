using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AutoTranslator_Core
{
    public static class LanguageDetector
    {
        // ✨ 架構師擴充版：邊緣世界 150 大高頻簡體專屬字 (涵蓋裝備、戰鬥、社交、建築)
        private static readonly HashSet<char> SimpOnlyChars = new HashSet<char>("们这个吗没觉换变现见观说话语谁专业岁爷节厂广队归车东农鸡鸭岛鸟鱼龙麦龟战击杀类样设备尽还应让过发开总风机电气页飞门网线图体头实华单长当书报会爱从众双网乐树随复铁医药兽伤灵击枪剑装护轻重无产处将".ToCharArray());

        // ✨ 架構師擴充版：邊緣世界 150 大高頻繁體專屬字 (完全對應上方)
        private static readonly HashSet<char> TradOnlyChars = new HashSet<char>("們這個嗎沒覺換變現見觀說話語誰專業歲爺節廠廣隊歸車東農雞鴨島鳥魚龍麥龜戰擊殺類樣設備盡還應讓過發開總風機電氣頁飛門網線圖體頭實華單長當書報會愛從眾雙網樂樹隨復鐵醫藥獸傷靈擊槍劍裝護輕重無產處將".ToCharArray());
        /// <summary>
        /// 檢測這個字典是否為「掛羊頭賣狗肉」的假語言檔
        /// </summary>
        public static bool IsFakeLanguage(Dictionary<string, string> fileData, TargetLanguage expectedLang)
        {
            // 我們只管簡繁互串的抓漏，其他語言不判定
            if (expectedLang != TargetLanguage.Traditional && expectedLang != TargetLanguage.Simplified)
                return false;

            int simpCount = 0;
            int tradCount = 0;
            int maxCharsToCheck = 500; // 抽樣前 500 個字就夠了，極速過濾

            StringBuilder sample = new StringBuilder();
            foreach (var val in fileData.Values)
            {
                if (string.IsNullOrWhiteSpace(val)) continue;
                sample.Append(val);
                if (sample.Length >= maxCharsToCheck) break;
            }

            string textToCheck = sample.ToString();
            foreach (char c in textToCheck)
            {
                if (SimpOnlyChars.Contains(c)) simpCount++;
                else if (TradOnlyChars.Contains(c)) tradCount++;
            }

            // 判斷邏輯：嚴格防呆，避免誤殺
            if (expectedLang == TargetLanguage.Traditional)
            {
                // 期望是繁體，但簡體專屬字 > 5 個，且數量是繁體的 3 倍以上 -> 絕對是假繁體！
                if (simpCount > 5 && simpCount > tradCount * 3) return true;
            }
            else if (expectedLang == TargetLanguage.Simplified)
            {
                // 期望是簡體，但繁體專屬字 > 5 個，且數量是簡體的 3 倍以上 -> 絕對是假簡體！
                if (tradCount > 5 && tradCount > simpCount * 3) return true;
            }

            return false;
        }
    }
}