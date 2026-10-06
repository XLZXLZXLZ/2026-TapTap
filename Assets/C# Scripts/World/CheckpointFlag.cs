using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class CheckpointFlag : MonoBehaviour
    {
        public Vector2 SpawnOffset = new Vector2(0f, 0.45f);
        public Vector2 SpawnPosition => (Vector2)transform.position + SpawnOffset;

        private void Awake()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        private void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }
    }
}
