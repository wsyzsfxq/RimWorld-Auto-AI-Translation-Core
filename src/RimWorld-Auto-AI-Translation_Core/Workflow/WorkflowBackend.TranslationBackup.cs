using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AutoTranslator_Core.Workflow.Synchronization;

namespace AutoTranslator_Core.Workflow
{
    public sealed partial class WorkflowBackend
    {
        // Called only under TranslationBackupManager's existing exclusive task lease.
        internal List<TranslationBackupOrigin> CaptureTranslationBackupOrigins(ICollection<string> files)
        {
            // An installation using only legacy UI cache need not create a database
            // just to back up translations. Available XML provenance is archived too.
            if (!File.Exists(_repository.DatabasePath)) return new List<TranslationBackupOrigin>();
            EnsureDatabaseInitialized();
            return _repository.GetTranslationBackupOrigins(files);
        }

        internal void SynchronizeTranslationBackupFiles(ICollection<TranslationBackupOrigin> origins)
        {
            EnsureDatabaseInitialized();
            var languages = new HashSet<string>(_repository.GetTranslationBackupLanguages(), StringComparer.Ordinal);
            languages.Add(WorkflowRuntimeSettings.GetTargetLanguageFolder());
            string languageRoot = Path.Combine(_generatedPackRoot, "Languages");
            if (Directory.Exists(languageRoot))
                foreach (string folder in Directory.GetDirectories(languageRoot)) languages.Add(Path.GetFileName(folder));
            var issues = new List<string>();
            foreach (string language in languages)
            {
                // A user-selected snapshot supersedes earlier interrupted writes/deletes.
                // Replaying them here could recreate translations removed by the restore.
                foreach (PendingFileOperationRecord pending in _repository.GetPendingFileOperations(language))
                    _repository.AbandonPendingFileOperation(pending.OperationId, "Superseded by a manual translation backup restore.");
                TranslationSynchronizationResult result = _synchronizer.Synchronize(
                    CancellationToken.None, language, restoredFilesAreAuthoritative: true, restoredOrigins: origins);
                if (result.ReadErrorEntries > 0 || result.FailedPendingOperations > 0)
                    issues.Add(language + " 的部分译文状态未能同步");
                if (result.InvalidEntries > 0)
                    issues.Add(language + " 有 " + result.InvalidEntries + " 条译文需要重新确认");
            }
            if (issues.Count > 0) throw new IOException(string.Join("；", issues) + "。请查看工作台状态和同步日志。");
        }
    }
}
