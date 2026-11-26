/**
 * @file RoomDraft.cs
 * @brief 两端共用单房编辑会话。RoomDraft 解析初始数据；Open 预读候选文件再替换现场，Save 区分工作文件与交换副本；
 * Find/Select 按稳定 ID 或格定位，Click 将当前工具翻译为编辑命令，Place/Move 校验并提交，
 * PlacementError 经 Core 的 PlacementRules 判定越界、墙、门预留与对象占用，再补判 Core 没有的装饰物占格；LegacyPlacementError 保留旧逐项判定供比对，旧房间数据无法转换时也回退到它；Commit 记修改前后选择与数据，Undo/Redo 经 Restore 在历史栈之间移动快照。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public enum RoomTool { Select, Box, Target, Move, Floor, Wall, Erase, Decoration }
    public sealed partial class RoomDraft
    {
        public JObject Room { get; private set; }
        public string Selected { get; private set; }
        public string WorkPath { get; private set; }
        public Vector2Int? SelectedCell { get; private set; }
        public readonly HashSet<Vector2Int> Selection = new HashSet<Vector2Int>();
        RoomTool tool;
        public RoomTool Tool
        {
            get => tool;
            set { tool = value; BrushAsset = null; BrushDefinition = null; BrushBuiltin = null; }
        }
        public int Revision { get; private set; }
        JObject saved;
        readonly Stack<JObject> undo = new Stack<JObject>();
        readonly Stack<JObject> redo = new Stack<JObject>();
        public bool Dirty => !JToken.DeepEquals(saved,Design());
        public RoomDraft(string json) { Reset(RoomFile.Parse(json),null,null,null); }
        public void Open(string path, bool import)
        {
            var text=File.ReadAllText(path);
            var catalog=new JObject();
            JObject world=null,room;
            if ((string)JObject.Parse(text)["kind"]=="world") {
                world=WorldLayout.Read(path,catalog);
                room=(JObject)catalog.Properties().FirstOrDefault()?.Value ?? RoomFile.Parse(Resources.Load<TextAsset>("DefaultRoom").text);
            } else { room=RoomFile.Parse(text); RoomAssets.Receive(room,path); }
            Reset(room,world,catalog,import?null:Path.GetFullPath(path));
        }
        public void ExportRoom(string path)
        {
            RoomFile.RequireStructure(Room);
            RoomAssets.Publish(Room,path);
            RoomFile.Write(path,RoomFile.ForExport(Room).ToString());
        }
        public void Save(string path,bool export)
        {
            if(World==null) {
                ExportRoom(path);
            }
            else {
                var written=WorldLayout.Write(path,World,Catalog);
                if(!export) World=written;
            }
            if(!export) { WorkPath=Path.GetFullPath(path); saved=Design(); Revision++; }
        }
        public JToken Find(string id) => RoomFile.Objects(Room).FirstOrDefault(o => (string)o["id"] == id);
        public void Select(Vector2Int cell) => Select(cell, false);
        public void Select(Vector2Int cell, bool additive)
        {
            if (!additive) Selection.Clear();
            if (RoomFile.Terrain(Room, cell) != RoomFile.Void && !Selection.Add(cell) && additive)
            {
                Selection.Remove(cell);
                if (Selection.Count == 0) { Selected = null; SelectedCell = null; Revision++; return; }
                cell = Selection.OrderBy(p => p.y).ThenBy(p => p.x).First();
            }
            var item = RoomFile.Objects(Room).FirstOrDefault(o => RoomFile.At(o) == cell);
            SelectedCell = RoomFile.Inside(Room, cell) ? cell : (Vector2Int?)null;
            Selected = (string)item?["id"];
            Revision++;
        }
        public void SelectRectangle(Vector2Int start, Vector2Int finish)
        {
            var low = Vector2Int.Max(Vector2Int.Min(start, finish), Vector2Int.zero);
            var high = Vector2Int.Min(Vector2Int.Max(start, finish), RoomFile.Size(Room) - Vector2Int.one);
            for (int y=low.y; y<=high.y; y++)
                for (int x=low.x; x<=high.x; x++)
                {
                    var point = new Vector2Int(x,y);
                    if (RoomFile.Terrain(Room, point) == RoomFile.Void) continue;
                    Selection.Add(point);
                    SelectedCell = point;
                }
            if (SelectedCell.HasValue) Selected = (string)RoomFile.Objects(Room).FirstOrDefault(item => RoomFile.At(item) == SelectedCell.Value)?["id"];
            Revision++;
        }
        public void Click(Vector2Int cell)
        {
            switch (Tool)
            {
                case RoomTool.Box: Place(true, cell); break;
                case RoomTool.Target: Place(false, cell); break;
                case RoomTool.Move: Move(cell); break;
                case RoomTool.Decoration: PlaceAsset(cell); break;
                default: Select(cell); break;
            }
        }
        public string PlacementError(bool box, Vector2Int cell, string ignore = null)
        {
            string error;
            try
            {
                var core = Sokoban.Core.LegacyRoomAdapter.ToDraft(RoomFile.ToLegacyData(Room));
                error = Sokoban.Core.PlacementRules.Error(core, box ? Sokoban.Core.ObjectCategories.Box : Sokoban.Core.ObjectCategories.Target,
                    new Sokoban.Core.Cell(cell.x, cell.y), ignore);
            }
            catch (FormatException) { return LegacyPlacementError(box, cell, ignore); }
            if (error != null) return error;
            var decorations = Room["decorations"] as JArray;
            if (decorations != null && decorations.Any(o => (string)o["id"] != ignore && RoomFile.At(o) == cell)) return "格子已占用";
            return null;
        }
        public string LegacyPlacementError(bool box, Vector2Int cell, string ignore = null)
        {
            if (!RoomFile.Inside(Room,cell)) return "超出房间边界";
            if (RoomFile.Terrain(Room,cell) == RoomFile.Void) return "请先铺设地板";
            if (!RoomFile.Floor(Room,cell)) return "墙上无法放置";
            var doors = Room["doors"];
            var primary = doors.FirstOrDefault(d => (string)d["id"] == (string)Room["primaryEntranceId"]);
            if (doors.Any(d => RoomFile.At(d) == cell) || primary != null && RoomFile.At(primary) + RoomFile.Inward(primary) == cell) return "门格或主入口预留格";
            if (RoomFile.Objects(Room).Any(o => (string)o["id"] != ignore && RoomFile.At(o) == cell)) return "格子已占用";
            return null;
        }
        public void SetRoomMarker(Vector2Int cell, string role)
        {
            RoomFile.Check(RoomFile.Floor(Room, cell), "请在地板格上设置出生点或门");
            RoomFile.Check(role == "spawn" || role == "entry" || role == "exit", "未知房间标记");
            bool spawn = role == "spawn";
            var previous = spawn ? Room["spawn"] : null;
            if (previous != null && RoomFile.At(previous) == cell) return;
            RoomFile.Check(!RoomFile.Objects(Room).Any(item => item != previous && RoomFile.At(item) == cell), "格子已占用");
            var next = (JObject)Room.DeepClone();
            var marker = new JObject { ["cell"] = RoomFile.Point(cell) };
            if (spawn)
            {
                marker["id"] = (string)previous?["id"] ?? FreshMarkerId("spawn");
                next["spawn"] = marker;
            }
            else
            {
                string direction = new[] { "N", "E", "S", "W" }.FirstOrDefault(side =>
                {
                    var inward = RoomFile.Inward(new JObject { ["direction"] = side });
                    return RoomFile.Terrain(Room, cell - inward) == RoomFile.Void && RoomFile.Floor(Room, cell + inward);
                });
                RoomFile.Check(direction != null, "门需要朝向空地，且内侧有可通行地板");
                marker["id"] = FreshMarkerId(role);
                marker["role"] = role;
                marker["direction"] = direction;
                ((JArray)next["doors"]).Add(marker);
                if (role == "entry" && next["primaryEntranceId"] == null) next["primaryEntranceId"] = (string)marker["id"];
            }
            Commit(next, (string)marker["id"]);
        }
        string FreshMarkerId(string role) => Find(role) == null ? role : role + "-" + Guid.NewGuid().ToString("N");
        public void Place(bool box, Vector2Int cell) => Place(box, cell, (box ? "box-" : "target-") + Guid.NewGuid().ToString("N"));
        public void Place(bool box, Vector2Int cell, string id)
        {
            var error = PlacementError(box, cell); RoomFile.Check(error == null, error);
            RoomFile.Check(!string.IsNullOrWhiteSpace(id) && Find(id) == null, "对象 ID 为空或已占用");
            var next = (JObject)Room.DeepClone();
            var obj = new JObject { ["id"] = id, ["cell"] = RoomFile.Point(cell) };
            if (box) obj["recoveryCell"] = RoomFile.Point(cell);
            ApplyBrush(next, obj, box ? RoomTool.Box : RoomTool.Target, cell);
            ((JArray)next[box ? "boxes" : "targets"]).Add(obj);
            Commit(next, id);
            NotifyPlacement(id);
        }
        public void Move(Vector2Int cell)
        {
            var item = Find(Selected);
            bool box = item != null && item.Parent == Room["boxes"];
            bool decor = item != null && item.Parent == Room["decorations"];
            RoomFile.Check(item != null && (box || decor || item.Parent == Room["targets"]), "请选择箱子、目标或装饰");
            if (RoomFile.At(item) == cell) return;
            var error = PlacementError(box, cell, Selected); RoomFile.Check(error == null, error);
            var next = (JObject)Room.DeepClone();
            var obj = next[box ? "boxes" : decor ? "decorations" : "targets"].First(o => (string)o["id"] == Selected);
            obj["cell"] = RoomFile.Point(cell);
            if (box) obj["recoveryCell"] = RoomFile.Point(cell);
            Commit(next, Selected);
        }
        void Commit(JObject next, string id)
        {
            Remember(); Selection.Clear(); Room=next; Catalog[ActiveId]=next; Selected=id;
            SelectedCell=id==null?null:(Vector2Int?)RoomFile.At(Find(id)); Revision++;
        }
        public void Undo() => Restore(undo,redo);
        public void Redo() => Restore(redo,undo);
        void Restore(Stack<JObject> from,Stack<JObject> to)
        {
            RoomFile.Check(from.Count>0,"没有可执行的历史操作");
            to.Push(State()); Apply(from.Pop()); Revision++;
        }
    }
}
