/**
 * @file PuzzleHost.cs
 * @brief Start 接收菜单或编辑器传入的运行状态。Begin 和 BuildModels 按世界原点生成各房间实例，用活动房间的稳定 ID 绑定角色、箱子和门。
 * BindRoom 绑定活动房间并设置镜头目标，按规则朝向和角色网格的正面设置模型。ShowArtwork 读取房间界面图资源并显示。
 * Move 和 Turn 接受互斥动作，Update 与 Animate 推进独立时钟，恢复期间由 TickRecovery 推进规则会话并驱动恢复演出。
 * SetCamera 更新正交构图，RefreshFeedback 更新目标、门与遮挡。Leave 和 Persist 只为正式局写单槽。
 * RestoreMotion 从演出段重建一次未完成的移动或转向。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class PuzzleHost : MonoBehaviour
    {
        public PlayPanels panels;
        public static PuzzleRun MenuRequest;
        public PuzzleRun Run { get; private set; }
        public bool IsTrial { get; private set; }
        public bool Busy => duration > 0;
        readonly Dictionary<string, Transform> objects = new Dictionary<string, Transform>();
        readonly RecoveryPresenter presenter = new RecoveryPresenter();
        Camera lens;
        Transform actor, movingBox;
        Vector3 playerStart, boxStart, focus;
        Quaternion playerStartRotation;
        float elapsed, duration, startAngle, angle, endAngle;
        bool turning, leaving;
        Action returnToEditor;
        Texture2D artwork;

        void Start()
        {
            if (Run != null) return;
            try
            {
                if (MenuRequest != null)
                {
                    var request = MenuRequest;
                    MenuRequest = null;
                    Begin(request, false, null);
                    CampaignSlot.Save(request);
                    return;
                }
                throw new InvalidOperationException("请从主菜单开始游戏，或从编辑器试玩房间");
            }
            catch (Exception error) { panels.ShowError(error.Message); }
        }

        public void Begin(PuzzleRun run, bool trial, Action onReturn)
        {
            IsTrial = trial;
            returnToEditor = onReturn;
            lens = Camera.main;
            Run = run;
            BuildModels(run);
            BindRoom();
            lens.orthographic = true;
            angle = run.Heading * 90;
            SetCamera();
            panels.ShowPlay(trial);
            ShowInventory();
            RefreshFeedback();
            RestoreMotion();
            ShowEntryHint();
        }

        void ShowEntryHint()
        {
            string hint = Run.Room.Meta.EntryHint;
            if (Run.PendingItem == null && !string.IsNullOrWhiteSpace(hint)) panels.ShowInformation(Run.Room.Meta.Name, hint);
        }

        void RestoreMotion()
        {
            var motion = Run.Motion;
            if (motion == null) return;
            turning = motion.Kind == "turn";
            duration = turning ? .38f : .22f;
            if (turning)
            {
                startAngle = angle;
                endAngle = angle + 90 * motion.Direction;
            }
            else
            {
                Vector3 delta = Position(motion.Delta);
                playerStart = actor.position - delta;
                playerStartRotation = actor.rotation;
                movingBox = motion.Pushed == null ? null : objects[motion.Pushed];
                if (movingBox) boxStart = movingBox.position - delta;
            }
            elapsed = motion.Fraction * duration;
            Animate(0);
        }

        void BuildModels(PuzzleRun run)
        {
            var builder = new RoomPreview();
            foreach (var pair in run.Models)
            {
                var placement = run.World?.Rooms.FirstOrDefault(r => r.LevelId == pair.Key);
                if (run.World != null && placement == null) continue;
                var origin = placement == null ? new Cell(0, 0) : placement.Origin;
                var group = builder.BuildRoom(pair.Value, origin, null, transform, run.World, run.Models);
                groups.Add(pair.Key, group);
                group.Find(run.Rooms[pair.Key].Spawn.Id).gameObject.SetActive(false);
                foreach (var box in run.BoxesIn(pair.Key)) group.Find(box.Key).position = Position(box.Value);
                SyncObjectStates(pair.Key, group);
                if (pair.Key != run.ActiveId || !run.Recovering) SyncTerrainGates(pair.Key, group);
            }
            InitAtmosphere();
        }

        void BindRoom()
        {
            var run = Run;
            if (actor) actor.gameObject.SetActive(false);
            var current = groups[run.ActiveId];
            objects.Clear();
            foreach (Transform part in current)
            {
                objects[part.name] = part;
            }
            actor = objects[run.Room.Spawn.Id];
            actor.gameObject.SetActive(true);
            actor.rotation = RoomPreview.PlayerRotation(run.Facing);
            actor.localScale = Vector3.one;
            actor.position = Position(run.Player);
            var room = run.Room.Room;
            targetFocus = Position(run.Origin) + new Vector3(room.Width - 1, 0, room.Height - 1) * .5f;
            targetSize = Mathf.Max(3, Mathf.Max(room.Width, room.Height) * .69f / Mathf.Min(1, lens.aspect));
            if (!framed) { focus = targetFocus; lens.orthographicSize = targetSize; framed = true; }
            EnterAtmosphere();
            ShowArtwork(run.Models[run.ActiveId]);
            foreach (var box in run.Boxes) objects[box.Key].position = Position(box.Value);
        }

        void ShowArtwork(RoomDraftModel model)
        {
            if (!panels.roomArtwork) return;
            if (artwork) Destroy(artwork);
            var asset = model.UiImageAssetId == null ? null : model.Assets.FirstOrDefault(a => a.Id == model.UiImageAssetId);
            artwork = asset == null ? null : ImportedAppearance.ReadImage(asset);
            panels.roomArtwork.texture = artwork;
            panels.roomArtwork.gameObject.SetActive(artwork != null);
        }

        static Vector3 Position(Vector2Int cell) => new Vector3(cell.x, 0, cell.y);

        void Update()
        {
            if (Run == null || leaving) return;
            if (panels.Modal)
            {
                if (Run.PendingItem != null || themeFade < 1) TickAtmosphere(Time.deltaTime);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { panels.Pause(); return; }
            TickAtmosphere(Time.deltaTime);
            if (Run.Recovering) TickRecovery(Time.deltaTime);
            if (Busy) { Animate(Time.deltaTime); return; }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0) SelectItem((int)Mathf.Sign(wheel));
            if (Input.GetKeyDown(KeyCode.R)) { UseItem(); return; }
            if (Input.GetKeyDown(KeyCode.F)) { Interact(); return; }

            if (Input.GetKeyDown(KeyCode.Q)) Turn(-1);
            else if (Input.GetKeyDown(KeyCode.E)) Turn(1);
            else if (Input.GetKeyDown(KeyCode.W)) Move(Vector2Int.up);
            else if (Input.GetKeyDown(KeyCode.S)) Move(Vector2Int.down);
            else if (Input.GetKeyDown(KeyCode.A)) Move(Vector2Int.left);
            else if (Input.GetKeyDown(KeyCode.D)) Move(Vector2Int.right);
        }

        public bool UseItem()
        {
            if (leaving || panels.Modal || Busy || !Run.BeginRecovery()) return false;
            actor.position = Position(Run.Player);
            ShowInventory(); Persist(); return true;
        }

        public void SelectItem(int direction)
        {
            if (leaving || panels.Modal || direction == 0 || Run.Inventory.Count == 0) return;
            Run.CycleItem(direction); ShowInventory(); Persist();
        }

        public bool Move(Vector2Int screen)
        {
            if (Busy || leaving || panels.Modal) return false;
            var world = screen;
            for (int i = 0; i < Run.Heading; i++) world = new Vector2Int(world.y, -world.x);
            string oldRoom = Run.ActiveId;
            if (!Run.Step(world, out string pushed)) return false;
            if (oldRoom != Run.ActiveId)
            {
                BindRoom();
                RefreshFeedback();
                ShowInventory();
                ShowEntryHint();
                Persist();
                return true;
            }
            SyncObjectStates(Run.ActiveId, groups[Run.ActiveId]);
            playerStart = actor.position;
            playerStartRotation = actor.rotation;
            movingBox = pushed == null ? null : objects[pushed];
            if (movingBox) boxStart = movingBox.position;
            elapsed = 0;
            duration = .22f;
            Run.Motion = new PuzzleMotion { Kind = "move", Fraction = 0, Delta = world, Pushed = pushed };
            Persist();
            return true;
        }

        public void Turn(int direction)
        {
            if (Busy || leaving || panels.Modal || Mathf.Abs(direction) != 1) return;
            turning = true;
            startAngle = angle;
            endAngle = angle + direction * 90;
            elapsed = 0;
            duration = .38f;
            Run.Motion = new PuzzleMotion { Kind = "turn", Fraction = 0, Direction = direction };
            Persist();
        }

        void Animate(float seconds)
        {
            elapsed += seconds;
            float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
            if (turning)
            {
                angle = Mathf.Lerp(startAngle, endAngle, t);
                SetCamera();
            }
            else
            {
                actor.position = Vector3.Lerp(playerStart, Position(Run.Player), t);
                actor.rotation = Quaternion.Slerp(playerStartRotation, RoomPreview.PlayerRotation(Run.Facing), t);
                if (movingBox) movingBox.position = Vector3.Lerp(boxStart, boxStart + Position(Run.Player) - playerStart, t);
            }
            UpdateOcclusion();
            if (elapsed < duration) { if (Run.Motion != null) Run.Motion.Fraction = elapsed / duration; return; }
            if (turning)
            {
                Run.Heading = (Mathf.RoundToInt(endAngle / 90) % 4 + 4) % 4;
                angle = Run.Heading * 90;
            }
            bool walked = !turning;
            turning = false;
            movingBox = null;
            duration = 0;
            Run.Motion = null;
            Persist();
            RefreshFeedback();
            if (walked && !panels.Modal && !Run.Recovering)
            {
                var hint = Run.HintAtPlayer();
                if (!string.IsNullOrWhiteSpace(hint)) panels.ShowInformation(Run.Room.Meta.Name, hint);
            }
        }

        void SetCamera()
        {
            CameraRig.Place(lens, focus, angle, lens.orthographicSize * 3 + 10);
        }

        void RefreshFeedback()
        {
            if (!Run.Recovering) SyncTerrainGates(Run.ActiveId, groups[Run.ActiveId]);
            UpdateOcclusion();
            foreach (var door in Run.Room.Doors)
                if (door.Role == "exit" && objects.TryGetValue(door.Id, out var gate)) gate.gameObject.SetActive(!Run.CanApproach(door));
            foreach (var box in Run.Boxes)
            {
                var tint = new MaterialPropertyBlock();
                if (Run.Targets.Contains(box.Value)) tint.SetColor("_BaseColor", new Color(.46f, .74f, .38f));
                foreach (var mesh in objects[box.Key].GetComponentsInChildren<Renderer>()) mesh.SetPropertyBlock(tint);
            }
        }

        void Persist()
        {
            if (!IsTrial && Run != null)
                try { CampaignSlot.Save(Run); }
                catch (Exception error) { panels.ShowError("保存进度失败：" + error.Message); }
        }

        public void Leave()
        {
            if (leaving) return;
            if (!IsTrial && Run != null)
            {
                try { CampaignSlot.Save(Run); }
                catch (Exception error) { panels.ShowError(error.Message); return; }
            }
            leaving = true;
            if (IsTrial) returnToEditor();
            else SceneManager.LoadSceneAsync("MainMenu");
        }

        void OnApplicationQuit() => Persist();
    }
}
