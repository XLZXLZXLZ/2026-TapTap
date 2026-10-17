using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap.MagneticLinkRemake
{
    // The only adapter that knows gameplay types. Existing gameplay and the old effect stay untouched.
    [DefaultExecutionOrder(400), DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class MagneticLinkRemakeView : MonoBehaviour
    {
        [SerializeField] private float forwardOffset = -0.08f;
        [SerializeField, Min(0.01f)] private float fadeInDuration = 0.1f;
        [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.28f;
        [SerializeField, Min(0.01f)] private float stateBlendDuration = 0.15f;
        private PlayerController player;
        private PlayerView playerView;
        private MagneticConnectionView previous;
        private bool previousActive;
        private FoldedMagneticLink effect;
        private float alpha, tension, activation, distant;
        private double lastClock;
        private bool hasClock;
        private Vector3 lastFrom, lastTo;
        private bool hasEndpoints;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterScenes()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!Application.isPlaying) return;
            foreach (PlayerController character in FindObjectsOfType<PlayerController>())
                if (character.GetComponent<MagneticLinkRemakeView>() == null)
                    character.gameObject.AddComponent<MagneticLinkRemakeView>();
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => Bootstrap();

        private void OnEnable()
        {
            player = GetComponent<PlayerController>();
            playerView = GetComponent<PlayerView>();
            if (effect == null)
            {
                var root = new GameObject("Folded Magnetic Link (Remake)") { layer = gameObject.layer };
                root.transform.SetParent(transform, false);
                effect = root.AddComponent<FoldedMagneticLink>();
            }
            effect.gameObject.SetActive(true);
            if (!effect.Ready) return; // Preserve the old visual if shader creation failed.
            previous = GetComponentInChildren<MagneticConnectionView>(true);
            if (previous != null)
            {
                previousActive = previous.gameObject.activeSelf;
                previous.gameObject.SetActive(false);
            }
            alpha = tension = activation = distant = 0f;
            hasClock = false;
            hasEndpoints = false;
        }

        private void LateUpdate()
        {
            double clock = RewindManager.DrivesSimulation ? RewindManager.Current.SimulationTime : Time.timeAsDouble;
            RefreshPresentation(clock, RewindManager.Rewinding);
        }

        public void RefreshPresentation(double clock, bool rewinding)
        {
            if (effect == null || !effect.Ready || player == null || player.Config == null
                || player.Body == null || player.Head == null) return;
            MovableEntity body = player.Body, head = player.Head;
            float dt = hasClock ? Mathf.Min(0.1f, (float)System.Math.Abs(clock - lastClock)) : 0f;
            lastClock = clock;
            hasClock = true;
            WorldMapProgression world = WorldMapProgression.Current;
            bool transitioning = world != null && world.gameObject.scene == gameObject.scene
                && (world.IsTransitioning || world.IsComplete);
            if (transitioning || (playerView != null && playerView.ExitPresentation))
            {
                // Completion freezes simulation time before the exit walk starts. A simulation-
                // timed fade would retain the cached world-space span throughout the cinematic.
                alpha = tension = activation = distant = 0f;
                hasEndpoints = false;
                effect.Clear();
                return;
            }
            bool connected = player.HasMagneticConnection;
            bool preview = player.Phase == PlayerPhase.Detached && player.CanRecall;
            bool visible = player.isActiveAndEnabled && (connected || preview)
                && player.Phase != PlayerPhase.Joined && !body.IsReturning && !head.IsReturning;
            float unit = body.Motor.Size.x / Mathf.Max(0.01f, body.Motor.Collider.size.x);
            Transform lower = body.Visual != null ? body.Visual : body.transform;
            Transform upper = head.Visual != null ? head.Visual : head.transform;
            Vector3 from = lower.position + lower.up * (body.Motor.Size.y * 0.42f);
            Vector3 to = upper.position - upper.up * (head.Motor.Size.y * 0.42f);
            from.z += forwardOffset * unit;
            to.z += forwardOffset * unit;
            visible &= Vector3.Distance(from, to) > 0.025f * unit;
            if (visible)
            {
                lastFrom = from;
                lastTo = to;
                hasEndpoints = true;
            }
            else if (hasEndpoints && !rewinding)
            {
                // Joining/respawning can collapse or teleport the endpoints before fading finishes.
                // Let the last valid span dissipate instead of hitting the renderer's short-span cutoff.
                from = lastFrom;
                to = lastTo;
            }
            bool pulling = player.Phase == PlayerPhase.Pulling;
            // Pulling retains the classification captured by gameplay at activation.
            bool distantTarget = pulling ? player.IsDistantPull
                : head.Motor.Position.y - body.Motor.Position.y > player.Config.ToWorld(player.Config.DistantThreshold);
            if (rewinding)
            {
                alpha = visible ? 1f : 0f;
                tension = pulling ? 1f : 0f;
                activation = connected ? 1f : 0f;
                distant = distantTarget ? 1f : 0f;
            }
            else
            {
                float duration = visible ? fadeInDuration : fadeOutDuration;
                alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, dt / Mathf.Max(0.01f, duration));
                if (visible)
                {
                    float blend = dt / Mathf.Max(0.01f, stateBlendDuration);
                    activation = Mathf.MoveTowards(activation, connected ? 1f : 0f, blend);
                    distant = Mathf.MoveTowards(distant, distantTarget ? 1f : 0f, blend);
                    tension = Mathf.MoveTowards(tension, pulling ? 1f : 0f, dt * 7f);
                }
                // Preserve the last style while a disconnected/dead span dissipates.
            }
            effect.Render(from, to, Mathf.SmoothStep(0f, 1f, alpha), tension, clock, unit,
                Mathf.SmoothStep(0f, 1f, activation), Mathf.SmoothStep(0f, 1f, distant));
        }

        private void OnDisable()
        {
            if (effect != null) effect.gameObject.SetActive(false);
            if (previous != null) previous.gameObject.SetActive(previousActive);
            previous = null;
        }

        private void OnDestroy()
        {
            if (effect != null) Destroy(effect.gameObject);
        }
    }
}
