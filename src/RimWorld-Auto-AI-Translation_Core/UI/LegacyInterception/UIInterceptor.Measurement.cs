using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core.LegacyInterception
{
    public static partial class UIInterceptor
    {
        // Measurement must never enqueue a translation or change the source name.
        internal static bool TryResolveMeasuredText(string text, out string translated)
        {
            translated = null;
            return UIInterceptionModeController.LegacyEnabled &&
                !Patch_GUI_Label_GUIContent.BypassInterceptor && !string.IsNullOrEmpty(text) &&
                !ShouldBypassUIPatchText(text) && !IsIgnored(text) &&
                !IsSourceOwnerBlacklisted(text) && TryGetCachedTranslationKnownSafe(text, out translated);
        }
    }

    // Retain legacy cached-text measurement without official Keyed reverse lookup.
    [HarmonyPatch]
    internal static class ResolveLegacyTextForMeasurement
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type owner in new[] { typeof(Text), typeof(GenUI), typeof(GUIStyle) })
            foreach (MethodInfo method in owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            {
                if (method.Name != "CalcSize" && method.Name != "CalcHeight" && method.Name != "GetWidthCached") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > 0 && (parameters[0].ParameterType == typeof(string) ||
                    parameters[0].ParameterType == typeof(GUIContent))) yield return method;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Prefix(object[] __args)
        {
            if (!UIInterceptionModeController.LegacyEnabled) return;
            string original = __args[0] is GUIContent content ? content.text : __args[0] as string;
            if (!UIInterceptor.TryResolveMeasuredText(original, out string translated)) return;
            if (__args[0] is GUIContent previous)
                __args[0] = new GUIContent(translated, previous.image, previous.tooltip);
            else __args[0] = translated;
        }
    }

    [HarmonyPatch(typeof(GenMapUI), "GetPawnLabel", new[]
        { typeof(Pawn), typeof(float), typeof(Dictionary<string, string>), typeof(GameFont) })]
    internal static class PawnLabelTranslationBeforeLayout
    {
        private static bool Prefix(Pawn __0, float __1, Dictionary<string, string> __2,
            GameFont __3, ref string __result)
        {
            if (__0 == null || !UIInterceptor.TryResolveMeasuredText(__0.LabelShortCap, out string translated))
                return true;
            GameFont previous = Text.Font;
            try
            {
                Text.Font = __3;
                // Use the translated name as the truncation/cache key. Both the
                // colonist bar and map labels measure this same returned value.
                __result = translated.Truncate(__1, __2);
                return false;
            }
            finally { Text.Font = previous; }
        }
    }
}
