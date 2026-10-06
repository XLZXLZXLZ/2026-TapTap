using UnityEngine;

namespace TapTap
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class LevelSceneRuntime : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private PlayerConfig playerConfig;
        [SerializeField] private DemoCamera cameraRig;
        [SerializeField] private PrototypeHUD hud;
        [SerializeField] private PlayerController player;
        [SerializeField] private LevelRegion region;
        [SerializeField] private Vector2 fallbackSpawnLocal;
        [SerializeField] private bool placePlayerAtSpawn = true;

        public GameObject PlayerPrefab => playerPrefab;
        public PlayerConfig PlayerConfig => playerConfig;
        public DemoCamera CameraRig => cameraRig;

        public void ConfigureResources(GameObject character, PlayerConfig settings, DemoCamera camera, PrototypeHUD overlay)
        {
            playerPrefab = character;
            playerConfig = settings;
            cameraRig = camera;
            hud = overlay;
        }

        public void Configure(PlayerController character, LevelRegion level, PlayerConfig settings, Vector2 spawn)
        {
            player = character;
            region = level;
            if (settings != null) playerConfig = settings;
            fallbackSpawnLocal = region != null ? (Vector2)region.transform.InverseTransformPoint(spawn) : spawn;
            if (cameraRig != null) cameraRig.BindPlayer(player);
            if (hud != null) hud.Configure(player, playerConfig);
        }

        private void Start()
        {
            if (player == null || playerConfig == null) return;
            var respawn = player.GetComponent<RespawnService>();
            player.Configure(player.GetComponent<PlayerInput>(), playerConfig, player.Body, player.Head,
                respawn, player.GetComponent<PlayerView>());
            if (placePlayerAtSpawn && region != null)
            {
                Vector2 spawn = region.EntryCheckpoint != null ? region.EntrySpawnPosition :
                    (Vector2)region.transform.TransformPoint(fallbackSpawnLocal);
                player.TeleportJoined(spawn);
            }
            if (respawn != null) respawn.SetCheckpoint(player.Body.transform.position);
            if (cameraRig != null)
            {
                cameraRig.BindPlayer(player);
                cameraRig.SnapToPlayer();
            }
            if (hud != null) hud.Configure(player, playerConfig);
        }
    }
}
