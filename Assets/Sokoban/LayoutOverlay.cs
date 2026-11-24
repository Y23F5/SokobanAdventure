/**
 * @file LayoutOverlay.cs
 * @brief Draw 按草稿绘制世界空间网格、悬停和选中格，并复用内置 Prefab 显示物件虚影；
 * Line 复用线段实例，Outline 绘制四边轮廓，Dispose 释放覆盖层和临时材质。
 */
using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    internal sealed class LayoutOverlay
    {
        readonly GameObject root = new GameObject("编辑标记");
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly Material material = new Material(Resources.Load<Material>("LayoutModels/GhostSurface"));
        readonly ObjectGhost ghost = new ObjectGhost();
        int used;

        void Line(Vector3 a,Vector3 b,Color tint,float width)
        {
            if (used==lines.Count)
            {
                var part=new GameObject("格线");
                part.transform.SetParent(root.transform,false);
                var renderer=part.AddComponent<LineRenderer>();
                renderer.sharedMaterial=material;
                renderer.positionCount=2;
                lines.Add(renderer);
            }
            var line=lines[used++];
            line.enabled=true;
            line.startWidth=line.endWidth=width;
            var props=new MaterialPropertyBlock();
            props.SetColor("_BaseColor",tint);
            line.SetPropertyBlock(props);
            line.SetPosition(0,a);line.SetPosition(1,b);
        }
        void Outline(Vector2Int cell,Color color)
        {
            var p=new Vector3(cell.x-.48f,.11f,cell.y-.48f);
            var x=Vector3.right*.96f;var z=Vector3.forward*.96f;
            Line(p,p+x,color,.045f);Line(p+x,p+x+z,color,.045f);
            Line(p+x+z,p+z,color,.045f);Line(p+z,p,color,.045f);
        }
        internal string Draw(RoomDraft draft,bool grid,bool hover,Vector2Int cell)
        {
            used=0;
            var origin=draft.Origin;
            var size=RoomFile.Size(draft.Room);
            if(grid && (draft.World==null || draft.Placement!=null))
            {
                var low=new Vector3(origin.x-.5f,.015f,origin.y-.5f);var color=new Color(.32f,.24f,.15f,.45f);
                for(int i=0;i<=size.x;i++) Line(low+Vector3.right*i,low+Vector3.right*i+Vector3.forward*size.y,color,.014f);
                for(int i=0;i<=size.y;i++) Line(low+Vector3.forward*i,low+Vector3.forward*i+Vector3.right*size.x,color,.014f);
            }
            if (draft.Selection.Count > 1) foreach (var point in draft.Selection) Outline(origin + point, Color.yellow);
            if(draft.SelectedCell.HasValue) Outline(draft.SelectedCell.Value+origin,new Color(1,.72f,.2f));
            foreach (var gap in RoomFile.BoundaryGaps(draft.Room)) Outline(origin + gap, Color.red);
            string error=null;
            bool visible=false, moving=draft.Tool==RoomTool.Move;
            if(hover)
            {
                if (draft.Tool == RoomTool.Floor || draft.Tool == RoomTool.Wall)
                {
                    error = draft.TerrainPlacementError(cell - origin, draft.Tool);
                    visible = true;
                    Outline(cell, error == null ? Color.green : Color.red);
                }
                else if(moving || draft.Tool==RoomTool.Box || draft.Tool==RoomTool.Target || draft.Tool==RoomTool.Decoration)
                {
                    var selected=draft.Find(draft.Selected);
                    bool box=draft.Tool==RoomTool.Box || moving && selected?.Parent==draft.Room["boxes"];
                    error=moving && selected==null?"请选择箱子或目标":draft.PlacementError(box,cell-origin,moving?draft.Selected:null);
                    if(draft.World!=null && draft.Placement==null)error="房间尚未放入世界";
                    visible = !moving || selected != null;
                    Outline(cell,error==null?Color.green:Color.red);
                }
                else if(RoomFile.Inside(draft.Room,cell-origin))Outline(cell,new Color(1,.94f,.76f));
            }
            for(int i=used;i<lines.Count;i++)lines[i].enabled=false;
            if(visible) ghost.Show(draft,cell,moving,error); else ghost.Hide();
            return error;
        }

        internal void Dispose()
        {
            Object.Destroy(root);Object.Destroy(material);
            ghost.Dispose();
        }
    }
}
