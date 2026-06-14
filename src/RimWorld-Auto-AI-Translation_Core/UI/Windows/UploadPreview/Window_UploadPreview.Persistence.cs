using HarmonyLib;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
        public partial class Window_UploadPreview : Window
        {

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
