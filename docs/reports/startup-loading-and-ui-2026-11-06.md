# 启动译文失败定位与界面调整

> 后续交付：用户要求部署测试后，已完成 [1.7 编译、文件同步及指定脚本启动](rw17-startup-ui-delivery-2026-11-06.md)。下文“未编译/未部署”描述初次源码修改时的状态；本次产物已交付，功能仍未经运行验证。

- 日期：2026-11-06。
- 来源：用户提供的启动异常、三张设置/云端列表截图、后续明确选择，以及本机当前 Player.log 和译文文件。
- 状态：已修改源码、语言资源及文档，仅静态审阅；未编译、未部署、未执行自动测试或运行验收，未提交或推送。

## 启动加载失败的直接原因

本机日志 `C:/Users/liyue/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log` 在 22:12:29 记录了三条字段不存在的错误：

| 译文路径 | 当前运行时对象没有的字段 |
|---|---|
| `VocalLinkCatalyst.comps.0.useLabel` | `RimWorld.CompProperties_Forbiddable.useLabel` |
| `SBP_ChefsNotes.comps.1.useLabel` | `Verse.CompProperties.useLabel` |
| `SBP_MasterChefRecipe.comps.1.useLabel` | `Verse.CompProperties.useLabel` |

对应错误位于原日志第 4361、4379、4397 行，22:12:09 的提前应用也有同样错误。第 4431 行记录 Keyed=4283、提交 Def=3、暂不适用 Def=4747、成功=否；Def=3 表示提交量，不能据此认定三条 Def 已成功显示。随后启动任务收到 false，抛出用户提供的通用 `Translation memory reload failed`。

三条数字组件路径存在于当前生成包的 `Languages/ChineseSimplified/DefInjected/ThingDef/cj_rimtalk_AutoTranslated.xml` 和 `shenpc_shoppingbasketprogrambumperharvest_AutoTranslated.xml`。RimTalk 文件还保留了 `VocalLinkCatalyst.comps.CompUsable.useLabel`。原始厨师笔记 XML 在可用组件下确有 useLabel，但原始 XML 的数字位置不能证明当前运行时也是同一位置。本轮不据此猜测正确索引，也不声称新分析能自动修复这些旧键。

已有 4747 条缺失 Def 的译文已正常按旧规则跳过，不是本次失败触发项；4283 条 Keyed 的处理也不会因最后的通用异常自动回滚。

## 用户确认及实现

此前记录明确要求现存 Def 的字段错误继续回传失败。本轮先说明该限制，用户明确选择“跳过失效路径并警告，其余继续加载”。

- 提交变更译文前，按 TKey 归一化和旧 Def 名兼容规则定位当前对象，沿字段、数字列表位置及游戏提供的列表名称查找目标；字段查找兼容 LoadAlias。
- 只有能够明确确认当前对象没有对应字段时，才跳过该条，不加入实际注入集合、不计入提交数；日志列出跳过数量及前三条路径和原因，逐条调试日志保留细节，明确这些译文未显示。
- 空对象、越界位置、自定义索引器或检查本身异常不能一概视为失效。部分字符串列表允许翻译扩充数量，因此继续交给游戏原有注入逻辑判断；字段类型、禁止翻译属性及实际赋值错误也继续由游戏处理并回传失败。
- 保留文件、数据库和已有译文，不猜测新的组件位置。启动提前应用和普通分帧加载使用同一检查；手动重载沿用普通加载入口。

静态读取本机 1.7 游戏程序集的元数据和 IL，确认 `DefInjectionPackage.GetFieldNamed` 的字段/LoadAlias 查找以及公开的 `TranslationHandleUtility.GetElementIndexByHandle`；没有调用游戏方法或执行项目代码。

## 界面与默认值

1. “应用 DLL 译文”的字段初值和 Scribe 缺失字段默认值同时改为 true；明确保存的 false 继续生效。该开关仍只在 DLL 分析模式应用，不改变当前模式选择。
2. UI 拦截模式的四个开关横排一行：启用 UI 拦截、自动翻译新文字、悬停显示原文、翻译日志窗口文字。按实际文字分配宽度，窄处使用短名称及完整提示，保留原有模型配置检查、停止处理和保存行为。面板高度由 268/308 缩至 200/240，不留下原三行的空白。
3. 云端列表“一键空投”去掉前置云朵图标，同步八份语言资源。空投、详情及版本选择使用相同绘制字体测量文字并留出边距，从右向左独立分配位置，名称区域使用剩余宽度；避免固定 80 宽度裁切长按钮文字。

## 交付边界

本轮只做源码/资源修改和静态调用链、默认值及布局尺寸审阅。没有编译、打包、部署或启动游戏；没有自动操作界面或截图验证。当前安装版本是否已包含其他会话的交付不在本次核对范围，本轮修改尚未由本会话交付到游戏。
