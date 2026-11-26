/**
 * @file BehaviorLua.cs
 * @brief Evaluate 在无标准库的 MoonSharp 虚拟机内运行条件或脚本，self 只公开所有者 ID 与 setVisible 动作，超出指令预算时报错。
 * Evaluate(source, owner, condition, effects)：条件模式把源码包成 return 表达式并要求布尔结果；动作模式把 self.setVisible 调用
 * 转成 kind 为 setVisible 的 Core.BehaviorNode 追加到 effects；源码为空时视为条件成立；超过 8192 字符或 10000 条指令抛异常。
 */
using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using Sokoban.Core;

namespace Sokoban
{
    public static class BehaviorLua
    {
        public static bool Evaluate(string source, string owner, bool condition, List<BehaviorNode> effects)
        {
            if (string.IsNullOrWhiteSpace(source)) return true;
            if (source.Length > 8192) throw new InvalidOperationException("Lua 节点超过 8192 字符");
            var script = new Script(CoreModules.None);
            var self = new Table(script);
            self.Set("id", DynValue.NewString(owner ?? ""));
            self.Set("setVisible", DynValue.NewCallback((ctx, args) =>
            {
                if (condition || effects == null || args.Count != 1 || args[0].Type != DataType.Boolean)
                    throw new ScriptRuntimeException("self.setVisible 需要一个布尔参数且只能用于动作");
                effects.Add(ObjectBehavior.Node(ObjectBehavior.SetVisible,
                    new Dictionary<string, string> { ["visible"] = args[0].Boolean ? "true" : "false" }));
                return DynValue.Nil;
            }));
            script.Globals.Set("self", DynValue.NewTable(self));
            var chunk = script.LoadString(condition ? "return (" + source + ")" : source);
            var thread = script.CreateCoroutine(chunk).Coroutine;
            thread.AutoYieldCounter = 10000;
            var result = thread.Resume();
            if (thread.State != CoroutineState.Dead) throw new InvalidOperationException("Lua 节点达到执行预算");
            if (!condition) return true;
            if (result.Type != DataType.Boolean) throw new InvalidOperationException("Lua 条件需返回布尔值");
            return result.Boolean;
        }
    }
}
