/**
 * @file WorldLayout.cs
 * @brief Read 预读世界及相对引用；Write 先发布不可变房间内容，再原子替换世界清单。
 * Empty 建立有限世界；PlacementIssue 比较含边界的矩形；Issues 校验房间、连接与起终点。
 * ConnectionIssues 接受候选原点以便预演平移，不写草稿；Endpoint 查找指定门；RequireValid 在读写前检查整图。
 */
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public static class WorldLayout
    {
        public static JObject Empty(string first) => new JObject {
            ["kind"]="world", ["formatVersion"]=1, ["worldId"]=Guid.NewGuid().ToString("N"), ["contentVersion"]=1,
            ["bounds"]=new JObject { ["min"]=new JArray(0,0), ["max"]=new JArray(63,31) },
            ["rooms"]=new JArray(), ["connections"]=new JArray(),
            ["start"]=new JObject { ["levelId"]=first,["spawnId"]="spawn" },
            ["finish"]=new JObject { ["endpoint"]=new JObject { ["levelId"]=first,["doorId"]="exit" }, ["requiresSolved"]=new JArray(first) }
        };
        public static string PlacementIssue(JObject world,JObject rooms,string id,Vector2Int origin,bool moving)
        {
            return PlacementIssue(world, rooms, id, origin, moving, RoomFile.Size((JObject)rooms[id]));
        }
        public static string PlacementIssue(JObject world,JObject rooms,string id,Vector2Int origin,bool moving,Vector2Int size)
        {
            var min=RoomFile.Cell(world["bounds"]["min"]); var max=RoomFile.Cell(world["bounds"]["max"]);
            long right=(long)origin.x+size.x, top=(long)origin.y+size.y;
            if (origin.x < min.x || origin.y < min.y || right - 1 > max.x || top - 1 > max.y)
            {
                long outsideX = origin.x;
                long outsideY = origin.y;
                if (origin.x >= min.x && origin.y >= min.y)
                {
                    if (right - 1 > max.x) outsideX = Math.Max(outsideX, (long)max.x + 1);
                    else outsideY = Math.Max(outsideY, (long)max.y + 1);
                }
                return $"{id} 超出世界边界，冲突格 ({outsideX}, {outsideY})";
            }
            foreach(var other in world["rooms"])
            {
                string otherId=(string)other["levelId"];
                if(otherId==id) { if(moving) continue; return id+" 已放置"; }
                var pos=RoomFile.Cell(other["origin"]); var extent=RoomFile.Size((JObject)rooms[otherId]);
                if(origin.x<(long)pos.x+extent.x && right>pos.x && origin.y<(long)pos.y+extent.y && top>pos.y) return id+" 与 "+otherId+" 占地重叠 "+Vector2Int.Max(origin,pos);
            }
            return null;
        }
        static JToken Endpoint(JToken endpoint,JObject world,JObject rooms,out Vector2Int origin,string movingRoom = null,Vector2Int candidate = default)
        {
            string id=(string)endpoint?["levelId"];
            if (movingRoom != null && id == movingRoom)
            {
                origin = candidate;
                return rooms[id]?["doors"]?.FirstOrDefault(door => (string)door["id"] == (string)endpoint["doorId"]);
            }
            var placed=world["rooms"].FirstOrDefault(r=>(string)r["levelId"]==id);
            origin=placed==null?Vector2Int.zero:RoomFile.Cell(placed["origin"]);
            return placed==null?null:rooms[id]?["doors"]?.FirstOrDefault(d=>(string)d["id"]==(string)endpoint?["doorId"]);
        }
        public static IEnumerable<string> Issues(JObject world,JObject rooms)
        {
            foreach(var row in world["rooms"]) {
                string id=(string)row["levelId"];
                if (!(row["forwardRequiresSolved"] is JArray requirements) || requirements.Any(required => !world["rooms"].Any(r => (string)r["levelId"] == (string)required)))
                    yield return id + " 的前置房间引用无效";
                var error=PlacementIssue(world,rooms,id,RoomFile.Cell(row["origin"]),true);
                if(error!=null) yield return error;
                foreach(var issue in RoomFile.StructureIssues((JObject)rooms[id])) yield return issue;
            }
            foreach (var issue in ConnectionIssues(world,rooms)) yield return issue;
            var start = world["start"];
            string startRoom = (string)start?["levelId"];
            if (!world["rooms"].Any(r => (string)r["levelId"] == startRoom)
                || (string)rooms[startRoom]?["spawn"]?["id"] != (string)start?["spawnId"])
                yield return "世界起点无效 "+startRoom;
            if (!(world["finish"]?["requiresSolved"] is JArray finishRooms) || finishRooms.Any(id => !world["rooms"].Any(r => (string)r["levelId"] == (string)id)))
                yield return "世界终局的前置房间引用无效";
            if (Endpoint(world["finish"]?["endpoint"],world,rooms,out _) == null)
                yield return "世界终点门无效 "+world["finish"]?["endpoint"];
        }
        public static IEnumerable<string> ConnectionIssues(JObject world,JObject rooms,string movingRoom=null,Vector2Int candidate=default)
        {
            var ids=new HashSet<string>();
            var endpoints=new HashSet<(string,string)>();
            foreach(var link in world["connections"]) {
                string id=(string)link["id"];
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) yield return "连接 ID 缺失或重复 "+id;
                var from=Endpoint(link["forwardFrom"],world,rooms,out var a,movingRoom,candidate);
                var to=Endpoint(link["forwardTo"],world,rooms,out var b,movingRoom,candidate);
                foreach(var end in new[]{link["forwardFrom"],link["forwardTo"]}) {
                    var key=((string)end?["levelId"],(string)end?["doorId"]);
                    if(!endpoints.Add(key)) yield return $"{id} 端点重复使用 {key}";
                }
                string source=(string)link["forwardFrom"]?["levelId"], target=(string)link["forwardTo"]?["levelId"];
                string label=$"{id} {source}.{link["forwardFrom"]?["doorId"]} → {target}.{link["forwardTo"]?["doorId"]}";
                if(from==null || to==null) yield return label+" 端点缺失";
                else if((string)link["mode"]=="direct") {
                    if (RoomFile.Inward(from)!=-RoomFile.Inward(to) || a+RoomFile.At(from)-RoomFile.Inward(from)!=b+RoomFile.At(to))
                        yield return label+" 直连门不相邻或朝向不匹配";
                } else if((string)link["mode"]!="teleport") yield return label+" 连接方式无效";
            }
        }
        static void RequireValid(JObject world,JObject rooms)
        {
            string issue=Issues(world,rooms).FirstOrDefault();
            RoomFile.Check(issue==null,issue);
        }
        public static JObject Read(string path,JObject catalog)
        {
            var world=JObject.Parse(File.ReadAllText(path));
            RoomFile.Check((string)world["kind"]=="world" && (int?)world["formatVersion"]==1 && world["rooms"] is JArray && world["connections"] is JArray,"无法读取世界格式");
            var min=RoomFile.Cell(world["bounds"]["min"]); var max=RoomFile.Cell(world["bounds"]["max"]);
            RoomFile.Check(min.x<=max.x && min.y<=max.y,"世界边界无效");
            string root=Path.GetDirectoryName(Path.GetFullPath(path))+Path.DirectorySeparatorChar;
            foreach(var placement in world["rooms"]) {
                string id=(string)placement["levelId"], relative=(string)placement["file"];
                RoomFile.Check(!Path.IsPathRooted(relative) && !relative.Split('/','\\').Contains(".."),"需要世界目录内的相对引用");
                string roomPath=Path.Combine(root,relative);
                RoomFile.Check(File.Exists(roomPath),$"{id} 引用的房间文件缺失：{relative}");
                var room=RoomAssets.ReadRoom(roomPath);
                RoomFile.Check((string)room["levelId"]==id && catalog[id]==null,"房间 ID 不匹配或重复："+id);
                catalog[id]=room; RoomFile.Cell(placement["origin"]);
            }
            RequireValid(world,catalog);
            return world;
        }
        public static JObject Write(string path,JObject source,JObject catalog)
        {
            RequireValid(source,catalog);
            var world=(JObject)source.DeepClone(); var root=Path.GetDirectoryName(Path.GetFullPath(path));
            foreach(var row in world["rooms"]) {
                var room=catalog[(string)row["levelId"]]; var text=room.ToString();
                string relative=(string)row["file"];
                if(!string.IsNullOrEmpty(relative) && !Path.IsPathRooted(relative) && !relative.Split('/','\\').Contains("..")) {
                    var original=Path.Combine(root,relative);
                    if(File.Exists(original) && JToken.DeepEquals(JToken.Parse(File.ReadAllText(original)),room)) { RoomAssets.Publish((JObject)room,original); continue; }
                }
                using(var hash=SHA256.Create()) relative="rooms/"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant()+".json";
                string target=Path.Combine(root,relative); Directory.CreateDirectory(Path.GetDirectoryName(target));
                RoomAssets.Publish((JObject)room,target);
                RoomFile.Write(target,text); row["file"]=relative;
            }
            RoomFile.Write(path,world.ToString()); return world;
        }
    }
}
