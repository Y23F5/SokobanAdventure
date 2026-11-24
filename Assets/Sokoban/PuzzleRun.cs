/**
 * @file PuzzleRun.cs
 * @brief 构造器从设计装配活动房间的世界格。FromDraft 复制未保存设计，Campaign 读取正式世界。
 * Step 原子提交一步或一次单箱推动。Snapshot 保存运行位置与完成历史，Restore 校验并恢复进度。
 */
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class PuzzleRun
    {
        public readonly JObject World, Rooms;
        readonly Dictionary<string, RoomProgress> states = new Dictionary<string, RoomProgress>();
        readonly List<string> inventory = new List<string>();
        RoomProgress Current => states[ActiveId];
        public IReadOnlyList<string> Inventory => inventory;
        public string SelectedItem { get; private set; }
        public string PendingItem { get; private set; }
        public string ActiveId { get; private set; }
        public JObject Room => Current.Design;
        public Vector2Int Origin => Current.Origin;
        public Dictionary<string, Vector2Int> Boxes => Current.Boxes;
        public HashSet<Vector2Int> Targets => Current.Targets;
        public Vector2Int Player { get; private set; }
        public bool Solved => Current.Solved;
        public bool EverSolved => Current.EverSolved;
        public int Heading;
        public JObject Motion { get; internal set; }

        PuzzleRun(JObject world, JObject rooms, string active)
        {
            World = world;
            Rooms = rooms;
            if (world == null)
            {
                var room = (JObject)rooms[active];
                RoomFile.RequireStructure(room);
                states.Add(active, new RoomProgress(room, Vector2Int.zero));
            }
            else
            {
                string issue = WorldLayout.Issues(world, rooms).FirstOrDefault();
                RoomFile.Check(issue == null, issue);
                foreach (var placement in world["rooms"])
                {
                    string id = (string)placement["levelId"];
                    states.Add(id, new RoomProgress((JObject)rooms[id], RoomFile.Cell(placement["origin"])));
                }
            }
            RoomFile.Check(states.ContainsKey(active), "请先将选中的房间放入世界");
            ActiveId = active;
            Player = Origin + RoomFile.At(Room["spawn"]);
            Discover();
        }

        public static PuzzleRun FromDraft(RoomDraft draft) => new PuzzleRun((JObject)draft.World?.DeepClone(), (JObject)draft.Catalog.DeepClone(), draft.ActiveId);

        public static PuzzleRun Campaign()
        {
            var rooms = new JObject();
            var world = WorldLayout.Read(Path.Combine(Application.streamingAssetsPath, "World", "main.world.json"), rooms);
            return new PuzzleRun(world, rooms, (string)world["start"]["levelId"]);
        }

        public bool Step(Vector2Int delta, out string pushed)
        {
            pushed = null;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1) return false;
            var next = Player + delta;
            if (!RoomFile.Inside(Room, next - Origin)) return CrossDoor(delta);
            if (!RoomFile.Floor(Room, next - Origin) || IsReserved(next)) return false;
            var door = Room["doors"].FirstOrDefault(d => RoomFile.At(d) == next - Origin);
            if (door != null && (string)door["role"] == "exit" && !CanApproach(door)) return false;
            string box = Boxes.FirstOrDefault(pair => pair.Value == next).Key;
            if (box != null)
            {
                var beyond = next + delta;
                if (Recovering || !RoomFile.Floor(Room, beyond - Origin) || Boxes.ContainsValue(beyond)) return false;
                Boxes[box] = beyond;
                pushed = box;
            }
            Player = next;
            Current.EverSolved |= Solved;
            return true;
        }

    }
}
