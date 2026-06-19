using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個資料模型保存本機模組的翻譯統計與快照資訊。
// EN: This model stores local mod translation statistics and snapshots.

namespace AutoTranslator_Core
{
    // 這個類別負責 Local模組Meta 的主要流程與狀態。
    // EN: This class manages the main workflow and state for LocalModMeta.
    public class LocalModMeta
    {
        // 這個屬性提供 OriginalRecordId 的讀寫或計算結果。
        // EN: This property exposes original record id.
        public string OriginalRecordId { get; set; }
        // 這個屬性提供 目標模組版本 的讀寫或計算結果。
        // EN: This property exposes target mod version.
        public string TargetModVersion { get; set; }
        // 這個屬性提供 翻譯Date 的讀寫或計算結果。
        // EN: This property exposes translation date.
        public DateTime TranslationDate { get; set; }
        // 這個屬性提供 IsSmartMerged 的讀寫或計算結果。
        // EN: This property exposes is smart merged.
        public bool IsSmartMerged { get; set; }
        // 這個屬性提供 MergedAiCount 的讀寫或計算結果。
        // EN: This property exposes merged ai count.
        public int MergedAiCount { get; set; }
    }
}
