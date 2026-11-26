/**
 * @file PuzzleBehavior.cs
 * @brief 运行时执行行为图的显示动作：房间解开时按 roomSolved 触发器隐藏目标，未解开时恢复显示。
 * ApplyBehavior()：逐房读取全部行为图，经 ObjectBehavior.Effects 取 roomSolved 触发的 setVisible 动作，已解开的房间按动作的 visible 参数设置目标渲染器，未解开则一律显示；目标缺省为图的所有者，targetRoom 缺省为图所在房间，Lua 抛出的异常只记日志不中断游戏。
 * Interact()：从前格实体图取 onInteract 动作，统一处理 Lua 与可视节点生成的动作描述。
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
        void ApplyBehavior()
        {
            if (Run == null) return;
            foreach (var pair in Run.Models)
            {
                if (!Run.Rooms.ContainsKey(pair.Key) || !groups.ContainsKey(pair.Key)) continue;
                bool solved = Run.SolvedIn(pair.Key);
                foreach (var graph in ObjectBehavior.AllGraphs(pair.Value))
                {
                    try
                    {
                        foreach (var node in ObjectBehavior.Effects(graph, TriggerIds.RoomSolved).Where(n => n.Kind == ObjectBehavior.SetVisible))
                        {
                            string room = ObjectBehavior.Parameter(node, "targetRoom") ?? pair.Key;
                            string id = ObjectBehavior.Parameter(node, "targetObject") ?? graph.OwnerId;
                            bool visible = !solved || ObjectBehavior.Parameter(node, "visible") != "false";
                            var part = FindPart(room, id);
                            if (!part) continue;
                            foreach (var renderer in part.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
                        }
                    }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }
        }

        public bool Interact()
        {
            if (Run == null || leaving || panels.Modal || Busy || Run.Recovering) return false;
            string roomId = Run.ActiveId;
            var effects = new System.Collections.Generic.List<(string owner, BehaviorNode node)>();
            var graphs = Run.Interact(out bool transitioned);
            foreach (var graph in graphs)
            {
                try { effects.AddRange(ObjectBehavior.Effects(graph, TriggerIds.OnInteract).Select(node => (graph.OwnerId, node)).ToArray()); }
                catch (Exception error) { Debug.LogException(error); }
            }
            string message = Run.HintAtPlayer(true);
            if (!transitioned && effects.Count == 0 && string.IsNullOrWhiteSpace(message)) return false;
            foreach (var effect in effects)
            {
                var node = effect.node;
                var targetRoom = ObjectBehavior.Parameter(node, "targetRoom") ?? roomId;
                var target = ObjectBehavior.Parameter(node, "targetObject") ?? effect.owner;
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
                        Run.SetTerrainState(targetRoom, ObjectBehavior.Parameter(node, "targetTerrain") ?? effect.owner,
                            ObjectBehavior.Parameter(node, "state") == "on");
                        break;
                    case BehaviorNodeKinds.PlaySound:
                        PlaySound(ObjectBehavior.Parameter(node, "sound"));
                        break;
                    case BehaviorNodeKinds.ShowText:
                        message = ObjectBehavior.Parameter(node, "text");
                        break;
                    case BehaviorNodeKinds.StartRecovery:
                        Run.BeginScriptedRecovery(ObjectBehavior.Parameter(node, "recovery"));
                        break;
                }
            }
            foreach (var group in groups)
            {
                SyncObjectStates(group.Key, group.Value);
                if (group.Key != roomId || !Run.Recovering) SyncTerrainGates(group.Key, group.Value);
            }
            foreach (var effect in effects.Where(effect => effect.node.Kind == BehaviorNodeKinds.SetVisible))
            {
                var node = effect.node;
                var targetRoom = ObjectBehavior.Parameter(node, "targetRoom") ?? roomId;
                var target = ObjectBehavior.Parameter(node, "targetObject") ?? effect.owner;
                var part = FindPart(targetRoom, target);
                if (!part) continue;
                bool visible = ObjectBehavior.Parameter(node, "visible") != "false";
                foreach (var renderer in part.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
            }
            actor.position = Position(Run.Player);
            RefreshFeedback();
            ShowInventory();
            if (Run.PendingItem == null && !string.IsNullOrWhiteSpace(message)) panels.ShowInformation(Run.Room.Meta.Name, message);
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
