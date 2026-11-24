/**
 * @file PuzzleAtmosphere.cs
 * @brief InitAtmosphere 创建每房云层并缓存原材质。EnterAtmosphere 按已访问集合显示模型并从入口揭雾。
 * TickAtmosphere 插值镜头、背景与光色。UpdateOcclusion 从关键格沿观察方向检测墙段。
 * ShowInventory、AcceptDiscovery 显示和确认道具。TickRecovery 按剩余时长更新演出并同步归位。
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
        Material translucent;
        DioramaLighting lighting;
        Color theme;
        void InitAtmosphere()
        {
            surface = new MaterialPropertyBlock();
            translucent = Resources.Load<Material>("LayoutModels/GhostSurface");
            lighting = new DioramaLighting(lens);
            foreach (var pair in groups)
            {
                foreach (var r in pair.Value.GetComponentsInChildren<Renderer>(true)) originals[r] = r.sharedMaterial;
                var size = RoomFile.Size((Newtonsoft.Json.Linq.JObject)Run.Rooms[pair.Key]);
                var cloud = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cloud.name = pair.Key + " · 云雾";
                Destroy(cloud.GetComponent<Collider>());
                cloud.transform.SetParent(transform, false);
                cloud.transform.position = pair.Value.position + new Vector3((size.x-1)*.5f,.7f,(size.y-1)*.5f);
                cloud.transform.rotation = Quaternion.Euler(90,0,0);
                cloud.transform.localScale = new Vector3(size.x+1,size.y+1,1);
                var rCloud = cloud.GetComponent<MeshRenderer>();
                rCloud.sharedMaterial = new Material(Resources.Load<Shader>("RoomMist"));
                rCloud.shadowCastingMode = ShadowCastingMode.Off;
                clouds.Add(pair.Key,rCloud);
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
                    if (r.sharedMaterial == translucent) r.sharedMaterial = mat;
                    surface.Clear();
                    if (!active && mat.HasProperty("_BaseColor")) surface.SetColor("_BaseColor",mat.GetColor("_BaseColor")*.64f);
                    r.SetPropertyBlock(surface);
                }
            }
            if (clearing)
            {
                var p = actor.position - clearing.transform.position;
                clearing.sharedMaterial.SetVector("_Entry",new Vector4(.5f+p.x/clearing.transform.localScale.x,.5f+p.z/clearing.transform.localScale.y,0,0));
                clearing.sharedMaterial.SetFloat("_Reveal",0);
            }
            string id = (string)Run.Room["themeId"];
            theme = DioramaLighting.Theme(id);
            if (!hasSkyColor) { lighting.Apply(theme,1); hasSkyColor=true; }
        }
        bool hasSkyColor;
        void TickAtmosphere(float seconds)
        {
            float t = 1-Mathf.Exp(-4*seconds);
            focus = Vector3.Lerp(focus,targetFocus,t);
            lens.orthographicSize = Mathf.Lerp(lens.orthographicSize,targetSize,t);
            lighting.Apply(theme,t);
            SetCamera();
            if (!clearing) return;
            reveal = Mathf.Min(1,reveal+seconds/.95f);
            clearing.sharedMaterial.SetFloat("_Reveal",reveal);
            if (reveal == 1) { clearing.enabled = false; clearing = null; }
        }
        void UpdateOcclusion()
        {
            var loads = Run.Boxes.Keys.Select(key => objects[key].position);
            if (Run.Recovering) loads = loads.Concat(Run.Boxes.Keys.Select(key => Position(Run.RecoveryCell(key))));
            var points = loads.Concat(Run.Targets.Select(Position)).Append(actor.position).Select(p => p+Vector3.up*.24f).ToArray();
            foreach (var wall in boundaryWalls)
            {
                var bounds = new Bounds(wall.position + Vector3.up*.58f,new Vector3(.97f,1.16f,.97f));
                bool occludes = points.Any(point => bounds.IntersectRay(new Ray(point,-lens.transform.forward)));
                foreach (var r in wall.GetComponentsInChildren<Renderer>())
                {
                    var mat = originals[r];
                    r.sharedMaterial = occludes ? translucent : mat;
                    surface.Clear();
                    if (occludes) { var color = mat.GetColor("_BaseColor"); color.a=.23f; surface.SetColor("_BaseColor",color); }
                    r.SetPropertyBlock(surface);
                }
            }
        }
        string ItemName => (string)Run.Rooms[Run.SelectedItem]["name"] + "恢复装置";
        void ShowInventory()
        {
            panels.itemLabel.text = ItemName + (Run.Recovering ? "　归位中" : "　滚轮切换 · R 使用");
            if (Run.PendingItem != null) panels.ShowDiscovery("获得「" + ItemName + "」\n\n在本房按 R，使箱子回到预设位置。\n滚轮切换已获得的装置，已完成的探索会保留。");
        }
        public void AcceptDiscovery() { Run.AcceptItem(); Persist(); }
        WarehouseLift liftRig;
        readonly Dictionary<string,FactoryBelt> belts = new Dictionary<string,FactoryBelt>();

        readonly Dictionary<string,ForestRecovery> forestEffects = new Dictionary<string,ForestRecovery>();
        void TickRecovery(float seconds)
        {
            if (Run.AdvanceRecovery(seconds))
            {
                foreach (var box in Run.Boxes) { objects[box.Key].position=Position(box.Value); objects[box.Key].localScale=Vector3.one; }
                liftRig?.Clear(); liftRig = null;
                foreach (var belt in belts.Values) belt.Clear();
                belts.Clear();
                foreach (var forest in forestEffects.Values) forest.Clear();
                forestEffects.Clear();
                actor.localScale=Vector3.one;
                if (!turning && Busy) { duration=0; movingBox=null; Run.Motion=null; }
                actor.position=Position(Run.Player);
                RefreshFeedback(); ShowInventory(); Persist();
                return;
            }
            float fraction = 1 - Run.RecoveryRemaining / PuzzleRun.RecoveryDuration;
            if ((string)Run.Room["themeId"] == "warehouse" && objects.TryGetValue("主题模型",out var truck))
            {
                var moved = Run.Boxes.Where(b => b.Value != Run.RecoveryCell(b.Key)).OrderBy(b => b.Key,System.StringComparer.Ordinal).ToArray();
                if (moved.Length > 0)
                {
                    float progress = fraction*moved.Length;
                    int index = Mathf.Min((int)progress,moved.Length-1);
                    for (int i=0; i<index; i++) objects[moved[i].Key].position = Position(Run.RecoveryCell(moved[i].Key));
                    var box = moved[index];
                    if (liftRig == null) liftRig = new WarehouseLift(truck);
                    liftRig.Sample(objects[box.Key],Position(box.Value),Position(Run.RecoveryCell(box.Key)),progress-index);
                }
                actor.localScale = Run.PlayerOnRecoveryCell ? Vector3.one*Mathf.Clamp01(Run.RecoveryRemaining/.22f) : Vector3.one;
                return;
            }
            if ((string)Run.Room["themeId"] == "factory")
            {
                foreach (var box in Run.Boxes)
                {
                    var home = Run.RecoveryCell(box.Key);
                    if (box.Value == home) continue;
                    if (!belts.TryGetValue(box.Key,out var belt))
                    {
                        belt = new FactoryBelt(groups[Run.ActiveId],Position(box.Value),Position(home));
                        belts.Add(box.Key,belt);
                    }
                    belt.Sample(objects[box.Key],fraction);
                }
                actor.localScale = Run.PlayerOnRecoveryCell ? Vector3.one*Mathf.Clamp01(Run.RecoveryRemaining/.22f) : Vector3.one;
                return;
            }
            if ((string)Run.Room["themeId"] == "forest")
            {
                foreach (var box in Run.Boxes)
                {
                    var home = Run.RecoveryCell(box.Key);
                    if (box.Value == home) continue;
                    if (!forestEffects.TryGetValue(box.Key,out var forest))
                    {
                        forest = new ForestRecovery(groups[Run.ActiveId],objects[box.Key],Position(home));
                        forestEffects.Add(box.Key,forest);
                    }
                    forest.Sample(fraction);
                }
                float clearance = Mathf.SmoothStep(1,.18f,Mathf.InverseLerp(.57f,.72f,fraction));
                actor.localScale = Run.PlayerOnRecoveryCell ? Vector3.one * Mathf.Min(clearance, Mathf.Clamp01(Run.RecoveryRemaining/.22f)) : Vector3.one;
                return;
            }
            foreach (var box in Run.Boxes)
            {
                var part = objects[box.Key];
                float lift = Mathf.Sin(Mathf.PI*fraction);
                part.position = Vector3.Lerp(Position(box.Value),Position(Run.RecoveryCell(box.Key)),Mathf.SmoothStep(0,1,fraction)) + Vector3.up*lift*.8f;
                part.localScale = Vector3.one * (1 - .55f*lift);
            }
            if (Run.PlayerOnRecoveryCell) actor.localScale = Vector3.one*Mathf.Clamp01(Run.RecoveryRemaining/.22f);
            else actor.localScale = Vector3.one;
        }
        void OnDestroy()
        {
            lighting?.Dispose();
            foreach (var r in clouds.Values) if (r) Destroy(r.sharedMaterial);
        }
    }
}
