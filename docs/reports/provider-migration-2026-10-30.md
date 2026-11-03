# API 功能与云端身份迁移记录

日期：2026-10-30。状态：源码已修改，仅作静态审阅，未经编译或运行验证。

续作审阅与缺漏补齐更新至 2026-11-03，详见[记录](./rw17-followup-static-review-2026-11-02.md)。

对照上游 `codex/rimworld-1.7-support` 的 `7af6ef8bac048744bff0fda2a06a7e45572ddc11`，迁移用户本轮确认的功能。没有合并整个分支，没有调用模型测试接口，没有替换 DLL、打包或推送。

## 1. 网络传输的历史结论

旧版确实使用过 HttpClient。`2377e3f` 中的模型列表和翻译请求使用 `client.SendAsync`；`195408a`（2026-06-07）将这两处改为 UnityWebRequest。历史源码 `RimWorld-Auto-AI-Translation_Core/RimWorld-Auto-AI-Translation_Translator.cs` 的注释明确提到 Mono 解压、读取、编码冲突及 `Illegal byte sequence`。同提交的 CloudClient 注释也提到 Windows GBK 干扰。

这些证据说明当时是为处理已遇到的编码问题切换底层；没有日志支撑错误发生频率或独立证明注释中的根因。本轮撤回“1.6、1.7 统一换回 HttpClient”的建议，保留当前 1.6 UnityWebRequest、并发、取消、超时和用量统计。新增供应商只调整请求参数和请求头。

用户随后确认实施：1.7 使用 .NET 10 的 `HttpClient + SocketsHttpHandler`，1.6 继续 UnityWebRequest。两版本共用翻译业务源码，通过独立工程与 `RIMWORLD_1_7` 选择底层，不复制两套翻译系统。对应网络源码现已接入，未经编译或运行验证；这不能证明旧编码故障绝不会复现，也不等于整个 Mod 已完成 1.7 兼容。

### 1.7 网络实施补充

- 模型请求仍先通过原有预算预留、相同请求去重和并发门控，再调用新网络模块。失败重试策略、译文处理与用量结算继续共用，未直接照搬上游绕过这些环节的请求入口。
- 使用长期复用的 HttpClient；JSON 请求独立设置凭据，不写入共享默认请求头。响应按 UTF-8 解码，无效字节明确记为无效响应，避免静默替换字符后写入译文。
- 超时覆盖等待响应头和读取完整正文，排队等待不占网络超时时间。停止请求后等待底层任务退出并释放响应及请求，再释放并发名额、活动计数与预算预留；不以提前返回替代实际取消。
- 取消、响应超时、HTTP 错误、传输错误及发送前失败保留不同类别；阶段使用 `Native...`。HttpClient 没有提供本实现所需的上传进度，不把零值误判成“上传阶段超时”，诊断中标为未知。
- 模型列表 GET 同样使用新模块；取消由抓取代次、抓取状态和配置变化决定，不与停止全部翻译混用。过期响应不进入列表写回或失败重试。
- 1.7 不包含旧 Unity 翻译协程引擎。云 ZIP 下载原先依赖的 `DownloadHandlerFile` 在本机 1.7 兼容库中不存在，因此补了独立流式下载：直接写临时文件，保留重定向、服务端记录标识、重试及外层解压回滚。下载使用单独复用的连接池，不携带模型凭据，也不被停止 AI 翻译误取消。
- 新增 `RimWorld-Auto-AI-Translation_Core.RW17.csproj`，定义 `net10.0` 和 `RIMWORLD_1_7`；共用源码，独立 `bin/*-RW17` 与 `obj/RW17`，引用现有 1.7 Harmony、Cecil netstandard2、Newtonsoft net6 和 SQLite netstandard2。扫描器在该目标下识别 1.7 目录。未修改 1.5/1.6 工程；About 支持版本与 LoadFolders 已补上 1.7（含 Harmony 依赖声明）。

SQLite 启动加载与旧 UI 的 1.7 适配已完成静态实现：SQLite 按编译常量区分 1.6（net471）与 1.7（netstandard2.0）目录，`prepare-sqlite-runtime.ps1` 同时准备两版本配套库；旧 UI 拦截新增 Waylith 的 `GUI.Label(Rect,string,GUIStyle)` 补丁及嵌套防护。以上均为静态适配，未经编译或运行验证。新增工程不等于本机已具备完整 .NET 10 SDK，也不代表可以发布 1.7 包。用户数据库路径及内容均未更改。

## 2. 已接入的功能

| 项目 | 源码中的行为 |
|---|---|
| 云端身份与本地关联 | `CloudClientIdentity` 在游戏用户配置目录保存 `ATC_CloudIdentities.json`，记录 SteamID、实际上传 ID 和可取得的旧 ID。已有记录继续使用；新用户优先 SteamID，无法取得时生成并持久保存随机 ID。旧 `ATC_CloudClientId.txt` 可继承，不覆盖。Steam 暂时不可用时继续使用上次已保存的关联。 |
| 身份调用位置 | 上传预览、后台上传、纠错贡献、“我的上传”统一使用关联记录。Steam/Unity 信息在主线程取得，不增加 CPU 指纹。当前账号可通过旧 ID 关联识别历史上传；本地文件不替代服务端所有权迁移。 |
| 思考模式 | 每组 API 配置保存“自动／尽量关闭／启用”。自动模式对翻译尽量关闭，对 AI 分类复核启用模型支持的思考；强制思考型号显示不能关闭及实际档位。字段、例外和证据见[协议核对表](./thinking-protocol-audit-2026-10-30.md)。 |
| 额外翻译要求 | 设置的 AI 接口章节增加编辑入口，保存术语、措辞和风格偏好。普通翻译、失败补试和旧 UI 翻译携带要求；分类复核不携带。一次普通翻译任务使用启动时的要求，不在批次中途改变。保存不自动重翻已有译文，原有复用规则继续生效。 |
| MiMo | 增加独立供应商，模型清单来自实际 `/models`。聊天请求使用 `api-key`、`max_completion_tokens`、非流式响应，自动输出模式为 JSON 对象。显式选择 JSON Schema 时提示改为自动或 JSON 对象。枚举追加在末尾，保留已有配置的编号。 |
| Player2 | 在 Custom_OpenAI 下提供本地客户端协议开关及 Game Client ID。使用 `player2-game-key`，不发送 Bearer 或模型名；模型由客户端选择。默认本机 `http://127.0.0.1:4315/v1`，可填写客户端实际端口。此入口是本地桌面协议，未伪装为 Player2 远程 Web API 授权。 |

身份文件只涉及云端上传关联，与 SQLite 翻译数据无关。同一游戏目录升级时沿用原数据库；不同安装目录的数据保持独立。本轮没有移动、重建或清空数据库。

旧版没有保存本地身份、且云清单尚未取得时，不能凭空知道历史服务器记录属于谁；可取得的旧标识会保留为关联，不因以后读到云清单而悄悄改变已选定的上传 ID。共享旧配置也无法证明历史操作者身份，第一位接续该配置的账号继承旧关联，之后按账号区分。

## 3. 官方资料核对

- [MiMo Chat Completions](https://mimo.mi.com/docs/en-US/api/chat/openai-api)：核对认证、输出上限、JSON 对象、思考开关和响应格式；[模型清单](https://mimo.mi.com/docs/en-US/api/model/list-models)用于模型发现。官方也支持 Bearer 认证，本实现选用其列出的 `api-key` 方式。
- Player2 接入沿用原作者的本地客户端模式。[当前官方 Godot SDK 的本地配置](https://github.com/elefant-ai/player2-ai-npc-godot/blob/main/addons/player2/helpers/config/Player2LocalEndpointConfig.gd#L5-L8)仍保留 4315 端口的本地聊天接口；[请求头实现](https://github.com/elefant-ai/player2-ai-npc-godot/blob/main/addons/player2/helpers/api.gd#L130-L162)只给云端请求加 Bearer；[配置枚举](https://github.com/elefant-ai/player2-ai-npc-godot/blob/main/addons/player2/helpers/config/Player2APIConfig.gd#L5-L19)明确区分本地与云端。游戏标识及模型由客户端处理也与[官方 Defold 示例](https://github.com/elefant-ai/player2-ai-npc-defold)一致。新云 API 使用独立登录授权流程，本轮没有迁入云端登录或周期心跳；[官方心跳说明](https://blog.player2.game/p/building-ai-npcs-with-player2-api)将其描述为活跃及收益归属的建议，未找到聊天强制前置心跳依据。以上为官方源码和文档静态核对，尚未在实际客户端运行验证。
- 各供应商思考参数的官方链接、已确认型号和不适用情形集中在[思考模式核对表](./thinking-protocol-audit-2026-10-30.md)，不能把“各现有协议已接入”解释为所有未来型号自动兼容。

## 4. 显示入口与热重载的解释

Mod 可以调用游戏的按钮、标签和提示框，也可以自绘控件或使用图片文字。1.7 的文本绘制后端能集中接到多种普通控件的绘制，但没有证据证明全部 Mod 显示都经过同一个入口。新的接入方案仍在研究，本轮没有替换 DLL 模式或旧 UI 模式的架构。

现有热重载会重新应用 XML 译文和 DLL 显示补丁。每次显示时查询译文的位置能随新译文更新；已将英文复制进对象的缓存，需要刷新对应对象，或在实际绘制时换显示参数。图片文字、尚未覆盖的显示调用以及不知道重建方式的私有缓存，不能仅靠刷新译文字典解决。2026-11-02 已补启动时提前应用已准备 XML 译文和默认 DLL 模式的 1.7 采集入口；同时修正 SQLite 准备、Harmony 依赖范围及旧 UI 图标提示，见[续作静态审阅](./rw17-followup-static-review-2026-11-02.md)。源码未经编译或运行验证。

## 5. 静态审阅范围

已阅读设置保存、菜单入口、提示词构造、失败补试、统一请求、供应商转换、思考参数、模型列表刷新及云身份调用链。主项目以 `src/**/*.cs` 通配包含新文件。原独立结构化供应商测试的替身枚举同步追加 MiMo，未编写或执行测试。

本次不代表 1.7 已适配完成，也不代表任何模型请求或界面已通过运行验收。UI 采集和大型 DLL 分析开销继续暂缓；未匹配译文展示及其他群反馈仍见[待办表](./deferred-feedback-backlog-2026-10-30.md)。
