/**
 * @file WorldLayout.cs
 * @brief 世界文件的薄工具：编解码与规则全部交给 Core 的 WorldCodec 与 WorldValidator，本文件不含 JObject 与旧版本实现。
 * Empty(first)：建立只含一个起终点占位的有限世界；WithRooms、WithConnections、WithStart、WithFinish：世界文档不可变，用于生成修改后的新文档。
 * Sizes(catalog)、ToCoreRooms(catalog)：把房间目录转成 WorldValidator 需要的尺寸表与门信息表。
 * PlacementIssue(world, catalog, id, origin, moving[, size])：转调 WorldValidator.Placement，含边界与重叠判定。
 * ConnectionIssues(world, catalog, movingRoom, candidate)：转调 WorldValidator.Connections，movingRoom 非空时按候选原点预演。
 * EndpointIssues(world, catalog)：转调 WorldValidator.Endpoints。
 * Issues(world, catalog)：汇总前置房间引用、放置、各房结构错误、连接与起终点问题。
 * Read(path, catalog)：WorldCodec.Decode 后逐房经 RoomFile.Load 读入目录，检查相对引用与房间 ID，再整图校验。
 * Write(path, world, catalog)：整图校验后逐房经 RoomFile.Save 写成按内容哈希命名的文件，再用 WorldCodec.Encode 与 AtomicFile.Write 原子替换世界清单，返回带最新文件名的新世界文档。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Sokoban.Core;

namespace Sokoban
{
    public static class WorldLayout
    {
        public static WorldDocument Empty(string first) => new WorldDocument(Guid.NewGuid().ToString("N"), new Cell(0, 0), new Cell(63, 31),
            new List<WorldRoomRef>(), new List<WorldConnection>(), new WorldEndpoint(first, "spawn"), new WorldEndpoint(first, "exit"), new[] { first });

        public static WorldDocument WithRooms(WorldDocument w, IReadOnlyList<WorldRoomRef> rooms) =>
            new WorldDocument(w.WorldId, w.Min, w.Max, rooms, w.Connections, w.Start, w.FinishEndpoint, w.FinishRequiresSolved);

        public static WorldDocument WithConnections(WorldDocument w, IReadOnlyList<WorldConnection> connections) =>
            new WorldDocument(w.WorldId, w.Min, w.Max, w.Rooms, connections, w.Start, w.FinishEndpoint, w.FinishRequiresSolved);

        public static WorldDocument WithStart(WorldDocument w, WorldEndpoint start) =>
            new WorldDocument(w.WorldId, w.Min, w.Max, w.Rooms, w.Connections, start, w.FinishEndpoint, w.FinishRequiresSolved);

        public static WorldDocument WithFinish(WorldDocument w, WorldEndpoint finish) =>
            new WorldDocument(w.WorldId, w.Min, w.Max, w.Rooms, w.Connections, w.Start, finish, w.FinishRequiresSolved);

        public static Dictionary<string, Cell> Sizes(IReadOnlyDictionary<string, RoomDraftModel> catalog) =>
            catalog.ToDictionary(pair => pair.Key, pair => new Cell(pair.Value.Room.Width, pair.Value.Room.Height));

        static Dictionary<string, IReadOnlyList<Cell>> DecorationCells(IReadOnlyDictionary<string, RoomDraftModel> catalog) =>
            catalog.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<Cell>)pair.Value.Decorations.Select(decoration => decoration.Cell).ToArray());

        public static Dictionary<string, WorldRoomInfo> ToCoreRooms(IReadOnlyDictionary<string, RoomDraftModel> catalog) =>
            catalog.ToDictionary(pair => pair.Key, pair => new WorldRoomInfo(pair.Key,
                pair.Value.Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault()?.Id, pair.Value.Doors));

        public static string PlacementIssue(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog, string id, Cell origin, bool moving)
        {
            var room = catalog[id].Room;
            return PlacementIssue(world, catalog, id, origin, moving, new Cell(room.Width, room.Height));
        }

        public static string PlacementIssue(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog, string id, Cell origin, bool moving, Cell size) =>
            WorldValidator.Placement(world, Sizes(catalog), id, origin, moving, size,
                catalog.ToDictionary(pair => pair.Key, pair => pair.Value.Room.RecoveryDeviceFloor), DecorationCells(catalog));

        public static string RecoveryDeviceFloorIssue(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog, string id, Cell floor)
        {
            var placement = world?.Rooms.FirstOrDefault(room => room.LevelId == id);
            if (placement == null) return null;
            return WorldValidator.RecoveryDeviceFloorPlacement(world, Sizes(catalog),
                catalog.ToDictionary(pair => pair.Key, pair => pair.Value.Room.RecoveryDeviceFloor), id, placement.Origin, floor, DecorationCells(catalog));
        }

        public static IEnumerable<string> ConnectionIssues(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog, string movingRoom = null, Cell candidate = default) =>
            WorldValidator.Connections(world, ToCoreRooms(catalog), movingRoom, candidate).ToList();

        public static IEnumerable<string> EndpointIssues(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog) =>
            WorldValidator.Endpoints(world, ToCoreRooms(catalog)).ToList();

        public static IEnumerable<string> Issues(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog)
        {
            var known = new HashSet<string>(world.Rooms.Select(r => r.LevelId));
            foreach (var row in world.Rooms)
            {
                if (row.ForwardRequiresSolved.Any(required => !known.Contains(required))) yield return row.LevelId + " 的前置房间引用无效";
                if (!catalog.ContainsKey(row.LevelId)) { yield return row.LevelId + " 缺少房间数据"; continue; }
                var error = PlacementIssue(world, catalog, row.LevelId, row.Origin, true);
                if (error != null) yield return error;
                foreach (var issue in RoomValidator.ValidateDraft(catalog[row.LevelId], ValidationStage.Publish).Where(i => new[] { "spawn.missing", "door.geometry", "entry.occupied", "entry.missing", "boundary.open" }.Contains(i.RuleId))) yield return row.LevelId + " " + issue.Location + " " + issue.Message;
            }
            foreach (var issue in ConnectionIssues(world, catalog)) yield return issue;
            foreach (var issue in EndpointIssues(world, catalog)) yield return issue;
            if (world.FinishRequiresSolved.Any(id => !known.Contains(id))) yield return "世界终局的前置房间引用无效";
        }

        static void RequireValid(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog)
        {
            string issue = Issues(world, catalog).FirstOrDefault();
            RoomFile.Check(issue == null, issue);
        }

        static string RoomPath(string root, string relative)
        {
            RoomFile.Check(!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative) && !relative.Split('/', '\\').Contains(".."), "需要世界目录内的相对引用");
            return Path.Combine(root, relative);
        }

        public static WorldDocument Read(string path, Dictionary<string, RoomDraftModel> catalog)
        {
            WorldDocument world;
            try { world = WorldCodec.Decode(File.ReadAllText(path)); }
            catch (FormatException error) { throw new InvalidDataException(error.Message, error); }
            string root = Path.GetDirectoryName(Path.GetFullPath(path));
            foreach (var placement in world.Rooms)
            {
                string roomPath = RoomPath(root, placement.File);
                RoomFile.Check(File.Exists(roomPath), placement.LevelId + " 引用的房间文件缺失：" + placement.File);
                var room = RoomFile.Load(roomPath);
                RoomFile.Check(room.Room.Id == placement.LevelId && !catalog.ContainsKey(placement.LevelId), "房间 ID 不匹配或重复：" + placement.LevelId);
                catalog[placement.LevelId] = room;
            }
            RequireValid(world, catalog);
            return world;
        }

        public static WorldDocument Write(string path, WorldDocument source, IReadOnlyDictionary<string, RoomDraftModel> catalog)
        {
            RequireValid(source, catalog);
            string root = Path.GetDirectoryName(Path.GetFullPath(path));
            var rows = new List<WorldRoomRef>();
            foreach (var row in source.Rooms)
            {
                string text = RoomCodec.Encode(catalog[row.LevelId]);
                string relative = row.File;
                if (!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative) && !relative.Split('/', '\\').Contains("..") &&
                    File.Exists(Path.Combine(root, relative)) && File.ReadAllText(Path.Combine(root, relative)) == text)
                {
                    rows.Add(row);
                    continue;
                }
                using (var hash = SHA256.Create())
                    relative = "rooms/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant() + ".json";
                string target = Path.Combine(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                RoomFile.Save(target, catalog[row.LevelId]);
                rows.Add(new WorldRoomRef(row.LevelId, relative, row.Origin, row.ForwardRequiresSolved));
            }
            var written = WithRooms(source, rows);
            AtomicFile.Write(path, WorldCodec.Encode(written), temporary => WorldCodec.Decode(File.ReadAllText(temporary)));
            return written;
        }
    }
}
