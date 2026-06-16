using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個資料模型保存導出冷卻狀態。
// EN: This model stores export cooldown state.

namespace AutoTranslator_Core
{


    // 這個類別負責 冷卻狀態 的主要流程與狀態。
    // EN: This class manages the main workflow and state for CooldownState.
    public class CooldownState
    {
        // 這個欄位保存 Can導出 的執行狀態或快取資料。
        // EN: This field stores can export runtime state or cached data.
        public bool CanExport;
        // 這個欄位保存 RemainingSeconds 的執行狀態或快取資料。
        // EN: This field stores remaining seconds runtime state or cached data.
        public int RemainingSeconds;
        // 這個欄位保存 Next冷卻If導出Now 的執行狀態或快取資料。
        // EN: This field stores next cooldown if export now runtime state or cached data.
        public int NextCooldownIfExportNow;
        // 這個欄位保存 Recent導出Count 的執行狀態或快取資料。
        // EN: This field stores recent export count runtime state or cached data.
        public int RecentExportCount;
        // 這個欄位保存 TodayCount 的執行狀態或快取資料。
        // EN: This field stores today count runtime state or cached data.
        public int TodayCount;
        // 這個欄位保存 DailyLimitReached 的執行狀態或快取資料。
        // EN: This field stores daily limit reached runtime state or cached data.
        public bool DailyLimitReached;

        // 這個方法負責取得 DisplayText 資料。
        // EN: This method gets display text.
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
