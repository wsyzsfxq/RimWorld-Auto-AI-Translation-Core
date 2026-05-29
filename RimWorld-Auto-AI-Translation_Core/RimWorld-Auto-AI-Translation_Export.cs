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
    // ============================================================
    // P3 第一階段：EULA + 模組選擇 + 基本導出
    // ============================================================

    /// <summary>
    /// EULA 版本控制：條款有重大修改時遞增此常數
    /// 玩家如果同意的是舊版本，會被強制重新閱讀新版
    /// </summary>
    public static class ExportEulaVersion
    {
        public const string CurrentVersion = "1.0";
    }

    // ============================================================
    // P3 第二階段：智能分層冷卻管理器
    // ============================================================

    /// <summary>
    /// 冷卻狀態快照
    /// </summary>
    public class CooldownState
    {
        public bool CanExport;
        public int RemainingSeconds;
        public int NextCooldownIfExportNow;  // 如果現在導出，下次會冷卻多久（秒）
        public int RecentExportCount;          // 1 小時內的導出次數
        public int TodayCount;                  // 今日導出次數
        public bool DailyLimitReached;          // 是否達到每日上限

        public string GetDisplayText()
        {
            if (DailyLimitReached)
            {
                return "ATC_Export_TooManyToday".Translate(TodayCount);
            }
            if (CanExport)
            {
                if (NextCooldownIfExportNow == 0)
                    return "ATC_Export_CooldownReady_Free".Translate();
                return "ATC_Export_CooldownReady_NextWillCooldown".Translate(NextCooldownIfExportNow);
            }
            return "ATC_Export_CooldownWait".Translate(RemainingSeconds);
        }
    }

    /// <summary>
    /// 智能分層冷卻管理器
    ///
    /// 設計原則：
    /// - 合法使用（重試、對比）不應觸發冷卻 → 前 2 次免冷卻
    /// - 濫用行為（連續批量導出）會逐步加重冷卻
    /// - 距離上次導出 1 小時後計數重置 → 偶爾使用不累積負擔
    /// </summary>
    public static class ExportCooldownManager
    {
        /// <summary>
        /// 冷卻時間表：索引 = 即將執行的「1 小時內第 N 次」導出
        /// 例如：index=0 表示這是 1 小時內第 1 次導出，免冷卻
        /// </summary>
        private static readonly int[] CooldownSchedule = new[]
        {
        0,    // 第 1 次：免冷卻
        0,    // 第 2 次：免冷卻
        30,   // 第 3 次：30 秒
        60,   // 第 4 次：1 分鐘
        180,  // 第 5 次：3 分鐘
        300   // 第 6 次以後：5 分鐘
    };

        /// <summary>1 小時內的紀錄才算「最近」</summary>
        private const double RECENT_WINDOW_HOURS = 1.0;

        /// <summary>每日導出上限</summary>
        public const int DAILY_LIMIT = 100;

        /// <summary>單次最多導出模組數</summary>
        public const int PER_EXPORT_MOD_LIMIT = 10;

        /// <summary>
        /// 計算當前冷卻狀態
        /// </summary>
        public static CooldownState GetCurrentState()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;

            // 1. 檢查每日重置
            string today = now.ToString("yyyy-MM-dd");
            if (settings.TodayExportDate != today)
            {
                settings.TodayExportCount = 0;
                settings.TodayExportDate = today;
            }

            // 2. 清理過時紀錄（超過 24 小時）
            var cutoff = now.AddHours(-24);
            settings.ExportHistory.RemoveAll(s =>
            {
                if (!DateTime.TryParse(s, out DateTime t)) return true;  // 損壞紀錄一併清掉
                return t < cutoff;
            });

            // 3. 篩選 1 小時內的紀錄
            var recentExports = new List<DateTime>();
            foreach (var s in settings.ExportHistory)
            {
                if (DateTime.TryParse(s, out DateTime t))
                {
                    if ((now - t).TotalHours < RECENT_WINDOW_HOURS)
                        recentExports.Add(t);
                }
            }

            // 4. 檢查每日上限
            bool dailyReached = settings.TodayExportCount >= DAILY_LIMIT;

            // 5. 計算「下次導出」需要的冷卻
            int nextExportIndex = recentExports.Count;  // 即將是 1 小時內第 (N+1) 次
            int cooldownSecondsForNext = GetCooldownForIndex(nextExportIndex);

            // 6. 計算「再下一次」需要的冷卻（給 UI 顯示用）
            int cooldownSecondsForAfter = GetCooldownForIndex(nextExportIndex + 1);

            // 7. 判定能否立刻導出
            if (dailyReached)
            {
                return new CooldownState
                {
                    CanExport = false,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = 0,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = true
                };
            }

            if (cooldownSecondsForNext == 0 || recentExports.Count == 0)
            {
                // 免冷卻
                return new CooldownState
                {
                    CanExport = true,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = cooldownSecondsForAfter,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }

            // 需要冷卻：檢查距離上次導出多久
            DateTime lastExport = recentExports.Max();
            double elapsed = (now - lastExport).TotalSeconds;

            if (elapsed >= cooldownSecondsForNext)
            {
                return new CooldownState
                {
                    CanExport = true,
                    RemainingSeconds = 0,
                    NextCooldownIfExportNow = cooldownSecondsForAfter,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }
            else
            {
                return new CooldownState
                {
                    CanExport = false,
                    RemainingSeconds = (int)Math.Ceiling(cooldownSecondsForNext - elapsed),
                    NextCooldownIfExportNow = cooldownSecondsForNext,
                    RecentExportCount = recentExports.Count,
                    TodayCount = settings.TodayExportCount,
                    DailyLimitReached = false
                };
            }
        }

        /// <summary>
        /// 紀錄一次導出（必須在實際導出成功後呼叫）
        /// </summary>
        public static void RecordExport()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;

            // 寫入歷史
            settings.ExportHistory.Add(now.ToString("o"));

            // 更新今日計數
            string today = now.ToString("yyyy-MM-dd");
            if (settings.TodayExportDate != today)
            {
                settings.TodayExportDate = today;
                settings.TodayExportCount = 1;
            }
            else
            {
                settings.TodayExportCount++;
            }

            // 持久化
            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

            // 日誌：計算下次冷卻
            int recentCount = CountRecentExports();
            int nextCooldown = GetCooldownForIndex(recentCount);
            if (nextCooldown > 0)
            {
                AutoTranslatorSettings.AddLog(
                    "ATC_Log_CooldownTriggered".Translate(recentCount, nextCooldown));
            }
        }

        private static int CountRecentExports()
        {
            var settings = AutoTranslatorMod.Settings;
            var now = DateTime.Now;
            int count = 0;
            foreach (var s in settings.ExportHistory)
            {
                if (DateTime.TryParse(s, out DateTime t))
                {
                    if ((now - t).TotalHours < RECENT_WINDOW_HOURS) count++;
                }
            }
            return count;
        }

        private static int GetCooldownForIndex(int index)
        {
            if (index < 0) return 0;
            if (index >= CooldownSchedule.Length)
                return CooldownSchedule[CooldownSchedule.Length - 1];
            return CooldownSchedule[index];
        }
    }

    /// <summary>
    /// 導出流程控制器：負責判斷該走 EULA 流程還是快速提醒流程
    /// </summary>
    public static class ExportFlowController
    {
        public static void StartExportFlow()
        {
            // 先檢查是否有可導出的內容
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string langsPath = Path.Combine(packPath, "Languages");
            if (!Directory.Exists(langsPath))
            {
                Messages.Message("ATC_ExportWindow_NoTranslationFound".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var settings = AutoTranslatorMod.Settings;

            // EULA 仍有效 → 走快速提醒
            // EULA 過期/未同意/版本不一致 → 走完整 EULA
            if (settings.IsEulaStillValid())
            {
                Find.WindowStack.Add(new Dialog_ExportReminder(OnReminderConfirmed));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_ExportEula(OnEulaAccepted));
            }
        }

        private static void OnEulaAccepted()
        {
            // 寫入同意紀錄
            var settings = AutoTranslatorMod.Settings;
            settings.HasAcceptedExportEula = true;
            settings.EulaAcceptedTimestamp = DateTime.Now.ToString("o"); // ISO 8601
            settings.EulaAcceptedVersion = ExportEulaVersion.CurrentVersion;
            settings.EulaAcceptCount++;

            AutoTranslatorSettings.AddLog("📝 " +
                "ATC_Log_EulaAccepted".Translate(ExportEulaVersion.CurrentVersion));

            // 立刻寫入磁碟（避免玩家關閉遊戲沒存檔）
            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

            // 進入模組選擇視窗
            Find.WindowStack.Add(new Window_Export());
        }

        private static void OnReminderConfirmed()
        {
            Find.WindowStack.Add(new Window_Export());
        }
    }

    // ============================================================
    // 視窗 1：EULA 完整視窗（首次或過期時）
    // ============================================================

    public class Dialog_ExportEula : Window
    {
        private const float COUNTDOWN_SECONDS = 5f;

        private float _countdownRemaining = COUNTDOWN_SECONDS;
        private bool _check1 = false;
        private bool _check2 = false;
        private bool _check3 = false;
        private Vector2 _scrollPos = Vector2.zero;
        private readonly Action _onAccept;

        public override Vector2 InitialSize => new Vector2(700f, 750f);

        public Dialog_ExportEula(Action onAccept)
        {
            _onAccept = onAccept;
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // 倒數計時
            if (_countdownRemaining > 0f)
            {
                _countdownRemaining -= Time.unscaledDeltaTime;
                if (_countdownRemaining < 0f) _countdownRemaining = 0f;
            }
            bool countdownDone = _countdownRemaining <= 0f;

            // 標題
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f),
                "ATC_ExportEula_Title".Translate(ExportEulaVersion.CurrentVersion));
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 38f, inRect.width);

            // EULA 內文（捲動區）
            float scrollHeight = inRect.height - 250f;  // 預留下方倒數+勾選+按鈕
            Rect scrollOuter = new Rect(0, 48f, inRect.width, scrollHeight);
            string fullText = "ATC_ExportEula_FullText".Translate();
            float textHeight = Text.CalcHeight(fullText, scrollOuter.width - 20f);
            Rect scrollInner = new Rect(0, 0, scrollOuter.width - 20f, Mathf.Max(textHeight + 20f, scrollOuter.height));
            Widgets.BeginScrollView(scrollOuter, ref _scrollPos, scrollInner);
            Widgets.Label(new Rect(5f, 5f, scrollInner.width - 10f, textHeight), fullText);
            Widgets.EndScrollView();

            // 倒數顯示
            float yCursor = scrollOuter.yMax + 10f;
            Rect countdownRect = new Rect(0, yCursor, inRect.width, 25f);
            if (countdownDone)
            {
                GUI.color = new Color(0.6f, 1f, 0.6f);
                Widgets.Label(countdownRect, "✅ " + "ATC_ExportEula_CountdownDone".Translate());
            }
            else
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                Widgets.Label(countdownRect, "⏱️ " +
                    "ATC_ExportEula_CountdownLabel".Translate(Mathf.CeilToInt(_countdownRemaining)));
            }
            GUI.color = Color.white;
            yCursor += 30f;

            // 三個 Checkbox（倒數結束才可點擊）
            if (!countdownDone) GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.7f);

            Rect check1Rect = new Rect(0, yCursor, inRect.width, 24f);
            bool check1Backup = _check1;
            Widgets.CheckboxLabeled(check1Rect, "ATC_ExportEula_Check1".Translate(), ref _check1);
            if (!countdownDone) _check1 = check1Backup;  // 倒數未結束強制鎖定
            yCursor += 28f;

            Rect check2Rect = new Rect(0, yCursor, inRect.width, 24f);
            bool check2Backup = _check2;
            Widgets.CheckboxLabeled(check2Rect, "ATC_ExportEula_Check2".Translate(), ref _check2);
            if (!countdownDone) _check2 = check2Backup;
            yCursor += 28f;

            Rect check3Rect = new Rect(0, yCursor, inRect.width, 24f);
            bool check3Backup = _check3;
            Widgets.CheckboxLabeled(check3Rect, "ATC_ExportEula_Check3".Translate(), ref _check3);
            if (!countdownDone) _check3 = check3Backup;
            yCursor += 35f;

            GUI.color = Color.white;

            // 按鈕列
            bool allChecked = _check1 && _check2 && _check3;
            bool canConfirm = countdownDone && allChecked;

            float btnWidth = (inRect.width - 20f) / 2f;
            Rect cancelBtnRect = new Rect(0, yCursor, btnWidth, 40f);
            Rect confirmBtnRect = new Rect(btnWidth + 20f, yCursor, btnWidth, 40f);

            if (Widgets.ButtonText(cancelBtnRect, "ATC_ExportEula_CancelBtn".Translate()))
            {
                Close();
            }

            if (canConfirm)
            {
                GUI.color = new Color(0.4f, 1f, 0.4f);
            }
            else
            {
                GUI.color = new Color(0.4f, 0.4f, 0.4f);
            }

            if (Widgets.ButtonText(confirmBtnRect, "ATC_ExportEula_ConfirmBtn".Translate()))
            {
                if (!canConfirm)
                {
                    Messages.Message("ATC_ExportEula_NeedAllChecks".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
                else
                {
                    Close();
                    _onAccept?.Invoke();
                }
            }
            GUI.color = Color.white;
        }
    }

    // ============================================================
    // 視窗 2：已同意時的快速提醒視窗
    // ============================================================

    public class Dialog_ExportReminder : Window
    {
        private const float COUNTDOWN_SECONDS = 2f;

        private float _countdownRemaining = COUNTDOWN_SECONDS;
        private readonly Action _onConfirm;

        public override Vector2 InitialSize => new Vector2(550f, 350f);

        public Dialog_ExportReminder(Action onConfirm)
        {
            _onConfirm = onConfirm;
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (_countdownRemaining > 0f)
            {
                _countdownRemaining -= Time.unscaledDeltaTime;
                if (_countdownRemaining < 0f) _countdownRemaining = 0f;
            }
            bool countdownDone = _countdownRemaining <= 0f;

            var settings = AutoTranslatorMod.Settings;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f),
                "ATC_ExportReminder_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);

            float y = 50f;

            // 顯示同意時間
            string dateStr = "";
            if (DateTime.TryParse(settings.EulaAcceptedTimestamp, out DateTime accepted))
            {
                dateStr = accepted.ToString("yyyy-MM-dd");
            }
            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ExportReminder_AlreadyAccepted".Translate(dateStr, settings.EulaAcceptedVersion));
            y += 25f;

            int daysLeft = settings.GetEulaRemainingDays();
            GUI.color = daysLeft > 7 ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.7f, 0.3f);
            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ExportReminder_DaysLeft".Translate(daysLeft));
            GUI.color = Color.white;
            y += 30f;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ExportReminder_KeyPoints".Translate());
            y += 25f;

            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ExportReminder_Rule1".Translate());
            y += 22f;
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ExportReminder_Rule2".Translate());
            y += 22f;
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ExportReminder_Rule3".Translate());
            y += 35f;

            // 倒數提示
            if (!countdownDone)
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                Widgets.Label(new Rect(0, y, inRect.width, 22f),
                    "⏱️ " + "ATC_ExportEula_CountdownLabel".Translate(Mathf.CeilToInt(_countdownRemaining)));
                GUI.color = Color.white;
            }
            y += 30f;

            // 按鈕列
            float btnY = inRect.height - 45f;
            float btnWidth = (inRect.width - 20f) / 2f;
            Rect rereadRect = new Rect(0, btnY, btnWidth, 40f);
            Rect continueRect = new Rect(btnWidth + 20f, btnY, btnWidth, 40f);

            if (Widgets.ButtonText(rereadRect, "ATC_ExportReminder_RereadEula".Translate()))
            {
                Close();
                Find.WindowStack.Add(new Dialog_ExportEula(() =>
                {
                    // 重新閱讀完成後寫入新的同意紀錄
                    var s = AutoTranslatorMod.Settings;
                    s.HasAcceptedExportEula = true;
                    s.EulaAcceptedTimestamp = DateTime.Now.ToString("o");
                    s.EulaAcceptedVersion = ExportEulaVersion.CurrentVersion;
                    s.EulaAcceptCount++;
                    LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();
                    Find.WindowStack.Add(new Window_Export());
                }));
                return;
            }

            if (countdownDone)
            {
                GUI.color = new Color(0.4f, 1f, 0.4f);
            }
            else
            {
                GUI.color = new Color(0.4f, 0.4f, 0.4f);
            }
            if (Widgets.ButtonText(continueRect, "ATC_ExportReminder_Continue".Translate()))
            {
                if (countdownDone)
                {
                    Close();
                    _onConfirm?.Invoke();
                }
            }
            GUI.color = Color.white;
        }
    }

    // ============================================================
    // 視窗 3：模組選擇視窗（無全選按鈕）
    // ============================================================

    public class Window_Export : Window
    {
        private string _searchText = "";
        private Vector2 _scrollPos = Vector2.zero;
        private HashSet<string> _selectedPackageIds = new HashSet<string>();
        private List<ExportableModInfo> _availableMods;

        public override Vector2 InitialSize => new Vector2(750f, 700f);

        public Window_Export()
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            _availableMods = ScanAvailableMods();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f),
                "ATC_ExportWindow_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);

            // 沒有可導出的模組
            if (_availableMods.Count == 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(0, 50f, inRect.width, 30f),
                    "ATC_ExportWindow_NoTranslationFound".Translate());
                GUI.color = Color.white;
                return;
            }

            // 搜尋列
            Rect searchRect = new Rect(0, 45f, inRect.width, 30f);
            _searchText = Widgets.TextField(searchRect, _searchText);
            if (string.IsNullOrEmpty(_searchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height),
                    "  " + "ATC_ExportWindow_SearchHint".Translate());
                GUI.color = Color.white;
            }

            // 篩選
            var filtered = string.IsNullOrEmpty(_searchText)
                ? _availableMods
                : _availableMods.Where(m =>
                    m.ModName.ToLower().Contains(_searchText.ToLower()) ||
                    m.PackageId.ToLower().Contains(_searchText.ToLower())).ToList();

            // 統計提示
            Widgets.Label(new Rect(0, 80f, inRect.width, 22f),
                "ATC_ExportWindow_DetectedCount".Translate(_availableMods.Count));

            GUI.color = new Color(1f, 0.8f, 0.4f);
            Widgets.Label(new Rect(0, 102f, inRect.width, 22f),
                "ATC_ExportWindow_NoFullSelectNotice".Translate());
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(0, 130f, inRect.width);

            // 模組列表
            float listY = 140f;
            float listHeight = inRect.height - 240f;
            Rect listOutRect = new Rect(0, listY, inRect.width, listHeight);
            float rowHeight = 56f;
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, filtered.Count * rowHeight);

            Widgets.BeginScrollView(listOutRect, ref _scrollPos, viewRect);
            float yCursor = 0f;
            foreach (var mod in filtered)
            {
                Rect rowRect = new Rect(0, yCursor, viewRect.width, rowHeight - 4f);
                bool isSelected = _selectedPackageIds.Contains(mod.PackageId);

                Widgets.DrawHighlightIfMouseover(rowRect);
                if (isSelected) Widgets.DrawHighlight(rowRect);

                // 點擊整列切換勾選
                if (Mouse.IsOver(rowRect) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    if (isSelected) _selectedPackageIds.Remove(mod.PackageId);
                    else _selectedPackageIds.Add(mod.PackageId);
                    Event.current.Use();
                }

                // Checkbox
                Vector2 checkPos = new Vector2(rowRect.x + 5f, rowRect.y + (rowRect.height - 24f) / 2f);
                Widgets.CheckboxDraw(checkPos.x, checkPos.y, isSelected, false, 24f, null, null);

                // 模組名稱
                Text.Anchor = TextAnchor.UpperLeft;
                Rect nameRect = new Rect(rowRect.x + 40f, rowRect.y + 4f, rowRect.width - 50f, 22f);
                Widgets.Label(nameRect, $"<b>{mod.ModName}</b>  <color=#888888>({mod.PackageId})</color>");

                // 翻譯統計
                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Rect statRect = new Rect(rowRect.x + 40f, rowRect.y + 28f, rowRect.width - 50f, 22f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(statRect,
                    "ATC_ExportWindow_ModInfo_DefCount".Translate(mod.DefInjectedCount, mod.KeyedCount));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                yCursor += rowHeight;
            }
            Widgets.EndScrollView();

            // 底部資訊
            float infoY = listY + listHeight + 10f;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string folderName = $"RimWorld_Translations_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            Widgets.Label(new Rect(0, infoY, inRect.width, 22f),
                "ATC_ExportWindow_OutputPath".Translate(Path.Combine(desktopPath, folderName)));

            // P3 第二階段：冷卻狀態顯示
            CooldownState cooldown = ExportCooldownManager.GetCurrentState();
            GUI.color = cooldown.CanExport
                ? new Color(0.6f, 1f, 0.6f)
                : new Color(1f, 0.7f, 0.3f);
            Widgets.Label(new Rect(0, infoY + 22f, inRect.width, 22f),
                cooldown.GetDisplayText());
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(0, infoY + 44f, inRect.width, 22f),
                "ATC_Export_TodayCount".Translate(cooldown.TodayCount));

            GUI.color = new Color(1f, 0.7f, 0.3f);
            Widgets.Label(new Rect(0, infoY + 66f, inRect.width, 22f),
                "ATC_ExportWindow_WatermarkNotice".Translate());
            GUI.color = Color.white;

            // 確認按鈕
            float btnY = inRect.height - 45f;
            Rect btnRect = new Rect(0, btnY, inRect.width, 40f);

            // P3 第二階段：根據冷卻狀態決定按鈕顏色
            bool canClickConfirm = cooldown.CanExport && _selectedPackageIds.Count > 0;
            if (canClickConfirm)
            {
                GUI.color = new Color(1f, 0.6f, 0.3f);
            }
            else
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
            }

            if (Widgets.ButtonText(btnRect,
                "ATC_ExportWindow_ConfirmBtn".Translate(_selectedPackageIds.Count)))
            {
                // P3 第二階段：完整的前置檢查
                if (_selectedPackageIds.Count == 0)
                {
                    Messages.Message("ATC_ExportWindow_NoModSelected".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
                else if (_selectedPackageIds.Count > ExportCooldownManager.PER_EXPORT_MOD_LIMIT)
                {
                    Messages.Message("ATC_Export_TooManyAtOnce".Translate(_selectedPackageIds.Count),
                        MessageTypeDefOf.RejectInput, false);
                }
                else if (cooldown.DailyLimitReached)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "ATC_Export_TooManyToday".Translate(cooldown.TodayCount),
                        null, null, null, null,
                        "ATC_Export_CooldownDialogTitle".Translate()
                    ));
                    AutoTranslatorSettings.AddLog("ATC_Log_DailyLimitReached".Translate());
                }
                else if (!cooldown.CanExport)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "ATC_Export_CooldownDialogMsg".Translate(cooldown.RemainingSeconds),
                        null, null, null, null,
                        "ATC_Export_CooldownDialogTitle".Translate()
                    ));
                }
                else
                {
                    var modsToExport = _availableMods
                        .Where(m => _selectedPackageIds.Contains(m.PackageId))
                        .ToList();
                    Close();
                    ExportManager.ExecuteExport(modsToExport);
                }
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 掃描 !Translation_AI_Pack/Languages 找出所有有翻譯的模組
        /// </summary>
        private List<ExportableModInfo> ScanAvailableMods()
        {
            var result = new List<ExportableModInfo>();
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string langsPath = Path.Combine(packPath, "Languages");

            if (!Directory.Exists(langsPath)) return result;

            // 收集所有翻譯檔的 packageId 與條目數
            // 檔案命名規則：{packageId_with_underscores}_xxx.xml
            var modStats = new Dictionary<string, (int defCount, int keyedCount)>(StringComparer.OrdinalIgnoreCase);

            foreach (var langDir in Directory.GetDirectories(langsPath))
            {
                // DefInjected
                string defDir = Path.Combine(langDir, "DefInjected");
                if (Directory.Exists(defDir))
                {
                    foreach (var typeDir in Directory.GetDirectories(defDir))
                    {
                        foreach (var file in Directory.GetFiles(typeDir, "*.xml"))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            string packageId = ExtractPackageIdFromFileName(fileName);
                            if (string.IsNullOrEmpty(packageId)) continue;

                            int count = CountEntriesInXml(file);
                            if (!modStats.ContainsKey(packageId)) modStats[packageId] = (0, 0);
                            modStats[packageId] = (modStats[packageId].defCount + count, modStats[packageId].keyedCount);
                        }
                    }
                }

                // Keyed
                string keyedDir = Path.Combine(langDir, "Keyed");
                if (Directory.Exists(keyedDir))
                {
                    foreach (var file in Directory.GetFiles(keyedDir, "*.xml"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string packageId = ExtractPackageIdFromFileName(fileName);
                        if (string.IsNullOrEmpty(packageId)) continue;

                        int count = CountEntriesInXml(file);
                        if (!modStats.ContainsKey(packageId)) modStats[packageId] = (0, 0);
                        modStats[packageId] = (modStats[packageId].defCount, modStats[packageId].keyedCount + count);
                    }
                }
            }

            // 對應到實際的 ModMetaData
            foreach (var kv in modStats)
            {
                // 從底線版反推回點號版
                string normalizedId = kv.Key.Replace("_", ".").ToLower();
                var mod = ModLister.AllInstalledMods.FirstOrDefault(m =>
                    m.PackageId.ToLower() == normalizedId ||
                    m.PackageId.Replace(".", "_").ToLower() == kv.Key.ToLower());

                if (mod != null)
                {
                    result.Add(new ExportableModInfo
                    {
                        ModName = mod.Name,
                        PackageId = mod.PackageId,
                        PackageIdWithUnderscore = kv.Key,
                        ModRootDir = mod.RootDir.FullName,
                        DefInjectedCount = kv.Value.defCount,
                        KeyedCount = kv.Value.keyedCount
                    });
                }
                else
                {
                    // 模組已被解除安裝，但翻譯檔還在
                    result.Add(new ExportableModInfo
                    {
                        ModName = $"[已解除安裝] {kv.Key}",
                        PackageId = normalizedId,
                        PackageIdWithUnderscore = kv.Key,
                        ModRootDir = null,
                        DefInjectedCount = kv.Value.defCount,
                        KeyedCount = kv.Value.keyedCount
                    });
                }
            }

            return result.OrderBy(m => m.ModName).ToList();
        }

        private static string ExtractPackageIdFromFileName(string fileName)
        {
            // 檔名格式：{packageId_with_underscores}_AutoTranslated 或 {packageId_with_underscores}_原檔名
            // 用啟發式：取第一個底線到第二個底線之間的內容判斷
            // 實際上 packageId 可能有底線，所以我們取「直到 _AutoTranslated 或 _Keyed」為止
            int idx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return fileName.Substring(0, idx);

            // Keyed 檔案的命名規則是 {packageId}_{原始檔名}
            // 找不到 _AutoTranslated 就嘗試解析（最後一個底線之前）
            int lastIdx = fileName.LastIndexOf('_');
            if (lastIdx > 0) return fileName.Substring(0, lastIdx);

            return fileName;
        }

        private static int CountEntriesInXml(string filePath)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(filePath);
                return doc.DocumentElement?.ChildNodes.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

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

    // ============================================================
    // 導出管理器：實際執行 IO 與水印寫入
    // ============================================================

    public static class ExportManager
    {
        public static void ExecuteExport(List<ExportableModInfo> mods)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string exportRoot = Path.Combine(desktopPath, $"RimWorld_Translations_{timestamp}");

            AutoTranslatorSettings.AddLog("ATC_Log_ExportStart".Translate(mods.Count));

            try
            {
                Directory.CreateDirectory(exportRoot);
                WriteReadme(exportRoot, mods);
                WriteConsentRecord(exportRoot);

                int totalFiles = 0;
                foreach (var mod in mods)
                {
                    totalFiles += ExportSingleMod(mod, exportRoot);
                }

                // 開啟資料夾
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exportRoot,
                        UseShellExecute = true
                    });
                }
                catch (Exception openEx)
                {
                    Log.Warning($"[AutoTranslationCore] Cannot open folder: {openEx.Message}");
                }

                AutoTranslatorSettings.AddLog("ATC_Log_ExportComplete".Translate(mods.Count, totalFiles));

                // P3 第二階段：記錄冷卻
                ExportCooldownManager.RecordExport();

                // P3 第二階段：彈出帶「聯絡作者」按鈕的成功訊息
                ShowSuccessDialogWithContactOption(mods, totalFiles, exportRoot);
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("ATC_Log_ExportFailed".Translate(ex.Message));
                Messages.Message("ATC_Export_Failed_Message".Translate(ex.Message),
                    MessageTypeDefOf.RejectInput, false);
                Log.Error($"[AutoTranslationCore] Export failed: {ex}");
            }
        }

        /// <summary>
        /// 顯示完成訊息，並提供「聯絡原作者」入口
        /// </summary>
        private static void ShowSuccessDialogWithContactOption(
            List<ExportableModInfo> mods, int totalFiles, string exportPath)
        {
            string message = "ATC_Export_Success_Message".Translate(mods.Count, totalFiles, exportPath);
            string title = "ATC_Export_Success_Title".Translate();

            Find.WindowStack.Add(new Dialog_MessageBox(
                text: message,
                buttonAText: "ATC_Export_Success_ContactAuthorBtn".Translate(),
                buttonAAction: () =>
                {
                    Find.WindowStack.Add(new Dialog_ContactAuthor(mods));
                },
                buttonBText: "ATC_Export_Success_CloseBtn".Translate(),
                buttonBAction: null,
                title: title
            ));
        }
        /// <summary>
        /// 導出單一模組，回傳檔案數
        /// </summary>
        private static int ExportSingleMod(ExportableModInfo mod, string exportRoot)
        {
            int fileCount = 0;
            string targetFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
            string sourcePackPath = AutoTranslatorScanner.GetLocalPackPath();
            string sourceLangsRoot = Path.Combine(sourcePackPath, "Languages");

            // 建立目標資料夾結構
            // {exportRoot}/{cleanModName}/1.6/Languages/{TargetLang}/...
            string safeModName = MakeSafeFolderName(mod.ModName);
            string modExportRoot = Path.Combine(exportRoot, $"AutoTrans_{safeModName}");
            string targetLangPath = Path.Combine(modExportRoot, "1.6", "Languages", targetFolder);

            Directory.CreateDirectory(targetLangPath);

            // 複製 + 加水印 DefInjected
            foreach (var langDir in Directory.GetDirectories(sourceLangsRoot))
            {
                string defInjectedDir = Path.Combine(langDir, "DefInjected");
                if (!Directory.Exists(defInjectedDir)) continue;

                foreach (var typeDir in Directory.GetDirectories(defInjectedDir))
                {
                    string defType = Path.GetFileName(typeDir);
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml"))
                    {
                        if (!IsFileForThisMod(file, mod)) continue;

                        string targetTypeDir = Path.Combine(targetLangPath, "DefInjected", defType);
                        Directory.CreateDirectory(targetTypeDir);
                        string targetFile = Path.Combine(targetTypeDir, Path.GetFileName(file));
                        WriteFileWithWatermark(file, targetFile, mod);
                        fileCount++;
                    }
                }

                // Keyed
                string keyedDir = Path.Combine(langDir, "Keyed");
                if (Directory.Exists(keyedDir))
                {
                    foreach (var file in Directory.GetFiles(keyedDir, "*.xml"))
                    {
                        if (!IsFileForThisMod(file, mod)) continue;

                        string targetKeyedDir = Path.Combine(targetLangPath, "Keyed");
                        Directory.CreateDirectory(targetKeyedDir);
                        string targetFile = Path.Combine(targetKeyedDir, Path.GetFileName(file));
                        WriteFileWithWatermark(file, targetFile, mod);
                        fileCount++;
                    }
                }
            }

            // 寫入 About.xml
            WriteAboutXml(modExportRoot, mod);
            fileCount++;

            // 寫入 LoadFolders.xml
            WriteLoadFoldersXml(modExportRoot);
            fileCount++;

            return fileCount;
        }

        /// <summary>
        /// 判斷一個翻譯檔是否屬於指定模組
        /// </summary>
        private static bool IsFileForThisMod(string filePath, ExportableModInfo mod)
        {
            string fileName = Path.GetFileName(filePath).ToLower();
            string id1 = mod.PackageId.ToLower();
            string id2 = mod.PackageIdWithUnderscore.ToLower();
            return fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") ||
                   fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");
        }

        /// <summary>
        /// 將原檔案複製到目標位置，並在開頭插入水印註解
        /// </summary>
        private static void WriteFileWithWatermark(string sourceFile, string targetFile, ExportableModInfo mod)
        {
            string content = File.ReadAllText(sourceFile, Encoding.UTF8);
            string watermark = ExportTemplates.GetXmlWatermark(mod);

            // 在 XML 宣告之後插入水印註解
            // 找到 ?> 後插入
            int declarationEnd = content.IndexOf("?>");
            string result;
            if (declarationEnd > 0)
            {
                int insertPos = declarationEnd + 2;
                result = content.Substring(0, insertPos) + "\n" + watermark + content.Substring(insertPos);
            }
            else
            {
                // 沒有 XML 宣告，直接前置
                result = watermark + content;
            }

            File.WriteAllText(targetFile, result, Encoding.UTF8);
        }

        private static void WriteAboutXml(string modExportRoot, ExportableModInfo mod)
        {
            string aboutDir = Path.Combine(modExportRoot, "About");
            Directory.CreateDirectory(aboutDir);
            string aboutPath = Path.Combine(aboutDir, "About.xml");

            string content = ExportTemplates.GetAboutXml(mod);
            File.WriteAllText(aboutPath, content, Encoding.UTF8);
        }

        private static void WriteLoadFoldersXml(string modExportRoot)
        {
            string path = Path.Combine(modExportRoot, "LoadFolders.xml");
            string content = @"<?xml version=""1.0"" encoding=""utf-8""?>
<loadFolders>
  <v1.6>
    <li>1.6</li>
  </v1.6>
</loadFolders>";
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        private static void WriteReadme(string exportRoot, List<ExportableModInfo> mods)
        {
            string path = Path.Combine(exportRoot, "README_IMPORTANT.txt");
            string content = ExportTemplates.GetReadme(mods);
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        private static void WriteConsentRecord(string exportRoot)
        {
            string path = Path.Combine(exportRoot, "EULA_Consent_Record.txt");
            var settings = AutoTranslatorMod.Settings;
            string content = ExportTemplates.GetConsentRecord(
                settings.EulaAcceptedTimestamp,
                settings.EulaAcceptedVersion,
                settings.EulaAcceptCount
            );
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        /// <summary>
        /// 將模組名稱轉為安全的資料夾名稱（去除非法字元）
        /// </summary>
        private static string MakeSafeFolderName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) ? '_' : c);
            }
            string result = sb.ToString().Trim();
            return string.IsNullOrEmpty(result) ? "UnnamedMod" : result;
        }
    }

    // ============================================================
    // 模板：水印 / About.xml / README / 同意紀錄
    // ============================================================

    internal static class ExportTemplates
    {
        public static string GetXmlWatermark(ExportableModInfo mod)
        {
            return $@"<!--
  ============================================================
  WARNING: AUTO-GENERATED MACHINE TRANSLATION - DO NOT REDISTRIBUTE
  ============================================================

  This file was generated by Auto Translation Core for personal use only.

  - Original mod copyright belongs to the original author.
  - This translation is an unauthorized derivative work.
  - Public redistribution (Steam Workshop, NEXUS, etc.) is PROHIBITED.
  - Use this as a draft for manual translation work only.

  Generation Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
  Source Mod: {mod.ModName} ({mod.PackageId})
  Generator: Auto Translation Core

  If you found this file uploaded somewhere, please report to:
  - Steam Workshop: Report > DMCA
  - The original mod author
  ============================================================
-->
";
        }

        public static string GetAboutXml(ExportableModInfo mod)
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!--
  WARNING: MACHINE TRANSLATION - DO NOT REDISTRIBUTE
  This is an unauthorized derivative work.
  Original copyright belongs to the author of {mod.ModName}.
-->
<ModMetaData>
    <name>[MachineTrans-Draft] {mod.ModName}</name>
    <author>WARNING: Machine Translated, Not For Public Distribution</author>
    <packageId>localdraft.machinetrans.{mod.PackageId.ToLower()}</packageId>
    <description>WARNING: This is an AI machine translation draft. Strictly for personal or human-polishing use.

Forbidden:
- Uploading to Steam Workshop
- Public redistribution
- Impersonating official translation

Allowed:
- Personal local use
- As a starting point for human translation

Original Mod: {mod.ModName}
Original PackageId: {mod.PackageId}

Generated by: Auto Translation Core
Generation Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
    </description>
    <supportedVersions>
        <li>1.6</li>
    </supportedVersions>
    <modDependencies>
        <li>
            <packageId>{mod.PackageId}</packageId>
            <displayName>{mod.ModName}</displayName>
        </li>
    </modDependencies>
    <loadAfter>
        <li>{mod.PackageId}</li>
    </loadAfter>
</ModMetaData>";
        }

        public static string GetReadme(List<ExportableModInfo> mods)
        {
            // 將模組清單先組裝成字串
            var sb = new StringBuilder();
            foreach (var m in mods)
            {
                sb.AppendLine($"  - {m.ModName} ({m.PackageId})");
                sb.AppendLine($"      DefInjected: {m.DefInjectedCount}, Keyed: {m.KeyedCount}");
            }

            // 呼叫 XML 本地化標籤，並將時間、模組數量、清單字串當作參數塞進去
            return "ATC_Export_Readme_Full".Translate(
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mods.Count,
                sb.ToString()
            ).ToString();
        }

        public static string GetConsentRecord(string timestamp, string version, int count)
        {
            // 呼叫 XML 本地化標籤，一次將所有環境變數塞好
            return "ATC_Export_ConsentRecord_Full".Translate(
                timestamp,
                version,
                count,
                Environment.MachineName,
                Environment.OSVersion.ToString(),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            ).ToString();
        }
    }
    // ============================================================
    // P3 第二階段：聯絡原作者視窗
    // ============================================================

    public class Dialog_ContactAuthor : Window
    {
        private readonly List<ExportableModInfo> _exportedMods;
        private ExportableModInfo _selectedMod;
        private Vector2 _scrollPos = Vector2.zero;
        private Vector2 _templateScrollPos = Vector2.zero;

        public override Vector2 InitialSize => new Vector2(750f, 700f);

        public Dialog_ContactAuthor(List<ExportableModInfo> exportedMods)
        {
            _exportedMods = exportedMods ?? new List<ExportableModInfo>();
            _selectedMod = _exportedMods.FirstOrDefault();

            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f),
                "ATC_ContactAuthor_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);

            float y = 45f;

            // 介紹文字
            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ContactAuthor_JustExported".Translate(_exportedMods.Count));
            y += 28f;

            GUI.color = new Color(1f, 0.9f, 0.4f);
            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ContactAuthor_DidYouKnow".Translate());
            GUI.color = Color.white;
            y += 25f;

            Widgets.Label(new Rect(0, y, inRect.width, 22f),
                "ATC_ContactAuthor_Intro".Translate());
            y += 25f;

            GUI.color = new Color(0.6f, 1f, 0.6f);
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ContactAuthor_Benefit1".Translate());
            y += 22f;
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ContactAuthor_Benefit2".Translate());
            y += 22f;
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ContactAuthor_Benefit3".Translate());
            y += 22f;
            Widgets.Label(new Rect(20f, y, inRect.width - 20f, 22f),
                "ATC_ContactAuthor_Benefit4".Translate());
            GUI.color = Color.white;
            y += 30f;

            Widgets.DrawLineHorizontal(0, y, inRect.width);
            y += 10f;

            // 模組選擇（左欄）+ 範本預覽（右欄）
            float remainHeight = inRect.height - y - 60f;
            float leftWidth = 250f;
            float rightWidth = inRect.width - leftWidth - 10f;

            Rect leftRect = new Rect(0, y, leftWidth, remainHeight);
            Rect rightRect = new Rect(leftWidth + 10f, y, rightWidth, remainHeight);

            // 左欄：模組列表
            Widgets.Label(new Rect(leftRect.x, leftRect.y, leftRect.width, 22f),
                "ATC_ContactAuthor_SelectMod".Translate());
            Rect listOutRect = new Rect(leftRect.x, leftRect.y + 25f, leftRect.width, leftRect.height - 25f);
            Rect listViewRect = new Rect(0, 0, listOutRect.width - 16f, _exportedMods.Count * 32f);
            Widgets.DrawBoxSolid(listOutRect, new Color(0.1f, 0.1f, 0.1f));

            Widgets.BeginScrollView(listOutRect, ref _scrollPos, listViewRect);
            float itemY = 0f;
            foreach (var mod in _exportedMods)
            {
                Rect itemRect = new Rect(0, itemY, listViewRect.width, 30f);
                bool isSelected = mod == _selectedMod;
                if (isSelected) Widgets.DrawHighlight(itemRect);
                else Widgets.DrawHighlightIfMouseover(itemRect);

                if (Widgets.ButtonInvisible(itemRect))
                {
                    _selectedMod = mod;
                }

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(itemRect.x + 5f, itemRect.y, itemRect.width - 10f, itemRect.height),
                    mod.ModName);
                Text.Anchor = TextAnchor.UpperLeft;

                itemY += 32f;
            }
            Widgets.EndScrollView();

            // 右欄：Email 範本預覽
            if (_selectedMod != null)
            {
                Widgets.Label(new Rect(rightRect.x, rightRect.y, rightRect.width, 22f),
                    "ATC_ContactAuthor_TemplateLabel".Translate());

                string template = BuildEmailTemplate(_selectedMod);
                Rect templateOutRect = new Rect(rightRect.x, rightRect.y + 25f,
                    rightRect.width, rightRect.height - 25f);

                float textHeight = Text.CalcHeight(template, templateOutRect.width - 20f);
                Rect templateViewRect = new Rect(0, 0, templateOutRect.width - 20f,
                    Mathf.Max(textHeight + 20f, templateOutRect.height));

                Widgets.DrawBoxSolid(templateOutRect, new Color(0.08f, 0.08f, 0.08f));
                Widgets.BeginScrollView(templateOutRect, ref _templateScrollPos, templateViewRect);
                GUI.color = new Color(0.9f, 0.9f, 0.9f);
                Widgets.Label(new Rect(8f, 5f, templateViewRect.width - 16f, textHeight), template);
                GUI.color = Color.white;
                Widgets.EndScrollView();
            }

            // 底部按鈕列
            float btnY = inRect.height - 45f;
            float btnWidth = (inRect.width - 30f) / 3f;
            Rect copyBtnRect = new Rect(0, btnY, btnWidth, 40f);
            Rect workshopBtnRect = new Rect(btnWidth + 10f, btnY, btnWidth, 40f);
            Rect closeBtnRect = new Rect((btnWidth + 10f) * 2, btnY, btnWidth, 40f);

            // 複製範本
            if (_selectedMod != null)
                GUI.color = new Color(0.4f, 1f, 0.8f);
            else
                GUI.color = new Color(0.5f, 0.5f, 0.5f);

            if (Widgets.ButtonText(copyBtnRect, "ATC_ContactAuthor_CopyTemplate".Translate()))
            {
                if (_selectedMod != null)
                {
                    string template = BuildEmailTemplate(_selectedMod);
                    GUIUtility.systemCopyBuffer = template;
                    Messages.Message("ATC_ContactAuthor_TemplateCopied".Translate(),
                        MessageTypeDefOf.PositiveEvent, false);
                }
            }

            // 打開 Steam Workshop
            GUI.color = new Color(0.4f, 0.8f, 1f);
            if (Widgets.ButtonText(workshopBtnRect, "ATC_ContactAuthor_OpenWorkshop".Translate()))
            {
                TryOpenWorkshopPage(_selectedMod);
            }

            // 關閉
            GUI.color = Color.white;
            if (Widgets.ButtonText(closeBtnRect, "ATC_ContactAuthor_Close".Translate()))
            {
                Close();
            }
            GUI.color = Color.white;
        }

        /// <summary>
        /// 根據模組資訊建構 Email 範本
        /// </summary>
        private string BuildEmailTemplate(ExportableModInfo mod)
        {
            string targetLang = GetTargetLanguageName();
            string authorPlaceholder = TryGetModAuthor(mod) ?? "[Author Name]";

            string subject = "ATC_EmailTemplate_Subject".Translate(mod.ModName);
            string body = "ATC_EmailTemplate_Body".Translate(
                authorPlaceholder,
                mod.ModName,
                targetLang,
                mod.DefInjectedCount,
                mod.KeyedCount
            );

            return $"Subject: {subject}\n\n{body}";
        }

        private string GetTargetLanguageName()
        {
            switch (AutoTranslatorMod.Settings.TargetLang)
            {
                case TargetLanguage.Traditional: return "Traditional Chinese (繁體中文)";
                case TargetLanguage.Simplified: return "Simplified Chinese (简体中文)";
                case TargetLanguage.Japanese: return "Japanese (日本語)";
                case TargetLanguage.Korean: return "Korean (한국어)";
                case TargetLanguage.Russian: return "Russian (Русский)";
                case TargetLanguage.Ukrainian: return "Ukrainian (Українська)";
                case TargetLanguage.English: return "English";
                default: return "English";
            }
        }

        private string TryGetModAuthor(ExportableModInfo info)
        {
            if (string.IsNullOrEmpty(info.PackageId)) return null;
            var meta = ModLister.AllInstalledMods.FirstOrDefault(m =>
                m.PackageId.Equals(info.PackageId, StringComparison.OrdinalIgnoreCase));
            if (meta == null) return null;

            // RimWorld 1.6 API：用 AuthorsString（多作者用逗號分隔的字串）
            // 防呆：用反射檢查屬性是否存在，確保跨版本相容
            try
            {
                var authorsStringProp = typeof(ModMetaData).GetProperty("AuthorsString");
                if (authorsStringProp != null)
                {
                    string s = authorsStringProp.GetValue(meta) as string;
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }

                // Fallback：嘗試 Authors 集合（1.6 可能用 List<string>）
                var authorsProp = typeof(ModMetaData).GetProperty("Authors");
                if (authorsProp != null)
                {
                    var val = authorsProp.GetValue(meta);
                    if (val is System.Collections.IEnumerable enumerable)
                    {
                        var list = new List<string>();
                        foreach (var item in enumerable)
                        {
                            if (item != null) list.Add(item.ToString());
                        }
                        if (list.Count > 0) return string.Join(", ", list);
                    }
                }

                // 最後 fallback：舊版 Author
                var authorProp = typeof(ModMetaData).GetProperty("Author");
                if (authorProp != null)
                {
                    string s = authorProp.GetValue(meta) as string;
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoTranslationCore] Failed to read mod author: {ex.Message}");
            }

            return null;
        }
        /// <summary>
        /// 嘗試開啟模組的 Steam Workshop 頁面
        /// 步驟：
        /// 1. 從 ModMetaData 找 Steam Workshop ID（PublishedFileId.txt）
        /// 2. 用 steam:// 協定開啟（如果失敗 fallback 到網頁版）
        /// </summary>
        private void TryOpenWorkshopPage(ExportableModInfo info)
        {
            if (info == null) return;

            var meta = ModLister.AllInstalledMods.FirstOrDefault(m =>
                m.PackageId.Equals(info.PackageId, StringComparison.OrdinalIgnoreCase));

            if (meta == null || meta.RootDir == null)
            {
                Messages.Message("ATC_ContactAuthor_CannotOpenWorkshop".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            // RimWorld Steam Workshop 模組會在根目錄留下 PublishedFileId.txt
            string idFile = Path.Combine(meta.RootDir.FullName, "About", "PublishedFileId.txt");
            if (!File.Exists(idFile))
            {
                idFile = Path.Combine(meta.RootDir.FullName, "PublishedFileId.txt");
            }

            if (File.Exists(idFile))
            {
                try
                {
                    string workshopId = File.ReadAllText(idFile).Trim();
                    if (!string.IsNullOrEmpty(workshopId) && workshopId.All(char.IsDigit))
                    {
                        // 優先嘗試 Steam 協定（直接在 Steam 客戶端打開）
                        string steamUrl = $"steam://url/CommunityFilePage/{workshopId}";
                        string webUrl = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";

                        try
                        {
                            Application.OpenURL(steamUrl);
                        }
                        catch
                        {
                            Application.OpenURL(webUrl);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[AutoTranslationCore] Read PublishedFileId failed: {ex.Message}");
                }
            }

            Messages.Message("ATC_ContactAuthor_CannotOpenWorkshop".Translate(),
                MessageTypeDefOf.RejectInput, false);
        }
    }
    /// <summary>
    /// 安全地透過反射讀取屬性值，跨版本相容
    /// 用法：var author = ReflectionHelper.GetPropertyValue<string>(meta, "AuthorsString", "Author");
    /// </summary>
    public static class ReflectionHelper
    {
        public static T GetPropertyValue<T>(object obj, params string[] propertyNames) where T : class
        {
            if (obj == null) return null;
            Type type = obj.GetType();
            foreach (var name in propertyNames)
            {
                try
                {
                    var prop = type.GetProperty(name);
                    if (prop != null)
                    {
                        var val = prop.GetValue(obj);
                        if (val is T typed && !string.IsNullOrWhiteSpace(val.ToString()))
                            return typed;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}