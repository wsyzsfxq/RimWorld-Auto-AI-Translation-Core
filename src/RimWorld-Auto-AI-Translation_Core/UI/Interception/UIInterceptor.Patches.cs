using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                string tooltipText = TranslateTooltipText(content.tooltip);


                if (originalText.StartsWith("\u200B"))
                {
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                    return;
                }

                if (!UIInterceptor.ShouldInterceptText(originalText))
                {
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                    return;
                }
                string contentCacheKey = UIInterceptor.BuildCacheKey(UIInterceptor.GetTranslationLookupText(originalText));


                if (guiContentCache.TryGetValue(contentCacheKey, out GUIContent readyContent))
                {
                    if (!UIInterceptor.TrySanitizeUIReplacementText(originalText, readyContent.text, out string readyText))
                    {
                        guiContentCache.Remove(contentCacheKey);
                    }
                    else
                    {
                        if (!string.Equals(readyText, readyContent.text, StringComparison.Ordinal))
                        {
                            readyContent = new GUIContent(readyText, readyContent.image, tooltipText);
                            guiContentCache[contentCacheKey] = readyContent;
                        }
                        else if (!string.Equals(tooltipText, readyContent.tooltip, StringComparison.Ordinal))
                        {
                            readyContent = new GUIContent(readyText, readyContent.image, tooltipText);
                            guiContentCache[contentCacheKey] = readyContent;
                        }

                        if (AutoTranslatorMod.Settings.ShowOriginalUI)
                        {
                            Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                        }
                        content = readyContent;
                        return;
                    }
                }


                if (UIInterceptor.IsIgnored(originalText))
                {
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                    return;
                }


                if (UIInterceptor.TryGetCachedTranslation(originalText, out string translated))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }

                    GUIContent newContent = new GUIContent(translated, content.image, tooltipText);


                    guiContentCache[contentCacheKey] = newContent;

                    content = newContent;
                }
                else
                {

                    UIInterceptor.QueueForTranslation(originalText);
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                }
            }
        }

        // 這個方法負責翻譯 TooltipText 內容。
        // EN: This method translates tooltip text.
        private static string TranslateTooltipText(string tooltip)
        {
            if (string.IsNullOrWhiteSpace(tooltip)) return tooltip;
            if (tooltip.StartsWith("\u200B", StringComparison.Ordinal)) return tooltip;
            if (!UIInterceptor.ShouldInterceptText(tooltip)) return tooltip;
            if (UIInterceptor.IsIgnored(tooltip)) return tooltip;

            if (UIInterceptor.TryGetCachedTranslation(tooltip, out string translated))
            {
                return translated;
            }

            UIInterceptor.QueueForTranslation(tooltip);
            return tooltip;
        }

        // 這個方法負責翻譯 TooltipSignalText 內容。
        // EN: This method translates tooltip signal text.
        internal static string TranslateTooltipSignalText(string tooltip)
        {
            return TranslateTooltipText(tooltip);
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
                __1.text = Patch_GUI_Label_GUIContent.TranslateTooltipSignalText(__1.text);
            }

            if (__1.textGetter != null)
            {
                Func<string> originalGetter = __1.textGetter;
                __1.textGetter = () => Patch_GUI_Label_GUIContent.TranslateTooltipSignalText(originalGetter());
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

            Func<string> originalGetter = __1;
            __1 = () => Patch_GUI_Label_GUIContent.TranslateTooltipSignalText(originalGetter());
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


            if (label.StartsWith("\u200B")) return;
            if (!UIInterceptor.ShouldInterceptText(label)) return;


            if (UIInterceptor.IsIgnored(label)) return;


            if (UIInterceptor.TryGetCachedTranslation(label, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + label));
                }
                label = translated;
            }
            else
            {
                UIInterceptor.QueueForTranslation(label);
            }
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

            if (raw.StartsWith("\u200B")) return;
            if (!UIInterceptor.ShouldInterceptText(raw)) return;
            if (UIInterceptor.IsIgnored(raw)) return;

            if (UIInterceptor.TryGetCachedTranslation(raw, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + raw));
                }

                label = translated;
            }
            else
            {
                UIInterceptor.QueueForTranslation(raw);
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
            if (label.StartsWith("\u200B")) return;
            if (!UIInterceptor.ShouldInterceptText(label)) return;
            if (UIInterceptor.IsIgnored(label)) return;

            if (UIInterceptor.TryGetCachedTranslation(label, out string translated))
            {
                if (AutoTranslatorMod.Settings.ShowOriginalUI)
                {
                    Verse.TooltipHandler.TipRegion(rect,
                        new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + label));
                }
                label = translated;
            }
            else
            {
                UIInterceptor.QueueForTranslation(label);
            }
        }
    }
}
