/**
 * @file PuzzleBehavior.cs
 * @brief 把 F 交互与运行事件接到同一个行为图动作执行入口。
 * PrimeSolvedVisibility()：生成已载入房间的解题可见性规则，载入时只恢复显示，不重发解题动作。
 * Display(roomId, id, visible)：更新目标的组件可见性。
 * ApplyBehavior()：重算已解开房间的显示动作，并应用其他触发器已写入的可见状态。
 * EnqueueBehavior(roomId, trigger, owner)：按事件来源房间和可选所有者收集要分派的图。
 * DispatchBehavior(message)：按先后执行可视节点与 Lua 产生的动作，直到本次操作产生的事件处理完，再同步外观、地形与可见性。
 * ShowBehaviorMessage(message)：无道具领取面板时显示动作或交互产生的文本。
 * Interact()：取前格对象的图，经统一分派执行 onInteract，并刷新状态。
 * FindPart(roomId, id)：先按对象 ID 取房间下的同名子物体，取不到时按地形实体 ID 找到所在格，再取名称以该格结尾的地形物体。
 */
using System;
using System.Linq;
using System.Collections.Generic;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleHost
    {
        readonly Queue<(string roomId, string trigger, IEnumerable<BehaviorGraph> graphs)> behaviorEvents =
            new Queue<(string, string, IEnumerable<BehaviorGraph>)>();
        readonly Dictionary<string, List<(string roomId, string id, bool visible)>> solvedVisibility =
            new Dictionary<string, List<(string, string, bool)>>();

        void PrimeSolvedVisibility()
        {
            solvedVisibility.Clear();
            foreach (var pair in Run.Models)
            {
                if (!Run.Rooms.ContainsKey(pair.Key) || !groups.ContainsKey(pair.Key)) continue;
                var rules = solvedVisibility[pair.Key] = new List<(string, string, bool)>();
                foreach (var graph in ObjectBehavior.AllGraphs(pair.Value))
                {
                    try
                    {
                        foreach (var node in ObjectBehavior.Effects(graph, TriggerIds.RoomSolved).Where(n => n.Kind == ObjectBehavior.SetVisible))
                            rules.Add((ObjectBehavior.Parameter(node, "targetRoom") ?? pair.Key,
                                ObjectBehavior.Parameter(node, "targetObject") ?? graph.OwnerId,
                                ObjectBehavior.Parameter(node, "visible") != "false"));
                    }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }
        }

        void ApplyBehavior()
        {
            foreach (var source in solvedVisibility.OrderBy(source => Run.SolvedIn(source.Key)))
            {
                bool solved = Run.SolvedIn(source.Key);
                foreach (var rule in source.Value) Display(rule.roomId, rule.id, !solved || rule.visible);
            }
            foreach (var room in groups)
                foreach (var entry in Run.VisibilityIn(room.Key)) Display(room.Key, entry.Key, entry.Value);
        }

        void Display(string roomId, string id, bool visible)
        {
            var part = FindPart(roomId, id);
            if (!part) return;
            foreach (var renderer in part.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        }

        void EnqueueBehavior(string roomId, string trigger, string owner)
        {
            var model = Run.Models[roomId];
            var graphs = owner == null ? ObjectBehavior.AllGraphs(model) : ObjectBehavior.GraphsOf(model, owner);
            behaviorEvents.Enqueue((roomId, trigger, graphs));
        }

        bool DispatchBehavior(ref string message)
        {
            bool hadEffects = false;
            while (behaviorEvents.Count > 0)
            {
                var pending = behaviorEvents.Dequeue();
                var visibility = pending.trigger == TriggerIds.RoomSolved ? solvedVisibility[pending.roomId] : null;
                visibility?.Clear();
                foreach (var graph in pending.graphs)
                {
                    BehaviorNode[] effects;
                    try { effects = ObjectBehavior.Effects(graph, pending.trigger).ToArray(); }
                    catch (Exception error) { Debug.LogException(error); continue; }
                    foreach (var node in effects)
                    {
                        hadEffects = true;
                        var targetRoom = ObjectBehavior.Parameter(node, "targetRoom") ?? pending.roomId;
                        var target = ObjectBehavior.Parameter(node, "targetObject") ?? graph.OwnerId;
                        switch (node.Kind)
                        {
                            case BehaviorNodeKinds.SetState:
                                if (!string.IsNullOrEmpty(target) && groups.ContainsKey(targetRoom) && Run.Models[targetRoom].ObjectStates.TryGetValue(target, out var table))
                                {
                                    string state = ObjectBehavior.Parameter(node, "state");
                                    if (table.States.Contains(state))
                                        Run.SetObjectState(targetRoom, target, state == table.Initial ? null : state);
                                }
                                break;
                            case BehaviorNodeKinds.SetTerrainState:
                                Run.SetTerrainState(targetRoom, ObjectBehavior.Parameter(node, "targetTerrain") ?? graph.OwnerId,
                                    ObjectBehavior.Parameter(node, "state") == "on");
                                break;
                            case BehaviorNodeKinds.PlaySound:
                                PlaySound(ObjectBehavior.Parameter(node, "sound"));
                                break;
                            case BehaviorNodeKinds.ShowText:
                                message = ObjectBehavior.Parameter(node, "text");
                                break;
                            case BehaviorNodeKinds.StartRecovery:
                                if (pending.roomId == Run.ActiveId) Run.BeginRecovery();
                                break;
                            case BehaviorNodeKinds.SetVisible:
                                if (target != null)
                                {
                                    bool visible = ObjectBehavior.Parameter(node, "visible") != "false";
                                    visibility?.Add((targetRoom, target, visible));
                                    Run.SetVisibility(targetRoom, target, visibility == null ? (bool?)visible : null);
                                }
                                break;
                        }
                    }
                }
            }
            foreach (var group in groups)
            {
                if (!hadEffects && group.Key != Run.ActiveId) continue;
                SyncObjectStates(group.Key, group.Value);
                if (group.Key != Run.ActiveId || !Run.Recovering) SyncTerrainGates(group.Key, group.Value);
            }
            ApplyBehavior();
            return hadEffects;
        }

        void ShowBehaviorMessage(string message)
        {
            if (!panels.Modal && !string.IsNullOrWhiteSpace(message)) panels.ShowInformation(Run.Room.Meta.Name, message);
        }

        public bool Interact()
        {
            if (Run == null || leaving || panels.Modal || Busy || Run.Recovering) return false;
            string roomId = Run.ActiveId;
            var graphs = Run.Interact(out bool transitioned);
            behaviorEvents.Enqueue((roomId, TriggerIds.OnInteract, graphs));
            string message = Run.HintAtPlayer(true);
            bool hadEffects = DispatchBehavior(ref message);
            if (!transitioned && !hadEffects && string.IsNullOrWhiteSpace(message)) return false;
            actor.position = Position(Run.Player);
            RefreshFeedback();
            ShowInventory();
            ShowBehaviorMessage(message);
            Persist();
            return true;
        }

        public static IReadOnlyList<string> BuiltinSounds { get; } = Array.AsReadOnly(new[]
            { "interact", "push", "gateOpen", "gateClose", "recoveryStart", "recoveryEnd" });
        AudioSource effectAudio;

        void PlaySound(string name)
        {
            if (!BuiltinSounds.Contains(name)) return;
            var clip = Resources.Load<AudioClip>("PuzzleAudio/" + name);
            if (!clip) return;
            if (!effectAudio)
            {
                effectAudio = gameObject.AddComponent<AudioSource>();
                effectAudio.playOnAwake = false;
                effectAudio.spatialBlend = 0;
            }
            effectAudio.PlayOneShot(clip);
        }

        void SyncTerrainGates(string roomId, Transform group)
        {
            var room = Run.Rooms[roomId].Room;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != TerrainCategories.Openable) continue;
                    var gate = group.Find("Vine " + cell);
                    if (gate) gate.gameObject.SetActive(!Run.TerrainGateOpen(roomId, cell));
                }
        }

        Transform FindPart(string roomId, string id)
        {
            if (id == null || !groups.TryGetValue(roomId, out var group) || !Run.Models.TryGetValue(roomId, out var model)) return null;
            var direct = group.Find(id);
            if (direct) return direct;
            var room = model.Room;
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).EntityId != id) continue;
                    string suffix = " " + cell;
                    foreach (Transform part in group)
                        if (part.name.EndsWith(suffix, StringComparison.Ordinal)) return part;
                }
            return null;
        }
    }
}
