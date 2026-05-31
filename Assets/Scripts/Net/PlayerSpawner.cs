using System;
using Fusion;
using UnityEngine;

namespace HiAndSee.Net
{
    /// <summary>
    /// 放在 GameLobby 場景。場景載入後，為本機玩家生成角色。
    /// Shared 模式：每個 client 各自生成自己的角色（取得 input/state authority）。
    /// runner 是 DontDestroyOnLoad，會在場景切換後留存，所以由場景內的 spawner 負責生成最穩。
    /// 重要：要等 runner 執行中且 SceneManager 不忙，同步 Spawn 才不會因 prefab 尚未載入而拋例外。
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] NetworkObject _playerPrefab;
        [SerializeField] Transform _spawnPoint;

        bool _done;
        int _retries;

        void Start() => TrySpawn();

        void TrySpawn()
        {
            if (_done) return;

            var runner = FindFirstObjectByType<NetworkRunner>();
            bool ready = runner != null && runner.IsRunning
                         && runner.SceneManager != null && !runner.SceneManager.IsBusy;

            if (!ready)
            {
                if (_retries++ < 100) Invoke(nameof(TrySpawn), 0.2f); // 等就緒，最多 ~20s
                return;
            }

            if (_playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] _playerPrefab 未指派");
                return;
            }

            Vector3 pos = _spawnPoint != null ? _spawnPoint.position : new Vector3(0f, 1.2f, 0f);
            pos += new Vector3(UnityEngine.Random.Range(-2.5f, 2.5f), 0f, UnityEngine.Random.Range(-2.5f, 2.5f));

            try
            {
                runner.Spawn(_playerPrefab, pos, Quaternion.identity, runner.LocalPlayer);
                _done = true; // 成功才標記完成
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PlayerSpawner] spawn 重試：" + e.Message);
                if (_retries++ < 100) Invoke(nameof(TrySpawn), 0.2f);
            }
        }
    }
}
