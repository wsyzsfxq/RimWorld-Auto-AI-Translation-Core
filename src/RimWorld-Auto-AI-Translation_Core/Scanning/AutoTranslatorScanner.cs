using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Verse;
using static AutoTranslator_Core.DeleteTranslationWindow;

namespace AutoTranslator_Core
{
    public static partial class AutoTranslatorScanner
    {
        // 🌟 咪咪特製：絕對不可翻譯的黑名單 (包含官方 DLC 與大哥的新模組 ID)
        private static readonly HashSet<string> BlacklistedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
            "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey",
            "auto.aitranslation.core", "aitranslation.pack" 
        };
        // Unity 限制：DefDatabase / LanguageDatabase 等 API 必須在主執行緒呼叫
        // 背景執行緒（Task.Run）發出的注入請求會被排隊，由 GameComponent 在下個 Tick 派發
        private static readonly object _pendingInjectLock = new object();
        private static bool _pendingMemoryDrop = false;
        private static Dictionary<string, string> GlobalPrimaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryDefDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalPrimaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> GlobalSecondaryKeyedDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly TranslationValidationStats _validationStats = new TranslationValidationStats();

        private class TranslationValidationStats
        {
            public int NewlineFixed;
            public int RulePrefixFixed;
            public int TokenFixed;
            public int StructureFallback;
            public int XmlKeySkipped;
            public int EnglishResidualDetected;
            public int EnglishResidualRetried;
            public int EnglishResidualFallback;

            public void Reset()
            {
                NewlineFixed = 0;
                RulePrefixFixed = 0;
                TokenFixed = 0;
                StructureFallback = 0;
                XmlKeySkipped = 0;
                EnglishResidualDetected = 0;
                EnglishResidualRetried = 0;
                EnglishResidualFallback = 0;
            }
        }
        // 🌟 咪咪原本就在這的標籤清單（大哥，這段要留著喔！）
        private static readonly HashSet<string> ExactTextTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "label", "description", "jobString", "reportString", "text", "labelShort", "customLabel", "descriptionShort",
            "pawnLabel", "gerund", "verb", "deathMessage", "inspectString", "baseInspectString", "helpText",
            "letterLabel", "letterText", "message", "messageSuccess", "messageFailed", "rejectInputMessage",
            "skillLabel", "endMessage", "beginLetterLabel", "beginLetter", "recoveryMessage", "destroyedLabel",
            "pawnSingular", "pawnPlural", "leaderTitle", "adjective", "royalFavorLabel", "arrivalText", "arrivalTextEnemy",
            "logRulesInitiator", "logRulesRecipient", "useLabel", "ingestCommandString", "ingestReportString",
            "meatLabel", "corpseLabel", "discoverLetterTitle", "discoverLetterText", "letterLabelEnemy", "letterTextEnemy",
            "commandLabel", "commandDescription", "formatString", "outfitName", "labelNoun", "labelNounPretty",
            "customSummary", "summary",
            "rulesStrings"
        };

        // 🌟 咪咪新增：標籤黑名單
        // 🌟 咪咪新增：從開源模組移植過來的「絕對不能翻」標籤清單 ＋ 咪咪的 RimWorld 底層防禦包
        private static readonly HashSet<string> BlacklistedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    // ===== 第一類：資源路徑與特效 =====
    "alienRace", "texPath", "graphicPath", "soundDef", "effecter",
    "iconPath", "shader", "soundCast", "soundCastTail", "soundInteract",
    "soundHitPawn", "soundMiss", "soundMeleeHit", "soundMeleeMiss",
    "soundAmbience", "linkSound", "fleckDef",

    // ===== 第二類：Def 引用 =====
    "thingDef", "itemDef", "pawnKindDef", "hediffDef", "recipeDef",
    "researchProjectDef", "terrainDef", "traitDef", "skillDef",
    "damageDef", "weaponDef", "apparelDef", "projectileDef",

    // ===== 第三類：底層程式變數 =====
    "defName", "dollName", "dollPartName", "methodName", "class", "worker",

    // ===== 第四類：NL Facial Animation 系列（修正 Bug A/G）=====
    // 解決：NL 系列種族頭部、眼部、嘴部貼圖消失問題
    "eyeTexPath", "browTexPath", "lidTexPath", "lashTexPath",
    "mouthTexPath", "noseTexPath", "earTexPath", "hairTexPath",
    "headTexPath", "bodyTexPath", "skinTexPath",
    "eyeballTexPath", "irisTexPath", "pupilTexPath",
    "expressionPath", "animationPath", "facialDef",
    // 🌟 咪咪緊急追加：各種可能漏網的貼圖與模型路徑 (防止 HAR 與臉部模組破圖)
    "texture", "texturePath", "path", "graphicPath", "maskPath",
    "headGraphicPath", "bodyGraphicPath", "crownGraphicPath",
    "frontTexPath", "sideTexPath", "backTexPath",
    // ===== 第五類：Humanoid Alien Races (HAR) 框架 =====
    // 解決：米莉拉、沃芬等人工種族身體頭部消失
    "bodyGraphicData", "headGraphicData", "graphicData",
    "bodyAddon", "bodyAddons", "headAddons", "bodyPart",
    "skinColorChannel", "hairColorChannel", "channelName",
    "linkedBodyPartsGroup", "renderNodeProperties",
    "maskPath", "shaderType", "subPath",
    "targetJobs", "animationFrames", "faceAnimationDef",
    "browOffset", "lidOffset", "headOffset", "mouthOffset",
    "noseOffset", "earOffset", "eyeballOffset", "eyeballOffsetL", "eyeballOffsetR",
    "layerOffset", "angle", "scale", "drawSize", "offset", "offsets",

    // ===== 第六類：通用引用型欄位（防誤翻）=====
    "li_ref", "parent", "parentName", "abstract", "inherit",
    "compClass", "thingClass", "race", "category", "categories",
    "tradeTags", "weaponTags", "apparelTags", "tags",
    "linkFlags", "renderNodeTagDef", "tagDef"
};        // 🌟 咪咪新增：檔案格式偵測 Regex
        private static readonly Regex FilePathRegex = new Regex(@"\.(png|jpg|jpeg|wav|mp3|ogg|xml|txt|lua|tex|dds)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ProtectedTokenRegex = new Regex(@"(\{[^{}\r\n]+\}|\[[^\[\]\r\n]+\])", RegexOptions.Compiled);
        // XML 名稱合法性檢查正則 (符合 W3C XML 1.0 NameStartChar / NameChar 規範簡化版)
        private static readonly Regex ValidXmlNameRegex = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_\-\.]*$",
            RegexOptions.Compiled
        );

    }
        // Scanner workflow methods are split into partial files in Scanning/AutoTranslatorScanner.*.cs.
}
