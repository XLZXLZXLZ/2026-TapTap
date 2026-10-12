using UnityEngine;

namespace TapTap
{
    // An entry scene can use the existing individual level Prefabs without altering the authored map.
    [DefaultExecutionOrder(-150)]
    public sealed class WorldMapGameplayBootstrap : MonoBehaviour
    {
        public WorldMapDefinition World;
        public GameObject RuntimePrefab;

        private void Awake()
        {
            if (World == null || RuntimePrefab == null) { Debug.LogError("游玩入口缺少大世界或 Runtime 配置。", this); return; }
            var modules = World.OrderedModules();
            if (modules.Count == 0) return;
            string selected = WorldMapSceneRouter.PendingModule(World);
            WorldMapModule first = modules.Find(module => module.Id == selected) ?? modules[0];
            var root = new GameObject(World.name);
            root.SetActive(false);
            var generated = new GameObject("Generated");
            generated.transform.SetParent(root.transform, false);
            WorldMapRoot world = root.AddComponent<WorldMapRoot>();
            world.Source = World;
            foreach (WorldMapModule module in modules)
            {
                if (module.Level.OutputPrefab == null)
                {
                    Debug.LogError(module.DisplayName + " 缺少关卡 Prefab，请重新生成单关。", this);
                    Destroy(root); return;
                }
                GameObject instance = Instantiate(module.Level.OutputPrefab, generated.transform, false);
                LevelRegion region = instance.GetComponent<LevelRegion>();
                region.Source = module.Level;
                region.WorldModuleId = module.Id;
                region.SequenceNumber = World.SequenceNumber(module);
                instance.transform.localPosition = (Vector2)module.Position * region.UnitSize;
                if (region.EntryCheckpoint == null) region.EntryCheckpoint = instance.GetComponentInChildren<CheckpointFlag>(true);
                instance.SetActive(module == first);
            }
            GameObject resources = Instantiate(RuntimePrefab);
            LevelSceneRuntime runtime = resources.GetComponent<LevelSceneRuntime>();
            PlayerConfig config = first.Level.Palette.Config ?? runtime.PlayerConfig;
            GameObject playerObject = Instantiate(runtime.PlayerPrefab);
            playerObject.transform.localScale = Vector3.Scale(playerObject.transform.localScale,
                new Vector3(config.UnitSize, config.UnitSize, 1f));
            PlayerController player = playerObject.GetComponent<PlayerController>();
            player.Configure(player.GetComponent<PlayerInput>(), config, player.Body, player.Head,
                player.GetComponent<RespawnService>(), player.GetComponent<PlayerView>());
            LevelRegion entry = world.FindRegion(first);
            runtime.Configure(player, entry, config, entry.EntrySpawnPosition);
            root.SetActive(true);
        }
    }
}
