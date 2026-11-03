using System;
using System.Collections.Generic;
using System.Linq;
using AutoTranslator_Core.Workflow;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    internal sealed class CloudUploadSelectionItem
    {
        internal string PackageId;
        internal string DisplayName;
        internal long TranslationCount;
    }

    internal sealed class Window_CloudUploadSelection : Window
    {
        private readonly List<CloudUploadSelectionItem> _items;
        private readonly string _languageFolder;
        private readonly string _description;
        private readonly Func<ICollection<string>, bool> _upload;
        private readonly HashSet<string> _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Vector2 _scroll;
        private string _search = string.Empty;
        private List<CloudModRecord> _registrySnapshot;
        private HashSet<string> _cloudPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public override Vector2 InitialSize => new Vector2(1000f, 720f);

        internal Window_CloudUploadSelection(IEnumerable<CloudUploadSelectionItem> items,
            string languageFolder, string description, Func<ICollection<string>, bool> upload)
        {
            _items = items.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            _languageFolder = languageFolder;
            _description = description;
            _upload = upload;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        private static string T(string chinese, string english) => AutoTranslatorMod.WfText(chinese, english);

        private static bool RegistryReady => AutoTranslatorSettings.HasFetchedCloudThisSession &&
            !AutoTranslatorSettings.IsFetchingCloud && !AutoTranslatorSettings.CloudConnectionFailed;

        private HashSet<string> GetCloudPackages()
        {
            if (ReferenceEquals(_registrySnapshot, AutoTranslatorSettings.CloudRegistry)) return _cloudPackages;
            _registrySnapshot = AutoTranslatorSettings.CloudRegistry;
            _cloudPackages = new HashSet<string>((_registrySnapshot ?? new List<CloudModRecord>())
                .Where(record => record != null && string.Equals(record.Language, _languageFolder,
                    StringComparison.OrdinalIgnoreCase))
                .Select(record => record.PackageId).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            return _cloudPackages;
        }

        public override void DoWindowContents(Rect rect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, rect.width - 35f, 34f), T("选择要上传的译文", "Choose translations to upload"));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 42f, rect.width, 48f), _description);

            bool ready = RegistryReady;
            HashSet<string> cloudPackages = ready ? GetCloudPackages() : new HashSet<string>();
            if (WorkflowUiStyle.Button(new Rect(0f, 96f, 120f, 32f), T("全选", "Select all"), WorkflowButtonStyle.Quiet))
            {
                _selected.Clear();
                foreach (CloudUploadSelectionItem item in _items) _selected.Add(item.PackageId);
            }
            Rect missingButton = new Rect(132f, 96f, 260f, 32f);
            if (WorkflowUiStyle.Button(missingButton, T("只选云端暂无译文", "Select only missing from cloud"),
                    WorkflowButtonStyle.Quiet, ready))
            {
                _selected.Clear();
                foreach (CloudUploadSelectionItem item in _items)
                    if (!cloudPackages.Contains(item.PackageId)) _selected.Add(item.PackageId);
            }
            TooltipHandler.TipRegion(missingButton, ready
                ? T("只选云端还没有当前目标语言译文的 Mod。", "Select only mods without cloud translations in this target language.")
                : T("尚未取得云端清单，暂时不能判断哪些 Mod 没有云端译文。", "The cloud list is not available yet; absence cannot be determined."));
            Rect refreshButton = new Rect(rect.width - 156f, 96f, 156f, 32f);
            if (WorkflowUiStyle.Button(refreshButton, T("刷新云端状态", "Refresh cloud status"),
                    WorkflowButtonStyle.Quiet, !AutoTranslatorSettings.IsFetchingCloud))
                AutoTranslatorMod.StartCloudRegistryFetch();

            _search = Widgets.TextField(new Rect(0f, 140f, 340f, 30f), _search);
            TooltipHandler.TipRegion(new Rect(0f, 140f, 340f, 30f), T("搜索 Mod 名称或包 ID", "Search mod name or package ID"));
            GUI.color = ready ? WorkflowUiStyle.MutedText : WorkflowUiStyle.WarningText;
            Widgets.Label(new Rect(352f, 144f, rect.width - 352f, 26f), ready
                ? T("云端状态按当前目标语言判断。", "Cloud status uses the selected target language.")
                : AutoTranslatorSettings.IsFetchingCloud
                    ? T("正在读取云端状态……", "Loading cloud status...")
                    : T("云端状态未知；仍可手动选择上传。", "Cloud status is unknown; you can still select mods manually."));
            GUI.color = Color.white;

            List<CloudUploadSelectionItem> visible = _items.Where(item => string.IsNullOrWhiteSpace(_search) ||
                ((item.DisplayName ?? string.Empty) + " " + item.PackageId).IndexOf(_search,
                    StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rect list = new Rect(0f, 180f, rect.width, rect.height - 266f);
            Rect view = new Rect(0f, 0f, list.width - 18f, Math.Max(list.height, visible.Count * 58f));
            Widgets.BeginScrollView(list, ref _scroll, view);
            for (int index = 0; index < visible.Count; index++)
            {
                CloudUploadSelectionItem item = visible[index];
                Rect row = new Rect(0f, index * 58f, view.width, 56f);
                if (row.yMax < _scroll.y || row.y > _scroll.y + list.height) continue;
                if (_selected.Contains(item.PackageId)) Widgets.DrawBoxSolid(row, WorkflowUiStyle.Selection);
                else Widgets.DrawHighlightIfMouseover(row);
                bool selected = _selected.Contains(item.PackageId);
                bool previous = selected;
                Widgets.Checkbox(new Vector2(row.x + 6f, row.y + 15f), ref selected, 24f);
                if (selected != previous)
                {
                    if (selected) _selected.Add(item.PackageId); else _selected.Remove(item.PackageId);
                }
                Rect details = new Rect(row.x + 40f, row.y, row.width - 40f, row.height);
                Widgets.Label(new Rect(details.x, row.y + 3f, details.width - 280f, 26f), item.DisplayName);
                Text.Font = GameFont.Tiny;
                GUI.color = WorkflowUiStyle.MutedText;
                Widgets.Label(new Rect(details.x, row.y + 30f, details.width - 280f, 22f), item.PackageId);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(row.xMax - 272f, row.y + 4f, 125f, 26f),
                    item.TranslationCount.ToString("N0") + T(" 条译文", " entries"));
                Widgets.Label(new Rect(row.xMax - 145f, row.y + 4f, 140f, 44f),
                    !ready ? T("云端状态未知", "Cloud status unknown")
                        : cloudPackages.Contains(item.PackageId) ? T("云端已有译文", "Available in cloud")
                        : T("云端暂无译文", "Missing from cloud"));
                if (Widgets.ButtonInvisible(details))
                {
                    if (!_selected.Add(item.PackageId)) _selected.Remove(item.PackageId);
                }
            }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(0f, rect.height - 78f, rect.width, 25f),
                T("已选 ", "Selected ") + _selected.Count + " / " + _items.Count + T(" 个 Mod", " mods"));
            bool idle = !AutoTranslatorSettings.IsRunning;
            if (WorkflowUiStyle.Button(new Rect(rect.width - 280f, rect.height - 40f, 150f, 34f),
                    T("上传所选译文", "Upload selected"), WorkflowButtonStyle.Primary, _selected.Count > 0 && idle))
            {
                if (_upload(_selected.ToList())) Close();
            }
            if (WorkflowUiStyle.Button(new Rect(rect.width - 118f, rect.height - 40f, 118f, 34f),
                    T("取消", "Cancel"), WorkflowButtonStyle.Quiet)) Close();
        }
    }
}
