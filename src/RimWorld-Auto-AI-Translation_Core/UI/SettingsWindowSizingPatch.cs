using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    internal static class SettingsWindowSizingPatch
    {
        private static readonly object Sync = new object();
        private static readonly FieldInfo ModField =
            AccessTools.Field(typeof(Dialog_ModSettings), "mod");
        private static bool _installed;
        private static bool _loggedAppliedSize;

        internal static void EnsureInstalled()
        {
            if (_installed) return;
            lock (Sync)
            {
                if (_installed) return;

                MethodInfo getter = AccessTools.PropertyGetter(
                    typeof(Dialog_ModSettings),
                    nameof(Dialog_ModSettings.InitialSize));
                MethodInfo postfix = AccessTools.Method(
                    typeof(SettingsWindowSizingPatch),
                    nameof(AfterGetInitialSize));
                MethodInfo drawContents = AccessTools.Method(
                    typeof(Dialog_ModSettings),
                    nameof(Dialog_ModSettings.DoWindowContents));
                MethodInfo drawPrefix = AccessTools.Method(
                    typeof(SettingsWindowSizingPatch),
                    nameof(BeforeDrawContents));
                if (getter == null || postfix == null || drawContents == null ||
                    drawPrefix == null || ModField == null)
                {
                    Log.Warning("[AutoTranslationCore] Settings window layout patch was not installed: target members were not found.");
                    return;
                }

                Harmony harmony = new Harmony("MingYang.AutoTranslation.SettingsWindowSizing");
                harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                harmony.Patch(drawContents, prefix: new HarmonyMethod(drawPrefix));
                _installed = true;
            }
        }

        private static void BeforeDrawContents(Dialog_ModSettings __instance, ref Rect inRect)
        {
            try
            {
                Mod mod = ModField.GetValue(__instance) as Mod;
                if (!(mod is AutoTranslatorMod)) return;

                // RimWorld's Dialog_ModSettings constructor forces both close controls on,
                // then DoWindowContents always reserves CloseButSize.y at the bottom. ATC
                // keeps the title-bar X and Escape, suppresses only the redundant bottom
                // button, and returns the reserved height to the mod content rectangle.
                __instance.doCloseButton = false;
                inRect.height += Window.CloseButSize.y;
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] Settings window content layout failed: " + ex.Message);
            }
        }

        private static void AfterGetInitialSize(Dialog_ModSettings __instance, ref Vector2 __result)
        {
            try
            {
                Mod mod = ModField.GetValue(__instance) as Mod;
                if (!(mod is AutoTranslatorMod)) return;

                SettingsWindowSize size = SettingsWindowSizePolicy.Resolve(
                    UI.screenWidth,
                    UI.screenHeight);
                __result = new Vector2(size.Width, size.Height);
                if (!_loggedAppliedSize)
                {
                    _loggedAppliedSize = true;
                    Log.Message(
                        "[AutoTranslationCore] Dynamic settings window size: " +
                        size.Width.ToString("0") + "x" + size.Height.ToString("0") +
                        " for logical screen " + UI.screenWidth + "x" + UI.screenHeight + ".");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AutoTranslationCore] Dynamic settings window sizing failed: " + ex.Message);
            }
        }
    }
}
