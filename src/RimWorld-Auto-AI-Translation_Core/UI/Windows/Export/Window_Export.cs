using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個檔案負責導出模組選取與確認視窗。
// EN: This file draws the export selection and confirmation window.

namespace AutoTranslator_Core
{
    // 這個類別負責 視窗導出 的主要流程與狀態。
    // EN: This class manages the main workflow and state for Window_Export.
    public class Window_Export : Window
    {
        // 這個欄位保存 searchText 的執行狀態或快取資料。
        // EN: This field stores search text runtime state or cached data.
        private string _searchText = "";
        // 這個欄位保存 scrollPos 的執行狀態或快取資料。
        // EN: This field stores scroll pos runtime state or cached data.
        private Vector2 _scrollPos = Vector2.zero;
        private HashSet<string> _selectedPackageIds = new HashSet<string>();
        // 這個欄位保存 available模組 的執行狀態或快取資料。
        // EN: This field stores available mods runtime state or cached data.
        private List<ExportableModInfo> _availableMods;

        // 這個屬性提供 InitialSize 的讀寫或計算結果。
        // EN: This method handles vector2.
        public override Vector2 InitialSize => new Vector2(750f, 700f);

        // 這個方法負責處理 視窗導出 相關流程。
        // EN: This constructor initializes window export.
        public Window_Export()
        {
            doCloseButton = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            _availableMods = ScanAvailableMods();
        }

        // 這個方法負責處理 Do視窗Contents 相關流程。
        // EN: This method handles do window contents.
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f),
                "ATC_ExportWindow_Title".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);


            if (_availableMods.Count == 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(0, 50f, inRect.width, 30f),
                    "ATC_ExportWindow_NoTranslationFound".Translate());
                GUI.color = Color.white;
                return;
            }


            Rect searchRect = new Rect(0, 45f, inRect.width, 30f);
            _searchText = Widgets.TextField(searchRect, _searchText);
            if (string.IsNullOrEmpty(_searchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height),
                    "  " + "ATC_ExportWindow_SearchHint".Translate());
                GUI.color = Color.white;
            }


            var filtered = string.IsNullOrEmpty(_searchText)
                ? _availableMods
                : _availableMods.Where(m =>
                    m.ModName.ToLower().Contains(_searchText.ToLower()) ||
                    m.PackageId.ToLower().Contains(_searchText.ToLower())).ToList();


            Widgets.Label(new Rect(0, 80f, inRect.width, 22f),
                "ATC_ExportWindow_DetectedCount".Translate(_availableMods.Count));

            GUI.color = new Color(1f, 0.8f, 0.4f);
            Widgets.Label(new Rect(0, 102f, inRect.width, 22f),
                "ATC_ExportWindow_NoFullSelectNotice".Translate());
            GUI.color = Color.white;

            Widgets.DrawLineHorizontal(0, 130f, inRect.width);


            float listY = 140f;
            float listHeight = inRect.height - 240f;
            Rect listOutRect = new Rect(0, listY, inRect.width, listHeight);
            float rowHeight = 56f;
            Rect viewRect = new Rect(0, 0, listOutRect.width - 20f, filtered.Count * rowHeight);

            Widgets.BeginScrollView(listOutRect, ref _scrollPos, viewRect);
            float yCursor = 0f;
            foreach (var mod in filtered)
            {
                Rect rowRect = new Rect(0, yCursor, viewRect.width, rowHeight - 4f);
                bool isSelected = _selectedPackageIds.Contains(mod.PackageId);

                Widgets.DrawHighlightIfMouseover(rowRect);
                if (isSelected) Widgets.DrawHighlight(rowRect);


                if (Mouse.IsOver(rowRect) && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    if (isSelected) _selectedPackageIds.Remove(mod.PackageId);
                    else _selectedPackageIds.Add(mod.PackageId);
                    Event.current.Use();
                }


                Vector2 checkPos = new Vector2(rowRect.x + 5f, rowRect.y + (rowRect.height - 24f) / 2f);
                Widgets.CheckboxDraw(checkPos.x, checkPos.y, isSelected, false, 24f, null, null);


                Text.Anchor = TextAnchor.UpperLeft;
                Rect nameRect = new Rect(rowRect.x + 40f, rowRect.y + 4f, rowRect.width - 50f, 22f);
                Widgets.Label(nameRect, $"<b>{mod.ModName}</b>  <color=#888888>({mod.PackageId})</color>");


                GUI.color = new Color(0.7f, 0.7f, 0.7f);
                Rect statRect = new Rect(rowRect.x + 40f, rowRect.y + 28f, rowRect.width - 50f, 22f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(statRect,
                    "ATC_ExportWindow_ModInfo_DefCount".Translate(mod.DefInjectedCount, mod.KeyedCount));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                yCursor += rowHeight;
            }
            Widgets.EndScrollView();


            float infoY = listY + listHeight + 10f;
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string folderName = $"RimWorld_Translations_{DateTime.Now:yyyy-MM-dd_HHmmss}";
            Widgets.Label(new Rect(0, infoY, inRect.width, 22f),
                "ATC_ExportWindow_OutputPath".Translate(Path.Combine(desktopPath, folderName)));


            CooldownState cooldown = ExportCooldownManager.GetCurrentState();
            GUI.color = cooldown.CanExport
                ? new Color(0.6f, 1f, 0.6f)
                : new Color(1f, 0.7f, 0.3f);
            Widgets.Label(new Rect(0, infoY + 22f, inRect.width, 22f),
                cooldown.GetDisplayText());
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(0, infoY + 44f, inRect.width, 22f),
                "ATC_Export_TodayCount".Translate(cooldown.TodayCount));

            GUI.color = new Color(1f, 0.7f, 0.3f);
            Widgets.Label(new Rect(0, infoY + 66f, inRect.width, 22f),
                "ATC_ExportWindow_WatermarkNotice".Translate());
            GUI.color = Color.white;


            float btnY = inRect.height - 45f;
            Rect btnRect = new Rect(0, btnY, inRect.width, 40f);


            bool canClickConfirm = cooldown.CanExport && _selectedPackageIds.Count > 0;
            if (canClickConfirm)
            {
                GUI.color = new Color(1f, 0.6f, 0.3f);
            }
            else
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
            }

            if (Widgets.ButtonText(btnRect,
                "ATC_ExportWindow_ConfirmBtn".Translate(_selectedPackageIds.Count)))
            {

                if (_selectedPackageIds.Count == 0)
                {
                    Messages.Message("ATC_ExportWindow_NoModSelected".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                }
                else if (_selectedPackageIds.Count > ExportCooldownManager.PER_EXPORT_MOD_LIMIT)
                {
                    Messages.Message("ATC_Export_TooManyAtOnce".Translate(_selectedPackageIds.Count),
                        MessageTypeDefOf.RejectInput, false);
                }
                else if (cooldown.DailyLimitReached)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "ATC_Export_TooManyToday".Translate(cooldown.TodayCount),
                        null, null, null, null,
                        "ATC_Export_CooldownDialogTitle".Translate()
                    ));
                    AutoTranslatorSettings.AddLog("ATC_Log_DailyLimitReached".Translate());
                }
                else if (!cooldown.CanExport)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "ATC_Export_CooldownDialogMsg".Translate(cooldown.RemainingSeconds),
                        null, null, null, null,
                        "ATC_Export_CooldownDialogTitle".Translate()
                    ));
                }
                else
                {
                    var modsToExport = _availableMods
                        .Where(m => _selectedPackageIds.Contains(m.PackageId))
                        .ToList();
                    Close();
                    ExportManager.ExecuteExport(modsToExport);
                }
            }
            GUI.color = Color.white;
        }


        // 這個方法負責掃描 Available模組 資料。
        // EN: This method scans available mods.
        private List<ExportableModInfo> ScanAvailableMods()
        {
            var result = new List<ExportableModInfo>();
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string langsPath = Path.Combine(packPath, "Languages");

            if (!Directory.Exists(langsPath)) return result;


            var modStats = new Dictionary<string, (int defCount, int keyedCount)>(StringComparer.OrdinalIgnoreCase);

            foreach (var langDir in Directory.GetDirectories(langsPath))
            {

                string defDir = Path.Combine(langDir, "DefInjected");
                if (Directory.Exists(defDir))
                {
                    foreach (var typeDir in Directory.GetDirectories(defDir))
                    {
                        foreach (var file in Directory.GetFiles(typeDir, "*.xml"))
                        {
                            string fileName = Path.GetFileNameWithoutExtension(file);
                            string packageId = ExtractPackageIdFromFileName(fileName);
                            if (string.IsNullOrEmpty(packageId)) continue;

                            int count = CountEntriesInXml(file);
                            if (!modStats.ContainsKey(packageId)) modStats[packageId] = (0, 0);
                            modStats[packageId] = (modStats[packageId].defCount + count, modStats[packageId].keyedCount);
                        }
                    }
                }


                string keyedDir = Path.Combine(langDir, "Keyed");
                if (Directory.Exists(keyedDir))
                {
                    foreach (var file in Directory.GetFiles(keyedDir, "*.xml"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string packageId = ExtractPackageIdFromFileName(fileName);
                        if (string.IsNullOrEmpty(packageId)) continue;

                        int count = CountEntriesInXml(file);
                        if (!modStats.ContainsKey(packageId)) modStats[packageId] = (0, 0);
                        modStats[packageId] = (modStats[packageId].defCount, modStats[packageId].keyedCount + count);
                    }
                }
            }


            foreach (var kv in modStats)
            {

                string normalizedId = kv.Key.Replace("_", ".").ToLower();
                var mod = ModLister.AllInstalledMods.FirstOrDefault(m =>
                    m.PackageId.ToLower() == normalizedId ||
                    m.PackageId.Replace(".", "_").ToLower() == kv.Key.ToLower());

                if (mod != null)
                {
                    result.Add(new ExportableModInfo
                    {
                        ModName = mod.Name,
                        PackageId = mod.PackageId,
                        PackageIdWithUnderscore = kv.Key,
                        ModRootDir = mod.RootDir.FullName,
                        DefInjectedCount = kv.Value.defCount,
                        KeyedCount = kv.Value.keyedCount
                    });
                }
                else
                {

                    result.Add(new ExportableModInfo
                    {
                        ModName = $"[已解除安裝] {kv.Key}",
                        PackageId = normalizedId,
                        PackageIdWithUnderscore = kv.Key,
                        ModRootDir = null,
                        DefInjectedCount = kv.Value.defCount,
                        KeyedCount = kv.Value.keyedCount
                    });
                }
            }

            return result.OrderBy(m => m.ModName).ToList();
        }

        // 這個方法負責處理 ExtractPackageIdFromFile名稱 相關流程。
        // EN: This method handles extract package id from file name.
        private static string ExtractPackageIdFromFileName(string fileName)
        {


            int idx = fileName.IndexOf("_AutoTranslated", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) return fileName.Substring(0, idx);


            int lastIdx = fileName.LastIndexOf('_');
            if (lastIdx > 0) return fileName.Substring(0, lastIdx);

            return fileName;
        }

        // 這個方法負責處理 CountEntriesInXml 相關流程。
        // EN: This method handles count entries in XML.
        private static int CountEntriesInXml(string filePath)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.Load(filePath);
                return doc.DocumentElement?.ChildNodes.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
