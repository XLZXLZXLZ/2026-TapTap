using UnityEngine;

namespace TapTap
{
    public sealed class PrototypeHUD : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerConfig config;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        public void Configure(PlayerController target, PlayerConfig settings) { player = target; config = settings; }

        private void OnGUI()
        {
            if (player == null || config == null) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
                textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            }
            GUI.color = new Color(0.04f, 0.07f, 0.12f, 0.9f);
            GUI.DrawTexture(new Rect(16f, 16f, 370f, 137f), Texture2D.whiteTexture);
            GUI.color = new Color(0.22f, 0.89f, 0.78f);
            GUI.Label(new Rect(30f, 25f, 350f, 28f), "TAP / TAP   |   " + player.Phase, titleStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(30f, 57f, 350f, 23f), "A / D or LEFT / RIGHT     SPACE: magnetic action", textStyle);
            string instruction = player.Phase == PlayerPhase.Detached
                ? "Hold to aim. Blue guide + release: recall."
                : "Hold to extend. Early release waits for Holding.";
            GUI.Label(new Rect(30f, 81f, 350f, 23f), instruction, textStyle);
            GUI.Label(new Rect(30f, 103f, 350f, 23f), "Mint: one-way    Purple: belt    Red: lethal", textStyle);
            GUI.Label(new Rect(30f, 125f, 350f, 23f), "R: restart current scene", textStyle);
            GUI.color = Color.white;
        }
    }
}
