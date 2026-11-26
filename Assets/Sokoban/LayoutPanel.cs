/**
 * @file LayoutPanel.cs
 * @brief 两种宿主共用的布局工具面板。LayoutPanel 载入内置房间；Run 捕获失败并保留草稿；Click 处理格子命令；
 * Mode 是唯一的界面模式源（布局、检查、试玩、单独编辑），检查页返回只回上一模式，宿主与界面只读它，不再各自保存模式布尔量；
 * DialogTitle、DialogNote、DialogWarning 给出文件对话框的原型标题、说明与警告条文字，EntryNote 与 EntryIcon 给出列表行的类型图标与右侧说明，RefreshDirectory 供浏览按钮重读当前目录；
 * Tools 绘制放置与选择选项；Properties 显示当前实体资料；Files 绘制交换及历史操作；ChooseFile 打开文件面板；
 * Dialog 绘制目录、文件名和确认操作；Browse 加载目录内容；ConfirmFile 分派读写；ImportModel 通过尺寸检查后才登记模型；Guard 保护未保存的变动。
 */
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public sealed partial class LayoutPanel
    {
        public readonly Sokoban.Core.UiModeMachine Mode = new Sokoban.Core.UiModeMachine();
        public readonly RoomDraft Draft;
        public event Action DocumentOpened;
        public string Status = "选择工具后点击格子";
        public void ReportIssue(string message) { Status = message; }
        public bool Modal => operation != null || pending != null;
        string operation, filename, directory;
        int fileDialogVersion;
        string[] entries = Array.Empty<string>();
        Action pending, afterSave;
        bool overwrite;
        public string Operation => operation;
        public string DirectoryPath => directory;
        public string[] Entries => entries;
        public bool Confirming => pending != null;
        public bool CanKeepDraft => exitAction != null;
        public bool Overwrite => overwrite;
        public string DialogTitle => operation == "发布" ? "发布世界" : operation;
        public string DialogNote => operation switch {
            "打开" => "选择房间文件或世界文件，打开后与该文件建立关联。",
            "导入" => "读取交换文件成为独立草稿，不会改动原文件。",
            "导出" or "导出单房" => "把当前设计和所需资源写到交换目的地，当前工作文件保持不变。",
            "发布" => "发布前会检查整图，通过后写出世界清单和全部房间。",
            "导入模型" => "选择 GLB 文件，两个方向的实际尺寸都需为 1 格。",
            "保存" => "选择保存位置，保存后与该文件建立关联。",
            _ => "选择要处理的文件。" };
        public string DialogWarning => overwrite ? "目标已存在，请确认覆盖" : (operation == "发布" || operation == "导出") ? StructuralIssue : null;
        public string EntryNote(string path)
        {
            if (Directory.Exists(path)) return "文件夹";
            var info = new FileInfo(path);
            string size = info.Exists ? (info.Length < 1024 ? info.Length + " B" : info.Length / 1024 + " KB") : "";
            string name = Path.GetFileName(path).ToLowerInvariant();
            string kind = name.EndsWith(".world.json") ? "世界" : name.EndsWith(".room.json") ? "房间" : name.EndsWith(".glb") ? "模型" : "文件";
            return kind + " · " + size;
        }
        public static string EntryIcon(string path) => Directory.Exists(path) ? "▸" : path.ToLowerInvariant().EndsWith(".world.json") ? "▦" : path.ToLowerInvariant().EndsWith(".glb") ? "◆" : "▤";
        public void RefreshDirectory() => Run(() => Browse(directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), "请选择文件");
        public string Filename { get => filename; set { if (value != filename) overwrite=false; filename=value; } }
        public void ParentDirectory() => Run(()=>Browse(Directory.GetParent(directory)?.FullName ?? directory),"请选择文件");
        public void PickEntry(string path)
        {
            if(Directory.Exists(path)) Run(()=>Browse(path),"请选择文件");
            else Filename=path;
        }
        public void SubmitFile()
        {
            try { ConfirmFile(); }
            catch (UnauthorizedAccessException) { Status="此位置没有读写权限，请选择其他目录。"; }
            catch (Exception ex) { Status=ex.Message; }
        }
        public void CancelDialog() { fileDialogVersion++; pending=null; operation=null; afterSave=null; exitAction=null; }
        public void ContinueAfterSave()
        {
            var next=pending;
            if(next==null)return;
            if(Draft.WorkPath==null) { pending=null; afterSave=next; ChooseFile("保存"); return; }
            try { Draft.Save(Draft.WorkPath,false); pending=null; next(); }
            catch(Exception error) {Status=error.Message;}
        }
        public void DiscardAndContinue()
        {
            var next=pending;
            pending=null;
            try {next?.Invoke();}
            catch(Exception error) {Status=error.Message;}
        }
        public void KeepAndExit()
        {
            if(exitAction==null || !Flush())return;
            var leave=exitAction;
            CancelDialog();
            leave();
        }
        Vector2 scroll;
        public LayoutPanel(string slot=null,string recoveryDirectory=null) { Draft = new RoomDraft(Sokoban.Core.RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text)); InitSlot(slot,recoveryDirectory); }
        public void Run(Action action, string success)
        {
            try { action(); Status=success; }
            catch(UnauthorizedAccessException) { Status="文件只读或没有读写权限，请检查文件权限。"; }
            catch(Exception ex) { Status=ex.Message; }
        }
        public void Click(Sokoban.Core.Cell cell) => Run(()=>Draft.Click(cell),"当前格 " + cell);
        public void Tools()
        {
            GUILayout.Label("布局工具");
            var next=(RoomTool)GUILayout.SelectionGrid(Draft.Tool==RoomTool.Decoration?-1:(int)Draft.Tool,new[]{"选择","放箱子","放目标","移动选中","地板","墙壁","擦除"},2,GUILayout.Height(94));
            if ((int)next>=0 && next != Draft.Tool) { CancelTool(); Draft.Tool=next; }
        }
        public void Properties()
        {
            GUILayout.Label(Draft.Model.Meta.Name); GUILayout.Label(Draft.ActiveId + "  ·  " + Draft.Room.Width + "×" + Draft.Room.Height);
            GUILayout.Label("主题  " + Draft.Theme); GUILayout.Space(8);
            var obj=Draft.Find(Draft.Selected);
            if(obj==null) { GUILayout.Label("尚未选择对象"); return; }
            GUILayout.Label("对象 ID"); GUILayout.TextArea(obj.Id);
            GUILayout.Label("位置  " + obj.Cell);
        }
        public void Files()
        {
            if(GUILayout.Button("新建空白房间")) NewRoom(true);
            if(GUILayout.Button("从默认布局新建")) NewDefaultRoom();
            GUILayout.BeginHorizontal();
            foreach(var label in new[]{"打开","保存","导入","导出"}) if(GUILayout.Button(label)) ChooseFile(label);
            GUILayout.EndHorizontal(); GUILayout.BeginHorizontal();
            if(Draft.World!=null && GUILayout.Button("发布世界")) ChooseFile("发布");
            if(GUILayout.Button("撤销")) Run(Draft.Undo,"已撤销");
            if(GUILayout.Button("重做")) Run(Draft.Redo,"已重做");
            GUILayout.EndHorizontal();
        }
        public void ChooseFile(string op)
        {
            if (Painting || PlacingRoom) CancelTool();
            if (op=="发布" && Draft.World==null) { Status="请先打开世界地图"; return; }
            if ((op == "保存" || op == "导出" || op == "发布") && StructuralIssue is string issue)
            {
                Status = issue;
                return;
            }
            if(op=="保存" && Draft.WorkPath!=null) { Run(()=>Draft.Save(Draft.WorkPath,false),"已保存工作文件"); return; }
            fileDialogVersion++;
            operation=op; overwrite=false;
            filename=op=="导出单房" ? Draft.ActiveId+".room.json" : op=="保存" || op=="导出" || op=="发布" ? (Draft.World==null?"Room.json":"main.world.json") : "";
            Run(()=>Browse(directory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),"可输入完整文件路径");
        }
        void Browse(string path)
        {
            var dirs=Directory.GetDirectories(path).OrderBy(p=>p);
            var files=Directory.GetFiles(path).Where(p=>FileMatchesOperation(p)).OrderBy(p=>p);
            entries=dirs.Concat(files).ToArray();
            if (directory != path && Path.IsPathRooted(filename)) filename = Path.GetFileName(filename);
            if (directory != path) fileDialogVersion++;
            directory=path; scroll=Vector2.zero; overwrite=false;
        }
        bool FileMatchesOperation(string path)
        {
            var ext=Path.GetExtension(path).ToLowerInvariant();
            if(operation=="导入模型" || operation=="导入动作") return ext==".glb";
            if(operation=="导入贴图" || operation=="导入界面图") return ext==".png" || ext==".jpg" || ext==".jpeg";
            return ext==".json";
        }
        public void Guard(Action next) { if(Draft.Dirty) pending=next; else next(); }
        void ConfirmFile()
        {
            var op=operation; RoomFile.Check(!string.IsNullOrWhiteSpace(filename),"请输入文件名");
            string path=Path.GetFullPath(Path.Combine(directory ?? "",filename));
            if(Directory.Exists(path)) { Browse(path); filename=""; return; }
            string assetKind=op=="导入模型"?"model":op=="导入动作"?"animation":op=="导入贴图"?"texture":op=="导入界面图"?"uiImage":null;
            if (assetKind == "model") { ImportModel(path); return; }
            if(assetKind!=null) { Draft.ImportAsset(path,assetKind); operation=null; Status="已导入素材 "+Path.GetFileName(path); return; }
            if(op=="加入房间") {Draft.Attach(path); operation=null;Status="已加入房间目录"; return;}
            if(op=="打开" || op=="导入")
            {
                var candidate=new RoomDraft(Sokoban.Core.RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text)); candidate.Open(path,op=="导入"); operation=null;
                Guard(()=>{ Draft.Recover(candidate.Snapshot()); DocumentOpened?.Invoke(); Flush(); Status="已"+op+"  "+Path.GetFileName(path); });
            }
            else
            {
                if(File.Exists(path) && !overwrite) { overwrite=true; Status="目标已存在，请确认覆盖"; return; }
                if(op=="导出单房") Draft.ExportRoom(path);
                else Draft.Save(path,op=="导出" || op=="发布");
                operation=null; Status="已"+op+"  "+Path.GetFileName(path);
                var next=afterSave; afterSave=null; next?.Invoke();
            }
        }

        bool checkingModel;
        async void ImportModel(string path)
        {
            if (checkingModel) { Status = "正在检查模型尺寸"; return; }
            checkingModel = true;
            var room = Draft.Room;
            var kind = Draft.Tool;
            var input = filename;
            int version = fileDialogVersion;
            Status = "正在检查模型尺寸";
            try
            {
                var asset = RoomAssets.Import(path, "model");
                await ImportedAppearance.InspectModel(asset);
                if (version != fileDialogVersion || input != filename || room != Draft.Room || kind != Draft.Tool) return;
                string id = Draft.RegisterAsset(asset);
                if (kind == RoomTool.Decoration) Draft.SelectAsset(id, AppearanceLibrary.Decoration);
                operation = null;
                Status = "已导入素材 " + Path.GetFileName(path);
            }
            catch (Exception error)
            {
                if (version == fileDialogVersion && input == filename) Status = error.Message;
            }
            finally { checkingModel = false; }
        }
        public void Dialog(bool expandList = false)
        {
            if(pending!=null)
            {
                GUILayout.Label("当前房间有未保存的修改");
                if(GUILayout.Button("保存并继续"))
                {
                    var next=pending; pending=null;
                    if(Draft.WorkPath!=null) Run(()=>{ Draft.Save(Draft.WorkPath,false); next(); },"工作文件已保存");
                    else { afterSave=next; ChooseFile("保存"); }
                }
                if(GUILayout.Button("放弃修改并继续")) { var next=pending; pending=null; try { next(); } catch(Exception ex) { Status=ex.Message; } }
                if(exitAction!=null && GUILayout.Button("保留草稿退出")) {if(Flush()){pending=null;var leave=exitAction;exitAction=null;leave();}}
                if(GUILayout.Button("取消")) {pending=null;exitAction=null;}
                GUILayout.FlexibleSpace();
                return;
            }
            GUILayout.Label(operation); GUILayout.Label(directory, GUILayout.Height(24));
            if(GUILayout.Button("上一级目录")) Run(()=>Browse(Directory.GetParent(directory)?.FullName ?? directory),"请选择文件");
            scroll=GUILayout.BeginScrollView(scroll,expandList ? GUILayout.ExpandHeight(true) : GUILayout.Height(110));
            foreach(var path in entries) if(GUILayout.Button((Directory.Exists(path)?"▸  ":"")+Path.GetFileName(path)))
            { if(Directory.Exists(path)) Run(()=>Browse(path),"请选择文件"); else { filename=path; overwrite=false; } break; }
            GUILayout.EndScrollView(); GUILayout.Label("文件名或完整路径");
            string typed=GUILayout.TextField(filename, GUILayout.MinWidth(1), GUILayout.ExpandWidth(true)); if(typed!=filename) overwrite=false; filename=typed;
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(overwrite?"确认覆盖":operation))
            {
                try { ConfirmFile(); }
                catch (UnauthorizedAccessException) { Status = "此位置没有读写权限，请选择其他目录。"; }
                catch (Exception ex) { Status = ex.Message; }
            }
            if(GUILayout.Button("取消")) { operation=null; afterSave=null; exitAction=null; }
            GUILayout.EndHorizontal();
        }
    }
}
