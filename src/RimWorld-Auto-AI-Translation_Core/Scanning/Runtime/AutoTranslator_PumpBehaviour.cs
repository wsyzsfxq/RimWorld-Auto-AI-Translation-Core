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

        /// <summary>
        /// MonoBehaviour 主執行緒派發器（覆蓋主選單階段）
        /// </summary>
        public class AutoTranslator_PumpBehaviour : UnityEngine.MonoBehaviour
        {
            private float _accumulator = 0f;

            private void Update()
            {
                // 每 0.5 秒檢查一次佇列，避免每幀都 lock
                _accumulator += UnityEngine.Time.unscaledDeltaTime;
                if (_accumulator >= 0.5f)
                {
                    _accumulator = 0f;
                    AutoTranslatorScanner.PumpMainThreadDispatcher();
                }
            }
        }
}
