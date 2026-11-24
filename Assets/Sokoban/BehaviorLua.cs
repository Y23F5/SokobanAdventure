/**
 * @file BehaviorLua.cs
 * @brief Evaluate 在无标准库的 MoonSharp 虚拟机内运行条件或脚本。self 仅公开稳定身份及表现动作，脚本超出指令预算时报错。
 */
using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public static class BehaviorLua
    {
        public static bool Evaluate(string source, string owner, bool condition, List<JToken> effects)
        {
            if (string.IsNullOrWhiteSpace(source)) return true;
            if (source.Length > 8192) throw new InvalidOperationException("Lua 节点超过 8192 字符");
            var script = new Script(CoreModules.None);
            var self = new Table(script);
            self.Set("id", DynValue.NewString(owner));
            self.Set("setVisible", DynValue.NewCallback((ctx, args) =>
            {
                if (condition || args.Count != 1 || args[0].Type != DataType.Boolean) throw new ScriptRuntimeException("self.setVisible 需要一个布尔参数且只能用于动作");
                effects.Add(new JObject { ["kind"]="setVisible", ["visible"]=args[0].Boolean });
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
