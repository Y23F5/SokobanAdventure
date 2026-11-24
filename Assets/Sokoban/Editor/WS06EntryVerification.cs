/**
 * @file WS06EntryVerification.cs
 * @brief Run checks entry enumeration, unsupported graph diagnostics and rectangle selection. Rows validates unique report labels; Replay walks reported paths with PuzzleRun.Step. PartitionedRoom checks each named route across a solid partition.
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
            var bases = rows.Keys.Where(k => !k.Contains(" → ")).OrderBy(k=>k);
            if (!bases.SequenceEqual(labels.OrderBy(k=>k))) throw new InvalidOperationException("入口清单错误 " + label);
            return rows;
        }
        static void Replay(RoomDraft draft, Vector2Int start, string row, Vector2Int? destination = null)
        {
            if (!row.StartsWith("有解 ",StringComparison.Ordinal)) throw new InvalidOperationException(row);
            var run = PuzzleRun.FromDraft(draft);
            var state = run.Snapshot();
            state["player"] = RoomFile.Point(start);
            run.Restore(state);
            var route = row.Substring(3);
            int solvedAt = -1;
            var deltas = new[]{Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};
            for (int i = 0; i < route.Length; i++)
            {
                int index = "NESW".IndexOf(route[i]);
                if (index < 0 || !run.Step(deltas[index],out _)) throw new InvalidOperationException($"第 {i+1} 步运行拒绝 {route}");
                if (solvedAt < 0 && run.Solved) solvedAt = i+1;
            }
            if (!run.Solved || (destination.HasValue && (run.Player != destination.Value || solvedAt >= route.Length)))
                throw new InvalidOperationException("路线终局或出口不符 " + row);
            Debug.Log($"WS06ENT REPLAY start={start} path={route} solvedStep={solvedAt} endStep={route.Length} player={run.Player}");
        }
        static void PartitionedRoom()
        {
            var draft = new LayoutPanel().Draft;
            var north = new Vector2Int(3,6);
            var upperExit = new Vector2Int(5,6);
            var lowerExit = RoomFile.At(draft.Find("exit"));
            var west = RoomFile.At(draft.Find("entry"));
            var barrier = Enumerable.Range(0,7).Select(x=>new Vector2Int(x,4)).ToArray();
            if (draft.Brush(barrier,RoomTool.Wall)!=null || draft.Brush(new[]{north,upperExit},RoomTool.Floor)!=null) throw new InvalidOperationException("构造隔墙或开门失败");
            draft.SetRoomMarker(north,"entry");
            draft.SetRoomMarker(upperExit,"exit");
            RoomFile.RequireStructure(draft.Room);
            string northName="门 "+draft.OwnerAt(north), upperId=draft.OwnerAt(upperExit);
            var rows=Rows("隔墙双入口双出口",RoomSolution.CheckEntries(draft.Room,null),"出生点","门 entry",northName);
            foreach (string label in new[]{"出生点","门 entry",northName})
                foreach (string exit in new[]{"exit",upperId})
                {
                    var start=label==northName ? north : label=="门 entry" ? west : RoomFile.At(draft.Room["spawn"]);
                    var dest=exit=="exit" ? lowerExit : upperExit;
                    var result=RoomSolution.Check(draft.Room,start,destination:dest);
                    bool reachable=label!=northName && dest==lowerExit;
                    var expected=reachable ? RoomSolution.Outcome.Solved : RoomSolution.Outcome.Unsolvable;
                    var route=rows[label+" → "+exit];
                    if(result.Status!=expected || route!=(reachable?"有解 ":"确认无解 ")+result.Detail) throw new InvalidOperationException(label+" → "+exit+" "+route);
                    if(reachable) Replay(draft,start,route,dest);
                }
            Debug.Log("WS06ENT PASS 隔墙两侧入口结果不同；可推箱一侧仍须到指定出口，另一出口不可达");
        }
        [MenuItem("Sokoban/验证/WS-06 入口与框选")]
        public static void Run()
        {
            var draft=new LayoutPanel().Draft;
            PartitionedRoom();
            var north=new Vector2Int(3,6);
            if(draft.Brush(new[]{north},RoomTool.Floor)!=null) throw new InvalidOperationException("无法开门");
            draft.SetRoomMarker(north,"entry");
            string second=draft.OwnerAt(north);
            var rows=Rows("单房",RoomSolution.CheckEntries(draft.Room,null),"出生点","门 entry","门 "+second);
            Replay(draft,RoomFile.At(draft.Room["spawn"]),rows["出生点"]);
            foreach(var door in draft.Room["doors"].Where(d=>(string)d["role"]=="entry")) Replay(draft,RoomFile.At(door),rows["门 "+door["id"]]);
            Debug.Log("WS06ENT PASS 两入口与出生点的返回走法分别回放解开");
            var graph=ObjectBehavior.Template(); graph["nodes"].Single(n=>(string)n["kind"]=="condition")["expression"]="self.id ~= ''";
            draft.SetBehavior(new Vector2Int(3,0),graph);
            var owner=draft.OwnerAt(new Vector2Int(3,0));
            var unsupported=Rows("无法模拟",RoomSolution.CheckEntries(draft.Room,null),rows.Keys.Where(k => !k.Contains(" → ")).ToArray());
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
