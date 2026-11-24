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
            EverSolved = Solved;
        }

        internal JObject Write()
        {
            var boxes = new JObject();
            foreach (var b in Boxes) boxes.Add(b.Key, RoomFile.Point(b.Value));
            return new JObject { ["boxes"] = boxes, ["everSolved"] = EverSolved };
        }

        internal void Read(JObject data)
        {
            var boxes = data?["boxes"] as JObject;
            RoomFile.Check(boxes != null && boxes.Count == Boxes.Count && boxes.Properties().All(b => Boxes.ContainsKey(b.Name)), "箱子记录与房间不符");
            var cells = boxes.Properties().ToDictionary(b => b.Name, b => RoomFile.Cell(b.Value));
            RoomFile.Check(cells.Values.Distinct().Count() == cells.Count && cells.Values.All(p => RoomFile.Floor(Design, p - Origin)), "箱子占格无效");
            foreach (var b in cells) Boxes[b.Key] = b.Value;
            EverSolved = (bool)data["everSolved"] || Solved;
        }
    }
}
