using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個檔案負責導出頻率限制與冷卻計算。
// EN: This file calculates export limits and cooldowns.

namespace AutoTranslator_Core
{


    // 這個類別負責 導出冷卻管理器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ExportCooldownManager.
    public static class ExportCooldownManager
    {


        // 這個欄位保存 冷卻Schedule 的執行狀態或快取資料。
        // EN: This field stores cooldown schedule runtime state or cached data.
        private static readonly int[] CooldownSchedule = new[]
        {
        0,
        0,
        30,
        60,
        180,
        300
    };


        // 這個常數定義 RECENTWINDOWHOURS 的固定值。
        // EN: This constant defines the fixed value for recent window hours.
        private const double RECENT_WINDOW_HOURS = 1.0;


        // 這個常數定義 DAILYLIMIT 的固定值。
        // EN: This constant defines the fixed value for daily limit.
        public const int DAILY_LIMIT = 100;


        // 這個常數定義 PEREXPORTMODLIMIT 的固定值。
        // EN: This constant defines the fixed value for per export mod limit.
        public const int PER_EXPORT_MOD_LIMIT = 10;


        // 這個方法負責取得 Current狀態 資料。
        // EN: This method gets current state.
        public static CooldownState GetCurrentState()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;


            string today = now.ToString("yyyy-MM-dd");
            if (settings.TodayExportDate != today)
            {
                settings.TodayExportCount = 0;
                settings.TodayExportDate = today;
            }


            var cutoff = now.AddHours(-24);
            settings.ExportHistory.RemoveAll(s =>
            {
                if (!DateTime.TryParse(s, out DateTime t)) return true;
                return t < cutoff;
            });


            var recentExports = new List<DateTime>();
            foreach (var s in settings.ExportHistory)
            {
                if (DateTime.TryParse(s, out DateTime t))
                {
                    if ((now - t).TotalHours < RECENT_WINDOW_HOURS)
                        recentExports.Add(t);
                }
            }


            bool dailyReached = settings.TodayExportCount >= DAILY_LIMIT;


            int nextExportIndex = recentExports.Count;
            int cooldownSecondsForNext = GetCooldownForIndex(nextExportIndex);


            int cooldownSecondsForAfter = GetCooldownForIndex(nextExportIndex + 1);


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


        // 這個方法負責處理 Record導出 相關流程。
        // EN: This method handles record export.
        public static void RecordExport()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;


            settings.ExportHistory.Add(now.ToString("o"));


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


            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();


            int recentCount = CountRecentExports();
            int nextCooldown = GetCooldownForIndex(recentCount);
            if (nextCooldown > 0)
            {
                AutoTranslatorSettings.AddLog(
                    "ATC_Log_CooldownTriggered".Translate(recentCount, nextCooldown));
            }
        }

        // 這個方法負責處理 CountRecentExports 相關流程。
        // EN: This method handles count recent exports.
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

        // 這個方法負責取得 冷卻ForIndex 資料。
        // EN: This method gets cooldown for index.
        private static int GetCooldownForIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= CooldownSchedule.Length)
                return CooldownSchedule[CooldownSchedule.Length - 1];
            return CooldownSchedule[index];
        }
    }
}
