using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    public static class GameCompletionDebug
    {
        [MenuItem("TapTap/结局/通关记录/标记已通关（写入存档）")]
        private static void MarkCompleted()
        {
            GameCompletionStore.MarkCompleted();
            RefreshMenus();
            Debug.Log("已保存曾通关记录；主菜单将显示 AfterCompletionContent。");
        }

        [MenuItem("TapTap/结局/通关记录/清除通关记录（仅此记录）")]
        private static void ClearCompletion()
        {
            GameCompletionStore.Clear();
            RefreshMenus();
            Debug.Log("已清除曾通关记录；关卡进度、音量等设置保持原值。");
        }

        private static void RefreshMenus()
        {
            foreach (MainMenuCompletionContent menu in Object.FindObjectsOfType<MainMenuCompletionContent>())
                menu.Refresh();
        }
    }

    [CustomEditor(typeof(MainMenuCompletionContent))]
    public sealed class MainMenuCompletionContentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (MainMenuCompletionContent)target;
            EditorGUILayout.LabelField("本机通关记录", GameCompletionStore.HasCompletedGame ? "已通关" : "未通关");
            EditorGUILayout.HelpBox("将二周目模型、灯光和装饰放在 AfterCompletionContent 下。下面的预览按钮只改变显示，不写入存档；运行时自动按存档显示。", MessageType.Info);
            using (new EditorGUI.DisabledScope(controller.AfterCompletionContent == null))
            {
                if (GUILayout.Button("预览已通关（不写存档）")) Preview(controller, true);
                if (GUILayout.Button("预览未通关（不写存档）")) Preview(controller, false);
                if (GUILayout.Button("恢复存档对应的显示")) Preview(controller, GameCompletionStore.HasCompletedGame);
            }
        }

        private static void Preview(MainMenuCompletionContent controller, bool visible)
        {
            if (controller.transform.IsChildOf(controller.AfterCompletionContent.transform)) return;
            Undo.RecordObject(controller.AfterCompletionContent, "预览二周目主菜单内容");
            controller.AfterCompletionContent.SetActive(visible);
            SceneView.RepaintAll();
        }
    }
}
