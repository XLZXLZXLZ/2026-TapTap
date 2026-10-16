using UnityEngine;

namespace TapTap
{
    // Replace this child with final UI/artwork without changing triggers or camera logic.
    public sealed class FinalEscapeEndingScreen : MonoBehaviour
    {
        public string Title = "THE END";
        public string Subtitle = "感谢游玩";
        public Color Background = new Color(0.015f, 0.025f, 0.04f, 1f);
        private GUIStyle titleStyle, subtitleStyle;

        private void OnGUI()
        {
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            GUI.depth = -10000;
            GUI.color = Background;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                subtitleStyle = new GUIStyle(titleStyle);
            }
            titleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.06f);
            subtitleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
            GUI.Label(new Rect(0f, Screen.height * 0.39f, Screen.width, Screen.height * 0.12f), Title, titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.53f, Screen.width, Screen.height * 0.06f), Subtitle, subtitleStyle);
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }
    }
}
