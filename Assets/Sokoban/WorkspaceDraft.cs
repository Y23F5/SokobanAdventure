/**
 * @file WorkspaceDraft.cs
 * @brief Reset/NewRoom 初始化工作区。Design/State/Apply、Snapshot/Recover 保存与还原设计、选择及双向历史。
 * Remember 在命令提交前记录，UseRoom/RoomAt 切换房间。BeginWorld、Attach、PlaceRoom 和 RemoveRoom 管理房间清单。
 * Brush 提交整笔地形或对象擦除；Rename 修改名称。Origin 将世界格转换到当前房间。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        JObject removedPlacements=new JObject();
        public JObject World {get;private set;}
        public JObject Catalog {get;private set;}
        public string ActiveId => (string)Room["levelId"];
        public JToken Placement => World?["rooms"].FirstOrDefault(r=>(string)r["levelId"]==ActiveId);
        public Vector2Int Origin => Placement==null?Vector2Int.zero:RoomFile.Cell(Placement["origin"]);
        void Reset(JObject room,JObject world,JObject catalog,string path)
        {
            removedPlacements=new JObject(); Room=room; World=world; Catalog=catalog??new JObject(); Catalog[ActiveId]=room;
            Selected=null; SelectedCell=null; Tool=RoomTool.Select; BrushAsset=null; WorkPath=path; undo.Clear(); redo.Clear(); saved=Design(); Revision++;
        }
        public void NewRoom(string name="新房间") {
            var room=RoomFile.Parse(Resources.Load<TextAsset>("DefaultRoom").text);
            room["levelId"]="room-"+Guid.NewGuid().ToString("N"); room["name"]=name;
            room["boxes"]=new JArray(); room["targets"]=new JArray(); Reset(room,null,null,null); saved=null;
        }
        JObject Design() => new JObject { ["world"]=World?.DeepClone(),["rooms"]=Catalog.DeepClone() };
        JObject State() => new JObject { ["data"]=Design(),["removed"]=removedPlacements.DeepClone(),["active"]=ActiveId,["selected"]=Selected,["cell"]=SelectedCell.HasValue?RoomFile.Point(SelectedCell.Value):null };
        void Remember() { undo.Push(State()); redo.Clear(); }
        void Apply(JObject state) {
            removedPlacements=state["removed"] is JObject removed?(JObject)removed.DeepClone():new JObject();var data=state["data"]; Catalog=(JObject)data["rooms"].DeepClone(); World=data["world"] as JObject;
            Room=(JObject)Catalog[(string)state["active"]]; RoomFile.Check(Room!=null,"草稿缺少选中房间");
            if (BrushAsset!=null && Room["assets"]?[BrushAsset]==null) { BrushAsset=null; if(Tool==RoomTool.Decoration) Tool=RoomTool.Select; }
            Selected=(string)state["selected"]; SelectedCell=state["cell"] is JArray?RoomFile.Cell(state["cell"]):(Vector2Int?)null;
        }
        public JObject Snapshot() => new JObject { ["version"]=1,["current"]=State(),["path"]=WorkPath,["saved"]=saved?.DeepClone(),["tool"]=(int)Tool,["brushAsset"]=BrushAsset,["undo"]=new JArray(undo.Reverse()),["redo"]=new JArray(redo.Reverse()) };
        public void Recover(JObject snapshot) {
            RoomFile.Check((int?)snapshot["version"]==1,"草稿版本无效");
            foreach(var state in snapshot["undo"].Concat(snapshot["redo"]).Append(snapshot["current"])) {
                var rooms=(JObject)state["data"]["rooms"];foreach(var prop in rooms.Properties()) RoomFile.Parse(prop.Value.ToString(), draft: true);
                RoomFile.Check(rooms[(string)state["active"]]!=null,"草稿房间引用缺失");
            }
            Apply((JObject)snapshot["current"]); saved=snapshot["saved"] as JObject; WorkPath=(string)snapshot["path"];BrushAsset=(string)snapshot["brushAsset"];Tool=(RoomTool)(int)snapshot["tool"];
            undo.Clear(); redo.Clear();foreach(JObject value in snapshot["undo"]) undo.Push(value);foreach(JObject value in snapshot["redo"]) redo.Push(value);Revision++;
        }
        public void BeginWorld() {
            RoomFile.Check(World==null,"已在世界工作区"); World=WorldLayout.Empty(ActiveId); World["start"]["spawnId"]=Room["spawn"]["id"];
            World["finish"]["endpoint"]["doorId"]=Room["doors"].Last()["id"];WorkPath=null;saved=null;undo.Clear();redo.Clear();Revision++;
        }
        public void Attach(string path) {
            var candidate=RoomAssets.ReadRoom(path);string id=(string)candidate["levelId"];
            RoomFile.Check(World!=null && Catalog[id]==null,"房间已在目录中或尚未创建世界");Remember();Catalog[id]=candidate;UseRoom(id);
        }
        public void UseRoom(string id) { Room=(JObject)Catalog[id];Selected=null;SelectedCell=null;Tool=RoomTool.Select;BrushAsset=null;Revision++; }
        public string RoomAt(Vector2Int point) {
            if(World==null) return ActiveId;
            foreach(var row in World["rooms"]) {string id=(string)row["levelId"];if(RoomFile.Inside((JObject)Catalog[id],point-RoomFile.Cell(row["origin"]))) return id;}
            return null;
        }
        public void PlaceRoom(Vector2Int origin,bool move) {
            RoomFile.Check(World!=null && (!move || Placement!=null),"请先放置房间");
            var error=WorldLayout.PlacementIssue(World,Catalog,ActiveId,origin,move);RoomFile.Check(error==null,error);
            Remember(); if(move) Placement["origin"]=RoomFile.Point(origin);
                        else {
                var row=removedPlacements[ActiveId] as JObject ?? new JObject { ["levelId"]=ActiveId,["file"]="",["forwardRequiresSolved"]=new JArray() };
                row["origin"]=RoomFile.Point(origin); ((JArray)World["rooms"]).Add(row); removedPlacements.Remove(ActiveId);
            }
            Revision++;
        }
        public void SetConnectionMode(string id, string mode)
        {
            RoomFile.Check(mode == "teleport" || mode == "direct", "未知连接方式");
            var link = World?["connections"]?.SingleOrDefault(row => (string)row["id"] == id);
            RoomFile.Check(link != null, "未找到连接 " + id);
            if ((string)link["mode"] == mode) return;
            Remember();
            link["mode"] = mode;
            Revision++;
        }
        public void RemoveRoom() {RoomFile.Check(Placement!=null,"房间尚未放置");Remember();removedPlacements[ActiveId]=Placement.DeepClone();Placement.Remove();Revision++;}
        public void Rename(string name) { RoomFile.Check(!string.IsNullOrWhiteSpace(name),"房间名称不能为空"); var next=(JObject)Room.DeepClone();next["name"]=name;Commit(next,Selected); }
        public string Brush(IEnumerable<Vector2Int> cells,RoomTool tool) {
            var points = cells.Distinct().ToArray();
            var size = RoomFile.Size(Room);
            var min = Vector2Int.zero;
            var max = size - Vector2Int.one;
            if (tool == RoomTool.Floor || tool == RoomTool.Wall)
                foreach (var point in points)
                {
                    min = Vector2Int.Min(min, point);
                    max = Vector2Int.Max(max, point);
                }
            var next = (JObject)Room.DeepClone();
            var origin = Origin + min;
            var placement = Placement;
            if (min != Vector2Int.zero || max != size - Vector2Int.one)
            {
                var extent = max - min + Vector2Int.one;
                next["size"] = RoomFile.Point(extent);
                if (placement != null)
                {
                    var catalog = (JObject)Catalog.DeepClone();
                    catalog[ActiveId] = next;
                    string conflict = WorldLayout.PlacementIssue(World, catalog, ActiveId, origin, true);
                    if (conflict != null) return conflict;
                }
                var rows = new JArray();
                for (int y = max.y; y >= min.y; y--)
                {
                    var row = new char[extent.x];
                    for (int x = 0; x < extent.x; x++) row[x] = RoomFile.Terrain(Room, new Vector2Int(min.x + x, y));
                    rows.Add(new string(row));
                }
                next["terrainRowsNorthToSouth"] = rows;
                foreach (var entity in RoomFile.Objects(next).Concat(next["terrainObjects"] as JArray ?? new JArray()))
                {
                    entity["cell"] = RoomFile.Point(RoomFile.At(entity) - min);
                    if (entity["recoveryCell"] != null) entity["recoveryCell"] = RoomFile.Point(RoomFile.Cell(entity["recoveryCell"]) - min);
                }
                size = extent;
            }
            string issue = null;
            foreach (var point in points) {
                var cell = point - min;
                if(!RoomFile.Inside(next,cell)) {issue="越界 "+cell;continue;}
                var obj=RoomFile.Objects(next).FirstOrDefault(item=>RoomFile.At(item)==cell);
                if(tool==RoomTool.Erase && obj!=null && (obj.Parent==next["boxes"] || obj.Parent==next["targets"] || obj.Parent==next["decorations"])) {obj.Remove();continue;}
                if((tool==RoomTool.Wall || tool==RoomTool.Erase) && (obj!=null || next["doors"].Any(door=>RoomFile.At(door)+RoomFile.Inward(door)==cell) || next["boxes"].Any(box=>RoomFile.Cell(box["recoveryCell"])==cell))) {issue="对象或门内侧占用 "+cell;continue;}
                int index = size.y - 1 - cell.y;
                var row = ((string)next["terrainRowsNorthToSouth"][index]).ToCharArray();
                char tile = tool == RoomTool.Wall ? '#' : tool == RoomTool.Erase ? RoomFile.Void : '.';
                if (row[cell.x] == tile) continue;
                var identity = next["terrainObjects"]?.FirstOrDefault(item => RoomFile.At(item) == cell);
                identity?.Remove();
                row[cell.x] = tile;
                next["terrainRowsNorthToSouth"][index] = new string(row);
            }
            if (!JToken.DeepEquals(Room, next))
            {
                Commit(next, null);
                if (placement != null) placement["origin"] = RoomFile.Point(origin);
            }
            return issue;
        }
    }
}
