using UnityEngine;

namespace AutoTranslator_Core
{
    public class UIInterceptorLifecycle : MonoBehaviour
    {
        private void OnApplicationQuit()
        {
            UIInterceptor.FlushCache();
            ModNameTranslationCache.SaveIfDirty();
        }

        private void OnDestroy()
        {
            UIInterceptor.FlushCache();
            ModNameTranslationCache.SaveIfDirty();
        }
    }
}
