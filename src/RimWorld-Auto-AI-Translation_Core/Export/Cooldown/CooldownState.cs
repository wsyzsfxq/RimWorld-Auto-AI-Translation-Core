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
    /// 冷卻狀態快照
    /// </summary>
    public class CooldownState
    {
        public bool CanExport;
        public int RemainingSeconds;
        public int NextCooldownIfExportNow;  // 如果現在導出，下次會冷卻多久（秒）
        public int RecentExportCount;          // 1 小時內的導出次數
        public int TodayCount;                  // 今日導出次數
        public bool DailyLimitReached;          // 是否達到每日上限

        public string GetDisplayText()
        {
            if (DailyLimitReached)
            {
                return "ATC_Export_TooManyToday".Translate(TodayCount);
            }
            if (CanExport)
            {
                if (NextCooldownIfExportNow == 0)
                    return "ATC_Export_CooldownReady_Free".Translate();
                return "ATC_Export_CooldownReady_NextWillCooldown".Translate(NextCooldownIfExportNow);
            }
            return "ATC_Export_CooldownWait".Translate(RemainingSeconds);
        }
    }
}
