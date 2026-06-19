using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;
// 這個檔案負責上傳預覽的保存與還原。
// EN: This file saves and restores upload preview edits.

namespace AutoTranslator_Core
{
        // 這個類別負責 視窗上傳Preview 的主要流程與狀態。
        // EN: This class manages the main workflow and state for Window_UploadPreview.
        public partial class Window_UploadPreview : Window
        {

        // 這個方法負責保存 ChangesIfAny 資料。
        // EN: This method saves changes if any.
        private void SaveChangesIfAny()
            {
                string packPath = AutoTranslatorScanner.GetLocalPackPath();
                int saveCount = 0;
                foreach (var pair in _categorizedData)
                {
                    var modified = pair.Value.Where(i => i.IsModified).ToList();
                    if (modified.Count == 0) continue;

                    string fileDir = pair.Key == "Keyed"
                        ? Path.Combine(_sourceDir, "Keyed")
                        : Path.Combine(_sourceDir, "DefInjected", pair.Key);

                    Directory.CreateDirectory(fileDir);
                    string targetFile = Path.Combine(fileDir, $"{_mod.PackageId.Replace(".", "_").ToLower()}_AutoTranslated.xml");
                    var existing = AutoTranslatorScanner.LoadXmlFileToDict(targetFile);

                    foreach (var item in modified) { existing[item.Key] = item.TranslatedText; item.IsModified = false; saveCount++; }
                    AutoTranslatorScanner.SaveXml(targetFile, existing);
                }
                if (saveCount > 0) { AutoTranslatorScanner.RequestMemoryDrop(); UIInterceptor.ClearUICache(); }
            }

        }
}
