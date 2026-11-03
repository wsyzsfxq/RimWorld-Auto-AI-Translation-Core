# 本地 Workshop 显示接口静态普查

范围：`E:/Program Files/steam/steamapps/workshop/content/294100`，递归枚举 DLL，以 Mono.Cecil 读取元数据与 IL，不加载执行目标程序集。包括历史版本和依赖库，未按 Mod 或二进制内容去重，不代表当前启用列表。

完整文件枚举共 553 个 DLL，成功读取 552 个，失败 1 个。初步 rg 清单为 552 个，以完整枚举和逐文件读取结果为准。

筛选对 Verse.Widgets、Verse.Listing_Standard、Verse.TooltipHandler、Verse.Messages、UnityEngine.GUI、UnityEngine.GUILayout 的方法引用，且包含 string、TaggedString、GUIContent 或 TipSignal 参数。此范围包含输入框、控件标识等非翻译用途，不是可注入白名单，也不覆盖任意自定义绘制框架。

290 个 DLL 存在相关引用，共 16,058 处方法引用、96 种完整签名；其中 29 种含多个 string 参数，24 种含 ref 参数。计数包含历史版本重复，不能换算为翻译成功率。

详细签名、次数、样本方法及 DLL 路径见同名 CSV；读取失败路径及异常见同名 errors.txt。当前统计只能支持按完整签名和参数用途建立规则的必要性，不能直接确定所有参数用途。必须逐项核对游戏或 Unity 接口实现；自定义 wrapper 另行分析。尚未修改参数识别规则，未编译、运行测试或进行游戏验收。
