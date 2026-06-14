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
        /// 主執行緒派發器：每個 Tick 檢查是否有待處理的跨執行緒請求
        /// (修正 P2-1：MemoryDrop 主執行緒守護)
        /// </summary>
        public class AutoTranslator_MainThreadDispatcher : GameComponent
        {
            // 為了讓 Component 在主選單也能跑（沒有 Game 物件時），
            // 我們另外用 LongEventHandler 做雙保險
            public AutoTranslator_MainThreadDispatcher(Game game) { }

            public override void GameComponentUpdate()
            {
                AutoTranslatorScanner.PumpMainThreadDispatcher();
            }
        }
}
