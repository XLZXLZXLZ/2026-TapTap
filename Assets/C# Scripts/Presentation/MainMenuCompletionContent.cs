using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    public sealed class MainMenuCompletionContent : MonoBehaviour
    {
        [Tooltip("通关后显示的内容根节点。本组件必须放在该节点外部的启用对象上。")]
        public GameObject AfterCompletionContent;

        private void OnEnable()
        {
            GameCompletionStore.Changed += CompletionChanged;
            Refresh();
        }

        private void OnDisable() => GameCompletionStore.Changed -= CompletionChanged;
        private void CompletionChanged(bool completed) => Refresh();

        public void Refresh()
        {
            if (AfterCompletionContent == null) return;
            if (transform.IsChildOf(AfterCompletionContent.transform))
            {
                Debug.LogError("通关内容控制器必须放在 AfterCompletionContent 及其子物体之外。", this);
                return;
            }
            AfterCompletionContent.SetActive(GameCompletionStore.HasCompletedGame);
        }
    }
}
