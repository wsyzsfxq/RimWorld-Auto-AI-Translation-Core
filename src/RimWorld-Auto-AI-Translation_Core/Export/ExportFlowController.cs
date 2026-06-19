using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using RimWorld;
// 這個檔案負責導出流程的步驟控制。
// EN: This file controls the export workflow steps.

namespace AutoTranslator_Core
{


    // 這個類別負責 導出流程控制器 的主要流程與狀態。
    // EN: This class manages the main workflow and state for ExportFlowController.
    public static class ExportFlowController
    {
        // 這個方法負責啟動 導出流程 流程。
        // EN: This method starts export flow.
        public static void StartExportFlow()
        {

            string packPath = AutoTranslatorScanner.GetLocalPackPath();
            string langsPath = Path.Combine(packPath, "Languages");
            if (!Directory.Exists(langsPath))
            {
                Messages.Message("ATC_ExportWindow_NoTranslationFound".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            var settings = AutoTranslatorMod.Settings;


            if (settings.IsEulaStillValid())
            {
                Find.WindowStack.Add(new Dialog_ExportReminder(OnReminderConfirmed));
            }
            else
            {
                Find.WindowStack.Add(new Dialog_ExportEula(OnEulaAccepted));
            }
        }

        // 這個方法負責處理 OnEulaAccepted 相關流程。
        // EN: This method handles on eula accepted.
        private static void OnEulaAccepted()
        {

            var settings = AutoTranslatorMod.Settings;
            settings.HasAcceptedExportEula = true;
            settings.EulaAcceptedTimestamp = DateTime.Now.ToString("o");
            settings.EulaAcceptedVersion = ExportEulaVersion.CurrentVersion;
            settings.EulaAcceptCount++;

            AutoTranslatorSettings.AddLog("📝 " +
                "ATC_Log_EulaAccepted".Translate(ExportEulaVersion.CurrentVersion));


            LoadedModManager.GetMod<AutoTranslatorMod>().WriteSettings();


            Find.WindowStack.Add(new Window_Export());
        }

        // 這個方法負責處理 OnReminderConfirmed 相關流程。
        // EN: This method handles on reminder confirmed.
        private static void OnReminderConfirmed()
        {
            Find.WindowStack.Add(new Window_Export());
        }
    }
}
