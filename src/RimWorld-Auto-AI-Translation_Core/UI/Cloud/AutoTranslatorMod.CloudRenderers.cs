using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public partial class AutoTranslatorMod : Mod
    {
        private void DrawCloudToolbarAndSettings(Listing_Standard l, Rect viewRect)
        {
            // --- 1. 第一排頂部工具列 (一般玩家操作) ---
            Rect topBarRect1 = l.GetRect(30f);
            if (AutoTranslatorSettings.IsFetchingCloud && AutoTranslatorSettings.CloudFetchStartedUtcTicks > 0)
            {
                double elapsedSeconds = (DateTime.UtcNow.Ticks - AutoTranslatorSettings.CloudFetchStartedUtcTicks) / (double)TimeSpan.TicksPerSecond;
                if (elapsedSeconds > 180.0)
                {
                    AutoTranslatorSettings.CloudFetchGeneration++;
                    AutoTranslatorSettings.IsFetchingCloud = false;
                    AutoTranslatorSettings.CloudFetchStartedUtcTicks = 0;
                    AutoTranslatorSettings.CloudConnectionFailed = true;
                    AutoTranslatorSettings.HasFetchedCloudThisSession = false;
                    Verse.Log.Warning("[ATC Cloud] Registry fetch UI watchdog released a stuck cloud fetch state.");
                }
            }

            if (AutoTranslatorSettings.IsFetchingCloud)
            {
                GUI.color = Color.yellow;
                Widgets.Label(topBarRect1, "ATC_Cloud_Fetching".Translate());
                GUI.color = Color.white;
            }
            else
            {
                if (Widgets.ButtonText(new Rect(topBarRect1.x, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Refresh".Translate()))
                {
                    StartCloudRegistryFetch();
                }

                GUI.color = new Color(1f, 0.8f, 0.2f);
                if (Widgets.ButtonText(new Rect(topBarRect1.x + 150f, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Btn_BatchOfficial".Translate()))
                    ExecuteBatchDownload("Official_Group");

                GUI.color = new Color(0.4f, 0.8f, 1f);
                if (Widgets.ButtonText(new Rect(topBarRect1.x + 300f, topBarRect1.y, 140f, topBarRect1.height), "ATC_Cloud_Btn_BatchAI".Translate()))
                    ExecuteBatchDownload("AI_Auto");
                GUI.color = Color.white;
            }

            l.Gap(5f);

            // --- ✨ 第二排頂部工具列 (漢化組大佬專區) ---
            Rect topBarRect2 = l.GetRect(30f);

            GUI.color = new Color(1f, 0.9f, 0.6f);
            if (Widgets.ButtonText(new Rect(topBarRect2.x, topBarRect2.y, 140f, topBarRect2.height), "ATC_Cloud_Btn_OpenWorkspace".Translate()))
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string workspaceRoot = System.IO.Path.Combine(packPath, "Upload_Workspace");
                System.IO.Directory.CreateDirectory(workspaceRoot); // 確保總管資料夾存在
                UnityEngine.Application.OpenURL("file://" + workspaceRoot); // 彈出 Windows 資料夾
            }

            GUI.color = new Color(1f, 0.6f, 0.2f);
            if (Widgets.ButtonText(new Rect(topBarRect2.x + 150f, topBarRect2.y, 140f, topBarRect2.height), "ATC_Cloud_Btn_BatchUpload".Translate()))
            {
                ExecuteBatchUpload(); // 呼叫批量上傳引擎
            }
            GUI.color = Color.white;

            l.Gap(5f);

            // 暱稱與特權碼輸入區
            Rect userRow = l.GetRect(24f);
            Widgets.Label(new Rect(userRow.x, userRow.y + 2f, 100f, 24f), "ATC_Cloud_Nickname".Translate());
            Settings.CloudNickname = Widgets.TextField(new Rect(userRow.x + 100f, userRow.y, 150f, 24f), Settings.CloudNickname);

            Widgets.Label(new Rect(userRow.x + 280f, userRow.y + 2f, 100f, 24f), "ATC_Cloud_AdminKey".Translate());
            Settings.CloudAdminToken = GUI.PasswordField(new Rect(userRow.x + 380f, userRow.y, 150f, 24f), Settings.CloudAdminToken, '*');
            bool hasPrivilegeCode = !string.IsNullOrWhiteSpace(Settings.CloudAdminToken);

            // ✨ V3.5 上傳類型選擇區 (放在密碼框的下一行)
            l.Gap(5f);
            Rect typeRow = l.GetRect(30f);
            string currentUploadType = NormalizeCloudUploadType(Settings.CloudUploadType, hasPrivilegeCode);
            if (Settings.CloudUploadType != currentUploadType)
            {
                Settings.CloudUploadType = currentUploadType;
                WriteSettings();
            }
            Widgets.Label(new Rect(typeRow.x, typeRow.y + 5f, 120f, 24f), "ATC_Cloud_Type_Select".Translate());

            if (DrawUploadTypeOption(new Rect(typeRow.x + 130f, typeRow.y, 180f, 30f), "ATC_Cloud_Type_AI".Translate().ToString(), Settings.CloudUploadType == "AI_Auto"))
            {
                Settings.CloudUploadType = "AI_Auto";
                WriteSettings();
            }
            if (DrawUploadTypeOption(new Rect(typeRow.x + 320f, typeRow.y, 180f, 30f), "ATC_Cloud_Type_Manual".Translate().ToString(), Settings.CloudUploadType == "Manual"))
            {
                Settings.CloudUploadType = "Manual";
                WriteSettings();
            }
            if (hasPrivilegeCode && DrawUploadTypeOption(new Rect(typeRow.x + 510f, typeRow.y, 180f, 30f), "ATC_Type_Official".Translate().ToString(), Settings.CloudUploadType == "Official_Group"))
            {
                Settings.CloudUploadType = "Official_Group";
                WriteSettings();
            }
            l.Gap(5f);
            Rect batchLogLabelRow = l.GetRect(22f);
            Widgets.Label(batchLogLabelRow, "ATC_Cloud_BatchUploadLogLabel".Translate());

            Rect batchLogRect = l.GetRect(54f);
            Settings.CloudBatchUploadLog = Widgets.TextArea(batchLogRect, Settings.CloudBatchUploadLog ?? "");
            if (string.IsNullOrEmpty(Settings.CloudBatchUploadLog))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(batchLogRect.x + 5f, batchLogRect.y + 2f, batchLogRect.width - 10f, batchLogRect.height), "ATC_Cloud_BatchUploadLogHint".Translate());
                GUI.color = Color.white;
            }
            // ✨ V5.2 雲端專屬語言選擇器！與遊戲本體語言徹底脫鉤！
            l.Gap(5f);
            Rect cloudLangRow = l.GetRect(30f);
            Widgets.Label(new Rect(cloudLangRow.x, cloudLangRow.y + 5f, 120f, 24f), "ATC_Cloud_SelectLang".Translate());

            if (Widgets.ButtonText(new Rect(cloudLangRow.x + 130f, cloudLangRow.y, 200f, 30f), "🌐 " + GetLangLabel(Settings.CloudTargetLang)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                {
                    options.Add(new FloatMenuOption(GetLangLabel(lang), () => {
                        Settings.CloudTargetLang = lang;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            l.Gap(10f);
            Widgets.DrawLineHorizontal(0, l.CurHeight, viewRect.width);
            l.Gap(10f);
        }

        private static bool DrawUploadTypeOption(Rect rect, string label, bool selected)
        {
            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            bool oldWordWrap = Text.WordWrap;

            if (selected)
            {
                Widgets.DrawBoxSolid(rect, new Color(0.25f, 0.35f, 0.25f, 0.32f));
            }
            else if (Mouse.IsOver(rect))
            {
                Widgets.DrawHighlight(rect);
            }

            GUI.color = selected ? new Color(0.75f, 1f, 0.75f) : new Color(0.75f, 0.75f, 0.75f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;

            const float radioSize = 24f;
            float radioY = rect.y + ((rect.height - radioSize) / 2f);
            bool clicked = Widgets.RadioButton(new Vector2(rect.x + 6f, radioY), selected, false) || Widgets.ButtonInvisible(rect);

            Rect labelRect = new Rect(rect.x + 36f, rect.y, rect.width - 40f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = false;
            Widgets.Label(labelRect, label);
            Text.WordWrap = oldWordWrap;
            Text.Anchor = oldAnchor;
            GUI.color = oldColor;

            return clicked && !selected;
        }

        private Dictionary<string, List<CloudModRecord>> BuildCloudLookup(string targetLangFolder)
        {
            // 🚀🚀🚀 架構師極速優化大絕招 (真正解決 FPS 掉到 15 的元凶) 🚀🚀🚀
            // 不要在每幀重建字典與排序！利用快取將 O(N*M) 降維打擊成絕對的 0 延遲！
            if (_cachedCloudLookup == null || _lastCloudRegistryCount != AutoTranslatorSettings.CloudRegistry.Count || _lastCloudLangFolder != targetLangFolder)
            {
                _cachedCloudLookup = new Dictionary<string, List<CloudModRecord>>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in AutoTranslatorSettings.CloudRegistry)
                {
                    if (record.Language == targetLangFolder)
                    {
                        if (!_cachedCloudLookup.ContainsKey(record.PackageId))
                        {
                            _cachedCloudLookup[record.PackageId] = new List<CloudModRecord>();
                        }
                        _cachedCloudLookup[record.PackageId].Add(record);
                    }
                }

                // ✨ 提前把雲端紀錄排序好，絕對不要放在下方迴圈每秒排 60 次！
                foreach (var key in _cachedCloudLookup.Keys.ToList())
                {
                    _cachedCloudLookup[key] = _cachedCloudLookup[key]
                        .OrderByDescending(c => c.TranslationType == "Official_Group" || c.IsVerified)
                        .ThenByDescending(c => c.TranslationDate).ToList();
                }

                _lastCloudRegistryCount = AutoTranslatorSettings.CloudRegistry.Count;
                _lastCloudLangFolder = targetLangFolder;
            }
            var cloudLookup = _cachedCloudLookup;
            return _cachedCloudLookup;
        }

        private void DrawCloudModRow(ModMetaData mod, Rect rowRect, Dictionary<string, List<CloudModRecord>> cloudLookup, string targetLangFolder)
        {
                Widgets.DrawHighlightIfMouseover(rowRect);

                // ✨ V5.0 架構師優化：從剛才建好的高速字典中直接撈取，耗時趨近於 0！
                // ✨ V5.0 架構師優化：從剛才建好的高速字典中直接撈取，耗時趨近於 0！
                List<CloudModRecord> allVersions;
                if (cloudLookup.TryGetValue(mod.PackageId, out var foundList))
                {
                    allVersions = foundList; // ✨ 已經在剛才的快取中排好序了！直接套用！
                }
                else
                {
                    allVersions = new List<CloudModRecord>(); // 找不到給空清單
                }

                AutoTranslatorSettings.SelectedCloudVersion.TryGetValue(mod.PackageId, out CloudModRecord cloudRecord);

                if (cloudRecord == null || !allVersions.Any(v => v.RecordId == cloudRecord.RecordId))
                {
                    cloudRecord = allVersions.FirstOrDefault();
                    if (cloudRecord != null)
                    {
                        AutoTranslatorSettings.SelectedCloudVersion[mod.PackageId] = cloudRecord;
                    }
                    else
                    {
                        AutoTranslatorSettings.SelectedCloudVersion.Remove(mod.PackageId);
                    }
                }
             // --- 狀態判斷邏輯 ---
                string statusText = "";
                Color statusColor = Color.white;
                bool canDownload = false;

                if (cloudRecord == null)
                {
                    statusText = "ATC_Cloud_Status_NoCloud".Translate();
                    statusColor = Color.gray;
                }
                else if (cloudRecord.TranslationType == "Official_Group" || cloudRecord.IsVerified)
                {
                    statusText = "ATC_Cloud_Status_Official".Translate();
                    statusColor = new Color(1f, 0.8f, 0.2f); // 金色
                    canDownload = true;
                }
                else if (cloudRecord.TranslationType == "Manual")
                {
                    statusText = "ATC_Cloud_Status_Manual".Translate();
                    statusColor = new Color(0.4f, 1f, 0.4f); // 亮綠色
                    canDownload = true;
                }
                else
                {
                    statusText = "ATC_Cloud_Status_Latest".Translate();
                    statusColor = new Color(0.4f, 0.8f, 1f); // 藍色 (機翻)
                    canDownload = true;
                }

                // === 按鈕區塊 (從右到左排列，動態計算空間) ===
                Text.Font = GameFont.Small;
                float btnWidth = 85f; // ✨ 咪咪微調：加寬到 85f，確保「刪除中」、「上傳中」不會被切掉！
                float cursorX = rowRect.xMax - 5f;

                // 1. 特權雲端刪除
                if (!string.IsNullOrEmpty(Settings.CloudAdminToken) && cloudRecord != null)
                {
                    cursorX -= btnWidth;
                    Rect deleteCloudBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                    if (AutoTranslatorSettings.CloudUploadTarget == mod.PackageId + "_del")
                    {
                        GUI.color = Color.red;
                        Text.Anchor = TextAnchor.MiddleCenter; // ✨ 文字置中，完美對齊
                        Widgets.Label(deleteCloudBtn, "ATC_Cloud_Deleting".Translate());
                        Text.Anchor = TextAnchor.UpperLeft;    // ✨ 畫完記得恢復原狀
                    }
                    else
                    {
                        GUI.color = new Color(1f, 0.3f, 0.3f);
                        if (Widgets.ButtonText(deleteCloudBtn, "ATC_Cloud_Btn_DeleteCloud".Translate()))
                        {
                            AutoTranslatorSettings.CloudUploadTarget = mod.PackageId + "_del";
                            // ✨ 修正：傳入當前下拉選單選中的那筆精準 cloudRecord.RecordId 過去
                            string pid = mod.PackageId; string lang = targetLangFolder; string token = Settings.CloudAdminToken; string recId = cloudRecord.RecordId;
                            System.Threading.Tasks.Task.Run(async () => {
                                bool success = await AutoTranslatorCloudClient.DeleteCloudRecordAsync(pid, lang, recId, token); ATC_Dispatcher.RunOnMainThread(() => {
                                    AutoTranslatorSettings.CloudUploadTarget = "";
                                    if (success) { Messages.Message("ATC_Msg_DeleteCloudSuccess".Translate(mod.Name), MessageTypeDefOf.PositiveEvent, false); AutoTranslatorSettings.HasFetchedCloudThisSession = false; }
                                    else Messages.Message("ATC_Msg_DeleteCloudFailed".Translate(mod.Name), MessageTypeDefOf.RejectInput, false);
                                });
                            });
                        }
                    }
                    GUI.color = Color.white;
                    cursorX -= 5f;
                }

                // 2. 本地刪除
                cursorX -= btnWidth;
                Rect deleteLocalBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                GUI.color = new Color(1f, 0.6f, 0.6f);
                if (Widgets.ButtonText(deleteLocalBtn, "ATC_Cloud_Btn_DeleteLocal".Translate()))
                {
                    AutoTranslatorScanner.ClearOldTranslationFiles(new List<ModMetaData> { mod });
                    Messages.Message("ATC_Msg_DeleteLocalSuccess".Translate(mod.Name), MessageTypeDefOf.NeutralEvent, false);
                }
                GUI.color = Color.white;
                cursorX -= 5f;

                // 3. 上傳按鈕
                cursorX -= btnWidth;
                Rect uploadBtn = new Rect(cursorX, rowRect.y + 5f, btnWidth - 5f, 30f);
                if (AutoTranslatorSettings.CloudUploadTarget == mod.PackageId)
                {
                    GUI.color = Color.yellow;
                    Text.Anchor = TextAnchor.MiddleCenter; // ✨ 文字置中
                    Widgets.Label(uploadBtn, "ATC_Cloud_Btn_Uploading".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                    if (Widgets.ButtonText(uploadBtn, "ATC_Cloud_Btn_Upload".Translate()))
                    {
                        AutoTranslatorSettings.CloudUploadTarget = mod.PackageId;
                        string packPath = AutoTranslatorScanner.GetLocalPackPath();
                        string uNickname = Settings.CloudNickname; string uToken = Settings.CloudAdminToken;
                        string workspaceDir = System.IO.Path.Combine(packPath, "Upload_Workspace", mod.PackageId, targetLangFolder);
                        string liveLangDir = System.IO.Path.Combine(packPath, "Languages", targetLangFolder);
                        bool useWorkspace = System.IO.Directory.Exists(workspaceDir) && System.IO.Directory.GetFiles(workspaceDir, "*.xml", System.IO.SearchOption.AllDirectories).Length > 0;
                        string finalSourceDir = useWorkspace ? workspaceDir : liveLangDir;

                        string pId = mod.PackageId;
                        string tFolder = targetLangFolder;
                        string mName = mod.Name;
                        string fSource = finalSourceDir;
                        Find.WindowStack.Add(new Window_UploadPreview(mod, tFolder, fSource, mName));

                        // ✨ 因為上傳的複雜邏輯已經搬到 Window_UploadPreview 裡面了，所以這裡只要負責呼叫視窗就好！
                        // 呼叫完視窗先把狀態清空，避免按鈕一直卡在「上傳中」的黃色狀態
                        AutoTranslatorSettings.CloudUploadTarget = "";
                    }
                    GUI.color = Color.white;
                }
                cursorX -= 5f;

                // ✨ 4. 新增大佬專用：打開專屬上傳工作室 (Workspace)
                cursorX -= 40f;
                Rect openFolderBtn = new Rect(cursorX, rowRect.y + 5f, 35f, 30f);
                GUI.color = new Color(1f, 0.9f, 0.6f);
                // 🌟 改用本地化文字，完美適配 35f 的按鈕寬度
                if (Widgets.ButtonText(openFolderBtn, "ATC_Cloud_Btn_Dir".Translate()))
                {
                    string packPath = AutoTranslatorScanner.GetLocalPackPath();
                    string workspaceDir = System.IO.Path.Combine(packPath, "Upload_Workspace", mod.PackageId, targetLangFolder);
                    System.IO.Directory.CreateDirectory(workspaceDir);
                    UnityEngine.Application.OpenURL("file://" + workspaceDir);
                }
                // 🌟 清理重複的垃圾代碼，只留一個乾淨的本地化 Tooltip
                if (Mouse.IsOver(openFolderBtn)) TooltipHandler.TipRegion(openFolderBtn, "ATC_Cloud_Btn_DirTooltip".Translate());
                GUI.color = Color.white;
                cursorX -= 5f;

                // 5. 下載按鈕
                if (canDownload)
                {
                    float dlWidth = 85f;
                    cursorX -= dlWidth;
                    Rect downloadBtn = new Rect(cursorX, rowRect.y + 5f, dlWidth - 5f, 30f);
                    GUI.color = new Color(0.6f, 1f, 0.6f);
                    if (Widgets.ButtonText(downloadBtn, "ATC_Cloud_Btn_Download".Translate()))
                    {
                        Messages.Message("ATC_Msg_DownloadStart".Translate(mod.Name), MessageTypeDefOf.NeutralEvent, false);
                        // ✨ 鎖定目前的 CloudRecord 實體傳遞過去！
                        CloudModRecord targetRecord = cloudRecord;
                        System.Threading.Tasks.Task.Run(async () => {
                            bool success = await AutoTranslatorCloudClient.DownloadAndInjectAsync(mod.PackageId, targetLangFolder, targetRecord);
                            ATC_Dispatcher.RunOnMainThread(() => {
                                if (success) Messages.Message("ATC_Msg_DownloadSuccess".Translate(mod.Name), MessageTypeDefOf.PositiveEvent, false);
                                else Messages.Message("ATC_Msg_DownloadFailed".Translate(mod.Name), MessageTypeDefOf.RejectInput, false);
                            });
                        });
                    }
                    GUI.color = Color.white;
                    cursorX -= 5f;
                }

                // ✨ 6. V5.0 UI 革命：下拉選單 (支援全中文化與直白顯示)
                if (cloudRecord != null)
                {
                    float dropWidth = 140f;
                    cursorX -= dropWidth;
                    Rect verDropRect = new Rect(cursorX, rowRect.y + 5f, dropWidth - 5f, 30f);

                    string mergedTag = cloudRecord.IsSmartMerged ? "ATC_Cloud_SmartMerged".Translate().ToString() : "";

                    // 🌟 咪咪的翻譯轉換器：把硬梆梆的英文代碼轉成中文
                    Func<string, string> getLocType = (t) => {
                        if (t == "Official_Group") return "ATC_Type_Official".Translate();
                        if (t == "Manual") return "ATC_Type_Manual".Translate();
                        if (t == "AI_Auto") return "ATC_Type_AI".Translate();
                        return t;
                    };

                    string currentLocType = getLocType(cloudRecord.TranslationType);
                    // 外面收合時，顯示「版本 + 類型」
                    string verLabel = $"v{cloudRecord.LatestVersion} ({currentLocType}){mergedTag}";

                    if (Widgets.ButtonText(verDropRect, verLabel))
                    {
                        List<FloatMenuOption> verOptions = new List<FloatMenuOption>();
                        foreach (var v in allVersions)
                        {
                            string mTag = v.IsSmartMerged ? "ATC_Cloud_SmartMerged".Translate().ToString() : "";
                            string vLocType = getLocType(v.TranslationType);
                            // 下拉展開時，清楚顯示「日期、類型、上傳者」
                            string optLabel = $"[{v.LastUpdated:yyyy-MM-dd}] ({vLocType}) - {v.Author}{mTag}";
                            verOptions.Add(new FloatMenuOption(optLabel, () => { AutoTranslatorSettings.SelectedCloudVersion[mod.PackageId] = v; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(verOptions));
                    }

                    if (Mouse.IsOver(verDropRect))
                    {
                        string yesStr = "ATC_Cloud_YesWithCount".Translate(cloudRecord.MergedAiCount);
                        string noStr = "ATC_Cloud_No".Translate();
                        string mergeStatus = cloudRecord.IsSmartMerged ? yesStr : noStr;
                        string logDisplay = string.IsNullOrWhiteSpace(cloudRecord.UpdateLog) ? "ATC_Cloud_NoLog".Translate().ToString() : cloudRecord.UpdateLog;

                        // 懸停視窗也同步顯示翻譯過後的類型
                        string tipStr = "ATC_Cloud_UploadDate".Translate(cloudRecord.LastUpdated.ToString("yyyy-MM-dd HH:mm")) + "\n" +
                                        "ATC_Cloud_TransType".Translate(currentLocType) + "\n" +
                                        "ATC_Cloud_IsSmartMerged".Translate(mergeStatus) + "\n" +
                                        "📜 " + "ATC_Cloud_LogTitle".Translate() + ": " + logDisplay;
                        TooltipHandler.TipRegion(verDropRect, tipStr);
                    }
                }
                // === ✨ 最後繪製左側的文字 (動態計算剩餘寬度，絕對不會碰撞重疊！) ===
                float leftSpace = cursorX - rowRect.x - 10f;
                Rect nameRect = new Rect(rowRect.x + 5f, rowRect.y + 2f, leftSpace, 20f);
                Rect statusRect = new Rect(rowRect.x + 5f, rowRect.y + 22f, leftSpace, 18f);

                Text.Font = GameFont.Small;

                // 🌟 咪咪防禦網：關閉自動換行，名字太長就優雅截斷，滑鼠移過去再顯示全名！
                Text.WordWrap = false;
                Widgets.Label(nameRect, mod.Name);
                Text.WordWrap = true; // 畫完一定要開回來，不然其他地方的 UI 會壞掉！

                if (Mouse.IsOver(nameRect))
                {
                    TooltipHandler.TipRegion(nameRect, mod.Name); // 滑鼠懸停顯示完整名字
                }

                Text.Font = GameFont.Tiny;
                GUI.color = statusColor;
                Widgets.Label(statusRect, statusText);
                GUI.color = Color.white;
        }
    }
}
