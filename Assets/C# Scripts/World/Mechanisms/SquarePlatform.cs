using System;
using UnityEngine;
using UnityEngine.Events;

namespace TapTap
{
    // Full-square geometry uses exactly the same one-way rules as a thin platform.
    // Animation consumers can subscribe here without introducing another surface kind.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D), typeof(WorldSurface))]
    public sealed class SquarePlatform : MonoBehaviour
    {
        [Serializable] public sealed class HeadEvent : UnityEvent<MovableEntity> { }
        [SerializeField] private HeadEvent onHeadPassedUpward = new HeadEvent();
        [SerializeField] private HeadEvent onHeadLanded = new HeadEvent();
        public event Action<MovableEntity> HeadPassedUpward;
        public event Action<MovableEntity> HeadLanded;

        public void NotifyHeadPassedUpward(MovableEntity head)
        {
            if (!CanNotify(head)) return;
            HeadPassedUpward?.Invoke(head);
            onHeadPassedUpward.Invoke(head);
        }

        public void NotifyHeadLanded(MovableEntity head)
        {
            if (!CanNotify(head)) return;
            HeadLanded?.Invoke(head);
            onHeadLanded.Invoke(head);
        }

        private static bool CanNotify(MovableEntity head) => head != null && head.Part == EntityPart.Head
            && !head.IsReturning && !RewindManager.Rewinding;

        private void Reset()
        {
            GetComponent<WorldSurface>().Configure(SurfaceKind.OneWay);
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            box.size = Vector2.one;
            box.offset = Vector2.zero;
        }
    }
}
