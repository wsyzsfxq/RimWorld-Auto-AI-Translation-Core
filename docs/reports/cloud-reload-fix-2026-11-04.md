# 云端下载后重载失败：日志定位与修复

日期：2026-11-04。状态：读取用户本机实际日志定位，源码已修改；已生成 1.7 修复产物，未经修复后的运行验证。

后续范围调整：2026-11-06 用户确认将“能明确确认字段不存在”的旧译文路径也改为跳过、警告并保留文件，见[启动译文与界面调整](startup-loading-and-ui-2026-11-06.md)。下文对字段路径错误的失败处理是 11 月 4 日的历史范围；其他未知或真实赋值错误仍需回传失败。

## 本次日志说明了什么

1. `01:49:55`，云端 4 个 Mod 下载均成功。
2. `01:49:56`，状态同步完成：8 个译文文件、216 条文件条目，候选数据库条目为 0。当前无候选记录不能据此认为下载失败。
3. 随后重载读取了 49 条 Keyed、167 条 Def 译文。游戏报告：

   `Found no Verse.ThingCategoryDef named BuildingsNeatStorage to match BuildingsNeatStorage.label ()`

4. 原代码把这一个不存在的目标计入 `InjectionFailed`，最终返回 `false`，外层因而显示 `Translation memory reload failed.`。
5. `01:50:27` 手动热重载时重复发生；同时 DLL 清单不存在，却被算成 DLL 重载失败。

对应译文保存在当前游戏的 `Mods/!Translation_AI_Pack/Languages/ChineseTraditional/DefInjected/ThingCategoryDef/sbz_neatstorage_AutoTranslated.xml`。源 Mod 本地当前可读 XML 没有该 ThingCategoryDef，日志也已确认本轮运行时没有这个 Def；不据此推断它具体在哪个版本删除、改名或为何没有加载。

日志位置：

- `C:\Users\liyue\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log`
- `E:\Program Files\steam\steamapps\common\RimWorld\Mods\!Translation_AI_Pack\AutoTranslation_Log.txt`

## 修改

- 应用 Def 译文前，沿用游戏自身的 TKey 路径归一化、旧 Def 名兼容映射和 Def 查询方式，判断当前游戏是否存在对应 Def。
- 目标不存在时，本次不提交注入，不计入已提交数量；文件和数据库保持原样。日志记录暂不适用数量和第一条目标。
- 目标存在但字段路径、实际注入或清理失败时，继续保留原有失败回传。没有把所有注入错误统一改为成功，也没有修改游戏、第三方 Mod 或用户译文文件。
- 启动提前应用和普通分帧加载复用上述处理，诊断中均标出暂不适用数量。
- 当前进程尚未应用过 DLL 清单、也没有保留任何补丁时，缺少清单视作没有 DLL 工作；显示“当前没有 DLL 译文，无需应用”。坏清单、格式错误、曾应用过的清单丢失和补丁应用失败继续报告失败。

静态读取本机 1.6 / 1.7 的 `DefInjectionPackage.SetDefFieldAtPath`，确认上述 TKey、旧名映射和 `GetDefSilentFail(..., false)` 的顺序与游戏一致。没有执行游戏程序集来验证行为。

## 交付边界

修复产物由现有 RW17 工程生成，0 个错误、3 个既有警告。ZIP 为 `E:\Download\ATC_V4.6_RW1.7_20261104_fix1.zip`，校验记录放在 `E:\Documents\Codex交付\ATC_1.7_20261104_fix1`。

准备修复包时 Steam 1.7 进程仍在运行，因此未覆盖游戏正在使用的 DLL。安装后需要完全重启游戏，已有云端译文无需重新下载或重新调用 AI 翻译。没有自动测试、启动游戏或操作游戏界面；修复是否消除实际报错仍待用户手动验收。

后续检测到游戏已退出，已完成本地部署：旧主 DLL 备份在交付目录的 `before-deployment` 中，构建输出、仓库包、Steam 本地开发版三份主 DLL 均为 1,758,208 字节，SHA-256 均为 `8AF4FB1B675743D40AADAEF3E30E5642D0B86A5D0E558DAC882302D82CF4E764`。40 个运行文件与修复包清单一致；没有改配置、用户数据库或译文，也没有启动游戏。

用户随后反馈的多语言混杂另见[原始包核对记录](cloud-language-mix-review-2026-11-04.md)，不属于本次重载报错修复已解决的范围。
