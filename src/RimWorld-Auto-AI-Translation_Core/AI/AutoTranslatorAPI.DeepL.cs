using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorAPI
    {

        /// <summary>
        /// 將內部 TargetLanguage 映射為 DeepL API 的語言代碼
        /// 回傳 null 代表 DeepL 不支援該語系
        /// </summary>
        private static string MapToDeepLLangCode(TargetLanguage lang)
        {
            switch (lang)
            {
                case TargetLanguage.Traditional: return "ZH-HANT";
                case TargetLanguage.Simplified: return "ZH-HANS";
                case TargetLanguage.Japanese: return "JA";
                case TargetLanguage.Korean: return "KO";
                case TargetLanguage.Russian: return "RU";
                case TargetLanguage.English: return "EN-US";
                case TargetLanguage.Ukrainian: return null;  // DeepL 不支援烏克蘭文
                // ✨ 架構師擴充：支援 DeepL 新語系
                case TargetLanguage.French: return "FR";
                case TargetLanguage.German: return "DE";
                case TargetLanguage.Spanish: return "ES";
                case TargetLanguage.Italian: return "IT";
                case TargetLanguage.Polish: return "PL";
                case TargetLanguage.Portuguese: return "PT-BR"; // 巴西葡文
                case TargetLanguage.Turkish: return "TR";
                default: return null;
            }
        }
    }
}
