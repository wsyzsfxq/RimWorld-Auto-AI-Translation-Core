using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個資料模型保存可導出模組的統計資訊。
// EN: This model stores exportable mod statistics.

namespace AutoTranslator_Core
{


    // 這個類別負責 Exportable模組資訊 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ExportableModInfo.
    public class ExportableModInfo
    {
        // 這個欄位保存 模組名稱 的執行狀態或快取資料。
        // EN: This field stores mod name runtime state or cached data.
        public string ModName;
        // 這個欄位保存 PackageId 的執行狀態或快取資料。
        // EN: This field stores package id runtime state or cached data.
        public string PackageId;
        // 這個欄位保存 PackageIdWithUnderscore 的執行狀態或快取資料。
        // EN: This field stores package id with underscore runtime state or cached data.
        public string PackageIdWithUnderscore;
        // 這個欄位保存 模組RootDir 的執行狀態或快取資料。
        // EN: This field stores mod root dir runtime state or cached data.
        public string ModRootDir;
        // 這個欄位保存 DefInjectedCount 的執行狀態或快取資料。
        // EN: This field stores Def Injected count runtime state or cached data.
        public int DefInjectedCount;
        // 這個欄位保存 KeyedCount 的執行狀態或快取資料。
        // EN: This field stores Keyed count runtime state or cached data.
        public int KeyedCount;
    }
}
