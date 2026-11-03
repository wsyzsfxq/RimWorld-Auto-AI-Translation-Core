using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    [HarmonyPatch(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) })]
    internal static class RecordKeyedDisplayOrigin
    {
        private static void Postfix(string __0, TaggedString __result)
        { OfficialKeyedCatalog.Observe(__0, __result.ToString(), false); }
    }

    [HarmonyPatch(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string), typeof(object[]) })]
    internal static class RecordFormattedKeyedDisplayOrigin
    {
        private static void Postfix(string __0, string __result)
        { OfficialKeyedCatalog.Observe(__0, __result, true); }
    }

    // Text is resolved at the display/measurement boundary only. No change to
    // a Mod's cached field, variable, dictionary key, or initialization logic.
    [HarmonyPatch]
    internal static class ResolveKeyedTextForDisplay
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type owner in new[] { typeof(Widgets), typeof(GUI) })
            foreach (string name in new[] { "Label", "LabelFit", "Button", "Box", "ButtonText" })
            foreach (MethodInfo method in owner.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.Name != name) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length > 1 && parameters[0].ParameterType == typeof(Rect) &&
                    (parameters[1].ParameterType == typeof(string) || parameters[1].ParameterType == typeof(TaggedString) ||
                     parameters[1].ParameterType == typeof(GUIContent))) yield return method;
            }
        }
        [HarmonyPriority(Priority.First)]
        private static void Prefix(object[] __args)
        {
            if (__args[1] is string text)
            {
                if (OfficialKeyedCatalog.TryResolve(text, out string translated)) __args[1] = translated;
            }
            else if (__args[1] is TaggedString tagged)
            {
                if (OfficialKeyedCatalog.TryResolve(tagged.ToString(), out string translated)) __args[1] = (TaggedString)translated;
            }
            else if (__args[1] is GUIContent content)
            {
                bool changedText = OfficialKeyedCatalog.TryResolve(content.text, out string translated);
                bool changedTip = OfficialKeyedCatalog.TryResolve(content.tooltip, out string tooltip);
                if (changedText || changedTip)
                    __args[1] = new GUIContent(changedText ? translated : content.text, content.image,
                        changedTip ? tooltip : content.tooltip);
            }
        }
    }

    [HarmonyPatch]
    internal static class ResolveTranslatedTextForMeasurement
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
            string original = __args[0] is GUIContent content ? content.text : __args[0] as string;
            if (string.IsNullOrEmpty(original)) return;
            if (!OfficialKeyedCatalog.TryResolve(original, out string translated) &&
                !LegacyInterception.UIInterceptor.TryResolveMeasuredText(original, out translated)) return;
            if (__args[0] is GUIContent previous)
                __args[0] = new GUIContent(translated, previous.image, previous.tooltip);
            else __args[0] = translated;
        }
    }

    [HarmonyPatch(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), new[] { typeof(Rect), typeof(TipSignal) })]
    internal static class ResolveKeyedTooltip
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Func<string>, Getter> Getters =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Func<string>, Getter>();
        [HarmonyPriority(Priority.First)]
        private static void Prefix(ref TipSignal __1)
        {
            if (OfficialKeyedCatalog.TryResolve(__1.text, out string translated)) __1.text = translated;
            if (__1.textGetter != null && !(__1.textGetter.Target is Getter))
                __1.textGetter = Getters.GetValue(__1.textGetter, original => new Getter(original)).Invoke;
        }
        private sealed class Getter
        {
            private readonly Func<string> _original;
            internal Getter(Func<string> original) { _original = original; }
            internal string Invoke()
            {
                string text = _original();
                return OfficialKeyedCatalog.TryResolve(text, out string translated) ? translated : text;
            }
        }
    }
}
