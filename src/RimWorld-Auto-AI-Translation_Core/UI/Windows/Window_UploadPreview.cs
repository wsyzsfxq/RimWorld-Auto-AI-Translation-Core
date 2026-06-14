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
            private ModMetaData _mod;
            private string _targetLangFolder;
            private string _sourceDir;
            private string _modName;
            private Vector2 _leftScrollPos = Vector2.zero;
            private Vector2 _rightScrollPos = Vector2.zero;
            private bool _isLoading = true;
            private bool _isEditable = false; // ✨ 控制左右兩側預設不可修改的狀態開關

            private class PreviewItem
            {
                public string Key;
                public string OriginalText;
                public string TranslatedText;
                public bool IsModified;
            }

            private Dictionary<string, List<PreviewItem>> _categorizedData = new Dictionary<string, List<PreviewItem>>();
            private string _selectedCategory = "";
            private string _updateLogText = ""; // ✨ 用來裝更新說明的暫存字串

            public override Vector2 InitialSize => new Vector2(1000f, 780f);

            public Window_UploadPreview(ModMetaData mod, string targetLangFolder, string sourceDir, string modName)
            {
                _mod = mod;
                _targetLangFolder = targetLangFolder;
                _sourceDir = sourceDir;
                _modName = modName;
                this.doCloseButton = false;
                this.doCloseX = true;
                this.forcePause = true;
                this.absorbInputAroundWindow = true;

                System.Threading.Tasks.Task.Run(() => LoadPreviewData());
            }
            public override void DoWindowContents(Rect inRect)
            {
            // 🛡️ 咪咪的免死金牌：預覽畫面也必須保護原文！
            Patch_GUI_Label_GUIContent.BypassInterceptor = true;
            try
            {
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(0, 0, inRect.width, 35f), "🔍 " + "ATC_UploadPreview_Title".Translate(_mod.Name));
                Text.Font = GameFont.Small;
                Widgets.DrawLineHorizontal(0, 35f, inRect.width);

                if (_isLoading)
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0, 0, inRect.width, inRect.height), "🔄 " + "ATC_UploadPreview_Loading".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    return;
                }
                // ✨ 加上防呆提示：如果過濾完發現根本沒有這個模組的翻譯
                if (_categorizedData.Count == 0)
                {
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(0, 0, inRect.width, inRect.height), "⚠️ " + "ATC_UploadPreview_NoTranslation".Translate()); Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;

                    // 畫一個大大的取消按鈕讓他離開
                    if (Widgets.ButtonText(new Rect(inRect.width / 2f - 75f, inRect.height - 60f, 150f, 40f), "ATC_Btn_Cancel".Translate()))
                    {
                        this.Close();
                    }
                    return; // 🛡️ 直接提早結束，不畫底下的上傳按鈕，徹底防止空包彈！
                }
                // 頂部核心配置區
                float topOffset = 45f;
                float leftWidth = 220f;
                float spacing = 15f;
                float rightWidth = inRect.width - leftWidth - spacing;
                float contentHeight = inRect.height - topOffset - 150f; // 留 150px 給底部的更新日誌輸入格與按鈕

                Rect leftOutRect = new Rect(0, topOffset, leftWidth, contentHeight);
                Rect rightOutRect = new Rect(leftWidth + spacing, topOffset, rightWidth, contentHeight);

                // 👈 左側：分類選單
                Widgets.DrawBoxSolid(leftOutRect, new Color(0.1f, 0.1f, 0.1f, 0.5f));
                Rect leftViewRect = new Rect(0, 0, leftOutRect.width - 20f, _categorizedData.Count * 35f);
                Widgets.BeginScrollView(leftOutRect, ref _leftScrollPos, leftViewRect);
                float curY = 0f;
                foreach (var category in _categorizedData.Keys)
                {
                    Rect rowRect = new Rect(5f, curY, leftViewRect.width - 5f, 30f);
                    if (_selectedCategory == category) Widgets.DrawHighlightSelected(rowRect);
                    else Widgets.DrawHighlightIfMouseover(rowRect);

                    if (Widgets.ButtonInvisible(rowRect)) { _selectedCategory = category; _rightScrollPos = Vector2.zero; }
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(rowRect, $"{category} ({_categorizedData[category].Count})");
                    Text.Anchor = TextAnchor.UpperLeft;
                    curY += 35f;
                }
                Widgets.EndScrollView();

                // 👉 右側：清單檢視區
                Widgets.DrawBoxSolid(rightOutRect, new Color(0.05f, 0.05f, 0.05f, 0.5f));
                if (!string.IsNullOrEmpty(_selectedCategory) && _categorizedData.ContainsKey(_selectedCategory))
                {
                    var items = _categorizedData[_selectedCategory];
                    float rowHeight = 70f;
                    Rect rightViewRect = new Rect(0, 0, rightOutRect.width - 20f, items.Count * rowHeight);
                    Widgets.BeginScrollView(rightOutRect, ref _rightScrollPos, rightViewRect);

                    float editY = 0f;
                    foreach (var item in items)
                    {
                        Rect itemRect = new Rect(5f, editY, rightViewRect.width - 10f, rowHeight - 5f);
                        Widgets.DrawHighlightIfMouseover(itemRect);

                        Text.Font = GameFont.Tiny;
                        GUI.color = Color.gray;
                        Widgets.Label(new Rect(itemRect.x, itemRect.y, itemRect.width, 15f), item.Key);

                        Text.Font = GameFont.Small;
                        GUI.color = Color.white;
                        Rect transRect = new Rect(itemRect.x, itemRect.y + 15f, itemRect.width, itemRect.height - 15f);

                        // ✨ 智慧鎖：根據 _isEditable 旗標決定玩家能不能直接打字修改
                        if (_isEditable)
                        {
                            string newText = Widgets.TextArea(transRect, item.TranslatedText ?? "");
                            if (newText != item.TranslatedText) { item.TranslatedText = newText; item.IsModified = true; }
                        }
                        else
                        {
                            Widgets.Label(transRect, item.TranslatedText);
                        }
                        editY += rowHeight;
                    }
                    GUI.color = Color.white;
                    Widgets.EndScrollView();
                }

                // 📜 底部一整橫條：Steam 口味「更新日誌 / 填寫說明區」
                float logAreaY = topOffset + contentHeight + 10f;
                Rect logLabelRect = new Rect(0, logAreaY, inRect.width, 22f);
                Widgets.Label(logLabelRect, "📝 " + "ATC_Upload_LogLabel".Translate());

                Rect logInputRect = new Rect(0, logAreaY + 22f, inRect.width, 55f);
                _updateLogText = Widgets.TextArea(logInputRect, _updateLogText);
                if (string.IsNullOrEmpty(_updateLogText))
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(logInputRect.x + 5f, logInputRect.y + 2f, logInputRect.width, logInputRect.height), "ATC_Upload_LogHint".Translate());
                    GUI.color = Color.white;
                }

                // 🔘 最底部：三顆經典交互按鈕
                float btnY = inRect.height - 35f;

                // 按鈕一 (最左邊)：取消上傳
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (Widgets.ButtonText(new Rect(0, btnY, 130f, 35f), "ATC_Btn_Cancel".Translate()))
                {
                    this.Close();
                }

                // 按鈕二 (中間)：修改內容
                if (_isEditable) GUI.color = Color.yellow;
                else GUI.color = new Color(0.7f, 0.7f, 1f);
                if (Widgets.ButtonText(new Rect(145f, btnY, 150f, 35f), _isEditable ? "✍️ " + "ATC_Upload_EditingMode".Translate() : "⚙️ " + "ATC_Upload_UnlockEdit".Translate()))
                {
                    _isEditable = !_isEditable; // 切換解鎖狀態
                }

                // 按鈕三 (最右邊)：安全檢查完畢，發射上傳！
                bool isAdmin = !string.IsNullOrEmpty(AutoTranslatorMod.Settings.CloudAdminToken);
                bool hasValidLog = !string.IsNullOrWhiteSpace(_updateLogText) && _updateLogText.Trim().Length >= 5;
                bool canUpload = isAdmin || hasValidLog; // 🌟 咪咪防禦網：沒有特權的普通玩家，必須乖乖寫滿 5 個字的更新日誌！

                GUI.color = canUpload ? new Color(0.4f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.5f);

                if (Widgets.ButtonText(new Rect(inRect.width - 180f, btnY, 180f, 35f), "🚀 " + "ATC_Upload_ConfirmUploadBtn".Translate()))
                {
                    if (!canUpload)
                    {
                        // 拒絕上傳，並彈出本地化警告！
                        Verse.Messages.Message("ATC_Msg_UploadLogRequired".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false);
                        return;
                    }

                    // 如果玩家有就地動手修改，先幫他儲存到磁碟中
                    SaveChangesIfAny();

                    // 喚醒原本的 CloudClient 上傳，並把我們打好的日誌一起打包丟去 Worker 資料庫
                    ExecuteActualUpload();
                    this.Close();
                }
                GUI.color = Color.white;
            }
            finally
            {
                // 🛡️ 收回免死金牌
                Patch_GUI_Label_GUIContent.BypassInterceptor = false;
            }
        }
    }
            // Upload preview support methods are split into partial files in UI/Windows/UploadPreview/.
}
