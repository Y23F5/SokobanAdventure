/**
 * @file PuzzleAtmosphere.cs
 * @brief InitAtmosphere 创建每房云层并缓存原材质。EnterAtmosphere 按已访问集合显示模型并从入口揭雾。
 * TickAtmosphere 插值镜头、背景与光色。UpdateOcclusion 从关键格沿观察方向检测墙段。
 * ShowInventory、AcceptDiscovery 显示和确认道具，道具名取自道具所属房间的显示名。
 * TickRecovery 推进规则会话的恢复时钟；演出完全由 RecoveryPresenter 按恢复道具的脚本取姿态，宿主不按主题名分支；完成时清理零件池并把箱子与玩家同步到规则结果。
 */
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sokoban
{
    public sealed partial class PuzzleHost
    {
        readonly Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        readonly Dictionary<string, MeshRenderer> clouds = new Dictionary<string, MeshRenderer>();
        readonly Dictionary<Renderer, Material> originals = new Dictionary<Renderer, Material>();
        MaterialPropertyBlock surface;
        readonly HashSet<string> shown = new HashSet<string>();
        Vector3 targetFocus;
        float targetSize, reveal;
        bool framed;
        MeshRenderer clearing;
        DioramaLighting lighting;
        Color theme;
        void InitAtmosphere()
        {
            surface = new MaterialPropertyBlock();
            lighting = new DioramaLighting(lens);
            foreach (var pair in groups)
            {
                foreach (var r in pair.Value.GetComponentsInChildren<Renderer>(true)) originals[r] = r.sharedMaterial;
                var room = Run.Rooms[pair.Key].Room;
                var cloud = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cloud.name = pair.Key + " · 云雾";
                Destroy(cloud.GetComponent<Collider>());
                cloud.transform.SetParent(transform, false);
                cloud.transform.position = pair.Value.position + new Vector3((room.Width - 1) * .5f, .7f, (room.Height - 1) * .5f);
                cloud.transform.rotation = Quaternion.Euler(90, 0, 0);
                cloud.transform.localScale = new Vector3(room.Width + 1, room.Height + 1, 1);
                var rCloud = cloud.GetComponent<MeshRenderer>();
                rCloud.sharedMaterial = new Material(Resources.Load<Shader>("RoomMist"));
                rCloud.shadowCastingMode = ShadowCastingMode.Off;
                clouds.Add(pair.Key, rCloud);
                if (Run.Visited(pair.Key)) shown.Add(pair.Key);
            }
        }
        void EnterAtmosphere()
        {
            bool first = shown.Add(Run.ActiveId);
            clearing = first ? clouds[Run.ActiveId] : null;
            reveal = 0;
            foreach (var pair in groups)
            {
                bool active = pair.Key == Run.ActiveId;
                bool visible = Run.Visited(pair.Key);
                pair.Value.gameObject.SetActive(visible);
                var cloud = clouds[pair.Key];
                cloud.enabled = !visible || cloud == clearing;
                if (!visible) continue;
                foreach (var r in pair.Value.GetComponentsInChildren<Renderer>(true))
                {
                    if (!originals.TryGetValue(r, out var mat)) originals[r] = mat = r.sharedMaterial;
                    if (!mat) continue;
                    var materials = r.sharedMaterials;
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        var paint = materials[slot];
                        if (!paint || !paint.HasProperty("_BaseColor")) continue;
                        if (active) r.SetPropertyBlock(null, slot);
                        else
                        {
                            r.GetPropertyBlock(surface);
                            surface.SetColor("_BaseColor", paint.GetColor("_BaseColor") * .64f);
                            r.SetPropertyBlock(surface, slot);
                        }
                    }
                }
            }
            if (clearing)
            {
                var p = actor.position - clearing.transform.position;
                clearing.sharedMaterial.SetVector("_Entry", new Vector4(.5f + p.x / clearing.transform.localScale.x, .5f + p.z / clearing.transform.localScale.y, 0, 0));
                clearing.sharedMaterial.SetFloat("_Reveal", 0);
            }
            theme = DioramaLighting.Theme(Run.Room.Meta.ThemeId);
            if (!hasSkyColor) { lighting.Apply(theme, 1); hasSkyColor = true; }
        }
        bool hasSkyColor;
        void TickAtmosphere(float seconds)
        {
            float t = 1 - Mathf.Exp(-4 * seconds);
            focus = Vector3.Lerp(focus, targetFocus, t);
            lens.orthographicSize = Mathf.Lerp(lens.orthographicSize, targetSize, t);
            lighting.Apply(theme, t);
            SetCamera();
            if (!clearing) return;
            reveal = Mathf.Min(1, reveal + seconds / .95f);
            clearing.sharedMaterial.SetFloat("_Reveal", reveal);
            if (reveal == 1) { clearing.enabled = false; clearing = null; }
        }
        void UpdateOcclusion()
        {
            var room = Run.Room.Room;
            CameraRig.ClipWalls(groups[Run.ActiveId], room.Width, room.Height, CameraRig.Side(angle));
        }
        string ItemName => Run.SelectedItem == null ? "" : Run.ItemRoomName(Run.SelectedItem) + "恢复装置";
        void ShowInventory()
        {
            panels.itemLabel.text = ItemName + (Run.Recovering ? "　归位中" : "　滚轮切换 · R 使用");
            if (Run.PendingItem != null) panels.ShowDiscovery("获得「" + ItemName + "」\n\n在本房按 R，使箱子回到预设位置。\n滚轮切换已获得的装置，已完成的探索会保留。");
        }
        public void AcceptDiscovery() { Run.AcceptItem(); Persist(); }
        void TickRecovery(float seconds)
        {
            if (Run.AdvanceRecovery(seconds))
            {
                presenter.Clear();
                foreach (var box in Run.Boxes) { objects[box.Key].position = Position(box.Value); objects[box.Key].localScale = Vector3.one; }
                SyncObjectStates(Run.ActiveId, groups[Run.ActiveId]);
                actor.localScale = Vector3.one;
                if (!turning && Busy) { duration = 0; movingBox = null; Run.Motion = null; }
                actor.position = Position(Run.Player);
                RefreshFeedback(); ShowInventory(); Persist();
                return;
            }
            float fraction = Run.RecoveryFraction;
            SampleObjectStateRecovery(fraction);
            presenter.Sample(Run, groups[Run.ActiveId], key => objects[key], fraction);
            actor.localScale = Run.PlayerOnClosingCell ? Vector3.one * Mathf.Clamp01(Run.RecoveryRemaining / .22f) : Vector3.one;
        }
        void OnDestroy()
        {
            lighting?.Dispose();
            presenter.Clear();
            if (artwork) Destroy(artwork);
            foreach (var r in clouds.Values) if (r) Destroy(r.sharedMaterial);
        }
    }
}
