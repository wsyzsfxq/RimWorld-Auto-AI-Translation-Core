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


    public static partial class AutoTranslatorCloudClient
    {
        public const string PrimaryApiBaseUrl = "https://api.anln666-nas.xyz/api/v1";
        public const string BackupApiBaseUrl = "https://cn-api.anln666-nas.xyz/api/v1";
        public static string CloudApiBaseUrl = PrimaryApiBaseUrl;

        public static readonly HttpClient cloudClient = new HttpClient()
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan
        };

        static AutoTranslatorCloudClient()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            cloudClient.DefaultRequestHeaders.Add("User-Agent", "RimWorld-ATC-CloudClient/5.0");
            cloudClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            cloudClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            cloudClient.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
        }
        private static void LogCloudTranslatedWarning(string key)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Warning("[ATC Cloud] " + key.Translate()));
        }
    }
        // Cloud client feature methods are split into partial files in Cloud/AutoTranslatorCloudClient.*.cs.
}
