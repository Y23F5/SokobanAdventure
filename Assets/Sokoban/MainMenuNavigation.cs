/**
 * @file MainMenuNavigation.cs
 * @brief Start 核验继续按钮。StartGame 在存在存档时询问覆盖，NewCampaign 和 ContinueGame
 * 将已初始化的游戏状态传给 Game。OpenEditor、Load 与 Quit 负责单次场景导航或退出。
 */
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;

namespace Sokoban
{
    public sealed class MainMenuNavigation : MonoBehaviour
    {
        bool loading;
        public Button continueButton;
        public PlayPanels panels;
        void Start() { continueButton.interactable = CampaignSlot.Available(); }
        public void StartGame()
        {
            if (File.Exists(CampaignSlot.PathName)) panels.Confirm(); else NewCampaign();
        }
        public void NewCampaign() => Enter(false);
        public void ContinueGame() => Enter(true);
        void Enter(bool resume)
        {
            if (loading) return;
            try
            {
                PuzzleHost.MenuRequest = resume ? CampaignSlot.Load() : PuzzleRun.Campaign();
                Load("Game");
            }
            catch (System.Exception error) { PuzzleHost.MenuRequest = null; panels.ShowError(error.Message); }
        }
        public void OpenEditor() => Load("RuntimeEditor");
        public void Quit() => Application.Quit();
        void Load(string scene)
        {
            if (loading) return;
            loading = true;
            SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
        }
    }
}
