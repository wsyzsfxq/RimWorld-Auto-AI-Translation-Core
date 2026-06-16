using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個檔案保存導出條款版本號。
// EN: This file stores the export EULA version.

namespace AutoTranslator_Core
{


    // 這個類別負責 導出Eula版本 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ExportEulaVersion.
    public static class ExportEulaVersion
    {
        // 這個常數定義 Current版本 的固定值。
        // EN: This constant defines the fixed value for current version.
        public const string CurrentVersion = "1.0";
    }
}
