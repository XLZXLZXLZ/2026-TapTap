using UnityEngine;

namespace TapTap
{
    public sealed class PrototypeHUD : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerConfig config;
        private GUIStyle titleStyle;
        private GUIStyle textStyle;
        private string jumpNumber = "1";
        public void Configure(PlayerController target, PlayerConfig settings) { player = target; config = settings; }

        private void OnGUI()
        {
            if (player == null || config == null) return;
            WorldMapProgression flow = WorldMapProgression.Current;
            if (WorldMapProgression.InputBlocked) return;
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
                ? "SPACE: aligned = recall now; otherwise aim + release."
                : "Hold to extend. Early release waits for Holding.";
            GUI.Label(new Rect(30f, 81f, 350f, 23f), instruction, textStyle);
            GUI.Label(new Rect(30f, 103f, 350f, 23f), "Ctrl: bullet time " + (GameDebugTime.BulletTimeOn ? "ON · 0.5x" : "OFF")
                + "   I: " + GameDebugTime.Speed + "x", textStyle);
            string rewind = RewindManager.Rewinding ? "Z: rewinding — release to continue" : "Hold Z: undo    R: restart";
            GUI.Label(new Rect(30f, 125f, 350f, 23f), rewind, textStyle);
            GUI.color = Color.white;
            if (flow != null)
            {
                GUI.Box(new Rect(16f, 162f, 370f, 122f), "");
                GUI.Label(new Rect(30f, 170f, 340f, 24f), "第 " + flow.CurrentNumber + " 关 · " + flow.CurrentModule.DisplayName, textStyle);
                GUI.Label(new Rect(30f, 194f, 337f, 22f), "已通过 " + flow.CompletedCount + " / " + flow.LevelCount
                    + " · O 出口：" + (flow.ExitsEnabled ? "启用" : "停用") + " · PageDown 跳关", textStyle);
                jumpNumber = GUI.TextField(new Rect(30f, 220f, 60f, 24f), jumpNumber, 6);
                if (GUI.Button(new Rect(98f, 220f, 88f, 24f), "跳到序号") && int.TryParse(jumpNumber, out int number))
                    flow.GoToSequence(number);
                using (new GUIEnabledScope(flow.CurrentIndex + 1 < flow.LevelCount))
                    if (GUI.Button(new Rect(194f, 220f, 80f, 24f), "下一关")) flow.SkipNext();
                if (GUI.Button(new Rect(282f, 220f, 85f, 24f), "重开本关")) flow.RestartCurrent();
                if (GUI.Button(new Rect(30f, 252f, 337f, 24f), "返回选关（Esc） · P 解锁全部关卡")) flow.ReturnToSelection();
            }
        }

        private readonly struct GUIEnabledScope : System.IDisposable
        {
            private readonly bool previous;
            public GUIEnabledScope(bool enabled) { previous = GUI.enabled; GUI.enabled = previous && enabled; }
            public void Dispose() { GUI.enabled = previous; }
        }
    }
}
