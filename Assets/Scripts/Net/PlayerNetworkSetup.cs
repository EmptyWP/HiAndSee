using Fusion;
using UnityEngine;

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

        public override void Spawned()
        {
            bool isLocal = Object.HasInputAuthority;

            if (localOnlyBehaviours != null)
                foreach (var b in localOnlyBehaviours)
                    if (b != null) b.enabled = isLocal;

            if (localOnlyObjects != null)
                foreach (var go in localOnlyObjects)
                    if (go != null) go.SetActive(isLocal);

            gameObject.name = isLocal ? "Player (You)" : "Player (Remote)";
        }
    }
}
