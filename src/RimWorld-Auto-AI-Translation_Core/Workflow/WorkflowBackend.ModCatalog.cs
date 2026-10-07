using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoTranslator_Core.Workflow.Analysis;

namespace AutoTranslator_Core.Workflow
{
    public sealed partial class WorkflowBackend
    {
        public async Task<ModCatalogSummary> SynchronizeModCatalogAsync(Action<int, int, string> progress)
        {
            var installed = await NativeTranslationCatalog.CaptureInstalledModsAsync();
            return await RunExclusiveAsync(WorkflowTaskKind.ModCatalogSynchronization, "同步 Mod 列表",
                token => Task.Run(() =>
                {
                    var snapshots = new List<ModSnapshotRecord>();
                    int total = installed.Count * 2;
                    foreach (var mod in installed)
                    {
                        token.ThrowIfCancellationRequested();
                        snapshots.Add(NativeTranslationCatalog.Describe(mod));
                        progress?.Invoke(snapshots.Count, total, "读取 " + mod.Name);
                    }
                    ModCatalogSummary result = _repository.SynchronizeModCatalog(snapshots, token,
                        (completed, count, name) => progress?.Invoke(installed.Count + completed,
                            installed.Count + count, "更新 " + name));
                    progress?.Invoke(total, total, "记录已保存");
                    return result;
                }, token),
                CancellationToken.None);
        }
    }
}
