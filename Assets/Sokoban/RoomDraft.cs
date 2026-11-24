/**
 * @file RoomDraft.cs
 * @brief 两端共用单房编辑会话。RoomDraft 解析初始数据；Open 预读候选文件再替换现场，Save 区分工作文件与交换副本；
 * Find/Select 按稳定 ID 或格定位，Click 将当前工具翻译为编辑命令，Place/Move 校验并提交，
 * PlacementError 返回越界、占用或门预留原因，Commit 记修改前后选择与数据，Undo/Redo 经 Restore 在历史栈之间移动快照。
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
        public RoomTool Tool;
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
            RoomFile.Write(path,Room.ToString());
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
        public void Select(Vector2Int cell)
        {
            var item = RoomFile.Objects(Room).FirstOrDefault(o => RoomFile.At(o) == cell);
            SelectedCell = RoomFile.Inside(Room, cell) ? cell : (Vector2Int?)null;
            Selected = (string)item?["id"];
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
            if (!RoomFile.Inside(Room,cell)) return "超出房间边界";
            if (RoomFile.Terrain(Room,cell) == RoomFile.Void) return "请先铺设地板";
            if (!RoomFile.Floor(Room,cell)) return "墙上无法放置";
            var doors = Room["doors"];
            var primary = doors.First(d => (string)d["id"] == (string)Room["primaryEntranceId"]);
            if (doors.Any(d => RoomFile.At(d) == cell) || RoomFile.At(primary) + RoomFile.Inward(primary) == cell) return "门格或主入口预留格";
            if (RoomFile.Objects(Room).Any(o => (string)o["id"] != ignore && RoomFile.At(o) == cell)) return "格子已占用";
            return null;
        }
        public void Place(bool box, Vector2Int cell)
        {
            var error = PlacementError(box, cell); RoomFile.Check(error == null, error);
            string id = (box ? "box-" : "target-") + Guid.NewGuid().ToString("N");
            var next = (JObject)Room.DeepClone();
            var obj = new JObject { ["id"] = id, ["cell"] = RoomFile.Point(cell) };
            if (box) obj["recoveryCell"] = RoomFile.Point(cell);
            ((JArray)next[box ? "boxes" : "targets"]).Add(obj);
            Commit(next, id);
            NotifyPlacement(id);
        }
        public void Move(Vector2Int cell)
        {
            var item = Find(Selected);
            bool box = item != null && item.Parent == Room["boxes"];
            bool decor = item != null && item.Parent == Room["decorations"];
            RoomFile.Check(item != null && (box || decor || item.Parent == Room["targets"]), "请选择箱子或目标");
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
            Remember(); Room=next; Catalog[ActiveId]=next; Selected=id;
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
