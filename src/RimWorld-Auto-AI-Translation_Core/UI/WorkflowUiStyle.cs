using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    internal enum WorkflowButtonStyle
    {
        Quiet,
        Primary,
        Warning,
        Stop,
        Link,
        Dropdown,
        Tab,
        ActiveTab
    }

    internal static class WorkflowUiStyle
    {
        private static readonly GUIStyle[] SingleLineSources = new GUIStyle[3];
        private static readonly GUIStyle[] SingleLineStyles = new GUIStyle[3];
        private const float ToggleMarkWidth = 22f;
        private const float ToggleLeadingPadding = 6f;
        private const float ToggleLabelGap = 6f;
        private const float ToggleTrailingPadding = 8f;

        internal static readonly Color Panel = new Color(0.075f, 0.09f, 0.102f, 0.96f);
        internal static readonly Color RaisedPanel = new Color(0.12f, 0.145f, 0.165f, 0.96f);
        internal static readonly Color Header = new Color(0.16f, 0.18f, 0.20f, 0.98f);
        internal static readonly Color Selection = new Color(0.145f, 0.18f, 0.12f, 0.92f);
        internal static readonly Color MutedText = new Color(0.62f, 0.64f, 0.65f);
        internal static readonly Color LinkText = new Color(0.48f, 0.73f, 0.90f);
        internal static readonly Color GoodText = new Color(0.60f, 0.80f, 0.40f);
        internal static readonly Color WarningText = new Color(0.90f, 0.68f, 0.34f);
        internal static readonly Color ErrorText = new Color(0.94f, 0.43f, 0.39f);

        internal static bool Button(
            Rect rect,
            string label,
            WorkflowButtonStyle style = WorkflowButtonStyle.Quiet,
            bool enabled = true,
            GameFont font = GameFont.Small)
        {
            enabled &= GUI.enabled;
            Color previousGuiColor = GUI.color;
            GUI.color = Color.white;
            Color background;
            Color border;
            Color text;
            switch (style)
            {
                case WorkflowButtonStyle.Primary:
                    background = new Color(0.26f, 0.36f, 0.15f, 0.98f);
                    border = new Color(0.46f, 0.60f, 0.28f);
                    text = Color.white;
                    break;
                case WorkflowButtonStyle.Warning:
                    background = new Color(0.46f, 0.35f, 0.08f, 0.98f);
                    border = new Color(0.78f, 0.61f, 0.18f);
                    text = Color.white;
                    break;
                case WorkflowButtonStyle.Stop:
                    background = new Color(0.40f, 0.14f, 0.12f, 0.98f);
                    border = new Color(0.66f, 0.29f, 0.25f);
                    text = Color.white;
                    break;
                case WorkflowButtonStyle.Link:
                    background = Color.clear;
                    border = Color.clear;
                    text = LinkText;
                    break;
                case WorkflowButtonStyle.Dropdown:
                    background = new Color(0.13f, 0.17f, 0.20f, 0.98f);
                    border = new Color(0.38f, 0.55f, 0.66f);
                    text = Color.white;
                    break;
                case WorkflowButtonStyle.ActiveTab:
                    background = new Color(0.12f, 0.145f, 0.165f, 1f);
                    border = new Color(0.38f, 0.42f, 0.45f);
                    text = Color.white;
                    break;
                case WorkflowButtonStyle.Tab:
                    background = new Color(0.085f, 0.10f, 0.115f, 1f);
                    border = new Color(0.25f, 0.28f, 0.30f);
                    text = new Color(0.78f, 0.78f, 0.76f);
                    break;
                default:
                    background = new Color(0.11f, 0.13f, 0.145f, 0.98f);
                    border = new Color(0.31f, 0.35f, 0.38f);
                    text = Color.white;
                    break;
            }

            if (style != WorkflowButtonStyle.Link)
            {
                Widgets.DrawBoxSolid(rect, background);
                DrawBorder(rect, border);
            }
            if (enabled && Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            GUI.color = enabled ? text : new Color(text.r, text.g, text.b, 0.38f);
            SingleLineLabel(new Rect(rect.x + 4f, rect.y + 1f,
                Mathf.Max(0f, rect.width - 8f), Mathf.Max(0f, rect.height - 2f)),
                label, font, TextAnchor.MiddleCenter);
            GUI.color = Color.white;

            if (style == WorkflowButtonStyle.ActiveTab)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 3f, rect.width, 3f), GoodText);
            bool clicked = enabled && Widgets.ButtonInvisible(rect);
            GUI.color = previousGuiColor;
            return clicked;
        }

        // Compact controls must not inherit word wrapping or padding from a
        // preceding label. Measure and draw with the same local style.
        internal static void SingleLineLabel(Rect rect, string label, GameFont font,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (string.IsNullOrEmpty(label) || rect.width <= 0f || rect.height <= 0f) return;
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            try
            {
                Text.Font = font;
                Text.Anchor = anchor;
                Text.WordWrap = false;
                GUIStyle style = GetSingleLineStyle(font);
                style.alignment = anchor;
                GUI.Label(rect, label, style);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        private static GUIStyle GetSingleLineStyle(GameFont font)
        {
            int index = (int)font;
            GUIStyle source = Text.CurFontStyle;
            GUIStyle style = SingleLineStyles[index];
            if (!ReferenceEquals(source, SingleLineSources[index]) || style == null ||
                !ReferenceEquals(style.font, source.font) || style.fontSize != source.fontSize)
            {
                SingleLineSources[index] = source;
                style = new GUIStyle(source)
                {
                    wordWrap = false,
                    padding = new RectOffset(),
                    contentOffset = Vector2.zero,
                    fixedWidth = 0f,
                    fixedHeight = 0f,
                    clipping = TextClipping.Clip
                };
                SingleLineStyles[index] = style;
            }
            return style;
        }

        internal static float ToggleButtonWidth(string label, GameFont font = GameFont.Small)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            bool previousWrap = Text.WordWrap;
            try
            {
                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.WordWrap = false;
                float textWidth = GetSingleLineStyle(font).CalcSize(new GUIContent(label ?? string.Empty)).x;
                return Mathf.Ceil(textWidth + ToggleLeadingPadding + ToggleMarkWidth +
                    ToggleLabelGap + ToggleTrailingPadding);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        internal static bool ToggleButton(Rect rect, string label, bool selected,
            bool enabled = true, GameFont font = GameFont.Small)
        {
            enabled &= GUI.enabled;
            bool clicked = Button(rect, string.Empty, WorkflowButtonStyle.Quiet, enabled, font);
            Color previousColor = GUI.color;
            try
            {
                Rect markRect = new Rect(rect.x + ToggleLeadingPadding, rect.y,
                    ToggleMarkWidth, rect.height);
                GUI.color = !enabled ? MutedText : selected ? GoodText : new Color(0.86f, 0.34f, 0.31f);
                SingleLineLabel(markRect, selected ? "✓" : "×", font, TextAnchor.MiddleCenter);
                GUI.color = enabled ? Color.white : MutedText;
                SingleLineLabel(new Rect(markRect.xMax + ToggleLabelGap, rect.y,
                    Mathf.Max(0f, rect.xMax - markRect.xMax - ToggleLabelGap - ToggleTrailingPadding), rect.height),
                    label, font);
            }
            finally { GUI.color = previousColor; }
            return clicked;
        }

        internal static bool LabeledDropdown(Rect row, string label, string currentValue,
            float labelWidth, bool enabled = true, float maximumControlWidth = float.MaxValue,
            GameFont font = GameFont.Small)
        {
            labelWidth = Mathf.Min(labelWidth, Mathf.Max(0f, row.width - 8f));
            SingleLineLabel(new Rect(row.x, row.y, Mathf.Max(0f, labelWidth - 6f), row.height),
                label, font);
            Rect control = new Rect(row.x + labelWidth, row.y + 2f,
                Mathf.Min(maximumControlWidth, Mathf.Max(0f, row.width - labelWidth)),
                Mathf.Max(0f, row.height - 4f));
            return Button(control, (currentValue ?? string.Empty) + "  ▾",
                WorkflowButtonStyle.Dropdown, enabled, font);
        }

        internal static void DrawBorder(Rect rect, Color color)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1f, rect.height), color);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
    }
}
