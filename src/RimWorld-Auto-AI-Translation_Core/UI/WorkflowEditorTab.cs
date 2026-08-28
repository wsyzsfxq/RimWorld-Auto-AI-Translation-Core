using AutoTranslator_Core.Workflow;
using AutoTranslator_Core.Workflow.Analysis;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AutoTranslator_Core
{
    public partial class AutoTranslatorMod : Mod
    {
        private enum WorkflowEditorClassificationFilter
        {
            All,
            NeedsTranslation,
            Undetermined,
            NoTranslationNeeded
        }

        private enum WorkflowEditorTranslationFilter
        {
            All,
            Untranslated,
            Translated,
            Failed
        }

        private static string _workflowEditorModSearch = string.Empty;
        private static string _workflowEditorCandidateSearch = string.Empty;
        private static Vector2 _workflowEditorModScroll = Vector2.zero;
        private static Vector2 _workflowEditorCandidateScroll = Vector2.zero;
        private static WorkflowEditorClassificationFilter _workflowEditorClassificationFilter;
        private static WorkflowEditorTranslationFilter _workflowEditorTranslationFilter;
        private static string _workflowEditorSelectedModIdentity = string.Empty;
        private static string _workflowEditorSelectedModPackageId = string.Empty;
        private static string _workflowEditorSelectedModDisplayName = string.Empty;
        private static WorkflowEditorSnapshot _workflowEditorSnapshot;
        private static Task<WorkflowEditorSnapshot> _workflowEditorLoadTask;
        private static string _workflowEditorLoadError = string.Empty;
        private static string _workflowEditorSelectedCandidateId = string.Empty;
        private static string _workflowEditorTranslationBuffer = string.Empty;
        private static bool _workflowEditorRefreshRequested;
        private static bool _workflowEditorPreviouslyBusy;
        private static bool _workflowEditorSuppressNextIdleRefresh;
        private static int _workflowEditorPageIndex;
        private const int WorkflowEditorPageSize = 100;

        private void DrawWorkflowEditorTab(Listing_Standard listing, Rect viewRect)
        {
            EndWorkflowSelectionGesture();
            UpdateWorkflowEditorSnapshot();
            float height = Mathf.Max(540f, viewRect.height - listing.CurHeight - 4f);
            Rect full = listing.GetRect(height);
            const float gap = 10f;
            float leftWidth = Mathf.Clamp(full.width * 0.25f, 220f, 300f);
            Rect left = new Rect(full.x, full.y, leftWidth, full.height);
            Rect right = new Rect(left.xMax + gap, full.y, full.width - leftWidth - gap, full.height);
            DrawWorkflowEditorModPane(left);
            DrawWorkflowEditorCandidatePane(right);
        }

        private void DrawWorkflowEditorModPane(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, WorkflowUiStyle.Panel);
            WorkflowUiStyle.DrawBorder(rect, new Color(0.28f, 0.31f, 0.33f));
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x + 7f, rect.y + 6f, rect.width - 14f, 19f),
                WfText("选择要编辑的 Mod", "Select a mod to edit"));
            Rect search = new Rect(rect.x + 6f, rect.y + 28f, rect.width - 12f, 27f);
            _workflowEditorModSearch = Widgets.TextField(search, _workflowEditorModSearch ?? string.Empty);

            List<WorkflowModMetadataSnapshot> mods = _workflowModMetadataCache
                .Where(mod => mod != null && !mod.IsProtectedSystemPackage &&
                    (string.IsNullOrWhiteSpace(_workflowEditorModSearch) ||
                     (mod.DisplayName ?? string.Empty).IndexOf(_workflowEditorModSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (mod.PackageId ?? string.Empty).IndexOf(_workflowEditorModSearch, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(mod => mod.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            Rect outRect = new Rect(rect.x + 4f, search.yMax + 5f, rect.width - 8f, rect.yMax - search.yMax - 9f);
            const float rowHeight = 38f;
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, mods.Count * rowHeight));
            Widgets.BeginScrollView(outRect, ref _workflowEditorModScroll, view);
            for (int i = 0; i < mods.Count; i++)
            {
                WorkflowModMetadataSnapshot mod = mods[i];
                Rect row = new Rect(0f, i * rowHeight, view.width, rowHeight);
                if (row.yMax < _workflowEditorModScroll.y || row.y > _workflowEditorModScroll.y + outRect.height) continue;
                bool selected = string.Equals(
                    _workflowEditorSelectedModIdentity, mod.ModIdentity, StringComparison.Ordinal);
                if (selected) Widgets.DrawBoxSolid(row, WorkflowUiStyle.Selection);
                else if ((i & 1) == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.025f));
                Widgets.DrawLineHorizontal(row.x, row.yMax - 1f, row.width);
                Widgets.Label(new Rect(row.x + 5f, row.y + 3f, row.width - 10f, 17f), mod.DisplayName);
                GUI.color = Color.grey;
                Widgets.Label(new Rect(row.x + 5f, row.y + 19f, row.width - 10f, 16f), mod.PackageId ?? string.Empty);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(row) &&
                    _workflowModsByIdentity.TryGetValue(mod.ModIdentity, out ModMetaData boundMod))
                    SelectWorkflowEditorMod(boundMod, mod.ModIdentity);
            }
            Widgets.EndScrollView();
            Text.Font = GameFont.Small;
        }

        private void DrawWorkflowEditorCandidatePane(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, WorkflowUiStyle.Panel);
            WorkflowUiStyle.DrawBorder(rect, new Color(0.28f, 0.31f, 0.33f));
            if (string.IsNullOrWhiteSpace(_workflowEditorSelectedModIdentity))
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect.ContractedBy(20f), WfText(
                    "请先从左侧选择一个 Mod。\n编辑器会读取新数据库中的条目、分类层和译文状态。",
                    "Select a mod on the left.\nThe editor reads candidates, classification layers, and translation state from the new database."));
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Rect title = new Rect(rect.x + 7f, rect.y + 5f, rect.width - 14f, 22f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(title, (_workflowEditorSnapshot?.DisplayName ?? _workflowEditorSelectedModDisplayName) +
                                 "　" + (_workflowEditorSnapshot?.PackageId ?? _workflowEditorSelectedModPackageId));
            float filterY = title.yMax + 3f;
            Rect search = new Rect(rect.x + 7f, filterY, rect.width * 0.46f, 27f);
            string nextSearch = Widgets.TextField(search, _workflowEditorCandidateSearch ?? string.Empty);
            if (!string.Equals(nextSearch, _workflowEditorCandidateSearch, StringComparison.Ordinal))
            {
                _workflowEditorCandidateSearch = nextSearch;
                _workflowEditorPageIndex = 0;
                RequestWorkflowEditorPage();
            }
            Rect classification = new Rect(search.xMax + 7f, filterY, rect.width * 0.25f, 27f);
            Rect translation = new Rect(classification.xMax + 7f, filterY, rect.xMax - classification.xMax - 14f, 27f);
            if (WorkflowUiStyle.Button(classification, GetEditorClassificationFilterLabel() + "  ▾",
                    WorkflowButtonStyle.Dropdown, true, GameFont.Tiny))
                OpenWorkflowEditorClassificationFilterMenu();
            if (WorkflowUiStyle.Button(translation, GetEditorTranslationFilterLabel() + "  ▾",
                    WorkflowButtonStyle.Dropdown, true, GameFont.Tiny))
                OpenWorkflowEditorTranslationFilterMenu();

            float detailHeight = 250f;
            Rect list = new Rect(rect.x + 6f, search.yMax + 5f, rect.width - 12f,
                rect.height - (search.yMax - rect.y) - detailHeight - 12f);
            DrawWorkflowEditorCandidateList(list);
            Rect detail = new Rect(rect.x + 6f, list.yMax + 6f, rect.width - 12f, detailHeight);
            DrawWorkflowEditorDetail(detail);
            Text.Font = GameFont.Small;
        }

        private void DrawWorkflowEditorCandidateList(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, WorkflowUiStyle.Panel);
            WorkflowUiStyle.DrawBorder(rect, new Color(0.28f, 0.31f, 0.33f));
            const float headerHeight = 22f;
            const float paginationHeight = 34f;
            const float rowHeight = 34f;
            Rect header = new Rect(rect.x, rect.y, rect.width, headerHeight);
            Widgets.DrawBoxSolid(header, WorkflowUiStyle.Header);
            float[] widths = { rect.width * 0.38f, rect.width * 0.17f, rect.width * 0.19f, rect.width * 0.26f - 18f };
            string[] labels = { WfText("条目／位置", "Entry / location"), WfText("有效分类", "Classification"), WfText("翻译状态", "Translation"), WfText("来源文件", "Source file") };
            float x = header.x + 5f;
            for (int i = 0; i < labels.Length; i++)
            {
                Widgets.Label(new Rect(x, header.y + 3f, widths[i], 17f), labels[i]);
                x += widths[i];
            }

            List<WorkflowCandidateEditorItem> items = GetFilteredWorkflowEditorCandidates();
            Rect outRect = new Rect(
                rect.x + 2f,
                header.yMax,
                rect.width - 4f,
                rect.height - headerHeight - paginationHeight - 3f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, items.Count * rowHeight));
            Widgets.BeginScrollView(outRect, ref _workflowEditorCandidateScroll, view);
            for (int i = 0; i < items.Count; i++)
            {
                WorkflowCandidateEditorItem item = items[i];
                Rect row = new Rect(0f, i * rowHeight, view.width, rowHeight);
                if (row.yMax < _workflowEditorCandidateScroll.y || row.y > _workflowEditorCandidateScroll.y + outRect.height) continue;
                if (string.Equals(_workflowEditorSelectedCandidateId, item.CandidateId, StringComparison.Ordinal))
                    Widgets.DrawBoxSolid(row, WorkflowUiStyle.Selection);
                else if ((i & 1) == 1) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.025f));
                Widgets.DrawLineHorizontal(row.x, row.yMax - 1f, row.width);
                string[] values =
                {
                    item.SourceDomain + " · " + item.LogicalLocator,
                    GetClassificationLabel(item.EffectiveClassification) + " · " + GetLayerLabel(item.EffectiveLayer),
                    GetTranslationStateLabel(item),
                    item.SourceFileRelativePath + (item.SourceLineNumber > 0 ? ":" + item.SourceLineNumber : string.Empty)
                };
                float cellX = row.x + 5f;
                for (int column = 0; column < values.Length; column++)
                {
                    Widgets.Label(new Rect(cellX, row.y + 8f, widths[column], 18f), values[column]);
                    cellX += widths[column];
                }
                if (Widgets.ButtonInvisible(row)) SelectWorkflowEditorCandidate(item);
            }
            if (items.Count == 0)
            {
                string message = !string.IsNullOrWhiteSpace(_workflowEditorLoadError)
                    ? WfText("读取失败：", "Load failed: ") + _workflowEditorLoadError
                    : _workflowEditorLoadTask != null
                        ? WfText("正在读取候选条目……", "Loading candidates...")
                        : WfText("当前筛选范围没有条目。", "No candidates match the current filters.");
                Widgets.Label(new Rect(8f, 9f, view.width - 16f, 25f), message);
            }
            Widgets.EndScrollView();
            DrawWorkflowEditorPagination(new Rect(
                rect.x + 2f,
                rect.yMax - paginationHeight - 1f,
                rect.width - 4f,
                paginationHeight));
        }

        private static void DrawWorkflowEditorPagination(Rect rect)
        {
            int totalItems = _workflowEditorSnapshot?.TotalCount ?? 0;
            int pageCount = totalItems == 0
                ? 0
                : (totalItems + WorkflowEditorPageSize - 1) / WorkflowEditorPageSize;
            int currentPage = pageCount == 0
                ? 0
                : Mathf.Clamp(_workflowEditorPageIndex, 0, pageCount - 1);

            Widgets.DrawBoxSolid(rect, WorkflowUiStyle.RaisedPanel);
            WorkflowUiStyle.DrawBorder(rect, new Color(0.28f, 0.31f, 0.33f));

            const float gap = 4f;
            const float counterWidth = 72f;
            const float edgeWidth = 30f;
            const float navWidth = 72f;
            const float pageWidth = 34f;
            int visiblePageCount = Math.Min(5, pageCount);
            float contentWidth = counterWidth + edgeWidth * 2f + navWidth * 2f +
                                 pageWidth * visiblePageCount + gap * (5 + visiblePageCount);
            float x = rect.x + Mathf.Max(4f, (rect.width - contentWidth) * 0.5f);
            float y = rect.y + 4f;
            const float height = 26f;

            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = WorkflowUiStyle.MutedText;
            Widgets.Label(new Rect(x, y, counterWidth, height),
                pageCount == 0 ? "0 / 0" : (currentPage + 1) + " / " + pageCount);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            x += counterWidth + gap;

            bool hasPrevious = currentPage > 0;
            bool hasNext = currentPage + 1 < pageCount;
            if (WorkflowUiStyle.Button(new Rect(x, y, edgeWidth, height), "«",
                    WorkflowButtonStyle.Quiet, hasPrevious, GameFont.Tiny))
                SetWorkflowEditorPage(0, pageCount);
            x += edgeWidth + gap;
            if (WorkflowUiStyle.Button(new Rect(x, y, navWidth, height), WfText("‹ 上一页", "‹ Previous"),
                    WorkflowButtonStyle.Quiet, hasPrevious, GameFont.Tiny))
                SetWorkflowEditorPage(currentPage - 1, pageCount);
            x += navWidth + gap;

            int firstVisiblePage = pageCount <= visiblePageCount
                ? 0
                : Mathf.Clamp(currentPage - 2, 0, pageCount - visiblePageCount);
            for (int i = 0; i < visiblePageCount; i++)
            {
                int pageIndex = firstVisiblePage + i;
                if (WorkflowUiStyle.Button(
                        new Rect(x, y, pageWidth, height),
                        (pageIndex + 1).ToString(),
                        pageIndex == currentPage ? WorkflowButtonStyle.ActiveTab : WorkflowButtonStyle.Quiet,
                        true,
                        GameFont.Tiny))
                    SetWorkflowEditorPage(pageIndex, pageCount);
                x += pageWidth + gap;
            }

            if (WorkflowUiStyle.Button(new Rect(x, y, navWidth, height), WfText("下一页 ›", "Next ›"),
                    WorkflowButtonStyle.Quiet, hasNext, GameFont.Tiny))
                SetWorkflowEditorPage(currentPage + 1, pageCount);
            x += navWidth + gap;
            if (WorkflowUiStyle.Button(new Rect(x, y, edgeWidth, height), "»",
                    WorkflowButtonStyle.Quiet, hasNext, GameFont.Tiny))
                SetWorkflowEditorPage(pageCount - 1, pageCount);
        }

        private static void SetWorkflowEditorPage(int pageIndex, int pageCount)
        {
            if (pageCount <= 0) return;
            int nextPage = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            if (nextPage == _workflowEditorPageIndex) return;
            _workflowEditorPageIndex = nextPage;
            RequestWorkflowEditorPage();
        }

        private void DrawWorkflowEditorDetail(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, WorkflowUiStyle.RaisedPanel);
            WorkflowUiStyle.DrawBorder(rect, new Color(0.31f, 0.35f, 0.38f));
            WorkflowCandidateEditorItem item = GetSelectedWorkflowEditorCandidate();
            if (item == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, WfText("选择一个条目后在这里查看和编辑。", "Select an entry to inspect and edit."));
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            Text.Font = GameFont.Tiny;
            CandidateClassification localClassification = item.SourceDomain == CandidateSourceDomain.Dll
                ? item.DllClassification
                : item.XmlClassification;
            ClassificationLayer localLayer = item.SourceDomain == CandidateSourceDomain.Dll
                ? ClassificationLayer.Dll
                : ClassificationLayer.Xml;
            Widgets.Label(new Rect(rect.x + 7f, rect.y + 5f, 58f, 27f),
                WfText("分类层：", "Layers: "));
            float layerX = rect.x + 65f;
            const float layerGap = 5f;
            float clearAiWidth = Mathf.Min(176f, rect.width * 0.19f);
            float layerWidth = Mathf.Max(108f,
                (rect.xMax - layerX - clearAiWidth - layerGap * 3f - 7f) / 3f);
            Rect localLayerRect = new Rect(layerX, rect.y + 4f, layerWidth, 27f);
            Rect aiLayerRect = new Rect(localLayerRect.xMax + layerGap, localLayerRect.y, layerWidth, 27f);
            Rect manualLayerRect = new Rect(aiLayerRect.xMax + layerGap, localLayerRect.y, layerWidth, 27f);
            Rect clearAiRect = new Rect(manualLayerRect.xMax + layerGap, localLayerRect.y,
                rect.xMax - manualLayerRect.xMax - layerGap - 7f, 27f);
            DrawWorkflowEditorLayer(localLayerRect,
                GetWorkflowEditorLocalLayerLabel(item.SourceDomain) + " · " +
                GetClassificationLabel(localClassification), item.EffectiveLayer == localLayer);
            DrawWorkflowEditorLayer(aiLayerRect,
                "AI · " + GetClassificationLabel(item.AiClassification),
                item.EffectiveLayer == ClassificationLayer.AiReview);
            string manualLabel = WfText("手动 · ", "Manual · ") +
                                 (item.ManualClassification == CandidateClassification.NotAnalyzed
                                     ? WfText("未设置", "Not set")
                                     : GetClassificationLabel(item.ManualClassification)) + "  ▾";
            if (WorkflowUiStyle.Button(
                    manualLayerRect,
                    manualLabel,
                    item.EffectiveLayer == ClassificationLayer.Manual
                        ? WorkflowButtonStyle.ActiveTab
                        : WorkflowButtonStyle.Dropdown,
                    !AutoTranslatorSettings.IsRunning,
                    GameFont.Tiny))
                OpenWorkflowEditorManualClassificationMenu(item);
            if (WorkflowUiStyle.Button(clearAiRect, WfText("清除 AI 复核结果", "Clear AI review"),
                    WorkflowButtonStyle.Quiet,
                    !AutoTranslatorSettings.IsRunning && item.AiClassification != CandidateClassification.NotAnalyzed,
                    GameFont.Tiny))
            {
                StartWorkflowEditorOperation(
                    backend => backend.ClearAiReviewAsync(new[] { item.CandidateId }),
                    WfText("清除 AI 复核结果", "Clear AI review result"),
                    () => ApplyWorkflowEditorAiClassification(item, CandidateClassification.NotAnalyzed),
                    true);
            }
            float half = (rect.width - 21f) * 0.5f;
            Rect sourceLabel = new Rect(rect.x + 7f, rect.y + 39f, half, 18f);
            Rect translationLabel = new Rect(sourceLabel.xMax + 7f, sourceLabel.y, half, 18f);
            Widgets.Label(sourceLabel, WfText("原文（只读）", "Source (read-only)"));
            Widgets.Label(translationLabel, WfText("译文", "Translation"));
            Rect sourceBox = new Rect(sourceLabel.x, sourceLabel.yMax + 2f, half, 105f);
            Rect translationBox = new Rect(translationLabel.x, translationLabel.yMax + 2f, half, 105f);
            Widgets.DrawBoxSolid(sourceBox, WorkflowUiStyle.Panel);
            WorkflowUiStyle.DrawBorder(sourceBox, new Color(0.28f, 0.31f, 0.33f));
            Widgets.Label(sourceBox.ContractedBy(5f), item.SourceText ?? string.Empty);
            bool busy = AutoTranslatorSettings.IsRunning;
            GUI.color = busy ? Color.grey : Color.white;
            _workflowEditorTranslationBuffer = Widgets.TextArea(
                translationBox, _workflowEditorTranslationBuffer ?? string.Empty);
            GUI.color = Color.white;

            float buttonY = sourceBox.yMax + 7f;
            const float buttonGap = 5f;
            float buttonWidth = (rect.width - 14f - buttonGap) * 0.5f;
            string[] labels =
            {
                WfText("保存译文", "Save translation"), WfText("删除译文", "Delete translation")
            };
            for (int i = 0; i < labels.Length; i++)
            {
                Rect button = new Rect(rect.x + 7f + i * (buttonWidth + buttonGap), buttonY, buttonWidth, 31f);
                WorkflowButtonStyle style = i == 0 ? WorkflowButtonStyle.Primary : WorkflowButtonStyle.Stop;
                bool clicked = WorkflowUiStyle.Button(button, labels[i], style, !busy, GameFont.Tiny);
                if (!clicked || busy) continue;
                switch (i)
                {
                    case 0:
                        StartWorkflowEditorOperation(backend => backend.SaveManualTranslationAsync(
                            item.CandidateId, _workflowEditorTranslationBuffer), WfText("保存译文", "Save translation"));
                        break;
                    case 1:
                        StartWorkflowEditorOperation(backend => backend.DeleteTranslationAsync(item.CandidateId),
                            WfText("删除译文", "Delete translation"));
                        break;
                }
            }
        }

        private static void DrawWorkflowEditorLayer(Rect rect, string label, bool isEffective)
        {
            Widgets.DrawBoxSolid(rect, isEffective
                ? new Color(0.20f, 0.29f, 0.13f, 0.95f)
                : new Color(0.10f, 0.12f, 0.135f, 0.95f));
            WorkflowUiStyle.DrawBorder(rect, isEffective
                ? WorkflowUiStyle.GoodText
                : new Color(0.28f, 0.31f, 0.33f));
            GUI.color = isEffective ? Color.white : WorkflowUiStyle.MutedText;
            Widgets.Label(new Rect(rect.x + 5f, rect.y + 3f, rect.width - 10f, rect.height - 6f), label + (isEffective
                ? WfText(" [当前生效]", " [effective]")
                : string.Empty));
            GUI.color = Color.white;
        }

        private static string GetWorkflowEditorLocalLayerLabel(CandidateSourceDomain sourceDomain)
        {
            return sourceDomain == CandidateSourceDomain.Dll
                ? WfText("本地分析（DLL）", "Local analysis (DLL)")
                : WfText("本地分析（XML）", "Local analysis (XML)");
        }

        private static void SelectWorkflowEditorMod(ModMetaData mod, string modIdentity = null)
        {
            if (mod == null) return;
            _workflowEditorSelectedModIdentity = !string.IsNullOrWhiteSpace(modIdentity)
                ? modIdentity
                : ModAnalysisTargetFactory.CreateModIdentity(mod);
            _workflowEditorSelectedModPackageId = mod.PackageId ?? string.Empty;
            _workflowEditorSelectedModDisplayName = mod.Name ?? mod.PackageId ?? string.Empty;
            _workflowEditorSnapshot = null;
            _workflowEditorSelectedCandidateId = string.Empty;
            _workflowEditorTranslationBuffer = string.Empty;
            _workflowEditorCandidateScroll = Vector2.zero;
            _workflowEditorPageIndex = 0;
            _workflowEditorRefreshRequested = true;
        }

        private static void SelectWorkflowEditorCandidate(WorkflowCandidateEditorItem item)
        {
            if (item == null) return;
            _workflowEditorSelectedCandidateId = item.CandidateId;
            _workflowEditorTranslationBuffer = item.TranslationText ?? string.Empty;
        }

        private static WorkflowCandidateEditorItem GetSelectedWorkflowEditorCandidate()
        {
            return _workflowEditorSnapshot?.Candidates?.FirstOrDefault(item =>
                string.Equals(item.CandidateId, _workflowEditorSelectedCandidateId, StringComparison.Ordinal));
        }

        private static List<WorkflowCandidateEditorItem> GetFilteredWorkflowEditorCandidates()
        {
            return (_workflowEditorSnapshot?.Candidates ?? new List<WorkflowCandidateEditorItem>())
                .Take(WorkflowEditorPageSize).ToList();
        }

        private static void UpdateWorkflowEditorSnapshot()
        {
            RefreshWorkflowModMetadataCacheIfNeeded();
            bool busy = AutoTranslatorSettings.IsRunning;
            if (_workflowEditorPreviouslyBusy && !busy)
            {
                if (_workflowEditorSuppressNextIdleRefresh)
                    _workflowEditorSuppressNextIdleRefresh = false;
                else
                    _workflowEditorRefreshRequested = true;
            }
            _workflowEditorPreviouslyBusy = busy;
            if (_workflowEditorLoadTask != null && _workflowEditorLoadTask.IsCompleted)
            {
                bool accepted = false;
                try
                {
                    WorkflowEditorSnapshot loaded = _workflowEditorLoadTask.GetAwaiter().GetResult();
                    if (string.Equals(loaded.QueryKey, GetWorkflowEditorQueryKey(), StringComparison.Ordinal))
                    {
                        _workflowEditorSnapshot = loaded;
                        accepted = true;
                    }
                    _workflowEditorLoadError = string.Empty;
                    WorkflowCandidateEditorItem selected = GetSelectedWorkflowEditorCandidate();
                    if (selected != null) _workflowEditorTranslationBuffer = selected.TranslationText ?? string.Empty;
                }
                catch (Exception ex) { _workflowEditorLoadError = ex.GetBaseException().Message; }
                _workflowEditorLoadTask = null;
                if (accepted) _workflowEditorRefreshRequested = false;
            }
            if (string.IsNullOrWhiteSpace(_workflowEditorSelectedModIdentity) ||
                _workflowEditorLoadTask != null) return;
            if (!_workflowEditorRefreshRequested && _workflowEditorSnapshot != null) return;
            string selectedModIdentity = _workflowEditorSelectedModIdentity;
            string selectedPackageId = _workflowEditorSelectedModPackageId;
            string selectedDisplayName = _workflowEditorSelectedModDisplayName;
            WorkflowBackend backend;
            try { backend = WorkflowBackendRuntime.GetOrCreate(); }
            catch (Exception ex)
            {
                _workflowEditorLoadError = ex.GetBaseException().Message;
                _workflowEditorRefreshRequested = false;
                return;
            }
            _workflowEditorLoadTask = Task.Run(() => backend.GetEditorSnapshot(
                selectedModIdentity, selectedPackageId, selectedDisplayName,
                _workflowEditorCandidateSearch, GetWorkflowEditorClassificationQuery(),
                (int)_workflowEditorTranslationFilter, _workflowEditorPageIndex, WorkflowEditorPageSize));
        }

        private static CandidateClassification? GetWorkflowEditorClassificationQuery()
        {
            switch (_workflowEditorClassificationFilter)
            {
                case WorkflowEditorClassificationFilter.NeedsTranslation: return CandidateClassification.NeedsTranslation;
                case WorkflowEditorClassificationFilter.Undetermined: return CandidateClassification.Undetermined;
                case WorkflowEditorClassificationFilter.NoTranslationNeeded: return CandidateClassification.NoTranslationNeeded;
                default: return null;
            }
        }

        private static string GetWorkflowEditorQueryKey()
        {
            return string.Join("\n", _workflowEditorSelectedModIdentity, _workflowEditorCandidateSearch ?? string.Empty,
                GetWorkflowEditorClassificationQuery()?.ToString() ?? string.Empty,
                ((int)_workflowEditorTranslationFilter).ToString(), _workflowEditorPageIndex.ToString());
        }

        private static void RequestWorkflowEditorPage()
        {
            _workflowEditorCandidateScroll = Vector2.zero;
            _workflowEditorSelectedCandidateId = string.Empty;
            _workflowEditorTranslationBuffer = string.Empty;
            _workflowEditorRefreshRequested = true;
        }

        private static async void StartWorkflowEditorOperation(
            Func<WorkflowBackend, Task> operation,
            string displayName,
            Action applySnapshotChange = null,
            bool preserveFilterSnapshot = false)
        {
            try
            {
                if (preserveFilterSnapshot) _workflowEditorSuppressNextIdleRefresh = true;
                await operation(WorkflowBackendRuntime.GetOrCreate());
                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    AutoTranslatorSettings.AddLog("✅ " + displayName);
                    applySnapshotChange?.Invoke();
                    if (!preserveFilterSnapshot) _workflowEditorRefreshRequested = true;
                    else if (!_workflowEditorPreviouslyBusy) _workflowEditorSuppressNextIdleRefresh = false;
                    _workflowOperationCompletionPendingRefresh = true;
                });
            }
            catch (OperationCanceledException)
            {
                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    if (!_workflowEditorPreviouslyBusy) _workflowEditorSuppressNextIdleRefresh = false;
                    AutoTranslatorSettings.AddLog("🛑 " + displayName + " " + WfText("已停止", "stopped"));
                });
            }
            catch (Exception ex)
            {
                ATC_Dispatcher.RunOnMainThread(() =>
                {
                    if (!_workflowEditorPreviouslyBusy) _workflowEditorSuppressNextIdleRefresh = false;
                    AutoTranslatorSettings.AddErrorLog(displayName + ": " + ex.GetBaseException().Message);
                });
            }
        }

        private static void SetWorkflowEditorClassification(
            WorkflowCandidateEditorItem item,
            CandidateClassification classification)
        {
            StartWorkflowEditorOperation(
                backend => backend.SetManualClassificationAsync(new[] { item.CandidateId }, classification),
                WfText("更新手动分类", "Update manual classification"),
                () => ApplyWorkflowEditorManualClassification(item, classification),
                true);
        }

        private static void ApplyWorkflowEditorManualClassification(
            WorkflowCandidateEditorItem item,
            CandidateClassification classification)
        {
            item.ManualClassification = classification;
            RefreshWorkflowEditorEffectiveClassification(item);
        }

        private static void ApplyWorkflowEditorAiClassification(
            WorkflowCandidateEditorItem item,
            CandidateClassification classification)
        {
            item.AiClassification = classification;
            item.AiClassificationIsCurrent = classification != CandidateClassification.NotAnalyzed;
            RefreshWorkflowEditorEffectiveClassification(item);
        }

        private static void RefreshWorkflowEditorEffectiveClassification(WorkflowCandidateEditorItem item)
        {
            if (item.ManualClassification != CandidateClassification.NotAnalyzed)
            {
                item.EffectiveClassification = item.ManualClassification;
                item.EffectiveLayer = ClassificationLayer.Manual;
                return;
            }
            if (item.AiClassificationIsCurrent &&
                item.AiClassification != CandidateClassification.NotAnalyzed)
            {
                item.EffectiveClassification = item.AiClassification;
                item.EffectiveLayer = ClassificationLayer.AiReview;
                return;
            }
            CandidateClassification local = item.SourceDomain == CandidateSourceDomain.Dll
                ? item.DllClassification
                : item.XmlClassification;
            item.EffectiveClassification = local == CandidateClassification.NotAnalyzed
                ? CandidateClassification.Undetermined
                : local;
            item.EffectiveLayer = local == CandidateClassification.NotAnalyzed
                ? (ClassificationLayer?)null
                : item.SourceDomain == CandidateSourceDomain.Dll
                    ? ClassificationLayer.Dll
                    : ClassificationLayer.Xml;
        }

        private static void OpenWorkflowEditorManualClassificationMenu(WorkflowCandidateEditorItem item)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddWorkflowEditorManualClassificationOption(options, item,
                CandidateClassification.NotAnalyzed,
                WfText("未设置（跟随自动结果）", "Not set (follow automatic result)"));
            AddWorkflowEditorManualClassificationOption(options, item,
                CandidateClassification.NeedsTranslation, WfText("需要翻译", "Needs translation"));
            AddWorkflowEditorManualClassificationOption(options, item,
                CandidateClassification.Undetermined, WfText("待复核", "Needs review"));
            AddWorkflowEditorManualClassificationOption(options, item,
                CandidateClassification.NoTranslationNeeded, WfText("无需翻译", "No translation needed"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddWorkflowEditorManualClassificationOption(
            ICollection<FloatMenuOption> options,
            WorkflowCandidateEditorItem item,
            CandidateClassification classification,
            string label)
        {
            string prefix = item.ManualClassification == classification ? "✓ " : "   ";
            options.Add(new FloatMenuOption(prefix + label, () =>
            {
                if (classification == CandidateClassification.NotAnalyzed)
                {
                    StartWorkflowEditorOperation(
                        backend => backend.ClearManualClassificationAsync(new[] { item.CandidateId }),
                        WfText("清除手动分类", "Clear manual classification"),
                        () => ApplyWorkflowEditorManualClassification(item, classification),
                        true);
                    return;
                }
                SetWorkflowEditorClassification(item, classification);
            }));
        }

        private static string GetEditorClassificationFilterLabel()
        {
            switch (_workflowEditorClassificationFilter)
            {
                case WorkflowEditorClassificationFilter.NeedsTranslation: return WfText("分类：需要翻译", "Class: needs");
                case WorkflowEditorClassificationFilter.Undetermined: return WfText("分类：待判定", "Class: undetermined");
                case WorkflowEditorClassificationFilter.NoTranslationNeeded: return WfText("分类：无需翻译", "Class: no translation");
                default: return WfText("分类：全部", "Class: all");
            }
        }

        private static void OpenWorkflowEditorClassificationFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddWorkflowEditorClassificationFilterOption(options,
                WorkflowEditorClassificationFilter.All, WfText("全部", "All"));
            AddWorkflowEditorClassificationFilterOption(options,
                WorkflowEditorClassificationFilter.NeedsTranslation, WfText("需要翻译", "Needs translation"));
            AddWorkflowEditorClassificationFilterOption(options,
                WorkflowEditorClassificationFilter.Undetermined, WfText("待判定", "Undetermined"));
            AddWorkflowEditorClassificationFilterOption(options,
                WorkflowEditorClassificationFilter.NoTranslationNeeded, WfText("无需翻译", "No translation"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddWorkflowEditorClassificationFilterOption(
            ICollection<FloatMenuOption> options,
            WorkflowEditorClassificationFilter filter,
            string label)
        {
            string prefix = _workflowEditorClassificationFilter == filter ? "✓ " : "   ";
            options.Add(new FloatMenuOption(prefix + label, () =>
            {
                _workflowEditorClassificationFilter = filter;
                _workflowEditorPageIndex = 0;
                RequestWorkflowEditorPage();
            }));
        }

        private static string GetEditorTranslationFilterLabel()
        {
            switch (_workflowEditorTranslationFilter)
            {
                case WorkflowEditorTranslationFilter.Untranslated: return WfText("译文：未翻译", "Translation: new");
                case WorkflowEditorTranslationFilter.Translated: return WfText("译文：已翻译", "Translation: done");
                case WorkflowEditorTranslationFilter.Failed: return WfText("译文：失败", "Translation: failed");
                default: return WfText("译文：全部", "Translation: all");
            }
        }

        private static void OpenWorkflowEditorTranslationFilterMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            AddWorkflowEditorTranslationFilterOption(options,
                WorkflowEditorTranslationFilter.All, WfText("全部", "All"));
            AddWorkflowEditorTranslationFilterOption(options,
                WorkflowEditorTranslationFilter.Untranslated, WfText("未翻译", "Untranslated"));
            AddWorkflowEditorTranslationFilterOption(options,
                WorkflowEditorTranslationFilter.Translated, WfText("已翻译", "Translated"));
            AddWorkflowEditorTranslationFilterOption(options,
                WorkflowEditorTranslationFilter.Failed, WfText("失败", "Failed"));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void AddWorkflowEditorTranslationFilterOption(
            ICollection<FloatMenuOption> options,
            WorkflowEditorTranslationFilter filter,
            string label)
        {
            string prefix = _workflowEditorTranslationFilter == filter ? "✓ " : "   ";
            options.Add(new FloatMenuOption(prefix + label, () =>
            {
                _workflowEditorTranslationFilter = filter;
                _workflowEditorPageIndex = 0;
                RequestWorkflowEditorPage();
            }));
        }

        private static string GetClassificationLabel(CandidateClassification value)
        {
            switch (value)
            {
                case CandidateClassification.NeedsTranslation: return WfText("需要翻译", "Needs");
                case CandidateClassification.NoTranslationNeeded: return WfText("无需翻译", "No translation");
                case CandidateClassification.Undetermined: return WfText("待判定", "Undetermined");
                default: return WfText("未分析", "Not analyzed");
            }
        }

        private static string GetLayerLabel(ClassificationLayer? layer)
        {
            if (!layer.HasValue) return WfText("系统回退", "Fallback");
            switch (layer.Value)
            {
                case ClassificationLayer.Manual: return WfText("手动", "Manual");
                case ClassificationLayer.AiReview: return "AI";
                case ClassificationLayer.Dll: return "DLL";
                default: return "XML";
            }
        }

        private static string GetTranslationStateLabel(WorkflowCandidateEditorItem item)
        {
            if (item.TranslationState == CandidateTranslationState.Translated && item.TranslationIsCurrent)
                return WfText("已翻译", "Translated") + " · " + item.TranslationOrigin;
            if (item.TranslationState == CandidateTranslationState.Failed) return WfText("失败", "Failed");
            if (item.TranslationState == CandidateTranslationState.Translating) return WfText("翻译中", "Translating");
            return WfText("未翻译", "Untranslated");
        }
    }
}
