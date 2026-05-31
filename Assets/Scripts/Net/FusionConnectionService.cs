using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HiAndSee.Net
{
    /// <summary>連線狀態。</summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Photon Fusion 2 連線服務（Shared 模式）。
    /// 連線成功後由 Fusion 載入 GameLobby 場景給所有人，並為每位玩家生成自己的角色。
    /// 對 UI 只暴露 Connect / Leave 與三個事件。
    /// </summary>
    public class FusionConnectionService : MonoBehaviour, INetworkRunnerCallbacks
    {
        public event Action<ConnectionState> StateChanged;
        public event Action<int, int> PlayersChanged;   // (目前人數, 上限)
        public event Action<string> ErrorOccurred;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public string LocalNickname { get; private set; }

        [Header("生成設定")]
        [Tooltip("玩家角色 prefab（需有 NetworkObject）")]
        [SerializeField] NetworkObject _playerPrefab;
        [Tooltip("連線後載入的場景（需加入 Build Settings）")]
        [SerializeField] string _gameLobbyScenePath = "Assets/Scenes/GameLobby.unity";

        NetworkRunner _runner;
        GameObject _runnerObject;
        int _maxPlayers = 4;
        bool _connecting;
        bool _spawnedLocal;

        /// <summary>開始連線（兩顆按鈕共用：Shared 模式下第一個進的人建房、其餘加入）。</summary>
        public void Connect(string sessionName, int maxPlayers, string nickname)
        {
            if (_connecting) return;
            if (string.IsNullOrWhiteSpace(sessionName))
            {
                ErrorOccurred?.Invoke("請輸入房間名稱");
                return;
            }
            _maxPlayers = Mathf.Max(1, maxPlayers);
            LocalNickname = nickname;
            _connecting = true;
            _spawnedLocal = false;
            SetState(ConnectionState.Connecting);
            StartShared(sessionName);
        }

        public void Leave()
        {
            _connecting = false;
            if (_runner != null && _runner.IsRunning)
                _ = _runner.Shutdown();
            else
                CleanupRunner();
            SetState(ConnectionState.Disconnected);
        }

        async void StartShared(string sessionName)
        {
            CleanupRunner();
            _runnerObject = new GameObject("NetworkRunner");
            _runner = _runnerObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            int buildIndex = SceneUtility.GetBuildIndexByScenePath(_gameLobbyScenePath);
            SceneRef sceneRef = buildIndex >= 0 ? SceneRef.FromIndex(buildIndex) : SceneRef.None;

            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = _maxPlayers,
                Scene = sceneRef,
                SceneManager = _runnerObject.AddComponent<NetworkSceneManagerDefault>(),
            };

            StartGameResult result;
            try
            {
                result = await _runner.StartGame(args);
            }
            catch (Exception e)
            {
                _connecting = false;
                ErrorOccurred?.Invoke("連線發生例外：" + e.Message);
                SetState(ConnectionState.Disconnected);
                CleanupRunner();
                return;
            }

            _connecting = false;
            if (result.Ok)
            {
                SetState(ConnectionState.Connected);
                RaisePlayers();
            }
            else
            {
                var detail = string.IsNullOrEmpty(result.ErrorMessage) ? "" : " (" + result.ErrorMessage + ")";
                ErrorOccurred?.Invoke("連線失敗：" + result.ShutdownReason + detail);
                SetState(ConnectionState.Disconnected);
                CleanupRunner();
            }
        }

        void SpawnLocalPlayer()
        {
            if (_spawnedLocal || _playerPrefab == null || _runner == null) return;
            _spawnedLocal = true;
            var pos = new Vector3(UnityEngine.Random.Range(-3f, 3f), 1.2f, UnityEngine.Random.Range(-3f, 3f));
            _runner.Spawn(_playerPrefab, pos, Quaternion.identity, _runner.LocalPlayer);
        }

        void RaisePlayers()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                PlayersChanged?.Invoke(0, _maxPlayers);
                return;
            }
            int current = _runner.ActivePlayers.Count();
            int max = _runner.SessionInfo.IsValid && _runner.SessionInfo.MaxPlayers > 0
                ? _runner.SessionInfo.MaxPlayers
                : _maxPlayers;
            PlayersChanged?.Invoke(current, max);
        }

        void SetState(ConnectionState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }

        void CleanupRunner()
        {
            if (_runner != null) { _runner.RemoveCallbacks(this); _runner = null; }
            if (_runnerObject != null) { Destroy(_runnerObject); _runnerObject = null; }
        }

        void OnDestroy()
        {
            if (_runner != null) _runner.RemoveCallbacks(this);
        }

        // ---------- INetworkRunnerCallbacks ----------

        // 生成改由 GameLobby 場景內的 PlayerSpawner 負責（本服務會隨 MainMenu 卸載而銷毀）。
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) => RaisePlayers();
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) => RaisePlayers();

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            _connecting = false;
            ErrorOccurred?.Invoke("連線失敗：" + reason);
            SetState(ConnectionState.Disconnected);
        }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            ErrorOccurred?.Invoke("已斷線：" + reason);
            SetState(ConnectionState.Disconnected);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (shutdownReason != ShutdownReason.Ok)
                ErrorOccurred?.Invoke("連線關閉：" + shutdownReason);
            SetState(ConnectionState.Disconnected);
        }

        // 未使用的回呼（留空滿足介面）
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    }
}
