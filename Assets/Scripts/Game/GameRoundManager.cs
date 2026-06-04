using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using HiAndSee.Net;
using HiAndSee.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HiAndSee.Game
{
    [DefaultExecutionOrder(-25)]
    public class GameRoundManager : MonoBehaviour
    {
        public static GameRoundManager Instance { get; private set; }

        [Header("Round")]
        [SerializeField] int totalTasks = 4;
        [SerializeField] float taskHoldSeconds = 2.4f;
        [SerializeField] float rescueHoldSeconds = 2.4f;
        [SerializeField] float captureRange = 3.0f;
        [SerializeField] float interactRange = 3.0f;
        [SerializeField] float sheriffCaptureCooldown = 8f;
        [SerializeField] bool autoStartWhenReady = true;
        [SerializeField] float autoStartDelay = 0.8f;
        [SerializeField] bool makeLocalPlayerGhostWhenTestingBots = true;

        [Header("Scene")]
        [SerializeField] Transform cage;
        [SerializeField] Transform releasePoint;
        [SerializeField] Transform roundSpawnPoint;
        [SerializeField] string groundMeshName = "Ground_Mesh";
        [SerializeField] float spawnEdgePadding = 1.5f;
        [SerializeField] float spawnHeightOffset = 1.2f;
        [SerializeField] GameplayTaskStation[] taskStations;

        [Header("Runtime Visuals")]
        [SerializeField] bool buildRuntimeCageVisual = true;
        [SerializeField] float cageSize = 4.8f;
        [SerializeField] float cageHeight = 2.8f;

        readonly List<GameplayTaskStation> _runtimeTasks = new();
        int _completedTasks;
        bool _roundStarted;
        HiAndSeeRoundResult _result;
        string _prompt = string.Empty;
        float _holdProgress;
        float _autoStartAt = -1f;

        public bool RoundStarted => _roundStarted;
        public int CompletedTasks => _completedTasks;
        public int TotalTasks => totalTasks;
        public float TaskHoldSeconds => taskHoldSeconds;
        public float RescueHoldSeconds => rescueHoldSeconds;
        public float CaptureRange => captureRange;
        public float InteractRange => interactRange;
        public float SheriffCaptureCooldown => sheriffCaptureCooldown;
        public Vector3 CagePosition => cage != null ? cage.position + Vector3.up * 1.1f : Vector3.up * 1.1f;
        public Vector3 ReleasePosition => releasePoint != null ? releasePoint.position : CagePosition + Vector3.forward * 3f;

        public static GameRoundManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<GameRoundManager>();
            if (existing != null) return existing;
            return new GameObject("GameRoundManager").AddComponent<GameRoundManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            ResolveSceneRefs();
            EnsureCageVisual();
            EnsureTaskStations();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            EnsureLocalInteractor();

            if (!_roundStarted && autoStartWhenReady)
                TryAutoStartRound();

            if (!_roundStarted)
            {
                UpdateHud();
                return;
            }

            if (_result == HiAndSeeRoundResult.None)
                CheckGhostWin();

            UpdateHud();
        }

        public void StartRoundFromUi()
        {
            var runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning && !runner.IsSharedModeMasterClient)
            {
                SetPrompt("等待房主開始遊戲");
                return;
            }

            StartRoundAsAuthority();
        }

        public void StartRoundAsAuthority()
        {
            var players = GetPlayers().ToList();
            if (players.Count == 0)
            {
                SetPrompt("等待玩家生成");
                return;
            }

            _completedTasks = 0;
            _result = HiAndSeeRoundResult.None;
            foreach (var task in _runtimeTasks)
                task.MarkOpen();

            AssignRoles(players);
            BroadcastRoundStarted();
            BroadcastTaskProgress(_completedTasks, totalTasks);
        }

        public void ReceiveRoundStarted()
        {
            _roundStarted = true;
            _result = HiAndSeeRoundResult.None;
            SetPrompt("身份已分配，開始遊戲");
        }

        public void ReceiveTaskProgress(int completed, int total)
        {
            _completedTasks = Mathf.Clamp(completed, 0, Mathf.Max(1, total));
            totalTasks = Mathf.Max(1, total);

            if (_completedTasks >= totalTasks)
                BroadcastRoundResult(HiAndSeeRoundResult.HumansWin);
        }

        public void ReceiveRoundResult(HiAndSeeRoundResult result)
        {
            _result = result;
            SetPrompt(result == HiAndSeeRoundResult.HumansWin ? "人類完成任務，準備逃脫" : "鬼陣營抓住所有目標");
        }

        public void SetPrompt(string prompt, float holdProgress = 0f)
        {
            _prompt = prompt;
            _holdProgress = Mathf.Clamp01(holdProgress);
            UpdateHud();
        }

        public GameplayTaskStation GetNearestTask(Vector3 position, float range)
        {
            GameplayTaskStation best = null;
            float bestSqr = range * range;
            foreach (var task in _runtimeTasks)
            {
                if (task == null || task.Completed) continue;
                float sqr = (task.transform.position - position).sqrMagnitude;
                if (sqr > bestSqr) continue;
                best = task;
                bestSqr = sqr;
            }
            return best;
        }

        public bool TryCompleteTask(GameplayTaskStation task, PlayerNetworkSetup actor)
        {
            if (!_roundStarted || task == null || actor == null || task.Completed) return false;
            if (!CanWorkOnTasks(actor.Role)) return false;

            task.MarkCompleted();
            BroadcastTaskProgress(Mathf.Min(_completedTasks + 1, totalTasks), totalTasks);
            SetPrompt("任務完成");
            return true;
        }

        public bool TrySabotageTask(GameplayTaskStation task, PlayerNetworkSetup actor)
        {
            if (!_roundStarted || task == null || actor == null) return false;
            if (actor.Role != HiAndSeeRole.Impostor) return false;

            BroadcastTaskProgress(Mathf.Max(0, _completedTasks - 1), totalTasks);
            SetPrompt("內鬼破壞了任務進度");
            return true;
        }

        public bool TryCapture(PlayerNetworkSetup captor, PlayerNetworkSetup target)
        {
            if (!_roundStarted || captor == null || target == null || target == captor) return false;
            if (target.IsCaptured) return false;
            if (!CanCapture(captor.Role, target.Role)) return false;

            target.RequestCaptureTo(captor.Object.InputAuthority, CagePosition);
            SetPrompt("已抓捕目標");
            Invoke(nameof(CheckGhostWin), 0.2f);
            return true;
        }

        public bool TryReleaseFirstCaptured(PlayerNetworkSetup actor)
        {
            if (!_roundStarted || actor == null) return false;
            var target = GetPlayers().FirstOrDefault(p => p != actor && p.IsCaptured);
            if (target == null) return false;

            target.RequestReleaseTo(ReleasePosition);
            SetPrompt("已釋放一名玩家");
            return true;
        }

        public PlayerNetworkSetup FindLookTarget(Camera camera, PlayerNetworkSetup self, float range)
        {
            if (camera == null || self == null) return null;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            if (!Physics.SphereCast(ray, 0.35f, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
                return null;

            var target = hit.collider.GetComponentInParent<PlayerNetworkSetup>();
            return target != null && target != self ? target : null;
        }

        public bool HasCapturedPlayers()
        {
            return GetPlayers().Any(p => p.IsCaptured);
        }

        static bool CanWorkOnTasks(HiAndSeeRole role)
        {
            return role == HiAndSeeRole.Civilian || role == HiAndSeeRole.Sheriff;
        }

        static bool CanCapture(HiAndSeeRole captor, HiAndSeeRole target)
        {
            if (captor == HiAndSeeRole.Ghost)
                return target == HiAndSeeRole.Civilian || target == HiAndSeeRole.Sheriff;

            if (captor == HiAndSeeRole.Sheriff)
                return target != HiAndSeeRole.Ghost && target != HiAndSeeRole.Unassigned;

            return false;
        }

        void AssignRoles(List<PlayerNetworkSetup> players)
        {
            var seed = DateTime.UtcNow.Millisecond;
            var runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null && runner.SessionInfo.IsValid)
                seed = runner.SessionInfo.Name.GetHashCode();

            var rng = new System.Random(seed);
            bool hasBots = players.Any(p => p.IsBot);
            var localPlayer = players.FirstOrDefault(p => p.Object != null && p.Object.HasInputAuthority);
            players = players.OrderBy(_ => rng.Next()).ToList();

            if (hasBots && makeLocalPlayerGhostWhenTestingBots && localPlayer != null)
            {
                players.Remove(localPlayer);
                players.Insert(0, localPlayer);
            }

            for (int i = 0; i < players.Count; i++)
            {
                var role = HiAndSeeRole.Civilian;
                if (i == 0) role = HiAndSeeRole.Ghost;
                else if (i == 1) role = HiAndSeeRole.Sheriff;
                else if (i == 2 && players.Count >= 4) role = HiAndSeeRole.Impostor;

                players[i].RPC_AssignRole((byte)role, GetSpawnPosition(i, players.Count));
            }
        }

        void BroadcastRoundStarted()
        {
            var local = GetLocalPlayer();
            if (local != null) local.RPC_BroadcastRoundStarted();
            else ReceiveRoundStarted();
        }

        void BroadcastTaskProgress(int completed, int total)
        {
            var local = GetLocalPlayer();
            if (local != null) local.RPC_BroadcastTaskProgress(completed, total);
            else ReceiveTaskProgress(completed, total);
        }

        void BroadcastRoundResult(HiAndSeeRoundResult result)
        {
            var local = GetLocalPlayer();
            if (local != null) local.RPC_BroadcastRoundResult((byte)result);
            else ReceiveRoundResult(result);
        }

        void CheckGhostWin()
        {
            if (!_roundStarted || _result != HiAndSeeRoundResult.None) return;

            var humanTargets = GetPlayers()
                .Where(p => p.Role == HiAndSeeRole.Civilian || p.Role == HiAndSeeRole.Sheriff)
                .ToList();

            if (humanTargets.Count > 0 && humanTargets.All(p => p.IsCaptured))
                BroadcastRoundResult(HiAndSeeRoundResult.GhostsWin);
        }

        PlayerNetworkSetup GetLocalPlayer()
        {
            return GetPlayers().FirstOrDefault(p => p.Object != null && p.Object.HasInputAuthority);
        }

        IEnumerable<PlayerNetworkSetup> GetPlayers()
        {
            return FindObjectsByType<PlayerNetworkSetup>(FindObjectsSortMode.None)
                .Where(p => p != null && p.Object != null);
        }

        void ResolveSceneRefs()
        {
            if (cage == null)
            {
                var cageObject = GameObject.Find("Cage");
                if (cageObject != null) cage = cageObject.transform;
            }

            if (cage == null)
            {
                var cageObject = new GameObject("Cage");
                cageObject.transform.position = Vector3.zero;
                cage = cageObject.transform;
            }

            if (releasePoint == null)
            {
                var release = new GameObject("CageReleasePoint");
                release.transform.position = CagePosition + new Vector3(0f, 0f, 3f);
                releasePoint = release.transform;
            }

            if (roundSpawnPoint == null)
            {
                var spawn = GameObject.Find("GameSpawnPoint");
                if (spawn == null) spawn = GameObject.Find("SpawnPoint");
                if (spawn != null) roundSpawnPoint = spawn.transform;
            }
        }

        void EnsureCageVisual()
        {
            if (!buildRuntimeCageVisual || cage == null) return;
            if (cage.GetComponentsInChildren<Renderer>(true).Length > 0) return;

            var parent = new GameObject("RuntimeCageVisual").transform;
            parent.SetParent(cage, false);

            var barMaterial = CreateRuntimeMaterial("RuntimeCageBars", new Color(0.05f, 0.13f, 0.16f, 1f));
            var accentMaterial = CreateRuntimeMaterial("RuntimeCageAccent", new Color(0.05f, 0.78f, 0.74f, 1f));
            var floorMaterial = CreateRuntimeMaterial("RuntimeCageFloor", new Color(0.02f, 0.04f, 0.05f, 0.82f));

            float half = cageSize * 0.5f;
            float post = 0.18f;
            float bar = 0.12f;
            float y = cageHeight * 0.5f;

            AddCageCube(parent, "Floor", new Vector3(0f, 0.03f, 0f), new Vector3(cageSize, 0.06f, cageSize), floorMaterial);

            AddCageCube(parent, "Post_NE", new Vector3(half, y, half), new Vector3(post, cageHeight, post), barMaterial);
            AddCageCube(parent, "Post_NW", new Vector3(-half, y, half), new Vector3(post, cageHeight, post), barMaterial);
            AddCageCube(parent, "Post_SE", new Vector3(half, y, -half), new Vector3(post, cageHeight, post), barMaterial);
            AddCageCube(parent, "Post_SW", new Vector3(-half, y, -half), new Vector3(post, cageHeight, post), barMaterial);

            for (int i = -2; i <= 2; i++)
            {
                float offset = i * cageSize / 5f;
                AddCageCube(parent, "Bar_N_" + i, new Vector3(offset, y, half), new Vector3(bar, cageHeight, bar), barMaterial);
                AddCageCube(parent, "Bar_S_" + i, new Vector3(offset, y, -half), new Vector3(bar, cageHeight, bar), barMaterial);
                AddCageCube(parent, "Bar_E_" + i, new Vector3(half, y, offset), new Vector3(bar, cageHeight, bar), barMaterial);
                AddCageCube(parent, "Bar_W_" + i, new Vector3(-half, y, offset), new Vector3(bar, cageHeight, bar), barMaterial);
            }

            AddCageCube(parent, "Rail_N_Low", new Vector3(0f, 0.85f, half), new Vector3(cageSize + post, bar, bar), barMaterial);
            AddCageCube(parent, "Rail_S_Low", new Vector3(0f, 0.85f, -half), new Vector3(cageSize + post, bar, bar), barMaterial);
            AddCageCube(parent, "Rail_E_Low", new Vector3(half, 0.85f, 0f), new Vector3(bar, bar, cageSize + post), barMaterial);
            AddCageCube(parent, "Rail_W_Low", new Vector3(-half, 0.85f, 0f), new Vector3(bar, bar, cageSize + post), barMaterial);
            AddCageCube(parent, "Rail_N_Top", new Vector3(0f, cageHeight, half), new Vector3(cageSize + post, bar, bar), accentMaterial);
            AddCageCube(parent, "Rail_S_Top", new Vector3(0f, cageHeight, -half), new Vector3(cageSize + post, bar, bar), accentMaterial);
            AddCageCube(parent, "Rail_E_Top", new Vector3(half, cageHeight, 0f), new Vector3(bar, bar, cageSize + post), accentMaterial);
            AddCageCube(parent, "Rail_W_Top", new Vector3(-half, cageHeight, 0f), new Vector3(bar, bar, cageSize + post), accentMaterial);
        }

        static void AddCageCube(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = localScale;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        static Material CreateRuntimeMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var material = new Material(shader);
            material.name = name;
            material.color = color;
            return material;
        }

        void EnsureTaskStations()
        {
            _runtimeTasks.Clear();
            if (taskStations != null)
                _runtimeTasks.AddRange(taskStations.Where(t => t != null));

            var existing = FindObjectsByType<GameplayTaskStation>(FindObjectsSortMode.None);
            foreach (var task in existing)
                if (!_runtimeTasks.Contains(task)) _runtimeTasks.Add(task);

            if (_runtimeTasks.Count > 0)
            {
                totalTasks = Mathf.Max(1, _runtimeTasks.Count);
                return;
            }

            var center = cage != null ? cage.position : Vector3.zero;
            var offsets = new[]
            {
                new Vector3(5f, 0.6f, 0f),
                new Vector3(-5f, 0.6f, 0f),
                new Vector3(0f, 0.6f, 5f),
                new Vector3(0f, 0.6f, -5f),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                var station = GameObject.CreatePrimitive(PrimitiveType.Cube);
                station.transform.position = center + offsets[i];
                station.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
                var task = station.AddComponent<GameplayTaskStation>();
                task.SetGeneratedName("TaskStation_" + (i + 1));
                _runtimeTasks.Add(task);
            }

            totalTasks = _runtimeTasks.Count;
        }

        void UpdateHud()
        {
            var local = GetLocalPlayer();
            var role = local != null ? local.Role : HiAndSeeRole.Unassigned;
            var objective = GetObjective(role);
            var result = _result == HiAndSeeRoundResult.None
                ? string.Empty
                : (_result == HiAndSeeRoundResult.HumansWin ? "人類陣營勝利" : "鬼陣營勝利");

            GameUIController.Instance?.SetGameplayHud(
                GetRoleText(role),
                objective,
                "任務 " + _completedTasks + " / " + totalTasks,
                _prompt,
                _holdProgress,
                result);
        }

        void TryAutoStartRound()
        {
            if (_roundStarted || _result != HiAndSeeRoundResult.None) return;

            var runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning)
            {
                if (!runner.IsSharedModeMasterClient)
                {
                    SetPrompt("等待房主同步開局");
                    return;
                }

                int expectedPlayers = runner.ActivePlayers.Count();
                int spawnedPlayers = GetPlayers().Count(p => !p.IsBot);
                if (expectedPlayers == 0 || spawnedPlayers < expectedPlayers)
                {
                    _autoStartAt = -1f;
                    SetPrompt("等待玩家進入遊戲 " + spawnedPlayers + " / " + expectedPlayers);
                    return;
                }

                var botSpawner = FindFirstObjectByType<TestBotSpawner>();
                if (botSpawner != null && !botSpawner.Done)
                {
                    _autoStartAt = -1f;
                    SetPrompt("生成測試電腦中");
                    return;
                }
            }

            if (_autoStartAt < 0f)
            {
                _autoStartAt = Time.time + autoStartDelay;
                SetPrompt("準備抽選身份");
                return;
            }

            if (Time.time >= _autoStartAt)
                StartRoundAsAuthority();
        }

        void EnsureLocalInteractor()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;

            var local = GetLocalPlayer();
            if (local != null && local.GetComponent<PlayerGameplayInteractor>() == null)
                local.gameObject.AddComponent<PlayerGameplayInteractor>();
        }

        Vector3 GetSpawnPosition(int index, int count)
        {
            if (GroundSpawnArea.TryGetRandomPoint(groundMeshName, spawnHeightOffset, spawnEdgePadding, out var point))
                return point;

            var center = roundSpawnPoint != null ? roundSpawnPoint.position : Vector3.zero;
            if (count <= 1) return center;

            float angle = Mathf.PI * 2f * index / count;
            return center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.2f;
        }

        static string GetRoleText(HiAndSeeRole role)
        {
            return role switch
            {
                HiAndSeeRole.Ghost => "身份：鬼",
                HiAndSeeRole.Impostor => "身份：內鬼",
                HiAndSeeRole.Sheriff => "身份：警長",
                HiAndSeeRole.Civilian => "身份：一般人",
                _ => "身份未抽選"
            };
        }

        static string GetObjective(HiAndSeeRole role)
        {
            return role switch
            {
                HiAndSeeRole.Ghost => "目標：在人類逃脫前抓住所有人類。",
                HiAndSeeRole.Impostor => "目標：偽裝成人類並妨礙任務。",
                HiAndSeeRole.Sheriff => "目標：完成任務，必要時抓捕可疑玩家。",
                HiAndSeeRole.Civilian => "目標：完成共享任務並逃脫。",
                _ => "等待房主開始遊戲。"
            };
        }
    }
}
