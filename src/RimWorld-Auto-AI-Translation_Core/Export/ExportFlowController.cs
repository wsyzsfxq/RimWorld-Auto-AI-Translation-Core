using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;

namespace AutoTranslator_Core
{
    /// <summary>
    /// 導出流程控制器：負責判斷該走 EULA 流程還是快速提醒流程
    /// </summary>
    public static class ExportFlowController
    {
        public static void StartExportFlow()
        {
            // 先檢查是否有可導出的內容
            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string langsPath = Path.Combine(packPath, "Languages");
            if (!Directory.Exists(langsPath))
            {
                Messages.Message("ATC_ExportWindow_NoTranslationFound".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var settings = AutoTranslatorMod.Settings;

            // EULA 仍有效 → 走快速提醒
            // EULA 過期/未同意/版本不一致 → 走完整 EULA
            if (settings.IsEulaStillValid())
            {
                Find.WindowStack.Add(new Dialog_ExportReminder(OnReminderConfirmed));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_ExportEula(OnEulaAccepted));
            }
        }

        private static void OnEulaAccepted()
        {
            // 寫入同意紀錄
            var settings = AutoTranslatorMod.Settings;
            settings.HasAcceptedExportEula = true;
            settings.EulaAcceptedTimestamp = DateTime.Now.ToString("o"); // ISO 8601
            settings.EulaAcceptedVersion = ExportEulaVersion.CurrentVersion;
            settings.EulaAcceptCount++;

            AutoTranslatorSettings.AddLog("📝 " +
                "ATC_Log_EulaAccepted".Translate(ExportEulaVersion.CurrentVersion));

            // 立刻寫入磁碟（避免玩家關閉遊戲沒存檔）
            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();

            // 進入模組選擇視窗
            Find.WindowStack.Add(new Window_Export());
        }

        private static void OnReminderConfirmed()
        {
            Find.WindowStack.Add(new Window_Export());
        }
    }
}
