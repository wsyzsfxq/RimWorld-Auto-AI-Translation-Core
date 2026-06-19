using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責翻譯流程的效能統計。
// EN: This file records translation pipeline performance counters.

namespace AutoTranslator_Core
{
    // 這個類別負責 自動翻譯器效能 的主要流程與狀態。
    // EN: This class manages the main workflow and state for AutoTranslatorPerf.
    public static class AutoTranslatorPerf
    {
        // 這個欄位保存 activeApiRequests 的執行狀態或快取資料。
        // EN: This field stores active API requests runtime state or cached data.
        private static int _activeApiRequests = 0;
        // 這個欄位保存 totalApiRequests 的執行狀態或快取資料。
        // EN: This field stores total API requests runtime state or cached data.
        private static long _totalApiRequests = 0;
        // 這個欄位保存 successfulApiRequests 的執行狀態或快取資料。
        // EN: This field stores successful API requests runtime state or cached data.
        private static long _successfulApiRequests = 0;
        // 這個欄位保存 totalApiMs 的執行狀態或快取資料。
        // EN: This field stores total API ms runtime state or cached data.
        private static long _totalApiMs = 0;
        // 這個欄位保存 lastApiMs 的執行狀態或快取資料。
        // EN: This field stores last API ms runtime state or cached data.
        private static long _lastApiMs = 0;
        // 這個欄位保存 last記憶體DropMs 的執行狀態或快取資料。
        // EN: This field stores last memory drop ms runtime state or cached data.
        private static long _lastMemoryDropMs = 0;
        // 這個欄位保存 last記憶體DropKeyed 的執行狀態或快取資料。
        // EN: This field stores last memory drop Keyed runtime state or cached data.
        private static int _lastMemoryDropKeyed = 0;
        // 這個欄位保存 last記憶體DropDefs 的執行狀態或快取資料。
        // EN: This field stores last memory drop defs runtime state or cached data.
        private static int _lastMemoryDropDefs = 0;

        // 這個方法負責處理 BeginApiRequest 相關流程。
        // EN: This method handles begin API request.
        public static void BeginApiRequest()
        {
            System.Threading.Interlocked.Increment(ref _activeApiRequests);
        }

        // 這個方法負責處理 EndApiRequest 相關流程。
        // EN: This method handles end API request.
        public static void EndApiRequest(long elapsedMs, bool success)
        {
            System.Threading.Interlocked.Decrement(ref _activeApiRequests);
            System.Threading.Interlocked.Increment(ref _totalApiRequests);
            if (success) System.Threading.Interlocked.Increment(ref _successfulApiRequests);
            System.Threading.Interlocked.Add(ref _totalApiMs, Math.Max(0, elapsedMs));
            System.Threading.Interlocked.Exchange(ref _lastApiMs, Math.Max(0, elapsedMs));
        }

        // 這個方法負責處理 Record記憶體Drop 相關流程。
        // EN: This method handles record memory drop.
        public static void RecordMemoryDrop(long elapsedMs, int keyed, int defs)
        {
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropMs, Math.Max(0, elapsedMs));
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropKeyed, Math.Max(0, keyed));
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropDefs, Math.Max(0, defs));
        }

        // 這個屬性提供 ActiveApiRequests 的讀寫或計算結果。
        // EN: This property exposes active API requests.
        public static int ActiveApiRequests => Math.Max(0, System.Threading.Volatile.Read(ref _activeApiRequests));
        // 這個屬性提供 TotalApiRequests 的讀寫或計算結果。
        // EN: This property exposes total API requests.
        public static long TotalApiRequests => System.Threading.Volatile.Read(ref _totalApiRequests);
        // 這個屬性提供 SuccessfulApiRequests 的讀寫或計算結果。
        // EN: This property exposes successful API requests.
        public static long SuccessfulApiRequests => System.Threading.Volatile.Read(ref _successfulApiRequests);
        // 這個屬性提供 LastApiMs 的讀寫或計算結果。
        // EN: This property exposes last API ms.
        public static long LastApiMs => System.Threading.Volatile.Read(ref _lastApiMs);
        // 這個屬性提供 Last記憶體DropMs 的讀寫或計算結果。
        // EN: This property exposes last memory drop ms.
        public static long LastMemoryDropMs => System.Threading.Volatile.Read(ref _lastMemoryDropMs);
        // 這個屬性提供 Last記憶體DropKeyed 的讀寫或計算結果。
        // EN: This property exposes last memory drop Keyed.
        public static int LastMemoryDropKeyed => System.Threading.Volatile.Read(ref _lastMemoryDropKeyed);
        // 這個屬性提供 Last記憶體DropDefs 的讀寫或計算結果。
        // EN: This property exposes last memory drop defs.
        public static int LastMemoryDropDefs => System.Threading.Volatile.Read(ref _lastMemoryDropDefs);

        public static long AverageApiMs
        {
            get
            {
                long total = TotalApiRequests;
                return total <= 0 ? 0 : System.Threading.Volatile.Read(ref _totalApiMs) / total;
            }
        }
    }
}
