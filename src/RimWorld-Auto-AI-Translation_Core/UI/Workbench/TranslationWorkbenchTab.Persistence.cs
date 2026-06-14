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
        public static partial class TranslationWorkbenchTab
        {

            private static void SaveModifications()
            {
                if (_editingMod == null) return;

                // ✨ 咪咪修復：存檔時也必須使用 TargetLang！
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
