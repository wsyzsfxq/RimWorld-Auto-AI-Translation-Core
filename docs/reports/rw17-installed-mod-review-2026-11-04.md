# Steam 1.7 已安装 Mod 静态盘点

日期：2026-11-04。状态：仅静态检查本地文件和游戏程序集元数据，未启动游戏，未执行第三方 Mod 代码，未改配置、订阅或第三方文件，未在线查询更新。

本次清单在主线程部署本地 ATC 开发包之前截取。收尾时主线程已将当前 1.7 开发包部署到 `E:\Program Files\steam\steamapps\common\RimWorld\Mods\AI_TranslationCore_Dev`，原 Workshop 包保留；以下安装总数和来源表仍保留部署前快照，避免混用两次盘点。

## 结论

当前 Steam 游戏确实是 **1.7.4995 rev309**，但它准备沿用的默认配置仍是原先的 **1.6 Mod 列表**。不能把现有整套环境称为已经适配 1.7。已发现两个明确缺项，以及官方 DLC 资源混版；还有 52 个未声明 1.7 的代码 Mod，需要结合实际 1.7 更新或运行报错继续核对。

“未声明 1.7”“文件放在 1.6 文件夹”“没有找到待下载更新”分别是不同的事实，任何一个都不能单独证明运行不兼容或已兼容。1.7 的 Waylith 运行库提供大量旧 Unity、Prepatcher 接口兼容，部分旧 Mod 可能直接运行；其界面、补丁目标和行为仍需手工验收。

## 环境与范围

| 项目 | 本地证据 |
|---|---|
| Steam 游戏 | `E:\Program Files\steam\steamapps\common\RimWorld` |
| 实际游戏版本 | 根目录 `Version.txt`：`1.7.4995 rev309` |
| 运行库 | 根目录 `RimWorld.runtimeconfig.json`：`net10.0`，`10.0.13-waylithclr.dev`；含 UnityEngine → UnityStubs、旧 Prepatcher → RimWorldWaylith.PrepatcherCompat 的重定向 |
| 当前 Steam 启动参数 | 当前所有者对应 `userdata\201614847\config\localconfig.vdf` 中 RimWorld 的启动项仅 `-popupwindow`，没有指定独立存档目录 |
| 默认配置 | `C:\Users\liyue\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml`，版本字段仍是 `1.6.4871 rev591` |
| 当前启用清单 | 134 项：官方 6 项、第三方 128 项；其中第三方 127 项找到安装文件，1 项缺失 |
| 安装盘点 | 135 个可读 About 元数据目录：官方 6、本地 13、Workshop 116；部署本地 ATC 前未发现重复 packageId |
| 第三方声明情况 | 已安装且启用的 127 项中，52 项声明 1.7；75 项未声明 1.7。这 75 项不含官方 DLC，也不含缺失包 |
| 未声明 1.7 的 75 项 | 52 项会读取代码 DLL，23 项仅查到 XML、贴图、语言等资源；全部在下表逐项标明 |
| 既有日志 | 默认目录 `Player.log` 最后写于 2026-10-09，内容实际是隔离目录的 1.6 启动，不能用作本次 1.7 日志 |
| Steam 下载状态 | `steamapps\workshop\appworkshop_294100.acf` 中 `NeedsUpdate=0`、`NeedsDownload=0`。只表示该本地状态文件没有列出待处理下载，不证明每个作者都已发布 1.7 版 |

检查了全部启用项的 About、声明依赖和加载顺序、LoadFolders/版本文件夹、所选 Assemblies 下的 DLL，并读取相关 DLL 的程序集名称与类型引用。没有运行 Mod、没有连接模型 API、没有执行兼容测试。

## 明确缺项和需要先核对的环境问题

| 优先项 | 实际情况 | 处理意见 |
|---|---|---|
| RimTalk 补丁包缺失 | 配置第 108 项 `mjcg.rimtalkpatch` 在本地 Mods、116 个 Workshop 目录中均没有对应 About/packageId | 这是失效的启用项。需要确认是否仍需要这个补丁；需要则找到对应 1.7 包，不需要则在游戏 Mod 菜单移除。没有自动改配置 |
| Avian Genes 缺依赖 | `Workshop\3769643744\About\About.xml` 全局必需依赖包含 `Drifter.FlightFramework`，本机没有该包，也没有启用 | 补齐支持 1.7 的 [Flight Framework](https://steamcommunity.com/sharedfiles/filedetails/?id=3775887837)，或暂时不启用 Avian Genes。链接来自本地作者声明，未在线确认该包当前兼容状态 |
| 官方 DLC 资源混版 | Royalty 的 About 已声明 1.7、时间为 10 月 30 日；Ideology、Biotech、Anomaly、Odyssey 的 About 仍声明 1.6、时间为 7 月 30 日 | 先核对 Steam 内这些 DLC 的安装与更新状态。仅手改 supportedVersions 不能更新 DLC 内容；不能把目前目录算作四个 DLC 已完成 1.7 更新 |
| Steam 安装记录 | `appmanifest_294100.acf` 的 InstalledDepots 只列出本体与 Royalty，对应上述四个 DLC 没有安装 depot 记录 | 这是资源来源/更新状态需要核对的证据，不据此判断账号所有权。游戏还会依据 Steam 的 DLC 安装状态决定哪些官方包实际进入加载；配置里有启用项不等于它会成功加载 |

在“本机能找到对应元数据”的前提下，没有发现当前启用顺序违背 About 中已声明的依赖先后、loadBefore、loadAfter、forceLoadBefore、forceLoadAfter，也没有发现已声明的 incompatibleWith 同时启用项。该结论不涵盖作者未声明的兼容关系。

## 代码 Mod 的更新核对重点

52 个未声明 1.7 的代码 Mod 并非已经确定全部出错，优先关注以下几组：

| 范围 | 本地现状与原因 |
|---|---|
| 底层和性能 | Prepatcher、HugsLib、Adaptive Storage Framework、Ancot Library、Yet another Optimizer、Performance Optimizer 仍是旧声明/旧目录。它们改动加载或底层流程，出现错误会影响许多其他 Mod，适合优先核对 1.7 版本 |
| UI 与绘制 | Custom Portraits、Dynamic Portraits、Facial Animation、RimHUD、Allow Tool、Compact Hediffs、Dev In Your Language、DevelopMode fix 等仍读取旧代码；1.7 换引擎后这些界面和绘制补丁值得优先观察 |
| 外部网络 | Rimteller、RimTalk 轻量人格仍是旧声明。不能把 ATC 已适配的 1.7 HTTP 实现视为它们也适配了 |
| 种族与扩展 | Milira Race、Milira: Wings of Democracy、Anima Dragon、Avian Genes 仍是旧声明；Ariandel Library、Milira Imperium、Seraphim Viska 虽已有 1.7 内容，也不能替代上游基础包和兼容补丁的核对 |
| 本地小补丁 | RimWar Runtime Fixes、Sensible Factions 中文代码补丁、Seraphim Viska Low-Fly Arrival Fix/Threat Check Recursion Fix、Life Stage Reference Fix、Drop Pod Raid Jammer 中文/修复 DLL 均为 1.6 版本。需要针对当前 1.7 原 Mod 的方法和字段重新核对，不能只改版本号 |

本地 Harmony 已有专用 `1.7\Assemblies\0Harmony.dll`，程序集版本为 `3.0.0.0`。其他启用目录仍附带多个 Harmony：Prepatcher 与 Anima Dragon 为 `2.4.2.0`、Humanoid Alien Races 的 1.7 文件为 `2.4.3.0`、HugsLib 根目录为 `1.2.0.1`。这属于依赖组合需要观察的线索，不据此擅自删除任何一份或宣称已经发生版本冲突；实际加载与 Waylith 重定向有关。

对 Mod DLL 的类型引用静态读取，没有据此发现普遍的“Unity 类型全部消失”问题。PrepatcherImpl 对若干旧 Prepatcher 内部类型的引用，在内置兼容程序集没有同名定义；由于 1.7 同时重定向旧 Prepatcher 程序集、并可能替代旧实现，不凭这个差异直接判定 Prepatcher 必然无法运行。

## ATC 开发包的选取

截取清单时，`auto.aitranslation.core` 已启用，唯一已安装来源为：

`E:\Program Files\steam\steamapps\workshop\content\294100\3721659501`

主线程已部署当前开发源码的本地包。静态读取当前 1.7 的 `Verse.ModLister.RebuildModList/TryAddMod` 表明：游戏先读本地 Mods，再读 Workshop；一份本地与一份 Workshop 使用相同 packageId 时，Workshop 那份在运行时附加 Steam 后缀。原配置里的 `auto.aitranslation.core` 因而选取本地开发包，源 About 的官方 packageId 无需修改，也无需为此次部署删除 Workshop 订阅文件。实际加载目录仍需用户在首次 1.7 启动日志中确认。

## 全部 134 个启用项

以下是部署前快照。`需核对代码` 共 52 项、`需核对资源` 共 23 项，合计上述 75 项。`已声明1.7` 仅代表作者元数据声明，不代表本轮测试通过。`根目录` 表示 Mod 自身目录。

游戏 `InitLoadFolders` 的静态逻辑允许无 1.7 配置时回退现有版本，所以 `1.6` 目录不等于完全不加载。表中“目录”按当前配置推导，选取声明内的条件目录；如果 Steam 判定某 DLC 未安装，其相关条件目录会进一步变化。

| 顺序 | Mod | packageId | 判定 | 选取目录 | 来源 |
|---:|---|---|---|---|---|
| 1 | Prepatcher | zetrith.prepatcher | 需核对代码 | 根目录 | Workshop/2934420800 |
| 2 | Harmony | brrainz.harmony | 已声明1.7 | 根目录, 1.7 | Workshop/2009463077 |
| 3 | ludeon.rimworld | ludeon.rimworld | 官方本体 | 根目录 | Data/Core |
| 4 | ludeon.rimworld.royalty | ludeon.rimworld.royalty | 官方1.7声明 | 根目录 | Data/Royalty |
| 5 | ludeon.rimworld.ideology | ludeon.rimworld.ideology | 官方旧1.6声明 | 根目录 | Data/Ideology |
| 6 | ludeon.rimworld.biotech | ludeon.rimworld.biotech | 官方旧1.6声明 | 根目录 | Data/Biotech |
| 7 | ludeon.rimworld.anomaly | ludeon.rimworld.anomaly | 官方旧1.6声明 | 根目录 | Data/Anomaly |
| 8 | ludeon.rimworld.odyssey | ludeon.rimworld.odyssey | 官方旧1.6声明 | 根目录 | Data/Odyssey |
| 9 | Adaptive Storage Framework | adaptive.storage.framework | 需核对代码 | 1.6, 根目录 | Workshop/3033901359 |
| 10 | HugsLib | unlimitedhugs.hugslib | 需核对代码 | 根目录, v1.6 | Workshop/818773962 |
| 11 | Vanilla Expanded Framework | oskarpotocki.vanillafactionsexpanded.core | 已声明1.7 | 1.7, 根目录 | Workshop/2023507013 |
| 12 | [NL] Custom Portraits | nals.customportraits | 需核对代码 | 1.6, 根目录 | Workshop/1569605867 |
| 13 | [NL] Dynamic Portraits | nals.dynamicportraits | 需核对代码 | 1.6, 根目录 | Workshop/2253730555 |
| 14 | [NL] Facial Animation - WIP | nals.facialanimation | 需核对代码 | 1.6, 根目录 | Workshop/1635901197 |
| 15 | [Og] Rimteller: AI Powered Storyteller (Experimental) | og.rimteller | 需核对代码 | 1.6, 根目录 | Workshop/3638662307 |
| 16 | [sbz] Neat Storage | sbz.neatstorage | 已声明1.7 | 根目录 | Workshop/3416243474 |
| 17 | [TW1.6]堂丸贴图重置~UI Tang's~Retexture~UI | tw.tangs.retexture.ui | 需核对资源 | 根目录 | Workshop/3141849282 |
| 18 | [TW1.7]堂丸贴图重置~制成品 TangW's~Retexture~Manufactured | tw.tangs.retexture.manufactured | 已声明1.7 | 根目录 | Workshop/3058943402 |
| 19 | [TW1.7]堂丸贴图重置~原材料 TangW's~Retexture~Resource | tw.tangs.retexture.resource | 已声明1.7 | 根目录 | Workshop/3050448166 |
| 20 | [TW1.6]堂丸贴图重置~服饰 Tang's~Retexture~Apparel | tw.tangs.retexture.apparel | 需核对资源 | 根目录 | Workshop/3255510656 |
| 21 | [TW1.7]堂丸贴图重置~植物 TangW's~Retexture~Plants | tw.tangs.retexture.plants | 已声明1.7 | 根目录 | Workshop/3053738633 |
| 22 | [TW1.7]堂丸贴图重置~武器 TangW's~Retexture~Weapons | tw.tangs.retexture.weapons | 已声明1.7 | 根目录 | Workshop/3048306872 |
| 23 | [TW1.7]堂丸贴图重置~建筑 TangW's~Retexture~Building | tw.tangs.retexture.structure | 已声明1.7 | 根目录 | Workshop/3168625192 |
| 24 | [TW1.7]堂丸贴图重置~脸型 TangW's~Retexture~Face | tw.tangs.retexture.face.scar | 已声明1.7 | 根目录 | Workshop/3051527562 |
| 25 | [TW1.7]堂丸贴图重置~食物 TangW's~Retexture~Foods | tw.tangs.retexture.foods | 已声明1.7 | 根目录 | Workshop/3050762215 |
| 26 | [sbz] Mini Mech Rechargers | sbz.minimechrecharger | 已声明1.7 | 根目录 | Workshop/3554922769 |
| 27 | Adaptive Ideology Storage | adaptive.ideology.storage | 需核对资源 | 1.5, 根目录 | Workshop/3301337278 |
| 28 | Adaptive Old Simple Storage | danifox.adaptiveoldsimplestorage | 已声明1.7 | 根目录 | Workshop/3525371196 |
| 29 | Adaptive Primitive Storage | adaptive.primitivestorage | 需核对资源 | 1.5, 根目录 | Workshop/3400037215 |
| 30 | Adaptive Simple Storage | adaptive.simplestorage | 需核对资源 | 1.5, 根目录 | Workshop/3297307747 |
| 31 | Adaptive Storage - Global Settings | nuanki.adaptivestorageglobalsettings | 已声明1.7 | 根目录, 1.7 | Workshop/3530785055 |
| 32 | AI 智能漢化網絡(AI Translation Network) | auto.aitranslation.core | 已声明1.7 | 根目录, 1.7 | Workshop/3721659501 |
| 33 | Allow Tool | unlimitedhugs.allowtool | 需核对代码 | 根目录, v1.6 | Workshop/761421485 |
| 34 | Ancot Library | ancot.ancotlibrary | 需核对代码 | 1.6, Content, 1.6/Mods/Biotech | Workshop/2988801276 |
| 35 | Anima Dragon | drifter.animadragon | 需核对代码 | 根目录, 1.6 | Workshop/3745351876 |
| 36 | Ariandel Library | ariandel.ariandellibrary | 已声明1.7 | 1.7, Content, 1.7/Mods/Ideology, 1.7/Mods/Royalty, 1.7/Mods/Anomaly, 1.7/Mods/Nals.FacialAnimation, 1.7/Mods/Ancot.MiliraRace | Workshop/3665997350 |
| 37 | Avian Genes | drifter.aviangenes | 需核对代码 | 根目录, 1.6 | Workshop/3769643744 |
| 38 | Better Workbench Management | falconne.bwm | 需核对代码 | 1.6, 根目录 | Workshop/935982361 |
| 39 | Bionic icons | automatic.bionicicons | 需核对代码 | 1.6, 根目录 | Workshop/1677616980 |
| 40 | Character Editor | void.charactereditor | 已声明1.7 | 根目录, v1.7 | Workshop/1874644848 |
| 41 | Combat always collides | mute.nopawnstacking | 需核对代码 | 1.6, 根目录 | Workshop/2897894688 |
| 42 | Compact Hediffs | petetimessix.compacthediffs | 需核对代码 | 根目录, v1.6 | Workshop/2031734067 |
| 43 | Compact Hediffs_zh | lan.compacthediffs.zh | 已声明1.7 | 根目录 | Workshop/3256133883 |
| 44 | Damage Indicators [1.7] | caesarv6.damageindicators | 已声明1.7 | 1.4, 根目录 | Workshop/2016331497 |
| 45 | Dev In Your Language | latta.devl10n | 需核对代码 | 1.6, 根目录 | Workshop/2142743468 |
| 46 | DevelopMode fix | observer.developmode.fix | 需核对代码 | 1.6, 根目录 | Workshop/2879782112 |
| 47 | DragSelect | telardo.dragselect | 已声明1.7 | 1.7, 根目录 | Workshop/2599942235 |
| 48 | Dubs Mint Menus | dubwise.dubsmintmenus | 已声明1.7 | 1.7, 根目录 | Workshop/1446523594 |
| 49 | blueprints1.6-TEMP | dw.defi.fluffy.blueprints.fork | 需核对代码 | 根目录, 1.6 | Workshop/3718830257 |
| 50 | Dubs Mint Minimap | dubwise.dubsmintminimap | 已声明1.7 | 1.7, 根目录 | Workshop/1662119905 |
| 51 | Dynamic Trade Interface | zeracronius.dynamictradeinterface | 已声明1.7 | 根目录, 1.7 | Workshop/3020706506 |
| 52 | EdB Prepare Carefully | edb.preparecarefully | 需核对代码 | Common, 1.6 | Workshop/735106432 |
| 53 | FSharp.Core | latta.fsharp.core | 需核对代码 | 根目录 | Workshop/2059725183 |
| 54 | Gloomy Hair | gloomylynx.kotobike | 已声明1.7 | 根目录 | Workshop/2883462751 |
| 55 | Interaction Bubbles | jaxe.bubbles | 需核对代码 | 根目录 | Workshop/1516158345 |
| 56 | Killfeed | kahdeg.killfeed | 需核对代码 | v1.6 | Workshop/1362098265 |
| 57 | Level Up! | krafs.levelup | 已声明1.7 | 1.6, 根目录 | Workshop/1701592470 |
| 58 | LWM's Adaptive Deep Storage | asf.deepstorage | 需核对资源 | 根目录 | Workshop/3373064575 |
| 59 | Mark That Pawn | mlie.markthatpawn | 需核对代码 | 根目录, 1.6, Assets | Workshop/3056996662 |
| 60 | Mine Sight | rabiosus.minesight | 已声明1.7 | Common, 1.7 | Workshop/3769804600 |
| 61 | Music Manager (Continued) | zal.musicmanager | 已声明1.7 | 1.7 | Workshop/3220120457 |
| 62 | Nice Inventory Tab | andromeda.niceinventorytab | 已声明1.7 | 根目录, 1.7, NiceBillTab/1.7 | Workshop/3609897594 |
| 63 | No Random Relations | dvs.norandomrelations | 需核对代码 | 1.6, 根目录 | Workshop/2583377522 |
| 64 | Non uno Pinata (don't drop items) | avilmask.nonunopinata | 需核对代码 | 1.6, 根目录 | Workshop/1778821244 |
| 65 | OgreStack | ogre.ogrestack | 已声明1.7 | 1.7, 根目录 | Workshop/1447140290 |
| 66 | P-Music | peppsen.pmusic | 已声明1.7 | 根目录 | Workshop/725130005 |
| 67 | Pick Up And Haul | mehni.pickupandhaul | 需核对代码 | 1.6, 根目录 | Workshop/1279012058 |
| 68 | Pocket Sand | usagirei.pocketsand | 需核对代码 | 1.6, 根目录 | Workshop/2226330302 |
| 69 | Rim War | torann.rimwar | 需核对代码 | 根目录, v1.6 | Workshop/2222935097 |
| 70 | RimWar Runtime Fixes | lynn.rimwarsettlementgizmofix | 需核对代码 | 根目录, 1.6 | Mods/Lynn.RimWarGizmoFix |
| 71 | RimHUD | jaxe.rimhud | 需核对代码 | 根目录 | Workshop/1508850027 |
| 72 | Roads of the Rim (Continued) | mlie.roadsoftherim | 已声明1.7 | 1.7, 根目录 | Workshop/2280318231 |
| 73 | Sensible Factions | boots.sensiblefactions | 需核对代码 | 根目录 | Workshop/3531306011 |
| 74 | SimpleCameraSetting | ray1203.simplecamerasetting | 已声明1.7 | 1.6, 根目录 | Workshop/3232415388 |
| 75 | Smart Speed | sarg.smartspeed | 已声明1.7 | 根目录, 1.7 | Workshop/1504723424 |
| 76 | Stone Conversion Recipes | redram.stoneconversion | 需核对资源 | 根目录 | Workshop/2879659026 |
| 77 | Trait Rarity Colors | carnysenpai.traitraritycolors | 需核对代码 | Common, 1.6 | Workshop/1751884355 |
| 78 | Visible Raid Points | visibleraidpoints.1trickpwnyta | 需核对代码 | 1.6, 根目录 | Workshop/2562730174 |
| 79 | Who shot my leg off? | tixiv.whoshotmylegoff | 需核对代码 | 根目录 | Workshop/3491552121 |
| 80 | Won hair_men | won.hair | 需核对资源 | 1.6, 根目录 | Workshop/1879950403 |
| 81 | Won hair_women  | won2.hair | 需核对资源 | 1.6, 根目录 | Workshop/1886150617 |
| 82 | WVC - Work Modes | wvc.sergkart.biotech.moremechanoidsworkmodes | 已声明1.7 | 根目录, 1.7, 1.7/Odyssey | Workshop/2888380373 |
| 83 | Yayo's Animation (Continued) | com.yayo.yayoani.continued | 需核对代码 | 1.6, 根目录 | Workshop/2877292196 |
| 84 | Yet another Optimizer | sz.yaopt | 需核对代码 | Common, 1.6, 1.6/Mods/Facial Animation, 1.6/Mods/Humanoid Alien Races, 1.6/Mods/Performance Optimizer, 1.6/Mods/Vanilla Expanded Framework | Workshop/3718308218 |
| 85 | 中文人物名字 | wsh.rimpawnchinesename | 已声明1.7 | 1.7, 根目录 | Workshop/2883401175 |
| 86 | 百里墨的逆重飞船拓展 | hailuan.gravshipexpanded | 需核对代码 | 根目录, 1.6 | Workshop/3524562131 |
| 87 | 角色名字汉化补全PawnNameZH | kapa.pawnnamezh | 需核对代码 | 1.6, 根目录 | Workshop/2556008664 |
| 88 | [NL] Facial Animation - Experimentals | nals.facialanimationexperimentals | 需核对资源 | 1.6, 根目录 | Workshop/2581693737 |
| 89 | [sbz] Neat Storage_zh | zh.mzmgow.sbz.neatstorage | 已声明1.7 | 根目录 | Workshop/3424068498 |
| 90 | Adaptive Storage_zh | zh.adaptive.storage.neolithic | 已声明1.7 | 根目录, Cont, Cont/Adaptive Simple Storage, Cont/Adaptive Ideology Storage, Cont/Adaptive Primitive Storage, Cont/LWM's Adaptive Deep Storage, Cont/Adaptive Old Simple Storage, Cont/Adaptive Storage - Global Settings | Workshop/3050506223 |
| 91 | Allow Tool zh-pack | rwzh.chinesepack.allowtool | 已声明1.7 | 根目录 | Mods/SteamCMD_2016725307_AllowTool_zh |
| 92 | Dev In Your Language_zh | latta.devl10n.zh | 需核对资源 | 根目录 | Workshop/2574656057 |
| 93 | EdB Prepare Carefully - 精心准备 - 简&繁中文汉化 | cncg779.preparecarefully.cn | 已声明1.7 | Common, 根目录 | Workshop/3239734631 |
| 94 | Elite Raid 精英化袭击 | trigger.eliteraid | 已声明1.7 | 1.7, Common, 根目录 | Workshop/3492954974 |
| 95 | Nice Inventory Tab zh-pack | andromeda.niceinventorytab.zh | 已声明1.7 | 根目录 | Workshop/3610403738 |
| 96 | Non uno Pinata (don't drop items)简体中文汉化 | hecatessg.avilmask.nonunopinata.cn | 需核对资源 | 根目录 | Workshop/3232296662 |
| 97 | Rim_War_zh | rwzh.chinesepack.rimwar | 已声明1.7 | 根目录 | Workshop/2228758765 |
| 98 | RimHUD_zh | shouer.chinesepack.rimhud | 需核对资源 | 1.6, 根目录 | Workshop/1928161832 |
| 99 | RimTalk | cj.rimtalk | 已声明1.7 | 1.6, 根目录 | Workshop/3551203752 |
| 100 | Roads of the Rim zh-pack | rwzh.chinesepack.roadsoftherim | 已声明1.7 | 根目录 | Workshop/1926620840 |
| 101 | Sensible Factions 简体中文补丁（合理的势力分布） | lynn.sensiblefactions.zh | 需核对代码 | 根目录 | Mods/Sensible_Factions_ChineseSimplified |
| 102 | Useful Marks | andromeda.usefulmarks | 已声明1.7 | 1.7, 根目录 | Workshop/3506573327 |
| 103 | visibleraidpoints显示袭击点数  | visibleraidpoints.1trickponyta.cntrans | 需核对资源 | 根目录 | Workshop/2565846709 |
| 104 | WVC - Work Modes 简体中文补丁 | lynn.wvc.workmodes.chinesesimplified | 需核对资源 | 根目录 | Mods/WVC_Work_Modes_ChineseSimplified |
| 105 | Yayo's Animation_zh | zh.com.yayo.yayoani | 需核对资源 | 根目录 | Workshop/2585417219 |
| 106 | Humanoid Alien Races | erdelf.humanoidalienraces | 已声明1.7 | 根目录, 1.7, 1.7/Mods/Ideology, 1.7/Mods/Odyssey | Workshop/839005762 |
| 107 | RimTalk 轻量人格 | ow1r.rimtalklitep | 需核对代码 | 1.6, 根目录 | Workshop/3772693458 |
| 108 | 缺失包 | mjcg.rimtalkpatch | 未安装 | — | — |
| 109 | RimTalk（边缘世谭）简繁中文汉化 | maiya.rimtalk.zh | 已声明1.7 | 根目录 | Workshop/3569636392 |
| 110 | Useful Marks_zh 实用标记 简中汉化 | hours.usefulmarks.zh | 需核对资源 | 根目录 | Workshop/3507348026 |
| 111 | Milira Race | ancot.milirarace | 需核对代码 | 1.6, Content, 1.6/Mods/Royalty, 1.6/Mods/Biotech, 1.6/Mods/Odyssey | Workshop/3256974620 |
| 112 | Seraphim Viska | hailuan.angel | 已声明1.7 | 根目录, 1.7 | Workshop/3775396880 |
| 113 | Seraphim Viska Low-Fly Arrival Fix | lynn.angellowflyarrivalfix | 需核对代码 | 根目录, 1.6 | Mods/Lynn.AngelLowFlyFix |
| 114 | Seraphim Viska - Advanced Helmet Fit | lynn.angeladvancedhelmetfit | 需核对资源 | 根目录 | Mods/Lynn.AngelAdvancedHelmetFit |
| 115 | Seraphim Viska Threat Check Recursion Fix | lynn.angelthreatdisabledfix | 需核对代码 | 根目录, 1.6 | Mods/Lynn.AngelThreatDisabledFix |
| 116 | Milira:Wings of Democracy | sleepycot.wingsofdemocracy | 需核对代码 | 根目录, 1.6, 1.6/Mods/Anomaly, 1.6/Mods/Odyssey | Workshop/3626456960 |
| 117 | Milira Faction: Milira Imperium | ariandel.miliraimperium | 已声明1.7 | 1.7, Content, 1.7/Mods/Ideology, 1.7/Mods/Anomaly, 1.7/Mods/Odyssey, 1.7/Mods/Nals.FacialAnimation, 1.7/Mods/sleepycot.wingsofdemocracy | Workshop/3588393755 |
| 118 | RimWar - Milira Peaceful Group Fix | lynn.rimwarmilirapeacefulgroupfix | 需核对资源 | 根目录 | Mods/Lynn.RimWarMiliraPeacefulGroupFix |
| 119 | Life Stage Reference Fix | lynn.lifestagereferencefix | 需核对代码 | 根目录, 1.6 | Mods/Lynn.LifeStageReferenceFix |
| 120 | Dubs Performance Analyzer | dubwise.dubsperformanceanalyzer.steam | 已声明1.7 | 1.7, 根目录 | Workshop/2038874626 |
| 121 | Performance Optimizer | taranchuk.performanceoptimizer | 需核对代码 | 1.6, 根目录 | Workshop/2664723367 |
| 122 | Performance Optimizer zh | optimizer.zh | 需核对资源 | 根目录 | Workshop/2924800081 |
| 123 | Centralized Climate Control (Continued) | mlie.centralizedclimatecontrol | 需核对代码 | 根目录, 1.6, Assets | Workshop/2007268173 |
| 124 | [NL] Dynamic Portraits_zh | zh.nals.dynamicportraits | 需核对代码 | 根目录 | Workshop/3461359451 |
| 125 | LK Wall Sun Lamp | lk.sunlamp | 需核对资源 | 根目录 | Workshop/3546512512 |
| 126 | LK Wall Sun Lamp 简繁中文汉化 | liyue.lk.sunlamp.zh | 需核对资源 | 根目录 | Mods/LKWallSunLamp_ZH |
| 127 | Thrumbo Extension | leafzxg.thrumboextension | 已声明1.7 | 1.6, 根目录 | Workshop/859466666 |
| 128 | Drop Pod Raid Jammer | kongkim.droppodjammer | 需核对代码 | 1.6, 根目录 | Workshop/3527131849 |
| 129 | Drop Pod Raid Jammer 简体中文汉化 | liyue.droppodjammer.zh | 需核对代码 | 1.6, 根目录 | Mods/DropPodRaidJammer_ZH |
| 130 | Pick Up And Haul - Shared Storage Access | sugarcube.puahsharedstorage | 需核对代码 | 根目录 | Workshop/3774922633 |
| 131 | [Og] Repair Your Gear | og.repair.your.gear | 已声明1.7 | 根目录, 1.6 | Workshop/3513376486 |
| 132 | [SYR] Metallic Batteries (Continued) | mlie.syrmetallicbatteries | 已声明1.7 | 1.7, 根目录 | Workshop/3345223519 |
| 133 | [SYR] Metallic Batteries zh | syrchalis.metallicbatteries.jackwzw.zh | 需核对资源 | 根目录 | Workshop/3012151817 |
| 134 | Drop Pod Raid Jammer - Ritual Race Fix | liyue.droppodjammer.fix | 需核对代码 | 1.6 | Mods/DropPodRaidJammer_Fix |

来源路径说明：`Data/…`、`Mods/…` 均在上述 Steam 游戏目录内；`Workshop/…` 为 `E:\Program Files\steam\steamapps\workshop\content\294100\…`。各行的版本声明、依赖和目录选取可分别追溯到该目录内的 `About/About.xml`、`LoadFolders.xml` 和 `Assemblies` 文件。

没有更新或禁用第三方 Mod，没有移动存档、配置、数据库或 `!Translation_AI_Pack`。不同游戏安装目录的数据保持独立。本报告只说明本地静态状态，功能与界面仍未经运行验证。
