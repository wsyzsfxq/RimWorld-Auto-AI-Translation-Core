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
    /// 描述一個可導出的模組
    /// </summary>
    public class ExportableModInfo
    {
        public string ModName;
        public string PackageId;
        public string PackageIdWithUnderscore;
        public string ModRootDir;
        public int DefInjectedCount;
        public int KeyedCount;
    }
}
