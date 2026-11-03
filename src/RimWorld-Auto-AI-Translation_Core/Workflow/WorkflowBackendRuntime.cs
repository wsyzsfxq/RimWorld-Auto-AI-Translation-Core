using System;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core.Workflow
{
    public static class WorkflowBackendRuntime
    {
        private static readonly object Gate = new object();
        private static WorkflowBackend _backend;
        private static readonly object StartupGate = new object();
        private static bool _startupQueued;
        private static int _playDataGeneration;
        internal static int PlayDataGeneration => Volatile.Read(ref _playDataGeneration);

        public static WorkflowBackend GetOrCreate()
        {
            lock (Gate)
            {
                if (_backend != null) return _backend;
                if (string.IsNullOrWhiteSpace(AutoTranslatorMod.CoreModRoot))
                    throw new InvalidOperationException("Core mod root is unavailable.");
                AutoTranslatorScanner.EnsureWorkflowPackStorageReady();
                _backend = WorkflowBackend.CreateDefault(
                    AutoTranslatorMod.CoreModRoot,
                    AutoTranslatorScanner.GetLocalPackPath());
                return _backend;
            }
        }

        internal static void InvalidateStartupTranslationLoading()
        {
            lock (StartupGate)
            {
                Interlocked.Increment(ref _playDataGeneration);
                _startupQueued = false;
            }
        }

        public static void QueueStartupTranslationLoading()
        {
            QueueStartupTranslationLoading(PlayDataGeneration);
        }

        internal static void QueueStartupTranslationLoading(int generation)
        {
            lock (StartupGate)
            {
                if (_startupQueued || generation != _playDataGeneration) return;
                _startupQueued = true;
            }
            _ = RunStartupTranslationLoadingAsync(generation);
        }

        private static async Task RunStartupTranslationLoadingAsync(int generation)
        {
            bool completed = false;
            try
            {
                if (AutoTranslatorMod.Settings == null) return;
                while (!completed)
                {
                    while (generation == PlayDataGeneration && (WorkflowTaskCoordinator.Instance.IsBusy ||
                           AutoTranslatorSettings.LegacyPipelineIsRunning))
                        await Task.Delay(500);
                    if (generation != PlayDataGeneration) return;
                    if (!WorkflowTaskCoordinator.Instance.TryBegin(
                            WorkflowTaskKind.StartupSynchronization,
                            "Startup translation injection",
                            out WorkflowTaskLease lease))
                    {
                        await Task.Delay(500);
                        continue;
                    }
                    using (lease)
                    {
                        try
                        {
                            lock (StartupGate)
                            {
                                if (generation != _playDataGeneration) throw new OperationCanceledException();
                                AutoTranslatorSettings.ResetPipelineCancellation();
                            }
                            WorkflowTaskCoordinator.Instance.ReportStage(
                                string.Empty,
                                "读取本地缓存译文并注入游戏",
                                "⏳ 启动译文加载：读取本地缓存译文并注入游戏",
                                "workflow.startup-injection begin; database-candidates=skipped");
                            WorkflowTaskCoordinator.Instance.ReportProgress(
                                0, 1, string.Empty, "读取本地缓存译文并注入游戏",
                                subCompletedUnits: 0, subTotalUnits: 1,
                                writeRuntimeLog: false);
                            Task<bool> reload;
                            lock (StartupGate)
                            {
                                if (generation != _playDataGeneration) throw new OperationCanceledException();
                                reload = AutoTranslatorScanner.RequestMemoryDropAsync();
                            }
                            bool reloadSucceeded = await reload;
                            if (generation != PlayDataGeneration) throw new OperationCanceledException();
                            if (!reloadSucceeded)
                                throw new InvalidOperationException("Translation memory reload failed.");
                            lease.CancellationToken.ThrowIfCancellationRequested();
                            WorkflowTaskCoordinator.Instance.ReportProgress(
                                1, 1, string.Empty, "本地缓存译文注入完成",
                                subCompletedUnits: 1, subTotalUnits: 1,
                                writeRuntimeLog: false);
                            WorkflowTaskCoordinator.Instance.MarkTerminal(
                                lease.RunId, WorkflowRunState.Completed);
                            AutoTranslatorSettings.AddLog("✓ 启动译文加载完成");
                            completed = true;
                        }
                        catch (OperationCanceledException ex)
                        {
                            WorkflowTaskCoordinator.Instance.MarkTerminal(
                                lease.RunId, WorkflowRunState.Cancelled, ex.Message);
                            throw;
                        }
                        catch (Exception ex)
                        {
                            WorkflowTaskCoordinator.Instance.MarkTerminal(
                                lease.RunId, WorkflowRunState.Failed, ex.Message);
                            throw;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Warning("[AutoTranslationCore] Startup translation loading was cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error("[AutoTranslationCore] Startup translation loading failed: " + ex);
            }
            finally
            {
                if (!completed)
                {
                    lock (StartupGate)
                    {
                        if (generation == _playDataGeneration) _startupQueued = false;
                    }
                }
            }
        }
    }
}
