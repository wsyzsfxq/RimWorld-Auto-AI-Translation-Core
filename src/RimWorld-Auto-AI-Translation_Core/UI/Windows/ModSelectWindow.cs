using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    public class ModSelectWindow : Window
    {
        private const float RowHeight = 60f;

        private string searchText = "";
        private Vector2 scrollPos = Vector2.zero;
        private readonly HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        private readonly List<ModMetaData> preSelectedMods;
        private bool? dragTargetState = null;
        private static bool isTranslatingModNames = false;

        public override Vector2 InitialSize => new Vector2(600f, 700f);

        public ModSelectWindow(List<ModMetaData> updatedMods = null)
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            preSelectedMods = updatedMods ?? new List<ModMetaData>();

            if (AutoTranslatorMod.Settings.AutoClearOldOnUpdate)
            {
                foreach (var mod in preSelectedMods)
                {
                    selectedMods.Add(mod);
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            bool previousBypass = Patch_GUI_Label_GUIContent.BypassInterceptor;
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), "ATC_MultiSelect_Title".Translate());
                Text.Font = GameFont.Small;

                Rect searchRect = new Rect(0, 45f, inRect.width, 30f);
                searchText = Widgets.TextField(searchRect, searchText);
                if (string.IsNullOrEmpty(searchText))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_MultiSelect_Search".Translate());
                    GUI.color = Color.white;
                }

                List<ModMetaData> displayMods = GetDisplayMods();
                DrawSelectionButtons(inRect, displayMods);
                DrawModList(inRect, displayMods);
                DrawStartButton(inRect);
            }
            finally
            {
                Patch_GUI_Label_GUIContent.BypassInterceptor = previousBypass;
            }
        }

        private List<ModMetaData> GetDisplayMods()
        {
            IEnumerable<ModMetaData> mods = AutoTranslatorMod.GetValidModsCached()
                .Where(m => !AutoTranslatorScanner.IsTranslationPatchMod(m));

            if (!string.IsNullOrEmpty(searchText))
            {
                string searchLower = searchText.ToLowerInvariant();
                mods = mods.Where(m =>
                    (m.Name ?? "").ToLowerInvariant().Contains(searchLower) ||
                    (m.PackageId ?? "").ToLowerInvariant().Contains(searchLower) ||
                    GetCachedTranslatedModName(m).ToLowerInvariant().Contains(searchLower));
            }

            return mods.OrderBy(m => m.Name).ToList();
        }

        private void DrawSelectionButtons(Rect inRect, List<ModMetaData> displayMods)
        {
            Rect btnRow = new Rect(0, 85f, inRect.width, 30f);
            bool isAllSelected = displayMods.Count > 0 && displayMods.All(m => selectedMods.Contains(m));
            string btnLabel = isAllSelected ? "ATC_DeselectAll".Translate().ToString() : "ATC_SelectAll".Translate().ToString();

            if (Widgets.ButtonText(new Rect(btnRow.x, btnRow.y, 120f, btnRow.height), btnLabel))
            {
                if (isAllSelected)
                {
                    foreach (var mod in displayMods) selectedMods.Remove(mod);
                }
                else
                {
                    foreach (var mod in displayMods) selectedMods.Add(mod);
                }
            }

            GUI.color = new Color(1f, 0.6f, 0.8f);
            if (Widgets.ButtonText(new Rect(btnRow.x + 130f, btnRow.y, 120f, btnRow.height), "ATC_One_click_chaos".Translate()))
            {
                selectedMods.Clear();
                var rand = new System.Random();
                foreach (var mod in displayMods)
                {
                    if (rand.NextDouble() > 0.5) selectedMods.Add(mod);
                }
            }
            GUI.color = Color.white;
        }

        private void DrawModList(Rect inRect, List<ModMetaData> displayMods)
        {
            Widgets.DrawLineHorizontal(0, 120f, inRect.width);
            Rect listOutRect = new Rect(0, 130f, inRect.width, inRect.height - 180f);
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, displayMods.Count * RowHeight);

            Widgets.BeginScrollView(listOutRect, ref scrollPos, viewRect);

            if (Event.current.type == EventType.MouseUp) dragTargetState = null;

            int firstVisible = Mathf.Max(0, Mathf.FloorToInt(scrollPos.y / RowHeight) - 2);
            int lastVisible = Mathf.Min(displayMods.Count - 1, Mathf.CeilToInt((scrollPos.y + listOutRect.height) / RowHeight) + 2);
            if (firstVisible <= lastVisible)
            {
                QueueVisibleModNameTranslations(displayMods.GetRange(firstVisible, lastVisible - firstVisible + 1));
            }

            for (int i = firstVisible; i <= lastVisible; i++)
            {
                DrawModRow(displayMods[i], new Rect(0, i * RowHeight, viewRect.width, RowHeight));
            }

            Widgets.EndScrollView();
        }

        private void DrawModRow(ModMetaData mod, Rect rowRect)
        {
            bool isChecked = selectedMods.Contains(mod);
            Widgets.DrawHighlightIfMouseover(rowRect);

            if (Mouse.IsOver(rowRect))
            {
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    isChecked = !isChecked;
                    dragTargetState = isChecked;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDrag && dragTargetState.HasValue)
                {
                    isChecked = dragTargetState.Value;
                    Event.current.Use();
                }
            }

            Vector2 checkPos = new Vector2(rowRect.x, rowRect.y + (rowRect.height - 24f) / 2f);
            Widgets.CheckboxDraw(checkPos.x, checkPos.y, isChecked, false, 24f, null, null);

            string displayName = GetDisplayModName(mod);
            string statusLine = GetModStatusLine(mod);
            Rect labelRect = new Rect(rowRect.x + 30f, rowRect.y, rowRect.width - 30f, rowRect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;
            Widgets.Label(labelRect, $"{displayName}\n<size=10><color=#888888>{mod.PackageId}</color>  {statusLine}</size>");
            TooltipHandler.TipRegion(rowRect, $"{displayName}\n{mod.PackageId}\n{GetPlainModStatusText(mod)}");
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;

            if (isChecked) selectedMods.Add(mod);
            else selectedMods.Remove(mod);
        }

        private void DrawStartButton(Rect inRect)
        {
            Rect bottomBtnRect = new Rect(0, inRect.height - 40f, inRect.width, 40f);
            GUI.color = selectedMods.Count > 0 ? new Color(0.6f, 0.9f, 0.6f) : Color.grey;
            if (Widgets.ButtonText(bottomBtnRect, "ATC_MultiSelect_Start".Translate(selectedMods.Count)))
            {
                if (selectedMods.Count > 0)
                {
                    AutoTranslatorSettings.ClearLog();
                    AutoTranslatorSettings.ResetPipelineCancellation();
                    AutoTranslatorScanner.StartMultiScan(selectedMods.ToList());
                    Close();
                }
            }
            GUI.color = Color.white;
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
            if (string.IsNullOrWhiteSpace(translated) ||
                string.Equals(translated.Trim(), mod.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return mod.Name;
            }

            return $"{translated} / {mod.Name}";
        }

        private static string GetModStatusLine(ModMetaData mod)
        {
            ModTranslationStatus status = ModUpdateDetector.GetTranslationStatus(mod);
            string label = ModUpdateDetector.GetTranslationStatusLabelKey(status).Translate().ToString();
            string color = ModUpdateDetector.GetTranslationStatusColorHex(status);
            return $"<color={color}>{label}</color>";
        }

        private static string GetPlainModStatusText(ModMetaData mod)
        {
            return ModUpdateDetector.GetTranslationStatusLabelKey(ModUpdateDetector.GetTranslationStatus(mod)).Translate().ToString();
        }

        private static void QueueVisibleModNameTranslations(List<ModMetaData> displayMods)
        {
            if (!AutoTranslatorMod.Settings.TranslateWorkbenchModNames) return;
            if (AutoTranslatorSettings.IsRunning) return;
            if (isTranslatingModNames) return;
            if (displayMods == null || displayMods.Count == 0) return;
            if (!AutoTranslatorAPI.HasAnyReadyConfig()) return;
            if (!ModNameTranslationCache.TryBeginVisibleQueue(displayMods)) return;

            var pending = displayMods
                .Where(m => m != null)
                .Where(m => !ModNameTranslationCache.TryGet(m, out _))
                .Where(ModNameTranslationCache.TryMarkQueued)
                .Take(4)
                .ToList();

            if (pending.Count == 0) return;

            isTranslatingModNames = true;
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
                            if (translatedNames != null &&
                                translatedNames.Count == pending.Count &&
                                AutoTranslatorMod.Settings.TargetLang == targetLanguage)
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
                            isTranslatingModNames = false;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Verse.Log.Warning($"[AutoTranslationCore] Multi-select mod-name translation failed: {ex.Message}");
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        ModNameTranslationCache.MarkFailed(pending);
                        ModNameTranslationCache.ReleaseQueued(pending);
                        isTranslatingModNames = false;
                    });
                }
            });
        }
    }
}
