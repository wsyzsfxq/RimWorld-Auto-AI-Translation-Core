using System;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    internal sealed class Window_TranslationPreferences : Window
    {
        private readonly Action<string> _save;
        private string _draft;
        private Vector2 _scroll;

        public override Vector2 InitialSize => new Vector2(800f, 580f);

        internal Window_TranslationPreferences(string initialValue, Action<string> save)
        {
            _draft = TranslationPreferences.Normalize(initialValue);
            _save = save;
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 36f),
                AutoTranslatorMod.WfText("额外翻译要求", "Translation preferences"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 44f, inRect.width, 60f), AutoTranslatorMod.WfText(
                "填写术语、措辞和风格偏好，例如：人名保留英文。保存后用于新发起的翻译；不自动重翻已有译文，也不参与候选分类复核。",
                "Add terminology, wording and style preferences, such as keeping character names in English. Used for new translations; existing translations and classification reviews are unchanged."));

            Rect editor = new Rect(0f, 110f, inRect.width, Mathf.Max(80f, inRect.height - 166f));
            float width = Mathf.Max(1f, editor.width - 20f);
            float height = Mathf.Max(editor.height, Text.CalcHeight(_draft + "\n", width - 12f) + 40f);
            Widgets.BeginScrollView(editor, ref _scroll, new Rect(0f, 0f, width, height));
            _draft = WorkflowTextInput.Area(new Rect(0f, 0f, width, height), _draft);
            Widgets.EndScrollView();

            float y = inRect.height - 40f;
            if (WorkflowUiStyle.Button(new Rect(0f, y, 120f, 36f),
                    AutoTranslatorMod.WfText("清空", "Clear"), WorkflowButtonStyle.Quiet))
                _draft = string.Empty;
            if (WorkflowUiStyle.Button(new Rect(inRect.width - 254f, y, 120f, 36f),
                    AutoTranslatorMod.WfText("取消", "Cancel"), WorkflowButtonStyle.Quiet))
                Close();
            bool canSave = !AutoTranslatorSettings.IsRunning &&
                !Workflow.WorkflowTaskCoordinator.Instance.IsBusy;
            if (WorkflowUiStyle.Button(new Rect(inRect.width - 120f, y, 120f, 36f),
                    AutoTranslatorMod.WfText("保存", "Save"), WorkflowButtonStyle.Primary, canSave))
            {
                _save?.Invoke(TranslationPreferences.Normalize(_draft));
                Close();
            }
        }
    }
}
