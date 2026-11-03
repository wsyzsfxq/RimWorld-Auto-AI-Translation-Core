# 设置修改与 ASF / ASGS 未显示译文核对

状态：源码已修改；仅作静态判断，未编译、未运行、未发送模型请求。

后续状态：本文保留设置修改和缓存问题刚定位时的记录。下方“尚未实施”的缓存方案，已由[官方 Keyed 显示更新实现](keyed-localization-display-2026-11-04.md)接续实施；最终构建与交付状态见[修复包 2 汇总](rw17-fix2-delivery-2026-11-04.md)。

## 已修改的设置与入口

| 用户反馈 | 当前源码处理 |
|---|---|
| 参考字典单独摆在常用栏目外，名称难懂 | 移入“常用与界面显示”，按钮名缩短为“参考字典”，提示说明可以查看自带词条、添加或修改译法。 |
| 只有右侧箭头能展开栏目 | 公共栏目标题函数增加整条点击区域；箭头点击与整条点击短路处理，避免一次点击切换两遍。 |
| Mod 原名是一句说明，缺少开关 | 恢复“翻译 Mod 名称”开关，默认关闭。开启后只对工作台当前可见的非系统 Mod 名称排队，共享当前模型请求；显示“译名 / 原名”，不修改 Mod 元数据。 |
| 没配置模型也能点击调用模型的入口 | 复核、翻译、一键翻译、编辑器单条及批量翻译、连接检查、Mod 名称翻译、旧 UI 自动翻译/恢复入口按可用配置控制。无配置提示“未配置大模型”。一键翻译同时检查复核和翻译用的配置；型号不能用于复核时另作提示。关闭已开启的自动翻译仍然允许。 |
| 默认任务用量、输出量及超时 | 新默认分别为 500 万原字符、100K 最大输出 Token、180 秒；已有明确保存值仍沿用。 |
| “AI 接口”标题 | 改为“大模型配置”。 |
| 调试后出现无标题统计框 | 改为带标题的“运行统计”，说明仅在调试级别展示请求耗时、译文刷新耗时、条目数量；不是配置项。 |
| Harmony 固定不参与翻译 | 共用保护名单已加入 `brrainz.harmony`；工作台显示“基础组件，不作为翻译目标”。 |
| “打开新版译文编辑器” | 改为“打开翻译编辑器”，相关提示也去掉“新版”。 |
| 打开历史数据文件夹导致 Win32Exception(5) | 使用共用 `DesktopFileOpener`，明确 `UseShellExecute=true`，交给桌面打开已有文件夹/文件；异常转为清楚提示，不再直接抛出到设置窗口。工作区文件夹、日志文件、旧 UI 缓存文件夹也接入。原本已经明确使用桌面打开方式的导出入口保留。 |
| “恢复最近备份”没有对应的备份入口 | 现有入口改为“还原替换前译文”，说明它是逐个 Mod 的自动备份，不是整份数据快照。用户后续提出的手动备份另行按确认范围实施。 |

### Mod 名称翻译的边界

新增缓存沿用旧 `Cache/ModNameTranslations.json` 位置及旧键规则：目标语言、Package ID、原名共同决定缓存键。首次读取放在后台；列表只查询内存。请求前捕获目标语言及原名，异步结果按捕获值保存，不会把某语种译名记到另一个语种。文件采用临时文件替换。

名称翻译获取全局任务占用，与分析、复核、翻译等后台工作互斥；停止按钮可以取消，关闭该开关也会取消尚未完成的请求。读取可见名称时不改 Mod 本身的名称或 Package ID。没有模型时不会新建名称请求，已有缓存仍可显示。

## 截图中的未翻译文字属于 XML

### Adaptive Storage Framework

本机 Workshop ID `3033901359`。它没有单独的 1.7 目录，当前提供的是 `1.6/Assemblies/AdaptiveStorageFramework.dll`。该文件按程序集元数据读取，未执行代码；结果与附带源码 `Source/AdaptiveStorage/Strings.cs`、`AdaptiveStorageFrameworkSettings.cs` 一致。

截图文字来自 `Languages/English/Keyed/Keys.xml`：

| 原文 | XML 键 | 当前简体译文 |
|---|---|---|
| Contents tab to display on adaptive storage buildings: | `ASF_SettingContentsTabSelection` | 自适应存储建筑上显示的内容标签页： |
| Automatically open contents tab | `ASF_AutomaticallyOpenContentsTab` | 自动打开内容标签页 |
| Default label style | `ASF_DefaultLabelStyleSetting` | 默认标签样式 |
| Hide labels when zoomed out | `ASF_HideLabelsZoomSetting` | 缩小时隐藏标签 |
| Maximum zoom level | `ASF_MaxZoomLevelSetting` | 最大缩放级别 |
| Hide labels until mouseover | `ASF_HideLabelsMouseOverSetting` | 鼠标悬停前隐藏标签 |

当前 SQLite 中上述候选全部存在，简体译文状态为 `Translated`。对应中文也存在于生成包的 `Languages/ChineseSimplified/Keyed/adaptive_storage_framework_AutoTranslated.xml`。

实际程序集 `AdaptiveStorage.Strings/Translated::.cctor()` 的过程是：建立 `Translator.TranslateSimple` 委托，再调用 `AssignAllStringFields`，给静态字符串字段一次性赋值。设置窗口之后读取这些已存好的字段；它不会在每次绘制时重新查语言表。因此，后来写入并热重载 XML，并不会自动替换已经缓存的旧显示文字。

该 Mod 的 DLL 分析另有 `UnityEngine.CoreModule` 无法解析的真实错误，已由独立修复处理。这个错误与截图中这些 XML 文字已经翻译却未显示是两件事。

### Adaptive Storage - Global Settings

本机 Workshop ID `3530785055`，Package ID `NuanKi.AdaptiveStorageGlobalSettings`，当前分析目标为 `1.7/Assemblies/AdaptiveGlobalSettings.dll`。

| 截图原文 | 当前 1.7 XML 键 |
|---|---|
| Storage settings | `ASGS_CatalogTitle` |
| Mod settings | `ASGS_CatalogOptions` / `ASGS_Text_ModSettings` |
| Search buildings or mods... | `ASGS_CatalogSearch` |
| All mods / All buildings / Modified | `ASGS_CatalogAllMods` / `ASGS_CatalogAll` / `ASGS_ModifiedFilter` |
| Storage defaults / SETTINGS / Overview | `ASGS_UI_BuildingDefaults` / `ASGS_UI_Sections` / `ASGS_UI_Overview` |
| Storage / Behavior / Adaptive / Priority | `ASGS_UI_Storage` / `ASGS_UI_Behavior` / `ASGS_UI_Adaptive` / `ASGS_Priority` |
| Enabled / Disabled / Contained graphics / Original | `ASGS_UI_Enabled` / `ASGS_UI_Disabled` / `ASGS_UI_ContainedGraphics` / `ASGS_Text_Original` |

这些键在 `1.7/Languages/English/Keyed/StorageCatalog.xml`、`BuildingSettings.xml`、`Common.xml` 等文件中；当前 SQLite 及本地生成的简体 Keyed XML 已有中文译文。核对到的记录简体译文状态均为 `Translated`，并不是扫描漏掉了原文。

实际程序集 `AdaptiveStorageGlobalSettings.TranslationKeys::.cctor()` 对这些键逐条执行：

```text
原文键 → Translator.Translate → 转为普通 string → 写入静态字段
```

后续界面读取这些字段，同样没有重新查询语言表。已有的 DLL 显示替换器只从方法内字符串追踪到直接显示参数，不能因此自动接管这类跨字段缓存；DLL 补丁没有应用，也不代表这些 XML 键需要再送 AI 翻译一遍。

## 缓存问题的最小后续方案

以下是当时提出的具体方案，彼时尚未实施；后续实现已接入，现状以[官方 Keyed 显示更新实现](keyed-localization-display-2026-11-04.md)为准：

1. 识别并记录“字段确实由哪个 XML 键翻译得到”。直接初始化链可以依据 `Translate(key)` 到 `stsfld` 的实际代码；ASF 的反射批量赋值需要另外证明其键来源，不能单凭字段名相同就认定。
2. 只在确认是界面显示参数的消费位置，用所记录的键取得当前译文并传给显示函数；原字段和内部逻辑继续保持原值。
3. 已经有中文的键直接使用本地译文，不重新调用 AI。没有当前译文或无法证明来源时保留原显示，并记录未覆盖的位置。
4. 字段到显示参数的流经分支、包装或自定义辅助函数时，需要明确哪些写法已支持；不能把“有译文”当成“这个位置已能刷新”。

这需要补上当前显示替换器的字段来源与消费位置支持。单纯扩大 XML/DLL 扫描数量、全局按原文替换、改写所有只读字段或重新执行静态初始化，都不能作为本次已经完成的修复。启动时提前应用已有译文有帮助，但当前提前读取为异步准备，不能据此保证每个 Mod 的静态字段初始化前都已经应用完毕；因此也不能承诺重启必然解决。

## 自动备份的真实范围

现有 `CreateBackupBeforeClear` 在清除/替换某个 Mod 的本地译文前，将匹配的 `Languages` 文件压到 `Backups/<PackageId>/<PackageId>_<时间>.zip`，每个 Mod 保留最近三份。它不是每次 AI 翻译都会生成的完整快照，不含数据库、API 配置或 DLL 应用清单。

现有恢复分别选择各个已加载 Mod 最近的 ZIP，覆盖压缩包内的文件，不删除之后新增的文件。不同 Mod 的备份可能来自不同时间。当前按钮改名和提示只如实表达这一行为；后续手动备份必须与它分开保存、分开恢复，不能混称为同一份完整备份。

用户随后确认的手动备份范围为 XML、DLL 和 UI 拦截的译文，不备份 SQLite 或模型配置。该项交由独立任务实施，本报告不将其列为已完成。

## 后续：统一下拉选择和紧凑开关

修复包 2 交付后，用户要求三处下拉选择统一采用供应商一栏的外观。源码已将“当前语言”“供应商”“界面翻译”接入同一个绘制方法：名称在框外左侧，蓝灰色选择框中只显示当前值和下拉箭头。语言区仍保留三列布局，模式切换的两个分支使用相同外观；没有改变选择、保存或默认值。

“显示翻译快捷按钮”“翻译 Mod 名称”使用与工作台相同的边框勾叉开关，保留原来的启用条件、模型检查、取消和保存逻辑。

工作台复核范围原来将名称和多项范围文字挤在七等分的一格中。现在将名称放在框外，选择框使用右侧剩余空间，完整范围放入悬停提示；与公共按钮共用单行、垂直居中的文字样式。没有通过各处硬改文字纵坐标来补偿裁切。

上述是修复包 2 之后的源码修改，仅静态阅读了布局、两种模式分支、点击回调和绘制状态；本任务没有编译、部署或运行验证，不能据此说当前游戏中的新外观已生效。
