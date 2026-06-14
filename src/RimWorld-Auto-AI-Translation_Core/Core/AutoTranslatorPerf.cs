using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static class AutoTranslatorPerf
    {
        private static int _activeApiRequests = 0;
        private static long _totalApiRequests = 0;
        private static long _successfulApiRequests = 0;
        private static long _totalApiMs = 0;
        private static long _lastApiMs = 0;
        private static long _lastMemoryDropMs = 0;
        private static int _lastMemoryDropKeyed = 0;
        private static int _lastMemoryDropDefs = 0;

        public static void BeginApiRequest()
        {
            System.Threading.Interlocked.Increment(ref _activeApiRequests);
        }

        public static void EndApiRequest(long elapsedMs, bool success)
        {
            System.Threading.Interlocked.Decrement(ref _activeApiRequests);
            System.Threading.Interlocked.Increment(ref _totalApiRequests);
            if (success) System.Threading.Interlocked.Increment(ref _successfulApiRequests);
            System.Threading.Interlocked.Add(ref _totalApiMs, Math.Max(0, elapsedMs));
            System.Threading.Interlocked.Exchange(ref _lastApiMs, Math.Max(0, elapsedMs));
        }

        public static void RecordMemoryDrop(long elapsedMs, int keyed, int defs)
        {
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropMs, Math.Max(0, elapsedMs));
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropKeyed, Math.Max(0, keyed));
            System.Threading.Interlocked.Exchange(ref _lastMemoryDropDefs, Math.Max(0, defs));
        }

        public static int ActiveApiRequests => Math.Max(0, System.Threading.Volatile.Read(ref _activeApiRequests));
        public static long TotalApiRequests => System.Threading.Volatile.Read(ref _totalApiRequests);
        public static long SuccessfulApiRequests => System.Threading.Volatile.Read(ref _successfulApiRequests);
        public static long LastApiMs => System.Threading.Volatile.Read(ref _lastApiMs);
        public static long LastMemoryDropMs => System.Threading.Volatile.Read(ref _lastMemoryDropMs);
        public static int LastMemoryDropKeyed => System.Threading.Volatile.Read(ref _lastMemoryDropKeyed);
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
