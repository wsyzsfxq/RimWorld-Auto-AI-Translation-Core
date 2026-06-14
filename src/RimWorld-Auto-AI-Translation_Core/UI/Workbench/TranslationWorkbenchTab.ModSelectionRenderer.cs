using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class TranslationWorkbenchTab
    {
        private const float ModRowHeight = 44f;
        private const float SearchResultRowHeight = 65f;

        private static void DrawModSelectionMode(Rect leftOutRect, Rect rightOutRect)
        {
            Rect searchRect = new Rect(leftOutRect.x + 5f, leftOutRect.y + 5f, leftOutRect.width - 10f, 30f);
            _modSearchText = Widgets.TextField(searchRect, _modSearchText);
            if (string.IsNullOrEmpty(_modSearchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_Workbench_SearchMod".Translate());
                GUI.color = Color.white;
            }

            Rect filterRect = new Rect(leftOutRect.x + 5f, leftOutRect.y + 40f, leftOutRect.width - 10f, 24f);
            Widgets.CheckboxLabeled(filterRect, "ATC_Workbench_ShowTranslatedOnly".Translate(), ref _showOnlyTranslated);
            Widgets.DrawLineHorizontal(leftOutRect.x, leftOutRect.y + 70f, leftOutRect.width);

            DrawModList(leftOutRect);
            DrawGlobalSearchPanel(rightOutRect);
        }

        private static void DrawModList(Rect leftOutRect)
        {
            HashSet<string> translatedPackageIds = GetTranslatedPackageIdsSafe();
            List<ModMetaData> displayMods = GetModSelectionDisplayMods(translatedPackageIds);

            Rect listOutRect = new Rect(leftOutRect.x, leftOutRect.y + 75f, leftOutRect.width, leftOutRect.height - 75f);
            Rect listViewRect = new Rect(0, 0, listOutRect.width - 20f, Mathf.Max(1f, displayMods.Count * ModRowHeight));
            int firstVisible = Mathf.Max(0, Mathf.FloorToInt(_modListScroll.y / ModRowHeight) - 2);
            int lastVisible = Mathf.Min(displayMods.Count - 1, Mathf.CeilToInt((_modListScroll.y + listOutRect.height) / ModRowHeight) + 2);

            if (firstVisible <= lastVisible)
            {
                QueueVisibleModNameTranslations(displayMods.GetRange(firstVisible, lastVisible - firstVisible + 1));
            }

            Widgets.BeginScrollView(listOutRect, ref _modListScroll, listViewRect);
            try
            {
                for (int i = firstVisible; i <= lastVisible; i++)
                {
                    ModMetaData mod = displayMods[i];
                    if (mod == null) continue;

                    Rect rowRect = new Rect(5f, i * ModRowHeight, listViewRect.width - 5f, ModRowHeight - 5f);
                    Widgets.DrawHighlightIfMouseover(rowRect);

                    if (Widgets.ButtonInvisible(rowRect))
                    {
                        _editingMod = mod;
                        _isLoading = true;
                        _itemSearchText = "";
                        Task.Run(() => LoadRealData(mod));
                    }

                    bool isTranslated = translatedPackageIds.Contains(mod.PackageId ?? "");
                    GUI.color = isTranslated ? new Color(0.6f, 1f, 0.6f) : Color.white;

                    Text.WordWrap = false;
                    string displayName = (isTranslated ? "* " : "") + GetDisplayModName(mod);
                    Widgets.Label(new Rect(rowRect.x + 5f, rowRect.y + 5f, rowRect.width - 10f, rowRect.height - 10f), displayName);
                    TooltipHandler.TipRegion(rowRect, $"{GetDisplayModName(mod)}\n{mod.PackageId}");
                    GUI.color = Color.white;
                }
            }
            finally
            {
                Text.WordWrap = true;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Widgets.EndScrollView();
            }

            if (_isTranslatedModsCacheLoading)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(leftOutRect.x + 8f, leftOutRect.yMax - 22f, leftOutRect.width - 16f, 18f), "Loading translation cache...");
                GUI.color = Color.white;
            }
        }

        private static void DrawGlobalSearchPanel(Rect rightOutRect)
        {
            Text.Font = GameFont.Medium;
            string searchTitle = "ATC_Workbench_GlobalSearchTitle".CanTranslate()
                ? "ATC_Workbench_GlobalSearchTitle".Translate().ToString()
                : "Global Search";
            Widgets.Label(new Rect(rightOutRect.x + 10f, rightOutRect.y + 5f, rightOutRect.width, 30f), searchTitle);
            Text.Font = GameFont.Small;

            Rect searchBoxRect = new Rect(rightOutRect.x + 10f, rightOutRect.y + 40f, rightOutRect.width - 240f, 30f);
            _globalSearchText = Widgets.TextField(searchBoxRect, _globalSearchText);

            Rect langFilterRect = new Rect(rightOutRect.xMax - 220f, rightOutRect.y + 40f, 110f, 30f);
            string allLabel = "ATC_Lang_All".CanTranslate() ? "ATC_Lang_All".Translate().ToString() : "All";
            string filterLabel = _globalSearchLangFilter.HasValue ? _globalSearchLangFilter.Value.ToString() : allLabel;
            if (Widgets.ButtonText(langFilterRect, filterLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption(allLabel, () => _globalSearchLangFilter = null)
                };
                foreach (TargetLanguage lang in Enum.GetValues(typeof(TargetLanguage)))
                {
                    TargetLanguage captureLang = lang;
                    opts.Add(new FloatMenuOption(captureLang.ToString(), () => _globalSearchLangFilter = captureLang));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            GUI.color = new Color(0.4f, 0.8f, 1f);
            string searchBtnLabel = "ATC_Btn_Search".CanTranslate() ? "ATC_Btn_Search".Translate().ToString() : "Search";
            if (Widgets.ButtonText(new Rect(rightOutRect.xMax - 100f, rightOutRect.y + 40f, 90f, 30f), searchBtnLabel))
            {
                if (!string.IsNullOrWhiteSpace(_globalSearchText) && !_isGlobalSearching)
                {
                    ExecuteGlobalSearch(_globalSearchText, _globalSearchLangFilter);
                }
            }
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(rightOutRect.x, rightOutRect.y + 80f, rightOutRect.width);

            if (_isGlobalSearching)
            {
                DrawGlobalSearchLoading(rightOutRect);
            }
            else if (_globalSearchResults.Count > 0)
            {
                DrawGlobalSearchResults(rightOutRect);
            }
            else if (!string.IsNullOrWhiteSpace(_globalSearchText))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                string notFoundMsg = "ATC_Workbench_GlobalSearchNoResult".CanTranslate()
                    ? "ATC_Workbench_GlobalSearchNoResult".Translate().ToString()
                    : "No results";
                Widgets.Label(new Rect(rightOutRect.x, rightOutRect.y + 90f, rightOutRect.width, 100f), notFoundMsg);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private static void DrawGlobalSearchLoading(Rect rightOutRect)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect loadingArea = new Rect(rightOutRect.x, rightOutRect.y + 90f, rightOutRect.width, 80f);

            GUI.color = Color.yellow;
            string loadingMsg = "ATC_Workbench_GlobalSearching".CanTranslate()
                ? "ATC_Workbench_GlobalSearching".Translate().ToString()
                : "Searching...";
            Widgets.Label(new Rect(loadingArea.x, loadingArea.y, loadingArea.width, 30f), loadingMsg);
            GUI.color = Color.white;

            float barWidth = loadingArea.width * 0.6f;
            Rect barRect = new Rect(loadingArea.x + (loadingArea.width - barWidth) / 2f, loadingArea.y + 35f, barWidth, 22f);
            Widgets.FillableBar(barRect, _globalSearchProgress);

            Text.Font = GameFont.Tiny;
            Widgets.Label(barRect, $"{(_globalSearchProgress * 100f):F0}%");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void DrawGlobalSearchResults(Rect rightOutRect)
        {
            Rect resOutRect = new Rect(rightOutRect.x, rightOutRect.y + 85f, rightOutRect.width, rightOutRect.height - 85f);
            Rect resViewRect = new Rect(0, 0, resOutRect.width - 20f, Mathf.Max(1f, _globalSearchResults.Count * SearchResultRowHeight));
            int firstVisible = Mathf.Max(0, Mathf.FloorToInt(_globalSearchScroll.y / SearchResultRowHeight) - 2);
            int lastVisible = Mathf.Min(_globalSearchResults.Count - 1, Mathf.CeilToInt((_globalSearchScroll.y + resOutRect.height) / SearchResultRowHeight) + 2);

            Widgets.BeginScrollView(resOutRect, ref _globalSearchScroll, resViewRect);
            try
            {
                for (int i = firstVisible; i <= lastVisible; i++)
                {
                    GlobalSearchResult res = _globalSearchResults[i];
                    if (res == null || res.Mod == null) continue;

                    Rect rowRect = new Rect(5f, i * SearchResultRowHeight, resViewRect.width - 5f, 60f);
                    Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));
                    Widgets.DrawHighlightIfMouseover(rowRect);

                    if (Widgets.ButtonInvisible(rowRect))
                    {
                        _editingMod = res.Mod;
                        _isLoading = true;
                        _itemSearchText = res.TranslatedText;
                        Task.Run(() => LoadRealData(res.Mod));
                    }

                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(rowRect.x + 5f, rowRect.y + 2f, rowRect.width, 15f), $"[{res.Mod.Name}] {res.Key}");

                    Text.Font = GameFont.Small;
                    GUI.color = Color.white;
                    Widgets.Label(new Rect(rowRect.x + 5f, rowRect.y + 20f, rowRect.width - 10f, 35f), res.TranslatedText);
                }
            }
            finally
            {
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                Widgets.EndScrollView();
            }
        }

        private static string GetCachedTranslatedModName(ModMetaData mod)
        {
            if (mod == null) return "";
            return ModNameTranslationCache.TryGet(mod, out string translated) ? translated : "";
        }

        private static string GetDisplayModName(ModMetaData mod)
        {
            if (mod == null) return "";
            if (!AutoTranslatorMod.Settings.TranslateWorkbenchModNames) return mod.Name;

            string translated = GetCachedTranslatedModName(mod);
            if (string.IsNullOrWhiteSpace(translated) || string.Equals(translated.Trim(), mod.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return mod.Name;
            }

            return $"{translated} / {mod.Name}";
        }

        private static List<ModMetaData> GetModSelectionDisplayMods(HashSet<string> translatedPackageIds)
        {
            translatedPackageIds = translatedPackageIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<ModMetaData> validMods = AutoTranslatorMod.GetValidModsCached() ?? new List<ModMetaData>();
            string searchText = _modSearchText ?? "";
            bool translateNames = AutoTranslatorMod.Settings.TranslateWorkbenchModNames;

            if (_cachedModSelectionList != null &&
                _cachedModSelectionSearch == searchText &&
                _cachedModSelectionShowTranslatedOnly == _showOnlyTranslated &&
                _cachedModSelectionTranslateNames == translateNames &&
                _cachedModSelectionValidCount == validMods.Count &&
                _cachedModSelectionTranslatedCount == translatedPackageIds.Count)
            {
                return _cachedModSelectionList;
            }

            IEnumerable<ModMetaData> allMods = validMods.Where(m => m != null);

            if (_showOnlyTranslated)
            {
                allMods = allMods.Where(m => translatedPackageIds.Contains(m.PackageId ?? ""));
            }

            if (!string.IsNullOrEmpty(searchText))
            {
                string searchLower = searchText.ToLowerInvariant();
                allMods = allMods.Where(m =>
                    ((m.Name ?? "").ToLowerInvariant().Contains(searchLower)) ||
                    ((m.PackageId ?? "").ToLowerInvariant().Contains(searchLower)) ||
                    (translateNames && GetCachedTranslatedModName(m).ToLowerInvariant().Contains(searchLower)));
            }

            _cachedModSelectionList = allMods.ToList();
            _cachedModSelectionSearch = searchText;
            _cachedModSelectionShowTranslatedOnly = _showOnlyTranslated;
            _cachedModSelectionTranslateNames = translateNames;
            _cachedModSelectionValidCount = validMods.Count;
            _cachedModSelectionTranslatedCount = translatedPackageIds.Count;
            return _cachedModSelectionList;
        }

        private static void QueueVisibleModNameTranslations(List<ModMetaData> displayMods)
        {
            if (!AutoTranslatorMod.Settings.TranslateWorkbenchModNames) return;
            if (AutoTranslatorSettings.IsRunning) return;
            if (_isTranslatingModNames) return;
            if (displayMods == null || displayMods.Count == 0) return;
            if (!AutoTranslatorAPI.HasAnyReadyConfig()) return;
            if (!ModNameTranslationCache.TryBeginVisibleQueue(displayMods)) return;

            List<ModMetaData> pending = displayMods
                .Where(m => m != null)
                .Where(m => !ModNameTranslationCache.TryGet(m, out _))
                .Where(ModNameTranslationCache.TryMarkQueued)
                .Take(4)
                .ToList();

            if (pending.Count == 0) return;

            _isTranslatingModNames = true;
            TargetLanguage targetLanguage = AutoTranslatorMod.Settings.TargetLang;
            Task.Run(async () =>
            {
                try
                {
                    List<string> translatedNames = await AutoTranslatorAPI.TranslateBatchAsync(
                        pending.Select(m => m.Name).ToList(),
                        suppressFinalParseError: true);

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        try
                        {
                            if (translatedNames != null && translatedNames.Count == pending.Count && AutoTranslatorMod.Settings.TargetLang == targetLanguage)
                            {
                                for (int i = 0; i < pending.Count; i++)
                                {
                                    string translated = translatedNames[i]?.Trim() ?? "";
                                    if (!string.IsNullOrWhiteSpace(translated))
                                    {
                                        ModNameTranslationCache.Store(pending[i], translated);
                                    }
                                }
                                ModNameTranslationCache.SaveIfDirty();
                            }
                            else
                            {
                                ModNameTranslationCache.MarkFailed(pending);
                            }
                        }
                        finally
                        {
                            ModNameTranslationCache.ReleaseQueued(pending);
                            _isTranslatingModNames = false;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Verse.Log.Warning($"[AutoTranslationCore] Workbench mod-name translation failed: {ex.Message}");
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        ModNameTranslationCache.MarkFailed(pending);
                        ModNameTranslationCache.ReleaseQueued(pending);
                        _isTranslatingModNames = false;
                    });
                }
            });
        }
    }
}
