/**
 * @file PuzzleSave.cs
 * @brief Snapshot 保存全世界各房箱位、探索与恢复进度。Restore 兼容旧单房记录，先在候选会话完成校验再替换状态。
 * ReadSession 检查玩家与房间、道具和演出快照的一致性，ValidateMotion 核对单次移动或转向数据。
 */
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleRun
    {
        public JObject Snapshot()
        {
            var rooms = new JObject();
            foreach (var pair in states) rooms.Add(pair.Key, pair.Value.Write());
            return new JObject { ["version"] = 2, ["worldId"] = (string)World?["worldId"], ["contentVersion"] = (int?)World?["contentVersion"],
                ["levelId"] = ActiveId, ["player"] = RoomFile.Point(Player), ["rooms"] = rooms, ["inventory"] = new JArray(inventory),
                ["selectedItem"] = SelectedItem, ["pendingItem"] = PendingItem, ["heading"] = Heading,
                ["motion"] = Motion?.DeepClone(), ["recoveryRemaining"] = RecoveryRemaining };
        }

        public void Restore(JObject data)
        {
            var candidate = new PuzzleRun(World, Rooms, ActiveId);
            candidate.ReadSession(data);
            states.Clear();
            foreach (var pair in candidate.states) states.Add(pair.Key, pair.Value);
            ActiveId = candidate.ActiveId;
            Player = candidate.Player;
            inventory.Clear();
            inventory.AddRange(candidate.inventory);
            SelectedItem = candidate.SelectedItem;
            PendingItem = candidate.PendingItem;
            Heading = candidate.Heading;
            Motion = candidate.Motion;
            RecoveryRemaining = candidate.RecoveryRemaining;
        }

        void ReadSession(JObject data)
        {
            int version = (int)data["version"];
            string active = (string)data["levelId"];
            RoomFile.Check((version == 1 || version == 2) && (string)data["worldId"] == (string)World?["worldId"] && (int?)data["contentVersion"] == (int?)World?["contentVersion"] && states.ContainsKey(active), "存档与当前世界不匹配");
            ActiveId = active;
            inventory.Clear();
            if (version == 1)
            {
                Current.Read(data);
                Discover();
            }
            else
            {
                var rooms = data["rooms"] as JObject;
                RoomFile.Check(rooms != null && rooms.Count == states.Count, "存档房间数量无效");
                foreach (var pair in states) pair.Value.Read(rooms[pair.Key] as JObject);
                inventory.AddRange(data["inventory"].Values<string>());
                SelectedItem = (string)data["selectedItem"];
                PendingItem = (string)data["pendingItem"];
                RoomFile.Check(inventory.Distinct().Count() == inventory.Count && inventory.All(states.ContainsKey) && Visited(active) && Visited(SelectedItem) && (PendingItem == null || PendingItem == active), "存档道具无效");
                RecoveryRemaining = (float)data["recoveryRemaining"];
                RoomFile.Check(RecoveryRemaining >= 0 && RecoveryRemaining <= RecoveryDuration,"恢复时间无效");
            }
            Player = RoomFile.Cell(data["player"]);
            Heading = (int)data["heading"];
            RoomFile.Check(Heading >= 0 && Heading <= 3 && RoomFile.Floor(Room, Player - Origin) && !Boxes.ContainsValue(Player), "玩家位置或视角无效");
            Motion = data["motion"] is JObject motion ? (JObject)motion.DeepClone() : null;
            if (Motion == null) return;
            float progress = (float)Motion["fraction"];
            RoomFile.Check(progress >= 0 && progress < 1, "存档动画进度无效");
            if ((string)Motion["kind"] == "turn") RoomFile.Check(Mathf.Abs((int)Motion["direction"]) == 1, "转向无效");
            else
            {
                var delta = RoomFile.Cell(Motion["delta"]);
                string pushed = (string)Motion["pushed"];
                RoomFile.Check((string)Motion["kind"] == "move" && Mathf.Abs(delta.x) + Mathf.Abs(delta.y) == 1 && RoomFile.Floor(Room, Player - delta - Origin) && (pushed == null || Boxes.TryGetValue(pushed, out var box) && box == Player + delta), "存档移动与箱子位置不符");
            }
        }
    }
}
