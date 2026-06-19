using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責 翻譯工作台分頁存取 相關邏輯，支援 Auto Translation Core 的執行流程。
// EN: This file contains translation workbench tab persistence support code.

namespace AutoTranslator_Core
{
        // 這個類別負責 翻譯工作台分頁 的主要流程與狀態。
        // EN: This class manages the main workflow and state for TranslationWorkbenchTab.
        public static partial class TranslationWorkbenchTab
        {

            // 這個方法負責保存 Modifications 資料。
            // EN: This method saves modifications.
            private static void SaveModifications()
            {
                if (_editingMod == null) return;


                string targetLangFolder = AutoTranslatorScanner.GetFolderNameByLanguage(AutoTranslatorMod.Settings.TargetLang);
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                string cleanPackageId = _editingMod.PackageId.Replace(".", "_").ToLower();
                string workspaceBaseDir = System.IO.Path.Combine(packPath, "Upload_Workspace");
                int savedCount = 0;
                bool touchedTranslationFiles = false;
                var clearKeysByDefType = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var categoryPair in _categorizedData)
                {
                    bool categoryModified = categoryPair.Value.Any(item => item.IsModified || !string.Equals(item.TranslatedText ?? "", item.OriginalTranslatedText ?? "", StringComparison.Ordinal));
                    if (!categoryModified) continue;

                    string category = categoryPair.Key;
                    clearKeysByDefType[category] = new HashSet<string>(
                        categoryPair.Value.Where(item => !string.IsNullOrWhiteSpace(item.Key)).Select(item => item.Key),
                        StringComparer.OrdinalIgnoreCase);

                    string targetDir = category == "Keyed"
                        ? System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "DefInjected", category);
                    string workspaceDir = category == "Keyed"
                        ? System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "DefInjected", category);

                    System.IO.Directory.CreateDirectory(targetDir);
                    System.IO.Directory.CreateDirectory(workspaceDir);

                    foreach (var oldFile in AutoTranslatorScanner.GetXmlFilesForTranslationCache(workspaceDir, System.IO.SearchOption.TopDirectoryOnly))
                    {
                        System.IO.File.Delete(oldFile);
                        AutoTranslatorScanner.NotifyTranslationFileChanged(oldFile);
                        touchedTranslationFiles = true;
                    }
                    foreach (var oldFile in AutoTranslatorScanner.GetXmlFilesForTranslationCache(targetDir, System.IO.SearchOption.TopDirectoryOnly))
                    {
                        if (System.IO.Path.GetFileName(oldFile).ToLower().Contains(cleanPackageId))
                        {
                            System.IO.File.SetAttributes(oldFile, System.IO.FileAttributes.Normal);
                            System.IO.File.Delete(oldFile);
                            AutoTranslatorScanner.NotifyTranslationFileChanged(oldFile);
                            touchedTranslationFiles = true;
                        }
                    }

                    string targetFile = System.IO.Path.Combine(targetDir, $"{cleanPackageId}_AutoTranslated.xml");
                    string workspaceFile = System.IO.Path.Combine(workspaceDir, $"{cleanPackageId}_AutoTranslated.xml");

                    Dictionary<string, string> fullDictToSave = new Dictionary<string, string>();
                    foreach (var item in categoryPair.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(item.TranslatedText)) fullDictToSave[item.Key] = item.TranslatedText;
                        if (item.IsModified) { item.IsModified = false; item.OriginalTranslatedText = item.TranslatedText; savedCount++; }
                    }

                    if (fullDictToSave.Count > 0)
                    {
                        AutoTranslatorScanner.SaveXml(targetFile, fullDictToSave);
                        AutoTranslatorScanner.SaveXml(workspaceFile, fullDictToSave);
                        touchedTranslationFiles = true;
                    }
                }

                AutoTranslatorSettings.AddLog("💾 " + "ATC_Log_WorkbenchSaved".Translate(savedCount));
                if (touchedTranslationFiles)
                {
                    AutoTranslatorScanner.RequestMemoryDropForPackage(_editingMod.PackageId, clearKeysByDefType);
                    UIInterceptor.RefreshRuntimeUICache();
                }

                InitTranslatedModsCache();
                if (HasAnySavedTranslationForCurrentMod(targetLangFolder, cleanPackageId))
                {
                    MarkPackageTranslated(_editingMod.PackageId);
                    ModUpdateDetector.MarkModAsTranslated(_editingMod.PackageId, _editingMod.RootDir.FullName);
                }
            }

            private static bool HasAnySavedTranslationForCurrentMod(string targetLangFolder, string cleanPackageId)
            {
                string langRoot = System.IO.Path.Combine(AutoTranslatorScanner.GetLocalPackPath(), "Languages", targetLangFolder);
                if (!System.IO.Directory.Exists(langRoot)) return false;

                foreach (var file in AutoTranslatorScanner.GetXmlFilesForTranslationCache(langRoot, System.IO.SearchOption.AllDirectories))
                {
                    if (!System.IO.Path.GetFileName(file).ToLower().Contains(cleanPackageId)) continue;
                    if (AutoTranslatorScanner.LoadXmlFileToDict(file).Count > 0) return true;
                }

                return false;
            }
        }
}
