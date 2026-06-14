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

            private void LoadPreviewData()
            {
                var resultData = new Dictionary<string, List<PreviewItem>>();

                // ✨ 咪咪雙重判定雷達
                string id1 = _mod.PackageId.ToLower();
                string id2 = _mod.PackageId.Replace(".", "_").ToLower();
                // 如果是從專屬工作室來的，無條件全部放行，不檢查檔名！
                bool isWorkspace = _sourceDir.Contains("Upload_Workspace");

                if (Directory.Exists(_sourceDir))
                {
                    // 1. 解析 Keyed 類別
                    string keyedDir = Path.Combine(_sourceDir, "Keyed");
                    if (Directory.Exists(keyedDir))
                    {
                        var list = new List<PreviewItem>();
                        foreach (var file in Directory.GetFiles(keyedDir, "*.xml", SearchOption.AllDirectories))
                        {
                            string fileName = Path.GetFileName(file).ToLower();
                            bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                            if (isValid)
                            {
                                var dict = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                foreach (var kv in dict)
                                    list.Add(new PreviewItem { Key = kv.Key, OriginalText = "ATC_Preview_ClickToSee".Translate(), TranslatedText = kv.Value });
                            }
                        }
                        if (list.Count > 0) resultData["Keyed"] = list;
                    }

                    // 2. 解析 DefInjected 類別
                    string defBaseDir = Path.Combine(_sourceDir, "DefInjected");
                    if (Directory.Exists(defBaseDir))
                    {
                        foreach (var typeDir in Directory.GetDirectories(defBaseDir))
                        {
                            string defType = Path.GetFileName(typeDir);
                            var list = new List<PreviewItem>();

                            // 🌟 關鍵修復：加入 SearchOption.AllDirectories，就算有 100 層子資料夾也照樣挖出來！
                            foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                            {
                                string fileName = Path.GetFileName(file).ToLower();
                                bool isValid = isWorkspace || fileName.StartsWith(id1 + "_") || fileName.StartsWith(id1 + ".") || fileName.StartsWith(id2 + "_") || fileName.StartsWith(id2 + ".");

                                if (isValid)
                                {
                                    var dict = AutoTranslatorScanner.LoadXmlFileToDict(file);
                                    foreach (var kv in dict)
                                        list.Add(new PreviewItem { Key = kv.Key, OriginalText = "ATC_Preview_ClickToSee".Translate(), TranslatedText = kv.Value });
                                }
                            }
                            if (list.Count > 0) resultData[defType] = list;
                        }
                    }
                }

                ATC_Dispatcher.RunOnMainThread(() => {
                    _categorizedData = resultData;
                    _selectedCategory = _categorizedData.Keys.FirstOrDefault() ?? "";
                    _isLoading = false;
                });
            }
        }
}
