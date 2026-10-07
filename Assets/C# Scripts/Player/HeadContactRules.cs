using UnityEngine;

namespace TapTap
{
    public enum HeadContactAction { None, BounceEntity, AssembleDownward, PickupHead }

    public static class HeadContactRules
    {
        public static HeadContactAction Evaluate(
            EntityPart movingPart, EntityPart contactedPart, Vector2 normal)
        {
            if (movingPart == EntityPart.Body && contactedPart == EntityPart.Head
                && Mathf.Abs(normal.x) > 0.5f)
                return HeadContactAction.PickupHead;
            if (normal.y < 0.5f) return HeadContactAction.None;
            if (movingPart == EntityPart.Head && contactedPart == EntityPart.Body)
                return HeadContactAction.AssembleDownward;
            if (contactedPart == EntityPart.Head)
                return HeadContactAction.BounceEntity;
            return HeadContactAction.None;
        }
    }
}
