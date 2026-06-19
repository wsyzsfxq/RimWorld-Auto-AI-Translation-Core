using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案定義可用的目標語言列舉。
// EN: This file defines the supported target languages.

namespace AutoTranslator_Core
{
    // 這個列舉定義 目標語言 可使用的固定選項。
    // EN: This enum defines the available target language options.
    public enum TargetLanguage { Traditional, Simplified, Japanese, Korean, Russian, Ukrainian, English, French, German, Spanish, Italian, Polish, Portuguese, Turkish }
}
