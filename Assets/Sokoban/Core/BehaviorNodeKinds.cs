/**
 * @file BehaviorNodeKinds.cs
 * @brief 行为节点类型的默认注册表：登记 roomSolved、condition、setVisible、setState、lua 五种节点，不含已删除的 openPassage，未知节点由校验阶段报错。
 * BehaviorNodeKinds.CreateDefault()：登记五种节点的输入输出端口与求解器能否模拟，roomSolved 无输入，condition 输出 true 与 false，其余输入输出各一个 in 与 out，lua 不可模拟。
 * BehaviorNodeKinds.CanSimulate(node)：节点类型已登记、类型可模拟，且 condition 节点没有 expression 参数时才可模拟，未知类型视为不可模拟。
 * BehaviorNodeKinds.HasUnsimulatable(graphs)：任一图含不可模拟节点即为真，供求解结果映射为无法验证。
 */
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public static class BehaviorNodeKinds
    {
        public const string RoomSolved = "roomSolved";
        public const string Condition = "condition";
        public const string SetVisible = "setVisible";
        public const string SetState = "setState";
        public const string Lua = "lua";
        public const string InPort = "in";
        public const string OutPort = "out";
        public const string TruePort = "true";
        public const string FalsePort = "false";

        public static Registry<BehaviorNodeKind> CreateDefault()
        {
            var registry = new Registry<BehaviorNodeKind>("behaviorNode");
            registry.Register(RoomSolved, new BehaviorNodeKind(RoomSolved, new string[0], new[] { OutPort }, true));
            registry.Register(Condition, new BehaviorNodeKind(Condition, new[] { InPort }, new[] { TruePort, FalsePort }, true));
            registry.Register(SetVisible, new BehaviorNodeKind(SetVisible, new[] { InPort }, new[] { OutPort }, true));
            registry.Register(SetState, new BehaviorNodeKind(SetState, new[] { InPort }, new[] { OutPort }, true));
            registry.Register(Lua, new BehaviorNodeKind(Lua, new[] { InPort }, new[] { OutPort }, false));
            return registry;
        }

        public static bool CanSimulate(BehaviorNode node)
        {
            if (!Registries.BehaviorNodes.TryGet(node.Kind, out var kind) || !kind.Simulatable) return false;
            return !(node.Kind == Condition && node.Parameters.TryGetValue("expression", out var expression) && !string.IsNullOrWhiteSpace(expression));
        }

        public static bool HasUnsimulatable(IEnumerable<BehaviorGraph> graphs) =>
            graphs.Any(graph => graph.Nodes.Any(node => !CanSimulate(node)));
    }
}
