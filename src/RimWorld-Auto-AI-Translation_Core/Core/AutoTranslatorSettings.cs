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
    public class AutoTranslatorSettings : ModSettings
    {
        public TargetLanguage TargetLang = TargetLanguage.Traditional;
        public bool HasManualTargetLanguage = false;
        public bool OnlyScanActiveMods = true;
        public int MaxThreads = 3;
        public List<ApiKeyConfig> ApiConfigs = new List<ApiKeyConfig>();

        public float CurrentProgress = 0f;
        public string CurrentTaskName = "";
        public float SubProgress = 0f;
        public string SubTaskName = "";
        public static bool ShouldAutoScroll = true;
        public static bool IsSkipCurrentRequested = false;

        public static float lastSettingsViewHeight = 1000f;
        public static bool ShowFinishPopup = false;
        public static Vector2 mainScrollPos = Vector2.zero;

        public static Vector2 logScrollPos = Vector2.zero;
        public static List<string> RuntimeLogs = new List<string>();

        public static Vector2 errorScrollPos = Vector2.zero;
        public static List<string> ErrorLogs = new List<string>();

        public static readonly object logLock = new object();

        public static bool IsCancellationRequested = false;
        public static bool IsRunning = false;

        public static void RequestPipelineCancellation()
        {
            IsCancellationRequested = true;
            IsSkipCurrentRequested = false;
            AutoTranslatorAPI.AbortActiveTranslationRequests("Pipeline cancellation requested");
        }

        public static void ResetPipelineCancellation()
        {
            IsCancellationRequested = false;
            IsSkipCurrentRequested = false;
        }

        public bool EnableUIInterceptor = false;
        public bool EnableUINewTranslation = true;
        public bool EnableUIErrorLogInterception = false;
        public bool ShowOriginalUI = false;
        public bool TranslateWorkbenchModNames = false;

        public long TotalCharCount = 0;
        [NonSerialized] public long SessionCharCount = 0;

        // 分頁控制 (不需存檔)
        [NonSerialized] public static int ActiveTab = 0;
        // ===== V3.0 雲端狀態暫存 (不需存檔) =====
        [NonSerialized] public static List<CloudModRecord> CloudRegistry = new List<CloudModRecord>();
        [NonSerialized] public static bool IsFetchingCloud = false;
        [NonSerialized] public static bool HasFetchedCloudThisSession = false;
        [NonSerialized] public static string CloudUploadTarget = ""; // 記錄目前正在上傳哪一個 PackageId
        [NonSerialized] public static string CloudSearchText = "";   // ✨ 新增：雲端介面的搜尋框文字
        // ✨ 新增：明確記錄連線是否失敗，防止 UI 說謊！
        [NonSerialized] public static bool CloudConnectionFailed = false;
        [NonSerialized] public static int CloudFetchGeneration = 0;
        [NonSerialized] public static long CloudFetchStartedUtcTicks = 0;
        public static Vector2 cloudScrollPos = Vector2.zero;
        // ===== V3.1 雲端使用者設定 =====
        public string CloudNickname = "野生大佬";
        public string CloudAdminToken = "";
        public string CloudUploadType = "AI_Auto"; // ✨ 新增：預設是 AI 機翻
        public string CloudBatchUploadLog = "";
        public TargetLanguage CloudTargetLang = TargetLanguage.Traditional; // ✨ 雲端專屬目標語言！
        // ✨ V3.0 新增：雲端多版本選取暫存 (不需要存檔，開機自動重整)
        [NonSerialized] public static Dictionary<string, CloudModRecord> SelectedCloudVersion = new Dictionary<string, CloudModRecord>(StringComparer.OrdinalIgnoreCase);
        // 新功能：檢測到更新是否直接全自動背景啟動翻譯
        public bool AutoTranslateOnUpdate = false;
        public int TimeoutSeconds = 60;
        // ✨ 增量翻譯開關與時間戳記憶體
        public bool AutoClearOldOnUpdate = true; // 預設開啟智能增量
        public Dictionary<string, long> ModLastVerifiedTimes = new Dictionary<string, long>();
        public Dictionary<string, string> ModLastVerifiedFingerprints = new Dictionary<string, string>();
        // ✨ 架構師新增：宣告一個公開靜態變數來記錄過濾數量
        public static int FilteredModsCount = 0;
        // ===== P3 新增：EULA 同意狀態 =====
        public bool HasAcceptedExportEula = false;
        public string EulaAcceptedTimestamp = "";   // ISO 8601 格式
        public string EulaAcceptedVersion = "";      // 同意的 EULA 版本
        public int EulaAcceptCount = 0;              // 累計同意次數

        // ===== P3 第二階段新增：導出冷卻與每日上限 =====
        public List<string> ExportHistory = new List<string>();  // ISO 8601 時間字串清單
        public string TodayExportDate = "";                       // 今日日期 yyyy-MM-dd
        public int TodayExportCount = 0;                          // 今日導出次數

        /// <summary>
        /// 判斷 EULA 同意是否仍有效（30 天內 且 版本一致）
        /// </summary>
        public bool IsEulaStillValid()
        {
            if (!HasAcceptedExportEula) return false;
            if (string.IsNullOrEmpty(EulaAcceptedTimestamp)) return false;
            if (EulaAcceptedVersion != ExportEulaVersion.CurrentVersion) return false;

            if (DateTime.TryParse(EulaAcceptedTimestamp, out DateTime accepted))
            {
                return (DateTime.Now - accepted).TotalDays < 30;
            }
            return false;
        }

        /// <summary>
        /// 取得 EULA 同意剩餘天數（負數表示已過期）
        /// </summary>
        public int GetEulaRemainingDays()
        {
            if (!DateTime.TryParse(EulaAcceptedTimestamp, out DateTime accepted)) return 0;
            return 30 - (int)(DateTime.Now - accepted).TotalDays;
        }

        public static void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            lock (logLock)
            {
                RuntimeLogs.Add(line);
                if (RuntimeLogs.Count > 500) RuntimeLogs.RemoveAt(0);
                WriteLogToFile(line);
            }
        }

        public static void AddErrorLog(string msg)
        {
            lock (logLock)
            {
                if (ErrorLogs.Any(x => x.Contains(msg))) return;
                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                ErrorLogs.Add(line);
                if (ErrorLogs.Count > 100) ErrorLogs.RemoveAt(0);
                errorScrollPos.y = 99999f;
                WriteLogToFile("[ERROR] " + line);
            }

            // 🌟 UX 革命：呼叫跨執行緒神器，保證 100% 在左上角彈出！
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                Verse.Messages.Message(msg, RimWorld.MessageTypeDefOf.RejectInput, false);
            });
        }
        private static void WriteLogToFile(string line)
        {
            try
            {
                string path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "AutoTranslation_Log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, line + "\n");
            }
            catch { }
        }

        public static void ClearLog()
        {
            lock (logLock)
            {
                RuntimeLogs.Clear();
                ErrorLogs.Clear();
                logScrollPos = Vector2.zero;
                errorScrollPos = Vector2.zero;
            }
            try
            {
                string path = Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "AutoTranslation_Log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, $"=== Auto Translation Core V4.9 Ultimate [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n\n");
            }
            catch { }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref TargetLang, "TargetLang", TargetLanguage.Traditional);
            Scribe_Values.Look(ref HasManualTargetLanguage, "HasManualTargetLanguage", false);
            Scribe_Values.Look(ref OnlyScanActiveMods, "OnlyScanActiveMods", true);
            Scribe_Values.Look(ref EnableUIInterceptor, "EnableUIInterceptor", false);
            Scribe_Values.Look(ref EnableUINewTranslation, "EnableUINewTranslation", true);
            Scribe_Values.Look(ref EnableUIErrorLogInterception, "EnableUIErrorLogInterception", false);
            Scribe_Values.Look(ref TranslateWorkbenchModNames, "TranslateWorkbenchModNames", false);
            Scribe_Values.Look(ref MaxThreads, "MaxThreads", 3);
            Scribe_Values.Look(ref ShowOriginalUI, "ShowOriginalUI", false);
            Scribe_Collections.Look(ref ApiConfigs, "ApiConfigs", LookMode.Deep);
            Scribe_Values.Look(ref TotalCharCount, "TotalCharCount", 0L);
            // ✨ 存檔掛載
            Scribe_Values.Look(ref AutoClearOldOnUpdate, "AutoClearOldOnUpdate", true);
            Scribe_Collections.Look(ref ModLastVerifiedTimes, "ModLastVerifiedTimes", LookMode.Value, LookMode.Value);
            if (ModLastVerifiedTimes == null) ModLastVerifiedTimes = new Dictionary<string, long>();
            Scribe_Collections.Look(ref ModLastVerifiedFingerprints, "ModLastVerifiedFingerprints", LookMode.Value, LookMode.Value);
            if (ModLastVerifiedFingerprints == null) ModLastVerifiedFingerprints = new Dictionary<string, string>();
            // 確保超時秒數能被存檔，預設值為 60 秒
            Scribe_Values.Look(ref TimeoutSeconds, "TimeoutSeconds", 60);
            // ===== V3.1 雲端使用者設定 =====
            Scribe_Values.Look(ref CloudNickname, "CloudNickname", "野生大佬");
            Scribe_Values.Look(ref CloudAdminToken, "CloudAdminToken", "");
            Scribe_Values.Look(ref CloudUploadType, "CloudUploadType", "AI_Auto"); // ✨ 新增這行存檔
            Scribe_Values.Look(ref CloudBatchUploadLog, "CloudBatchUploadLog", "");
            Scribe_Values.Look(ref CloudTargetLang, "CloudTargetLang", TargetLanguage.Traditional); // ✨ 存檔掛載           
            // P3 新增：EULA 同意狀態序列化
            Scribe_Values.Look(ref HasAcceptedExportEula, "HasAcceptedExportEula", false);
            Scribe_Values.Look(ref EulaAcceptedTimestamp, "EulaAcceptedTimestamp", "");
            Scribe_Values.Look(ref EulaAcceptedVersion, "EulaAcceptedVersion", "");
            Scribe_Values.Look(ref EulaAcceptCount, "EulaAcceptCount", 0);
            Scribe_Values.Look(ref AutoTranslateOnUpdate, "AutoTranslateOnUpdate", false);

            // P3 第二階段：冷卻欄位序列化
            Scribe_Collections.Look(ref ExportHistory, "ExportHistory", LookMode.Value);
            Scribe_Values.Look(ref TodayExportDate, "TodayExportDate", "");
            Scribe_Values.Look(ref TodayExportCount, "TodayExportCount", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (ApiConfigs == null || ApiConfigs.Count == 0)
                {
                    ApiConfigs = new List<ApiKeyConfig> { new ApiKeyConfig() };
                }
                // 防 null：舊存檔可能沒有 ExportHistory 欄位
                if (ExportHistory == null) ExportHistory = new List<string>();
            }
        }
    }
}
