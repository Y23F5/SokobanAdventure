/**
 * @file ObjectState.cs
 * @brief 触发器注册表与对象状态表：状态转换挂在触发器上，与 setState 节点合并为一套。
 * TriggerInfo(id, description)：触发器类型描述。
 * TriggerIds：roomSolved、firstPush、recoveryStart、recoveryEnd 与 onInteract 触发器 ID，CreateDefault 登记全部触发器。
 * StateTransition(from, trigger, to, remnant)：从某状态经触发器转到目标状态，remnant 是转换时在原格留下的残留物外观，可为空。
 * ObjectStateTable(initial, states, transitions)：对象状态表，Find(from, trigger) 返回匹配的转换，没有则返回空。
 */
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class TriggerInfo
    {
        public string Id { get; }
        public string Description { get; }

        public TriggerInfo(string id, string description)
        {
            Id = id;
            Description = description;
        }
    }

    public static class TriggerIds
    {
        public const string RoomSolved = "roomSolved";
        public const string FirstPush = "firstPush";
        public const string RecoveryStart = "recoveryStart";
        public const string RecoveryEnd = "recoveryEnd";

        public const string OnInteract = "onInteract";

        public static Registry<TriggerInfo> CreateDefault()
        {
            var registry = new Registry<TriggerInfo>("trigger");
            registry.Register(RoomSolved, new TriggerInfo(RoomSolved, "房间由未解开变为已解开"));
            registry.Register(FirstPush, new TriggerInfo(FirstPush, "对象第一次被推动"));
            registry.Register(RecoveryStart, new TriggerInfo(RecoveryStart, "恢复开始"));
            registry.Register(RecoveryEnd, new TriggerInfo(RecoveryEnd, "恢复完成"));
            registry.Register(OnInteract, new TriggerInfo(OnInteract, "按 F 交互"));
            return registry;
        }
    }

    public sealed class StateTransition
    {
        public string From { get; }
        public string Trigger { get; }
        public string To { get; }
        public AppearanceRef Remnant { get; }

        public StateTransition(string from, string trigger, string to, AppearanceRef remnant = null)
        {
            From = from;
            Trigger = trigger;
            To = to;
            Remnant = remnant;
        }
    }

    public sealed class ObjectStateTable
    {
        public string Initial { get; }
        public IReadOnlyList<string> States { get; }
        public IReadOnlyList<StateTransition> Transitions { get; }

        public ObjectStateTable(string initial, IReadOnlyList<string> states, IReadOnlyList<StateTransition> transitions)
        {
            Initial = initial;
            States = states ?? new List<string>();
            Transitions = transitions ?? new List<StateTransition>();
        }

        public StateTransition Find(string from, string trigger) =>
            Transitions.FirstOrDefault(t => t.From == from && t.Trigger == trigger);
    }
}
