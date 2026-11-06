# 1.7 Keyed 回滚版部署与 Mod 名单

> 后续：用户实际启动 1.7.5000 后，游戏因蓝图/框架生成异常重置了名单。已依据启动日志暂时停用 5 项，恢复 116 项，详见[失败诊断及调整](rw17-startup-failure-2026-11-06.md)。下文 121 项属于原部署记录，不再代表当前名单。

- 完成日期：2026-11-06；工作开始于 2026-11-05。
- 范围：编译并部署[全局 Keyed 显示反查回滚](keyed-display-rollback-2026-11-05.md)，按用户要求预先配置已安装 Mod。
- 状态：产物已生成、文件已复制、名单已保存；未启动游戏，未经运行验证，未提交或推送。

## 编译与部署

- 目标：Steam RimWorld `1.7.4998 rev1299`，RW17 Release，.NET 10。
- 构建工具：本机 `.NET 10 SDK 10.0.403` 的 MSBuild。Visual Studio 的 .NET Framework MSBuild 无法加载此便携 SDK 的任务依赖，因此改用该 SDK 自带的 MSBuild；没有为此修改产品源码或工程引用。
- 编译：0 个错误、3 个既有警告；未执行测试、lint 或项目运行验证。
- 部署：`E:/Program Files/steam/steamapps/common/RimWorld/Mods/AI_TranslationCore_Dev`。
- 官方 Package ID：`Auto.AITranslation.Core`，未修改 About.xml。
- 主 DLL：1,830,400 字节，SHA-256 `225F7450F49B53D62B1934279E3D20778A379B5401E55E1B9CC3E5AF04B62384`。
- 构建输出、仓库 `1.7/Assemblies` 与安装目录三份 DLL 相同；核对了 39 个运行文件，本次实际替换主 DLL 和 `LoadFolders.xml`，其他依赖与资源相同。

## 启用名单

- 由原有 8 项增加至 121 项：Core、Royalty 和 119 个第三方 Mod，新增 113 项，原有项目保留。
- 按用户追加要求，未声明支持 1.7 不作为排除条件。这不代表已经运行确认所有 Mod 的兼容性。
- 以本机 Steam InstalledDepots 和 About 元数据核对，当前 1.7 仅 Royalty 有匹配的 DLC 安装记录；没有将四个旧版 DLC 残留目录当作有效依赖。
- 缺少必需 DLC、前置及其下游依赖的项目未启用。用户明确选择保留 Yet another Optimizer，未启用与其声明不兼容的模拟经营框架及依赖它的外卖扩展。
- 自动生成的 `AITranslation.Pack` 按其说明未启用；翻译文件仍保留供 ATC 正常读取。
- 加载顺序依据作者声明排列；Prepatcher、Harmony、Fishery 位于 Core 前，Royalty 位于 Core 后。必需依赖、互斥和先后顺序只作本地元数据检查，没有执行第三方代码。

## 备份和限制

交付记录目录：`E:/Documents/Codex交付/ATC_1.7_20261105_Keyed回滚`。

- `ModsConfig.before.xml`：修改前的 8 项名单。
- `ModsConfig.after.xml`：最终 121 项名单。
- `before-deployment`：被替换的安装文件。
- `before-repository`：更新前的仓库 1.7 DLL。
- `deployment.json`、`dll-three-copies.json`、`mod-plan.json`：实际部署、哈希、依赖及排除原因。
- `部署与Mod名单.md`：完整启用顺序和未启用项目。
- `build-rw17.log`：成功构建日志。

用户译文、数据库、存档、模型配置和语言选择没有修改；Prefs 与 ATC 设置文件的部署前后哈希相同。没有更改 Steam 订阅或第三方 Mod 文件。

固定隔离测试启动脚本仍指向 1.6，故本次没有调用它，也没有换用其他游戏启动入口。用户下次正常启动 Steam 1.7 即读取已保存的名单。没有帧率恢复或译文显示已验收的结论。
