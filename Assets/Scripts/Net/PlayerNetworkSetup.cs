using Fusion;
using HiAndSee.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HiAndSee.Net
{
    /// <summary>
    /// 掛在玩家 prefab 上。生成後，只有「本人」(input authority) 啟用 FPC / 相機 / 輸入；
    /// 其他玩家只是被 NetworkTransform 同步的角色身體。
    /// 透過 Inspector 指派要「只對本人啟用」的元件與物件，藉此和 StarterAssets / InputSystem 解耦
    /// （本腳本只相依 Fusion，能進 Assembly-CSharp 順利編譯）。
    /// </summary>
    public class PlayerNetworkSetup : NetworkBehaviour
    {
        [Tooltip("只對本人啟用的 Behaviour：FirstPersonController、StarterAssetsInputs、PlayerInput…")]
        [SerializeField] UnityEngine.Behaviour[] localOnlyBehaviours;

        [Tooltip("只對本人啟用的物件：相機、AudioListener…")]
        [SerializeField] GameObject[] localOnlyObjects;

        /// <summary>玩家暱稱（網路同步，供準備大廳的玩家列表顯示）。</summary>
        [Networked] public NetworkString<_16> Nick { get; set; }
        [Networked] public byte RoleId { get; set; }
        [Networked] public NetworkBool IsCaptured { get; set; }
        [Networked] public NetworkBool IsBot { get; set; }

        public HiAndSeeRole Role => (HiAndSeeRole)RoleId;

        public override void Spawned()
        {
            bool isLocal = Object.HasInputAuthority;

            if (localOnlyBehaviours != null)
                foreach (var b in localOnlyBehaviours)
                    if (b != null) b.enabled = isLocal;

            if (localOnlyObjects != null)
                foreach (var go in localOnlyObjects)
                    if (go != null) go.SetActive(isLocal);

            if (Object.HasStateAuthority)
                Nick = PlayerPrefs.GetString("mp_nickname", "Player");

            gameObject.name = isLocal ? "Player (You)" : "Player (Remote)";

            if (isLocal && SceneManager.GetActiveScene().name == "Game" && GetComponent<PlayerGameplayInteractor>() == null)
                gameObject.AddComponent<PlayerGameplayInteractor>();
        }

        public void ConfigureBot(int botIndex)
        {
            if (!Object.HasStateAuthority) return;

            IsBot = true;
            Nick = "BOT " + botIndex;
            gameObject.name = "BOT " + botIndex;
        }

        public void RequestCaptureTo(PlayerRef captor, Vector3 cagePosition)
        {
            if (Object.HasStateAuthority) ApplyCaptured(cagePosition);
            else RPC_RequestCapture(captor, cagePosition);
        }

        public void RequestReleaseTo(Vector3 releasePosition)
        {
            if (Object.HasStateAuthority) ApplyReleased(releasePosition);
            else RPC_RequestRelease(releasePosition);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_AssignRole(byte roleId, Vector3 spawnPosition)
        {
            RoleId = roleId;
            IsCaptured = false;
            TeleportTo(spawnPosition);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestCapture(PlayerRef captor, Vector3 cagePosition)
        {
            if (IsCaptured) return;
            ApplyCaptured(cagePosition);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestRelease(Vector3 releasePosition)
        {
            if (!IsCaptured) return;
            ApplyReleased(releasePosition);
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_BroadcastRoundStarted()
        {
            GameRoundManager.EnsureExists().ReceiveRoundStarted();
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_BroadcastTaskProgress(int completed, int total)
        {
            GameRoundManager.EnsureExists().ReceiveTaskProgress(completed, total);
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RPC_BroadcastRoundResult(byte result)
        {
            GameRoundManager.EnsureExists().ReceiveRoundResult((HiAndSeeRoundResult)result);
        }

        void ApplyCaptured(Vector3 position)
        {
            IsCaptured = true;
            TeleportTo(position);
        }

        void ApplyReleased(Vector3 position)
        {
            IsCaptured = false;
            TeleportTo(position);
        }

        void TeleportTo(Vector3 position)
        {
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;

            var networkTransform = GetComponent<NetworkTransform>();
            if (networkTransform != null)
                networkTransform.Teleport(position, transform.rotation);
            else
                transform.position = position;

            if (controller != null) controller.enabled = true;
        }
    }
}
