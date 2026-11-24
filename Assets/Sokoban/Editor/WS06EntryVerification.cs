/**
 * @file WS06EntryVerification.cs
 * @brief Run checks entry enumeration, unsupported graph diagnostics and rectangle selection. Rows validates unique report labels; Replay independently walks each reported solo path from the specified cell.
 */
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace Sokoban.Editor
{
    public static class WS06EntryVerification
    {
        static Dictionary<string,string> Rows(string label, string report, params string[] labels)
        {
            Debug.Log("WS06ENT REPORT " + label + " | " + report.Replace("\n", " | "));
            var rows = report.Split('\n').Select(line => line.Split(new[]{" · "}, StringSplitOptions.None)).ToDictionary(parts => parts[0], parts => parts[1]);
            if (!rows.Keys.OrderBy(k=>k).SequenceEqual(labels.OrderBy(k=>k))) throw new InvalidOperationException("入口清单错误 " + label);
            return rows;
        }
        static void Replay(JObject room, Vector2Int start, string row)
        {
            if (!row.StartsWith("有解 ",StringComparison.Ordinal)) throw new InvalidOperationException(row);
            var player=start; var box=RoomFile.At(room["boxes"].Single());
            foreach (char direction in row.Substring(3))
            {
                var delta = new[]{Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left}["NESW".IndexOf(direction)];
                player+=delta;
                if (!RoomFile.Floor(room,player) || room["doors"].Any(d=>(string)d["role"]=="exit" && RoomFile.At(d)==player)) throw new InvalidOperationException("路径进入墙或锁闭出口");
                if (player==box) { box+=delta; if (!RoomFile.Floor(room,box)) throw new InvalidOperationException("箱子越界"); }
            }
            if (box!=RoomFile.At(room["targets"].Single())) throw new InvalidOperationException("走法未解开");
        }
        [MenuItem("Sokoban/验证/WS-06 入口与框选")]
        public static void Run()
        {
            var draft=new LayoutPanel().Draft;
            var north=new Vector2Int(3,6);
            if(draft.Brush(new[]{north},RoomTool.Floor)!=null) throw new InvalidOperationException("无法开门");
            draft.SetRoomMarker(north,"entry");
            string second=draft.OwnerAt(north);
            var rows=Rows("单房",RoomSolution.CheckEntries(draft.Room,null),"出生点","门 entry","门 "+second);
            Replay(draft.Room,RoomFile.At(draft.Room["spawn"]),rows["出生点"]);
            foreach(var door in draft.Room["doors"].Where(d=>(string)d["role"]=="entry")) Replay(draft.Room,RoomFile.At(door),rows["门 "+door["id"]]);
            Debug.Log("WS06ENT PASS 两入口与出生点的返回走法分别回放解开");
            var graph=ObjectBehavior.Template(); graph["nodes"].Single(n=>(string)n["kind"]=="condition")["expression"]="self.id ~= ''";
            draft.SetBehavior(new Vector2Int(3,0),graph);
            var owner=draft.OwnerAt(new Vector2Int(3,0));
            var unsupported=Rows("无法模拟",RoomSolution.CheckEntries(draft.Room,null),rows.Keys.ToArray());
            if(!unsupported.Values.All(v=>v.StartsWith("无法验证 ") && v.Contains(owner) && v.Contains((string)graph["id"]))) throw new InvalidOperationException("入口缺少对象定位");
            Debug.Log("WS06ENT PASS 每个入口报告无法验证并定位图和所有者");
            draft.Open("Docs/关卡样例/main.world.json", false); draft.UseRoom("factory");
            var incoming=draft.World["connections"].Single(l=>(string)l["forwardTo"]["levelId"]==draft.ActiveId);
            var outgoing=draft.World["connections"].Single(l=>(string)l["forwardFrom"]["levelId"]==draft.ActiveId);
            incoming["allowReturn"]=false; outgoing["allowReturn"]=false;
            var forward=Rows("世界单向",RoomSolution.CheckEntries(draft.Room,draft.World,0),"出生点","门 entry");
            outgoing["allowReturn"]=true;
            var both=Rows("世界回流",RoomSolution.CheckEntries(draft.Room,draft.World,0),"出生点","门 entry","门 exit");
            if(!forward.Values.Concat(both.Values).All(v=>v.StartsWith("搜索未完成 "))) throw new InvalidOperationException("零预算端点报告错误");
            Debug.Log("WS06ENT PASS 正向目标端点始终准入，源端点随 allowReturn 进入报告");
            draft.NewBlankRoom(); var points=new[]{Vector2Int.zero,Vector2Int.one,new Vector2Int(7,7)};
            draft.Brush(points,RoomTool.Floor); draft.Select(points[2]); draft.SelectRectangle(Vector2Int.one,new Vector2Int(-1,-1));
            if(!draft.Selection.SetEquals(points)) throw new InvalidOperationException("反向框选越界裁剪、跳过 Void 或追加选择失败");
            Debug.Log("WS06ENT PASS Rectangle 反向越界框选保留旧格并跳过空地");
        }
    }
}
