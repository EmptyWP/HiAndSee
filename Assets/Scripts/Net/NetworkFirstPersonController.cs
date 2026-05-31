using Fusion;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HiAndSee.Net
{
    /// <summary>
    /// Fusion 2 Shared 模式用的第一人稱控制器。
    /// 重點：移動與轉身放在 FixedUpdateNetwork（網路模擬迴圈），NetworkTransform 才會把它當權威狀態同步、
    /// 不會把在 Update 移動的位置「拉回」。相機 pitch 在本地（不需網路）。沿用 StarterAssets 的輸入與數值。
    /// 取代原本 Update 驅動的 FirstPersonController（在網路下會與 NetworkTransform 衝突）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NetworkFirstPersonController : NetworkBehaviour
    {
        [Header("移動（沿用 FPC 數值）")]
        public float MoveSpeed = 7f;
        public float SprintSpeed = 11f;
        public float RotationSpeed = 2f;
        public float JumpHeight = 1.2f;
        public float Gravity = -15f;
        public float TopClamp = 90f;
        public float BottomClamp = -90f;

        [Header("走路晃動 (head bob，僅本機視覺)")]
        public float BobAmplitude = 0.06f;   // 晃動幅度（公尺）
        public float BobFrequency = 1.8f;    // 走路時每秒晃動次數
        public float BobSmoothing = 10f;     // 起步/停止的平滑

        [Header("參考")]
        [SerializeField] StarterAssetsInputs _input;
        [SerializeField] Transform _cameraTarget;   // PlayerCameraRoot

        CharacterController _cc;
        float _verticalVel;
        float _pitch;
        float _yawAccum;   // 累積每幀的滑鼠水平位移，於 FixedUpdateNetwork 一次套用
        float _bobTimer;
        Vector3 _camBaseLocalPos;
        bool _bobInit;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (_input == null) _input = GetComponent<StarterAssetsInputs>();
            if (_cameraTarget != null) { _camBaseLocalPos = _cameraTarget.localPosition; _bobInit = true; }
        }

        public override void Spawned()
        {
            if (HasInputAuthority) SetCursor(true);
        }

        void Update()
        {
            if (!HasInputAuthority) return;
            var kb = Keyboard.current;
            if (kb != null && kb.leftAltKey.wasPressedThisFrame)
                SetCursor(Cursor.lockState != CursorLockMode.Locked);

            // 逐 render frame 累積滑鼠水平位移（避免在 32Hz 模擬迴圈漏掉 delta → 轉動更跟手）
            if (Cursor.lockState == CursorLockMode.Locked && _input != null)
                _yawAccum += _input.look.x * RotationSpeed;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || _cc == null || _input == null) return;

            float dt = Runner.DeltaTime;

            // 轉身（yaw）— 套用 Update 累積的位移，不漏 delta；身體旋轉會被 NetworkTransform 同步給他人
            if (_yawAccum != 0f)
            {
                transform.Rotate(Vector3.up * _yawAccum);
                _yawAccum = 0f;
            }

            // 重力 / 跳躍（用 CharacterController.isGrounded，避免 GroundLayers 偵測不到地面）
            if (_cc.isGrounded && _verticalVel < 0f) _verticalVel = -2f;
            if (_input.jump && _cc.isGrounded)
            {
                _verticalVel = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                _input.jump = false; // 消耗，避免按住連跳
            }
            _verticalVel += Gravity * dt;

            // 水平移動
            float speed = _input.sprint ? SprintSpeed : MoveSpeed;
            Vector3 dir = transform.right * _input.move.x + transform.forward * _input.move.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            _cc.Move((dir * speed + Vector3.up * _verticalVel) * dt);
        }

        void LateUpdate()
        {
            if (!HasInputAuthority || _cameraTarget == null) return;

            // 相機 pitch（游標鎖定時才轉）
            if (Cursor.lockState == CursorLockMode.Locked && _input != null)
            {
                _pitch = Mathf.Clamp(_pitch + _input.look.y * RotationSpeed, BottomClamp, TopClamp);
                _cameraTarget.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            ApplyHeadBob();
        }

        // 走路晃動：依水平速度上下 + 輕微左右晃，停下平滑回正（僅本機相機、不需網路）
        void ApplyHeadBob()
        {
            if (!_bobInit || _cc == null) return;
            Vector3 v = _cc.velocity; v.y = 0f;
            float hSpeed = v.magnitude;
            Vector3 target = _camBaseLocalPos;
            if (_cc.isGrounded && hSpeed > 0.1f)
            {
                _bobTimer += Time.deltaTime * (hSpeed / Mathf.Max(0.1f, MoveSpeed));
                float phase = _bobTimer * BobFrequency * Mathf.PI * 2f;
                float y = Mathf.Sin(phase) * BobAmplitude;
                float x = Mathf.Cos(phase * 0.5f) * BobAmplitude * 0.6f;
                target = _camBaseLocalPos + new Vector3(x, y, 0f);
            }
            else
            {
                _bobTimer = 0f;
            }
            _cameraTarget.localPosition = Vector3.Lerp(_cameraTarget.localPosition, target, Time.deltaTime * BobSmoothing);
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
