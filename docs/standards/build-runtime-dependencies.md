# 编译与运行依赖

恢复项目声明的 NuGet 包后，可取得 `Mono.Cecil 0.11.6` 的 `Mono.Cecil.dll`。各游戏版本的项目需引用适用的程序集，并将其随安装包提供。1.5、1.6 项目引用包中的 `lib/net40/Mono.Cecil.dll`，新增 RW17 项目引用 `lib/netstandard2.0/Mono.Cecil.dll`，均设置为复制到构建输出；无需按 RimWorld 版本单独编译 Cecil。

RW17 工程面向 .NET 10，共用现有源码，引用 1.7 游戏根目录的程序集及 Harmony 的 1.7 程序集。Newtonsoft.Json 使用包内 `net6.0`，System.Data.SQLite 使用包内 `netstandard2.0`。输出和中间文件与 1.5/1.6 分开。2026-11-04 已针对 1.7.4995 生成本机测试包并部署，构建记录见[1.7 测试交付](../reports/rw17-test-delivery-2026-11-04.md)；未经运行验证，尚不代表完成发布验收。

SQLite 配套库通过仓库根目录下的 `tools/prepare-sqlite-runtime.ps1` 整理，安装包需包含对应平台的运行库及校验文件。脚本从已恢复的 `System.Data.SQLite 2.0.4` 和 `SourceGear.sqlite3 3.53.4` 包中复制文件，同时准备 `1.6`（net471 提供器）和 `1.7`（netstandard2.0 提供器）两个版本目录的 `Assemblies` 与 `Native/sqlite`。SQLite 启动加载按编译常量选择版本目录：`RIMWORLD_1_7` 读取 `1.7`，其余（1.5/1.6）读取 `1.6`。

用户数据库由 Mod 首次使用时创建；已有数据库继续使用，并按需升级表结构，无需预置到安装包。

主 DLL 采用选定源码提交的构建结果，连同所需运行依赖和 Mod 资源组成发布包。
