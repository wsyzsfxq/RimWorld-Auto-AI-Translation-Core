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
    public class UpdateLogWindow : Window
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 InitialSize => new Vector2(750f, 700f);
        public UpdateLogWindow() { this.doCloseButton = true; this.doCloseX = true; this.forcePause = true; this.absorbInputAroundWindow = true; }
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 40f), "📜 " + "ATC_UpdateLog_Btn".Translate());
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 35f, inRect.width);
            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 100f);
            string logText = "ATC_UpdateLog_FullText".Translate();
            float textHeight = Text.CalcHeight(logText, inRect.width - 20f);
            Rect viewRect = new Rect(0, 0, inRect.width - 20f, Mathf.Max(textHeight + 50f, outRect.height));
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            Widgets.Label(new Rect(0, 0, viewRect.width, textHeight), logText);
            Widgets.EndScrollView();
        }
    }
}
