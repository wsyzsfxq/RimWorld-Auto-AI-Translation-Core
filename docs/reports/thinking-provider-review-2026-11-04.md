# 思考模式逐厂商复查

日期：2026-11-04。状态：源码已调整，仅静态审阅；未编译、未发送模型请求、未经运行验证。

适用范围：当前大模型配置中的“自动、尽量关闭、启用”，包括 AI 复核、翻译、失败补试、旧 UI 拦截模式的翻译。此记录补充[2026-10-30 协议核对表](thinking-protocol-audit-2026-10-30.md)，不改变原有三种模式的产品含义。

## 直接结论

开关已接到实际请求中，但不能说“所有厂商的所有型号都能随意开关思考”。部分模型必须思考，只能降低强度；未知型号、自定义网关或缺少模型能力资料时，程序仍沿用服务默认设置。

“自动”在普通翻译中尽量关闭思考，在 AI 复核中请求开启；“尽量关闭”对两者都尽量关闭；“启用”对两者都请求开启。这些是发送给服务的要求，不代表服务器一定生成额外推理，也不代表翻译质量已经验证。

| 厂商或入口 | 当前源码会怎样处理 | 仍有的限制 |
|---|---|---|
| Google Gemini | 已识别的 2.5 型号使用思考预算；较新型号使用思考档位 | 2.5 Pro、部分新型号不能关闭；minimal 仍可能思考。未知别名不自动套参数 |
| OpenAI | 已识别的推理模型发送 `reasoning_effort`，保留原输出额度 | Astra、6.1 Sol、部分 o 系列不能关闭；固定 Chat 接口不能调用仅支持 Responses 的 Pro 型号 |
| DeepSeek | 已识别型号发送 `thinking.type`，开启时用 low 档 | 旧名和未识别型号不能当成已确认可用；账号可调用哪些模型仍由服务决定 |
| Grok | 4.3 可请求关闭；4.5/4.6/4.7 用 low 档 | 后三者必须思考；旧 Grok 3 Mini 的现行参数未确认，保留服务默认值 |
| GLM | 已识别型号发送思考开关；5.3/5.3-FLASH 保持开启并降到 low | 5.3/5.3-FLASH 不能关闭；5.3-FLASHX 暂不宣称能关闭 |
| 阿里百炼 | 已识别的混合思考型号发送开关；MiniMax 两个特殊型号分别处理 | 固定思考型号不能关闭；部分旧开源 Qwen 开思考要求流式，当前请求方式不支持 |
| OpenRouter | 按已经取得的模型资料判断是否能关闭、能用哪些档 | 没有明确能力资料时不能保证关闭；动态路由不固定某个模型的能力 |
| 小米 MiMo | 已识别的 2.5/2.6 型号发送 `thinking.type`，输出上限使用 `max_completion_tokens` | 未识别型号保留默认；思考和最终答案共用输出额度 |
| 自定义 OpenAI 兼容地址 | 已知官方主机按其协议处理；其他地址保留原请求 | “兼容 OpenAI”不代表支持同样的思考字段，未知网关不能保证开关有效 |
| Player2 | 沿用 Player2 客户端设置，本界面不编辑思考模式 | 当前没有接入在 Mod 中控制 Player2 思考的协议 |
| DeepL | 不发送思考参数，选择器不可编辑 | 不属于这套大模型 JSON 请求流程 |

## 本次查到并补上的缺漏

1. 百炼目录中带提供商前缀的部分型号没有接上开关。已按官方明确列出的型号补入：`kimi/kimi-k2.5`、`kimi/kimi-k2.6`，硅基流动和万擎托管的四个混合思考 DeepSeek 型号，以及 `antdigital/Ling-3.1-flash`。固定思考的 Kimi、DeepSeek、MiniMax、Stepfun 型号补上明确说明，保持思考，不伪造关闭请求。
2. “自动”模式下，百炼旧 `qwen3-235b-a22b`、`qwen3-32b` 普通翻译可以关闭思考，但复核会开启思考，而这些型号开启时需要流式。原来的配置检查只看普通翻译，所以可能通过检查后在每一批复核前失败。现在配置检查、请求轮换都按本次是否属于复核处理；不适用的配置不会进入该用途的请求池。普通连接检查和普通翻译保持原含义。
3. 后台整项开始前检查，配置不足时一次说明原因。UI 复核按钮也需要使用同样的复核适用性判断，一键翻译同时检查复核与翻译配置；该按钮接线已交给本次设置界面修改同步处理。

改动位置：`AI/ApiThinkingPolicy.cs`、`AI/AutoTranslatorAPI.Workflow.cs`、`Workflow/AI/AutoTranslatorApiModelTransport.cs`；对应按钮由本次设置界面修改同步接线。

## 静态调用链

配置保存 `ApiKeyConfig.ThinkingMode` → 工作流传入实际 `RequestPurpose` → 共用请求入口识别 `ai_review` → 准备厂商请求 → `ApiThinkingPolicy.Prepare` 附加对应字段 → 共用网络发送。失败补试仍走同一入口，属于翻译用途；旧 UI 翻译也走该入口。没有另建一套按游戏版本区分的思考策略。

OpenRouter 能力资料来自用户原有的模型列表读取，不额外发请求。更换地址、重置模型列表后不继续使用原地址的能力记录。各厂商的三种模式状态说明与发出的参数来自同一个判断函数。

## 官方资料

- Google：[思考说明](https://ai.google.dev/gemini-api/docs/generate-content/thinking)、[Google 的 GenerateContent 思考预算表](https://firebase.google.com/docs/ai-logic/thinking)、[3.5 档位说明](https://ai.google.dev/gemini-api/docs/whats-new-gemini-3.5)。Google 文档现有页面同时说明 Interactions 与 GenerateContent，本项目继续使用 GenerateContent，不混用其请求结构。
- OpenAI：[推理模型说明](https://developers.openai.com/api/docs/guides/reasoning)、[Chat Completions 参数](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)。本次通过官方 OpenAI 文档工具查阅。
- DeepSeek：[思考模式](https://api-docs.deepseek.com/guides/thinking_mode/)、[Chat 参数](https://api-docs.deepseek.com/api/create-chat-completion/)。
- Grok：[推理参数](https://docs.x.ai/developers/model-capabilities/text/reasoning)、[4.3 型号](https://docs.x.ai/developers/models/grok-4.3)。
- GLM：[智谱说明](https://docs.bigmodel.cn/cn/guide/capabilities/thinking)、[Z.ai 说明](https://docs.z.ai/guides/capabilities/thinking-mode)。
- 百炼：[中文型号与参数说明](https://help.aliyun.com/zh/model-studio/deep-thinking/)、[国际版非流式说明](https://www.alibabacloud.com/help/en/model-studio/deep-thinking)。本次新增的明确型号取自中文托管目录，不按名称前缀猜测未来型号。
- OpenRouter：[思考开关与逐型号能力资料](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)。
- MiMo：[官方 Chat 文档](https://mimo.mi.com/docs/en-US/api/chat/openai-api)、[同页官方 Markdown](https://mimo.mi.com/static/docs/api/chat/openai-api.md)。网页正文不能直接提取，本次读取官方 Markdown 核对了 `thinking.type` 与输出额度字段。
- Player2：[官方插件的 Chat 接口示例](https://github.com/elefant-ai/player2-ai-npc-defold#chat-completion-api)。当前源码没有发送思考控制字段，不能据此声称 Player2 本身不支持任何相关能力。

本记录的“已接入”指源码会准备并发送相应参数；没有用真实请求确认服务接收或效果，不等同于接口测试通过。
