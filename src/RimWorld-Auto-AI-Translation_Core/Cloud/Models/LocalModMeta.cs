using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public class LocalModMeta
    {
        public string OriginalRecordId { get; set; }
        public string TargetModVersion { get; set; }
        public DateTime TranslationDate { get; set; }
        public bool IsSmartMerged { get; set; }
        public int MergedAiCount { get; set; }
    }
}
