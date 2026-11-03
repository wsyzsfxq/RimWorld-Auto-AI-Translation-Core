#if RIMWORLD_1_7
using System;
using HarmonyLib;
using UnityEngine;

namespace AutoTranslator_Core.LegacyInterception
{
    // RimWorld 1.7 (Waylith) draws string labels directly instead of routing them
    // through the GUIContent overload. The legacy UI interception therefore needs
    // an explicit patch on GUI.Label(Rect, string, GUIStyle) so text is still
    // replaced. It reuses Patch_Widgets_Label_String, which already carries the
    // nested-call guard and the original-text tooltip registration.
    [HarmonyPatch(typeof(GUI), nameof(GUI.Label), new[] { typeof(Rect), typeof(string), typeof(GUIStyle) })]
    public static class Patch_GUI_Label_String
    {
        public static void Prefix(Rect __0, ref string __1, out bool __state)
        {
            Patch_Widgets_Label_String.Prefix(__0, ref __1, out __state);
        }

        public static Exception Finalizer(Exception __exception, bool __state)
        {
            return Patch_Widgets_Label_String.Finalizer(__exception, __state);
        }
    }
}
#endif
