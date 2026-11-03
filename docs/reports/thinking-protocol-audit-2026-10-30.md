# 思考模式协议核对与源码接入

日期：2026-10-30。状态：已按官方文档实现协议层，静态审阅，未经编译或运行验证；未调用任何模型接口试验。

适用范围：现有翻译工作流、AI 复核和旧 UI 模式共用的模型请求。对应源码为 `src/RimWorld-Auto-AI-Translation_Core/AI/ApiThinkingPolicy.cs`。本记录依赖当前五步工作流设计，不恢复旧 Policy 云分析。

## 模式含义

| 设置 | 普通翻译 | AI 复核 |
|---|---|---|
| 自动 `Auto` | 能关闭时关闭；必思考模型使用已确认的最低档 | 能控制时请求开启，选择已确认的较低思考档或服务的动态思考 |
| 优先关闭 `PreferOff` | 能关闭时关闭；必思考模型使用已确认的最低档 | 同样优先关闭，不因复核用途改为开启 |
| 开启 `Enabled` | 能控制时请求开启 | 能控制时请求开启 |

复核需要结合上下文判断候选是否为显示文字，允许使用模型思考；普通翻译默认优先降低额外耗时和成本。这是本项目的请求策略，不是供应商宣称的质量保证。动态思考模型仍可能自行决定不进行额外推理。

不知道接口或型号的能力时，不猜字段，保留服务默认值并显示原因。“优先关闭”不能保证必思考模型停止思考；`minimal` 也不等于完全关闭。思考消耗可能计入输出上限，适配层不自动放大用户设置的 Token 数。

## 已核对的供应商协议

| 供应商 | 本次实现 | 已知边界与官方依据 |
|---|---|---|
| Google 原生 GenerateContent | `generationConfig.thinkingConfig`。2.5 使用 `thinkingBudget`，开启设动态值 `-1`，可关型号设 `0`；2.5 Pro 关闭请求降到 `128`。新型号按已确认能力使用 `thinkingLevel` 的 `low` / `minimal`。 | 不能同时发送 level 和 budget；2.5 不接受 level。`minimal` 仍可能思考。保留调用方的输出上限及 JSON 格式。[GenerateContent 思考说明](https://ai.google.dev/gemini-api/docs/generate-content/thinking)；3.5 的补充见[版本说明](https://ai.google.dev/gemini-api/docs/generate-content/whats-new-gemini-3.5)。不把 Interactions API 的字段搬进当前请求。 |
| OpenAI | Chat Completions 顶层 `reasoning_effort`。GPT-5 的关闭请求用 `minimal`；已确认支持关闭的 5.1、5.2、5.4、5.5、5.6、部分 GPT-6 用 `none`；Astra、6.1 Sol、已列入的 o 系列使用 `low`。 | 转换 `max_tokens` 为 `max_completion_tokens`，数值不变。型号按源码内明确列表判断，不按 `gpt-*` 前缀猜能力。[Chat 参数](https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create)、[思考说明](https://developers.openai.com/api/docs/guides/reasoning)、[GPT-5](https://developers.openai.com/api/docs/guides/gpt-5)、[5.1](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.1)、[5.2](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.2)、[5.4](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.4)、[5.6](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.6)、[当前 GPT-6](https://developers.openai.com/api/docs/guides/latest-model)。 |
| DeepSeek | 官方当前型号使用顶层 `thinking.type=enabled/disabled`；开启时使用已确认的 `reasoning_effort=low`。 | 仅应用于代码中已确认型号。旧 `deepseek-chat` / `deepseek-reasoner` 显示官方停用提示，不擅自换模型。[Chat API](https://api-docs.deepseek.com/api/create-chat-completion/)、[思考模式](https://api-docs.deepseek.com/guides/thinking_mode/)、[旧名停用公告](https://api-docs.deepseek.com/news/news260424/)。 |
| Grok | `reasoning_effort`。4.3 使用 `none/low`，4.5、4.6、4.7 使用 `low`。 | 后三者无法关闭；不把“隐藏思考文本”当成关闭，不改为 Responses。[推理能力](https://docs.x.ai/developers/model-capabilities/text/reasoning)、[4.3 型号能力](https://docs.x.ai/developers/models/grok-4.3)。 |
| GLM | 顶层 `thinking.type`；5.3 / 5.3-FLASH 必须开启并使用 `low`；5.2 开启时使用 `high`（其 low 实际映射为 high）。 | 5.3-FLASHX 的文档明确 effort 值，但没有同样明确关闭行为，使用已确认的 low 并在状态中说明。其余已列入型号使用 type 开关。[智谱思考说明](https://docs.bigmodel.cn/cn/guide/capabilities/thinking)、[Z.ai 说明](https://docs.z.ai/guides/capabilities/thinking-mode)。不混用 Coding Plan 的映射去判断普通接口。 |
| 阿里百炼 | 混合思考型号发送顶层 `enable_thinking`；仅思考型号不发送关闭参数；Qwen3.8-Omni-Flash 单独使用 `reasoning_effort=none/low`。 | 百炼托管的 DeepSeek、GLM 按百炼协议处理。已确认仅流式型号明确提示当前非流式接口不适用。[百炼思考说明](https://www.alibabacloud.com/help/en/model-studio/deep-thinking)、[Qwen-Omni 的思考与输出协议](https://www.alibabacloud.com/help/en/model-studio/qwen-omni)。 |
| OpenRouter | 顶层 `reasoning`，读取已获取模型列表中的 `reasoning.mandatory` / `supported_efforts`。能关时发 `enabled=false`；必思考时选最低有据档；开启优先选已列出的 low。 | `supported_parameters` 只能证明有参数，不能单独证明可关闭。只有该列表而没有明确关闭能力时，关闭请求保留默认并说明；开启可发送 `enabled=true`。动态路由保留默认。`exclude=true` 只隐藏返回内容，不能当关闭。[官方 Reasoning Tokens 文档](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens)。 |
| 小米 MiMo | 已核对 2.5、2.5 Pro、2.6 Flash、2.6 Pro、2.6 Pro Ultraspeed 使用顶层 `thinking.type`。 | 保留现有非流式传输；输出字段使用 `max_completion_tokens`，不增加额度。[当前 Chat API](https://mimo.mi.com/docs/en-US/api/chat/openai-api)、[2.5 思考说明](https://platform.xiaomimimo.com/docs/en-US/usage-guide/passing-back-reasoning_content)。 |
| 自定义 OpenAI 兼容 | 仅确认为已知官方主机时使用相应 Chat 协议；其他网关保留原请求。 | 不根据模型名字猜供应商。Google 的 OpenAI 兼容入口不等于 Google 原生 GenerateContent，因此不套原生字段。供应商选项已明确指定协议时，仍按该供应商处理其自定义地址。 |
| Player2 / DeepL | Player2 使用本地客户端的模型与设置；DeepL 不发送大模型思考字段。 | 不把 Player2 的虚拟模型名称送进任一远端厂商适配。DeepL 仍不进入五步大模型翻译流程。 |

## 接口与型号限制

仍未确认的具体能力：Grok 3 Mini 的当前官方参数说明未能取得，保留原请求并显示能力未确认；GLM-5.3-FLASHX 仅设置已证实的 low，不宣称能够关闭；OpenRouter 没有取得 mandatory / supported_efforts 时不把支持 reasoning 参数等同于可以关闭。以上边界不会被“三种模式已接入”掩盖。

新版型号的识别是明确表项，不是按未来版本前缀推测：

| 表项范围 | 具体型号与处理 | 对应官方证据 |
|---|---|---|
| OpenAI 5.4 的小型号 | `gpt-5.4-mini`、`gpt-5.4-nano` 及文档列出的 2026-03-17 快照：`none/low` | [mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini)、[nano](https://developers.openai.com/api/docs/models/gpt-5.4-nano) |
| OpenAI 5.5 / 5.6 | `gpt-5.5` 及正式快照；`gpt-5.6-sol/terra/luna`：`none/low` | [5.5 模型](https://developers.openai.com/api/docs/models/gpt-5.5)、[5.6 指南](https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.6) |
| OpenAI GPT-6 | `gpt-6-astra`、`gpt-6.1-sol` 最低 low；`gpt-6.1-terra`、`gpt-6-sol/luna` 支持 none/low | [当前型号指南](https://developers.openai.com/api/docs/guides/latest-model)与[思考说明](https://developers.openai.com/api/docs/guides/reasoning) |
| Google Gemini 3 / 4 | `gemini-4-argon`、`gemini-3.8-flash`、`gemini-3.7-flash`、`gemini-3.1-pro-preview` 最低 low；3.6/3.5 Flash、3.5/3.1 Flash-Lite、3 Flash Preview 使用 minimal/low | [原生接口等级表](https://ai.google.dev/gemini-api/docs/generate-content/thinking?hl=en)、[Gemini 3 原生接口指南](https://ai.google.dev/gemini-api/docs/generate-content/gemini-3)、[3.5 Flash 说明](https://ai.google.dev/gemini-api/docs/generate-content/whats-new-gemini-3.5) |
| DeepSeek V4 | `deepseek-flash`、`deepseek-v4-pro`，保留已公布的 `deepseek-v4-flash` 旧名参数兼容 | [当前 Chat API](https://api-docs.deepseek.com/api/create-chat-completion/)、[V4 公告](https://api-docs.deepseek.com/news/news260424/)；不保证旧名继续可调用 |
| Grok 4.x | 4.3 可关闭，4.5、4.6、4.7 必须思考；仅匹配已列出的型号及 latest 名 | [4.3 型号页](https://docs.x.ai/developers/models/grok-4.3)、[当前思考说明](https://docs.x.ai/developers/model-capabilities/text/reasoning) |
| GLM 5.x | 5、5-Turbo、5V-Turbo、5.1、5.2 使用 type 开关；5.3 / 5.3-FLASH 必思考；5.3-FLASHX 只设置已证实的 low | [思考说明中的支持型号和参数例外](https://docs.bigmodel.cn/cn/guide/capabilities/thinking) |
| 百炼 Qwen / 第三方 | Qwen3.5 Plus/Flash、3.6 Plus/Flash、3.7 Max/Plus/Flash、3.8 Max/Flash/27B 及源码逐项列出的快照；Max 的早期预览和 3.8-2.4T 单列必思考；DeepSeek、GLM、Kimi 使用文档列明的托管型号 | [百炼支持型号表](https://www.alibabacloud.com/help/en/model-studio/deep-thinking)、[中文托管型号表](https://help.aliyun.com/zh/model-studio/deep-thinking/)；Omni 与 MiniMax 的单独字段见下文 |
| MiMo 2.6 | `mimo-v2.6-flash`、`mimo-v2.6-pro`、`mimo-v2.6-pro-ultraspeed` | [Chat API 的 model、thinking、max_completion_tokens 参数](https://mimo.mi.com/docs/en-US/api/chat/openai-api) |

旧型号也按其实际协议处理：GLM-4.5-Air / X / AirX / Flash、GLM-4.7-Flash 使用 type 开关，依据[4.5 家族说明](https://docs.bigmodel.cn/cn/guide/models/text/glm-4.5)、[4.5-Flash](https://docs.bigmodel.cn/cn/guide/models/free/glm-4.5-flash)、[4.7-Flash 调用示例](https://docs.bigmodel.cn/cn/guide/models/free/glm-4.7-flash)与上表通用思考参数说明。4.5-Flash 的官方自动路由由服务执行，客户端不改用户选中的模型名。

百炼旧 Qwen3-235B-A22B、32B、30B-A3B、14B、8B 的混合模式仍使用 `enable_thinking`；固定 Instruct / Thinking 型号不伪造开关。235B-A22B 与 32B 开启思考时需要流式，当前请求准备阶段给出原因，关闭时保留非流式调用；其他旧开源型号仍提示其服务可能要求流式。依据[官方深度思考说明（含旧版索引）](https://help.aliyun.com/zh/model-studio/deep-thinking/)及[专属部署的非思考调用示例](https://help.aliyun.com/en/model-studio/model-deployment-quick-start)。不套用 PAI 自部署的 `chat_template_kwargs` 到百炼托管 API。

百炼 MiniMax/MiniMax-M3 使用 `thinking.type=adaptive/disabled`，MiniMax/MiniMax-M3.1-Flash 必须思考并使用 `reasoning_effort=low`；这是百炼文档明确列出的两个不同协议，不能一律发送 `enable_thinking`。依据[百炼中文思考说明](https://help.aliyun.com/zh/model-studio/deep-thinking/)及[Chat 参数](https://www.alibabacloud.com/help/en/model-studio/qwen-api-via-openai-chat-completions)。

当前请求发送非流式 Chat Completions（Google 为原生 GenerateContent），本次没有改造为 Responses 或流式解析。

- 已明确识别 `o1-pro`、`o3-pro`、`gpt-5.2-pro`、`gpt-5.4-pro`、`gpt-5.5-pro` 及代码中列出的正式快照：请求准备阶段说明只支持 Responses，不把错误请求发给模型。[o1-pro](https://developers.openai.com/api/docs/models/o1-pro)、[o3-pro](https://developers.openai.com/api/docs/models/o3-pro)、[5.2-pro](https://developers.openai.com/api/docs/models/gpt-5.2-pro)、[5.4-pro](https://developers.openai.com/api/docs/models/gpt-5.4-pro)、[5.5-pro](https://developers.openai.com/api/docs/models/gpt-5.5-pro)。
- 百炼 `qwen3.5-122b-a10b`、`qwen3.5-27b`、`qwen3.5-omni-plus`、`qwen3-omni-flash` 的官方说明要求流式，因此当前路径给出不适用原因，依据见上表百炼链接。
- 这不是完整可用模型目录。新型号、未确认的别名和快照、第三方托管变体仍可能具有不同参数要求；未列入者保留默认并明确能力尚未确认。模型是否可在账号与地区调用仍由供应商决定。

当前仍有以下实质边界：Grok 3 Mini 的官网型号页已重定向到总目录，当前思考说明不再列其参数；[xAI 官方 SDK 的型号声明](https://github.com/xai-org/xai-sdk-python/blob/60188edfd4fadbd5312c3974dfa4343421af14ca/src/xai_sdk/types/model.py)虽然仍保留旧名，但不能据此推断该旧名当前接受哪些档位，因此保留原请求并提示未确认。GLM-5.3-FLASHX 暂不宣称关闭成功。未取得 OpenRouter 明确能力元数据时，也不能保证关闭成功。这些边界不能写成“全部型号均已适配”。

## 接线与数据边界

- `Prepare(payload, config, endpoint, review)` 克隆请求后附加参数，避免改写调用方对象；已识别协议清理互斥的旧思考字段。Google 只替换 `thinkingConfig`。
- `review` 由实际 `invocation.RequestPurpose == "ai_review"` 决定，不由 API 配置的快慢档推断。
- `GetStatus` 用同一解析结果说明实际选择；`GetUnsupportedReason` 提供已知接口不适用原因。
- `RememberModelCapabilities` 只解析已经获取的模型列表响应，不发网络请求，不保存凭据。缓存跟随配置对象，地址改变后不再使用旧地址能力；模型列表重置时应调用 `ForgetModelCapabilities`。
- 主项目及 1.5 项目使用源码通配包含，新文件不需要手工加入 Compile 项；这仅是项目文件的静态判断。

## 静态审阅结论

已对照请求字段、模型例外、配置枚举、调用目的和模型列表回调阅读源码。没有执行编译、测试、游戏、模型 API 试调用、推送；没有据此宣称接口或业务运行正常。后续型号能力变化需要更新明示列表或供应商返回的能力信息，不能将本次核对解释为所有未来型号自动兼容。
