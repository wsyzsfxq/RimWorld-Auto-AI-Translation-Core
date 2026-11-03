#if RIMWORLD_1_7
namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorAPI
    {
        public static void AbortActiveTranslationRequests(string reason)
        {
            // Cancels translation requests only; the caller still waits for activity drain.
            NativeHttpTransport.AbortTranslations();
        }
    }
}
#endif
