/**
 * @file WS06VLightingVerification.cs
 * @brief Run opens the runtime editor in an empty QA profile. Tick advances Observe once per play frame. Sample reads and checks the live camera, sky and directional light in each view.
 * Observe：默认房间补上箱子、目标与恢复道具后通过发布级校验，再依次检查编辑、试玩、返回编辑和正式局四个视图；房间尺寸取自 Core.Room。
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sokoban.Core;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class WS06VLightingVerification
    {
        const string Key = "WS06VLighting.Active";
        static IEnumerator routine;
        static int frame = -1;
        static int failures;
        public static void Run()
        {
            if (File.Exists(CampaignSlot.PathName) || File.Exists(Path.Combine(Application.persistentDataPath,"runtime-editor-ws02.json")))
                throw new InvalidOperationException("Use an empty QA profile for the default editor and campaign slots.");
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetBool(Key,true);
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() => EditorApplication.update += Tick;
        static void Tick()
        {
            if (!SessionState.GetBool(Key,false) || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                if (routine == null) routine = Observe();
                if (routine.MoveNext()) return;
            }
            catch (Exception ex) { failures++; Debug.LogException(ex); }
            SessionState.EraseBool(Key);
            Debug.Log("LIGHTQA failures=" + failures);
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
        static void Check(bool ok,string message)
        {
            if (ok) Debug.Log("LIGHTQA PASS " + message);
            else { failures++; Debug.LogError("LIGHTQA FAIL " + message); }
        }
        static bool Near(Color a, Color b) => ((Vector4)a - (Vector4)b).sqrMagnitude < .000001f;
        static void Sample(string stage,Vector2Int span,bool editor)
        {
            var c = Camera.main;
            var sky = RenderSettings.skybox;
            var sun = Object.FindObjectsOfType<Light>().Single(l => l.type == LightType.Directional && l.enabled);
            Color tint = sky.GetColor("_Tint"), warehouse = new Color32(218,197,156,255);
            Debug.Log($"LIGHTQA SAMPLE {stage} span={span} aspect={c.aspect:F5} ortho={c.orthographic} size={c.orthographicSize:F5} pos={c.transform.position.ToString("F4")} angles={c.transform.eulerAngles.ToString("F3")} clear={c.clearFlags} background={c.backgroundColor:F5} sky={sky.shader.name} tint={tint:F5} sun={sun.name} rgb={sun.color:F5} intensity={sun.intensity:F3} direction={sun.transform.forward.ToString("F4")} shadows={sun.shadows} strength={sun.shadowStrength:F3}");
            float size = editor ? Mathf.Max(span.y*.63f,span.x*.6f/Mathf.Max(.1f,c.aspect*.74f)) : Mathf.Max(3,Mathf.Max(span.x,span.y)*.69f/Mathf.Min(1,c.aspect));
            Check(c.orthographic && Mathf.Abs(c.orthographicSize-size)<.001f && Quaternion.Angle(c.transform.rotation,Quaternion.Euler(55,32,0))<.05f,stage+" default camera");
            Check(c.clearFlags==CameraClearFlags.Skybox && sky.shader.name=="Sokoban/DioramaSky" && Near(c.backgroundColor,warehouse) && Near(tint,warehouse),stage+" warehouse sky");
            Check(Near(sun.color,Color.Lerp(Color.white,warehouse,.28f)) && sun.shadows==LightShadows.Soft && Mathf.Abs(sun.shadowStrength-.55f)<.001f,stage+" warehouse sun and shadows");
        }
        static IEnumerator Observe()
        {
            yield return null; yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            var draft = editor.Panel.Draft;
            Check(draft.World==null && draft.ActiveId=="blank" && draft.Model.Meta.ThemeId=="warehouse","fresh warehouse template");
            draft.Place(true,new Cell(3,2),"box-qa");
            draft.Place(false,new Cell(4,2),"target-qa");
            draft.Commit(new AddRecoveryItemCommand(draft.Model,new RecoveryItem("recovery-qa","warehouse-lift",RecoveryItem.DefaultDuration)));
            var span = new Vector2Int(draft.Room.Width,draft.Room.Height);
            Sample("editor",span,true);
            var position=Camera.main.transform.position;
            var originalSky=RenderSettings.skybox;
            editor.StartTrial(); yield return null; yield return null;
            var trial = Object.FindObjectOfType<PuzzleHost>();
            Check(trial && trial.IsTrial && trial.Run.ActiveId==draft.ActiveId,"template trial");
            Sample("trial",span,false);
            trial.Leave(); yield return null; yield return null;
            Check(editor.Interface.gameObject.activeInHierarchy && !trial && RenderSettings.skybox==originalSky && Vector3.Distance(Camera.main.transform.position,position)<.001f,"returned editor restores position and original sky material");
            Sample("returned",span,true);
            PuzzleHost.MenuRequest=PuzzleRun.Campaign();
            SceneManager.LoadScene("Game"); yield return null; yield return null; yield return null;
            var game=Object.FindObjectOfType<PuzzleHost>();
            Check(!game.IsTrial && game.Run.ActiveId=="warehouse","campaign warehouse");
            var campaignRoom = game.Run.Room.Room;
            Sample("campaign",new Vector2Int(campaignRoom.Width,campaignRoom.Height),false);
        }
    }
}
