using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.Editor
{
    public static class ProgressKeyDebug
    {
        private const string MenuPath = "TapTap/调试/给玩家钥匙（仅运行时）";
        private const string KeyPrefabPath = "Assets/Prefabs/Interactables/ProgressKey.prefab";

        [MenuItem(MenuPath)]
        private static void GiveKey()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return;

            var player = Object.FindObjectOfType<PlayerController>();
            if (player == null)
            {
                Debug.LogWarning("当前场景没有正在运行的玩家，无法给予钥匙。");
                return;
            }

            Scene scene = player.gameObject.scene;
            var stage = GameStageProgression.ForScene(scene);
            if (stage.Stage >= GameStage.CarryingKey)
            {
                Debug.LogWarning(stage.HasKey ? "玩家已经持有钥匙。" : "门已经打开，钥匙已消耗。", player);
                return;
            }

            if (!ReturnJourneyContact.CanTrigger(player) || player.Head == null || player.Head.IsReturning
                || GameStageProgression.InputBlocked || PauseMenu.IsOpen)
            {
                Debug.LogWarning("请在取消暂停、回溯或演出结束后再给予钥匙。", player);
                return;
            }

            ProgressKey key = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                key = root.GetComponentInChildren<ProgressKey>(true);
                if (key != null) break;
            }

            if (key == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KeyPrefabPath);
                if (prefab == null || prefab.GetComponent<ProgressKey>() == null)
                {
                    Debug.LogWarning("当前场景没有钥匙，且未找到有效的钥匙预制体。", player);
                    return;
                }

                var owner = Object.Instantiate(prefab, player.Body.transform.position, Quaternion.identity);
                SceneManager.MoveGameObjectToScene(owner, scene);
                key = owner.GetComponent<ProgressKey>();
            }

            if (stage.Stage == GameStage.Outbound && !stage.BeginReturn())
            {
                Debug.LogWarning("当前无法进入返程阶段，未给予钥匙。", player);
                return;
            }

            // A key in an unloaded region needs an active root so its carry visual keeps following.
            if (!key.gameObject.activeInHierarchy) key.transform.SetParent(null, true);
            key.gameObject.SetActive(true);
            key.enabled = true;
            if (key.TryCollect(player))
                Debug.Log("已触发调试钥匙拾取；拾取演出结束后即可用于开门。本次游玩重载会保留，退出 Play 后清除。", player);
            else
                Debug.LogWarning("当前无法拾取钥匙，请等待玩家恢复正常操作后重试。", player);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanGiveKey() => EditorApplication.isPlaying && !EditorApplication.isPaused;
    }
}
