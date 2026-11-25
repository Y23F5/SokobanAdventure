/**
 * @file PuzzleTravel.cs
 * @brief Discover 记录首次访问并发放道具。Route 按门身份寻找正反方向连接，Allowed 读取各房当前解谜状态。
 * CrossDoor 在全部检查通过后提交跨房；BeginRecovery、AdvanceRecovery 和 SafeLanding 管理恢复时钟、受限格和安全移位。
 */
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleRun
    {
        static readonly Vector2Int[] SearchOrder = {Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left};
        public const float RecoveryDuration = 2.4f;
        public float RecoveryRemaining { get; private set; }
        public bool Recovering => RecoveryRemaining > 0;
        public bool Visited(string id) => inventory.Contains(id);
        public IReadOnlyDictionary<string,Vector2Int> BoxesIn(string id) => states[id].Boxes;
        public void AcceptItem() => PendingItem = null;
        void Discover()
        {
            if (Visited(ActiveId)) return;
            inventory.Add(ActiveId);
            SelectedItem = PendingItem = ActiveId;
        }
        public void CycleItem(int delta)
        {
            int index = inventory.IndexOf(SelectedItem);
            SelectedItem = inventory[(index + System.Math.Sign(delta) + inventory.Count) % inventory.Count];
        }
        JToken Route(JToken door, out bool reverse)
        {
            reverse = false;
            if (World == null) return null;
            bool Matches(JToken end) => (string)end["levelId"] == ActiveId && (string)end["doorId"] == (string)door["id"];
            foreach (var link in World["connections"])
            {
                if (Matches(link["forwardFrom"])) return link;
                if (Matches(link["forwardTo"])) { reverse = true; return link; }
            }
            return null;
        }
        bool Allowed(JToken route, bool reverse)
        {
            if (reverse) return (bool)route["allowReturn"];
            var dest = (string)route["forwardTo"]["levelId"];
            var room = World["rooms"].First(r => (string)r["levelId"] == dest);
            return Solved && room["forwardRequiresSolved"].All(id => states[(string)id].Solved);
        }
        public bool CanApproach(JToken door)
        {
            var route = Route(door, out bool reverse);
            return route == null ? (string)door["role"] == "entry" || Solved : Allowed(route, reverse);
        }
        bool CrossDoor(Vector2Int delta)
        {
            if (Recovering) return false;
            var door = Room["doors"].FirstOrDefault(d => RoomFile.At(d) + Origin == Player && -RoomFile.Inward(d) == delta);
            if (door == null) return false;
            var route = Route(door, out bool reverse);
            if (route == null || !Allowed(route, reverse)) return false;
            var end = route[reverse ? "forwardFrom" : "forwardTo"];
            string id = (string)end["levelId"];
            var dest = states[id];
            var target = dest.Design["doors"].First(d => (string)d["id"] == (string)end["doorId"]);
            var landing = dest.Origin + RoomFile.At(target);
            if (dest.Boxes.ContainsValue(landing)) return false;
            ActiveId = id;
            Player = landing;
            Discover();
            return true;
        }
        public Vector2Int RecoveryCell(string boxId) => Current.Homes[boxId];
        public string ObjectState(string roomId, string id) => states[roomId].StateOf(id);
        internal JObject ObjectStateRecords(string roomId) => states[roomId].ObjectStates;
        internal JToken StateRestoreEdge(string id) => Current.RestoreEdge(id);
        public bool PlayerOnRecoveryCell => Current.Homes.ContainsValue(Player);
        bool IsReserved(Vector2Int p) => Recovering && Current.Homes.ContainsValue(p);
        public bool BeginRecovery()
        {
            if (Recovering || Motion != null || SelectedItem != ActiveId) return false;
            RecoveryRemaining = RecoveryDuration;
            return true;
        }
        Vector2Int SafeLanding()
        {
            var queue = new Queue<Vector2Int>();
            var seen = new HashSet<Vector2Int> {Player};
            queue.Enqueue(Player);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in SearchOrder)
                {
                    var next = p + d;
                    if (!seen.Add(next) || !Current.Walkable(next) || Current.Homes.ContainsValue(next)) continue;
                    if (!Boxes.ContainsValue(next)) return next;
                    queue.Enqueue(next);
                }
            }
            var door = Room["doors"].First(d => (string)d["id"] == (string)Room["primaryEntranceId"]);
            return Origin + RoomFile.At(door) + RoomFile.Inward(door);
        }
        public bool AdvanceRecovery(float seconds)
        {
            if (!Recovering) return false;
            RecoveryRemaining = Mathf.Max(0, RecoveryRemaining - Mathf.Max(0, seconds));
            if (Recovering) return false;
            if (Current.Homes.ContainsValue(Player)) Player = SafeLanding();
            bool previouslySolved = Solved;
            foreach (var home in Current.Homes) Boxes[home.Key] = home.Value;
            Current.RestoreObjectStates();
            Current.RecordSolved(previouslySolved);
            return true;
        }
    }
}
