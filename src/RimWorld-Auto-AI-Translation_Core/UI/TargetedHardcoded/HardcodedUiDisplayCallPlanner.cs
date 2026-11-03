using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace AutoTranslator_Core.TargetedHardcodedUi
{
    // Track exact literal provenance within straight-line IL. No source value is mutated.
    // Branch joins, address-taking and unknown operations discard provenance conservatively.
    internal static class HardcodedUiDisplayCallPlanner
    {
        internal sealed class Site
        {
            public int InstructionIndex;
            public MethodBase Call;
            public Dictionary<int, HardcodedUiTranspileSpec> Arguments = new Dictionary<int, HardcodedUiTranspileSpec>();
        }

        internal static List<Site> Plan(List<CodeInstruction> body, IList<HardcodedUiTranspileSpec> specs,
            IDictionary<string, string> diagnostics = null)
        {
            var byOrdinal = specs.ToDictionary(s => s.LiteralOrdinal);
            var stack = new List<HardcodedUiTranspileSpec>();
            var locals = new Dictionary<int, HardcodedUiTranspileSpec>();
            var sites = new List<Site>();
            int ordinal = -1;
            for (int i = 0; i < body.Count; i++)
            {
                CodeInstruction instruction = body[i];
                OpCode op = instruction.opcode;
                if (instruction.labels.Count > 0 || instruction.blocks.Count > 0)
                {
                    Record(diagnostics, stack.Concat(locals.Values), "ControlFlowBoundary: 分支汇合或异常边界处无法继续追踪原文");
                    stack.Clear(); locals.Clear();
                }
                if (op == OpCodes.Ldstr)
                {
                    ordinal++;
                    byOrdinal.TryGetValue(ordinal, out HardcodedUiTranspileSpec spec);
                    stack.Add(spec != null && string.Equals(spec.Literal, instruction.operand as string, StringComparison.Ordinal) ? spec : null);
                    continue;
                }
                int local = LocalIndex(instruction);
                if (local >= 0 && op.Name.StartsWith("stloc", StringComparison.Ordinal))
                { locals[local] = Pop(stack); continue; }
                if (local >= 0 && op.Name.StartsWith("ldloc", StringComparison.Ordinal) && !op.Name.StartsWith("ldloca", StringComparison.Ordinal))
                { locals.TryGetValue(local, out HardcodedUiTranspileSpec value); stack.Add(value); continue; }
                if (op == OpCodes.Dup)
                { stack.Add(stack.Count > 0 ? stack[stack.Count - 1] : null); continue; }
                if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj)
                {
                    MethodBase call = instruction.operand as MethodBase;
                    if (call == null) { stack.Clear(); locals.Clear(); continue; }
                    ParameterInfo[] parameters = call.GetParameters();
                    var arguments = new HardcodedUiTranspileSpec[parameters.Length];
                    for (int p = parameters.Length - 1; p >= 0; p--) arguments[p] = Pop(stack);
                    bool displayCall = HardcodedUiCallTarget.IsDisplayCall(call);
                    if (displayCall && parameters.Any(p => p.ParameterType.IsByRef || p.ParameterType.IsPointer))
                        Record(diagnostics, arguments, "UnsupportedByRef: 显示调用包含 ref/out 或指针参数，当前未支持");
                    else if (displayCall && i > 0 && body[i - 1].opcode.OpCodeType == OpCodeType.Prefix)
                        Record(diagnostics, arguments, "UnsupportedPrefix: 显示调用带有尚未支持的 IL 前缀");
                    else if (!displayCall)
                        Record(diagnostics, arguments,
                            call.DeclaringType?.FullName == "Verse.TaggedString" || call.DeclaringType?.FullName == "UnityEngine.GUIContent"
                                ? "UnsupportedWrappedType: 原文进入文字包装类型，当前未继续追踪"
                                : "UntrackedCall: 原文进入非直接支持的调用，无法确认后续显示位置");
                    if (!call.IsStatic && op != OpCodes.Newobj) Pop(stack);
                    if (op != OpCodes.Newobj && HardcodedUiCallTarget.IsDisplayCall(call) &&
                        instruction.labels.Count == 0 && instruction.blocks.Count == 0 &&
                        (i == 0 || body[i - 1].opcode.OpCodeType != OpCodeType.Prefix) &&
                        parameters.All(p => !p.ParameterType.IsByRef && !p.ParameterType.IsPointer))
                    {
                        var site = new Site { InstructionIndex = i, Call = call };
                        for (int p = 0; p < parameters.Length; p++)
                            if (parameters[p].ParameterType == typeof(string) && arguments[p] != null)
                                site.Arguments[p] = arguments[p];
                        if (site.Arguments.Count > 0) sites.Add(site);
                    }
                    if (op == OpCodes.Newobj || call is MethodInfo info && info.ReturnType != typeof(void)) stack.Add(null);
                    // A ref argument may overwrite a tracked local.
                    if (parameters.Any(p => p.ParameterType.IsByRef)) locals.Clear();
                    continue;
                }
                if (op == OpCodes.Nop) continue;
                if (op.FlowControl == FlowControl.Branch || op.FlowControl == FlowControl.Cond_Branch ||
                    op.FlowControl == FlowControl.Return || op.FlowControl == FlowControl.Throw ||
                    op.Name.StartsWith("ldloca", StringComparison.Ordinal))
                {
                    Record(diagnostics, stack.Concat(locals.Values), op.Name.StartsWith("ldloca", StringComparison.Ordinal)
                        ? "UnsupportedLocalAddress: 局部变量被取地址，当前未继续追踪"
                        : "ControlFlowBoundary: 控制流结束或跳转，当前未继续追踪");
                    stack.Clear(); locals.Clear(); continue;
                }
                int pops = StackCount(op.StackBehaviourPop);
                int pushes = StackCount(op.StackBehaviourPush);
                if (pops < 0 || pushes < 0) { stack.Clear(); locals.Clear(); continue; }
                for (int p = 0; p < pops; p++)
                {
                    var consumed = Pop(stack);
                    Record(diagnostics, new[] { consumed }, op == OpCodes.Stfld || op == OpCodes.Stsfld
                        ? "UnsupportedFieldFlow: 原文写入对象或静态字段，当前未跨字段追踪"
                        : "UntrackedInstruction: 原文经过尚未追踪的指令 " + op.Name);
                }
                for (int p = 0; p < pushes; p++) stack.Add(null);
            }
            if (diagnostics != null)
            {
                var matched = new HashSet<string>(sites.SelectMany(site => site.Arguments.Values).Select(spec => spec.EntryId));
                foreach (var spec in specs)
                    if (!matched.Contains(spec.EntryId) && !diagnostics.ContainsKey(spec.EntryId))
                        diagnostics[spec.EntryId] = "NoDisplaySink: 未找到可明确对应此原文的受支持显示参数；原因尚不能细分";
            }
            return sites;
        }

        private static void Record(IDictionary<string, string> diagnostics,
            IEnumerable<HardcodedUiTranspileSpec> values, string reason)
        {
            if (diagnostics == null) return;
            foreach (var value in values)
                if (value != null && !diagnostics.ContainsKey(value.EntryId)) diagnostics[value.EntryId] = reason;
        }

        private static HardcodedUiTranspileSpec Pop(List<HardcodedUiTranspileSpec> stack)
        {
            if (stack.Count == 0) return null;
            var value = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return value;
        }

        private static int LocalIndex(CodeInstruction instruction)
        {
            string name = instruction.opcode.Name;
            if (!name.StartsWith("ldloc", StringComparison.Ordinal) && !name.StartsWith("stloc", StringComparison.Ordinal)) return -1;
            if (name.Length == 7 && name[6] >= '0' && name[6] <= '3') return name[6] - '0';
            if (instruction.operand is LocalBuilder builder) return builder.LocalIndex;
            if (instruction.operand is LocalVariableInfo variable) return variable.LocalIndex;
            if (instruction.operand is byte b) return b;
            if (instruction.operand is short s) return s;
            if (instruction.operand is int n) return n;
            return -1;
        }

        private static int StackCount(StackBehaviour behaviour)
        {
            if (behaviour == StackBehaviour.Pop0 || behaviour == StackBehaviour.Push0) return 0;
            if (behaviour == StackBehaviour.Varpop || behaviour == StackBehaviour.Varpush) return -1;
            // Fixed behaviours encode one component per stack value (e.g. Popref_popi_pop1).
            return behaviour.ToString().Split('_').Length;
        }
    }
}
