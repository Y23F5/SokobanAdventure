/**
 * @file PlayPanels.cs
 * @brief ShowPlay、Pause、Settings、Help 和 Confirm 选择当前 Canvas 面板并记住返回页。
 * Back 和 Update 处理按钮及 Esc 的同帧输入。SetVolume、SetFullscreen 即时应用并持久化设置。
 * Accept 与 Leave 分派已确认的新局或退出动作；ShowError 在当前宿主上报告失败。
 * FocusPage 为菜单和模态面板提供初始键盘焦点，游玩 HUD 不占用导航。
 */
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
        public void ShowRoomArtwork(Newtonsoft.Json.Linq.JObject room)
        {
            if(!roomArtwork) return;
            string id=(string)room["uiImage"];
            if(roomTexture) Destroy(roomTexture);
            roomTexture=id==null ? null : ImportedAppearance.ReadImage(room["assets"][id]);
            roomArtwork.texture=roomTexture;
            roomArtwork.gameObject.SetActive(roomTexture);
        }
        void OnDestroy() { if(roomTexture) Destroy(roomTexture); }
        public Text itemLabel, discoveryText;
        public Text exitLabel, errorText, volumeLabel;
        public Slider volume;
        public Toggle fullscreen;
        public MainMenuNavigation menu;
        public PuzzleHost host;
        GameObject current;
        int changedFrame = -1;
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
        public void Help() => Show(help);
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
        public void ShowDiscovery(string message) { discoveryText.text = message; Show(discovery); }
        public void Back()
        {
            if (discovery && current == discovery) { host.AcceptDiscovery(); Show(null); }
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
