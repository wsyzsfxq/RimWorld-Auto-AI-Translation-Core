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

                foreach (var categoryPair in _categorizedData)
                {
                    string category = categoryPair.Key;
                    string targetDir = category == "Keyed"
                        ? System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(packPath, "Languages", targetLangFolder, "DefInjected", category);
                    string workspaceDir = category == "Keyed"
                        ? System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "Keyed")
                        : System.IO.Path.Combine(workspaceBaseDir, _editingMod.PackageId, targetLangFolder, "DefInjected", category);

                    System.IO.Directory.CreateDirectory(targetDir);
                    System.IO.Directory.CreateDirectory(workspaceDir);

                    foreach (var oldFile in System.IO.Directory.GetFiles(workspaceDir, "*.xml")) System.IO.File.Delete(oldFile);
                    foreach (var oldFile in System.IO.Directory.GetFiles(targetDir, "*.xml"))
                    {
                        if (System.IO.Path.GetFileName(oldFile).ToLower().Contains(cleanPackageId))
                        {
                            System.IO.File.SetAttributes(oldFile, System.IO.FileAttributes.Normal);
                            System.IO.File.Delete(oldFile);
                        }
                    }

                    string targetFile = System.IO.Path.Combine(targetDir, $"{cleanPackageId}_AutoTranslated.xml");
                    string workspaceFile = System.IO.Path.Combine(workspaceDir, $"{cleanPackageId}_AutoTranslated.xml");

                    Dictionary<string, string> fullDictToSave = new Dictionary<string, string>();
                    foreach (var item in categoryPair.Value)
                    {
                        if (!string.IsNullOrWhiteSpace(item.TranslatedText)) fullDictToSave[item.Key] = item.TranslatedText;
                        if (item.IsModified) { item.IsModified = false; savedCount++; }
                    }

                    if (fullDictToSave.Count > 0)
                    {
                        AutoTranslatorScanner.SaveXml(targetFile, fullDictToSave);
                        AutoTranslatorScanner.SaveXml(workspaceFile, fullDictToSave);
                    }
                }

                AutoTranslatorSettings.AddLog("💾 " + "ATC_Log_WorkbenchSaved".Translate(savedCount));
                AutoTranslatorScanner.RequestMemoryDrop();
                UIInterceptor.ClearUICache();

                InitTranslatedModsCache();
                _translatedPackageIds.Add(_editingMod.PackageId);
                ModUpdateDetector.MarkModAsTranslated(_editingMod.PackageId, _editingMod.RootDir.FullName);
            }
        }
}
