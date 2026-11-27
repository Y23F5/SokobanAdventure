/**
 * @file RoomDraft.cs
 * @brief 两端共用单房编辑会话，包装 Core.RoomDraftModel；全部修改经 Core 命令提交，历史用 Core 的 CommandHistory。
 * RoomTool：界面工具枚举，只供界面使用，不作序列化键。
 * Model、Room、Catalog、Selected、SelectedCell、Selection、Tool、WorkPath、Revision、Dirty：当前草稿、房间、房间目录与界面状态，Dirty 取自命令历史的修订号与保存基线比较。
 * Commit(command, select)：统一提交入口，执行命令并压栈、清空多选、同步房间目录、选中指定对象；命令抛异常时不改变任何状态。
 * Undo/Redo：经 CommandHistory 后退与重做，随后同步房间目录并清除选择。
 * Find/FindDecoration：按稳定 ID 取对象或装饰；Select/SelectRectangle/Click：按格选择，Click 把当前工具翻译为放置或移动。
 * Place/Move/PlacementError：经 PlacementRules 预检后提交 PlaceObjectCommand、MoveObjectCommand；SetRoomMarker：出生点用 SetSpawnCommand，门写门地形与 DoorInfo，朝向取 DoorPlacement.Outward；
 * BrushRotation 与 RotateBrush 保存本次放置预览的四分之一圈档位；PlacementMarkerError 预检出生点与门，TryMarkerTool 映射房间标记工具。
 * RotateSelection 将所选对象或格的旋转归并到一次可撤销提交；SetAssetHeight 按房内素材 ID 提交高度偏移。
 * MarkerOccupied/DoorDirection：设置门与出生点前的占用与朝向判定；RemoveObject/Rename：经命令删除对象（连带行为图）与改名；
 * Open/Save/ExportRoom：读入世界或单房，保存时用 Core 编解码并以 CommandHistory.MarkSaved 更新保存基线，导出前要求发布级无错误。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public enum RoomTool { Select, Box, Target, Move, Floor, Wall, Erase, Decoration, Spawn, Entry, Exit, Hint }

    public sealed partial class RoomDraft
    {
        readonly Dictionary<string, DraftHolder> holders = new Dictionary<string, DraftHolder>();
        CommandHistory history = new CommandHistory();
        RoomTool tool;
        int revision;

        public RoomDraftModel Model => holders[ActiveId].Draft;
        public Room Room => Model.Room;
        public Dictionary<string, RoomDraftModel> Catalog { get; private set; } = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
        public string Selected { get; private set; }
        public Cell? SelectedCell { get; private set; }
        public readonly HashSet<Cell> Selection = new HashSet<Cell>();
        public string WorkPath { get; private set; }
        public int Revision => revision;
        public bool Dirty => history.Dirty || unsaved;
        public int BrushRotation { get; private set; }
        public RoomTool Tool
        {
            get => tool;
            set { tool = value; BrushRotation = 0; ClearBrush(); }
        }

        public RoomDraft(RoomDraftModel model) { Reset(model, null, null, null); }

        public void Commit(ICommand command, string select = null)
        {
            history.Execute(command);
            Selection.Clear();
            SyncCatalog();
            Selected = select;
            SelectedCell = select == null ? (Cell?)null : (Find(select)?.Cell ?? FindDecoration(select)?.Cell ?? FindRecoveryDevice(select)?.Anchor);
            revision++;
        }

        void SyncCatalog() { foreach (var pair in holders) Catalog[pair.Key] = pair.Value.Draft; }

        void AfterHistory()
        {
            SyncCatalog();
            Selection.Clear();
            Selected = null;
            SelectedCell = null;
            revision++;
        }

        public void Undo() { RoomFile.Check(history.CanUndo, "没有可执行的历史操作"); history.Undo(); AfterHistory(); }

        public void Redo() { RoomFile.Check(history.CanRedo, "没有可执行的历史操作"); history.Redo(); AfterHistory(); }

        public PlacedObject Find(string id) => id == null ? null : Room.Objects.FirstOrDefault(o => o.Id == id);

        public Decoration FindDecoration(string id) => id == null ? null : Model.Decorations.FirstOrDefault(d => d.Id == id);

        string IdAt(Cell cell) => Room.ObjectAt(cell)?.Id ?? Model.Decorations.FirstOrDefault(d => d.Cell == cell)?.Id
            ?? (Model.RecoveryDevice?.Anchor == cell ? Model.RecoveryDevice.Id : null);

        public void Select(Cell cell) => Select(cell, false);

        public void Select(Cell cell, bool additive)
        {
            if (!additive) Selection.Clear();
            if (Room.TerrainAt(cell).CategoryId != TerrainCategories.Void && !Selection.Add(cell) && additive)
            {
                Selection.Remove(cell);
                if (Selection.Count == 0) { Selected = null; SelectedCell = null; revision++; return; }
                cell = Selection.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            }
            SelectedCell = Room.Inside(cell) || Model.RecoveryDevice?.Anchor == cell ? cell : (Cell?)null;
            Selected = IdAt(cell);
            revision++;
        }

        public void SelectRectangle(Cell start, Cell finish)
        {
            int x0 = Math.Max(Math.Min(start.X, finish.X), 0), y0 = Math.Max(Math.Min(start.Y, finish.Y), 0);
            int x1 = Math.Min(Math.Max(start.X, finish.X), Room.Width - 1), y1 = Math.Min(Math.Max(start.Y, finish.Y), Room.Height - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var point = new Cell(x, y);
                    if (Room.TerrainAt(point).CategoryId == TerrainCategories.Void) continue;
                    Selection.Add(point);
                    SelectedCell = point;
                }
            if (SelectedCell.HasValue) Selected = IdAt(SelectedCell.Value);
            revision++;
        }

        public void Click(Cell cell)
        {
            switch (Tool)
            {
                case RoomTool.Box: Place(true, cell); break;
                case RoomTool.Target: Place(false, cell); break;
                case RoomTool.Move: Move(cell); break;
                case RoomTool.Decoration: PlaceAsset(cell); break;
                case RoomTool.Hint:
                    string id = "hint-" + Guid.NewGuid().ToString("N");
                    Commit(new PlaceObjectCommand(holders[ActiveId], id, ObjectCategories.Hint, cell, null, BrushRotation), id);
                    break;
                case RoomTool.Spawn:
                case RoomTool.Entry:
                case RoomTool.Exit:
                    RoomFile.Check(TryMarkerTool(Tool, out var role), "未知房间标记");
                    var error = PlacementMarkerError(cell);
                    RoomFile.Check(error == null, error);
                    SetRoomMarker(cell, role, BrushRotation);
                    break;
                default: Select(cell); break;
            }
        }

        
        public string PlacementError(bool box, Cell cell, string ignore = null) =>
            PlacementRules.Error(Model, box ? ObjectCategories.Box : ObjectCategories.Target, cell, ignore);

        public string PlacementError(string categoryId, Cell cell, string ignore = null) =>
            PlacementRules.Error(Model, categoryId, cell, ignore);

        public void Place(bool box, Cell cell) => Place(box, cell, (box ? "box-" : "target-") + Guid.NewGuid().ToString("N"));

        public void Place(bool box, Cell cell, string id)
        {
            var error = PlacementError(box, cell);
            RoomFile.Check(error == null, error);
            RoomFile.Check(!string.IsNullOrWhiteSpace(id) && !Room.Ids.Contains(id), "对象 ID 为空或已占用");
            var look = BrushCategory == (box ? ObjectCategories.Box : ObjectCategories.Target) ? BrushAppearance : null;
            Commit(new PlaceObjectCommand(holders[ActiveId], id, box ? ObjectCategories.Box : ObjectCategories.Target, cell, look, BrushRotation), id);
            NotifyPlacement(id);
        }

        public void Move(Cell cell)
        {
            var item = Find(Selected);
            RoomFile.Check((item != null && item.CategoryId != ObjectCategories.Spawn) || FindDecoration(Selected) != null, "请选择箱子、目标或装饰");
            if (item == null) { MoveDecoration(Selected, cell); return; }
            if (item.Cell == cell) return;
            var error = PlacementError(item.CategoryId == ObjectCategories.Box, cell, Selected);
            RoomFile.Check(error == null, error);
            Commit(new MoveObjectCommand(Model, Selected, cell), Selected);
        }

        public void RotateSelection(int quarterTurns)
        {
            if (quarterTurns % 4 == 0) return;
            var holder = holders[ActiveId];
            var selectedId = Selected;
            var selectedCell = SelectedCell;
            var points = Selection.ToArray();
            var targets = points.Length != 0 ? points : selectedCell.HasValue ? new[] { selectedCell.Value } : Array.Empty<Cell>();
            var commands = new List<ICommand>(targets.Length);
            foreach (var cell in targets)
            {
                string id = IdAt(cell);
                if (id != null) commands.Add(new RotateCommand(holder, id, quarterTurns));
                else if (Room.TerrainAt(cell).CategoryId != TerrainCategories.Void) commands.Add(new RotateCommand(holder, cell, quarterTurns));
            }
            if (targets.Length == 0 && selectedId != null) commands.Add(new RotateCommand(holder, selectedId, quarterTurns));
            RoomFile.Check(commands.Count > 0, "请选择可旋转的地形、门或物件");
            Commit(new DelegateCommand(
                () =>
                {
                    int applied = 0;
                    try
                    {
                        for (; applied < commands.Count; applied++) commands[applied].Apply();
                    }
                    catch
                    {
                        while (applied > 0) commands[--applied].Revert();
                        throw;
                    }
                },
                () =>
                {
                    for (int i = commands.Count - 1; i >= 0; i--) commands[i].Revert();
                }), selectedId);
            SelectedCell = selectedCell;
            Selection.UnionWith(points);
        }

        public void SetAssetHeight(string assetId, float heightOffset)
        {
            int milligrid = AssetDescriptor.ToMilligrid(heightOffset);
            if (Model.Assets.Any(asset => asset.Id == assetId && asset.HeightMilligrid == milligrid)) return;
            var points = Selection.ToArray();
            var cell = SelectedCell;
            Commit(new SetAssetHeightCommand(holders[ActiveId], assetId, heightOffset), Selected);
            SelectedCell = cell;
            Selection.UnionWith(points);
        }

        public bool MarkerOccupied(Cell cell) => PlacementRules.Occupied(Model, cell, null) || Model.Doors.Any(d => d.Cell == cell);

        public string DoorDirection(Cell cell)
        {
            var outward = DoorPlacement.Outward(Room, cell);
            if (!outward.HasValue) return null;
            var value = outward.Value;
            return value == Cell.Up ? "N" : value == Cell.Right ? "E" : value == Cell.Down ? "S" : "W";
        }

        string FreshMarkerId(string role) => Room.Ids.Contains(role) ? role + "-" + Guid.NewGuid().ToString("N") : role;

        public void RotateBrush(int quarterTurns) => BrushRotation = RotationSteps.Turn(BrushRotation, quarterTurns);

        public static bool TryMarkerTool(RoomTool tool, out string role)
        {
            role = tool == RoomTool.Spawn ? "spawn" : tool == RoomTool.Entry ? "entry" : tool == RoomTool.Exit ? "exit" : null;
            return role != null;
        }

        public string PlacementMarkerError(Cell cell)
        {
            if (Tool == RoomTool.Spawn)
            {
                var entrance = Model.Doors.FirstOrDefault(d => d.Id == Model.PrimaryEntranceId && d.Role == "entry");
                if (entrance == null) return "请先放置主入口";
                var spawn = entrance.Cell - entrance.Outward;
                if (cell != spawn) return "请点击主入口内侧 " + spawn;
                return PlacementRules.Error(Model, ObjectCategories.Spawn, cell,
                    Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault()?.Id);
            }
            return DoorMarkerError(cell, BrushRotation);
        }

        internal Cell? MarkerOutward(Cell cell, int turns)
        {
            var initial = DoorPlacement.Outward(Room, cell);
            if (!initial.HasValue) return null;
            return Cell.SearchOrder[RotationSteps.Turn(RotationSteps.DirectionIndex(initial.Value), turns)];
        }

        string DoorMarkerError(Cell cell, int turns)
        {
            if (Room.TerrainAt(cell).CategoryId != TerrainCategories.Floor) return "请在地板格上放置门";
            if (MarkerOccupied(cell)) return "格子已占用";
            var outward = MarkerOutward(cell, turns);
            if (!outward.HasValue || Room.TerrainAt(cell + outward.Value).CategoryId != TerrainCategories.Void
                || Room.TerrainAt(cell - outward.Value).CategoryId != TerrainCategories.Floor) return "门朝向不可通行";
            return null;
        }

        public void SetRoomMarker(Cell cell, string role, int? rotation = null)
        {
            RoomFile.Check(role == "spawn" || role == "entry" || role == "exit", "未知房间标记");
            if (role == "spawn")
            {
                Commit(new SetSpawnCommand(holders[ActiveId], rotation), "spawn");
                NotifyPlacement("spawn");
                return;
            }
            int turns = rotation ?? 0;
            var error = DoorMarkerError(cell, turns);
            RoomFile.Check(error == null, error);
            var outward = MarkerOutward(cell, turns).Value;
            string id = FreshMarkerId(role);
            Commit(new AddDoorCommand(holders[ActiveId], new DoorInfo(id, cell, outward, role), BrushCategory == role ? BrushAppearance : null), id);
            NotifyPlacement(id);
        }

        void MoveDecoration(string id, Cell cell)
        {
            var old = FindDecoration(id);
            if (old.Cell == cell) return;
            Commit(new MoveDecorationCommand(Model, id, cell), id);
        }

        static RoomDraftModel CloneWith(RoomDraftModel src, IEnumerable<DoorInfo> doors, string primary)
        {
            var source = src.Room;
            var room = RoomResize.Resized(source, 0, 0, source.Width, source.Height);
            for (int x = 0; x < source.Width; x++)
                for (int y = 0; y < source.Height; y++)
                {
                    var look = source.AppearanceAt(new Cell(x, y));
                    if (look != null) room.SetAppearance(new Cell(x, y), look);
                }
            foreach (var placed in source.Objects)
                if (placed.Appearance != null) room.SetObjectAppearance(placed.Id, placed.Appearance);
            var list = doors.ToList();
            foreach (var door in list)
            {
                var terrain = room.TerrainAt(door.Cell);
                if (terrain.CategoryId != TerrainCategories.Door || terrain.EntityId != door.Id)
                    room.SetTerrain(door.Cell, TerrainCategories.Door, door.Id, terrain.State);
            }
            var next = new RoomDraftModel(room, list, primary, src.Extras, src.RecoveryItems);
            next.Meta = src.Meta;
            next.ReplaceRecoveryDevice(src.RecoveryDevice);
            next.Decorations.AddRange(src.Decorations);
            next.Assets.AddRange(src.Assets);
            next.Graphs.AddRange(src.Graphs);
            next.Instances.AddRange(src.Instances);
            foreach (var pair in src.Templates) next.Templates[pair.Key] = pair.Value;
            foreach (var pair in src.ObjectStates) next.ObjectStates[pair.Key] = pair.Value;
            next.UiImageAssetId = src.UiImageAssetId;
            return next;
        }

        sealed class AddDoorCommand : ICommand
        {
            readonly DraftHolder holder;
            readonly DoorInfo door;
            readonly AppearanceRef appearance;
            RoomDraftModel before;

            RoomDraftModel after;

            public AddDoorCommand(DraftHolder holder, DoorInfo door, AppearanceRef appearance)
            {
                this.holder = holder;
                this.door = door;
                this.appearance = appearance;
            }

            public void Apply()
            {
                before = holder.Draft;
                string primary = before.PrimaryEntranceId ?? (door.Role == "entry" ? door.Id : null);
                if (after == null)
                {
                    after = CloneWith(before, before.Doors.Append(door), primary);
                    if (appearance != null) after.Room.SetAppearance(door.Cell, appearance);
                }
                holder.Draft = after;
            }

            public void Revert() => holder.Draft = before;
        }

        sealed class MoveDecorationCommand : ICommand
        {
            readonly RoomDraftModel draft;
            readonly string id;
            readonly Cell cell;
            Decoration before;

            public MoveDecorationCommand(RoomDraftModel draft, string id, Cell cell) { this.draft = draft; this.id = id; this.cell = cell; }

            public void Apply()
            {
                int index = draft.Decorations.FindIndex(d => d.Id == id);
                if (index < 0) throw new InvalidOperationException("找不到装饰：" + id);
                before = draft.Decorations[index];
                draft.Decorations[index] = new Decoration(id, cell, before.Appearance, before.Rotation);
            }

            public void Revert()
            {
                int index = draft.Decorations.FindIndex(d => d.Id == id);
                if (index >= 0) draft.Decorations[index] = before;
            }
        }

        sealed class RemoveObjectCommand : ICommand
        {
            readonly RoomDraftModel draft;
            readonly string id;
            PlacedObject placed;
            Decoration decoration;
            int decorationIndex;
            List<BehaviorGraph> graphs = new List<BehaviorGraph>();

            public RemoveObjectCommand(RoomDraftModel draft, string id) { this.draft = draft; this.id = id; }

            public void Apply()
            {
                placed = draft.Room.Objects.FirstOrDefault(o => o.Id == id);
                decorationIndex = draft.Decorations.FindIndex(d => d.Id == id);
                if (placed == null && decorationIndex < 0) throw new InvalidOperationException("找不到对象：" + id);
                graphs = draft.Graphs.Where(g => g.OwnerId == id).ToList();
                draft.Graphs.RemoveAll(g => g.OwnerId == id);
                if (placed != null) draft.Room.RemoveObject(id);
                else { decoration = draft.Decorations[decorationIndex]; draft.Decorations.RemoveAt(decorationIndex); }
            }

            public void Revert()
            {
                if (placed != null) draft.Room.AddObject(placed.Id, placed.CategoryId, placed.Cell, placed.Appearance, placed.Rotation, placed.HintText);
                else draft.Decorations.Insert(decorationIndex, decoration);
                draft.Graphs.AddRange(graphs);
            }
        }

        sealed class RoomMetaCommand : ICommand
        {
            readonly RoomDraftModel draft;
            readonly RoomMeta after;
            RoomMeta before;

            public RoomMetaCommand(RoomDraftModel draft, RoomMeta after) { this.draft = draft; this.after = after; }

            public void Apply() { before = draft.Meta; draft.Meta = after; }

            public void Revert() => draft.Meta = before;
        }

        public void RemoveObject(string id)
        {
            RoomFile.Check((Find(id) != null && Find(id).CategoryId != ObjectCategories.Spawn) || FindDecoration(id) != null, "请选择箱子、目标或装饰");
            Commit(new RemoveObjectCommand(Model, id), null);
        }

        public void Rename(string name)
        {
            RoomFile.Check(!string.IsNullOrWhiteSpace(name), "房间名称不能为空");
            Commit(new RoomMetaCommand(Model, Model.Meta.WithIdentity(name, Model.Meta.ThemeId)), Selected);
        }

        public void SetPrimaryEntrance(string id)
        {
            var model = Model;
            var door = model.Doors.FirstOrDefault(candidate => candidate.Id == id && candidate.Role == "entry");
            RoomFile.Check(door != null, "请选择入口门");
            if (model.PrimaryEntranceId == id) return;
            var inward = door.Cell + new Cell(-door.Outward.X, -door.Outward.Y);
            RoomFile.Check(model.Room.TerrainAt(inward).CategoryId == TerrainCategories.Floor, "入口内侧须为地板");
            var occupant = model.Room.ObjectAt(inward);
            RoomFile.Check(occupant == null || occupant.CategoryId == ObjectCategories.Spawn, "入口内侧被对象占用 " + inward);
            string oldId = model.PrimaryEntranceId;
            Commit(new DelegateCommand(() => model.PrimaryEntranceId = id, () => model.PrimaryEntranceId = oldId));
            Select(door.Cell);
        }

        public void SetHintText(string id, string text)
        {
            var model = Model;
            var item = Find(id);
            RoomFile.Check(item != null && item.CategoryId == ObjectCategories.Hint, "请选择提示物件");
            Commit(new DelegateCommand(() => model.Room.SetHintText(id, text),
                () => model.Room.SetHintText(id, item.HintText)), id);
        }

        public void SetRoomText(string description, string entryHint, string playHint)
        {
            var meta = Model.Meta;
            Commit(new RoomMetaCommand(Model, new RoomMeta(meta.Name, meta.ThemeId, description, entryHint, playHint)), Selected);
        }

        public void Open(string path, bool import)
        {
            string text = File.ReadAllText(path);
            WorldDocument world = null;
            try { world = WorldCodec.Decode(text); }
            catch (FormatException) { }
            if (world != null)
            {
                var catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
                world = WorldLayout.Read(path, catalog);
                var first = world.Rooms.Count > 0 ? catalog[world.Rooms[0].LevelId] : DefaultModel();
                Reset(first, world, catalog, import ? null : Path.GetFullPath(path));
            }
            else Reset(RoomFile.Load(path), null, null, import ? null : Path.GetFullPath(path));
        }

        public void ExportRoom(string path)
        {
            var errors = RoomFile.Issues(Model, ValidationStage.Publish).Where(i => i.Severity == Severity.Error).ToList();
            RoomFile.Check(errors.Count == 0, errors.Count == 0 ? null : errors[0].Location + " " + errors[0].Message);
            var dangling = ObjectBehavior.TargetIssues(Model, id => Catalog.TryGetValue(id, out var found) ? found : null).ToList();
            RoomFile.Check(dangling.Count == 0, dangling.Count == 0 ? null : dangling[0]);
            RoomFile.Save(path, Model);
        }

        public void Save(string path, bool export)
        {
            if (World == null) ExportRoom(path);
            else
            {
                var dangling = Catalog.SelectMany(p => ObjectBehavior.TargetIssues(p.Value, id => Catalog.TryGetValue(id, out var found) ? found : null)).ToList();
                RoomFile.Check(dangling.Count == 0, dangling.Count == 0 ? null : dangling[0]);
                var written = WorldLayout.Write(path, World, Catalog);
                if (!export) World = written;
            }
            if (!export)
            {
                WorkPath = Path.GetFullPath(path);
                history.MarkSaved();
                unsaved = false;
                revision++;
            }
        }

    }
}
