using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
        public static partial class TranslationWorkbenchTab
        {
            private static void DrawEditingMode(UnityEngine.Rect leftOutRect, UnityEngine.Rect rightOutRect)
            {
                    UnityEngine.Rect backBtnRect = new UnityEngine.Rect(leftOutRect.x + 5f, leftOutRect.y + 5f, leftOutRect.width - 10f, 35f);
                    UnityEngine.GUI.color = new UnityEngine.Color(1f, 0.7f, 0.7f);
                    if (Verse.Widgets.ButtonText(backBtnRect, "🔙 " + "ATC_Workbench_BackToList".Translate()))
                    {
                        _editingMod = null;
                        _categorizedData.Clear();
                        InitTranslatedModsCache();
                        return; // ✨ 加上這一行：立刻中斷 UI 繪製，防止下方程式碼報錯！
                    }
                    UnityEngine.GUI.color = UnityEngine.Color.white;

                    Verse.Widgets.DrawLineHorizontal(leftOutRect.x, leftOutRect.y + 45f, leftOutRect.width);

                    if (_isLoading)
                    {
                        Verse.Text.Anchor = UnityEngine.TextAnchor.MiddleCenter;
                        Verse.Widgets.Label(new UnityEngine.Rect(leftOutRect.x, leftOutRect.y + 50f, leftOutRect.width, 100f), "🔄 " + "ATC_UploadPreview_Loading".Translate());
                        Verse.Text.Anchor = UnityEngine.TextAnchor.UpperLeft;
                    }
                    else
                    {
                        UnityEngine.Rect catOutRect = new UnityEngine.Rect(leftOutRect.x, leftOutRect.y + 50f, leftOutRect.width, leftOutRect.height - 50f);
                        UnityEngine.Rect catViewRect = new UnityEngine.Rect(0, 0, catOutRect.width - 20f, _categorizedData.Count * 35f);
                        Verse.Widgets.BeginScrollView(catOutRect, ref _catListScroll, catViewRect);
                        try
                        {
                            float curY = 0f;
                            foreach (var category in _categorizedData.Keys)
                            {
                                UnityEngine.Rect rowRect = new UnityEngine.Rect(5f, curY, catViewRect.width - 5f, 30f);
                                if (_selectedCategory == category) Verse.Widgets.DrawHighlightSelected(rowRect);
                                else Verse.Widgets.DrawHighlightIfMouseover(rowRect);

                                if (Verse.Widgets.ButtonInvisible(rowRect))
                                {
                                    _selectedCategory = category;
                                    _itemScroll = UnityEngine.Vector2.zero;
                                }

                                Verse.Widgets.Label(rowRect, $"{category} ({_categorizedData[category].Count})");
                                curY += 35f;
                            }
                        }
                        finally
                        {
                            Verse.Widgets.EndScrollView();
                        }
                    }

                    UnityEngine.Rect headerRect = new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 5f, rightOutRect.width - 20f, 30f);
                    Verse.Text.Font = Verse.GameFont.Medium;
                    Verse.Widgets.Label(headerRect, _editingMod.Name);
                    Verse.Text.Font = Verse.GameFont.Small;

                    UnityEngine.Rect itemSearchRect = new UnityEngine.Rect(rightOutRect.x + 10f, rightOutRect.y + 40f, rightOutRect.width - 160f, 30f);
                    _itemSearchText = Verse.Widgets.TextField(itemSearchRect, _itemSearchText);
                    if (string.IsNullOrEmpty(_itemSearchText))
                    {
                        UnityEngine.GUI.color = UnityEngine.Color.gray;
                        Verse.Widgets.Label(new UnityEngine.Rect(itemSearchRect.x + 5f, itemSearchRect.y + 2f, itemSearchRect.width, itemSearchRect.height), "🔍 " + "ATC_Workbench_SearchHint".Translate());
                        UnityEngine.GUI.color = UnityEngine.Color.white;
                    }

                    UnityEngine.Rect saveBtnRect = new UnityEngine.Rect(rightOutRect.xMax - 140f, rightOutRect.y + 40f, 130f, 30f);
                    UnityEngine.GUI.color = new UnityEngine.Color(0.4f, 1f, 0.4f);
                    if (Verse.Widgets.ButtonText(saveBtnRect, "💾 " + "ATC_Workbench_SaveBtn".Translate()))
                    {
                        SaveModifications();
                        Verse.Messages.Message("ATC_Workbench_SaveSuccess".Translate(), RimWorld.MessageTypeDefOf.PositiveEvent, false);
                    }
                    UnityEngine.GUI.color = UnityEngine.Color.white;

                    Verse.Widgets.DrawLineHorizontal(rightOutRect.x, rightOutRect.y + 80f, rightOutRect.width);

                    if (!_isLoading && !string.IsNullOrEmpty(_selectedCategory) && _categorizedData.ContainsKey(_selectedCategory))
                    {
                        var items = _categorizedData[_selectedCategory];
                        if (!string.IsNullOrEmpty(_itemSearchText))
                        {
                            string searchLower = _itemSearchText.ToLower();
                            items = items.Where(i =>
                                (i.OriginalText != null && i.OriginalText.ToLower().Contains(searchLower)) ||
                                (i.TranslatedText != null && i.TranslatedText.ToLower().Contains(searchLower)) ||
                                (i.Key != null && i.Key.ToLower().Contains(searchLower))
                            ).ToList();
                        }

                        float rowHeight = 90f;
                        UnityEngine.Rect itemsOutRect = new UnityEngine.Rect(rightOutRect.x, rightOutRect.y + 85f, rightOutRect.width, rightOutRect.height - 85f);
                        UnityEngine.Rect itemsViewRect = new UnityEngine.Rect(0, 0, itemsOutRect.width - 20f, items.Count * rowHeight);
                        Verse.Widgets.BeginScrollView(itemsOutRect, ref _itemScroll, itemsViewRect);

                        try
                        {
                            float editY = 0f;
                            float halfWidth = (itemsViewRect.width - 10f) / 2f;

                            foreach (var item in items)
                            {
                                UnityEngine.Rect itemRect = new UnityEngine.Rect(5f, editY, itemsViewRect.width - 10f, rowHeight - 5f);
                                Verse.Widgets.DrawHighlightIfMouseover(itemRect);

                                Verse.Text.Font = Verse.GameFont.Tiny;
                                UnityEngine.GUI.color = UnityEngine.Color.gray;
                                Verse.Widgets.Label(new UnityEngine.Rect(itemRect.x, itemRect.y, itemRect.width, 15f), item.Key);

                                Verse.Text.Font = Verse.GameFont.Small;
                                UnityEngine.GUI.color = new UnityEngine.Color(0.8f, 0.8f, 0.8f);
                                UnityEngine.Rect originalRect = new UnityEngine.Rect(itemRect.x, itemRect.y + 15f, halfWidth - 5f, itemRect.height - 15f);

                                // 🛡️ 終極防彈裝甲：防止毒瘤原文摧毀 UI
                                try { Verse.Widgets.Label(originalRect, item.OriginalText ?? ""); }
                                catch { Verse.Widgets.Label(originalRect, "[Error: Invalid Rich Text]"); }

                                UnityEngine.GUI.color = UnityEngine.Color.white;
                                UnityEngine.Rect transRect = new UnityEngine.Rect(itemRect.x + halfWidth + 5f, itemRect.y + 15f, halfWidth - 5f, itemRect.height - 15f);

                                // 🛡️ 終極防彈裝甲：防止玩家輸入毒瘤文字
                                string newText = item.TranslatedText ?? "";
                                try { newText = Verse.Widgets.TextArea(transRect, newText); }
                                catch { newText = Verse.Widgets.TextField(transRect, newText); }

                                if (newText != item.TranslatedText)
                                {
                                    item.TranslatedText = newText;
                                    item.IsModified = true;
                                }

                                editY += rowHeight;
                            }
                        }
                        finally
                        {
                            Verse.Widgets.EndScrollView();
                        }
                    }
            }
        }
}
