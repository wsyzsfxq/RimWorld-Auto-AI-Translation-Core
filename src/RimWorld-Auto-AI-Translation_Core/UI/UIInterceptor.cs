using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    [StaticConstructorOnStartup]
    public static partial class UIInterceptor
    {
        public static ConcurrentDictionary<string, string> Cache = new ConcurrentDictionary<string, string>();
        public static ConcurrentDictionary<string, bool> IgnoredCache = new ConcurrentDictionary<string, bool>();
        private static ConcurrentQueue<string> TranslationQueue = new ConcurrentQueue<string>();
        private static ConcurrentDictionary<string, bool> PendingTranslations = new ConcurrentDictionary<string, bool>();
        private static readonly string CacheFilePath;
        private static readonly string IgnoredCacheFilePath;
        private const int MaxQueuedTranslations = 500;
        private const int MaxNewQueueItemsPerFrame = 6;
        private const int MaxIgnoredCacheSize = 20000;
        private static int _queuedApproxCount = 0;
        private static int _lastQueueFrame = -1;
        private static int _queuedThisFrame = 0;
        private static readonly object _frameBudgetLock = new object();
        private static volatile bool _cacheDirty = false;
        private static volatile bool _ignoredCacheDirty = false;
        private static long _lastCacheSaveTicks = 0L;

        private static readonly Regex LetterRegex = new Regex(@"\p{L}", RegexOptions.Compiled);
        private static readonly Regex EnglishRegex = new Regex(@"[a-zA-Z]", RegexOptions.Compiled);
        private static readonly Regex CyrillicRegex = new Regex(@"\p{IsCyrillic}", RegexOptions.Compiled);
        private static readonly Regex KanaRegex = new Regex(@"\p{IsHiragana}|\p{IsKatakana}", RegexOptions.Compiled);
        private static readonly Regex HangulRegex = new Regex(@"\p{IsHangulSyllables}", RegexOptions.Compiled);
        private static readonly Regex CJKRegex = new Regex(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.Compiled);
        private static readonly Regex DataKeyValueRegex = new Regex(@"^\s*(?:""[^""]{1,64}""|'[^']{1,64}'|text|translation|translated|result|value)\s*[:=]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LogTimestampRegex = new Regex(@"^(?<prefix>\s*(?:\(\d+\)\s*)?\[\d{1,2}:\d{2}:\d{2}\]\s*)(?<body>[\s\S]*)$", RegexOptions.Compiled);
        private static readonly Regex VolatileMetricRegex = new Regex(@"^\s*(?:FPS|TPS|帧率|幀率)\s*[:：]\s*\d+(?:\s*[\(（]\d+[\)）])?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TemperatureReadoutRegex = new Regex(@"^\s*(?:(?:Indoor|Indoors|Outdoor|Outdoors|Inside|Outside|Room|室內|室内|室外|戶外|户外|屋內|屋内|外面|内部|外部)\s+)?[-+]?\d+(?:\.\d+)?\s*(?:°\s*)?[CF]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StackCountRegex = new Regex(@"^\s*[\p{L}\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsCyrillic}\s'\-·・]+[xX×]\s*\d{1,6}\s*$", RegexOptions.Compiled);
        private static readonly Regex InternalKeyRegex = new Regex(@"^\s*[A-Za-z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+(?:Label|Def|Path|Worker|Job|Recipe|Tool|Group|Tab|Menu|Key)?\s*$|^\s*[A-Za-z][A-Za-z0-9]+(?:Label|Def|Path|Worker|Job|Recipe|Tool|Group|Tab|Menu|Key)\s*$", RegexOptions.Compiled);
        private static readonly Regex NumericStatusRegex = new Regex(@"(?:\d+(?:\.\d+)?\s*[%/]|[=/|]\s*\d+(?:\.\d+)?|\d+(?:\.\d+)?\s*(?:h|mm|cm|kg|g|W|kW|XP)\b)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static UIInterceptor()
        {
            CacheFilePath = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "UI_Hardcoded_Cache.json");
            IgnoredCacheFilePath = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "UI_Hardcoded_Ignored.json");
            AutoTranslatorMod.TryAutoSyncLanguageWithGame(resetCaches: false, log: false, writeSettings: true);
            LoadCache();
            LoadIgnoredCache();

            AutoTranslatorScanner.RunAdvancedDetoxScanner();
            AutoTranslatorScanner.RunNewlineDetoxScanner();
            AutoTranslatorScanner.CleanupPatchModTwins();
            AutoTranslatorScanner.MemoryDrop_InjectNow();

            Task.Run(() => BackgroundTranslationWorker());

            Task.Run(async () =>
            {
                await Task.Delay(3000);
                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    AutoTranslatorMod.TryAutoSyncLanguageWithGame(resetCaches: true, log: false, writeSettings: true);
                });
            });

            Task.Run(async () =>
            {
                await Task.Delay(10000);

                if (AutoTranslatorMod.Settings.AutoTranslateOnUpdate)
                {
                    var updates = ModUpdateDetector.GetUpdatedOrNewModsCached();
                    if (updates.Count > 0)
                    {
                        AutoTranslatorSettings.AddLog("ATC_Log_AutoStartUpdateScan".Translate(updates.Count));
                        AutoTranslatorScanner.StartMultiScan(updates);
                    }
                }
            });

            var harmony = new Harmony("MingYang.AutoTranslation.UIInterceptor");
            harmony.PatchAll(typeof(UIInterceptor).Assembly);

            Log.Message("[AutoTranslationCore] " + "ATC_Log_UIInterceptorStarted".Translate());
        }

        private static readonly System.Threading.CancellationTokenSource _workerCts = new System.Threading.CancellationTokenSource();
    }
}
