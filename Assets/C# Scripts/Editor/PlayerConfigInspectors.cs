using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TapTap.Editor
{
    // Use explicit labels for regular fields too, preserving Unity's range, minimum and header drawers.
    public abstract class LocalizedPlayerConfigInspector : UnityEditor.Editor
    {
        private readonly Dictionary<string, GUIContent> labels = new Dictionary<string, GUIContent>();

        protected virtual void OnEnable()
        {
            labels.Clear();
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var name = field.GetCustomAttribute<InspectorNameAttribute>();
                var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                if (name != null) labels[field.Name] = new GUIContent(name.displayName, tooltip?.tooltip);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                bool script = property.propertyPath == "m_Script";
                using (new EditorGUI.DisabledScope(script))
                {
                    GUIContent label = script ? new GUIContent("脚本") :
                        labels.TryGetValue(property.name, out GUIContent localized) ? localized : new GUIContent(property.displayName, property.tooltip);
                    EditorGUILayout.PropertyField(property, label, true);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(PlayerConfig)), CanEditMultipleObjects]
    public sealed class PlayerConfigInspector : LocalizedPlayerConfigInspector { }

    [CustomEditor(typeof(PlayerVisualConfig)), CanEditMultipleObjects]
    public sealed class PlayerVisualConfigInspector : LocalizedPlayerConfigInspector { }
}
