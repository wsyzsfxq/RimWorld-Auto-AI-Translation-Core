# 1.7 启动译文与界面修复部署

> 后续修正：用户实际截图指出两种模式的动作按钮宽度及留白仍不一致，已另行完成[布局统一并重新部署](ui-mode-layout-unification-2026-11-06.md)。本页 DLL 大小及哈希属于前一次交付记录，不代表当前最新版。

- 日期：2026-11-06。
- 授权：用户要求“部署测试”；执行 prepare-rimworld-test 与 build-rimworld-mod 中的交付流程，按本轮实际使用的 1.7 目标编译、同步、核对三份 DLL，并调用指定启动脚本。
- 范围：[DLL 缺失类型处理](dll-missing-types-analysis-2026-11-06.md)、[失效字段路径及界面调整](startup-loading-and-ui-2026-11-06.md)，以及工作区已有的 Keyed 回滚和 UI 模式设置调整。
- 状态：已生成产物、复制文件并启动指定脚本；未自动操作游戏或执行测试步骤，未经运行验证，未提交或推送。

## 目标与产物

| 项目 | 实际结果 |
|---|---|
| 游戏版本 | `1.7.5000 rev1275` |
| 工程 | `RimWorld-Auto-AI-Translation_Core/RimWorld-Auto-AI-Translation_Core.RW17.csproj`，Release，.NET 10 |
| 构建工具 | `C:/Users/liyue/.codex/tools/dotnet10/dotnet.exe msbuild`，沿用此前已记录的 1.7 工具链；关闭构建分析器，未执行测试/lint |
| 编译结果 | 0 个错误、3 个既有警告：两处 ServicePointManager 过时提示、一处 TranslationBatchItemResult.Value 未赋值提示 |
| 安装目录 | `E:/Program Files/steam/steamapps/common/RimWorld/Mods/AI_TranslationCore_Dev` |
| 官方 Package ID | `Auto.AITranslation.Core`；仓库与安装目录一致，About.xml 未修改 |
| 当前启用名单 | 原有 116 项，ATC 已启用；未改名单 |
| 主 DLL 大小 | 1,835,520 字节 |
| 主 DLL SHA-256 | `715AD36F0177A50CA0B1498E3B907B056C2DF324EE54A91F5FF9D28CD210FC9F` |

以下三份主 DLL 的大小和 SHA-256 相同：

1. `E:/MyDocuments/Code/RimWorld-Auto-AI-Translation-Core/RimWorld-Auto-AI-Translation_Core/bin/Release-RW17/RimWorld_Auto_AI_Translation_Core.dll`
2. `E:/MyDocuments/Code/RimWorld-Auto-AI-Translation-Core/rimworld-mods/AI_TranslationCore/1.7/Assemblies/RimWorld_Auto_AI_Translation_Core.dll`
3. `E:/Program Files/steam/steamapps/common/RimWorld/Mods/AI_TranslationCore_Dev/1.7/Assemblies/RimWorld_Auto_AI_Translation_Core.dll`

## 同步与保留

核对 39 个运行文件：1.7 托管依赖/各平台 SQLite 配套库、Defs、Languages 与 LoadFolders.xml。依赖及其余资源已相同，本次实际替换主 DLL 和八种语言的空投按钮文本资源。同步只复制变化文件，不镜像删除，不复制 About.xml，也不触及 Steam Workshop 订阅目录。

安装目录的旧 DLL、八份旧语言文件和仓库原 1.7 主 DLL 已备份。启动前对 524 个配置、生成包译文和数据库文件进行前后内容哈希核对，均相同；未修改存档、模型配置、语言选择、已有译文或数据库。上述哈希核对在启动游戏之前完成，游戏后续正常写入不属于此次复制核对范围。

## 指定脚本启动

三份 DLL 和资源核对完成后，调用 `E:/Program Index/游戏/Run_RimWorld_Test.bat`。该脚本在此前交付中已改为启动当前 Steam 1.7，本轮只读检查，没有修改或换用其他入口。

脚本返回 0，随后观察到 PID `9464` 的 `E:/Program Files/steam/steamapps/common/RimWorld/RimWorld.exe`。这只说明脚本已调用并创建游戏进程，不代表模组、译文显示、启动完成或界面已经验收。没有自动操作界面、读取新运行日志来执行验收或继续测试。

## 备份与记录

`E:/Documents/Codex交付/ATC_1.7_20261106_启动与界面修复`：

- `build-rw17.log`：本次编译日志。
- `before-deployment/`、`before-repository/`：实际替换文件的旧版本。
- `deployment.json`：目标、版本、变化文件及三份 DLL 哈希。
- `runtime-files.json`：39 个运行文件的内容清单。
- `protected-before.json`：启动前核对的用户文件哈希，不包含配置正文或凭据。
- `launcher.json`：指定脚本返回值和观察到的进程。
- `deploy.ps1`：本次部署操作的留存记录。
