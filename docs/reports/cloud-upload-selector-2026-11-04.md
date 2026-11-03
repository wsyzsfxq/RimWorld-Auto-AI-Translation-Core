# 云端上传选择与多行输入框静态修改

- 日期：2026-11-04
- 状态：源码已修改，未经编译、运行或真实上传验证。
- 范围：本地译文和贡献工作区批量上传的 Mod 选择；当前多行输入框首行排版。
- 依据：用户确认先选择 Mod，再上传；只显示有译文的 Mod，提供“全选”和“只选云端暂无译文”。

## 原来的上传行为

两个批量上传按钮都调用 `ExecuteBatchUpload`。准备阶段扫描所有符合条件的 Mod，只显示一个批量总确认；确认后上传全部扫描结果，没有逐 Mod 选择器。`QueueBatchUploadPreparation` 始终返回 `true`，其后的另一份旧批量上传代码实际上不可达。

## 当前修改

1. 两个入口共用 `Window_CloudUploadSelection`，打开时没有预先勾选。只有点击“上传所选译文”后才进入发送流程。
2. 后台按现有上传文件规则读取 XML，统计非空译文；没有非空译文的 Mod 不出现在列表。展示名称、包 ID、译文数量、当前语种云端状态，支持名称和包 ID 搜索。
3. “全选”选中所有可上传 Mod；“只选云端暂无译文”替换当前选择，仅选中云端没有当前目标语言记录的 Mod。其它语言是否已有记录不影响判断。
4. 云端状态使用已有完整清单。尚未获取、获取中或获取失败时显示“云端状态未知”，禁用第二个批量选择按钮；仍可逐项手动选择，并可刷新云端状态。未知不当作“没有”。
5. 清单准备和实际上传分别取得全局后台任务租约，列表选择期间释放租约。上传时重新核对文件，复用原来逐 Mod 的来源比例检查、昵称、类别、更新说明、贡献工作区回写及结果日志。
6. 为上传追加独立任务名，接入停止操作；取消会终止当前请求和后续 Mod 的发送，已完成上传保留。没有模型配置仍可选择并上传已有译文。
7. 删除了不可达的备用全量上传分支，避免以后出现绕过选择器的另一条入口。

### 实际支持范围

本次沿用原有云端 XML 打包和服务协议，没有扩展云端对独立 DLL JSON 清单或旧 UI JSON 缓存的支持。选择窗口只显示当前接口实际能上传的内容，不把仅有本地 JSON 的 Mod 列为可上传。

“人工精翻”仍在每个 Mod 实际发送前按来源检查：AI 超过 50% 不允许此标记。统计来源规则和云端字段未改变。

## 多行输入框首行裁切

用户另报旧 UI 译文管理窗口的第一行汉字上沿被裁掉，并确认译文没有多余换行。

静态确认：

- `Window_UITranslationManager.DrawEditor` 先切换 `Text.Font=Tiny` 绘制字段说明，绘制译文 `Widgets.TextArea` 后才恢复 `Small`；输入框因此继承了小号标签字体。
- 本机 1.7 `Assembly-CSharp.dll` 中 `Widgets.TextArea` 只选取 `Text.CurTextAreaStyle`，随后调用 `GUI.TextArea`，不额外留输入内容边距。
- 本机 `UnityStubs.dll` 中 `GUI.TextFieldTextRect` 按样式内边距收缩矩形；`DrawTextAreaContent` 直接用内容矩形设置裁剪，从其上沿开始绘字。该路径没有应用 `GUIStyle.contentOffset`，因此不能仅靠调整这个偏移声称解决。
- 实际绘字后端为 `RimWorld.dll` 中 `RimWorldWaylith.Imgui.ImGuiGuiBackend.DrawText`，按布局行的 Ascent 和字形 Ascent 对齐。未执行绘字方法，不能仅凭 IL 断言某台机器实际字体的字形边界。

新增 `WorkflowTextInput.Area`，统一使用 `Small` 字体、左上对齐、明确内边距和纯文本输入；克隆游戏样式，不改共享样式。旧 UI 管理、工作台译文编辑、额外翻译要求、纠错提交、上传预览与更新说明都接入这一入口。旧 UI 原文显示也恢复为正文大小。

这消除了输入框意外继承 `Tiny` 字体的确定问题，并统一留白；是否还存在 1.7 字体后端自身造成的裁切，需要用户实际查看新包，当前未声称画面已经正常。

## 主要文件

- `UI/Windows/Window_CloudUploadSelection.cs`
- `UI/Cloud/AutoTranslatorMod.CloudActions.cs`
- `UI/Cloud/AutoTranslatorMod.CloudRenderers.cs`
- `Cloud/AutoTranslatorCloudClient.Upload.cs`
- `Workflow/WorkflowDomain.cs`、`Workflow/WorkflowTaskCoordinator.cs`（仅上传任务类型及名称）
- `UI/WorkflowTextInput.cs` 与调用它的六处界面文件。

本次没有实际上传，没有调用大模型，没有操作游戏界面，没有运行测试或构建。
