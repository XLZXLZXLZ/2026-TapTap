using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent, DefaultExecutionOrder(900)]
    public sealed class PlayerAudioFeedback : MonoBehaviour
    {
        private PlayerController player;
        private RespawnService respawn;
        private PlayerPhase previous;
        private bool subscribed;
        public void Bind(PlayerController controller)
        {
            Unsubscribe();
            player = controller;
            respawn = GetComponent<RespawnService>();
            previous = player.Phase;
            if (isActiveAndEnabled) Subscribe();
            AudioManager.Instance.SetListener(player);
        }
        private void OnEnable() { if (player != null) Subscribe(); }
        private void Subscribe()
        {
            if (subscribed) return;
            player.PhaseChanged += PhaseChanged;
            player.Effect += Effect;
            player.ContactFeedback += Contact;
            respawn.ReturnStarted += ReturnStarted;
            respawn.Returned += Returned;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (player != null)
            { player.PhaseChanged -= PhaseChanged; player.Effect -= Effect; player.ContactFeedback -= Contact; }
            if (respawn != null) { respawn.ReturnStarted -= ReturnStarted; respawn.Returned -= Returned; }
            subscribed = false;
        }
        private void OnDisable()
        {
            Unsubscribe();
            AudioManager.Existing?.StopOwner(this);
        }
        private void PhaseChanged(PlayerPhase next)
        {
            if (next == PlayerPhase.Extending || next == PlayerPhase.Detached && previous == PlayerPhase.Retracting)
                AudioManager.Instance.Play(SoundId.MagnetRelease, player.Head.Motor.Position, 1f, 1f, this);
            previous = next;
        }
        private void Effect(PlayerEffect effect)
        {
            AudioManager audio = AudioManager.Instance;
            Vector2 body = player.Body.Motor.Position, head = player.Head.Motor.Position;
            switch (effect)
            {
                case PlayerEffect.HeadReturned: audio.Play(SoundId.MagnetJoin, body, 0.65f, 1.08f, this); break;
                case PlayerEffect.Joined: audio.Play(SoundId.MagnetJoin, body, 1f, 1f, this); break;
                case PlayerEffect.DistantJoined:
                case PlayerEffect.DownwardJoined:
                    audio.Play(SoundId.MagnetJoin, body, 1f, 0.9f, this);
                    audio.Play(SoundId.ImpactHeavy, body, 0.8f, 1f, this);
                    break;
                case PlayerEffect.RecallFailed: audio.Play(SoundId.RecallFailed, body, 1f, 1f, this); break;
                case PlayerEffect.PullBlocked:
                    audio.Play(SoundId.RecallFailed, body, 0.75f, 0.9f, this);
                    audio.Play(SoundId.ImpactLight, body, 0.8f, 0.9f, this);
                    break;
                case PlayerEffect.ExtensionBlocked: audio.Play(SoundId.ImpactLight, head, 0.8f, 1.15f, this); break;
            }
        }
        private void Contact(PlayerContactFeedback contact)
        {
            if (contact.Assembly || contact.Entity == null || contact.Speed < player.Config.ToWorld(0.5f)) return;
            bool heavy = contact.Speed >= player.Config.ToWorld(7f);
            float gain = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(contact.Speed / player.Config.ToWorld(10f)));
            AudioManager.Instance.Play(heavy ? SoundId.ImpactHeavy : SoundId.ImpactLight, contact.Point,
                gain, contact.Entity.Part == EntityPart.Head ? 1.15f : 0.95f, contact.Entity);
        }
        private void ReturnStarted(RespawnService.Journey journey)
        {
            AudioManager.Existing?.StopOwner(this);
            AudioManager.Instance.Play(SoundId.Death, journey.Origin, 1f, 1f, journey.Primary);
        }
        private void Returned(MovableEntity entity)
        {
            // A grouped return publishes two Returned events; one cue per completed return is enough.
            if (entity == player.Head && !player.Body.IsReturning && player.Phase != PlayerPhase.Detached) return;
            AudioManager.Instance.Play(SoundId.Respawn, entity.Motor.Position, 1f, 1f, this);
        }
        private void LateUpdate()
        {
            if (player == null || player.Head == null || player.Body == null) return;
            bool pulling = player.Phase == PlayerPhase.Pulling;
            bool active = !player.Body.IsReturning && !player.Head.IsReturning
                && (pulling || player.Phase == PlayerPhase.Holding || player.Phase == PlayerPhase.Extending || player.Phase == PlayerPhase.Retracting);
            AudioManager.Instance.SetLoop(SoundId.MagnetHum, this, active, player.Head.Motor.Position,
                pulling ? 1f : 0.3f, pulling ? 1.2f : 0.9f);
            previous = player.Phase; // Restored snapshots update the baseline without emitting a OneShot.
        }
    }
}
