using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public partial class AutoTranslatorMod : Mod
    {
        private const float CloudRowHeight = 40f;

        private void DrawCloudTab(Listing_Standard l, Rect viewRect)
        {
            DrawCloudToolbarAndSettings(l, viewRect);

            if (AutoTranslatorSettings.IsFetchingCloud) return;

            Text.Font = GameFont.Small;
            string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(Settings.CloudTargetLang);
            int currentLangCloudCount = AutoTranslatorSettings.CloudRegistry.Count(c => c != null && c.Language == targetLangFolder);

            if (AutoTranslatorSettings.CloudConnectionFailed)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                Widgets.Label(l.GetRect(25f), "ATC_Cloud_ConnectionFailed".Translate());
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = new Color(0.4f, 0.8f, 1f);
                Widgets.Label(l.GetRect(25f), "ATC_Cloud_ConnectionNormal".Translate(currentLangCloudCount));
                GUI.color = Color.white;
            }

            Rect searchRect = l.GetRect(30f);
            string oldSearch = AutoTranslatorSettings.CloudSearchText ?? "";
            AutoTranslatorSettings.CloudSearchText = Widgets.TextField(searchRect, oldSearch);
            if (!string.Equals(oldSearch, AutoTranslatorSettings.CloudSearchText ?? "", StringComparison.Ordinal))
            {
                _cachedCloudDisplayMods = null;
            }

            if (string.IsNullOrEmpty(AutoTranslatorSettings.CloudSearchText))
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(searchRect.x + 5f, searchRect.y + 2f, searchRect.width, searchRect.height), "ATC_Cloud_SearchHint".Translate());
                GUI.color = Color.white;
            }
            l.Gap(10f);

            List<ModMetaData> localMods = GetCloudDisplayMods();
            if (localMods.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(l.GetRect(40f), "ATC_Cloud_NoModsWarning".Translate());
                GUI.color = Color.white;
                return;
            }

            Dictionary<string, List<CloudModRecord>> cloudLookup = BuildCloudLookup(targetLangFolder);
            float listStartY = l.CurHeight;
            int firstVisible = Math.Max(0, Mathf.FloorToInt((AutoTranslatorSettings.mainScrollPos.y - listStartY) / CloudRowHeight) - 2);
            int lastVisible = Math.Min(localMods.Count - 1, Mathf.CeilToInt((AutoTranslatorSettings.mainScrollPos.y - listStartY + 900f) / CloudRowHeight) + 2);

            for (int i = 0; i < localMods.Count; i++)
            {
                Rect rowRect = l.GetRect(CloudRowHeight);
                if (i < firstVisible || i > lastVisible)
                {
                    continue;
                }

                DrawCloudModRow(localMods[i], rowRect, cloudLookup, targetLangFolder);
            }
        }

        private List<ModMetaData> GetCloudDisplayMods()
        {
            List<ModMetaData> validMods = GetValidModsCached() ?? new List<ModMetaData>();
            string searchText = AutoTranslatorSettings.CloudSearchText ?? "";

            if (_cachedCloudDisplayMods != null &&
                _cachedCloudSearchText == searchText &&
                _cachedCloudValidModCount == validMods.Count)
            {
                return _cachedCloudDisplayMods;
            }

            IEnumerable<ModMetaData> mods = validMods.Where(m => m != null && !ShouldSkipCloudSharingMod(m));
            if (!string.IsNullOrEmpty(searchText))
            {
                string searchLower = searchText.ToLowerInvariant();
                mods = mods.Where(m =>
                    ((m.Name ?? "").ToLowerInvariant().Contains(searchLower)) ||
                    ((m.PackageId ?? "").ToLowerInvariant().Contains(searchLower)));
            }

            _cachedCloudDisplayMods = mods.ToList();
            _cachedCloudSearchText = searchText;
            _cachedCloudValidModCount = validMods.Count;
            return _cachedCloudDisplayMods;
        }
    }
}
