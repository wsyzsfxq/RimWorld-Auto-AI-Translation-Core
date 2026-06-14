using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace AutoTranslator_Core
{
    /// <summary>
    /// 智能分層冷卻管理器
    ///
    /// 設計原則：
    /// - 合法使用（重試、對比）不應觸發冷卻 → 前 2 次免冷卻
    /// - 濫用行為（連續批量導出）會逐步加重冷卻
    /// - 距離上次導出 1 小時後計數重置 → 偶爾使用不累積負擔
    /// </summary>
    public static class ExportCooldownManager
    {
        /// <summary>
        /// 冷卻時間表：索引 = 即將執行的「1 小時內第 N 次」導出
        /// 例如：index=0 表示這是 1 小時內第 1 次導出，免冷卻
        /// </summary>
        private static readonly int[] CooldownSchedule = new[]
        {
        0,    // 第 1 次：免冷卻
        0,    // 第 2 次：免冷卻
        30,   // 第 3 次：30 秒
        60,   // 第 4 次：1 分鐘
        180,  // 第 5 次：3 分鐘
        300   // 第 6 次以後：5 分鐘
    };

        /// <summary>1 小時內的紀錄才算「最近」</summary>
        private const double RECENT_WINDOW_HOURS = 1.0;

        /// <summary>每日導出上限</summary>
        public const int DAILY_LIMIT = 100;

        /// <summary>單次最多導出模組數</summary>
        public const int PER_EXPORT_MOD_LIMIT = 10;

        /// <summary>
        /// 計算當前冷卻狀態
        /// </summary>
        public static CooldownState GetCurrentState()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;

            // 1. 檢查每日重置
            string today = now.ToString("yyyy-MM-dd");
            if (settings.TodayExportDate != today)
            {
                settings.TodayExportCount = 0;
                settings.TodayExportDate = today;
            }

            // 2. 清理過時紀錄（超過 24 小時）
            var cutoff = now.AddHours(-24);
            settings.ExportHistory.RemoveAll(s =>
            {
                if (!DateTime.TryParse(s, out DateTime t)) return true;  // 損壞紀錄一併清掉
                return t < cutoff;
            });

            // 3. 篩選 1 小時內的紀錄
            var recentExports = new List<DateTime>();
            foreach (var s in settings.ExportHistory)
            {
                if (DateTime.TryParse(s, out DateTime t))
                {
                    if ((now - t).TotalHours < RECENT_WINDOW_HOURS)
                        recentExports.Add(t);
                }
            }

            // 4. 檢查每日上限
            bool dailyReached = settings.TodayExportCount >= DAILY_LIMIT;

            // 5. 計算「下次導出」需要的冷卻
            int nextExportIndex = recentExports.Count;  // 即將是 1 小時內第 (N+1) 次
            int cooldownSecondsForNext = GetCooldownForIndex(nextExportIndex);

            // 6. 計算「再下一次」需要的冷卻（給 UI 顯示用）
            int cooldownSecondsForAfter = GetCooldownForIndex(nextExportIndex + 1);

            // 7. 判定能否立刻導出
            if (dailyReached)
            {
                return new CooldownState
                {
                    CanExport = false,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = 0,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = true
                };
            }

            if (cooldownSecondsForNext == 0 || recentExports.Count == 0)
            {
                // 免冷卻
                return new CooldownState
                {
                    CanExport = true,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = cooldownSecondsForAfter,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }

            // 需要冷卻：檢查距離上次導出多久
            DateTime lastExport = recentExports.Max();
            double elapsed = (now - lastExport).TotalSeconds;

            if (elapsed >= cooldownSecondsForNext)
            {
                return new CooldownState
                {
                    CanExport = true,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = cooldownSecondsForAfter,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }
            else
            {
                return new CooldownState
                {
                    CanExport = false,
                    RemainingSeconds = (int)Math.Ceiling(cooldownSecondsForNext - elapsed),
                    NextCooldownIfExportNow = cooldownSecondsForNext,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }
        }

        /// <summary>
        /// 紀錄一次導出（必須在實際導出成功後呼叫）
        /// </summary>
        public static void RecordExport()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;

            // 寫入歷史
            settings.ExportHistory.Add(now.ToString("o"));

            // 更新今日計數
            string today = now.ToString("yyyy-MM-dd");
            if (settings.TodayExportDate != today)
            {
                settings.TodayExportDate = today;
                settings.TodayExportCount = 1;
            }
            else
            {
                settings.TodayExportCount++;
            }

            // 持久化
            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

            // 日誌：計算下次冷卻
            int recentCount = CountRecentExports();
            int nextCooldown = GetCooldownForIndex(recentCount);
            if (nextCooldown > 0)
            {
                AutoTranslatorSettings.AddLog(
                    "ATC_Log_CooldownTriggered".Translate(recentCount, nextCooldown));
            }
        }

        private static int CountRecentExports()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;
            int count = 0;
            foreach (var s in settings.ExportHistory)
            {
                if (DateTime.TryParse(s, out DateTime t))
                {
                    if ((now - t).TotalHours < RECENT_WINDOW_HOURS) count++;
                }
            }
            return count;
        }

        private static int GetCooldownForIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= CooldownSchedule.Length)
                return CooldownSchedule[CooldownSchedule.Length - 1];
            return CooldownSchedule[index];
        }
    }
}
