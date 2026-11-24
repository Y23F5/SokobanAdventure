/**
 * @file PaletteView.cs
 * @brief 运行端像素风素材栏与地图覆盖层。Fill 绘制纯色矩形；Frame 叠绘像素边缘；Text 设定字色及对齐；Button 绘制可点击按钮；
 * Card 为内置素材绘制缩略图、悬停与选中态；Sidebar 切换工具及当前对象的移动操作；Project 投影格子顶点；
 * Outline 标出悬停与选中格；Line 将像素线段对齐两个端点；MapOverlay 绘制网格、半透明体积预览及合法性轮廓；Face 用纯色纹理绘制半透明四边形。
 */
using UnityEngine;

namespace Sokoban
{
    public sealed class PaletteView
    {
        static readonly Color Ink = new Color32(100,71,61,255), Border = new Color32(156,111,75,255), Paper = new Color32(255,232,185,255), Gold = new Color32(248,193,92,255);
        Vector2 scroll;
        bool terrain;
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
        void Card(Rect r,RoomDraft draft,RoomTool tool,string name)
        {
            if(r.Contains(Event.current.mousePosition)) r.y-=2;
            Frame(r,draft.Tool==tool); var x=r.x+12; var y=r.y+10;
            Fill(new Rect(x,y,29,28),Ink);
            Fill(new Rect(x+3,y+3,23,22),tool==RoomTool.Box?new Color32(191,133,74,255):new Color32(105,150,117,255));
            if(tool==RoomTool.Box) Fill(new Rect(x+12,y+3,5,22),Gold);
            else Fill(new Rect(x+9,y+9,11,10),Paper);
            Text(new Rect(x+40,r.y,r.width-54,r.height),name,17);
            if(GUI.Button(r,GUIContent.none,GUIStyle.none)) draft.Tool=tool;
        }
        public void Sidebar(Rect r,LayoutPanel panel)
        {
            Frame(r); var d=panel.Draft; float x=r.x+9,w=r.width-18;
                        if(Button(new Rect(x,r.y-5,60,32),"物件",!terrain)) terrain=false;
            if(Button(new Rect(x+64,r.y-5,60,32),"地形",terrain)) terrain=true;
            var list=new Rect(x,r.y+43,w,Mathf.Max(112,r.height-229));
            scroll=GUI.BeginScrollView(list,scroll,new Rect(0,0,w-16,terrain?155:110));
            if(terrain) {
                int row=0; foreach(var tool in new[]{RoomTool.Floor,RoomTool.Wall,RoomTool.Erase}) {
                    string name=tool==RoomTool.Floor?"地板":tool==RoomTool.Wall?"墙壁":"擦除";
                    if(Button(new Rect(2,row++*50+3,w-20,43),name,d.Tool==tool))d.Tool=tool;
                }
            } else { Card(new Rect(2,3,w-20,47),d,RoomTool.Box,"纸箱"); Card(new Rect(2,58,w-20,47),d,RoomTool.Target,"目标点"); } GUI.EndScrollView();
            float y=list.yMax+9; Fill(new Rect(x,y,w,2),Border);y+=9;
            if(d.Tool==RoomTool.Box || d.Tool==RoomTool.Target)
            {
                Text(new Rect(x,y,w,24),d.Tool==RoomTool.Box?"纸箱":"目标点",18);
                Text(new Rect(x,y+28,w,46),d.Tool==RoomTool.Box?"占一格。请放在空地上。":"标出箱子需要抵达的格子。");
                if(Button(new Rect(x,y+83,w,30),"返回选择")) d.Tool=RoomTool.Select;
            }
            else
            {
                var obj=d.Find(d.Selected);
                if(obj==null) Text(new Rect(x,y,w,64),(string)d.Room["name"]+"\n"+RoomFile.Size(d.Room));
                else
                {
                    Text(new Rect(x,y,w,44),d.Selected,12);Text(new Rect(x,y+46,w,26),"格子  "+RoomFile.At(obj));
                    if(obj["recoveryCell"]!=null) Text(new Rect(x,y+73,w,22),"恢复格  "+RoomFile.Cell(obj["recoveryCell"]));
                    if((obj.Parent==d.Room["boxes"] || obj.Parent==d.Room["targets"]) && Button(new Rect(x,y+103,w,30),d.Tool==RoomTool.Move?"取消移动":"移动位置",d.Tool==RoomTool.Move)) d.Tool=d.Tool==RoomTool.Move?RoomTool.Select:RoomTool.Move;
                }
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
        static void Outline(Camera lens,float scale,Vector2Int cell,Color color,float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            var a = Project(lens,cell.x-.48f,.04f,cell.y+.48f,scale);
            var b = Project(lens,cell.x+.48f,.04f,cell.y-.48f,scale);
            var topRight = new Vector2(b.x,a.y);
            var bottomLeft = new Vector2(a.x,b.y);
            Line(a,topRight,color,width);
            Line(topRight,b,color,width);
            Line(bottomLeft,b,color,width);
            Line(a,bottomLeft,color,width);
        }
        public static string MapOverlay(RoomDraft draft,Camera lens,float scale,bool grid,bool hovered,Vector2Int cell)
        {
            var origin=draft.Origin;var size=RoomFile.Size(draft.Room); var line=new Color(0.31f,.27f,.18f,.42f);
            if(grid && Event.current.type==EventType.Repaint)
            {
                for(int x=origin.x;x<=origin.x+size.x;x++) Line(Project(lens,x-.5f,.02f,origin.y-.5f,scale),Project(lens,x-.5f,.02f,origin.y+size.y-.5f,scale),line,1);
                for(int z=origin.y;z<=origin.y+size.y;z++) Line(Project(lens,origin.x-.5f,.02f,z-.5f,scale),Project(lens,origin.x+size.x-.5f,.02f,z-.5f,scale),line,1);
            }
            if (draft.SelectedCell.HasValue)
                Outline(lens, scale, draft.SelectedCell.Value+origin, new Color32(255,181,48,255), 3);
            if (!hovered) return null;
            if (draft.Tool == RoomTool.Select || (int)draft.Tool>=(int)RoomTool.Floor)
            {
                if (RoomFile.Inside(draft.Room,cell-origin)) Outline(lens,scale,cell,new Color32(255,248,195,255),2);
                return null;
            }
            var obj=draft.Find(draft.Selected);
            bool box=draft.Tool==RoomTool.Box || (draft.Tool==RoomTool.Move && obj?.Parent==draft.Room["boxes"]);
            var error=draft.PlacementError(box,cell-origin,draft.Tool==RoomTool.Move?draft.Selected:null);
            var tint=error==null?new Color(.18f,.63f,.32f):new Color(.85f,.2f,.13f);
            
            var roof=Project(lens,cell.x+.34f,box?.65f:.06f,cell.y+.34f,scale);
            var front=Project(lens,cell.x-.34f,box?.65f:.06f,cell.y-.34f,scale);
            Face(front,roof,new Color(tint.r,tint.g,tint.b,.52f));
            Face(front,Project(lens,cell.x+.34f,0,cell.y-.34f,scale),new Color(tint.r*.8f,tint.g*.8f,tint.b*.8f,.56f));
            Outline(lens,scale,cell,tint,3);
            return error;
        }
    }
}
