using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    public class DeleteTranslationWindow : Window
    {
        private const float RowHeight = 56f;

        private string searchText = "";
        private Vector2 scrollPos = Vector2.zero;
        private readonly HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        private bool? dragTargetState = null;
        private static bool isTranslatingModNames = false;

        public override Vector2 InitialSize => new Vector2(600f, 700f);

        public DeleteTranslationWindow()
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            bool previousBypass = Patch_GUI_Label_GUIContent.BypassInterceptor;
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), "ATC_DeleteModTrans_Title".Translate());
                Text.Font = GameFont.Small;

                List<ModMetaData> displayMods = GetDisplayMods();

                Rect searchRect = new Rect(0, 45f, inRect.width - 130f, 30f);
                searchText = Widgets.TextField(searchRect, searchText);
                if (string.IsNullOrEmpty(searchText))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_MultiSelect_Search".Translate());
                    GUI.color = Color.white;
                }

                DrawSelectVisibleButton(new Rect(searchRect.xMax + 10f, searchRect.y, 120f, searchRect.height), displayMods);
                DrawModList(inRect, displayMods);
                DrawDeleteButton(inRect);
            }
            finally
            {
                Patch_GUI_Label_GUIContent.BypassInterceptor = previousBypass;
            }
        }

        private List<ModMetaData> GetDisplayMods()
        {
            IEnumerable<ModMetaData> mods = AutoTranslatorMod.GetValidModsCached()
                .Where(ModUpdateDetector.HasLocalTranslationFiles);

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

        private void DrawSelectVisibleButton(Rect buttonRect, List<ModMetaData> displayMods)
        {
            bool hasVisibleMods = displayMods != null && displayMods.Count > 0;
            bool allVisibleSelected = hasVisibleMods && displayMods.All(m => selectedMods.Contains(m));
            string label = allVisibleSelected ? "ATC_DeselectAll".Translate().ToString() : "ATC_SelectAll".Translate().ToString();

            GUI.color = hasVisibleMods ? Color.white : Color.grey;
            if (Widgets.ButtonText(buttonRect, label) && hasVisibleMods)
            {
                foreach (ModMetaData mod in displayMods)
                {
                    selectedMods.Remove(mod);
                }

                if (!allVisibleSelected)
                {
                    foreach (ModMetaData mod in displayMods)
                    {
                        selectedMods.Add(mod);
                    }
                }
            }
            GUI.color = Color.white;
        }

        private void DrawModList(Rect inRect, List<ModMetaData> displayMods)
        {
            Widgets.DrawLineHorizontal(0, 85f, inRect.width);
            Rect listOutRect = new Rect(0, 95f, inRect.width, inRect.height - 145f);
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
            Rect labelRect = new Rect(rowRect.x + 30f, rowRect.y, rowRect.width - 30f, rowRect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.WordWrap = true;
            Widgets.Label(labelRect, $"{displayName}\n<size=10><color=#888888>{mod.PackageId}</color></size>");
            TooltipHandler.TipRegion(rowRect, $"{displayName}\n{mod.PackageId}");
            Text.WordWrap = true;
            Text.Anchor = TextAnchor.UpperLeft;

            if (isChecked) selectedMods.Add(mod);
            else selectedMods.Remove(mod);
        }

        private void DrawDeleteButton(Rect inRect)
        {
            Rect bottomBtnRect = new Rect(0, inRect.height - 40f, inRect.width, 40f);
            GUI.color = selectedMods.Count > 0 ? new Color(1f, 0.4f, 0.4f) : Color.grey;
            if (Widgets.ButtonText(bottomBtnRect, "ATC_ConfirmDelete_Btn".Translate(selectedMods.Count)))
            {
                if (selectedMods.Count > 0)
                {
                    ExecuteDelete(selectedMods.ToList());
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
            string translated = GetCachedTranslatedModName(mod);
            if (string.IsNullOrWhiteSpace(translated) ||
                string.Equals(translated.Trim(), mod.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return mod.Name;
            }

            return $"{translated} / {mod.Name}";
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
                    Verse.Log.Warning($"[AutoTranslationCore] Delete-window mod-name translation failed: {ex.Message}");
                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        ModNameTranslationCache.MarkFailed(pending);
                        ModNameTranslationCache.ReleaseQueued(pending);
                        isTranslatingModNames = false;
                    });
                }
            });
        }

        private void ExecuteDelete(List<ModMetaData> modsToDelete)
        {
            try
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string langsPath = Path.Combine(packPath, "Languages");
                if (!Directory.Exists(langsPath)) return;

                int deletedFiles = 0;
                string[] allXmls = Directory.GetFiles(langsPath, "*.xml", SearchOption.AllDirectories);

                foreach (var mod in modsToDelete)
                {
                    string id1 = (mod.PackageId ?? "").ToLowerInvariant();
                    string id2 = (mod.PackageId ?? "").Replace(".", "_").ToLowerInvariant();

                    foreach (var file in allXmls)
                    {
                        string fileName = Path.GetFileName(file).ToLowerInvariant();
                        if (fileName.StartsWith(id1 + "_") ||
                            fileName.StartsWith(id1 + ".") ||
                            fileName.StartsWith(id2 + "_") ||
                            fileName.StartsWith(id2 + "."))
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                            deletedFiles++;
                        }
                    }

                    AutoTranslatorMod.Settings.ModLastVerifiedTimes.Remove(mod.PackageId);
                    AutoTranslatorMod.Settings.ModLastVerifiedFingerprints.Remove(mod.PackageId);
                }

                LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();
                ModUpdateDetector.ClearStatusCache();

                string logMsg = "ATC_Log_DeleteTransSuccess".Translate(modsToDelete.Count, deletedFiles);
                AutoTranslatorSettings.AddLog(logMsg);
                Log.Message($"[AutoTranslationCore] {logMsg}");
                Messages.Message("ATC_Message_DeleteTransSuccess".Translate(deletedFiles), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                AutoTranslatorSettings.AddErrorLog("ATC_Message_DeleteTransError".Translate(ex.Message));
                Log.Warning($"[AutoTranslationCore] Delete failed: {ex.Message}");
            }
        }

        public static bool IsCodeOnlyMod(ModMetaData mod)
        {
            if (mod == null || mod.RootDir == null) return true;

            var folders = mod.LoadFoldersForVersion(VersionControl.CurrentVersionStringWithoutBuild);
            var pathsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (folders != null && folders.Any())
            {
                foreach (var folder in folders)
                {
                    pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, folder.folderName));
                }
            }

            pathsToCheck.Add(mod.RootDir.FullName);
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, VersionControl.CurrentVersionStringWithoutBuild));
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "1.5"));
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "1.4"));
            pathsToCheck.Add(Path.Combine(mod.RootDir.FullName, "Common"));

            foreach (var basePath in pathsToCheck)
            {
                if (!Directory.Exists(basePath)) continue;

                string defPath = Path.Combine(basePath, "Defs");
                string patchPath = Path.Combine(basePath, "Patches");
                string langPath = Path.Combine(basePath, "Languages");

                if (Directory.Exists(defPath) || Directory.Exists(patchPath) || Directory.Exists(langPath))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
