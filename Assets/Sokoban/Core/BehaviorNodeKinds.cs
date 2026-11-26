/**
 * @file BehaviorNodeKinds.cs
 * @brief 行为节点类型的默认注册表：登记触发器、条件、动作和脚本节点，不含已删除的 openPassage，未知节点由校验阶段报错。
 * BehaviorNodeKinds.CreateDefault()：触发器无输入，condition 输出 true 与 false，其余输入输出各一个 in 与 out。
 */
namespace Sokoban.Core
{
    public static class BehaviorNodeKinds
    {
        public const string RoomSolved = "roomSolved";
        public const string Condition = "condition";
        public const string SetVisible = "setVisible";
        public const string SetState = "setState";
        public const string StartRecovery = "startRecovery";
        public const string SetTerrainState = "setTerrainState";
        public const string ShowText = "showText";
        public const string PlaySound = "playSound";
        public const string Lua = "lua";
        public const string InPort = "in";
        public const string OutPort = "out";
        public const string TruePort = "true";
        public const string FalsePort = "false";

        public static Registry<BehaviorNodeKind> CreateDefault()
        {
            var registry = new Registry<BehaviorNodeKind>("behaviorNode");
            foreach (string trigger in new[] { RoomSolved, TriggerIds.FirstPush, TriggerIds.RecoveryStart, TriggerIds.RecoveryEnd, TriggerIds.OnInteract })
                registry.Register(trigger, new BehaviorNodeKind(trigger, new string[0], new[] { OutPort }));
            registry.Register(Condition, new BehaviorNodeKind(Condition, new[] { InPort }, new[] { TruePort, FalsePort }));
            registry.Register(SetVisible, new BehaviorNodeKind(SetVisible, new[] { InPort }, new[] { OutPort }));
            registry.Register(SetState, new BehaviorNodeKind(SetState, new[] { InPort }, new[] { OutPort }));
            registry.Register(Lua, new BehaviorNodeKind(Lua, new[] { InPort }, new[] { OutPort }));
            foreach (string action in new[] { StartRecovery, SetTerrainState, ShowText, PlaySound })
                registry.Register(action, new BehaviorNodeKind(action, new[] { InPort }, new[] { OutPort }));
            return registry;
        }
    }
}
