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
    public class CloudModRecord
    {
        public string RecordId { get; set; }
        public string PackageId { get; set; }
        public string Language { get; set; }
        public string ModName { get; set; }
        public string LatestVersion { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime ModLastUpdated { get; set; }
        public string UploaderID { get; set; }
        public string Author { get; set; }
        public string TranslationType { get; set; }
        public bool IsVerified { get; set; }
        public string FileUrl { get; set; }
        public string TargetModVersion { get; set; }
        public DateTime TranslationDate { get; set; }
        public bool IsSmartMerged { get; set; }
        public int MergedAiCount { get; set; }
        public string UpdateLog { get; set; }
    }
}
