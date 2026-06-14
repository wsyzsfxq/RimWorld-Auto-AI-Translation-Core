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
        [StaticConstructorOnStartup]
        public static class AutoTranslator_StartupHook
        {
            static AutoTranslator_StartupHook()
            {
                // 註冊一個無限循環的 LongEvent 來定期 Pump 主執行緒佇列
                // 這個技巧確保即使在主選單（沒有 Game 物件），佇列也能被處理
                Verse.LongEventHandler.QueueLongEvent(() =>
                {
                    // 開機只執行一次，掛載 Update 鉤子
                    UnityEngine.Object hook = new UnityEngine.GameObject("ATC_MainThreadPump");
                    UnityEngine.Object.DontDestroyOnLoad(hook);
                    ((UnityEngine.GameObject)hook).AddComponent<AutoTranslator_PumpBehaviour>();
                    ((UnityEngine.GameObject)hook).AddComponent<UIInterceptorLifecycle>();
                }, null, false, null);
            }
        }
}
