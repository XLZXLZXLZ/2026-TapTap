using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [AddComponentMenu("TapTap/World/Level Endpoint")]
    public sealed class LevelEndpoint : MonoBehaviour
    {
        [Tooltip("Identifier reserved for future level completion or exit routing.")]
        public string EndpointId = "finish";
        public string Label = "终点";
    }
}
