/**
 * @file SceneGhost.cs
 * @brief Draw 用实际箱体和目标的网格形状即时绘制半透明预览；Mark 标记原格和目标格；Dispose 释放预览材质。
 */
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class SceneGhost
    {
        readonly ObjectGhost model = new ObjectGhost();
        internal void Hide() => model.Hide();
        internal static void Mark(Vector2Int cell,Color color,string label)
        {
            using(new Handles.DrawingScope(color)) {
                Vector3 center=new Vector3(cell.x,.06f,cell.y);
                                var corner=new Vector3(.495f,0,.495f);var across=new Vector3(.495f,0,-.495f);
                Handles.DrawAAPolyLine(3,center+corner,center+across,center-corner,center-across,center+corner);
                if(!string.IsNullOrEmpty(label)) Handles.Label(center+Vector3.up*.82f,label,EditorStyles.whiteBoldLabel);
            }
        }
        internal void Draw(RoomDraft draft, Vector2Int cell,bool moving,string error,Vector2Int? source)
        {
            model.Show(draft,cell,moving,error);
            var tint=error==null ? Color.green : Color.red;
            var center=new Vector3(cell.x,0,cell.y);
            Mark(cell,tint,(source.HasValue?source+" → ":"")+cell+(error==null?"":"  "+error));
            if(source.HasValue) using(new Handles.DrawingScope(tint)) Handles.DrawDottedLine(new Vector3(source.Value.x,.1f,source.Value.y),center,4);
        }
        internal void Dispose() => model.Dispose();
    }
}
