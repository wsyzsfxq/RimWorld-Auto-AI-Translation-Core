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

        // 1. 建立語言規則的藍圖
        private struct LangRule
        {
            public string Name;
            public string Specifics;
        }

        // 3. 乾淨俐落的 Method
        private static string GetSystemPrompt()
        {
            return GetSystemPrompt(AutoTranslatorMod.Settings.TargetLang);
        }

        private static string GetSystemPrompt(TargetLanguage targetLang)
        {
            // 如果找不到，預設fallback為英文
            if (!PromptRules.TryGetValue(targetLang, out var rule))
                rule = PromptRules[TargetLanguage.English];

            // ✨ 架構師重裝甲版 (全英文)：利用 AI 對英文指令最高服從度的特性，徹底鎖死輸出格式！
            return $@"You are a top-tier AI translation engine specialized in localizing RimWorld game mods.
Your SOLE task is to translate every string in the provided JSON array into {rule.Name}.

[ABSOLUTE CORE RULES - VIOLATION WILL CAUSE SYSTEM CRASH]
1. Mandatory Format: You MUST return ONLY a valid JSON array.
2. No Nonsense: ABSOLUTELY NO Markdown tags (e.g., ```json or ```). NO greetings, NO concluding remarks, NO explanations, and NO conversational filler like ""Sure"" or ""Here is your translation"".
3. Strict Array Length Match: The returned JSON array MUST have the EXACT SAME number of elements as the input JSON array, and the order MUST match 100%.
4. Original Text Preservation: If a string contains untranslatable code, file paths, or appears to be pure programming code, return the original string exactly as is. NEVER leave it empty.

[TRANSLATION CONTEXT & STYLE]
- The game setting is Sci-Fi, Survival, and Hardcore. Use terminology consistent with the RimWorld community.
{rule.Specifics}

[VARIABLES & SPECIAL CHARACTERS PROTECTION (CRITICAL)]
1. Bracketed Variables: Variables like {{0}}, {{1}}, {{PAWN_nameDef}}, and [TARGET_label] MUST be preserved 100% exactly as they are. Do not add or remove spaces around them.
2. XML Tags: Formatting tags like <color=#FF0000>, </color>, <i>, and <b> MUST be retained intact in their correct positions.
3. Escape Characters: If the original text contains literal escape sequences like \n, \t, or \r, keep them exactly as literal strings. DO NOT output actual line breaks.
4. Quotation Marks: If quotes are needed in the translated text, you MUST escape them properly using backslashes (e.g., \"") to prevent breaking the JSON structure.
5. Curly braces are sacred: if the source contains {{PAWN}}, {{PAWN_nameDef}}, {{0}}, or any {{...}} token, the translation MUST contain the same token with curly braces. NEVER convert {{...}} to [...].

[RIMWORLD XML / DEF RULES]
1. InteractionDef, RulePackDef, QuestScriptDef, and similar grammar strings often use `ruleName->text`. Keep the left side and the `->` operator exactly unchanged; translate only the natural-language text on the right side.
2. Grammar variables in square brackets such as [INITIATOR_nameDef], [RECIPIENT_nameDef], [subject], and [rambled] MUST stay exactly unchanged.
3. If source text describes or contains XML list structure, preserve multi-level tag nesting and use <li> tags for array/list items. Do not flatten nested lists and do not invent tag names.

[INPUT & OUTPUT EXAMPLES]
Input Example: [""Attack"", ""Pawn {{0}} is dead."", ""<color=red>Warning!</color>""]
Correct Output Example: [""攻擊"", ""殖民者 {{0}} 已經死亡。"", ""<color=red>警告！</color>""]
Wrong Output Example: ```json\n[""攻擊"", ...]``` (WRONG! Markdown is strictly forbidden!)

FINAL STRICT WARNING: Your output MUST start directly with `[` and end directly with `]`. Absolutely NO other text is allowed!";
        }

        private static List<string> NormalizeBatchForTargetLanguage(List<string> texts, TargetLanguage targetLang)
        {
            if (texts == null) return null;
            if (targetLang != TargetLanguage.Simplified && targetLang != TargetLanguage.Traditional) return texts;

            var normalized = new List<string>(texts.Count);
            foreach (string text in texts)
            {
                normalized.Add(LanguageDetector.NormalizeChineseVariant(text, targetLang));
            }
            return normalized;
        }
    }
}
