using RimWorld;
using System;
using System.Diagnostics;
using System.IO;
using Verse;

namespace AutoTranslator_Core
{
    internal static class DesktopFileOpener
    {
        internal static bool OpenFolder(string path, bool createIfMissing = false)
        {
            try
            {
                if (createIfMissing) Directory.CreateDirectory(path);
                if (!Directory.Exists(path))
                {
                    Messages.Message(AutoTranslatorMod.WfText("文件夹还不存在。", "The folder does not exist yet."),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return OpenExistingPath(Path.GetFullPath(path));
            }
            catch (Exception ex) { return ReportFailure(ex); }
        }

        internal static bool OpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Messages.Message(AutoTranslatorMod.WfText("文件还不存在。", "The file does not exist yet."),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }
                return OpenExistingPath(Path.GetFullPath(path));
            }
            catch (Exception ex) { return ReportFailure(ex); }
        }

        private static bool OpenExistingPath(string path)
        {
            // .NET 10 defaults to executing paths as programs. Ask the desktop
            // shell to open directories/files with the user's normal application.
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }

        private static bool ReportFailure(Exception error)
        {
            AutoTranslatorSettings.AddWarningLog("打开文件或文件夹失败：" + error.Message);
            Messages.Message(AutoTranslatorMod.WfText(
                "打开失败，请查看日志中的具体原因。", "Could not open it. See the log for details."),
                MessageTypeDefOf.RejectInput, false);
            return false;
        }
    }
}
