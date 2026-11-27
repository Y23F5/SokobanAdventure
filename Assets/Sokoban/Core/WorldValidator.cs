/**
 * @file WorldValidator.cs
 * @brief 世界级检查的纯规则，与旧 WorldLayout 的 PlacementIssue、ConnectionIssues、起终点检查的判定与文案一致，供世界编辑与世界校验共用。
 * WorldRoomInfo：一个房间的最小门信息输入，含房间 ID、出生点 ID 与门列表（复用 DoorInfo，朝向为向外方向）。
 * WorldValidator.Placement(world, sizes, id, origin, moving, size)：返回不能放置的原因，可以放置时返回空；
 * 先判世界边界（房间占地超出 Min 与 Max 时给出首个冲突格），再逐个比较已放置房间：同 ID 时 moving 为真则跳过、否则报已放置，
 * 占地矩形相交时报两房重叠并给出两房原点逐轴取大的冲突格，仅边相接不算重叠；sizes 为各房间的宽高，按房间 ID 查找。
 * WorldValidator.Connections(world, rooms, movingRoom, candidate)：逐条检查连接，报连接 ID 缺失或重复、端点（房间已放置且门存在）缺失、
 * 同一门被多条连接占用；direct 模式要求两门向外方向相反且按原点换算后的门外格与对门所在格重合，teleport 不查几何，其他模式无效；
 * movingRoom 非空时该房间按 candidate 原点计算且无需已放置，用于平移预演。
 * WorldValidator.Endpoints(world, rooms)：检查起点房间已放置且出生点 ID 与房间一致，检查终点门存在于已放置房间；
 * 旧终点门文案附带 JSON 端点，此处改为房间 ID 加门 ID。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class WorldRoomInfo
    {
        public string LevelId { get; }
        public string SpawnId { get; }
        public IReadOnlyList<DoorInfo> Doors { get; }

        public WorldRoomInfo(string levelId, string spawnId, IReadOnlyList<DoorInfo> doors)
        {
            LevelId = levelId;
            SpawnId = spawnId;
            Doors = doors ?? new DoorInfo[0];
        }
    }

    public static class WorldValidator
    {
        static bool FloorOverlaps(Cell floor, Cell origin, Cell otherOrigin, Cell otherSize, Cell? otherFloor,
            IReadOnlyList<Cell> otherDecorations, out long x, out long y)
        {
            x = (long)origin.X + floor.X;
            y = (long)origin.Y + floor.Y;
            if (x >= otherOrigin.X && x < (long)otherOrigin.X + otherSize.X &&
                y >= otherOrigin.Y && y < (long)otherOrigin.Y + otherSize.Y) return true;
            if (otherFloor.HasValue && x == (long)otherOrigin.X + otherFloor.Value.X &&
                y == (long)otherOrigin.Y + otherFloor.Value.Y) return true;
            if (otherDecorations == null) return false;
            foreach (var cell in otherDecorations)
                if (x == (long)otherOrigin.X + cell.X && y == (long)otherOrigin.Y + cell.Y) return true;
            return false;
        }

        public static string RecoveryDeviceFloorPlacement(WorldDocument world, IReadOnlyDictionary<string, Cell> sizes,
            IReadOnlyDictionary<string, Cell?> recoveryDeviceFloors, string id, Cell origin, Cell floor,
            IReadOnlyDictionary<string, IReadOnlyList<Cell>> decorations = null)
        {
            foreach (var other in world.Rooms)
            {
                if (other.LevelId == id) continue;
                recoveryDeviceFloors.TryGetValue(other.LevelId, out var otherFloor);
                IReadOnlyList<Cell> otherDecorations = null;
                decorations?.TryGetValue(other.LevelId, out otherDecorations);
                if (FloorOverlaps(floor, origin, other.Origin, sizes[other.LevelId], otherFloor, otherDecorations, out var floorX, out var floorY))
                    return id + " 与 " + other.LevelId + " 占地重叠 (" + floorX + ", " + floorY + ")";
            }
            return null;
        }

        public static string Placement(WorldDocument world, IReadOnlyDictionary<string, Cell> sizes, string id, Cell origin, bool moving, Cell size,
            IReadOnlyDictionary<string, Cell?> recoveryDeviceFloors = null,
            IReadOnlyDictionary<string, IReadOnlyList<Cell>> decorations = null)
        {
            long right = (long)origin.X + size.X, top = (long)origin.Y + size.Y;
            if (origin.X < world.Min.X || origin.Y < world.Min.Y || right - 1 > world.Max.X || top - 1 > world.Max.Y)
            {
                long outsideX = origin.X;
                long outsideY = origin.Y;
                if (origin.X >= world.Min.X && origin.Y >= world.Min.Y)
                {
                    if (right - 1 > world.Max.X) outsideX = Math.Max(outsideX, (long)world.Max.X + 1);
                    else outsideY = Math.Max(outsideY, (long)world.Max.Y + 1);
                }
                return id + " 超出世界边界，冲突格 (" + outsideX + ", " + outsideY + ")";
            }
            foreach (var other in world.Rooms)
            {
                if (other.LevelId == id)
                {
                    if (moving) continue;
                    return id + " 已放置";
                }
                var pos = other.Origin;
                var extent = sizes[other.LevelId];
                if (origin.X < (long)pos.X + extent.X && right > pos.X && origin.Y < (long)pos.Y + extent.Y && top > pos.Y)
                    return id + " 与 " + other.LevelId + " 占地重叠 (" + Math.Max(origin.X, pos.X) + ", " + Math.Max(origin.Y, pos.Y) + ")";
                Cell? ownFloor = recoveryDeviceFloors != null && recoveryDeviceFloors.TryGetValue(id, out var own) ? own : null;
                Cell? otherFloor = recoveryDeviceFloors != null && recoveryDeviceFloors.TryGetValue(other.LevelId, out var theirs) ? theirs : null;
                IReadOnlyList<Cell> ownDecorations = null, otherDecorations = null;
                decorations?.TryGetValue(id, out ownDecorations);
                decorations?.TryGetValue(other.LevelId, out otherDecorations);
                if (ownFloor.HasValue && FloorOverlaps(ownFloor.Value, origin, pos, extent, otherFloor, otherDecorations, out var ownX, out var ownY))
                    return id + " 与 " + other.LevelId + " 占地重叠 (" + ownX + ", " + ownY + ")";
                if (otherFloor.HasValue && FloorOverlaps(otherFloor.Value, pos, origin, size, null, ownDecorations, out var otherX, out var otherY))
                    return id + " 与 " + other.LevelId + " 占地重叠 (" + otherX + ", " + otherY + ")";
            }
            return null;
        }

        static DoorInfo FindDoor(WorldEndpoint endpoint, WorldDocument world, IReadOnlyDictionary<string, WorldRoomInfo> rooms, out Cell origin, string movingRoom, Cell candidate)
        {
            origin = new Cell(0, 0);
            if (endpoint == null) return null;
            string id = endpoint.LevelId;
            if (movingRoom != null && id == movingRoom) origin = candidate;
            else
            {
                var placed = world.Rooms.FirstOrDefault(r => r.LevelId == id);
                if (placed == null) return null;
                origin = placed.Origin;
            }
            if (!rooms.TryGetValue(id, out var info) || info == null) return null;
            return info.Doors.FirstOrDefault(d => d.Id == endpoint.DoorId);
        }

        public static IEnumerable<string> Connections(WorldDocument world, IReadOnlyDictionary<string, WorldRoomInfo> rooms, string movingRoom = null, Cell candidate = default)
        {
            var ids = new HashSet<string>();
            var endpoints = new HashSet<(string, string)>();
            foreach (var link in world.Connections)
            {
                string id = link.Id;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) yield return "连接 ID 缺失或重复 " + id;
                var from = FindDoor(link.From, world, rooms, out var a, movingRoom, candidate);
                var to = FindDoor(link.To, world, rooms, out var b, movingRoom, candidate);
                foreach (var end in new[] { link.From, link.To })
                {
                    var key = (end?.LevelId, end?.DoorId);
                    if (!endpoints.Add(key)) yield return id + " 端点重复使用 (" + key.Item1 + ", " + key.Item2 + ")";
                }
                string label = id + " " + link.From?.LevelId + "." + link.From?.DoorId + " → " + link.To?.LevelId + "." + link.To?.DoorId;
                if (from == null || to == null) yield return label + " 端点缺失";
                else if (link.Mode == WorldCodec.Direct)
                {
                    var opposite = new Cell(-to.Outward.X, -to.Outward.Y);
                    if (from.Outward != opposite || a + from.Cell + from.Outward != b + to.Cell)
                        yield return label + " 直连门不相邻或朝向不匹配";
                }
                else if (link.Mode != WorldCodec.Teleport) yield return label + " 连接方式无效";
            }
        }

        public static IEnumerable<string> Endpoints(WorldDocument world, IReadOnlyDictionary<string, WorldRoomInfo> rooms)
        {
            var start = world.Start;
            string startRoom = start?.LevelId;
            if (!world.Rooms.Any(r => r.LevelId == startRoom)
                || !rooms.TryGetValue(startRoom, out var startInfo) || startInfo == null || startInfo.SpawnId != start.DoorId)
                yield return "世界起点无效 " + startRoom;
            var finish = world.FinishEndpoint;
            if (FindDoor(finish, world, rooms, out _, null, default) == null)
                yield return "世界终点门无效 " + finish?.LevelId + "." + finish?.DoorId;
        }
    }
}
