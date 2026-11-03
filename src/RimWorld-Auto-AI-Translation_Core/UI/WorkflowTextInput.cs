using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    // Input boxes use a fixed readable font instead of inheriting the preceding
    // label's Tiny font. Clone the skin style; never change the shared game style.
    internal static class WorkflowTextInput
    {
        private static GUIStyle _source;
        private static GUIStyle _area;

        internal static string Area(Rect rect, string value)
        {
            GameFont previousFont = Text.Font;
            try
            {
                Text.Font = GameFont.Small;
                GUIStyle source = Text.CurTextAreaStyle;
                if (!ReferenceEquals(source, _source) || !ReferenceEquals(_area?.font, source.font))
                {
                    _source = source;
                    RectOffset padding = source.padding ?? new RectOffset();
                    _area = new GUIStyle(source)
                    {
                        alignment = TextAnchor.UpperLeft,
                        wordWrap = true,
                        richText = false,
                        fixedHeight = 0f,
                        contentOffset = Vector2.zero,
                        padding = new RectOffset(
                            Mathf.Max(6, padding.left), Mathf.Max(6, padding.right),
                            Mathf.Max(6, padding.top), Mathf.Max(6, padding.bottom))
                    };
                }
                return GUI.TextArea(rect, value ?? string.Empty, _area);
            }
            finally { Text.Font = previousFont; }
        }
    }
}
