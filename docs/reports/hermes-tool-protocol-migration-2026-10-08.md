# Hermes 协议分层移植：阶段记录

日期：2026-10-08。状态：默认翻译工具及恢复链已接入源码；设置界面的独立协议配置尚未完成。仅静态审阅，未编译、未运行、未调用真实模型。

## 当前结论（取代下方早期阶段状态）

- 默认 AI 翻译已启用 `submit_translation_results`，`useTranslationTools=false` 开关已删除。旧正文翻译 DTO、反序列化和紧凑数组解析入口已删；工具参数直接交给现有译文校验和文件/数据库保存。复核/连接测试仍使用 JSON，不属于旧翻译降级。
- 首轮有效项立即保存。非法 JSON、缺项、重复编号与译文校验错误均可获得一次纠错；同一原批次拒绝项合并一轮，不再 N 次单条完整历史请求。二次任何参数/译文错误直接失败，不再补试。
- 同路由保留工具调用 ID、Gemini 原生签名与原编号；只写本轮拒绝项。成功后直接结束，无模型确认请求。
- 按 Hermes `agent/turn_recovery.py` 实现针对性兼容恢复一次→错误分类→已配置备用模型→终态摘要，没有正文 JSON 降级。项目原本没有独立备用链，因此只在既有档位路由选出的合格配置池内切换，不跨档位扩张。原并发限流恢复保留；取消/预算/本地调度/输出上限不切换。
- 换厂商时从原任务、最近工具回执和本轮纠错要求重建工具会话，不重放别家签名。实际请求始终经过原预算/用量结算；备用成功时合并此前“有响应无工具”的已知/估算用量；网络失败保留原不确定用量结算。
- `WorkflowIdentity.AiTranslationPromptVersion` 更新为 `ai-translation-tool-3.0`；未修改 XML 版本。
- 已静态检查参数类型、重复/缺项、纠错预算、配对先于保存、批次及纠错保存前取消、写入异常和终态计数。没有编译或实际验证。
- 设置 UI 仍是 Google→Gemini、其他→Chat Completions 映射。尚未完成协议独立配置、模型发现及鉴权统一接线；不宣称支持 Hermes 所有厂商或 Anthropic/Responses/Bedrock。

恢复参考：https://github.com/NousResearch/hermes-agent/blob/main/agent/turn_recovery.py 。llama.cpp pattern/format 恢复只有明确错误且确实移除关键词才重试；本项目当前工具 schema 无这两项，不会空转恢复。未移植 OAuth、图片、上下文压缩等无关策略。

## 以下为早期阶段记录（已被上述状态覆盖）

## 当前已实施

- `WorkflowToolProtocol.cs`：厂商到协议的选择，与 Chat Completions / Gemini 原生协议处理器分开。处理器只构造请求、提取结果和回放工具结果，不接管 Unity HTTP、并发、取消或请求记账。
- `AutoTranslatorAPI.Workflow.cs`：已有正文 JSON 入口已委托协议处理器。复核、连接测试、现有翻译仍可使用原 JSON 方式；工具会话作为显式参数接入，未从业务入口启用。
- 统一响应分别保存正文、思考、工具调用、结束原因、用量；工具参数不进入正文 JSON。
- 每个工具会话保存同一个配置对象及原生 assistant 消息。Gemini 的签名和其他协议元数据随原始消息回放；工具结果以协议要求的调用 ID / 名称关联。
- `ModelInvocation` 和 `AutoTranslatorApiModelTransport` 增加工具会话传递。工具响应结束原因只在显式工具会话且确实返回工具调用时被接受，正文 JSON 请求不放宽结束原因。
- `TranslationSubmissionTool.cs`：工具定义、严格参数读取、旧业务输入格式转换及结果回放。保存动作通过委托接收，磁盘/数据库异常不伪装成模型参数错误。

## 尚未完成 / 待主人确认

现已增加 `AiTranslationService.Tools.cs` 和重试上下文接线：显式工具模式通过工具参数→现有校验/保存→错误回执处理，首次成功项即时保存，拒绝项沿原逐条一次补试队列继续。`AiTranslationService` 构造参数 `useTranslationTools` 默认仍为 false，后台构造入口尚未切换，不代表用户已经能使用工具翻译。

主人新增确认：非法参数 JSON、缺项、重复编号也纳入一次纠错。结构错误与占位符校验错误共用每条最多一次机会；补试结果无论哪种错误都进入最终失败，不再重新排队。缺项/重复项以外的有效项继续保存；无法解析整体参数时本次无条目保存。每个拒绝项的补试从原始会话分支，保留相同 API 配置和原始条目编号，只接收该编号；已保存及其他编号不写入。整体参数损坏时沿原逐条规则可产生 N 个单条补试，已向主会话说明其成本。

1. 是否保留成功条目立即保存、失败条目逐条补试一次；同一原批次对话中的编号映射与仅失败项提交范围需要明确。
2. 非法 JSON、缺项、重复编号进入一次纠错已确认；整体参数损坏沿逐条补试的成本已提示，未自行改成整批补试。
3. 不支持工具调用的模型/兼容接口是否直接报告不支持，或允许显式选择旧正文模式；不得静默降级。
4. 无工具调用/HTTP不支持仍走请求失败，不自行合成工具调用或从正文提取参数；此类配置的最终产品策略待确认。

## 静态审阅边界

已人工沿入口→协议→既有 HTTP→归一化响应核对；现有 JSON 空正文补试次数、DeepSeek 参数、输出上限和取消路径保留。工具模式暂仅发送一次，不自行引入业务纠错次数。思考字段只保留在独立字段/必要协议上下文，不参与正文拼接或翻译 JSON 解析。

工具历史在保存前核对 Chat Completions 调用 ID，记录结果前核对数量，避免保存成功后才发现无法配对。响应调用列表与待回放列表使用独立集合，回放清理不会清空已返回响应。严格参数转换先检查类型和重复编号，再交给业务校验；取消在每批提交及补试保存前检查，保存完成到短事务边界后更新统计。工具会话不轮询新配置，配置被修改则中止该会话，避免跨模型回放签名。缺失用量时估算包含工具定义和会话，而非只估算末尾提示词。默认入口尚未切换，不能据此宣称已运行或验收。

本轮备份：`E:\Documents\Codex交付\LLM工具改造\2026-10-08-before`。未覆盖 DLL、未重置或提交其他成果。

## 参考来源与归属

职责划分和统一响应设计参考 NousResearch/hermes-agent：

- https://github.com/NousResearch/hermes-agent/blob/main/agent/transports/base.py
- https://github.com/NousResearch/hermes-agent/blob/main/agent/transports/types.py
- https://github.com/NousResearch/hermes-agent/blob/main/agent/transports/chat_completions.py
- https://github.com/NousResearch/hermes-agent/blob/main/agent/transports/__init__.py

参考本机已下载源码（2026-10-08）；这不是完整 Hermes 功能移植。Gemini 原生请求字段另参考 Google 官方 Function Calling 文档 https://ai.google.dev/gemini-api/docs/function-calling ，保留本项目既有 generateContent 接口，不迁移至 Interactions API。

Hermes 许可已从 https://github.com/NousResearch/hermes-agent/blob/main/LICENSE 核对为 MIT；对应归属与许可见 `docs/licenses/hermes-agent-MIT.txt`。当前 C# 实现按职责重新编写，未引入 Python 运行时。
