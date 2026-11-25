/**
 * @file LuaHost.cs
 * @brief Lua 宿主：行为入口与恢复入口共用 MoonSharp 解释器，每个入口 = 入口名 + 构造 _ENV 环境表的函数，权限就是环境表里有哪些名字。
 * LuaEntry：一个入口，声明入口名与环境表构造函数。
 * LuaHost.Register(entry)：登记入口，名字重复抛 ArgumentException。
 * LuaHost.Load(entryName, source)：用该入口的环境表加载脚本并按入口名与源码缓存编译结果，源码超过 8192 字符抛 InvalidOperationException。
 * LuaHost.Call(entryName, source, function, buildArgs)：在缓存的脚本里调用全局函数，参数由缓存脚本自己构造以满足 MoonSharp 的归属检查；指令数超过预算或脚本出错统一抛 InvalidOperationException，未知入口抛 UnknownIdException。
 * LuaHost.StandardMath(script)：构造只含纯数学函数的 math 表，不提供时间与随机函数。
 * LuaRecoveryMethod：把脚本里的 pose(t, ctx) 包装成 IRecoveryMethod，ctx 每次调用重新构造为只读表（含 params 数值参数表），返回的零件转为 RecoveryPart。
 */
using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace Sokoban.Core
{
    public sealed class LuaEntry
    {
        public string Name { get; }
        public Func<Script, Table> BuildEnvironment { get; }

        public LuaEntry(string name, Func<Script, Table> buildEnvironment)
        {
            Name = name;
            BuildEnvironment = buildEnvironment;
        }
    }

    public sealed class LuaHost
    {
        public const int MaxSourceLength = 8192;
        public const int InstructionBudget = 200000;

        readonly Registry<LuaEntry> entries = new Registry<LuaEntry>("lua-entry");
        readonly Dictionary<string, (Script script, Table env)> cache = new Dictionary<string, (Script, Table)>();

        public void Register(LuaEntry entry) => entries.Register(entry.Name, entry);

        public int CachedCount => cache.Count;

        public static Table StandardMath(Script script)
        {
            var math = new Table(script);
            math.Set("floor", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Floor(a[0].CastToNumber().Value))));
            math.Set("ceil", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Ceiling(a[0].CastToNumber().Value))));
            math.Set("abs", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Abs(a[0].CastToNumber().Value))));
            math.Set("sin", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Sin(a[0].CastToNumber().Value))));
            math.Set("cos", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Cos(a[0].CastToNumber().Value))));
            math.Set("sqrt", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Sqrt(a[0].CastToNumber().Value))));
            math.Set("min", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Min(a[0].CastToNumber().Value, a[1].CastToNumber().Value))));
            math.Set("max", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Max(a[0].CastToNumber().Value, a[1].CastToNumber().Value))));
            math.Set("atan", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Atan(a[0].CastToNumber().Value))));
            math.Set("atan2", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Atan2(a[0].CastToNumber().Value, a[1].CastToNumber().Value))));
            math.Set("tan", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Tan(a[0].CastToNumber().Value))));
            math.Set("pow", DynValue.NewCallback((c, a) => DynValue.NewNumber(Math.Pow(a[0].CastToNumber().Value, a[1].CastToNumber().Value))));
            math.Set("pi", DynValue.NewNumber(Math.PI));
            return math;
        }

        (Script script, Table env) Load(string entryName, string source)
        {
            var entry = entries.Get(entryName);
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length > MaxSourceLength) throw new InvalidOperationException("Lua 脚本超过 " + MaxSourceLength + " 字符");
            string key = entryName + "\n" + source;
            if (cache.TryGetValue(key, out var hit)) return hit;
            var script = new Script(CoreModules.None);
            var env = entry.BuildEnvironment(script);
            script.LoadString(source, env, entryName);
            script.DoString(source, env);
            cache[key] = (script, env);
            return (script, env);
        }

        public DynValue Call(string entryName, string source, string function, Func<Script, DynValue[]> buildArgs)
        {
            var (script, env) = Load(entryName, source);
            var fn = env.Get(function);
            if (fn.Type != DataType.Function) throw new InvalidOperationException("脚本没有函数 " + function);
            try
            {
                var coroutine = script.CreateCoroutine(fn).Coroutine;
                coroutine.AutoYieldCounter = 1000;
                int budget = InstructionBudget / 1000;
                var result = coroutine.Resume(buildArgs(script));
                while (coroutine.State != CoroutineState.Dead)
                {
                    if (--budget <= 0) throw new InvalidOperationException("Lua 脚本超出指令预算");
                    result = coroutine.Resume();
                }
                return result;
            }
            catch (InterpreterException e)
            {
                throw new InvalidOperationException("Lua 脚本出错：" + e.DecoratedMessage, e);
            }
        }
    }

    public sealed class LuaRecoveryMethod : IRecoveryMethod
    {
        public const string EntryName = "recovery.pose";
        readonly LuaHost host;
        readonly string source;

        public LuaRecoveryMethod(LuaHost host, string source)
        {
            this.host = host;
            this.source = source;
        }

        public static LuaEntry CreateEntry() => new LuaEntry(EntryName, script =>
        {
            var env = new Table(script);
            env.Set("math", DynValue.NewTable(LuaHost.StandardMath(script)));
            return env;
        });

        public IReadOnlyList<RecoveryPart> Pose(float t, RecoveryContext ctx)
        {
            var result = host.Call(EntryName, source, "pose", script =>
            {
                var table = new Table(script);
                table.Set("roomId", DynValue.NewString(ctx.RoomId));
                table.Set("boxId", DynValue.NewString(ctx.BoxId));
                table.Set("duration", DynValue.NewNumber(ctx.Duration));
                table.Set("seed", DynValue.NewNumber(ctx.Seed));
                var route = new Table(script);
                for (int i = 0; i < ctx.Route.Count; i++)
                {
                    var cell = new Table(script);
                    cell.Set("x", DynValue.NewNumber(ctx.Route[i].X));
                    cell.Set("y", DynValue.NewNumber(ctx.Route[i].Y));
                    route.Set(i + 1, DynValue.NewTable(cell));
                }
                table.Set("route", DynValue.NewTable(route));
                var parameters = new Table(script);
                foreach (var pair in ctx.Params) parameters.Set(pair.Key, DynValue.NewNumber(pair.Value));
                table.Set("params", DynValue.NewTable(parameters));
                return new[] { DynValue.NewNumber(t), DynValue.NewTable(table) };
            });
            if (result.Type != DataType.Table) throw new InvalidOperationException("pose 必须返回零件表");
            var parts = new List<RecoveryPart>();
            foreach (var item in result.Table.Values)
            {
                if (item.Type != DataType.Table) throw new InvalidOperationException("零件必须是表");
                var p = item.Table;
                string id = p.Get("id").CastToString();
                if (id == null) throw new InvalidOperationException("零件缺少 id");
                float Num(string k, float d) { var v = p.Get(k); return v.IsNil() ? d : (float)v.CastToNumber().Value; }
                var visible = p.Get("visible");
                var clip = p.Get("clip");
                parts.Add(new RecoveryPart(id, p.Get("resource").CastToString(), Num("x", 0), Num("y", 0), Num("z", 0),
                    Num("yaw", 0), Num("scale", 1), p.Get("color").CastToString(),
                    visible.IsNil() || visible.CastToBool(), clip.IsNil() ? (float?)null : (float)clip.CastToNumber().Value,
                    Num("pitch", 0), Num("roll", 0), Num("sx", 1), Num("sy", 1), Num("sz", 1),
                    p.Get("clipName").CastToString(), p.Get("child").CastToString()));
            }
            return parts;
        }
    }
}
