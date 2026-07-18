using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責 Harmony 攔截點與文字替換。
// EN: This file defines Harmony patches for UI text replacement.

namespace AutoTranslator_Core
{

    [HarmonyPatch(typeof(UnityEngine.GUI), "Label", new Type[] { typeof(UnityEngine.Rect), typeof(UnityEngine.GUIContent), typeof(UnityEngine.GUIStyle) })]
    // 這個類別負責 補丁GUILabelGUIContent 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_GUI_Label_GUIContent.
    public static class Patch_GUI_Label_GUIContent
    {
        // 這個欄位保存 BypassInterceptor 的執行狀態或快取資料。
        // EN: This field stores bypass interceptor runtime state or cached data.
        public static bool BypassInterceptor = false;
        private static string _skipNextGuiLabelText;
        private static int _skipNextGuiLabelFrame = -1;
        private const int MaxGuiContentCacheSize = 4096;
        private static Dictionary<string, GUIContent> guiContentCache = new Dictionary<string, GUIContent>();


        // 這個方法負責清除 快取 資料。
        // EN: This method clears cache.
        public static void ClearCache()
        {
            guiContentCache.Clear();
        }


        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(UnityEngine.Rect position, ref UnityEngine.GUIContent content)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || BypassInterceptor) return;

            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string originalText = content.text;
                if (_skipNextGuiLabelFrame == Time.frameCount
                    && (ReferenceEquals(_skipNextGuiLabelText, originalText) || string.Equals(_skipNextGuiLabelText, originalText, StringComparison.Ordinal)))
                {
                    _skipNextGuiLabelText = null;
                    _skipNextGuiLabelFrame = -1;
                    return;
                }

                if (UIInterceptor.ShouldBypassUIPatchText(originalText)) return;

                string tooltipText = Mouse.IsOver(position) ? TranslateTooltipText(content.tooltip) : content.tooltip;

                if (UIInterceptor.TryResolveRenderText(originalText, out string translated))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }

                    string contentCacheKey = UIInterceptor.BuildCacheKey(originalText);
                    if (guiContentCache.Count >= MaxGuiContentCacheSize) guiContentCache.Clear();
                    if (!guiContentCache.TryGetValue(contentCacheKey, out GUIContent newContent)
                        || !string.Equals(newContent.text, translated, StringComparison.Ordinal)
                        || !string.Equals(newContent.tooltip, tooltipText, StringComparison.Ordinal))
                    {
                        newContent = new GUIContent(translated, content.image, tooltipText);
                        guiContentCache[contentCacheKey] = newContent;
                    }

                    content = newContent;
                }
                else if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                {
                    content = new GUIContent(content.text, content.image, tooltipText);
                }
            }
        }

        // 這個方法負責翻譯 TooltipText 內容。
        // EN: This method translates tooltip text.
        private static string TranslateTooltipText(string tooltip)
        {
            if (string.IsNullOrWhiteSpace(tooltip)) return tooltip;
            if (UIInterceptor.ShouldBypassUIPatchText(tooltip)) return tooltip;
            if (UIInterceptor.TryResolveRenderText(tooltip, out string translated))
            {
                return translated;
            }
            return tooltip;
        }

        // 這個方法負責翻譯 TooltipSignalText 內容。
        // EN: This method translates tooltip signal text.
        internal static string TranslateTooltipSignalText(string tooltip)
        {
            return TranslateTooltipText(tooltip);
        }

        internal static void SkipNextGuiLabelForText(string text)
        {
            _skipNextGuiLabelText = text;
            _skipNextGuiLabelFrame = Time.frameCount;
        }

        internal sealed class TranslatedTooltipGetter
        {
            private readonly Func<string> _inner;

            public TranslatedTooltipGetter(Func<string> inner)
            {
                _inner = inner;
            }

            public string Invoke()
            {
                return TranslateTooltipSignalText(_inner());
            }
        }
    }

    public static class Patch_LudeonTK_LogWindow_Bypass
    {
        public static void Prefix(out bool __state)
        {
            __state = Patch_GUI_Label_GUIContent.BypassInterceptor;
            if (!AutoTranslatorMod.Settings.EnableUIErrorLogInterception)
            {
                Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            }
        }

        public static void Postfix(bool __state)
        {
            Patch_GUI_Label_GUIContent.BypassInterceptor = __state;
        }
    }

    [HarmonyPatch]
    public static class Patch_LudeonTK_LogWindow_Bypass_Target
    {
        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("LudeonTK.EditWindow_Log");
            return type == null
                ? null
                : AccessTools.Method(type, "DoWindowContents", new[] { typeof(Rect) });
        }

        public static void Prefix(out bool __state)
        {
            Patch_LudeonTK_LogWindow_Bypass.Prefix(out __state);
        }

        public static void Postfix(bool __state)
        {
            Patch_LudeonTK_LogWindow_Bypass.Postfix(__state);
        }
    }

    [HarmonyPatch(typeof(Verse.TooltipHandler), nameof(Verse.TooltipHandler.TipRegion), new Type[] { typeof(Rect), typeof(TipSignal) })]
    // 這個類別負責 補丁TooltipHandlerTipRegionTipSignal 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_TooltipHandler_TipRegion_TipSignal.
    public static class Patch_TooltipHandler_TipRegion_TipSignal
    {
        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(ref TipSignal __1)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;

            if (!string.IsNullOrWhiteSpace(__1.text))
            {
                if (UIInterceptor.ShouldBypassUIPatchText(__1.text)) return;
                __1.text = Patch_GUI_Label_GUIContent.TranslateTooltipSignalText(__1.text);
            }

            if (__1.textGetter != null)
            {
                if (__1.textGetter.Target is Patch_GUI_Label_GUIContent.TranslatedTooltipGetter) return;
                Func<string> originalGetter = __1.textGetter;
                var wrapper = new Patch_GUI_Label_GUIContent.TranslatedTooltipGetter(originalGetter);
                __1.textGetter = wrapper.Invoke;
            }
        }
    }

    [HarmonyPatch(typeof(Verse.TooltipHandler), nameof(Verse.TooltipHandler.TipRegion), new Type[] { typeof(Rect), typeof(Func<string>), typeof(int) })]
    // 這個類別負責 補丁TooltipHandlerTipRegionFunc 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_TooltipHandler_TipRegion_Func.
    public static class Patch_TooltipHandler_TipRegion_Func
    {
        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(ref Func<string> __1)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;
            if (__1 == null) return;
            if (__1.Target is Patch_GUI_Label_GUIContent.TranslatedTooltipGetter) return;

            Func<string> originalGetter = __1;
            var wrapper = new Patch_GUI_Label_GUIContent.TranslatedTooltipGetter(originalGetter);
            __1 = wrapper.Invoke;
        }
    }


    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.Label), new Type[] { typeof(Rect), typeof(string) })]
    // 這個類別負責 補丁WidgetsLabelString 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_Widgets_Label_String.
    public static class Patch_Widgets_Label_String
    {
        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(Rect rect, ref string label)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;
            if (string.IsNullOrEmpty(label)) return;


            if (UIInterceptor.ShouldBypassUIPatchText(label)) return;

            if (UIInterceptor.TryResolveRenderText(label, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + label));
                }
                label = translated;
            }

            Patch_GUI_Label_GUIContent.SkipNextGuiLabelForText(label);
        }
    }


    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.Label), new Type[] { typeof(Rect), typeof(TaggedString) })]
    // 這個類別負責 補丁WidgetsLabelTaggedString 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_Widgets_Label_TaggedString.
    public static class Patch_Widgets_Label_TaggedString
    {
        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(Rect rect, ref TaggedString label)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;

            string raw = label.RawText;
            if (string.IsNullOrEmpty(raw)) return;

            if (UIInterceptor.ShouldBypassUIPatchText(raw)) return;

            if (UIInterceptor.TryResolveRenderText(raw, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + raw));
                }

                label = translated;
                Patch_GUI_Label_GUIContent.SkipNextGuiLabelForText(translated);
            }
            else
            {
                Patch_GUI_Label_GUIContent.SkipNextGuiLabelForText(raw);
            }
        }
    }


    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.LabelFit), new Type[] { typeof(Rect), typeof(string) })]
    // 這個類別負責 補丁WidgetsLabelFit 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Patch_Widgets_LabelFit.
    public static class Patch_Widgets_LabelFit
    {
        // 這個方法負責處理 Prefix 相關流程。
        // EN: This method handles prefix.
        public static void Prefix(Rect rect, ref string label)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;
            if (string.IsNullOrEmpty(label)) return;
            if (UIInterceptor.ShouldBypassUIPatchText(label)) return;

            if (UIInterceptor.TryResolveRenderText(label, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + label));
                }
                label = translated;
            }

            Patch_GUI_Label_GUIContent.SkipNextGuiLabelForText(label);
        }
    }
}
