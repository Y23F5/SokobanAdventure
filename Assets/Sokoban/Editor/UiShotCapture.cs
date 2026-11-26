/**
 * @file UiShotCapture.cs
 * @brief Run 打开主菜单场景并进入 Play Mode；Tick 逐帧推进协程，Capture 用临时相机把界面离屏渲染到 1100x720 并依次截取游戏页 game-01 到 game-08（主菜单、HUD、暂停、设置、操作说明、确认覆盖、获得道具、错误提示），文件名与页面截图目录一致，保存到 Logs/UiShots 后退出批处理。编辑器页 game-09 到 15 与 editor-01 到 05 属 RuntimeEditor 场景，尚未接入。
 * Shot 渲染到 RenderTexture 后读像素存 PNG。只在运行时临时把 Canvas 切到相机渲染，退出 Play Mode 即丢弃，不修改任何场景、预制件或资源。
 */
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class UiShotCapture
    {
        const string Key = "UiShot.Active";
        const int W = 1100, H = 720;
        static IEnumerator routine;
        static int frame = -1;

        public static void Run()
        {
            Directory.CreateDirectory(Path.GetFullPath("Logs/UiShots"));
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
            try
            {
                if (routine == null) routine = Capture();
                if (routine.MoveNext()) return;
                Debug.Log("UISHOT PASS");
                SessionState.EraseBool(Key);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                SessionState.EraseBool(Key);
                EditorApplication.Exit(1);
            }
        }

        static void Shot(Camera cam, string name)
        {
            Canvas.ForceUpdateCanvases();
            var rt = RenderTexture.GetTemporary(W, H, 24);
            var image = new Texture2D(W, H, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            string path = Path.GetFullPath("Logs/UiShots/" + name + ".png");
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(image);
            }
            if (!File.Exists(path)) throw new InvalidOperationException("截图未生成：" + name);
            Debug.Log("UISHOT saved " + name);
        }

        static IEnumerator Capture()
        {
            for (int i = 0; i < 10; i++) yield return null;
            var panels = UnityEngine.Object.FindObjectOfType<PlayPanels>();
            if (!panels) throw new InvalidOperationException("主菜单场景里没有 PlayPanels");

            var camObject = new GameObject("UiShotCamera");
            var cam = camObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(159, 176, 138, 255);
            cam.orthographic = true;
            cam.cullingMask = -1;
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1;
            }
            Debug.Log("UISHOT target " + W + "x" + H);
            for (int i = 0; i < 5; i++) yield return null;

            Shot(cam, "game-01-主菜单");

            var board = panels.menu && panels.menu.continueButton ? panels.menu.continueButton.transform.parent.gameObject : null;
            if (board) board.transform.parent.gameObject.SetActive(false);

            panels.ShowPlay(false);
            panels.hud.SetActive(true);
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-02-游玩HUD");

            panels.Pause();
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-03-暂停");

            panels.Settings();
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-04-设置");

            panels.Help();
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-05-操作说明");

            panels.Confirm();
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-06-确认覆盖");

            panels.ShowPlay(false);
            panels.hud.SetActive(false);
            var session = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("PuzzleSession"));
            for (int i = 0; i < 5; i++) yield return null;
            var sessionPanels = session.GetComponentInChildren<PlayPanels>(true);
            if (!sessionPanels || !sessionPanels.discovery) throw new InvalidOperationException("PuzzleSession 预制件里的 PlayPanels 没有绑定获得道具页");
            foreach (var canvas in session.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1;
            }
            sessionPanels.ShowDiscovery("获得：仓库恢复装置", "按 R 让当前房间的箱子回到初始位置。");
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-07-获得道具");
            UnityEngine.Object.Destroy(session);
            for (int i = 0; i < 2; i++) yield return null;

            panels.ShowError("请从主菜单开始游戏，或从编辑器试玩房间。");
            for (int i = 0; i < 5; i++) yield return null;
            Shot(cam, "game-08-错误提示");
        }
    }
}
