using HiAndSee.Net;
using UnityEngine;
using UnityEngine.UIElements;

namespace HiAndSee.UI
{
    /// <summary>
    /// 主選單（UI Toolkit）。三畫面切換：主選單 / 連線 / 設定。
    /// 連線按鈕接上 FusionConnectionService（Shared 模式 → 載入 GameLobby）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        const string NicknameKey = "mp_nickname";
        const string SessionKey = "mp_session";

        FusionConnectionService _connection;

        VisualElement _menuScreen, _connectScreen, _settingsScreen;
        Button _playBtn, _settingsBtn, _quitBtn, _soundBtn;
        Button _connectBack, _settingsBack, _hostBtn, _joinBtn, _minusBtn, _plusBtn, _saveBtn;
        TextField _roomField, _nameField;
        Label _maxLabel, _connectStatus, _profileName;
        Slider _musicSlider, _sfxSlider;
        Toggle _fullscreenToggle;

        int _maxPlayers = 4;
        bool _muted;

        void Awake()
        {
            _connection = GetComponent<FusionConnectionService>();
            if (_connection == null) _connection = gameObject.AddComponent<FusionConnectionService>();
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            _menuScreen = root.Q<VisualElement>("menu-screen");
            _connectScreen = root.Q<VisualElement>("connect-screen");
            _settingsScreen = root.Q<VisualElement>("settings-screen");

            _playBtn = root.Q<Button>("play-btn");
            _settingsBtn = root.Q<Button>("settings-btn");
            _quitBtn = root.Q<Button>("quit-btn");
            _soundBtn = root.Q<Button>("sound-btn");
            _connectBack = root.Q<Button>("connect-back");
            _settingsBack = root.Q<Button>("settings-back");
            _hostBtn = root.Q<Button>("host-btn");
            _joinBtn = root.Q<Button>("join-btn");
            _minusBtn = root.Q<Button>("minus-btn");
            _plusBtn = root.Q<Button>("plus-btn");
            _saveBtn = root.Q<Button>("save-btn");

            _roomField = root.Q<TextField>("room-field");
            _nameField = root.Q<TextField>("name-field");
            _maxLabel = root.Q<Label>("max-label");
            _connectStatus = root.Q<Label>("connect-status");
            _profileName = root.Q<Label>("profile-name");
            _musicSlider = root.Q<Slider>("music-slider");
            _sfxSlider = root.Q<Slider>("sfx-slider");
            _fullscreenToggle = root.Q<Toggle>("fullscreen-toggle");

            // 還原偏好
            string nick = PlayerPrefs.GetString(NicknameKey, "小探險家 " + Random.Range(1000, 9999));
            if (_nameField != null) _nameField.value = nick;
            if (_profileName != null) _profileName.text = nick;
            if (_roomField != null) _roomField.value = PlayerPrefs.GetString(SessionKey, "Room1");

            Bind(_playBtn, () => ShowScreen(_connectScreen));
            Bind(_settingsBtn, () => ShowScreen(_settingsScreen));
            Bind(_quitBtn, Quit);
            Bind(_soundBtn, ToggleSound);
            Bind(_connectBack, () => ShowScreen(_menuScreen));
            Bind(_settingsBack, () => ShowScreen(_menuScreen));
            Bind(_hostBtn, StartConnect);
            Bind(_joinBtn, StartConnect);
            Bind(_minusBtn, () => StepMax(-1));
            Bind(_plusBtn, () => StepMax(1));
            Bind(_saveBtn, SaveSettings);

            _connection.StateChanged += OnStateChanged;
            _connection.ErrorOccurred += OnError;

            ShowScreen(_menuScreen);
        }

        void OnDisable()
        {
            if (_connection != null)
            {
                _connection.StateChanged -= OnStateChanged;
                _connection.ErrorOccurred -= OnError;
            }
        }

        static void Bind(Button b, System.Action cb) { if (b != null) b.clicked += cb; }

        void ShowScreen(VisualElement target)
        {
            SetVisible(_menuScreen, target == _menuScreen);
            SetVisible(_connectScreen, target == _connectScreen);
            SetVisible(_settingsScreen, target == _settingsScreen);
        }

        static void SetVisible(VisualElement e, bool v)
        {
            if (e != null) e.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void StepMax(int d)
        {
            _maxPlayers = Mathf.Clamp(_maxPlayers + d, 1, 8);
            if (_maxLabel != null) _maxLabel.text = _maxPlayers.ToString();
        }

        void StartConnect()
        {
            string room = _roomField != null ? _roomField.value : "Room1";
            string nick = _nameField != null && !string.IsNullOrEmpty(_nameField.value) ? _nameField.value : "Player";
            PlayerPrefs.SetString(SessionKey, room);
            PlayerPrefs.SetString(NicknameKey, nick);
            PlayerPrefs.Save();
            if (_connectStatus != null) _connectStatus.text = "連線中…";
            _connection.Connect(room, _maxPlayers, nick);
        }

        void OnStateChanged(ConnectionState state)
        {
            if (_connectStatus == null) return;
            if (state == ConnectionState.Connecting) _connectStatus.text = "正在前往營地…";
            else if (state == ConnectionState.Connected) _connectStatus.text = "已連線，載入 GameLobby…";
            else _connectStatus.text = "準備好就出發！";
        }

        void OnError(string message)
        {
            if (_connectStatus != null) _connectStatus.text = message;
            Debug.LogWarning("[MainMenu] " + message);
        }

        void ToggleSound()
        {
            _muted = !_muted;
            AudioListener.volume = _muted ? 0f : 1f;
            if (_soundBtn != null) _soundBtn.text = _muted ? "x" : "♪";
        }

        void SaveSettings()
        {
            if (_nameField != null)
            {
                PlayerPrefs.SetString(NicknameKey, _nameField.value);
                if (_profileName != null) _profileName.text = _nameField.value;
            }
            if (_fullscreenToggle != null) Screen.fullScreen = _fullscreenToggle.value;
            PlayerPrefs.Save();
            ShowScreen(_menuScreen);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
