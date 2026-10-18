using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapTap
{
    public static class ReturnJourneyContact
    {
        public static PlayerController FindPlayer(Scene scene)
        {
            foreach (PlayerController player in Object.FindObjectsOfType<PlayerController>())
                if (player.gameObject.scene == scene) return player;
            return null;
        }
        public static bool CanTrigger(PlayerController player) => player != null && player.Body != null
            && player.Body.Motor != null && !player.Body.IsReturning && !WorldMapProgression.InputBlocked
            && !RewindManager.Rewinding && Time.timeScale > 0f
            && (SceneResetManager.Existing == null || !SceneResetManager.Existing.IsResetting);

        // Works with the kinematic controller without Rigidbody2D trigger callbacks.
        public static bool TouchesBody(BoxCollider2D area, PlayerController player)
        {
            if (area == null || !area.enabled || !area.gameObject.activeInHierarchy || player == null || player.Body == null) return false;
            KinematicMotor2D motor = player.Body.Motor;
            Bounds body = new Bounds(motor.Position + motor.CenterOffset, motor.Size);
            Bounds trigger = area.bounds;
            trigger.Expand(new Vector3(0f, 0f, 1f));
            if (trigger.Intersects(body)) return true;
            trigger.Expand(body.size);
            foreach (MotionSegment segment in motor.MotionSegments)
            {
                Vector2 delta = segment.To - segment.From;
                if (delta.sqrMagnitude < 0.000001f) continue;
                if (trigger.IntersectRay(new Ray(segment.From + motor.CenterOffset, delta.normalized), out float distance)
                    && distance <= delta.magnitude) return true;
            }
            return false;
        }
    }
}
