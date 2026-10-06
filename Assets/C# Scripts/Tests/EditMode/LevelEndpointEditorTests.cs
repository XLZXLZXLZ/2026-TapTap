using System;
using System.IO;
using NUnit.Framework;
using TapTap.Editor;
using UnityEditor;
using UnityEngine;

namespace TapTap.Tests
{
    public sealed class LevelEndpointEditorTests
    {
        private string folder;
        private LevelDefinition layout;
        private PlayerConfig config;
        private LevelPalette palette;
        private LevelBrush brush;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__EndpointTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            brush = LevelEditorAssets.EnsureEndpointBrush();
            config = ScriptableObject.CreateInstance<PlayerConfig>();
            config.UnitSize = 2f;
            AssetDatabase.CreateAsset(config, folder + "/Config.asset");
            palette = ScriptableObject.CreateInstance<LevelPalette>();
            palette.Config = config;
            palette.Brushes.Add(brush);
            AssetDatabase.CreateAsset(palette, folder + "/Palette.asset");
            layout = ScriptableObject.CreateInstance<LevelDefinition>();
            layout.Palette = palette;
            AssetDatabase.CreateAsset(layout, folder + "/Layout.asset");
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(folder);

        [Test]
        public void EndpointBrushIsDiscoverableAndAssetCreationIsIdempotent()
        {
            Assert.That(brush.HandlerId, Is.EqualTo("endpoint"));
            Assert.That(brush.LayerId, Is.EqualTo("markers"));
            Assert.That(LevelBrushHandlers.Get(brush.HandlerId), Is.TypeOf<EndpointLevelBrushHandler>());
            Assert.That(LevelEditorAssets.EnsureEndpointBrush(), Is.SameAs(brush));
            Assert.That(brush.Prefab.GetComponent<LevelEndpoint>(), Is.Not.Null);
            Assert.That(brush.Prefab.GetComponentsInChildren<Collider2D>(), Is.Empty);
        }

        [Test]
        public void EndpointConfigurationSurvivesLayoutAssetReload()
        {
            layout.Put(new Vector2Int(3, 2), brush, new EndpointPlacementSettings { EndpointId = "exit_right", Label = "右侧出口" });
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssetIfDirty(layout);
            string path = folder + "/Layout.asset";
            Assert.That(File.ReadAllText(path), Does.Contain("EndpointPlacementSettings"));
            Resources.UnloadAsset(layout);
            layout = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            var settings = layout.Find(new Vector2Int(3, 2), "markers").Settings as EndpointPlacementSettings;
            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.EndpointId, Is.EqualTo("exit_right"));
            Assert.That(settings.Label, Is.EqualTo("右侧出口"));
        }

        [Test]
        public void GeneratedRegionPreservesEndpointMetadataAndGridScale()
        {
            layout.Put(new Vector2Int(3, 2), brush, new EndpointPlacementSettings { EndpointId = "exit_right", Label = "右侧出口" });
            GameObject prefab = LevelPrefabBuilder.Save(layout, folder + "/Region.prefab");
            LevelEndpoint endpoint = prefab.GetComponentInChildren<LevelEndpoint>();
            Assert.That(endpoint, Is.Not.Null);
            Assert.That(endpoint.EndpointId, Is.EqualTo("exit_right"));
            Assert.That(endpoint.Label, Is.EqualTo("右侧出口"));
            Assert.That(endpoint.transform.localPosition, Is.EqualTo(new Vector3(7f, 4f, 0f)));
            Assert.That(endpoint.transform.localScale, Is.EqualTo(new Vector3(2f, 2f, 1f)));
            Assert.That(prefab.GetComponent<LevelRegion>().EntryCheckpoint, Is.Null);
        }

        [Test]
        public void MissingOrEmptyEndpointSettingsBlockExport()
        {
            LevelPlacement placement = layout.Put(new Vector2Int(3, 2), brush, new EmptyPlacementSettings());
            Assert.That(LevelPrefabBuilder.Validate(layout), Has.Some.Contains("终点缺少配置"));
            placement.Settings = new EndpointPlacementSettings { EndpointId = "  " };
            Assert.That(LevelPrefabBuilder.Validate(layout), Has.Some.Contains("终点标识不能为空"));
            Assert.Throws<InvalidOperationException>(() => LevelPrefabBuilder.Save(layout, folder + "/Invalid.prefab"));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Invalid.prefab"), Is.Null);
        }
    }
}
