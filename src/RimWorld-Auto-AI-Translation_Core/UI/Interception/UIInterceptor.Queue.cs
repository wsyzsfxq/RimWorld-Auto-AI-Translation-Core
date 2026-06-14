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

namespace AutoTranslator_Core
{
    public static partial class UIInterceptor
    {

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


        // 🌟 把發現的野生生字丟進排隊區
        public static void QueueForTranslation(string text)
        {
            if (!AutoTranslatorMod.Settings.EnableUINewTranslation) return;
            if (System.Threading.Volatile.Read(ref _queuedApproxCount) >= MaxQueuedTranslations) return;

            if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return;
            if (!ShouldInterceptText(text))
            {
                return;
            }

            // 🚀 超高速攔截：如果在黑名單裡，看都不看直接踢掉！
            if (IsIgnored(text)) return;

            // 已經在排隊了也踢掉
            string lookupText = GetTranslationLookupText(text);
            string cacheKey = BuildCacheKey(lookupText);
            if (PendingTranslations.ContainsKey(cacheKey)) return;

            // 如果純數字或連個字母都沒有，拉黑！
            if (lookupText.All(char.IsDigit) || !LetterRegex.IsMatch(lookupText))
            {
                RememberIgnored(text);
                return;
            }

            var targetLang = AutoTranslatorMod.Settings.TargetLang;
            bool isForeignText = false;

            // 🔍 使用預先編譯的超高速正則表達式！
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

            // 🛑 如果判斷這不是外語生肉，加進黑名單！這輩子都別再浪費效能檢查它了！
            if (!isForeignText)
            {
                RememberIgnored(text);
                return;
            }

            if (!TryConsumeQueueBudget()) return;

            if (PendingTranslations.TryAdd(cacheKey, true))
            {
                TranslationQueue.Enqueue(lookupText);
                System.Threading.Interlocked.Increment(ref _queuedApproxCount);
            }
        }


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
                    // 正常退出
                    break;
                }
                catch (Exception ex)
                {
                    // 記錄錯誤，不再默默吞掉
                    Log.Warning($"[AutoTranslationCore] BackgroundTranslationWorker error: {ex.Message}");
                    // 出錯後延長等待，避免狂噴
                    try { await Task.Delay(5000, token); } catch (TaskCanceledException) { break; }
                }
            }
        }

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
