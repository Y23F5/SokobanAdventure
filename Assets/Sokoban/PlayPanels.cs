/**
 * @file PlayPanels.cs
 * @brief ShowPlay、Pause、Settings、Help 和 Confirm 选择当前 Canvas 面板并记住返回页。
 * Back 和 Update 处理按钮及 Esc 的同帧输入。SetVolume、SetFullscreen 即时应用并持久化设置。
 * Accept 与 Leave 分派已确认的新局或退出动作；ShowError 在当前宿主上报告失败。
 * ShowDiscovery 接收标题与说明两段文字，标题写入 discoveryTitle，说明写入 discoveryText 后显示获得道具页；只传说明的重载使用默认标题。
 * FocusPage 为菜单和模态面板提供初始键盘焦点，游玩 HUD 不占用导航。
 */
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Sokoban
{
    public sealed class PlayPanels : MonoBehaviour
    {
        public GameObject hud, pause, settings, help, confirm, error;
        public GameObject discovery;
        public RawImage roomArtwork;
        Texture2D roomTexture;
        public void ShowRoomArtwork(Sokoban.Core.AssetDescriptor asset)
        {
            if(!roomArtwork) return;
                        if(roomTexture) Destroy(roomTexture);
            roomTexture=asset==null ? null : ImportedAppearance.ReadImage(asset);
            roomArtwork.texture=roomTexture;
            roomArtwork.gameObject.SetActive(roomTexture);
        }
        void OnDestroy() { if(roomTexture) Destroy(roomTexture); }
        public Text itemLabel, discoveryText, discoveryTitle;
        public Text exitLabel, errorText, volumeLabel;
        public Slider volume;
        public Toggle fullscreen;
        public MainMenuNavigation menu;
        public PuzzleHost host;
        GameObject current;
        int changedFrame = -1;
        bool information;
        GameObject informationReturn;
        public bool Modal => current || changedFrame == Time.frameCount;

        void Awake()
        {
            AudioListener.volume = PlayerPrefs.GetFloat("MasterVolume", 1);
            if (PlayerPrefs.HasKey("Fullscreen"))
            {
                bool value = PlayerPrefs.GetInt("Fullscreen") != 0;
                if (Screen.fullScreen != value) Screen.fullScreen = value;
            }
            Show(null);
        }

        void Show(GameObject page)
        {
            current = page;
            foreach (var panel in new[] { pause, settings, help, confirm, error, discovery })
                if (panel) panel.SetActive(panel == page);
            hud.SetActive(host && !page);
            if (menu && menu.continueButton)
            {
                var board = menu.continueButton.transform.parent.gameObject;
                var group = board.GetComponent<CanvasGroup>();
                if (!group) group = board.AddComponent<CanvasGroup>();
                group.interactable = !page;
                group.blocksRaycasts = !page;
            }
            FocusPage(page ? page : menu && menu.continueButton ? menu.continueButton.transform.parent.gameObject : null);
            changedFrame = Time.frameCount;
        }

        internal static void FocusPage(GameObject page)
        {
            if (!EventSystem.current) return;
            EventSystem.current.SetSelectedGameObject(null);
            if (!page) return;
            foreach (var button in page.GetComponentsInChildren<Button>())
                if (button.IsInteractable() && button.navigation.mode != Navigation.Mode.None)
                {
                    EventSystem.current.SetSelectedGameObject(button.gameObject);
                    break;
                }
        }

        public void ShowPlay(bool trial)
        {
            exitLabel.text = trial ? "结束试玩并返回编辑" : "保存并返回主菜单";
            Show(null);
        }
        public void Pause() => Show(pause);
        public void Confirm() => Show(confirm);
        public void Help()
        {
            var meta = host ? host.Run?.Room.Meta : null;
            if (meta == null || (meta.Description.Length == 0 && meta.PlayHint.Length == 0)) { Show(help); return; }
            string text = string.Join("\n\n", new[] { meta.Description, meta.PlayHint }.Where(value => value.Length > 0));
            ShowInformation(meta.Name, text, pause);
        }
        public void Settings()
        {
            Show(settings);
            volume.SetValueWithoutNotify(AudioListener.volume);
            volumeLabel.text = Mathf.RoundToInt(AudioListener.volume * 100) + "%";
            fullscreen.SetIsOnWithoutNotify(Screen.fullScreen);
        }
        public void SetVolume(float value)
        {
            AudioListener.volume = value;
            volumeLabel.text = Mathf.RoundToInt(value * 100) + "%";
            PlayerPrefs.SetFloat("MasterVolume", value);
            PlayerPrefs.Save();
        }
        public void SetFullscreen(bool value)
        {
            Screen.fullScreen = value;
            PlayerPrefs.SetInt("Fullscreen", value ? 1 : 0);
            PlayerPrefs.Save();
        }
        public void ShowError(string message) { errorText.text = message; Show(error); }
        public void ShowDiscovery(string message) => ShowDiscovery("获得道具", message);
        public void ShowInformation(string title, string message, GameObject returnTo = null)
        {
            ShowDiscovery(title, message);
            information = true;
            informationReturn = returnTo;
        }

        public void ShowDiscovery(string title, string message)
        {
            information = false;
            if (discoveryTitle) discoveryTitle.text = title;
            discoveryText.text = message;
            Show(discovery);
        }
        public void Back()
        {
            if (discovery && current == discovery)
            {
                bool acceptDiscovery = !information;
                Show(acceptDiscovery ? null : informationReturn);
                if (acceptDiscovery) host.AcceptDiscovery();
            }
            else Show(host && current != pause ? pause : null);
        }
        public void Accept() { if (menu) menu.NewCampaign(); }
        public void Leave() { if (host) host.Leave(); }
        void Update()
        {
            if (current && changedFrame != Time.frameCount && Input.GetKeyDown(KeyCode.Escape)) Back();
        }
    }
}
