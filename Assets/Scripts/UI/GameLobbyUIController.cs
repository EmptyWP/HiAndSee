using System.Linq;
using Fusion;
using HiAndSee.Game;
using HiAndSee.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace HiAndSee.UI
{
    /// <summary>
    /// GameLobby 準備大廳 HUD：
    /// - 預設只顯示少量房間資訊與按鍵提示。
    /// - Tab 才開啟完整大廳頁面。
    /// - 頁面開啟時顯示游標；關閉後鎖定游標回到第一人稱操作。
    /// - 房主開始後同步載入 Game 場景，局內玩法由 Game 場景接手。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameLobbyUIController : MonoBehaviour
    {
        public static GameLobbyUIController Instance { get; private set; }

        [SerializeField] string _gameScenePath = "Assets/Scenes/Game.unity";

        Label _hudPlayers, _roomLabel, _playersCount;
        VisualElement _panel, _playersList;
        Button _startBtn;
        NetworkRunner _runner;
        float _nextPoll;
        bool _panelShown;
        bool _loadingGame;

        void OnEnable()
        {
            Instance = this;
            var root = GetComponent<UIDocument>().rootVisualElement;
            _hudPlayers = root.Q<Label>("hud-players");
            _roomLabel = root.Q<Label>("room-label");
            _playersCount = root.Q<Label>("players-count");
            _panel = root.Q<VisualElement>("lobby-panel");
            _playersList = root.Q<VisualElement>("players-list");
            _startBtn = root.Q<Button>("start-btn");
            if (_startBtn != null) _startBtn.clicked += OnStart;
            SetPanel(false);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_startBtn != null) _startBtn.clicked -= OnStart;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) SetPanel(!_panelShown);
                if (_panelShown && kb.enterKey.wasPressedThisFrame) OnStart();
            }

            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + 0.3f;
            Refresh();
        }

        void Refresh()
        {
            if (_runner == null || !_runner.IsRunning) _runner = FindFirstObjectByType<NetworkRunner>();
            if (_runner == null || !_runner.IsRunning) return;

            int cur = _runner.ActivePlayers.Count();
            int max = _runner.SessionInfo.IsValid && _runner.SessionInfo.MaxPlayers > 0
                ? _runner.SessionInfo.MaxPlayers : cur;
            string room = _runner.SessionInfo.IsValid ? _runner.SessionInfo.Name : "—";
            string playerText = "玩家 " + cur + " / " + max;
            string readyText = "準備 " + cur + " / " + max;

            if (_hudPlayers != null) _hudPlayers.text = playerText;
            if (_playersCount != null) _playersCount.text = readyText;
            if (_roomLabel != null) _roomLabel.text = "房間 " + room;
            RebuildList();
        }

        void RebuildList()
        {
            if (_playersList == null) return;
            _playersList.Clear();
            var players = FindObjectsByType<PlayerNetworkSetup>(FindObjectsSortMode.None);
            if (players.Length == 0)
            {
                AddRow("（等待玩家…）");
                return;
            }
            foreach (var p in players)
            {
                string nick = p.Nick.ToString();
                if (string.IsNullOrEmpty(nick)) nick = "玩家";
                if (p.HasInputAuthority) nick += "（你）";
                if (p.Role == HiAndSeeRole.Ghost) nick += " / 公開：鬼";
                if (p.IsCaptured) nick += " / 牢籠";
                AddRow("• " + nick);
            }
        }

        void AddRow(string text)
        {
            var row = new Label(text);
            row.AddToClassList("player-row");
            _playersList.Add(row);
        }

        void OnStart()
        {
            if (_loadingGame) return;

            if (_runner == null || !_runner.IsRunning)
                _runner = FindFirstObjectByType<NetworkRunner>();

            if (_runner != null && _runner.IsRunning)
            {
                if (!_runner.IsSharedModeMasterClient)
                {
                    if (_startBtn != null) _startBtn.text = "等待房主開始";
                    return;
                }

                _loadingGame = true;
                if (_startBtn != null) _startBtn.text = "載入遊戲...";
                _runner.LoadScene(_gameScenePath, LoadSceneMode.Single, LocalPhysicsMode.None, true);
            }
            else
            {
                _loadingGame = true;
                SceneManager.LoadScene(_gameScenePath);
            }

            SetPanel(false);
        }

        void SetPanel(bool show)
        {
            _panelShown = show;
            if (_panel != null) _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            UnityEngine.Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = show;
        }
    }
}
