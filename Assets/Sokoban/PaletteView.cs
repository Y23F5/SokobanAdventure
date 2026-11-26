/**
 * @file PaletteView.cs
 * @brief 运行端像素风素材栏与地图覆盖层，页签为地形、物件、装饰、其他、自定义，候选项按类别 ID 由 RoomDraft.PaletteItems 给出。
 * Fill 绘制纯色矩形；Frame 叠绘像素边缘；Text 设定字色及对齐；Button 绘制可点击按钮；
 * Card(item)：为一个素材候选项绘制名称、悬停与选中态，点击后按类别选择画刷：内置外观与本房模型走 ChooseAppearance 或 SelectAsset，对象模板走 SelectDefinition。
 * Sidebar：页签条加候选列表加所选内容信息，其他页本轮为空；Project 投影格子顶点；Outline 标出悬停与选中格；Line 将像素线段对齐两个端点；
 * MapOverlay 绘制网格、半透明体积预览及合法性轮廓，格坐标一律用 Core.Cell；Face 用纯色纹理绘制半透明四边形。
 */
using System;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public sealed class PaletteView
    {
        static readonly Color Ink = new Color32(100,71,61,255), Border = new Color32(156,111,75,255), Paper = new Color32(255,232,185,255), Gold = new Color32(248,193,92,255);
        Vector2 scroll;
        string tab = PaletteTabs.Object;
        public static void Fill(Rect rect,Color color)
        { var old=GUI.color; GUI.color=color; GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color=old; }
        public static void Frame(Rect r, bool selected=false)
        {
            Fill(new Rect(r.x+2,r.y+4,r.width-4,r.height-1),Ink);
            Fill(new Rect(r.x+2,r.y,r.width-4,r.height),selected?Gold:Border);
            Fill(new Rect(r.x,r.y+3,r.width,r.height-6),selected?Gold:Border);
            Fill(new Rect(r.x+3,r.y+3,r.width-6,r.height-7),selected?new Color32(255,216,146,255):Paper);
        }
        public static void Text(Rect r,string value,int size=14,TextAnchor align=TextAnchor.MiddleLeft)
        { var s=new GUIStyle(GUI.skin.label){fixedHeight=0,fontSize=size,alignment=align,wordWrap=true,padding=new RectOffset(),contentOffset=new Vector2(0,-2)}; s.normal.textColor=Ink; GUI.Label(r,value,s); }
        public static bool Button(Rect r,string value,bool selected=false)
        { Frame(r,selected); Text(r,value,15,TextAnchor.MiddleCenter); return GUI.Button(r,GUIContent.none,GUIStyle.none); }

        static bool IsChosen(RoomDraft draft, PaletteItem item) =>
            item.TemplateId != null ? draft.BrushTemplate == item.TemplateId : draft.BrushAppearance?.Id == item.Id && draft.BrushCategory == item.CategoryId;

        static void Choose(RoomDraft draft, PaletteItem item)
        {
            if (item.TemplateId != null) draft.SelectDefinition(item.TemplateId);
            else if (item.AssetId != null) draft.SelectAsset(item.AssetId, item.CategoryId);
            else draft.ChooseAppearance(item.Id, item.CategoryId);
        }

        void Card(Rect r,RoomDraft draft,PaletteItem item)
        {
            if(r.Contains(Event.current.mousePosition)) r.y-=2;
            Frame(r,IsChosen(draft,item)); var x=r.x+12; var y=r.y+10;
            Fill(new Rect(x,y,29,28),Ink);
            Fill(new Rect(x+3,y+3,23,22),item.CategoryId==ObjectCategories.Box?new Color32(191,133,74,255):new Color32(105,150,117,255));
            if(item.CategoryId==ObjectCategories.Box) Fill(new Rect(x+12,y+3,5,22),Gold);
            else Fill(new Rect(x+9,y+9,11,10),Paper);
            Text(new Rect(x+40,r.y,r.width-54,r.height),item.Name,15);
            if(GUI.Button(r,GUIContent.none,GUIStyle.none)) Choose(draft,item);
        }

        public void Sidebar(Rect r,LayoutPanel panel)
        {
            Frame(r); var d=panel.Draft; float x=r.x+9,w=r.width-18;
            float tabWidth=(w-4*3)/PaletteTabs.All.Length;
            for(int i=0;i<PaletteTabs.All.Length;i++)
                if(Button(new Rect(x+i*(tabWidth+3),r.y-5,tabWidth,32),PaletteTabs.Name(PaletteTabs.All[i]),tab==PaletteTabs.All[i])) { tab=PaletteTabs.All[i]; d.ClearBrush(); }
            var items=d.PaletteItems(tab);
            var list=new Rect(x,r.y+43,w,Mathf.Max(112,r.height-229));
            scroll=GUI.BeginScrollView(list,scroll,new Rect(0,0,w-16,Mathf.Max(list.height,items.Count*55+6)));
            for(int i=0;i<items.Count;i++) Card(new Rect(2,i*55+3,w-20,47),d,items[i]);
            if(items.Count==0) Text(new Rect(6,6,w-30,40),"本页暂无内容");
            GUI.EndScrollView();
            float y=list.yMax+9; Fill(new Rect(x,y,w,2),Border);y+=9;
            if(d.BrushCategory!=null)
            {
                Text(new Rect(x,y,w,24),AppearanceLibrary.TypeName(d.BrushCategory),18);
                Text(new Rect(x,y+28,w,46),d.BrushCategory==AppearanceLibrary.Decoration?"只能放在房间外，不占房内格。":"占一格。请放在地板上。");
                if(Button(new Rect(x,y+83,w,30),"返回选择")) d.ClearBrush();
                return;
            }
            var cell=d.SelectedCell;
            var placed=cell.HasValue?d.Room.ObjectAt(cell.Value):null;
            if(placed==null) Text(new Rect(x,y,w,64),(d.Model.Meta?.Name??"")+"\n"+d.Room.Width+" × "+d.Room.Height);
            else
            {
                Text(new Rect(x,y,w,44),placed.Id,12);Text(new Rect(x,y+46,w,26),"格子  "+placed.Cell);
                if(AppearanceLibrary.TypeName(placed.CategoryId)!=null) Text(new Rect(x,y+73,w,22),AppearanceLibrary.TypeName(placed.CategoryId));
            }
        }
        static Vector2 Project(Camera lens,float x,float y,float z,float scale)
        {var p=lens.WorldToScreenPoint(new Vector3(x,y,z));return new Vector2(p.x,Screen.height-p.y)/scale;}
        static void Line(Vector2 a,Vector2 b,Color color,float width)
        {
            var left = Mathf.Min(a.x, b.x);
            var top = Mathf.Min(a.y, b.y);
            Fill(new Rect(left, top, Mathf.Max(width, Mathf.Abs(a.x-b.x)), Mathf.Max(width, Mathf.Abs(a.y-b.y))), color);
        }
        static void Face(Vector2 a,Vector2 b,Color color)
        {Fill(Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y)),color);}
        static void Outline(Camera lens,float scale,Cell cell,Color color,float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            var a = Project(lens,cell.X-.48f,.04f,cell.Y+.48f,scale);
            var b = Project(lens,cell.X+.48f,.04f,cell.Y-.48f,scale);
            var topRight = new Vector2(b.x,a.y);
            var bottomLeft = new Vector2(a.x,b.y);
            Line(a,topRight,color,width);
            Line(topRight,b,color,width);
            Line(bottomLeft,b,color,width);
            Line(a,bottomLeft,color,width);
        }
        public static string MapOverlay(RoomDraft draft,Camera lens,float scale,bool grid,bool hovered,Cell cell)
        {
            var origin=draft.Origin; int width=draft.Room.Width, height=draft.Room.Height; var line=new Color(0.31f,.27f,.18f,.42f);
            if(grid && Event.current.type==EventType.Repaint)
            {
                for(int x=origin.X;x<=origin.X+width;x++) Line(Project(lens,x-.5f,.02f,origin.Y-.5f,scale),Project(lens,x-.5f,.02f,origin.Y+height-.5f,scale),line,1);
                for(int z=origin.Y;z<=origin.Y+height;z++) Line(Project(lens,origin.X-.5f,.02f,z-.5f,scale),Project(lens,origin.X+width-.5f,.02f,z-.5f,scale),line,1);
            }
            if (draft.SelectedCell.HasValue)
                Outline(lens, scale, draft.SelectedCell.Value+origin, new Color32(255,181,48,255), 3);
            if (!hovered) return null;
            string category=draft.BrushCategory;
            if (category==null)
            {
                if (draft.Room.Inside(cell-origin)) Outline(lens,scale,cell,new Color32(255,248,195,255),2);
                return null;
            }
            var error=draft.PlacementError(category,cell-origin);
            var tint=error==null?new Color(.18f,.63f,.32f):new Color(.85f,.2f,.13f);
            bool tall=category==ObjectCategories.Box;
            var roof=Project(lens,cell.X+.34f,tall?.65f:.06f,cell.Y+.34f,scale);
            var front=Project(lens,cell.X-.34f,tall?.65f:.06f,cell.Y-.34f,scale);
            Face(front,roof,new Color(tint.r,tint.g,tint.b,.52f));
            Face(front,Project(lens,cell.X+.34f,0,cell.Y-.34f,scale),new Color(tint.r*.8f,tint.g*.8f,tint.b*.8f,.56f));
            Outline(lens,scale,cell,tint,3);
            return error;
        }
    }
}
