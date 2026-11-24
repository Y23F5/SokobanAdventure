/**
 * @file RoomProgress.cs
 * @brief 构造器按房间原点展开箱子、目标与恢复格。Write 和 Read 按稳定箱子 ID 保存与核验单房进度。
 */
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    internal sealed class RoomProgress
    {
        internal readonly JObject Design;
        internal readonly Vector2Int Origin;
        internal readonly Dictionary<string, Vector2Int> Boxes, Homes;
        internal readonly HashSet<Vector2Int> Targets;
        internal readonly HashSet<Vector2Int> Passages;
        readonly HashSet<Vector2Int> scriptedPassages = new HashSet<Vector2Int>();
        readonly Dictionary<string, Vector2Int> scriptedWalls = new Dictionary<string, Vector2Int>();
        internal bool Walkable(Vector2Int world) => ObjectBehavior.Walkable(Design, world - Origin, EverSolved, Passages) || scriptedPassages.Contains(world - Origin);
        internal bool EverSolved;
        internal bool Solved => Targets.Count > 0 && Targets.All(Boxes.ContainsValue);

        internal RoomProgress(JObject room, Vector2Int origin)
        {
            RoomFile.RequireStructure(room);
            Design = room;
            Origin = origin;
            Boxes = room["boxes"].ToDictionary(b => (string)b["id"], b => origin + RoomFile.At(b));
            Homes = room["boxes"].ToDictionary(b => (string)b["id"], b => origin + RoomFile.Cell(b["recoveryCell"]));
            Targets = new HashSet<Vector2Int>(room["targets"].Select(t => origin + RoomFile.At(t)));
            Passages = ObjectBehavior.SolvedPassages(room);
            foreach (var graph in room["events"].Where(g => !ObjectBehavior.CanSimulate(g) && g["nodes"].Any(n => (string)n["kind"] == "openPassage")))
            {
                var wall = room["terrainObjects"].First(tile => (string)tile["id"] == (string)graph["ownerId"]);
                scriptedWalls[(string)graph["id"]] = RoomFile.At(wall);
            }
            RecordSolved(false);
        }

        internal void RecordSolved(bool previouslySolved)
        {
            if (!Solved) return;
            EverSolved = true;
            if (previouslySolved || scriptedWalls.Count == 0) return;
            foreach (var graph in Design["events"])
            {
                if (!scriptedWalls.TryGetValue((string)graph["id"], out var cell)) continue;
                try { if (ObjectBehavior.Effects(graph).Any(n => (string)n["kind"] == "openPassage")) scriptedPassages.Add(cell); }
                catch (System.Exception error) { Debug.LogWarning($"碰撞行为 {graph["id"]} 执行失败 · {error.Message}"); }
            }
        }

        internal JObject Write()
        {
            var boxes = new JObject();
            foreach (var b in Boxes) boxes.Add(b.Key, RoomFile.Point(b.Value));
            var data = new JObject { ["boxes"] = boxes, ["everSolved"] = EverSolved };
            if (scriptedPassages.Count > 0) data["openPassages"] = new JArray(scriptedPassages.Select(RoomFile.Point));
            return data;
        }

        internal void Read(JObject data)
        {
            var boxes = data?["boxes"] as JObject;
            RoomFile.Check(boxes != null && boxes.Count == Boxes.Count && boxes.Properties().All(b => Boxes.ContainsKey(b.Name)), "箱子记录与房间不符");
            var cells = boxes.Properties().ToDictionary(b => b.Name, b => RoomFile.Cell(b.Value));
            EverSolved = (bool)data["everSolved"];
            scriptedPassages.Clear();
            foreach (var item in data["openPassages"] as JArray ?? new JArray())
            {
                var cell = RoomFile.Cell(item);
                RoomFile.Check(EverSolved && scriptedWalls.ContainsValue(cell), "存档中有未知的开放墙格");
                scriptedPassages.Add(cell);
            }
            RoomFile.Check(cells.Values.Distinct().Count() == cells.Count && cells.Values.All(Walkable), "箱子占格无效");
            foreach (var b in cells) Boxes[b.Key] = b.Value;
            EverSolved = (bool)data["everSolved"] || Solved;
        }
    }
}
