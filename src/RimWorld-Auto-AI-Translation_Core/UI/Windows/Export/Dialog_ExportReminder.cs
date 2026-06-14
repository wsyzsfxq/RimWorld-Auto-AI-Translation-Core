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
}
