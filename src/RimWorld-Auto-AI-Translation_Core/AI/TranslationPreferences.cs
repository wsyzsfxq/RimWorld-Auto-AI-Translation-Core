namespace AutoTranslator_Core
{
    internal static class TranslationPreferences
    {
        internal static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        internal static string BuildPromptRules(string value)
        {
            string instructions = Normalize(value);
            if (string.IsNullOrWhiteSpace(instructions)) return string.Empty;
            return "\n[USER TRANSLATION PREFERENCES]\n" + instructions +
                "\n[END USER TRANSLATION PREFERENCES]\n" +
                "Apply these preferences to wording, terminology and style. " +
                "Preserve the required target language, output schema, item IDs, placeholders and tags.\n";
        }
    }
}
