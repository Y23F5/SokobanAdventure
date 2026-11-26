/**
 * @file PuzzleBehavior.cs
 * @brief 运行时执行行为图的显示动作：房间解开时按 roomSolved 触发器隐藏目标，未解开时恢复显示。
 * ApplyBehavior()：逐房读取全部行为图，经 ObjectBehavior.Effects 取 roomSolved 触发的 setVisible 动作，已解开的房间按动作的 visible 参数设置目标渲染器，未解开则一律显示；目标缺省为图的所有者，targetRoom 缺省为图所在房间，Lua 抛出的异常只记日志不中断游戏。
 * FindPart(roomId, id)：先按对象 ID 取房间下的同名子物体，取不到时按地形实体 ID 找到所在格，再取名称以该格结尾的地形物体。
 */
using System;
using System.Linq;
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
