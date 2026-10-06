using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TapTap.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TapTap.Tests
{
    public sealed class LevelEditorUndoTests
    {
        private string folder;
        private LevelDefinition layout;
        private LevelPalette palette;
        private LevelEditorWindow window;
        private LevelBrush wall;
        private LevelBrush conveyor;
        private LevelBrush checkpoint;
        private LevelBrush phaseBlock;

        [SetUp]
        public void SetUp()
        {
            folder = "Assets/__LevelUndoTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            wall = CreateBrush("Wall", "default", "geometry");
            conveyor = CreateBrush("Conveyor", "conveyor", "geometry");
            checkpoint = CreateBrush("Checkpoint", "checkpoint", "markers");
            phaseBlock = CreateBrush("Phase", "phase-block", "geometry");
            palette = ScriptableObject.CreateInstance<LevelPalette>();
            palette.Brushes.AddRange(new[] { wall, conveyor, checkpoint, phaseBlock });
            AssetDatabase.CreateAsset(palette, folder + "/Palette.asset");
            layout = ScriptableObject.CreateInstance<LevelDefinition>();
            layout.Palette = palette;
            layout.Put(new Vector2Int(0, 0), wall, new EmptyPlacementSettings());
            layout.Put(new Vector2Int(1, 0), conveyor, new ConveyorPlacementSettings { Speed = -3f });
            layout.Put(new Vector2Int(2, 0), phaseBlock, new PhaseBlockPlacementSettings { SolidWhenActive = false });
            layout.EntryPlacementId = layout.Put(new Vector2Int(0, 0), checkpoint,
                new CheckpointPlacementSettings { SpawnOffset = new Vector2(0.3f, 0.7f) }).Id;
            AssetDatabase.CreateAsset(layout, folder + "/Layout.asset");
            AssetDatabase.SaveAssets();
            window = ScriptableObject.CreateInstance<LevelEditorWindow>();
            Invoke("UseLayout", layout);
            Undo.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            if (window != null) Object.DestroyImmediate(window);
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(folder);
        }

        [UnityTest]
        public IEnumerator ConsecutiveMultiFrameStrokesUndoAndRedoOnlyTheirOwnCells()
        {
            var states = new List<string[]> { Snapshot() };
            for (int stroke = 0; stroke < 3; stroke++)
            {
                UseBrush(conveyor, new ConveyorPlacementSettings { Speed = stroke + 2f });
                Invoke("BeginStroke", 0);
                for (int x = 0; x < 6; x++)
                {
                    Invoke("PaintLine", new Vector2Int(x, stroke + 1), new Vector2Int(x, stroke + 1), false);
                    Undo.FlushUndoRecordObjects();
                    yield return null;
                }
                Invoke("EndStroke");
                states.Add(Snapshot());
                yield return null;
            }

            for (int stroke = 2; stroke >= 0; stroke--)
            {
                Undo.PerformUndo();
                CollectionAssert.AreEqual(states[stroke], Snapshot());
                yield return null;
            }
            for (int stroke = 1; stroke < states.Count; stroke++)
            {
                Undo.PerformRedo();
                CollectionAssert.AreEqual(states[stroke], Snapshot());
                yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ErasingAllLayersRestoresConfigurationAndEntryOnUndo()
        {
            string[] before = Snapshot();
            UseBrush(wall, new EmptyPlacementSettings());
            Invoke("BeginStroke", 1);
            Invoke("ApplyCells", new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) }, true);
            Invoke("EndStroke");
            string[] erased = Snapshot();
            Assert.That(layout.Placements, Is.Empty);

            Undo.PerformUndo();
            CollectionAssert.AreEqual(before, Snapshot());
            Undo.PerformRedo();
            CollectionAssert.AreEqual(erased, Snapshot());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReplacingPlacementSettingsTypePreservesOtherCellsOnUndoAndRedo()
        {
            string[] before = Snapshot();
            UseBrush(conveyor, new ConveyorPlacementSettings { Speed = 5f });
            Invoke("BeginStroke", 0);
            Invoke("ApplyCells", new[] { new Vector2Int(0, 0), new Vector2Int(2, 0) }, false);
            Invoke("EndStroke");
            string[] painted = Snapshot();

            Undo.PerformUndo();
            CollectionAssert.AreEqual(before, Snapshot());
            Undo.PerformRedo();
            CollectionAssert.AreEqual(painted, Snapshot());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NoOpStrokeDoesNotConsumeUndoAndUndoMarksPrefabOutOfDate()
        {
            string[] before = Snapshot();
            UseBrush(conveyor, new ConveyorPlacementSettings { Speed = 5f });
            Vector2Int cell = new Vector2Int(4, 1);
            Invoke("BeginStroke", 0);
            Invoke("PaintLine", cell, cell, false);
            Invoke("EndStroke");
            string[] painted = Snapshot();
            Invoke("BeginStroke", 0);
            Invoke("PaintLine", cell, cell, false);
            Assert.That(typeof(LevelEditorWindow).GetField("strokeUndoRecorded", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(window), Is.False, "Painting identical settings must not register another undo snapshot.");
            Invoke("EndStroke");
            CollectionAssert.AreEqual(painted, Snapshot());
            SetField("prefabNeedsUpdate", false);

            Undo.PerformUndo();

            CollectionAssert.AreEqual(before, Snapshot());
            Assert.That(typeof(LevelEditorWindow).GetField("prefabNeedsUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(window), Is.True);
            Undo.PerformRedo();
            CollectionAssert.AreEqual(painted, Snapshot());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CutPasteAndMoveRestoreFullPlacementSettingsAcrossUndoAndRedo()
        {
            SetField("selectionArea", new RectInt(0, 0, 3, 1));
            SetField("hasAreaSelection", true);
            Assert.That(Invoke("CopyArea"), Is.True);
            var states = new List<string[]> { Snapshot() };
            Invoke("DeleteArea");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            states.Add(Snapshot());
            SetField("clipboardIsCut", true);
            Invoke("PasteArea", new Vector2Int(5, 2));
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            states.Add(Snapshot());
            SetField("selectionArea", new RectInt(5, 2, 3, 1));
            SetField("hasAreaSelection", true);
            Invoke("MoveArea", Vector2Int.up);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            states.Add(Snapshot());

            for (int i = states.Count - 2; i >= 0; i--)
            {
                Undo.PerformUndo();
                CollectionAssert.AreEqual(states[i], Snapshot());
            }
            for (int i = 1; i < states.Count; i++)
            {
                Undo.PerformRedo();
                CollectionAssert.AreEqual(states[i], Snapshot());
            }
            LogAssert.NoUnexpectedReceived();
        }

        private LevelBrush CreateBrush(string name, string handler, string layer)
        {
            var brush = ScriptableObject.CreateInstance<LevelBrush>();
            brush.HandlerId = handler;
            brush.LayerId = layer;
            AssetDatabase.CreateAsset(brush, folder + "/" + name + ".asset");
            return brush;
        }

        private void UseBrush(LevelBrush brush, LevelPlacementSettings settings)
        {
            SetField("sampledBrush", brush);
            SetField("settingsBrush", brush);
            SetField("paintSettings", settings);
        }

        private string[] Snapshot()
        {
            Assert.That(layout.Placements.All(item => item != null && item.Brush != null && item.Settings != null),
                Is.True, "Undo must retain every placement and its managed settings reference.");
            return layout.Placements.Select(item => item.Id + ":" + item.Cell + ":" + item.Brush.name + ":"
                    + item.Settings.GetType().Name + ":" + JsonUtility.ToJson(item.Settings))
                .OrderBy(item => item, StringComparer.Ordinal).Concat(new[] { "entry:" + layout.EntryPlacementId }).ToArray();
        }

        private object Invoke(string name, params object[] arguments)
        {
            return typeof(LevelEditorWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, arguments);
        }

        private void SetField(string name, object value)
        {
            typeof(LevelEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value);
        }
    }
}
