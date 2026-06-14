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
    public enum TranslatorProvider { Google, OpenAI, DeepSeek, Grok, GLM, Alibaba, OpenRouter, DeepL, Custom_OpenAI }
}
