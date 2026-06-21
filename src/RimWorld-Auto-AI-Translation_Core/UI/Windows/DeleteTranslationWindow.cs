using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
// 這個檔案負責刪除翻譯的確認與執行視窗。
// EN: This file draws and executes the delete-translation confirmation window.

namespace AutoTranslator_Core
{
    // 這個類別負責 刪除翻譯視窗 的主要流程與狀態。
    // EN: This class manages the main workflow and state for DeleteTranslationWindow.
    public class DeleteTranslationWindow : Window
    {
        // 這個常數定義 RowHeight 的固定值。
        // EN: This constant defines the fixed value for row height.
        private const float RowHeight = 56f;

        // 這個欄位保存 searchText 的執行狀態或快取資料。
        // EN: This field stores search text runtime state or cached data.
        private string searchText = "";
        // 這個欄位保存 scrollPos 的執行狀態或快取資料。
        // EN: This field stores scroll pos runtime state or cached data.
        private Vector2 scrollPos = Vector2.zero;
        private readonly HashSet<ModMetaData> selectedMods = new HashSet<ModMetaData>();
        // 這個欄位保存 drag目標狀態 的執行狀態或快取資料。
        // EN: This field stores drag target state runtime state or cached data.
        private bool? dragTargetState = null;
        // 這個欄位保存 isTranslating模組Names 的執行狀態或快取資料。
        // EN: This field stores is translating mod names runtime state or cached data.
        private static bool isTranslatingModNames = false;
        private List<ModMetaData> availableMods = new List<ModMetaData>();
        private List<ModMetaData> cachedDisplayMods = null;
        private string cachedSearchText = null;
        private int cachedAvailableModCount = -1;
        private bool isLoadingAvailableMods = false;
        private bool isDeleting = false;
        private string availableModsError = "";
        private int availableModsLoadGeneration = 0;
        private int availableModsValidCacheVersion = -1;

        private sealed class DeleteModCandidate
        {
            public ModMetaData Mod;
            public string PackageId;
            public string ModName;
            public string NormalizedPackageId;
        }

        // 這個屬性提供 InitialSize 的讀寫或計算結果。
        // EN: This method handles vector2.
        public override Vector2 InitialSize => new Vector2(600f, 700f);

        // 這個方法負責刪除 翻譯視窗 資料。
        // EN: This constructor initializes delete translation window.
        public DeleteTranslationWindow()
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            QueueAvailableModsRefresh();
        }

        // 這個方法負責處理 Do視窗Contents 相關流程。
        // EN: This method handles do window contents.
        public override void DoWindowContents(Rect inRect)
        {
            bool previousBypass = Patch_GUI_Label_GUIContent.BypassInterceptor;
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 40f), "ATC_DeleteModTrans_Title".Translate());
                Text.Font = GameFont.Small;

                if (isLoadingAvailableMods && availableMods.Count == 0)
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(0, 45f, inRect.width, 30f), "ATC_CheckingModStatus".Translate());
                    GUI.color = Color.white;
                    return;
                }

                if (!string.IsNullOrEmpty(availableModsError))
                {
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    Widgets.Label(new Rect(0, 45f, inRect.width, 45f), availableModsError);
                    GUI.color = Color.white;
                }

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

        // 這個方法負責取得 Display模組 資料。
        // EN: This method gets display mods.
        private List<ModMetaData> GetDisplayMods()
        {
            string currentSearch = searchText ?? "";
            if (cachedDisplayMods != null &&
                cachedAvailableModCount == availableMods.Count &&
                string.Equals(cachedSearchText, currentSearch, StringComparison.Ordinal))
            {
                return cachedDisplayMods;
            }

            IEnumerable<ModMetaData> mods = availableMods;

            if (!string.IsNullOrEmpty(searchText))
            {
                string searchLower = searchText.ToLowerInvariant();
                mods = mods.Where(m =>
                    (m.Name ?? "").ToLowerInvariant().Contains(searchLower) ||
                    (m.PackageId ?? "").ToLowerInvariant().Contains(searchLower) ||
                    GetCachedTranslatedModName(m).ToLowerInvariant().Contains(searchLower));
            }

            cachedDisplayMods = mods.ToList();
            cachedSearchText = currentSearch;
            cachedAvailableModCount = availableMods.Count;
            return cachedDisplayMods;
        }

        private void QueueAvailableModsRefresh()
        {
            if (isLoadingAvailableMods) return;
            AutoTranslatorMod.GetValidModsCached();
            if (AutoTranslatorMod.IsValidModsCacheRefreshing &&
                availableModsValidCacheVersion != AutoTranslatorMod.ValidModsCacheVersion)
            {
                isLoadingAvailableMods = true;
                availableModsError = "";
                Task.Run(() =>
                {
                    while (AutoTranslatorMod.IsValidModsCacheRefreshing)
                    {
                        System.Threading.Thread.Sleep(50);
                    }

                    ATC_Dispatcher.RunOnMainThread(() =>
                    {
                        isLoadingAvailableMods = false;
                        QueueAvailableModsRefresh();
                    });
                });
                return;
            }

            TargetLanguage targetLang = AutoTranslatorMod.Settings.TargetLang;
            List<DeleteModCandidate> candidates = AutoTranslatorMod.GetValidModsCached()
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.PackageId))
                .Select(m => new DeleteModCandidate
                {
                    Mod = m,
                    PackageId = m.PackageId,
                    ModName = m.Name ?? "",
                    NormalizedPackageId = ModUpdateDetector.NormalizePackageIdForTranslationFileLookup(m.PackageId)
                })
                .ToList();

            int generation = ++availableModsLoadGeneration;
            int validCacheVersion = AutoTranslatorMod.ValidModsCacheVersion;
            isLoadingAvailableMods = true;
            availableModsError = "";

            Task.Run(() =>
            {
                List<ModMetaData> loaded = new List<ModMetaData>();
                string error = "";

                try
                {
                    HashSet<string> packagesWithFiles =
                        ModUpdateDetector.GetPackageIdsWithLocalTranslationFilesForKnownPackages(
                            candidates.Select(c => c.PackageId),
                            targetLang);

                    loaded = candidates
                        .Where(c => packagesWithFiles.Contains(c.NormalizedPackageId))
                        .OrderBy(c => c.ModName)
                        .Select(c => c.Mod)
                        .ToList();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Verse.Log.Warning($"[AutoTranslationCore] Delete-window local translation scan failed: {ex.Message}");
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    if (generation != availableModsLoadGeneration) return;
                    availableMods = loaded ?? new List<ModMetaData>();
                    cachedDisplayMods = null;
                    cachedSearchText = null;
                    cachedAvailableModCount = -1;
                    availableModsValidCacheVersion = validCacheVersion;
                    availableModsError = error;
                    isLoadingAvailableMods = false;
                });
            });
        }

        // 這個方法負責繪製 SelectVisibleButton 介面。
        // EN: This method draws select visible button.
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

        // 這個方法負責繪製 模組List 介面。
        // EN: This method draws mod list.
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

        // 這個方法負責繪製 模組Row 介面。
        // EN: This method draws mod row.
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

        // 這個方法負責繪製 刪除Button 介面。
        // EN: This method draws delete button.
        private void DrawDeleteButton(Rect inRect)
        {
            Rect bottomBtnRect = new Rect(0, inRect.height - 40f, inRect.width, 40f);
            GUI.color = selectedMods.Count > 0 && !isDeleting ? new Color(1f, 0.4f, 0.4f) : Color.grey;
            string label = isDeleting
                ? "ATC_CheckingModStatus".Translate().ToString()
                : "ATC_ConfirmDelete_Btn".Translate(selectedMods.Count).ToString();
            if (Widgets.ButtonText(bottomBtnRect, label))
            {
                if (selectedMods.Count > 0 && !isDeleting)
                {
                    ExecuteDelete(selectedMods.ToList());
                }
            }
            GUI.color = Color.white;
        }

        // 這個方法負責取得 CachedTranslated模組名稱 資料。
        // EN: This method gets cached translated mod name.
        private static string GetCachedTranslatedModName(ModMetaData mod)
        {
            if (mod == null) return "";
            return ModNameTranslationCache.TryGet(mod, out string translated) ? translated : "";
        }

        // 這個方法負責取得 Display模組名稱 資料。
        // EN: This method gets display mod name.
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

        // 這個方法負責排入 Visible模組名稱Translations 佇列。
        // EN: This method queues visible mod name translations.
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

        // 這個方法負責執行 刪除 動作。
        // EN: This method executes delete.
        private void ExecuteDelete(List<ModMetaData> modsToDelete)
        {
            if (isDeleting) return;
            isDeleting = true;
            List<AutoTranslatorScanner.LocalTranslationDeleteTarget> targets = (modsToDelete ?? new List<ModMetaData>())
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.PackageId))
                .Select(m => new AutoTranslatorScanner.LocalTranslationDeleteTarget
                {
                    PackageId = m.PackageId,
                    ModName = m.Name
                })
                .ToList();

            Task.Run(() =>
            {
                AutoTranslatorScanner.LocalTranslationDeleteResult result = null;
                Exception failure = null;

                try
                {
                    result = AutoTranslatorScanner.DeleteLocalTranslationFiles(targets);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    isDeleting = false;

                    if (failure != null)
                    {
                        AutoTranslatorSettings.AddErrorLog("ATC_Message_DeleteTransError".Translate(failure.Message));
                        Log.Warning($"[AutoTranslationCore] Delete failed: {failure.Message}");
                        return;
                    }

                    if (result != null && result.HasErrors)
                    {
                        string error = string.IsNullOrEmpty(result.FirstError) ? "Unknown error" : result.FirstError;
                        AutoTranslatorSettings.AddErrorLog("ATC_Message_DeleteTransError".Translate(error));
                        Messages.Message("ATC_Message_DeleteTransError".Translate(error), MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    int deletedFiles = result != null ? result.DeletedFiles : 0;
                    Messages.Message("ATC_Message_DeleteTransSuccess".Translate(deletedFiles), MessageTypeDefOf.PositiveEvent, false);
                    selectedMods.Clear();
                    QueueAvailableModsRefresh();
                    Close();
                });
            });
        }

        // 這個方法負責判斷 IsCodeOnly模組 條件是否成立。
        // EN: This method checks is code only mod.
        public static bool IsCodeOnlyMod(ModMetaData mod)
        {
            return !AutoTranslatorScanner.HasScannableTranslationSources(mod);
        }
    }
}
