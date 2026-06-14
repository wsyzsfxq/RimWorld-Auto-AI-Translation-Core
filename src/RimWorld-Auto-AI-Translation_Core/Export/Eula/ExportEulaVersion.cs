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
    /// EULA 版本控制：條款有重大修改時遞增此常數
    /// 玩家如果同意的是舊版本，會被強制重新閱讀新版
    /// </summary>
    public static class ExportEulaVersion
    {
        public const string CurrentVersion = "1.0";
    }
}
