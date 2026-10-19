using UnityEngine;

// The platform remains solid. Nearby colliders only bend the grass visually.
[DisallowMultipleComponent]
[AddComponentMenu("Environment/Platform Grass Interaction")]
public sealed class PlatformGrassInteraction : MonoBehaviour
{
    public BoxCollider platform;
    [Range(0.2f, 3f)] public float interactionHeight = 1.4f;
    [Range(0f, 1.5f)] public float extraRadius = 0.4f;
    [Range(0.05f, 2f)] public float recoveryTime = 0.45f;
    public LayerMask interactionLayers = ~0;
    public int ActiveContactCount { get; private set; }
    const int Capacity = 16;
    readonly Collider[] overlaps = new Collider[128];
    readonly Collider[] owners = new Collider[Capacity];
    readonly bool[] touched = new bool[Capacity];
    readonly Vector4[] contacts = new Vector4[Capacity];
    readonly Vector4[] strengths = new Vector4[Capacity];
    static readonly int ContactId = Shader.PropertyToID("_CodexPlatformGrassContacts");
    static readonly int StrengthId = Shader.PropertyToID("_CodexPlatformGrassStrengths");

    void Reset() { platform = GetComponent<BoxCollider>(); }
    void OnEnable() { ClearContacts(); }
    void LateUpdate() { RefreshInteractions(Time.deltaTime); }

    public void ClearContacts()
    {
        for (int i = 0; i < Capacity; i++) { owners[i] = null; contacts[i] = Vector4.zero; strengths[i] = Vector4.zero; }
        ActiveContactCount = 0;
        Publish();
    }

    public void RefreshInteractions(float deltaTime)
    {
        if (!platform || !platform.enabled) { ClearContacts(); return; }
        Transform p = platform.transform;
        Vector3 scale = p.lossyScale;
        Vector3 top = p.TransformPoint(platform.center + Vector3.up * platform.size.y * 0.5f);
        Vector3 half = new Vector3(Mathf.Abs(scale.x * platform.size.x) * 0.5f + 0.6f,
            interactionHeight * 0.5f, Mathf.Abs(scale.z * platform.size.z) * 0.5f + 0.6f);
        // Include props moved by Transform during this frame, before the next physics step.
        Physics.SyncTransforms();
        int count = Physics.OverlapBoxNonAlloc(top + p.up * (half.y - 0.05f), half, overlaps,
            p.rotation, interactionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < Capacity; i++) touched[i] = false;
        for (int n = 0; n < count; n++)
        {
            Collider c = overlaps[n];
            if (!c || c == platform || c.transform.IsChildOf(transform) || c is TerrainCollider) continue;
            Bounds b = c.bounds;
            // Ignore the surrounding landscape surfaces; characters and movable props are included.
            if (b.max.y < top.y + 0.08f || (!c.attachedRigidbody && !(c is CharacterController) &&
                (b.size.x > 5f || b.size.z > 5f || b.min.y < top.y - 0.65f))) continue;
            int slot = -1;
            for (int i = 0; i < Capacity; i++) if (owners[i] == c) { slot = i; break; }
            if (slot < 0) for (int i = 0; i < Capacity; i++) if (strengths[i].x < 0.005f) { slot = i; break; }
            if (slot < 0) continue;
            owners[slot] = c;
            touched[slot] = true;
            float radius = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) + extraRadius, 0.35f, 2.8f);
            contacts[slot] = new Vector4(b.center.x, top.y, b.center.z, radius);
        }
        ActiveContactCount = 0;
        for (int i = 0; i < Capacity; i++)
        {
            strengths[i].x = Mathf.MoveTowards(strengths[i].x, touched[i] ? 1f : 0f,
                deltaTime / (touched[i] ? 0.09f : Mathf.Max(0.05f, recoveryTime)));
            if (touched[i]) ActiveContactCount++;
            if (strengths[i].x <= 0f) owners[i] = null;
        }
        Publish();
    }

    void Publish() { Shader.SetGlobalVectorArray(ContactId, contacts); Shader.SetGlobalVectorArray(StrengthId, strengths); }
    void OnDisable() { ClearContacts(); }
}
