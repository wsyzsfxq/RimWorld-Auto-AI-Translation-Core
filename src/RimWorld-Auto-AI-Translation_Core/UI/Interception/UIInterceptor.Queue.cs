using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責背景攔截佇列與過濾條件。
// EN: This file manages the background UI interception queue and filters.

namespace AutoTranslator_Core
{
    // 這個類別負責 UIInterceptor 的主要流程與狀態。
    // EN: This class manages the main workflow and state for UIInterceptor.
    public static partial class UIInterceptor
    {

        // 這個方法負責嘗試執行 Consume佇列Budget 並回報是否成功。
        // EN: This method tries to consume queue budget and reports whether it succeeded.
        private static bool TryConsumeQueueBudget()
        {
            int frame = Time.frameCount;
            lock (_frameBudgetLock)
            {
                if (_lastQueueFrame != frame)
                {
                    _lastQueueFrame = frame;
                    _queuedThisFrame = 0;
                }

                if (_queuedThisFrame >= MaxNewQueueItemsPerFrame) return false;
                _queuedThisFrame++;
                return true;
            }
        }

        private static bool TryConsumeNewTranslationScanBudget()
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            lock (_newTranslationScanLock)
            {
                if (nowTicks >= _nextNewTranslationScanTicks)
                {
                    _nextNewTranslationScanTicks = nowTicks + NewTranslationScanInterval.Ticks;
                    _queuedThisScanWindow = 0;
                }

                if (_queuedThisScanWindow >= MaxNewQueueItemsPerScanWindow) return false;
                _queuedThisScanWindow++;
                return true;
            }
        }


        // 這個方法負責排入 For翻譯 佇列。
        // EN: This method queues for translation.
        public static void QueueForTranslation(string text)
        {
            if (!AutoTranslatorMod.Settings.EnableUINewTranslation) return;
            if (System.Threading.Volatile.Read(ref _queuedApproxCount) >= MaxQueuedTranslations) return;

            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;
            if (!ShouldInterceptText(text))
            {
                return;
            }


            if (IsIgnored(text)) return;


            string lookupText = GetTranslationLookupText(text);
            string cacheKey = BuildCacheKey(lookupText);
            if (PendingTranslations.ContainsKey(cacheKey)) return;


            if (lookupText.All(char.IsDigit) || !LetterRegex.IsMatch(lookupText))
            {
                RememberIgnored(text);
                return;
            }

            var targetLang = AutoTranslatorMod.Settings.TargetLang;
            bool isForeignText = false;


            bool hasEnglish = EnglishRegex.IsMatch(lookupText);
            bool hasCyrillic = CyrillicRegex.IsMatch(lookupText);
            bool hasKana = KanaRegex.IsMatch(lookupText);
            bool hasHangul = HangulRegex.IsMatch(lookupText);
            bool hasCJK = CJKRegex.IsMatch(lookupText);

            switch (targetLang)
            {
                case TargetLanguage.Traditional:
                case TargetLanguage.Simplified:
                    if (hasEnglish || hasCyrillic || hasKana || hasHangul) isForeignText = true;
                    break;
                case TargetLanguage.Japanese:
                    if (hasEnglish || hasCyrillic || hasHangul || (hasCJK && !hasKana)) isForeignText = true;
                    break;
                case TargetLanguage.Korean:
                    if (hasEnglish || hasCyrillic || hasKana || hasCJK) isForeignText = true;
                    break;
                case TargetLanguage.English:
                    if (hasCJK || hasKana || hasHangul || hasCyrillic) isForeignText = true;
                    break;
                case TargetLanguage.Russian:
                case TargetLanguage.Ukrainian:
                    if (hasEnglish || hasCJK || hasKana || hasHangul) isForeignText = true;
                    break;
                default:
                    if (hasEnglish || hasCJK) isForeignText = true;
                    break;
            }


            if (!isForeignText)
            {
                RememberIgnored(text);
                return;
            }

            if (!TryConsumeNewTranslationScanBudget()) return;
            if (!TryConsumeQueueBudget()) return;

            if (PendingTranslations.TryAdd(cacheKey, true))
            {
                TranslationQueue.Enqueue(lookupText);
                System.Threading.Interlocked.Increment(ref _queuedApproxCount);
            }
        }


        // 這個方法負責處理 Background翻譯Worker 相關流程。
        // EN: This method handles background translation worker.
        private static async Task BackgroundTranslationWorker()
        {
            var token = _workerCts.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token);
                    if (!AutoTranslatorMod.Settings.EnableUINewTranslation)
                    {
                        DiscardQueuedTranslations();
                        SaveCacheIfDue();
                        continue;
                    }

                    if (TranslationQueue.Count > 0 && AutoTranslatorAPI.HasAnyReadyConfig())
                    {
                        List<string> batch = new List<string>();
                        while (batch.Count < 20 && TranslationQueue.TryDequeue(out string text))
                        {
                            System.Threading.Interlocked.Decrement(ref _queuedApproxCount);
                            batch.Add(text);
                        }
                        if (batch.Count > 0)
                        {
                            var translatedBatch = await AutoTranslatorAPI.TranslateBatchAsync(batch);
                            if (translatedBatch != null && translatedBatch.Count == batch.Count)
                            {
                                bool hasNewCache = false;
                                for (int i = 0; i < batch.Count; i++)
                                {
                                    string original = batch[i];
                                    string originalCacheKey = BuildCacheKey(original);
                                    string translated = SanitizeUITranslationResult(original, translatedBatch[i]);
                                    if (!string.IsNullOrEmpty(translated) && original != translated)
                                    {
                                        Cache[originalCacheKey] = translated;
                                        hasNewCache = true;
                                    }
                                    else
                                    {
                                        RememberIgnored(original);
                                    }
                                    PendingTranslations.TryRemove(originalCacheKey, out _);
                                }
                                if (hasNewCache) _cacheDirty = true;
                                SaveCacheIfDue(hasNewCache);
                            }
                            else
                            {
                                foreach (var t in batch) PendingTranslations.TryRemove(BuildCacheKey(t), out _);
                            }
                        }
                    }
                    SaveCacheIfDue();
                }
                catch (TaskCanceledException)
                {

                    break;
                }
                catch (Exception ex)
                {

                    Log.Warning($"[AutoTranslationCore] BackgroundTranslationWorker error: {ex.Message}");

                    try { await Task.Delay(5000, token); } catch (TaskCanceledException) { break; }
                }
            }
        }

        // 這個方法負責處理 DiscardQueuedTranslations 相關流程。
        // EN: This method handles discard queued translations.
        private static void DiscardQueuedTranslations()
        {
            while (TranslationQueue.TryDequeue(out string text))
            {
                System.Threading.Interlocked.Decrement(ref _queuedApproxCount);
                PendingTranslations.TryRemove(BuildCacheKey(text), out _);
            }

            if (System.Threading.Volatile.Read(ref _queuedApproxCount) < 0)
            {
                System.Threading.Interlocked.Exchange(ref _queuedApproxCount, 0);
            }
        }
    }
}
