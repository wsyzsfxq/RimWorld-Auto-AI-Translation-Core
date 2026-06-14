using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public class ApiKeyConfig : IExposable
    {
        public TranslatorProvider Provider = TranslatorProvider.Google;
        public string Label = "";
        public bool Enabled = true;
        public string Key = "";
        public string CustomBaseUrl = "";
        public string SelectedModel = "";

        public List<string> FetchedModels = new List<string>();

        [NonSerialized] public bool IsFetching = false;
        [NonSerialized] public string lastFetchedKey = "";
        [NonSerialized] public string PendingFetchFingerprint = "";
        [NonSerialized] public int FetchRetryCount = 0;
        [NonSerialized] public long NextModelFetchRetryUtcTicks = 0L;
        [NonSerialized] public long FetchStartedUtcTicks = 0L;
        [NonSerialized] public int FetchGeneration = 0;

        // 🌟 新增這個變數：用來控制 UI 顯示「測試中⏳」
        [NonSerialized] public bool IsTesting = false;
        [NonSerialized] public long TestStartedUtcTicks = 0L;
        [NonSerialized] public int TestGeneration = 0;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Provider, "Provider", TranslatorProvider.Google);
            Scribe_Values.Look(ref Label, "Label", "");
            Scribe_Values.Look(ref Enabled, "Enabled", true);
            Scribe_Values.Look(ref Key, "Key", "");
            Scribe_Values.Look(ref CustomBaseUrl, "CustomBaseUrl", "");
            Scribe_Values.Look(ref SelectedModel, "SelectedModel", "");
            Scribe_Collections.Look(ref FetchedModels, "FetchedModels", LookMode.Value);

            if (FetchedModels == null) FetchedModels = new List<string>();
        }
    }
}
