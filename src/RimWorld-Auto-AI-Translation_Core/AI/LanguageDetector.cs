using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoTranslator_Core
{
    public static class LanguageDetector
    {
        private const string SimplifiedChars = "\u4EEC\u8FD9\u4E2A\u5417\u6CA1\u89C9\u6362\u53D8\u73B0\u89C1\u89C2\u8BF4\u8BDD\u8BED\u8C01\u4E13\u4E1A\u5C81\u7237\u8282\u5382\u5E7F\u961F\u5F52\u8F66\u4E1C\u519C\u9E21\u9E2D\u5C9B\u9E1F\u9C7C\u9F99\u9EA6\u9F9F\u6218\u51FB\u6740\u7C7B\u6837\u8BBE\u5907\u5C3D\u8FD8\u5E94\u8BA9\u8FC7\u53D1\u5F00\u603B\u98CE\u673A\u7535\u6C14\u9875\u98DE\u95E8\u7F51\u7EBF\u56FE\u4F53\u5934\u5B9E\u534E\u5355\u957F\u5F53\u4E66\u62A5\u4F1A\u7231\u4ECE\u4F17\u53CC\u7F51\u4E50\u6811\u968F\u590D\u94C1\u533B\u836F\u517D\u4F24\u7075\u51FB\u67AA\u5251\u88C5\u62A4\u8F7B\u91CD\u65E0\u4EA7\u5904\u5C06";
        private const string TraditionalChars = "\u5011\u9019\u500B\u55CE\u6C92\u89BA\u63DB\u8B8A\u73FE\u898B\u89C0\u8AAA\u8A71\u8A9E\u8AB0\u5C08\u696D\u6B72\u723A\u7BC0\u5EE0\u5EE3\u968A\u6B78\u8ECA\u6771\u8FB2\u96DE\u9D28\u5CF6\u9CE5\u9B5A\u9F8D\u9EA5\u9F9C\u6230\u64CA\u6BBA\u985E\u6A23\u8A2D\u5099\u76E1\u9084\u61C9\u8B93\u904E\u767C\u958B\u7E3D\u98A8\u6A5F\u96FB\u6C23\u9801\u98DB\u9580\u7DB2\u7DDA\u5716\u9AD4\u982D\u5BE6\u83EF\u55AE\u9577\u7576\u66F8\u5831\u6703\u611B\u5F9E\u773E\u96D9\u7DB2\u6A02\u6A39\u96A8\u5FA9\u9435\u91AB\u85E5\u7378\u50B7\u9748\u64CA\u69CD\u528D\u88DD\u8B77\u8F15\u91CD\u7121\u7522\u8655\u5C07";
        private static readonly HashSet<char> SimpOnlyChars = new HashSet<char>(SimplifiedChars.ToCharArray());
        private static readonly HashSet<char> TradOnlyChars = new HashSet<char>(TraditionalChars.ToCharArray());
        private static readonly Dictionary<char, char> TradToSimpChars = BuildCharMap(TraditionalChars, SimplifiedChars, GetSupplementalTradToSimpPairs());
        private static readonly Dictionary<char, char> SimpToTradChars = BuildCharMap(SimplifiedChars, TraditionalChars, GetSupplementalSimpToTradPairs());

        public static bool LooksLikeSimplified(string text)
        {
            CountChineseVariants(text, out int simpCount, out int tradCount);
            return simpCount > 0 && simpCount >= tradCount;
        }

        public static bool LooksLikeTraditional(string text)
        {
            CountChineseVariants(text, out int simpCount, out int tradCount);
            return tradCount > 0 && tradCount >= simpCount;
        }

        public static string NormalizeChineseVariant(string text, TargetLanguage targetLang)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (targetLang != TargetLanguage.Simplified && targetLang != TargetLanguage.Traditional) return text;

            Dictionary<char, char> map = targetLang == TargetLanguage.Simplified ? TradToSimpChars : SimpToTradChars;
            StringBuilder result = null;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (map.TryGetValue(c, out char replacement))
                {
                    if (result == null)
                    {
                        result = new StringBuilder(text.Length);
                        result.Append(text, 0, i);
                    }
                    result.Append(replacement);
                }
                else if (result != null)
                {
                    result.Append(c);
                }
            }

            return result == null ? text : result.ToString();
        }

        public static bool LooksLikeTargetLanguage(string text, TargetLanguage expectedLang)
        {
            string sample = NormalizeLanguageSample(text);
            if (sample.Length < 2) return false;

            CountScripts(sample, out int hanCount, out int kanaCount, out int hangulCount, out int cyrillicCount, out int latinCount, out int letterCount);
            if (letterCount < 2) return false;

            switch (expectedLang)
            {
                case TargetLanguage.Traditional:
                case TargetLanguage.Simplified:
                    return hanCount >= 2
                        && kanaCount == 0
                        && hangulCount == 0
                        && cyrillicCount == 0
                        && Percent(hanCount, letterCount) >= 35;

                case TargetLanguage.Japanese:
                    return kanaCount >= 1
                        && hanCount + kanaCount >= 2
                        && hangulCount == 0
                        && cyrillicCount == 0
                        && Percent(hanCount + kanaCount, letterCount) >= 35;

                case TargetLanguage.Korean:
                    return hangulCount >= 2
                        && Percent(hangulCount, letterCount) >= 35;

                case TargetLanguage.Russian:
                    return cyrillicCount >= 3
                        && Percent(cyrillicCount, letterCount) >= 70
                        && !ContainsAny(sample, UkrainianMarkerChars);

                case TargetLanguage.Ukrainian:
                    return cyrillicCount >= 3
                        && Percent(cyrillicCount, letterCount) >= 70
                        && ContainsAny(sample, UkrainianMarkerChars);

                case TargetLanguage.English:
                    return LooksLikeEnglish(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount);

                case TargetLanguage.French:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, FrenchMarkerChars,
                        new[] { "avec", "dans", "pour", "vous", "une", "des", "les", "aux" });

                case TargetLanguage.German:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, GermanMarkerChars,
                        new[] { "und", "der", "die", "das", "nicht", "eine", "mit", "ist" });

                case TargetLanguage.Spanish:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, SpanishMarkerChars,
                        new[] { "para", "con", "una", "los", "las", "del", "que", "por" });

                case TargetLanguage.Italian:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, ItalianMarkerChars,
                        new[] { "per", "con", "una", "gli", "della", "delle", "che", "non" });

                case TargetLanguage.Polish:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, PolishMarkerChars,
                        new[] { "oraz", "jest", "nie", "dla", "przez", "jego", "jako", "tego" });

                case TargetLanguage.Portuguese:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, PortugueseMarkerChars,
                        new[] { "para", "com", "uma", "dos", "das", "voce", "voc\u00EA", "n\u00E3o", "por" });

                case TargetLanguage.Turkish:
                    return LooksLikeLatinTarget(sample, hanCount, kanaCount, hangulCount, cyrillicCount, latinCount, letterCount, TurkishMarkerChars,
                        new[] { "bir", "icin", "i\u00E7in", "degil", "de\u011Fil", "olan", "daha", "ile" });

                default:
                    return false;
            }
        }

        private static string UkrainianMarkerChars => "\u0456\u0457\u0454\u0491\u0406\u0407\u0404\u0490";
        private static string FrenchMarkerChars => "\u00E0\u00E2\u00E6\u00E7\u00E9\u00E8\u00EA\u00EB\u00EE\u00EF\u00F4\u0153\u00F9\u00FB\u00FC\u00FF\u00C0\u00C2\u00C6\u00C7\u00C9\u00C8\u00CA\u00CB\u00CE\u00CF\u00D4\u0152\u00D9\u00DB\u00DC\u0178";
        private static string GermanMarkerChars => "\u00E4\u00F6\u00FC\u00DF\u00C4\u00D6\u00DC\u1E9E";
        private static string SpanishMarkerChars => "\u00E1\u00E9\u00ED\u00F3\u00FA\u00FC\u00F1\u00BF\u00A1\u00C1\u00C9\u00CD\u00D3\u00DA\u00DC\u00D1";
        private static string ItalianMarkerChars => "\u00E0\u00E8\u00E9\u00EC\u00ED\u00EE\u00F2\u00F3\u00F9\u00FA\u00C0\u00C8\u00C9\u00CC\u00CD\u00CE\u00D2\u00D3\u00D9\u00DA";
        private static string PolishMarkerChars => "\u0105\u0107\u0119\u0142\u0144\u00F3\u015B\u017A\u017C\u0104\u0106\u0118\u0141\u0143\u00D3\u015A\u0179\u017B";
        private static string PortugueseMarkerChars => "\u00E1\u00E2\u00E3\u00E0\u00E7\u00E9\u00EA\u00ED\u00F3\u00F4\u00F5\u00FA\u00FC\u00C1\u00C2\u00C3\u00C0\u00C7\u00C9\u00CA\u00CD\u00D3\u00D4\u00D5\u00DA\u00DC";
        private static string TurkishMarkerChars => "\u00E7\u011F\u0131\u00F6\u015F\u00FC\u0130\u00C7\u011E\u00D6\u015E\u00DC";
        private static string LatinMarkerChars => FrenchMarkerChars + GermanMarkerChars + SpanishMarkerChars + ItalianMarkerChars + PolishMarkerChars + PortugueseMarkerChars + TurkishMarkerChars;

        private static void CountChineseVariants(string text, out int simpCount, out int tradCount)
        {
            simpCount = 0;
            tradCount = 0;

            if (string.IsNullOrWhiteSpace(text)) return;

            foreach (char c in text)
            {
                if (SimpOnlyChars.Contains(c)) simpCount++;
                else if (TradOnlyChars.Contains(c)) tradCount++;
            }
        }

        private static string NormalizeLanguageSample(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string sample = text
                .Replace("\\n", " ")
                .Replace("\\r", " ")
                .Replace("\n", " ")
                .Replace("\r", " ");
            sample = Regex.Replace(sample, @"<[^>]+>", " ");
            sample = Regex.Replace(sample, @"\{[^}]*\}|\[[^\]]*\]|\$[A-Za-z0-9_]+|%[A-Za-z]", " ");
            sample = Regex.Replace(sample, @"\s+", " ");
            return sample.Trim();
        }

        private static void CountScripts(string text, out int hanCount, out int kanaCount, out int hangulCount, out int cyrillicCount, out int latinCount, out int letterCount)
        {
            hanCount = 0;
            kanaCount = 0;
            hangulCount = 0;
            cyrillicCount = 0;
            latinCount = 0;
            letterCount = 0;

            foreach (char c in text)
            {
                if (!char.IsLetter(c)) continue;
                letterCount++;

                if (IsHan(c)) hanCount++;
                else if (IsKana(c)) kanaCount++;
                else if (IsHangul(c)) hangulCount++;
                else if (IsCyrillic(c)) cyrillicCount++;
                else if (IsLatin(c)) latinCount++;
            }
        }

        private static bool LooksLikeEnglish(string sample, int hanCount, int kanaCount, int hangulCount, int cyrillicCount, int latinCount, int letterCount)
        {
            if (hanCount + kanaCount + hangulCount + cyrillicCount > 0) return false;
            if (latinCount < 3 || Percent(latinCount, letterCount) < 80) return false;
            if (ContainsAny(sample, LatinMarkerChars)) return false;

            return sample.Length <= 28 || CountWordMatches(sample, new[] { "the", "and", "for", "with", "from", "this", "that", "you", "your", "not" }) > 0;
        }

        private static bool LooksLikeLatinTarget(string sample, int hanCount, int kanaCount, int hangulCount, int cyrillicCount, int latinCount, int letterCount, string markerChars, string[] stopWords)
        {
            if (hanCount + kanaCount + hangulCount + cyrillicCount > 0) return false;
            if (latinCount < 4 || Percent(latinCount, letterCount) < 75) return false;
            if (ContainsAny(sample, markerChars)) return true;

            return sample.Length >= 20 && CountWordMatches(sample, stopWords) >= 2;
        }

        private static Dictionary<char, char> BuildCharMap(string sourceChars, string targetChars, Dictionary<char, char> supplementalPairs)
        {
            var map = new Dictionary<char, char>();
            int count = sourceChars.Length < targetChars.Length ? sourceChars.Length : targetChars.Length;
            for (int i = 0; i < count; i++)
            {
                if (sourceChars[i] != targetChars[i])
                {
                    map[sourceChars[i]] = targetChars[i];
                }
            }

            foreach (var pair in supplementalPairs)
            {
                if (pair.Key != pair.Value)
                {
                    map[pair.Key] = pair.Value;
                }
            }

            return map;
        }

        private static Dictionary<char, char> GetSupplementalTradToSimpPairs()
        {
            return new Dictionary<char, char>
            {
                {'內','内'}, {'為','为'}, {'與','与'}, {'後','后'}, {'於','于'}, {'壓','压'}, {'區','区'},
                {'參','参'}, {'隻','只'}, {'臺','台'}, {'號','号'}, {'員','员'}, {'問','问'}, {'國','国'},
                {'圍','围'}, {'圖','图'}, {'場','场'}, {'塊','块'}, {'壞','坏'}, {'壽','寿'}, {'夢','梦'},
                {'夠','够'}, {'奧','奥'}, {'婦','妇'}, {'學','学'}, {'寶','宝'}, {'寫','写'}, {'層','层'},
                {'屬','属'}, {'嶼','屿'}, {'幣','币'}, {'幫','帮'}, {'庫','库'}, {'廢','废'}, {'彈','弹'},
                {'強','强'}, {'徑','径'}, {'態','态'}, {'慣','惯'}, {'慘','惨'}, {'慮','虑'}, {'戲','戏'},
                {'戶','户'}, {'擁','拥'}, {'擇','择'}, {'擔','担'}, {'據','据'}, {'擬','拟'}, {'擴','扩'},
                {'擺','摆'}, {'敵','敌'}, {'數','数'}, {'斷','断'}, {'時','时'}, {'曆','历'}, {'會','会'},
                {'條','条'}, {'樓','楼'}, {'標','标'}, {'樣','样'}, {'檔','档'}, {'檢','检'}, {'權','权'},
                {'歡','欢'}, {'歲','岁'}, {'殘','残'}, {'殼','壳'}, {'氣','气'}, {'滅','灭'}, {'漢','汉'},
                {'營','营'}, {'獎','奖'}, {'獲','获'}, {'環','环'}, {'畫','画'}, {'療','疗'}, {'發','发'},
                {'盜','盗'}, {'盤','盘'}, {'礦','矿'}, {'禮','礼'}, {'種','种'}, {'稱','称'}, {'穩','稳'},
                {'窩','窝'}, {'競','竞'}, {'筆','笔'}, {'築','筑'}, {'節','节'}, {'糧','粮'}, {'級','级'},
                {'統','统'}, {'經','经'}, {'給','给'}, {'絕','绝'}, {'綠','绿'}, {'維','维'}, {'緊','紧'},
                {'緒','绪'}, {'線','线'}, {'練','练'}, {'縣','县'}, {'總','总'}, {'績','绩'}, {'織','织'},
                {'罰','罚'}, {'聖','圣'}, {'聽','听'}, {'職','职'}, {'聯','联'}, {'腦','脑'}, {'臉','脸'},
                {'臟','脏'}, {'舊','旧'}, {'艙','舱'}, {'萬','万'}, {'葉','叶'}, {'處','处'}, {'讓','让'},
                {'術','术'}, {'衛','卫'}, {'裝','装'}, {'複','复'}, {'規','规'}, {'視','视'}, {'覺','觉'},
                {'覽','览'}, {'觸','触'}, {'訓','训'}, {'記','记'}, {'許','许'}, {'該','该'}, {'認','认'},
                {'誌','志'}, {'語','语'}, {'誤','误'}, {'說','说'}, {'請','请'}, {'諾','诺'}, {'謀','谋'},
                {'證','证'}, {'識','识'}, {'譯','译'}, {'護','护'}, {'變','变'}, {'貝','贝'}, {'貨','货'},
                {'貧','贫'}, {'費','费'}, {'資','资'}, {'賊','贼'}, {'賓','宾'}, {'賞','赏'}, {'賣','卖'},
                {'質','质'}, {'購','购'}, {'贏','赢'}, {'趨','趋'}, {'車','车'}, {'軌','轨'}, {'載','载'},
                {'輕','轻'}, {'輛','辆'}, {'輸','输'}, {'轉','转'}, {'辦','办'}, {'邊','边'}, {'遞','递'},
                {'選','选'}, {'遺','遗'}, {'郵','邮'}, {'鄉','乡'}, {'醫','医'}, {'釋','释'}, {'針','针'},
                {'鈕','钮'}, {'鉤','钩'}, {'銀','银'}, {'銅','铜'}, {'銳','锐'}, {'鋼','钢'}, {'錄','录'},
                {'錯','错'}, {'鍵','键'}, {'鎖','锁'}, {'鎮','镇'}, {'長','长'}, {'門','门'}, {'開','开'},
                {'關','关'}, {'闆','板'}, {'間','间'}, {'隊','队'}, {'階','阶'}, {'險','险'}, {'雙','双'},
                {'雜','杂'}, {'離','离'}, {'難','难'}, {'電','电'}, {'霧','雾'}, {'靈','灵'}, {'靜','静'},
                {'頂','顶'}, {'項','项'}, {'順','顺'}, {'領','领'}, {'頭','头'}, {'顯','显'}, {'類','类'},
                {'飛','飞'}, {'飢','饥'}, {'飯','饭'}, {'飲','饮'}, {'飼','饲'}, {'體','体'}, {'驗','验'},
                {'驅','驱'}, {'驚','惊'}, {'髒','脏'}, {'鬥','斗'}, {'魚','鱼'}, {'鳥','鸟'}, {'鳴','鸣'},
                {'鹽','盐'}, {'點','点'}, {'黨','党'}, {'齒','齿'}, {'齡','龄'}, {'龍','龙'}
            };
        }

        private static Dictionary<char, char> GetSupplementalSimpToTradPairs()
        {
            var map = new Dictionary<char, char>();
            foreach (var pair in GetSupplementalTradToSimpPairs())
            {
                if (!map.ContainsKey(pair.Value))
                {
                    map[pair.Value] = pair.Key;
                }
            }
            return map;
        }

        private static int CountWordMatches(string text, string[] words)
        {
            int count = 0;
            foreach (string word in words)
            {
                if (Regex.IsMatch(text, @"(?<!\p{L})" + Regex.Escape(word) + @"(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    count++;
                }
            }
            return count;
        }

        private static int Percent(int part, int total)
        {
            if (total <= 0) return 0;
            return (int)((part * 100.0) / total);
        }

        private static bool ContainsAny(string text, string chars)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(chars)) return false;
            foreach (char c in text)
            {
                if (chars.IndexOf(c) >= 0) return true;
            }
            return false;
        }

        private static bool IsHan(char c)
        {
            return (c >= '\u3400' && c <= '\u4DBF')
                || (c >= '\u4E00' && c <= '\u9FFF')
                || (c >= '\uF900' && c <= '\uFAFF');
        }

        private static bool IsKana(char c)
        {
            return (c >= '\u3040' && c <= '\u30FF')
                || (c >= '\u31F0' && c <= '\u31FF')
                || (c >= '\uFF66' && c <= '\uFF9F');
        }

        private static bool IsHangul(char c)
        {
            return (c >= '\u1100' && c <= '\u11FF')
                || (c >= '\u3130' && c <= '\u318F')
                || (c >= '\uAC00' && c <= '\uD7AF');
        }

        private static bool IsCyrillic(char c)
        {
            return (c >= '\u0400' && c <= '\u04FF')
                || (c >= '\u0500' && c <= '\u052F');
        }

        private static bool IsLatin(char c)
        {
            return (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || (c >= '\u00C0' && c <= '\u024F');
        }

        public static bool IsFakeLanguage(Dictionary<string, string> fileData, TargetLanguage expectedLang)
        {
            if (expectedLang != TargetLanguage.Traditional && expectedLang != TargetLanguage.Simplified)
                return false;

            int simpCount = 0;
            int tradCount = 0;
            int maxCharsToCheck = 500;

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

            if (expectedLang == TargetLanguage.Traditional)
            {
                if (simpCount > 5 && simpCount > tradCount * 3) return true;
            }
            else if (expectedLang == TargetLanguage.Simplified)
            {
                if (tradCount > 5 && tradCount > simpCount * 3) return true;
            }

            return false;
        }
    }
}
