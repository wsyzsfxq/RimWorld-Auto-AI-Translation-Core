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
}
