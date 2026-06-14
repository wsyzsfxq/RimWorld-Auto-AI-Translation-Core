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
        public class ATC_Dispatcher : MonoBehaviour
        {
            // 拋棄傳統 Queue 與 lock，改用 Thread-Safe 的 ConcurrentQueue (耗能幾乎為 0)
            private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> executionQueue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            private static int _lastDispatcherWarningFrame = -1;
            private static ATC_Dispatcher _instance;
            private static readonly object _ensureLock = new object();

            public static void EnsureAlive()
            {
                if (_instance != null) return;
                if (!UnityData.IsInMainThread) return;

                lock (_ensureLock)
                {
                    if (_instance != null) return;

                    GameObject go = new GameObject("ATC_Dispatcher_Engine");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ATC_Dispatcher>();
                }
            }

            // 開放給任何背景 Task 呼叫，把任務無鎖推入序列
            public static void RunOnMainThread(Action action)
            {
                if (action == null) return;
                if (UnityData.IsInMainThread) EnsureAlive();
                else if (_instance == null && AutoTranslatorMod.MainThreadContext != null)
                {
                    try
                    {
                        AutoTranslatorMod.MainThreadContext.Post(_ => EnsureAlive(), null);
                    }
                    catch { }
                }
                executionQueue.Enqueue(action);
            }

            private void Awake()
            {
                if (_instance != null && _instance != this)
                {
                    UnityEngine.Object.Destroy(gameObject);
                    return;
                }

                _instance = this;
                UnityEngine.Object.DontDestroyOnLoad(gameObject);
            }

            private void OnDestroy()
            {
                if (_instance == this)
                {
                    _instance = null;
                }
            }

            // Unity 引擎的生命週期
            public void Update()
            {
                AutoTranslatorScanner.PumpMainThreadDispatcher();

                // TryDequeue 本身具備原子性操作 (Atomic)，沒有任何 lock 阻塞問題！
                // 只要裡面有東西，它就會瞬間抽出來執行；沒東西時，這行幾乎不花費任何 CPU 週期。
                int processed = 0;
                while (processed < 64 && executionQueue.TryDequeue(out Action action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        if (_lastDispatcherWarningFrame != Time.frameCount)
                        {
                            _lastDispatcherWarningFrame = Time.frameCount;
                            Verse.Log.Warning($"[AutoTranslationCore] Main-thread action failed: {ex.Message}");
                        }
                    }
                    processed++;
                }
            }
        }
}
