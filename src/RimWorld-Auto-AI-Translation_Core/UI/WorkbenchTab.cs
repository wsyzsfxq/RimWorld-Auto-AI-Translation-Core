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
        // 🚀 V4.0 終極全域編輯工作台 (內嵌分頁版) - 完美防彈裝甲版！
        // =====================================================================
        public static partial class TranslationWorkbenchTab
        {
            public class WorkbenchItem
            {
                public string Key;
                public string OriginalText;
                public string TranslatedText;
                public bool IsModified;
            }

            private static Verse.ModMetaData _editingMod = null;
            private static bool _isLoading = false;
            private static string _modSearchText = "";
            // ✨ 咪咪修復：預設改為 false，一進來就顯示所有模組，方便玩家編輯模組自帶的翻譯！
            private static bool _showOnlyTranslated = false;
            private static UnityEngine.Vector2 _modListScroll = UnityEngine.Vector2.zero;
            private static bool _isTranslatingModNames = false;
            private static bool _isTranslatedModsCacheLoading = false;
            private static string _translatedModsCacheError = null;
            private static List<Verse.ModMetaData> _cachedModSelectionList = null;
            private static string _cachedModSelectionSearch = null;
            private static bool _cachedModSelectionShowTranslatedOnly = false;
            private static bool _cachedModSelectionTranslateNames = false;
            private static int _cachedModSelectionValidCount = -1;
            private static int _cachedModSelectionTranslatedCount = -1;

            private static Dictionary<string, List<WorkbenchItem>> _categorizedData = new Dictionary<string, List<WorkbenchItem>>();
            private static string _selectedCategory = "";
            private static string _itemSearchText = "";
            private static UnityEngine.Vector2 _catListScroll = UnityEngine.Vector2.zero;
            private static UnityEngine.Vector2 _itemScroll = UnityEngine.Vector2.zero;

            private static HashSet<string> _translatedPackageIds = null;
            // ===== 全域搜尋專用變數 =====
            private static string _globalSearchText = "";
            private static bool _isGlobalSearching = false;
            private static UnityEngine.Vector2 _globalSearchScroll = UnityEngine.Vector2.zero;
            private static List<GlobalSearchResult> _globalSearchResults = new List<GlobalSearchResult>();
            private static TargetLanguage? _globalSearchLangFilter = null; // null 代表搜尋所有語言
            private static float _globalSearchProgress = 0f; // ✨ 搜尋進度百分比
            public class GlobalSearchResult
            {
                public Verse.ModMetaData Mod;
                public string Key;
                public string TranslatedText;
            }
            public static void DrawEditorTab(Verse.Listing_Standard l, UnityEngine.Rect viewRect)
            {
                InitTranslatedModsCache();

                float contentHeight = 600f;
                float leftWidth = Mathf.Min(360f, viewRect.width * 0.36f);
                float spacing = 15f;
                float rightWidth = viewRect.width - leftWidth - spacing;

                UnityEngine.Rect fullRect = l.GetRect(contentHeight);
                UnityEngine.Rect leftOutRect = new UnityEngine.Rect(fullRect.x, fullRect.y, leftWidth, contentHeight);
                UnityEngine.Rect rightOutRect = new UnityEngine.Rect(fullRect.x + leftWidth + spacing, fullRect.y, rightWidth, contentHeight);

                Verse.Widgets.DrawBoxSolid(leftOutRect, new UnityEngine.Color(0.1f, 0.1f, 0.1f, 0.5f));
                Verse.Widgets.DrawBoxSolid(rightOutRect, new UnityEngine.Color(0.05f, 0.05f, 0.05f, 0.5f));

                // ==========================================
                // 狀態 A：還沒選擇模組，顯示「模組搜尋列表」
                // ==========================================
                if (_editingMod == null)
                {
                    DrawModSelectionMode(leftOutRect, rightOutRect);
                }
                else
                {
                    DrawEditingMode(leftOutRect, rightOutRect);
                }
            }
            // Workbench support methods are split into partial files in UI/Workbench/.
        }
}
