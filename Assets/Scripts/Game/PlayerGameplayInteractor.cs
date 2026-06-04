using HiAndSee.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace HiAndSee.Game
{
    [RequireComponent(typeof(PlayerNetworkSetup))]
    public class PlayerGameplayInteractor : MonoBehaviour
    {
        PlayerNetworkSetup _player;
        Camera _camera;
        float _hold;
        float _sheriffCooldown;

        void Awake()
        {
            _player = GetComponent<PlayerNetworkSetup>();
        }

        void Start()
        {
            _camera = GetComponentInChildren<Camera>(true);
            if (_camera == null) _camera = Camera.main;
        }

        void Update()
        {
            if (_player == null || !_player.Object.HasInputAuthority) return;
            if (SceneManager.GetActiveScene().name != "Game") return;

            var manager = GameRoundManager.EnsureExists();
            if (!manager.RoundStarted)
            {
                manager.SetPrompt("等待所有玩家進入遊戲");
                return;
            }

            if (Cursor.lockState != CursorLockMode.Locked)
            {
                ResetHold(manager);
                return;
            }

            if (_sheriffCooldown > 0f)
                _sheriffCooldown -= Time.deltaTime;

            if (_player.IsCaptured)
            {
                ResetHold(manager);
                manager.SetPrompt("你被關在中央牢籠，等待其他玩家救援");
                return;
            }

            var kb = Keyboard.current;
            if (kb == null)
            {
                manager.SetPrompt(string.Empty);
                return;
            }

            if (TryCapture(manager, kb)) return;
            if (TryRescue(manager, kb)) return;
            if (TryTaskOrSabotage(manager, kb)) return;

            ResetHold(manager);
            manager.SetPrompt(GetIdlePrompt());
        }

        bool TryCapture(GameRoundManager manager, Keyboard kb)
        {
            if (_camera == null) _camera = GetComponentInChildren<Camera>(true);
            if (_camera == null) _camera = Camera.main;
            var target = manager.FindLookTarget(_camera, _player, manager.CaptureRange);
            if (target == null || target.IsCaptured) return false;

            bool canGhostCapture = _player.Role == HiAndSeeRole.Ghost &&
                                   (target.Role == HiAndSeeRole.Civilian || target.Role == HiAndSeeRole.Sheriff);
            bool canSheriffCapture = _player.Role == HiAndSeeRole.Sheriff &&
                                      target.Role != HiAndSeeRole.Ghost &&
                                      target.Role != HiAndSeeRole.Unassigned &&
                                      _sheriffCooldown <= 0f;

            if (!canGhostCapture && !canSheriffCapture)
                return false;

            string cooldownText = _player.Role == HiAndSeeRole.Sheriff && _sheriffCooldown > 0f
                ? " 冷卻 " + Mathf.CeilToInt(_sheriffCooldown) + "s"
                : string.Empty;
            manager.SetPrompt("[E] 抓捕目標" + cooldownText);

            if (kb.eKey.wasPressedThisFrame && manager.TryCapture(_player, target))
            {
                if (_player.Role == HiAndSeeRole.Sheriff)
                    _sheriffCooldown = manager.SheriffCaptureCooldown;
            }

            return true;
        }

        bool TryRescue(GameRoundManager manager, Keyboard kb)
        {
            float sqr = (transform.position - manager.CagePosition).sqrMagnitude;
            if (sqr > manager.InteractRange * manager.InteractRange || !manager.HasCapturedPlayers())
                return false;

            if (!kb.fKey.isPressed)
            {
                ResetHold(manager);
                manager.SetPrompt("[F] 長按救援牢籠玩家");
                return true;
            }

            _hold += Time.deltaTime;
            float progress = _hold / manager.RescueHoldSeconds;
            manager.SetPrompt("救援中...", progress);
            if (_hold >= manager.RescueHoldSeconds)
            {
                manager.TryReleaseFirstCaptured(_player);
                ResetHold(manager);
            }

            return true;
        }

        bool TryTaskOrSabotage(GameRoundManager manager, Keyboard kb)
        {
            var task = manager.GetNearestTask(transform.position, manager.InteractRange);
            if (task == null) return false;

            bool canWork = _player.Role == HiAndSeeRole.Civilian || _player.Role == HiAndSeeRole.Sheriff;
            bool canSabotage = _player.Role == HiAndSeeRole.Impostor;
            if (!canWork && !canSabotage) return false;

            string action = canSabotage ? "破壞任務" : "完成任務";
            if (!kb.fKey.isPressed)
            {
                ResetHold(manager);
                manager.SetPrompt("[F] 長按" + action);
                return true;
            }

            _hold += Time.deltaTime;
            float progress = _hold / manager.TaskHoldSeconds;
            manager.SetPrompt(action + "中...", progress);
            if (_hold >= manager.TaskHoldSeconds)
            {
                if (canSabotage) manager.TrySabotageTask(task, _player);
                else manager.TryCompleteTask(task, _player);
                ResetHold(manager);
            }

            return true;
        }

        string GetIdlePrompt()
        {
            return _player.Role switch
            {
                HiAndSeeRole.Ghost => "尋找人類，準星對準目標按 E 抓捕",
                HiAndSeeRole.Impostor => "靠近任務點長按 F 破壞進度",
                HiAndSeeRole.Sheriff => "完成任務；對準可疑玩家按 E 抓捕",
                HiAndSeeRole.Civilian => "靠近任務點長按 F 完成任務",
                _ => "等待開局"
            };
        }

        void ResetHold(GameRoundManager manager)
        {
            if (_hold <= 0f) return;
            _hold = 0f;
            manager.SetPrompt(string.Empty, 0f);
        }
    }
}
