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

namespace AutoTranslator_Core
{

    [HarmonyPatch(typeof(UnityEngine.GUI), "Label", new Type[] { typeof(UnityEngine.Rect), typeof(UnityEngine.GUIContent), typeof(UnityEngine.GUIStyle) })]
    public static class Patch_GUI_Label_GUIContent
    {
        public static bool BypassInterceptor = false;
        private static Dictionary<string, GUIContent> guiContentCache = new Dictionary<string, GUIContent>();

        // 🌟 咪咪特製：清空這個 Patch 獨享的 GUI 快取字典！
        public static void ClearCache()
        {
            guiContentCache.Clear();
        }

        // 🌟 咪咪極速瘦身：把又慢又卡的正則拔掉，主迴圈只做 O(1) 查表！
        public static void Prefix(UnityEngine.Rect position, ref UnityEngine.GUIContent content)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || BypassInterceptor) return;

            if (content != null && !string.IsNullOrEmpty(content.text))
            {
                string originalText = content.text;
                string tooltipText = TranslateTooltipText(content.tooltip);

                // 如果這是我們自己貼的「顯示原文」視窗，絕對不准攔截！
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

                // 🚀 終極光速通道 1：有快取直接換！(0 延遲，拯救 FPS)
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

                // 🚀 終極光速通道 2：黑名單直接放行！(0 延遲)
                if (UIInterceptor.IsIgnored(originalText))
                {
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                    return;
                }

                // 🔍 去查記憶體字典！
                if (UIInterceptor.TryGetCachedTranslation(originalText, out string translated))
                {
                    if (AutoTranslatorMod.Settings.ShowOriginalUI)
                    {
                        Verse.TooltipHandler.TipRegion(position, new Verse.TipSignal("\u200B" + "ATC_OriginalText".Translate() + ":\n" + originalText));
                    }

                    GUIContent newContent = new GUIContent(translated, content.image, tooltipText);

                    // 📦 存入光速通道快取！
                    guiContentCache[contentCacheKey] = newContent;

                    content = newContent;
                }
                else
                {
                    // 沒查到？把純淨生肉丟進背景排隊區，讓 Task 去頭痛！
                    UIInterceptor.QueueForTranslation(originalText);
                    if (!string.Equals(tooltipText, content.tooltip, StringComparison.Ordinal))
                    {
                        content = new GUIContent(content.text, content.image, tooltipText);
                    }
                }
            }
        }

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

        internal static string TranslateTooltipSignalText(string tooltip)
        {
            return TranslateTooltipText(tooltip);
        }
    }

    [HarmonyPatch(typeof(Verse.TooltipHandler), nameof(Verse.TooltipHandler.TipRegion), new Type[] { typeof(Rect), typeof(TipSignal) })]
    public static class Patch_TooltipHandler_TipRegion_TipSignal
    {
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
    public static class Patch_TooltipHandler_TipRegion_Func
    {
        public static void Prefix(ref Func<string> __1)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;
            if (__1 == null) return;

            Func<string> originalGetter = __1;
            __1 = () => Patch_GUI_Label_GUIContent.TranslateTooltipSignalText(originalGetter());
        }
    }

    /// <summary>
    /// 攔截 Widgets.Label(Rect, string) — RimWorld 最常用的 UI 文字 API
    /// </summary>
    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.Label), new Type[] { typeof(Rect), typeof(string) })]
    public static class Patch_Widgets_Label_String
    {
        public static void Prefix(Rect rect, ref string label)
        {
            if (!AutoTranslatorMod.Settings.EnableUIInterceptor || Patch_GUI_Label_GUIContent.BypassInterceptor) return;
            if (string.IsNullOrEmpty(label)) return;

            // 零寬字元保護（避免攔截自己貼出去的原文 tooltip）
            if (label.StartsWith("\u200B")) return;
            if (!UIInterceptor.ShouldInterceptText(label)) return;

            // 黑名單快速通道
            if (UIInterceptor.IsIgnored(label)) return;

            // 命中快取直接替換
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


    /// <summary>
    /// 攔截 Widgets.Label(Rect, TaggedString) — RimWorld 1.6 主力（Translate() 回傳型別）
    /// </summary>
    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.Label), new Type[] { typeof(Rect), typeof(TaggedString) })]
    public static class Patch_Widgets_Label_TaggedString
    {
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
                // TaggedString 有隱式轉換到 string，直接賦值即可
                label = translated;
            }
            else
            {
                UIInterceptor.QueueForTranslation(raw);
            }
        }
    }


    /// <summary>
    /// 攔截 Widgets.LabelFit(Rect, string) — 自動縮放版本
    /// </summary>
    [HarmonyPatch(typeof(Verse.Widgets), nameof(Verse.Widgets.LabelFit), new Type[] { typeof(Rect), typeof(string) })]
    public static class Patch_Widgets_LabelFit
    {
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
