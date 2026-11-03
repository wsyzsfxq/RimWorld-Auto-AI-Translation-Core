# RimWorld 1.7 静态审核与本机测试交付

日期：2026-11-04。状态：已完成静态审核、1.7 产物构建和本机部署；未经运行验证。

## 审核范围与结论

审核当前工作区的 1.7 工程、网络收发及取消、SQLite 加载、两种 UI 模式、启动译文提前应用和后续分帧加载。沿用[上一轮修复](rw17-followup-static-review-2026-11-02.md)，本轮静态阅读未再发现阻断交付的明确缺陷；这不代表业务功能已经通过游戏验收。

本机 Steam 游戏已是 `1.7.4995 rev309`。读取当前 `Assembly-CSharp.dll`、`UnityStubs.dll` 的元数据及相关 IL，核对 Label、LabelFit、Tooltip、日志窗口、Job.GetReport、语言注入和游戏数据清理入口。实际入口、参数与当前补丁相符；启动钩子仍由 Mod 构造阶段安装。

- 1.7 模型请求使用 .NET 10 的 HttpClient / SocketsHttpHandler；1.6 保留现有 UnityWebRequest，两版共用翻译业务流程。
- SQLite 从对应版本的运行库目录加载。用户数据库继续位于当前游戏 `Mods/!Translation_AI_Pack/Database/AutoTranslationCore.sqlite3`，交付没有读取、移动或清空数据库。
- 旧 UI 模式保留独立 JSON 缓存；新版观察和 DLL 应用继续受各自模式开关控制。
- 启动提前应用只使用已准备好的本轮 XML 缓存；未赶上启动回调时仍走普通分帧加载。复杂 DLL 字段、分支传递等既有覆盖限制仍在。

## 构建产物

使用微软官方 .NET 10 SDK `10.0.403` 的 MSBuild 构建 `RimWorld-Auto-AI-Translation_Core.RW17.csproj`，Release / AnyCPU，关闭构建期间的分析器。没有执行测试、lint 或游戏。

SDK 位于 `C:\Users\liyue\.codex\tools\dotnet10`。原有 Visual Studio 完整框架 MSBuild 未能加载便携 SDK 的任务依赖，因此改用该 SDK 自带的 MSBuild；1.6 工程和原有构建工具保持原样。[微软 SDK 安装说明](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)

构建结果：0 个错误、3 个警告。两个警告来自旧 `ServicePointManager.SecurityProtocol` 设置；它们不能配置当前 SocketsHttpHandler，1.7 原生传输继续使用系统 TLS 协商。另一个警告来自未赋值的 `TranslationBatchItemResult.Value`，静态检索只找到该类型和辅助方法定义，未发现当前生产调用链依赖该字段。本轮未为消除警告扩展代码修改范围。

本次编译分支 `feature/dll-dry-run-for-translation` 的未提交工作区，不是仅编译分支 HEAD。没有提交或推送。

主 DLL：`RimWorld_Auto_AI_Translation_Core.dll`。

- 大小：1,757,184 字节。
- SHA-256：`AA8A8BAA23BE239F52FCBF2DE52EF19A39F41E40AFD6B8E87E050D669576D8C5`。
- 目标框架：`.NETCoreApp,Version=v10.0`，引用当前 1.7 游戏程序集和 Harmony 3。

## 打包与部署

本轮仅准备 1.7：主 DLL、Mono.Cecil、Newtonsoft.Json、System.Data.SQLite、四个平台的 SQLite 原生库及校验文件，以及 About、Defs、Languages、LoadFolders.xml。Harmony 由独立 Mod 提供，不捆绑游戏程序集、用户数据库或配置。

仓库包新增对应的 `1.7/Assemblies` 编译产物，1.6 主 DLL 未被本轮覆盖。独立测试包和本地部署的 supportedVersions / LoadFolders 只声明实际包含的 1.7，官方 ID 保持 `Auto.AITranslation.Core`。

- 部署目录：`E:\Program Files\steam\steamapps\common\RimWorld\Mods\AI_TranslationCore_Dev`。
- ZIP：`E:\Download\ATC_V4.6_RW1.7_20261104.zip`。
- 构建日志、文件清单和三份 DLL 核对记录：`E:\Documents\Codex交付\ATC_1.7_20261104`。

这是首次创建该本地目录。没有覆盖创意工坊订阅、现有翻译包、存档或游戏配置。1.7 的 ModLister 会让同 ID 的本地 Mod 优先于 Workshop 副本；原启用 ID 可以继续使用，本轮没有修改 ModsConfig.xml。

构建输出、仓库包和部署目录三份主 DLL 的字节数与 SHA-256 相同；40 个运行文件在暂存包与部署目录间逐一核对内容一致。以上只说明产物和复制一致，不是游戏功能验收。

没有调用现有 `Run_RimWorld_Test.bat`：它指向隔离的 1.6 安装，本轮目标为 Steam 1.7，由用户手动启动。没有启动其他入口。

## 测试环境的已知问题

后台 agent 检查了部署前的全部 134 个启用项，发现默认配置仍沿用 1.6 Mod 清单，存在缺失包、缺依赖、未声明 1.7 的第三方 Mod，以及旧版本 DLC 资源。逐项依据和建议见[1.7 已装 Mod 检查报告](rw17-installed-mod-review-2026-11-04.md)。报告安装数量是部署前快照，本轮已另外新增上述本地 ATC。

测试时需区分环境本身的错误与 ATC 的错误。没有自动更新、停用其他 Mod 或修改 DLC。
