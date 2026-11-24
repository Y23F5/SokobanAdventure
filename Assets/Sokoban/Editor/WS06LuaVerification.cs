/**
 * @file WS06LuaVerification.cs
 * @brief Run drives graph conditions, actions, expression typing, bounded execution and solver classification. Reject records the expected diagnostic.
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Sokoban.Editor
{
    public static class WS06LuaVerification
    {
        static void Check(bool ok,string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("WS06LUA PASS " + detail);
        }
        static void Reject(Action action,string message)
        {
            try { action(); }
            catch (InvalidOperationException ex) when(ex.Message.Contains(message)) { Check(true,ex.Message); return; }
            throw new InvalidOperationException("缺少预期拒绝 " + message);
        }
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var cell = new Vector2Int(3,0);
            var template = ObjectBehavior.Template();
            draft.SetBehavior(cell, template);
            var owner = draft.OwnerAt(cell);
            var condition = template["nodes"].Single(n=>(string)n["kind"]=="condition");
            var action = template["nodes"].Single(n=>(string)n["kind"]=="setVisible");
            condition["expression"] = "self.id == '" + owner + "'";
            action["kind"]="lua";
            ((JObject)action).Remove("visible");
            action["script"]="self.setVisible(false); self.setVisible(true)";
            draft.SetBehavior(cell,template);
            var graph=draft.Room["events"].Single();
            var effects=ObjectBehavior.Effects(graph).ToArray();
            Check(effects.Length==2 && effects.All(e=>(string)e["kind"]=="setVisible" && e["visible"].Type==JTokenType.Boolean) && !(bool)effects[0]["visible"] && (bool)effects[1]["visible"],"matching self.id runs self.setVisible(false), then true");
            var other=(JObject)graph.DeepClone(); other["ownerId"]="different-owner";
            Check(!ObjectBehavior.Effects(other).Any(),"different self.id skips action");
            Reject(()=>BehaviorLua.Evaluate("self.id",owner,true,null),"布尔");
            BehaviorLua.Evaluate("local n=0; for i=1,5 do n=n+i end",owner,false,null);
            Reject(()=>BehaviorLua.Evaluate("local n=0; for i=1,100000 do n=n+i end",owner,false,null),"预算");
            Check(BehaviorLua.Evaluate("self.id ~= ''",owner,true,null),"boolean condition succeeds after budget rejection");
            var start=RoomFile.At(draft.Room["spawn"]);
            void Unsupported(string label)
            {
                var result=RoomSolution.Check(draft.Room,start);
                Check(result.Status==RoomSolution.Outcome.Unsupported && result.Expanded==0 && result.Detail.Contains(owner) && result.Detail.Contains((string)template["id"]),label+" "+result.Status+" expanded="+result.Expanded+" "+result.Detail);
            }
            ((JObject)condition).Remove("expression"); draft.SetBehavior(cell,template); Unsupported("Lua action alone");
            action["kind"]="setVisible"; ((JObject)action).Remove("script"); action["visible"]=false;
            condition["expression"]="true"; draft.SetBehavior(cell,template); Unsupported("Lua expression alone");
        }
    }
}
