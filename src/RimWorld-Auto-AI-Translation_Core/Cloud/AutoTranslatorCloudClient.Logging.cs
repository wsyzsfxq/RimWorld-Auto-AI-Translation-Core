using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorCloudClient
    {

        private static async Task<T> WaitForCloudTask<T>(Task<T> task, int timeoutSeconds, string operationName)
        {
            Task timeoutTask = Task.Delay(Math.Max(5, timeoutSeconds) * 1000);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            if (completedTask != task)
            {
                throw new TimeoutException($"{operationName} timed out after {timeoutSeconds}s");
            }

            return await task;
        }


        private static void LogCloudMessage(string message)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Message("[ATC Cloud] " + message));
        }


        private static void LogCloudWarning(string message)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Warning("[ATC Cloud] " + message));
        }


        private static void LogCloudError(string message)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Error("[ATC Cloud] " + message));
        }


        private static void LogCloudTranslatedMessage(string key, int arg)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Message("[ATC Cloud] " + key.Translate(arg)));
        }


        private static void LogCloudTranslatedWarning(string key, string arg)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Warning("[ATC Cloud] " + key.Translate(arg)));
        }


        private static void LogCloudTranslatedError(string key, string arg)
        {
            ATC_Dispatcher.RunOnMainThread(() => Verse.Log.Error("[ATC Cloud] " + key.Translate(arg)));
        }

    }
}
