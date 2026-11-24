/**
 * @file GridPlayVerification.cs
 * @brief Run 复核稀疏门与普通空格，打开运行编辑器进入 Play Mode。Tick 按帧驱动流程与退出码。
 * Flow 检查实际网格、虚影、试玩和正式局；Hover 通过编辑宿主绘制确定性射线命中的格子。
 */
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
namespace Sokoban.Editor
{
    public static class GridPlayVerification
    {
        const string Key = "Sokoban.QaGrid.Active";
        static IEnumerator flow;
        static int frame = -1;
        static void Check(bool ok,string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Debug.Log("GRIDQA PASS " + message);
        }
        static void SparseDoor()
        {
            var draft = new LayoutPanel().Draft;
            draft.Open("Docs/关卡样例/portal-check.world.json",false);
            draft.UseRoom("warehouse");
            var links = draft.World["connections"].DeepClone();
            Check(draft.Brush(new[]{new Vector2Int(10,0)}, RoomTool.Wall)==null,"扩建东侧包围盒");
            var outside = new Vector2Int(9,3);
            Check(RoomFile.Inside(draft.Room,outside) && RoomFile.Terrain(draft.Room,outside)==RoomFile.Void && JToken.DeepEquals(links,draft.World["connections"]),"出口前方为包围盒内部 Void，原传送端点保留");
            var run = PuzzleRun.FromDraft(draft);
            foreach (var step in new[]{Vector2Int.right,Vector2Int.right,Vector2Int.right,Vector2Int.up,Vector2Int.right,Vector2Int.right,Vector2Int.right})
                if (!run.Step(step,out _)) throw new InvalidOperationException("无法沿解谜路径抵达门格 " + run.Player);
            Check(run.Solved && run.Player==new Vector2Int(8,3),"实走推箱解开出口并到达 (8,3)");
            Check(run.Step(Vector2Int.right,out _) && run.ActiveId=="factory" && run.Player==run.Origin+RoomFile.At(run.Room["doors"].Single(d=>(string)d["id"]=="entry")),"向包围盒内 Void 迈步，传送至 factory 入口 " + run.Player);
            Check(run.Step(Vector2Int.left,out _) && run.ActiveId=="warehouse" && run.Player==new Vector2Int(8,3),"经同一连接反向返回");
            var ordinary = PuzzleRun.FromDraft(draft);
            // 合法发布房间会拒绝普通地板开口，单独在运行副本注入空格以覆盖移动规则。
            var p = ordinary.Player + Vector2Int.up - ordinary.Origin;
            var rows = ordinary.Room["terrainRowsNorthToSouth"];
            int index = RoomFile.Size(ordinary.Room).y-1-p.y;
            var chars=((string)rows[index]).ToCharArray(); chars[p.x]=RoomFile.Void; rows[index]=new string(chars);
            var before = ordinary.Snapshot();
            Check(!ordinary.Room["doors"].Any(d=>RoomFile.At(d)+ordinary.Origin==ordinary.Player) && !ordinary.Step(Vector2Int.up,out var pushed) && pushed==null && JToken.DeepEquals(before,ordinary.Snapshot()),"普通非门格向内部 Void 迈步被拒，运行快照逐字段一致");
        }
        public static void Run()
        {
            SparseDoor();
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetBool(Key,true);
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if (!SessionState.GetBool(Key,false) || !Application.isPlaying || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try
            {
                if(flow==null) flow=Flow();
                if(flow.MoveNext()) return;
                SessionState.EraseBool(Key); EditorApplication.Exit(0);
            }
            catch(Exception ex) { SessionState.EraseBool(Key); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        static int GridLines() => Object.FindObjectsOfType<LineRenderer>().Count(l=>l.enabled && l.name=="格线" && Mathf.Approximately(l.startWidth,.014f));
        static void Hover(LayoutHost host,RoomTool tool,Vector2Int cell,bool valid)
        {
            var ray=new Ray(new Vector3(cell.x+.42f,5,cell.y-.43f),Vector3.down);
            if(!RoomPreview.CellFromRay(ray,out var hit) || hit!=cell) throw new InvalidOperationException("射线吸附错误");
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            host.Panel.Draft.Tool=tool;
            typeof(LayoutHost).GetField("hoveredCell",flags).SetValue(host,hit);
            typeof(LayoutHost).GetField("hovering",flags).SetValue(host,true);
            typeof(LayoutHost).GetMethod("LateUpdate",flags).Invoke(host,null);
            var model=GameObject.Find("对象放置预览");
            if (!model) throw new InvalidOperationException("缺少虚影 " + tool);
            var tint=model.GetComponentInChildren<Renderer>().sharedMaterial.GetColor("_BaseColor");
            var color=valid?Color.green:Color.red; color.a=.43f;
            Check(model.transform.position==new Vector3(cell.x,.025f,cell.y) && tint==color && (host.HoverFeedback==null)==valid,tool+" 虚影位置 "+model.transform.position+" 颜色 "+tint+" 反馈 "+host.HoverFeedback);
        }
        static IEnumerator Flow()
        {
            yield return null; yield return null;
            var host=Object.FindObjectOfType<LayoutHost>();
            var panel=host.Panel; var draft=panel.Draft;
            draft.NewBlankRoom(); yield return null; yield return null;
            Check(host.GridVisible && GridLines()==32,"15×15 空房默认 32 条网格线");
            host.Interface.Act("网格"); yield return null; yield return null;
            Check(!host.GridVisible && GridLines()==0,"网格开关关闭实际线条");
            host.Interface.Act("网格"); yield return null; yield return null;
            Check(GridLines()==32,"重新开启网格恢复 32 条线");
            Hover(host,RoomTool.Floor,new Vector2Int(6,6),true);
            Hover(host,RoomTool.Box,new Vector2Int(6,6),false);
            draft.Open("Docs/关卡样例/WS-01.json",false);
            Hover(host,RoomTool.Box,new Vector2Int(2,4),true);
            Hover(host,RoomTool.Box,new Vector2Int(3,2),false);
            Hover(host,RoomTool.Wall,new Vector2Int(1,3),false);
            draft.Place(true,new Vector2Int(2,4)); draft.Tool=RoomTool.Select;
            var snapshot=draft.Snapshot(); var camera=Camera.main.transform.position;
            host.Interface.Act("试玩"); yield return null; yield return null;
            var game=Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.IsTrial && GridLines()==0 && !host.Interface.gameObject.activeInHierarchy,"试玩隐藏编辑网格和界面");
            Check(game.Run.Step(Vector2Int.up,out _),"试玩副本实际移动");
            game.Leave(); yield return null; yield return null;
            Check(host.Interface.gameObject.activeInHierarchy && GridLines()==16 && JToken.DeepEquals(snapshot,draft.Snapshot()) && Vector3.Distance(camera,Camera.main.transform.position)<.001f,"退出试玩恢复编辑地图、相机、选择、脏状态及历史");
            SceneManager.LoadScene("MainMenu"); yield return null; yield return null;
            Object.FindObjectOfType<MainMenuNavigation>().NewCampaign();
            for(int i=0;i<600 && !Object.FindObjectOfType<PuzzleHost>();i++) yield return null;
            yield return null; yield return null;
            game=Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.Run!=null && !game.IsTrial && GridLines()==0 && !Object.FindObjectOfType<LayoutHost>(),"主菜单新建正式局，无编辑网格");
        }
    }
}
