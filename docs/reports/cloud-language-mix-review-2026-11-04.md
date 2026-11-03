# 云端译文混语：截图与原始包核对

日期：2026-11-04。状态：完成本地文件、配置、当前游戏程序集及指定云端原始 ZIP 的只读核对。没有重新调用 AI、修改云端记录、覆盖用户译文或自动操作游戏。

## Neat Storage 的韩文已经存在于云端原始包

截图中的 `고기걸이`、`금고`、`높은 진열대`，可分别对应下载文件中的 `sbz_MeatHook.label`、`sbz_Safe.label`、`sbz_TallDisplayShelf.label`。

- 云端记录：`sbz.neatstorage_ChineseTraditional_1791283696305`。
- 请求语种：`ChineseTraditional`；服务端响应 `X-ATC-Record-Id` 也明确返回同一记录，没有返回另一条韩语记录。
- 重新只读获取该记录的原始 ZIP，`DefInjected/ThingDef/sbz_neatstorage_AutoTranslated.xml` 共 54 条，其中 24 条含韩文，其余包含繁体中文。
- 原始 ZIP 中该 XML 的 SHA-256 为 `D56BE5395414D753A9A00A50AE32CFD0E9A2ACED9B5B67A254D56853B5E794F8`，与当前 `Languages/ChineseTraditional` 文件、`Upload_Workspace/sbz.neatstorage/ChineseTraditional` 下载副本完全一致。

因此，这次韩文来自云端返回的繁中包本身；当前客户端下载、解压和显示没有将另一份韩语包混进这份 XML。上传者交付时已经混入，还是服务端入库或打包时混入，尚无上传原件或服务端处理记录可区分，不能直接归责于上传者。

核对用原始 ZIP 和响应头保存在 `E:\Documents\Codex交付\云端语种核对_20261104`。本次获取的是具体用户问题所需的源数据，没有调用模型接口或执行网络测试。

## 界面的变音字是开发者模式的缺翻标记

当前 `Prefs.xml` 保存 `devMode=True`。静态读取本机 1.7 `Verse.Translator.Translate`：当前语言没有对应 Keyed 译文时，回退默认英文；开发者模式会调用 `PseudoTranslated`，将英文字母替换为带变音符号的形式。

本机 Adaptive Storage - Global Settings 的 1.7 英文 Keyed 共 144 个键，本次云端 `nuanki_adaptivestorageglobalsettings_Translation.xml` 只有 43 个；其中缺少 101 个当前键，包括截图标题对应的 `ASGS_CatalogTitle`。所以该旧译文包也没有覆盖当前 1.7 的全部界面。不能把所有带变音符号的英文认成另一种外语。

已经有旧键译文而仍显示旧内容的情况，还可能涉及 Mod 自身缓存；本轮未对所有界面字段逐一作缓存归因，也未声称关闭开发者模式能够补齐汉化。

## 本地测试版的目标语种与原配置不同

- 游戏 `Prefs.xml`：`ChineseSimplified (简体中文)`。
- 旧创意工坊配置 `Mod_3721659501_AutoTranslatorMod.xml`：`TargetLang=Simplified`、`CloudTargetLang=Simplified`。
- 当前本地测试版读取另一份 `Mod_AI_TranslationCore_Dev_AutoTranslatorMod.xml`：没有序列化这两个默认字段，按当前源码默认值均为 `Traditional`；`HasManualTargetLanguage=True`，因此不会自动跟随游戏改回简体。
- 实际下载的四条记录和新写入目录也都为 `ChineseTraditional`，与上述配置一致。

本地测试版部署没有把旧创意工坊版设置自动迁入新文件，这是当前需要处理的配置衔接事项。但当前配置又明确标记手工目标，不能未经确认覆盖成另一个语种。本轮保留两份设置，没有替用户决定简体或繁体。

这能解释本次为什么下载繁中；但无法解释混入韩文，韩文仍是上述云端包内容的问题。

## 与重载修复的关系

前一个 `fix1` 修复不存在 Def 导致整次重载失败、没有 DLL 工作仍报失败的问题；它没有修改这份云端包中的韩文，也没有补译 1.7 的新键。混语问题和界面补译目前仍待处理。
