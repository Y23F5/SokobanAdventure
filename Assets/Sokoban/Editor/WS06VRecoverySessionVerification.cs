/**
 * @file WS06VRecoverySessionVerification.cs
 * VerifyPoses()：批处理验证三套 Lua 姿态、叉车导入模型层级和恢复会话。
 * @brief Run 先在编辑器内离线核对恢复脚本姿态与恢复会话规则，再在隔离档案里进入主菜单，Observe 检查静止箱子、重复恢复、三次菜单存读与试玩隔离；Tick 在真实 Play 帧里推进检查。
 * PoseChecks：经 LuaHost 与 LuaRecoveryMethod 调用四个随项目发布的恢复脚本，核对零件 ID、位姿范围、占用区、完成后归位与确定性。
 * SessionChecks：RecoverySession 完成后箱子只提交一次，重复完成与继续推进都不再改变状态。
 * Observe：恢复进度取 PuzzleRun.RecoveryFraction，存档比较 SaveCodec.ToJson，其他房间隔离按各房箱位、已解开记录与对象状态签名比较。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Sokoban.Core;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class WS06VRecoverySessionVerification
    {
        const string Key = "RecoveryQA.Active";
        static IEnumerator routine;
        static int frame = -1, failures;
        static string RoomId => Environment.GetEnvironmentVariable("RECOVERY_ROOM");
        static string Folder => Environment.GetEnvironmentVariable("RECOVERY_FOLDER");
        static readonly Vector2Int[] Moves = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        static void Check(bool ok, string message) { Debug.Log("RECQA " + (ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; }

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Debug.Log("RECQA PASS " + message);
        }

        static IRecoveryMethod Method(string script)
        {
            var asset = Resources.Load<TextAsset>("RecoveryScripts/" + script + ".lua");
            Require(asset != null, "随项目发布的恢复脚本存在 " + script);
            var host = new LuaHost();
            host.Register(LuaRecoveryMethod.CreateEntry());
            return new LuaRecoveryMethod(host, asset.text);
        }

        static RecoveryPart Part(IReadOnlyList<RecoveryPart> parts, string id) => parts.SingleOrDefault(p => p.PartId == id);

        static void PoseChecks()
        {
            var from = new Cell(4, 2);
            var home = new Cell(2, 2);
            var route = RecoveryRules.Route(from, home);
            var zone = RecoveryRules.OccupiedZone(new[] { new BoxMove(from, home) });
            var ctx = new RecoveryContext("qa", "box-1", route, RecoveryItem.DefaultDuration);
            foreach (string script in new[] { "warehouse-lift", "factory-belt", "forest-decay" })
            {
                var method = Method(script);
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    var parts = method.Pose(t, ctx);
                    Require(parts.Count > 0 && parts.Select(p => p.PartId).Distinct().Count() == parts.Count && parts.All(p => !float.IsNaN(p.X + p.Y + p.Z)), script + " 姿态 t=" + t + " 零件 ID 唯一且数值有效");
                    Require(Part(parts, "box-1") != null, script + " 姿态含真箱子零件 box-1 t=" + t);
                    string error = RecoveryPoseCheck.Validate(parts, zone, p => script != "warehouse-lift" || p.PartId == "box-1");
                    Require(error == null, script + " 房内零件落在占用区内 t=" + t + " " + error);
                    var again = method.Pose(t, ctx);
                    Require(again.Count == parts.Count && again.Select((p, k) => p.PartId == parts[k].PartId && p.X == parts[k].X && p.Y == parts[k].Y && p.Z == parts[k].Z).All(v => v), script + " 同一进度重复调用结果一致 t=" + t);
                }
            }

            var lift = Method("warehouse-lift");
            var device = new RecoveryDevice("forklift", RecoveryDeviceKind.Forklift, new Cell(-1, 1), 1, "warehouse-lift", RecoveryItem.DefaultDuration);
            var liftContext = new RecoveryContext("qa", "box-1", route, RecoveryItem.DefaultDuration, device: device);
            ctx = liftContext;
            Require(lift.Pose(0, ctx).Select(p => p.PartId).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(new[] { "boom", "box-1", "crossbar", "fork", "mast", "tower", "truck" }), "叉车脚本含车身、塔柱、吊臂、滑架、横梁、双叉与箱子");
            var liftStart = Part(lift.Pose(0, ctx), "box-1");
            var liftEnd = Part(lift.Pose(1, ctx), "box-1");
            var liftMid = Part(lift.Pose(.55f, ctx), "box-1");
            Require(liftStart.X == from.X && liftStart.Z == from.Y && liftStart.Y == 0, "叉车脚本 t=0 箱子在当前格地面");
            Require(liftEnd.X == home.X && liftEnd.Z == home.Y && liftEnd.Y == 0, "叉车脚本 t=1 箱子落回初始格地面");
            Require(liftMid.Y > 1.45f && liftMid.X < from.X && liftMid.X > home.X, "叉车脚本中段箱子抬起并在两格之间");
            var truckStart = Part(lift.Pose(0, ctx), "truck");
            var truckMid = Part(lift.Pose(.55f, ctx), "truck");
            var truckEnd = Part(lift.Pose(1, ctx), "truck");
            Require(truckStart.X < device.Anchor.X && truckMid.X == truckStart.X && truckEnd.X == truckStart.X &&
                truckMid.Z != truckStart.Z && truckEnd.Z == truckStart.Z, "叉车沿墙平移并返回墙外停放点");
            foreach (var part in lift.Pose(.55f, ctx).Where(p => p.Resource != null))
            {
                var model = Resources.Load<GameObject>(part.Resource);
                var resource = model && part.Child != null
                    ? model.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == part.Child)?.gameObject : model;
                Require(resource && resource.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterial),
                    "叉车演出零件已导入并绑定材质 " + part.PartId);
            }
            Require(Part(lift.Pose(.1f, ctx), "fork").ScaleX < .2f && Part(lift.Pose(.55f, ctx), "fork").ScaleX > .7f && Part(lift.Pose(.96f, ctx), "fork").ScaleX < .2f, "叉车脚本货叉在中段伸出、结束收回");

            var belt = Method("factory-belt");
            var rollers0 = belt.Pose(0, ctx).Where(p => p.PartId.StartsWith("roller-")).ToArray();
            var rollersFull = belt.Pose(.29f, ctx).Where(p => p.PartId.StartsWith("roller-")).ToArray();
            Require(rollers0.Length == 17 && rollersFull.Length == 17 && rollers0.All(p => !p.Visible) && rollersFull.All(p => p.Visible), "输送带脚本滚筒数量固定为 17，伸出前不可见、伸出后可见");
            Require(Math.Abs(Part(belt.Pose(.29f, ctx), "belt").ScaleZ - 2.72f) < .001f && Part(belt.Pose(1, ctx), "belt").ScaleZ < 2.72f * .025f, "输送带脚本导轨伸出到全长并在结束收回");
            var beltEnd = Part(belt.Pose(1, ctx), "box-1");
            Require(beltEnd.X == home.X && beltEnd.Z == home.Y && beltEnd.Y == 0 && Part(belt.Pose(.5f, ctx), "box-1").Y > .25f, "输送带脚本货物中段抬起、结束落回初始格");
            Require(belt.Pose(.5f, ctx).First(p => p.PartId == "roller-0").Roll < belt.Pose(.4f, ctx).First(p => p.PartId == "roller-0").Roll, "输送带脚本货物前进时滚筒同向转动");

            var decay = Method("forest-decay");
            var d0 = decay.Pose(0, ctx);
            var d5 = decay.Pose(.5f, ctx);
            var d9 = decay.Pose(.9f, ctx);
            var d1 = decay.Pose(1, ctx);
            Require(Part(d0, "box-1").Visible && !Part(d5, "box-1").Visible, "森林脚本开裂后原木隐藏");
            Require(Part(d5, "box-1-decay").Visible && !Part(d9, "box-1-decay").Visible, "森林脚本碎裂窗口内显示碎片、之后隐藏");
            Require(!Part(d5, "box-1-tree").Visible && Part(d9, "box-1-tree").Visible && Part(d9, "box-1-tree").ClipTime > 0, "森林脚本后段在初始格生长出树");
            Require(Part(d1, "box-1-tree").X == home.X && Part(d1, "box-1-tree").Z == home.Y && Part(d1, "box-1-tree").ClipTime == 1, "森林脚本结束时完整树木在初始格");

            var gateCtx = new RecoveryContext("qa", "gate-4-5", new List<Cell> { new Cell(4, 5) }, RecoveryItem.DefaultDuration,
                new Dictionary<string, float> { ["gateX"] = 4f, ["gateZ"] = 5f, ["yaw"] = -90f });
            var gate = Method("forest-vine").Pose(.5f, gateCtx).Single();
            Require(gate.PartId == "forest-vine" && gate.X == 4f && gate.Z == 5f && gate.Yaw == -90f && Math.Abs((gate.ClipTime ?? -1f) - .5f) < .0001f, "藤门脚本取格位与朝向参数，剪辑时刻等于进度");
        }

        static void SessionChecks()
        {
            var room = new Room("qa", 9, 5);
            for (int x = 0; x < 9; x++)
                for (int y = 0; y < 5; y++) room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2));
            var session = new RecoverySession(room, new Dictionary<string, Cell> { ["box-1"] = new Cell(4, 2) }, new Cell(0, 0), null);
            session.Start(RecoveryItem.DefaultDuration);
            Require(session.Recovering && !session.CanPush() && session.Zone.Contains(new Cell(3, 2)), "恢复开始：禁推且占用区覆盖两格之间");
            Require(!session.Advance(1f) && session.Boxes["box-1"] == new Cell(4, 2) && session.CompletedCount == 0, "恢复期间箱子保持原位且未提交");
            Require(session.Advance(10f) && session.Boxes["box-1"] == new Cell(2, 2) && session.CompletedCount == 1, "时间用尽时箱子一次性回到初始格并提交一次");
            session.Complete();
            Require(!session.Advance(1f) && session.CompletedCount == 1 && session.Boxes["box-1"] == new Cell(2, 2), "重复完成与继续推进都不再提交");
        }

        public static void VerifyPoses()
        {
            PoseChecks();
            SessionChecks();
        }

        public static void Run()
        {
            VerifyPoses();
            if (!Application.productName.StartsWith("RecoveryQA-") || File.Exists(CampaignSlot.PathName)) throw new InvalidOperationException("Use a fresh isolated RecoveryQA- product profile");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try { if (routine == null) routine = Observe(); if (routine.MoveNext()) return; }
            catch (Exception ex) { Debug.LogException(ex); failures++; }
            SessionState.EraseBool(Key);
            Debug.Log("RECQA END failures=" + failures);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static Vector3 Position(Vector2Int p) => new Vector3(p.x, 0, p.y);

        static string Rig(string cargo) => RoomId == "warehouse" ? "truck" : RoomId == "factory" ? "belt" : cargo + "-tree";

        static string Signature(SaveData data, string room) =>
            string.Join(";", data.Boxes[room].OrderBy(b => b.Key, StringComparer.Ordinal).Select(b => b.Key + "@" + b.Value[0] + "," + b.Value[1])) + "|" + data.EverSolved[room] + "|" +
            string.Join(";", data.ObjectStates[room].OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key + "=" + s.Value));

        static IEnumerator Observe()
        {
            yield return null;
            Time.captureFramerate = 120;
            var seed = PuzzleRun.Campaign();
            foreach (char step in "RRRURRRRRURRRRRRLDLDDLUULURRRDLDDRRRRUURRRURUULURDRDRRDRRURRDDLUURULRDDDRR")
            {
                if (seed.ActiveId == RoomId) break;
                if (!seed.Step(Moves["URDL".IndexOf(step)], out _)) throw new Exception("campaign approach blocked");
            }
            if (seed.ActiveId != RoomId) throw new Exception("room not reached");
            seed.AcceptItem();
            var start = seed.Snapshot();
            var spawn = seed.Room.Spawn.Cell;
            start.PlayerX = spawn.X;
            start.PlayerY = spawn.Y;
            seed.Restore(start);
            start = seed.Snapshot();
            CampaignSlot.Save(seed);
            Object.FindObjectOfType<MainMenuNavigation>().ContinueGame();
            while (!Object.FindObjectOfType<PuzzleHost>()) yield return null;
            yield return null; yield return null;
            var host = Object.FindObjectOfType<PuzzleHost>();
            var group = host.transform.Find(RoomId);
            bool Homes() => host.Run.Boxes.All(b => b.Value == host.Run.HomeOf(b.Key) && group.Find(b.Key).position == Position(b.Value));
            bool OtherRooms() { var now = host.Run.Snapshot(); return start.Boxes.Keys.Where(r => r != RoomId).All(r => Signature(start, r) == Signature(now, r)); }
            for (int cycle = 0; cycle < 2; cycle++)
            {
                if (!host.UseItem()) throw new Exception("stationary recovery refused");
                bool stationary = true;
                while (host.Run.Recovering)
                {
                    stationary &= Homes() && !group.Find(Rig(host.Run.Boxes.Keys.First())) && host.Run.Boxes.Keys.All(id => group.Find(id).GetComponentsInChildren<Renderer>().Any(r => r.enabled));
                    yield return null;
                }
                Check(stationary && Homes(), "stationary cycle " + (cycle + 1) + " boxes=" + host.Run.Boxes.Count);
            }
            string route = RoomId == "warehouse" ? "RR" : RoomId == "factory" ? "URR" : "DRRRRR";
            foreach (char step in route)
            {
                if (!host.Move(Moves["URDL".IndexOf(step)])) throw new Exception("push route blocked");
                while (host.Busy) yield return null;
            }
            string cargo = host.Run.Boxes.Single(b => b.Value != host.Run.HomeOf(b.Key)).Key;
            if (!host.UseItem()) throw new Exception("displaced recovery refused");
            Check(!host.UseItem(), "reentrant recovery rejected");
            foreach (float phase in new[] { .15f, .50f, .85f })
            {
                while (host.Run.RecoveryFraction < phase) yield return null;
                host.panels.Pause();
                float clock = host.Run.RecoveryRemaining;
                var snapshot = host.Run.Snapshot();
                var parts = group.GetComponentsInChildren<Transform>(true);
                var poses = parts.Select(p => p.localToWorldMatrix).ToArray();
                for (int i = 0; i < 30; i++) yield return null;
                Check(host.panels.Modal && clock == host.Run.RecoveryRemaining && parts.Select((p, i) => poses[i] == p.localToWorldMatrix).All(v => v), "pause " + phase + " remaining=" + clock);
                host.panels.Back();
                yield return null; yield return null; yield return null;
                Check(host.Run.RecoveryRemaining < clock, "continue " + phase);
                host.panels.Pause();
                snapshot = host.Run.Snapshot();
                var pos = group.Find(cargo).position;
                host.Leave();
                while (!Object.FindObjectOfType<MainMenuNavigation>()) yield return null;
                yield return null;
                File.Copy(CampaignSlot.PathName, Path.Combine(Folder, "phase-" + Mathf.RoundToInt(phase * 100) + ".json"));
                Check(File.ReadAllText(CampaignSlot.PathName) == SaveCodec.ToJson(snapshot), "saved snapshot " + phase);
                Object.FindObjectOfType<MainMenuNavigation>().ContinueGame();
                while (!Object.FindObjectOfType<PuzzleHost>()) yield return null;
                yield return null; yield return null;
                host = Object.FindObjectOfType<PuzzleHost>();
                group = host.transform.Find(RoomId);
                float dt = (host.Run.RecoveryFraction - snapshot.Presentation.RecoveryT) * host.Run.RecoveryDuration;
                Check(host.Run.ActiveId == RoomId && dt >= -.005f && dt < .08f && group.Find(Rig(cargo)) && Vector3.Distance(pos, group.Find(cargo).position) < .22f && OtherRooms(), "reloaded " + phase + " elapsed=" + dt + " poseDelta=" + Vector3.Distance(pos, group.Find(cargo).position));
            }
            while (host.Run.Recovering) yield return null;
            yield return null;
            Check(Homes() && !group.Find(Rig(cargo)) && OtherRooms(), "post-load landing and other rooms isolated");
            var cell = host.Run.HomeOf(cargo);
            var direction = cell - host.Run.Player;
            bool pushed = host.Move(direction);
            while (host.Busy) yield return null;
            Check(pushed && host.Run.Boxes[cargo] == cell + direction && group.Find(cargo).position == Position(cell + direction) && group.Find(cargo).GetComponentsInChildren<Renderer>().Any(r => r.enabled), "first push after recovery direction=" + direction + " cargo=" + cargo);
            Check(host.UseItem(), "repeat displaced recovery accepted");
            while (host.Run.Recovering) yield return null;
            yield return null;
            Check(Homes() && !group.Find(Rig(cargo)) && OtherRooms(), "second landing and cleanup");
            host.Leave();
            while (!Object.FindObjectOfType<MainMenuNavigation>()) yield return null;
            yield return null;
            var bytes = File.ReadAllBytes(CampaignSlot.PathName);
            Object.FindObjectOfType<MainMenuNavigation>().OpenEditor();
            while (!Object.FindObjectOfType<LayoutHost>()) yield return null;
            yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            editor.Panel.Draft.Open(Path.Combine(Application.streamingAssetsPath, "World", RoomId + ".room.json"), true);
            if (RoomId == "warehouse")
                editor.Panel.Draft.PlaceRecoveryDevice(new RecoveryDevice("qa-west-lift", RecoveryDeviceKind.Forklift,
                    new Cell(-1, 1), 1, "warehouse-lift", RecoveryItem.DefaultDuration),
                    replaceExisting: editor.Panel.Draft.Model.RecoveryDevice != null);
            editor.StartTrial();
            yield return null; yield return null;
            host = Object.FindObjectOfType<PuzzleHost>();
            if (host.Run.PendingItem != null) host.panels.Back();
            yield return null; yield return null;
            foreach (char step in route)
            {
                if (!host.Move(Moves["URDL".IndexOf(step)])) throw new Exception("trial route blocked");
                while (host.Busy) yield return null;
            }
            Check(host.IsTrial && host.UseItem(), "trial recovery started");
            if (RoomId == "warehouse")
            {
                var trialRoom = host.transform.Find(RoomId);
                var parked = trialRoom.Find("qa-west-lift");
                while (host.Run.RecoveryFraction < .46f) yield return null;
                var truck = trialRoom.Find("truck");
                var fork = trialRoom.Find("fork");
                Check(truck && fork && parked && !parked.gameObject.activeSelf &&
                    truck.localPosition.x < -1f && fork.localPosition.x >= 0f,
                    "authored west-side fixture keeps the truck out and sends the forks in");
                while (host.Run.Recovering) yield return null;
                yield return null;
                Check(parked.gameObject.activeSelf && !trialRoom.Find("truck") && !trialRoom.Find("fork"),
                    "authored model restored and temporary rig removed after recovery");
            }
            yield return null; yield return null;
            host.Leave();
            yield return null; yield return null;
            Check(bytes.SequenceEqual(File.ReadAllBytes(CampaignSlot.PathName)) && editor.Interface.gameObject.activeInHierarchy, "trial return preserves campaign bytes");
        }
    }
}
