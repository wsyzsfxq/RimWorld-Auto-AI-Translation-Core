# 1.7 启动失败与 Mod 名单调整

- 日期：2026-11-06。
- 来源：用户实际启动后保存的日志，以及本机 Mod XML、程序集文件版本和贴图文件头。
- 游戏已由部署时的 `1.7.4998 rev1299` 更新到本次启动的 `1.7.5000 rev1275`。
- 处理：恢复原 121 项名单中的 116 项，暂时停用下表 5 项；仅写入 ModsConfig.xml，未改产品源码、DLL、第三方 Mod 文件或用户译文。
- 状态：配置已保存，必需依赖和保留顺序已作静态核对；未启动游戏、未编译或执行测试，调整效果未经运行验证。

## 游戏为什么自动关闭 Mod

本次 `Logs/Player.log` 记录的启动时间是 `2026-11-06 20:39:33 +08:00`。第 923 行明确写明：加载游戏数据时捕获异常，重置 Mod 配置后重试。异常栈为 `ThingDefGenerator_Buildings.NewFrameDef_Thing` → `ImpliedBlueprintAndFrameDefs` → `GenerateImpliedDefs_PreResolve`。重置前第 910 行还报告建筑没有分配蓝图类型。

因此，直接触发全名单重置的是建筑蓝图/框架生成异常，而不是用户手动取消 Mod。异常没有打印当时正在处理的具体 Def 名，不能把下列某一项认定为唯一原因。

## 暂时停用

| Mod / Package ID | 证据及处理理由 |
|---|---|
| Prepatcher / `zetrith.prepatcher` | 日志 291–298 行明确报告 Harmony 加载冲突，旧版来自 Workshop `2934420800/Assemblies/0Harmony.dll`，版本为 2.4.2；1.7 Harmony 自身为 3.0.0。旧库抢先加载，DragSelect 随后找不到 HarmonyInfix。暂时撤下该旧库的来源。 |
| Yet another Optimizer / `sz.yaopt` | 日志 234、253、272 行列出三处 YaOpt Free patches 因泛型类型/字段句柄问题未应用。按用户“有问题先不启用”的新要求暂时停用；不再沿用此前仅在两个冲突项目中选择保留它的决定。 |
| 百里墨的逆重飞船拓展 / `hailuan.gravshipexpanded` | 日志 619 行的 BLM_GravcorePowerGenerator 找不到 GravshipComponentBase。该父定义在 Odyssey 的 Buildings_Gravship.xml，而当前 1.7 只安装并启用 Royalty。扩展 About 没声明必需 DLC，之前的声明依赖筛选漏掉了这项实际依赖。此无效建筑定义与后续蓝图/框架异常直接相关，但尚无运行对照证明它是唯一触发项。 |
| 堂丸武器贴图重置 / `tw.tangs.retexture.weapons` | 日志 656–657 行报告 Patches_Mechanoid.xml 对 Gun_HellsphereCannon 的 graphicData 操作失败，文件中确有无条件目标操作。暂时停用这一个武器贴图包。 |
| 菜篮子工程—刀耕火种 / `shenpc.vegetablebasketproject.slashandburnfarming` | 日志 3178 行报告 Workshop `3689683396` 的 Agricultural workstation.png 内容与扩展名不符；文件头为 FF D8 FF E0（JPEG），不是 PNG。该错误出现在重置后，但文件格式问题可以独立确认。仅停用刀耕火种扩展，菜篮子主包和其他无相同证据的扩展保留。 |

重新读取了原名单中 Mod 的当前 About 必需依赖，并检查了移除后的先后顺序；没有发现还需要连带移除的已启用项目。原先因缺 DLC、前置或用户选择而未启用的项目，没有在本轮重新加入。

## 没有据此扩大停用范围

- DragSelect 的 HarmonyInfix 类型缺失与日志明确指出的旧 Harmony 有关，本轮移除旧库来源；未把它直接判成独立的不兼容 Mod。是否恢复仍待用户下次正常启动确认。
- Adaptive Storage 系列图形初始化、部分图标/材质缺失、VEF 字段缺失与 HugsLib 空贴图警告大量发生在重置之后。此时第三方代码仍留在进程中，但游戏数据已重新按 Core/Royalty 加载，不能据此将这些全部认定为独立故障并停用。
- 没有以“未声明 1.7”作为停用理由，没有删除缓存、修改译文或绕过游戏的自动恢复机制。

## 保存和备份

证据及配置目录：`E:/Documents/Codex交付/RimWorld1.7启动失败_20261106`。

- `Player.failed.log`：本次完整失败日志副本；原日志保留。
- `ModsConfig.failed121.xml`：失败时预设的 121 项名单副本。
- `ModsConfig.reset.xml`：游戏重置为 Core/Royalty 后的名单。
- `ModsConfig.after.xml`：本轮保存的 116 项名单。
- `adjustment.json`：停用原因、配置哈希和未改动设置文件的哈希记录。

实际配置为默认用户目录的 `Config/ModsConfig.xml`，保存版本字段为 `1.7.5000 rev1275`，已启用 Core、Royalty 和 114 个第三方 Mod。Prefs 和 ATC 模型/语言设置文件内容哈希未变；数据库、译文、存档和 Steam 订阅均未修改。

本轮没有重新启动游戏，不能宣称剩余 116 项已经能完整加载。此前部署只核对 About 声明依赖，未覆盖实际 XML 的隐含 DLC 依赖和自带库冲突，这一限制已由此次真实启动日志补充。
