using AutoTranslator_Core.TargetedHardcodedUi;
using System;
using System.IO;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core.Workflow
{
    [Flags]
    internal enum RuntimeTranslationRefreshScope
    {
        None = 0,
        Xml = 1,
        Dll = 2
    }

    internal static class RuntimeTranslationRefresher
    {
        internal static async Task<bool> ForceReloadAllAsync()
        {
            AutoTranslatorSettings settings = AutoTranslatorMod.Settings;
            if (settings == null) return false;

            string languageRoot = Path.Combine(
                AutoTranslatorScanner.GetLocalPackPath(),
                "Languages",
                AutoTranslatorScanner.GetFolderNameByLanguage(settings.TargetLang));
            AutoTranslatorScanner.NotifyTranslationFilesChanged(languageRoot);
            bool xmlRefreshed = await AutoTranslatorScanner.RequestMemoryDropAsync();
            bool dllReloadRequested = await EnableAndRequestDllReloadAsync();
            AutoTranslatorSettings.AddLog(
                "运行时译文热重载：XML=" + (xmlRefreshed ? "成功" : "失败") +
                "；" + DescribeDllRefresh(dllReloadRequested));
            return xmlRefreshed && dllReloadRequested;
        }

        internal static Task<bool> EnableAndRequestDllReloadAsync()
        {
            TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            ATC_Dispatcher.RunOnMainThread(async () =>
            {
                try
                {
                    AutoTranslatorSettings settings = AutoTranslatorMod.Settings;
                    if (settings == null)
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    if (UIInterceptionModeController.LegacySelected)
                    {
                        // The saved manifest remains available when the user switches back.
                        // Successful deferral must not change the other mode's enable preference.
                        completion.TrySetResult(true);
                        return;
                    }

                    settings.EnableHardcodedUiPrototype = true;
                    LoadedModManager.GetMod<AutoTranslatorMod>()?.WriteSettings();
                    completion.TrySetResult(await HardcodedUiTargetedPatchManager.RequestReloadAsync());
                }
                catch (Exception ex)
                {
                    AutoTranslatorSettings.AddErrorLog(
                        "DLL 补丁清单重载请求失败：" + ex.Message);
                    completion.TrySetResult(false);
                }
            });
            return completion.Task;
        }

        internal static string DescribeDllRefresh(bool succeeded)
        {
            if (!UIInterceptionModeController.LegacySelected && succeeded &&
                HardcodedUiTargetedPatchManager.Status == "ready" &&
                HardcodedUiTargetedPatchManager.AppliedCount == 0 &&
                !File.Exists(HardcodedUiTargetedPatchManager.ManifestPath))
                return "当前没有 DLL 译文，无需应用";
            if (!UIInterceptionModeController.LegacySelected && !succeeded &&
                HardcodedUiTargetedPatchManager.Status == "rejected" &&
                HardcodedUiTargetedPatchManager.LastError ==
                    "no compatible display arguments; existing translations retained")
                return "DLL 译文已保存，但没有找到可替换的显示文字；具体条目及原因见日志";
            return UIInterceptionModeController.LegacySelected
                ? "DLL 译文已保存，UI 拦截模式下暂停应用"
                : (succeeded ? "DLL 补丁清单已应用" : "DLL 补丁清单应用失败");
        }
    }
}
