using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    // Join feedback only; the controller still owns contact resolution and distant-pull policy.
    [DisallowMultipleComponent, RequireComponent(typeof(PlayerController))]
    public sealed class MagneticJoinParticles : MonoBehaviour
    {
        [SerializeField] private ParticleSystem normalPrefab;
        [SerializeField] private ParticleSystem heavyPrefab;
        [SerializeField] private float depthOffset = -0.1f;
        private PlayerController player;
        private ParticleSystem normal, heavy;
        private bool distantPull;

        public void Configure(ParticleSystem normalEffect, ParticleSystem heavyEffect)
        {
            normalPrefab = normalEffect;
            heavyPrefab = heavyEffect;
        }

        private void OnEnable()
        {
            player = GetComponent<PlayerController>();
            player.PhaseChanged += OnPhaseChanged;
            player.ContactFeedback += OnContact;
            player.Effect += OnEffect;
            distantPull = false;
        }

        private void OnPhaseChanged(PlayerPhase phase)
        {
            if (phase == PlayerPhase.Pulling && player.Config != null)
                distantPull = player.Head.Motor.Position.y - player.Body.Motor.Position.y
                    > player.Config.ToWorld(player.Config.DistantThreshold);
            else if (phase != PlayerPhase.Joined)
                distantPull = false;
            // Joined is reported before its Assembly contact; retain the pull classification until then.
        }

        private void OnContact(PlayerContactFeedback contact)
        {
            if (!contact.Assembly || RewindManager.Rewinding || player.Config == null
                || player.Body == null || player.Head == null
                || player.Body.IsReturning || player.Head.IsReturning) return;
            ParticleSystem prefab = distantPull && heavyPrefab != null ? heavyPrefab : normalPrefab;
            distantPull = false;
            if (prefab == null) return;
            bool useHeavy = prefab == heavyPrefab;
            ParticleSystem effect = useHeavy ? heavy : normal;
            if (effect == null)
            {
                effect = Instantiate(prefab);
                effect.name = prefab.name + " (Join Feedback)";
                // Keep the burst stationary after emission, while ensuring it unloads with its player scene.
                SceneManager.MoveGameObjectToScene(effect.gameObject, gameObject.scene);
                var main = effect.main;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                if (useHeavy) heavy = effect;
                else normal = effect;
            }
            float unit = player.Body.Motor.Size.x / Mathf.Max(0.01f, player.Body.Motor.Collider.size.x);
            float z = player.Body.Visual != null ? player.Body.Visual.position.z : player.Body.transform.position.z;
            effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.transform.SetPositionAndRotation(new Vector3(contact.Point.x, contact.Point.y,
                z + depthOffset * unit), prefab.transform.rotation);
            effect.transform.localScale = prefab.transform.localScale * unit;
            effect.Play(true);
        }

        private void OnEffect(PlayerEffect effect)
        {
            if (effect == PlayerEffect.Death) { distantPull = false; Clear(); }
        }

        private void Update()
        {
            if (!RewindManager.Rewinding) return;
            Clear();
            // Restoring a snapshot does not raise PhaseChanged. Recover the pull variant when
            // resuming from a past pull, using the launch height already stored by the simulation.
            distantPull = player.Phase == PlayerPhase.Pulling && player.Config != null
                && Mathf.Approximately(player.CaptureState().PendingLaunchHeight, player.Config.DistantLaunchHeight);
        }

        private void Clear()
        {
            if (normal != null) normal.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (heavy != null) heavy.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.PhaseChanged -= OnPhaseChanged;
                player.ContactFeedback -= OnContact;
                player.Effect -= OnEffect;
            }
            distantPull = false;
            Clear();
        }

        private void OnDestroy()
        {
            if (normal != null) Destroy(normal.gameObject);
            if (heavy != null) Destroy(heavy.gameObject);
        }
    }
}
