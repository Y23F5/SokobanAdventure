/**
 * @file WorkspaceDraft.cs
 * @brief RoomDraft 的工作区部分：初始化、世界房间清单、笔划、快照与单独编辑，全部数据用 Core 类型。
 * Reset：以草稿模型、世界、房间目录和路径重置会话，并为每个房间建立持有者与新的命令历史。
 * NewBlankRoom/NewDefaultRoom：新建带西侧叉车的空白房间或默认布局房间；DefaultModel 读取 Resources 里的默认房间，Renamed 复制草稿并换房间 ID。
 * Origin/Placement：当前房间在世界中的原点与放置记录，单独编辑时原点由本地原点给出。
 * Snapshot/Recover：用 RoomCodec 与 WorldCodec 序列化全部房间、世界、未放置前的房间配置、路径与脏标记，不保存撤销历史。
 * ReadRemovedPlacements(root, world, catalog)：验证草稿里暂未放置房间的配置与引用，恢复候选值。
 * BeginWorld/Attach/UseRoom/RoomAt/PlaceRoom/RemoveRoom/SetConnectionMode：管理世界房间清单，修改世界文档的操作都经命令历史提交。
 * EnterStandaloneEdit/ReturnToWorld：以零原点单独编辑世界中的房间，返回时把改动作为一条可撤销命令写回世界并重跑世界校验。
 * Brush：经 BrushPlanner 整笔预检，世界视图越界整笔拒绝；TerrainPlacementError 给出笔刷悬停提示。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        sealed class Stash
        {
            public WorldDocument World;
            public Dictionary<string, RoomDraftModel> Catalog;
            public Dictionary<string, DraftHolder> Holders;
            public CommandHistory History;
            public Dictionary<string, WorldRoomRef> Removed;
            public string RoomId, WorkPath;
            public bool Unsaved;
        }

        sealed class DelegateCommand : ICommand
        {
            readonly Action apply, revert;
            public DelegateCommand(Action apply, Action revert) { this.apply = apply; this.revert = revert; }
            public void Apply() => apply();
            public void Revert() => revert();
        }

        sealed class Wrapped : ICommand
        {
            readonly ICommand inner;
            readonly Action applied, reverted;
            public Wrapped(ICommand inner, Action applied, Action reverted) { this.inner = inner; this.applied = applied; this.reverted = reverted; }
            public void Apply() { inner.Apply(); applied(); }
            public void Revert() { inner.Revert(); reverted(); }
        }

        Cell editorOrigin;
        bool unsaved;
        string activeId;
        Stash stash;
        readonly Dictionary<string, WorldRoomRef> removedPlacements = new Dictionary<string, WorldRoomRef>(StringComparer.Ordinal);

        public WorldDocument World { get; private set; }
        public string ActiveId => activeId;
        public bool IsStandalone => stash != null;
        public WorldRoomRef Placement => World?.Rooms.FirstOrDefault(r => r.LevelId == activeId);
        public Cell Origin => World == null ? editorOrigin : (Placement?.Origin ?? new Cell(0, 0));

        void Reset(RoomDraftModel model, WorldDocument world, Dictionary<string, RoomDraftModel> catalog, string path)
        {
            Selection.Clear();
            editorOrigin = new Cell(0, 0);
            removedPlacements.Clear();
            stash = null;
            World = world;
            Catalog = catalog ?? new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
            Catalog[model.Room.Id] = model;
            holders.Clear();
            foreach (var pair in Catalog) holders[pair.Key] = new DraftHolder(pair.Value);
            activeId = model.Room.Id;
            Selected = null;
            SelectedCell = null;
            Tool = RoomTool.Select;
            WorkPath = path;
            history = new CommandHistory();
            unsaved = false;
            revision++;
        }

        static RoomDraftModel DefaultModel() => RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text);

        static void CopyRest(RoomDraftModel src, RoomDraftModel next)
        {
            next.Meta = src.Meta;
            next.Decorations.AddRange(src.Decorations);
            next.Assets.AddRange(src.Assets);
            next.Graphs.AddRange(src.Graphs);
            next.Instances.AddRange(src.Instances);
            foreach (var pair in src.Templates) next.Templates[pair.Key] = pair.Value;
            foreach (var pair in src.ObjectStates) next.ObjectStates[pair.Key] = pair.Value;
            next.UiImageAssetId = src.UiImageAssetId;
        }

        static RoomDraftModel Renamed(RoomDraftModel src, string id)
        {
            var s = src.Room;
            var room = new Room(id, s.Width, s.Height);
            for (int x = 0; x < s.Width; x++)
                for (int y = 0; y < s.Height; y++)
                {
                    var cell = new Cell(x, y);
                    var t = s.TerrainAt(cell);
                    if (t.CategoryId != TerrainCategories.Void) room.SetTerrain(cell, t.CategoryId, t.EntityId, t.State, t.Rotation);
                    var look = s.AppearanceAt(cell);
                    if (look != null) room.SetAppearance(cell, look);
                }
            foreach (var o in s.Objects) room.AddObject(o.Id, o.CategoryId, o.Cell, o.Appearance, o.Rotation);
            var next = new RoomDraftModel(room, src.Doors, src.PrimaryEntranceId, src.Extras);
            next.ReplaceRecoveryDevice(src.RecoveryDevice);
            CopyRest(src, next);
            return next;
        }

        public void NewBlankRoom() => NewBlankRoom("room-" + Guid.NewGuid().ToString("N"), "空白房间", "warehouse", new Cell(15, 15));

        public void NewBlankRoom(string id, string name, string theme, Cell size)
        {
            RoomFile.Check(size.X > 0 && size.Y > 0, "房间尺寸必须为正");
            var model = new RoomDraftModel(new Room(id, size.X, size.Y));
            model.Meta = new RoomMeta(name, theme ?? "warehouse");
            model.ReplaceRecoveryDevice(new RecoveryDevice("recovery-device", RecoveryDeviceKind.Forklift,
                new Cell(-1, size.Y / 2), 1, "warehouse-lift", RecoveryDevice.DefaultDuration,
                itemName: "仓库恢复道具", itemDescription: "启动本房叉车，让箱子回到预设位置。",
                itemAcquiredHint: "按 R 启动本房恢复装置。"));
            Reset(model, null, null, null);
            unsaved = true;
        }

        public void NewDefaultRoom()
        {
            Reset(Renamed(DefaultModel(), "room-" + Guid.NewGuid().ToString("N")), null, null, null);
            unsaved = true;
        }

        void Execute(ICommand command) { history.Execute(command); revision++; }

        public void BeginWorld()
        {
            RoomFile.Check(World == null, "已在世界工作区");
            var world = WorldLayout.Empty(ActiveId);
            var spawn = Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            world = WorldLayout.WithStart(world, new WorldEndpoint(ActiveId, spawn?.Id ?? "spawn"));
            var exit = Model.Doors.LastOrDefault(d => d.Role == "exit") ?? Model.Doors.LastOrDefault();
            world = WorldLayout.WithFinish(world, new WorldEndpoint(ActiveId, exit?.Id ?? "exit"));
            World = world;
            WorkPath = null;
            history = new CommandHistory();
            unsaved = true;
            revision++;
        }

        public void Attach(string path)
        {
            var candidate = RoomFile.Load(path);
            string id = candidate.Room.Id;
            RoomFile.Check(World != null && !Catalog.ContainsKey(id), "房间已在目录中或尚未创建世界");
            string previous = activeId;
            Execute(new DelegateCommand(
                () => { Catalog[id] = candidate; holders[id] = new DraftHolder(candidate); },
                () => { Catalog.Remove(id); holders.Remove(id); if (activeId == id) activeId = previous; }));
            UseRoom(id);
        }

        public void UseRoom(string id)
        {
            RoomFile.Check(Catalog.ContainsKey(id), "目录中没有房间 " + id);
            Selection.Clear();
            activeId = id;
            Selected = null;
            SelectedCell = null;
            Tool = RoomTool.Select;
            revision++;
        }

        public string RoomAt(Cell point)
        {
            if (World == null) return ActiveId;
            foreach (var row in World.Rooms)
                if (Catalog[row.LevelId].Room.Inside(point - row.Origin)) return row.LevelId;
            return null;
        }

        public void PlaceRoom(Cell origin, bool move)
        {
            RoomFile.Check(World != null && (!move || Placement != null), "请先放置房间");
            var error = WorldLayout.PlacementIssue(World, Catalog, ActiveId, origin, move);
            RoomFile.Check(error == null, error);
            string id = ActiveId;
            var before = World;
            WorldRoomRef old = Placement;
            bool hadRemoved = removedPlacements.TryGetValue(id, out var removedRow);
            var rooms = move
                ? before.Rooms.Select(r => r.LevelId == id ? new WorldRoomRef(id, r.File, origin, r.ForwardRequiresSolved) : r).ToList()
                : before.Rooms.Concat(new[] { hadRemoved ? new WorldRoomRef(id, removedRow.File, origin, removedRow.ForwardRequiresSolved) : new WorldRoomRef(id, "", origin, new string[0]) }).ToList();
            var after = WorldLayout.WithRooms(before, rooms);
            Execute(new DelegateCommand(
                () => { World = after; removedPlacements.Remove(id); },
                () => { World = before; if (hadRemoved) removedPlacements[id] = removedRow; }));
        }

        public void RemoveRoom()
        {
            RoomFile.Check(Placement != null, "房间尚未放置");
            string id = ActiveId;
            var placed = Placement;
            var before = World;
            var after = WorldLayout.WithRooms(before, before.Rooms.Where(r => r.LevelId != id).ToList());
            Execute(new DelegateCommand(
                () => { World = after; removedPlacements[id] = placed; },
                () => { World = before; removedPlacements.Remove(id); }));
        }

        public void AddConnection(WorldEndpoint from, WorldEndpoint to, string mode, bool allowReturn)
        {
            RoomFile.Check(World != null && from != null && to != null, "请选择世界中的两个门端点");
            RoomFile.Check(from.LevelId != to.LevelId, "请选择不同房间的门");
            var link = new WorldConnection(Guid.NewGuid().ToString("N"), mode, from, to, allowReturn);
            var before = World;
            var after = WorldLayout.WithConnections(before, before.Connections.Append(link).ToArray());
            string issue = WorldLayout.ConnectionIssues(after, Catalog)
                .FirstOrDefault(message => message.StartsWith(link.Id, StringComparison.Ordinal));
            RoomFile.Check(issue == null, issue);
            Execute(new DelegateCommand(() => World = after, () => World = before));
        }

        public void RemoveConnection(string id)
        {
            RoomFile.Check(World != null && World.Connections.Any(link => link.Id == id), "未找到连接 " + id);
            var before = World;
            var after = WorldLayout.WithConnections(before, before.Connections.Where(link => link.Id != id).ToArray());
            Execute(new DelegateCommand(() => World = after, () => World = before));
        }

        public void SetConnectionMode(string id, string mode)
        {
            RoomFile.Check(mode == WorldCodec.Teleport || mode == WorldCodec.Direct, "未知连接方式");
            var link = World?.Connections.SingleOrDefault(row => row.Id == id);
            RoomFile.Check(link != null, "未找到连接 " + id);
            if (link.Mode == mode) return;
            var before = World;
            var after = WorldLayout.WithConnections(before, before.Connections.Select(c => c.Id == id ? new WorldConnection(c.Id, mode, c.From, c.To, c.AllowReturn) : c).ToList());
            Execute(new DelegateCommand(() => World = after, () => World = before));
        }

        public string Snapshot()
        {
            var rooms = new Dictionary<string, object>();
            foreach (var pair in Catalog) rooms[pair.Key] = RoomCodec.Encode(pair.Value);
            var root = new Dictionary<string, object>
            {
                ["active"] = activeId,
                ["path"] = WorkPath,
                ["dirty"] = Dirty,
                ["rooms"] = rooms,
                ["world"] = World == null ? null : WorldCodec.EncodeDraft(World, Catalog.Keys),
                ["removedPlacements"] = removedPlacements.Values.OrderBy(row => row.LevelId, StringComparer.Ordinal)
                    .Select(row => (object)new Dictionary<string, object>
                    {
                        ["id"] = row.LevelId,
                        ["file"] = row.File,
                        ["requires"] = row.ForwardRequiresSolved.Select(id => (object)id).ToList(),
                    }).ToList(),
                ["originX"] = (long)editorOrigin.X,
                ["originY"] = (long)editorOrigin.Y,
            };
            return MiniJson.Write(root);
        }

        public void Recover(string snapshot)
        {
            var root = MiniJson.Parse(snapshot) as Dictionary<string, object>;
            RoomFile.Check(root != null && root.TryGetValue("rooms", out var raw) && raw is Dictionary<string, object>, "草稿快照无效");
            var catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
            try
            {
                foreach (var pair in (Dictionary<string, object>)root["rooms"]) catalog[pair.Key] = RoomCodec.Decode((string)pair.Value);
            }
            catch (FormatException error) { throw new InvalidDataException(error.Message, error); }
            string active = root.TryGetValue("active", out var a) ? a as string : null;
            RoomFile.Check(active != null && catalog.ContainsKey(active), "草稿房间引用缺失");
            WorldDocument world = null;
            if (root.TryGetValue("world", out var w) && w is string worldText)
            {
                try { world = WorldCodec.DecodeDraft(worldText, catalog.Keys); }
                catch (FormatException error) { throw new InvalidDataException(error.Message, error); }
            }
            string path = root.TryGetValue("path", out var p) ? p as string : null;
            bool dirty = root.TryGetValue("dirty", out var d) && d is bool flag && flag;
            var removed = ReadRemovedPlacements(root, world, catalog);
            Reset(catalog[active], world, catalog, path);
            foreach (var pair in removed) removedPlacements.Add(pair.Key, pair.Value);
            if (root.TryGetValue("originX", out var ox) && ox is long x && root.TryGetValue("originY", out var oy) && oy is long y && world == null)
                editorOrigin = new Cell((int)x, (int)y);
            unsaved = dirty;
        }

        static Dictionary<string, WorldRoomRef> ReadRemovedPlacements(Dictionary<string, object> root,
            WorldDocument world, Dictionary<string, RoomDraftModel> catalog)
        {
            var removed = new Dictionary<string, WorldRoomRef>(StringComparer.Ordinal);
            if (!root.TryGetValue("removedPlacements", out var value)) return removed;
            if (!(value is List<object> rows)) throw new InvalidDataException("草稿未放置房间记录无效");
            var placed = world == null ? new HashSet<string>(StringComparer.Ordinal) :
                new HashSet<string>(world.Rooms.Select(row => row.LevelId), StringComparer.Ordinal);
            foreach (var item in rows)
            {
                if (world == null || !(item is Dictionary<string, object> row) ||
                    !row.TryGetValue("id", out var rawId) || !(rawId is string id) ||
                    !catalog.ContainsKey(id) || placed.Contains(id) || removed.ContainsKey(id) ||
                    !row.TryGetValue("file", out var rawFile) || !(rawFile is string file) ||
                    !row.TryGetValue("requires", out var rawRequires) || !(rawRequires is List<object> requires) ||
                    requires.Any(required => !(required is string key) || !catalog.ContainsKey(key)))
                    throw new InvalidDataException("草稿未放置房间配置无效");
                removed.Add(id, new WorldRoomRef(id, file, default, requires.Cast<string>().ToArray()));
            }
            return removed;
        }

        public string TerrainPlacementError(Cell cell, RoomTool tool)
        {
            if (World != null && Placement == null) return "房间尚未放置";
            if (!Room.Inside(cell) && World != null) return "请进入单独编辑后再扩建";
            return tool == RoomTool.Wall && PlacementRules.NeedsFloor(Model, cell) ? "对象或门内侧占用 " + cell : null;
        }

        public string Brush(IEnumerable<Cell> cells, RoomTool tool)
        {
            if (tool != RoomTool.Wall && tool != RoomTool.Floor && tool != RoomTool.Erase && tool != RoomTool.Vine) return "当前工具不是地形画刷";
            if (World != null && Placement == null) return "房间尚未放置";
            if (tool == RoomTool.Erase)
            {
                var inside = cells.Where(cell => Room.Inside(cell)).ToArray();
                if (inside.Length == cells.Count() && EraseContent(inside)) return null;
            }
            var plan = BrushPlanner.Preflight(Model, cells, tool == RoomTool.Wall ? BrushTool.Wall : tool == RoomTool.Floor ? BrushTool.Floor : tool == RoomTool.Vine ? BrushTool.Openable : BrushTool.Erase, rotation: BrushRotation,
                appearance: tool == RoomTool.Erase ? null : TerrainBrush(tool == RoomTool.Wall ? TerrainCategories.Wall : tool == RoomTool.Vine ? TerrainCategories.Openable : TerrainCategories.Floor));
            if (plan.Error != null) return plan.Error;
            if (World != null && plan.Expands) return "请进入单独编辑后再扩建";
            if (plan.NoChange) return null;
            var shift = new Cell(-plan.Left, -plan.Bottom);
            var command = new Wrapped(plan.CreateCommand(holders[ActiveId]),
                () => { if (World == null) editorOrigin = editorOrigin + shift; },
                () => { if (World == null) editorOrigin = editorOrigin - shift; });
            Commit(command, null);
            return null;
        }

        public void EnterStandaloneEdit(string roomId)
        {
            RoomFile.Check(World != null && stash == null, "当前不在世界视图");
            RoomFile.Check(Catalog.ContainsKey(roomId), "目录中没有房间 " + roomId);
            stash = new Stash
            {
                World = World,
                Catalog = Catalog,
                Holders = new Dictionary<string, DraftHolder>(holders, StringComparer.Ordinal),
                History = history,
                Removed = new Dictionary<string, WorldRoomRef>(removedPlacements, StringComparer.Ordinal),
                RoomId = activeId,
                WorkPath = WorkPath,
                Unsaved = unsaved,
            };
            var source = Catalog[roomId];
            var copy = CloneWith(source, source.Doors, source.PrimaryEntranceId);
            World = null;
            Catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal) { [roomId] = copy };
            holders.Clear();
            holders[roomId] = new DraftHolder(copy);
            removedPlacements.Clear();
            activeId = roomId;
            editorOrigin = new Cell(0, 0);
            history = new CommandHistory();
            Selection.Clear();
            Selected = null;
            SelectedCell = null;
            Tool = RoomTool.Select;
            revision++;
        }

        public List<string> ReturnToWorld()
        {
            RoomFile.Check(stash != null, "当前不在单独编辑");
            var s = stash;
            string id = activeId;
            var edited = holders[id].Draft;
            bool changed = history.Revision > 0;
            stash = null;
            World = s.World;
            Catalog = s.Catalog;
            holders.Clear();
            foreach (var pair in s.Holders) holders[pair.Key] = pair.Value;
            removedPlacements.Clear();
            foreach (var pair in s.Removed) removedPlacements[pair.Key] = pair.Value;
            history = s.History;
            WorkPath = s.WorkPath;
            unsaved = s.Unsaved;
            editorOrigin = new Cell(0, 0);
            activeId = id;
            Selection.Clear();
            Selected = null;
            SelectedCell = null;
            Tool = RoomTool.Select;
            if (changed)
            {
                var original = Catalog[id];
                Execute(new DelegateCommand(
                    () => { Catalog[id] = edited; holders[id].Draft = edited; },
                    () => { Catalog[id] = original; holders[id].Draft = original; }));
            }
            else revision++;
            return WorldLayout.Issues(World, Catalog).ToList();
        }

    }
}
