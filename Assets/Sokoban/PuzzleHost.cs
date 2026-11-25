/**
 * @file PuzzleHost.cs
 * @brief Start 接收菜单或编辑器传入的运行状态。Begin 和 BuildModels 按世界原点生成各房间实例，
 * 用活动房间的稳定 ID 绑定角色、箱子和门。Move 和 Turn 接受互斥动作，Update 与 Animate 推进独立时钟。
 * SetCamera 更新正交构图，RefreshFeedback 更新目标、门与遮挡。Leave 和 Persist 只为正式局写单槽。
 * RestoreMotion 从已提交的终点反推表现起点，继续未完成的一次移动或转向。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        readonly List<Transform> boundaryWalls = new List<Transform>();
        Camera lens;
        Transform actor, movingBox;
        Vector3 playerStart, boxStart, focus;
        float elapsed, duration, startAngle, angle, endAngle;
        bool turning, leaving;
        Action returnToEditor;

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
#if UNITY_EDITOR
                string snapshot = UnityEditor.SessionState.GetString("Sokoban.GameTrial", "");
                if (!string.IsNullOrEmpty(snapshot))
                {
                    var draft = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
                    draft.Recover(JObject.Parse(snapshot));
                    Begin(PuzzleRun.FromDraft(draft), true, () => UnityEditor.EditorApplication.isPlaying = false);
                    return;
                }
#endif
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
        }

        void RestoreMotion()
        {
            var motion = Run.Motion;
            if (motion == null) return;
            turning = (string)motion["kind"] == "turn";
            duration = turning ? .38f : .22f;
            if (turning)
            {
                startAngle = angle;
                endAngle = angle + 90 * (int)motion["direction"];
            }
            else
            {
                Vector3 delta = Position(RoomFile.Cell(motion["delta"]));
                playerStart = actor.position - delta;
                string pushed = (string)motion["pushed"];
                movingBox = pushed == null ? null : objects[pushed];
                if (movingBox) boxStart = movingBox.position - delta;
            }
            elapsed = (float)motion["fraction"] * duration;
            Animate(0);
        }

        void BuildModels(PuzzleRun run)
        {
            var builder = new RoomPreview();
            foreach (var row in run.Rooms.Properties())
            {
                var room = (JObject)row.Value;
                var placement = run.World?["rooms"].FirstOrDefault(r => (string)r["levelId"] == row.Name);
                if (run.World != null && placement == null) continue;
                var origin = placement == null ? Vector2Int.zero : RoomFile.Cell(placement["origin"]);
                var group = builder.BuildRoom(room, origin, null, transform, run.World, run.Rooms);
                groups.Add(row.Name, group);
                group.Find((string)room["spawn"]["id"]).gameObject.SetActive(false);
                foreach (var box in run.BoxesIn(row.Name)) group.Find(box.Key).position = Position(box.Value);
                SyncObjectStates(row.Name, group);
            }
            InitAtmosphere();
        }

        void BindRoom()
        {
            var run = Run;
            if (actor) actor.gameObject.SetActive(false);
            var current = groups[run.ActiveId];
            objects.Clear();
            boundaryWalls.Clear();
            foreach (Transform part in current)
            {
                objects[part.name] = part;
                if (part.name.StartsWith("Wall ", StringComparison.Ordinal)) boundaryWalls.Add(part);
            }
            actor = objects[(string)run.Room["spawn"]["id"]];
            actor.gameObject.SetActive(true);
            actor.position = Position(run.Player);
            var size = RoomFile.Size(run.Room);
            targetFocus = Position(run.Origin) + new Vector3(size.x - 1, 0, size.y - 1) * .5f;
            targetSize = Mathf.Max(3, Mathf.Max(size.x, size.y) * .69f / Mathf.Min(1, lens.aspect));
            if (!framed) { focus = targetFocus; lens.orthographicSize = targetSize; framed = true; }
            EnterAtmosphere();
            panels.ShowRoomArtwork(run.Room);
            foreach (var box in run.Boxes) objects[box.Key].position = Position(box.Value);
        }

        static Vector3 Position(Vector2Int cell) => new Vector3(cell.x, 0, cell.y);

        void Update()
        {
            if (Run == null || leaving) return;
            if (panels.Modal) { if (Run.PendingItem != null) TickAtmosphere(Time.deltaTime); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { panels.Pause(); return; }
            TickAtmosphere(Time.deltaTime);
            if (Run.Recovering) TickRecovery(Time.deltaTime);
            if (Busy) { Animate(Time.deltaTime); return; }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0) SelectItem((int)Mathf.Sign(wheel));
            if (Input.GetKeyDown(KeyCode.R)) UseItem();

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
            ShowInventory(); Persist(); return true;
        }
        public void SelectItem(int direction)
        {
            if (leaving || panels.Modal || direction == 0) return;
            Run.CycleItem(direction); ShowInventory(); Persist();
        }
        public bool Move(Vector2Int screen)
        {
            if (Busy || leaving || panels.Modal) return false;
            var world = screen;
            for (int i = 0; i < Run.Heading; i++) world = new Vector2Int(world.y, -world.x);
            string oldRoom = Run.ActiveId;
            var previousCell = Run.Player;
            bool wasSolved = Run.Solved;
            if (!Run.Step(world, out string pushed)) return false;
            if (oldRoom != Run.ActiveId)
            {
                BindRoom();
                if (Run.Player != previousCell + world)
                {
                    focus = targetFocus;
                    lens.orthographicSize = targetSize;
                    SetCamera();
                }
                RefreshFeedback();
                ShowInventory();
                Persist();
                return true;
            }
            SyncObjectStates(Run.ActiveId, groups[Run.ActiveId]);
            if (!wasSolved && Run.Solved) ApplySolvedBehaviors();
            playerStart = actor.position;
            movingBox = pushed == null ? null : objects[pushed];
            if (movingBox) boxStart = movingBox.position;
            elapsed = 0;
            duration = .22f;
            Run.Motion = new JObject { ["kind"] = "move", ["fraction"] = 0, ["delta"] = RoomFile.Point(world), ["pushed"] = pushed };
            Persist();
            return true;
        }

        void ApplySolvedBehaviors()
        {
            foreach (var graph in Run.Room["events"])
            {
                string owner = (string)graph["ownerId"];
                if (string.IsNullOrEmpty(owner)) continue;
                JToken[] effects;
                try { effects = ObjectBehavior.Effects(graph).ToArray(); }
                catch (Exception error) { Debug.LogWarning($"对象 {owner} 行为 {graph["id"]} 执行失败 · {error.Message}"); continue; }
                foreach (var effect in effects.Where(e => (string)e["kind"] == "setVisible"))
                {
                    string level = (string)effect["target"]?["levelId"] ?? Run.ActiveId;
                    string id = (string)effect["target"]?["objectId"] ?? owner;
                    if (!groups.TryGetValue(level, out var group)) continue;
                    var design = (JObject)Run.Rooms[level];
                    var tile = design["terrainObjects"]?.FirstOrDefault(t => (string)t["id"] == id);
                    string partName = id;
                    if (tile != null)
                    {
                        var cell = RoomFile.At(tile);
                        partName = (RoomFile.Floor(design,cell) ? "Floor " : "Wall ") + cell;
                    }
                    foreach (Transform part in group)
                        if (part.name == partName)
                            foreach (var mesh in part.GetComponentsInChildren<Renderer>(true)) mesh.enabled = (bool?)effect["visible"] ?? false;
                }
            }
        }
        public void Turn(int direction)
        {
            if (Busy || leaving || panels.Modal || Mathf.Abs(direction) != 1) return;
            turning = true;
            startAngle = angle;
            endAngle = angle + direction * 90;
            elapsed = 0;
            duration = .38f;
            Run.Motion = new JObject { ["kind"] = "turn", ["fraction"] = 0, ["direction"] = direction };
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
                if (movingBox) movingBox.position = Vector3.Lerp(boxStart, boxStart + Position(Run.Player) - playerStart, t);
            }
            UpdateOcclusion();
            if (elapsed < duration) { Run.Motion["fraction"] = elapsed / duration; return; }
            if (turning)
            {
                Run.Heading = (Mathf.RoundToInt(endAngle / 90) % 4 + 4) % 4;
                angle = Run.Heading * 90;
            }
            turning = false;
            movingBox = null;
            duration = 0;
            Run.Motion = null;
            Persist();
            RefreshFeedback();
        }

        void SetCamera()
        {
            lens.transform.rotation = Quaternion.Euler(DioramaLighting.Pitch, angle + DioramaLighting.Yaw, 0);
            lens.transform.position = focus - lens.transform.forward * (lens.orthographicSize * 3 + 10);
        }

        void RefreshFeedback()
        {
            UpdateOcclusion();
            foreach (var door in Run.Room["doors"])
                if ((string)door["role"] == "exit") objects[(string)door["id"]].gameObject.SetActive(!Run.CanApproach(door));
            foreach (var box in Run.Boxes)
            {
                var tint = new MaterialPropertyBlock();
                if (Run.Targets.Contains(box.Value)) tint.SetColor("_BaseColor", new Color(.46f,.74f,.38f));
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
