using System;
using System.Linq;
using Fusion;
using HiAndSee.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HiAndSee.Game
{
    public class TestBotSpawner : MonoBehaviour
    {
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] Transform _spawnCenter;
        [SerializeField] string _groundMeshName = "Ground_Mesh";
        [SerializeField, Range(1, 8)] int _targetTotalPlayers = 4;
        [SerializeField] float _spawnRadius = 3.4f;
        [SerializeField] float _edgePadding = 1.5f;
        [SerializeField] float _spawnHeightOffset = 1.2f;

        bool _done;
        int _retries;

        public bool Done => _done;

        void Start()
        {
            Invoke(nameof(TrySpawnBots), 0.3f);
        }

        void TrySpawnBots()
        {
            if (_done) return;
            if (SceneManager.GetActiveScene().name != "Game") return;

            var runner = FindFirstObjectByType<NetworkRunner>();
            bool ready = runner != null && runner.IsRunning && runner.SceneManager != null && !runner.SceneManager.IsBusy;
            if (!ready || !runner.IsSharedModeMasterClient)
            {
                Retry();
                return;
            }

            if (_playerPrefab == null)
            {
                Debug.LogWarning("[TestBotSpawner] _playerPrefab 未指派，略過測試 Bot。");
                _done = true;
                return;
            }

            int realPlayerCount = runner.ActivePlayers.Count();
            int spawnedRealPlayers = FindObjectsByType<PlayerNetworkSetup>(FindObjectsSortMode.None)
                .Count(p => p != null && p.Object != null && !p.IsBot && p.Object.InputAuthority != PlayerRef.None);

            if (realPlayerCount == 0 || spawnedRealPlayers < realPlayerCount)
            {
                Retry();
                return;
            }

            int desiredBots = Mathf.Clamp(_targetTotalPlayers - realPlayerCount, 0, 8 - realPlayerCount);
            int existingBots = FindObjectsByType<PlayerNetworkSetup>(FindObjectsSortMode.None)
                .Count(p => p != null && p.IsBot);

            for (int i = existingBots; i < desiredBots; i++)
                SpawnBot(runner, i, desiredBots);

            _done = true;
        }

        void SpawnBot(NetworkRunner runner, int index, int totalBots)
        {
            Vector3 pos = GetSpawnPosition(index, totalBots);
            var center = _spawnCenter != null ? _spawnCenter.position : Vector3.zero;
            Vector3 look = center - pos;
            look.y = 0f;
            Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

            try
            {
                var bot = runner.Spawn(_playerPrefab, pos, rot, PlayerRef.None);
                var setup = bot != null ? bot.GetComponent<PlayerNetworkSetup>() : null;
                if (setup != null) setup.ConfigureBot(index + 1);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TestBotSpawner] Bot 生成失敗：" + e.Message);
            }
        }

        Vector3 GetSpawnPosition(int index, int totalBots)
        {
            if (GroundSpawnArea.TryGetRandomPoint(_groundMeshName, _spawnHeightOffset, _edgePadding, out var point))
                return point;

            Vector3 center = _spawnCenter != null ? _spawnCenter.position : Vector3.zero;
            float angle = Mathf.PI * 2f * index / Mathf.Max(1, totalBots);
            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _spawnRadius;
        }

        void Retry()
        {
            if (_retries++ < 100) Invoke(nameof(TrySpawnBots), 0.2f);
            else _done = true;
        }
    }
}
