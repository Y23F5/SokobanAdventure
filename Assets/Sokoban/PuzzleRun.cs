/**
 * @file PuzzleRun.cs
 * @brief PuzzleRun 是一局游戏的规则会话：以 ValidatedRoom 与 WorldDocument 装配各房进度，玩家以活动房间的房内格保存，世界格由房间原点换算。
 * FromDraft(draft)：对草稿目录逐房调用 ValidatedRoom.From，任一房失败抛出带房间 ID 与规则定位的 InvalidOperationException。
 * Campaign()：读取正式世界文件及其房间文件，逐房校验后装配，起点取世界起点房间的出生点。
 * Step(delta, out pushed)：经 PushRules.TryPush 提交一步或一次单箱推动，门框外一步交给 CrossDoor 换房。
 * CanLeaveWorld(direction)：玩家站在终点门上朝外，且终点要求的房间均已解开时为真。
 * Interact()：按朝向查找前格实体，触发其对象状态迁移并返回关联行为图。
 * Facing：按出生点旋转初始化，成功移动后更新。
 * Origin、Player、Boxes、Targets、HomeOf：活动房间的世界格视图，规则状态始终保存在房内坐标。
 * Models、Hashes：各房草稿模型与内容哈希，供表现层读取装饰、供存档比对。
 * PuzzleMotion：进行中的一次移动或转向的演出进度，只进演出段，不进规则状态。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban
{
    public sealed class PuzzleMotion
    {
        public string Kind;
        public float Fraction;
        public Vector2Int Delta;
        public int Direction;
        public string Pushed;
    }

    public sealed partial class PuzzleRun
    {
        public readonly WorldDocument World;
        public readonly IReadOnlyDictionary<string, ValidatedRoom> Rooms;
        public readonly IReadOnlyDictionary<string, RoomDraftModel> Models;
        public readonly IReadOnlyDictionary<string, string> Hashes;
        public readonly string WorldId;
        readonly Dictionary<string, RoomProgress> states = new Dictionary<string, RoomProgress>();
        ExplorationSet exploration = new ExplorationSet();
        ItemBag bag = new ItemBag();
        Cell local;
        RoomProgress Current => states[ActiveId];
        public string ActiveId { get; private set; }
        public ValidatedRoom Room => Current.Validated;
        public Vector2Int Origin => ToVector(Current.Origin);
        public Vector2Int Player => ToVector(Current.Origin + local);
        public Cell PlayerLocal => local;
        public Dictionary<string, Vector2Int> Boxes => BoxesOf(Current);
        public IReadOnlyDictionary<string, Vector2Int> BoxesIn(string roomId) => BoxesOf(states[roomId]);
        public HashSet<Vector2Int> Targets => new HashSet<Vector2Int>(Current.Validated.Room.ObjectsOf(ObjectCategories.Target).Select(t => ToVector(Current.Origin + t.Cell)));
        public bool Solved => Current.Solved;
        public bool EverSolved => Current.EverSolved;
        public bool SolvedIn(string roomId) => states[roomId].Solved;
        public int Heading;
        public int Facing { get; private set; }
        public PuzzleMotion Motion { get; set; }

        internal static Vector2Int ToVector(Cell cell) => new Vector2Int(cell.X, cell.Y);
        static Dictionary<string, Vector2Int> BoxesOf(RoomProgress progress) => progress.Boxes.ToDictionary(b => b.Key, b => ToVector(progress.Origin + b.Value));
        static void Check(bool ok, string message) { if (!ok) throw new FormatException(message); }
        static ValidatedRoom Pick(IReadOnlyDictionary<string, ValidatedRoom> rooms, string id) =>
            rooms.TryGetValue(id, out var room) ? room : throw new InvalidOperationException("缺少房间 " + id);

        PuzzleRun(WorldDocument world, IReadOnlyDictionary<string, ValidatedRoom> rooms, IReadOnlyDictionary<string, RoomDraftModel> models,
            IReadOnlyDictionary<string, string> hashes, string active)
        {
            World = world;
            Rooms = rooms;
            Models = models;
            Hashes = hashes ?? models.ToDictionary(m => m.Key, m => ContentHash.Of(m.Value));
            WorldId = world?.WorldId ?? "standalone:" + active;
            if (world == null) states.Add(active, new RoomProgress(Pick(rooms, active), new Cell(0, 0)));
            else foreach (var entry in world.Rooms) states.Add(entry.LevelId, new RoomProgress(Pick(rooms, entry.LevelId), entry.Origin));
            if (!states.ContainsKey(active)) throw new InvalidOperationException("请先将选中的房间放入世界");
            ActiveId = active;
            local = Current.Validated.Spawn.Cell;
            Facing = Current.Validated.Spawn.Rotation;
            Enter();
        }

        static PuzzleRun Assemble(WorldDocument world, Dictionary<string, RoomDraftModel> models, string active)
        {
            var rooms = new Dictionary<string, ValidatedRoom>();
            foreach (var pair in models)
            {
                try { rooms.Add(pair.Key, ValidatedRoom.From(pair.Value, pair.Value.Meta)); }
                catch (InvalidOperationException error) { throw new InvalidOperationException($"房间 {pair.Key} 未通过发布级校验：{error.Message}", error); }
            }
            return new PuzzleRun(world, rooms, models, null, active);
        }

        public static PuzzleRun FromDraft(RoomDraft draft)
        {
            var models = draft.World == null
                ? new Dictionary<string, RoomDraftModel> { [draft.ActiveId] = draft.Model }
                : new Dictionary<string, RoomDraftModel>(draft.Catalog);
            return Assemble(draft.World, models, draft.ActiveId);
        }

        public static PuzzleRun Campaign()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "World", "main.world.json");
            var world = WorldCodec.Decode(File.ReadAllText(path));
            string folder = Path.GetDirectoryName(path);
            var models = new Dictionary<string, RoomDraftModel>();
            foreach (var entry in world.Rooms)
            {
                var model = RoomCodec.Decode(File.ReadAllText(Path.Combine(folder, entry.File)));
                model.Extras.TryGetValue("name", out var name);
                model.Extras.TryGetValue("themeId", out var theme);
                if (string.IsNullOrEmpty(model.Meta.Name) || model.Meta.ThemeId == null)
                    model.Meta = new RoomMeta(string.IsNullOrEmpty(model.Meta.Name) ? name ?? entry.LevelId : model.Meta.Name, model.Meta.ThemeId ?? theme);
                models.Add(entry.LevelId, model);
            }
            return Assemble(world, models, world.Start.LevelId);
        }

        public Vector2Int HomeOf(string boxId) =>
            ToVector(Current.Origin + Current.Validated.Room.ObjectsOf(ObjectCategories.Box).First(b => b.Id == boxId).Cell);

        public bool CanLeaveWorld(Vector2Int direction)
        {
            var end = World?.FinishEndpoint;
            if (end == null || Recovering || end.LevelId != ActiveId) return false;
            var door = Current.Validated.Doors.FirstOrDefault(d => d.Id == end.DoorId);
            return door != null && door.Cell == local && new Cell(direction.x, direction.y) == door.Outward
                && World.FinishRequiresSolved.All(id => states[id].Solved);
        }

        internal IReadOnlyList<BehaviorGraph> Interact(out bool transitioned)
        {
            transitioned = false;
            if (Recovering || Motion != null) return Array.Empty<BehaviorGraph>();
            var room = Current.Validated.Room;
            var cell = local + Cell.SearchOrder[Facing];
            var model = Models[ActiveId];
            var owners = Current.Boxes.Where(box => box.Value == cell).Select(box => box.Key)
                .Concat(room.Objects.Where(item => item.Cell == cell && item.CategoryId != ObjectCategories.Box).Select(item => item.Id))
                .Append(room.TerrainAt(cell).EntityId)
                .Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal);
            var graphs = new List<BehaviorGraph>();
            foreach (var owner in owners)
            {
                var states = Current.ObjectStates;
                states.TryGetValue(owner, out var before);
                Fire(ActiveId, TriggerIds.OnInteract, owner);
                states.TryGetValue(owner, out var after);
                transitioned |= before != after;
                graphs.AddRange(ObjectBehavior.GraphsOf(model, owner));
            }
            return graphs;
        }

        public bool Step(Vector2Int delta, out string pushed)
        {
            pushed = null;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1) return false;
            var direction = new Cell(delta.x, delta.y);
            var next = local + direction;
            if (Current.Validated.Room.TerrainAt(next).CategoryId == TerrainCategories.Void) return CrossDoor(direction);
            var door = Current.Validated.Doors.FirstOrDefault(d => d.Cell == next);
            if (door != null && door.Role == "exit" && !CanApproach(door)) return false;
            bool wasSolved = Current.Solved;
            var result = PushRules.TryPush(Current.Session, local, direction);
            if (!result.Moved) return false;
            local = result.Player;
            Facing = RotationSteps.DirectionIndex(direction);
            Current.Session.Place(local);
            Current.Session.RetryGateClosures(false);
            pushed = result.PushedBoxId;
            Current.RecordSolved();
            if (pushed != null) Fire(ActiveId, TriggerIds.FirstPush, pushed);
            if (!wasSolved && Current.Solved) Fire(ActiveId, TriggerIds.RoomSolved);
            return true;
        }
    }
}
