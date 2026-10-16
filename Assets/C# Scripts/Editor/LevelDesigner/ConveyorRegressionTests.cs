using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Isolated project only: -batchmode -executeMethod TapTap.Editor.ConveyorRegressionTests.RunBatch.
    public static class ConveyorRegressionTests
    {
        private const string Folder = "Assets/ConveyorRegression";
        private const string Pending = "TapTap.ConveyorRegression.Pending";
        private static string Results => Path.Combine(Application.dataPath, "../conveyor-results.txt");
        private static LevelPalette palette;
        private static LevelBrush brush;
        private static IEnumerator runtime;
        private static double deadline;

        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project.");
            File.WriteAllText(Results, "");
            try
            {
                LevelPrefabBuilder.EnsureFolder(Folder);
                palette = LevelEditorAssets.EnsureDefaults();
                brush = palette.Brushes.Find(item => item != null && item.HandlerId == "conveyor");
                ConveyorTileVisual view = brush.Prefab.GetComponent<ConveyorTileVisual>();
                Check(view != null && view.LeftModel != null && view.MiddleModel != null && view.RightModel != null, "Conveyor model slots missing.");
                Check(view.ArrowGlowMaterial != null && view.ArrowGlowMaterial.IsKeywordEnabled("_EMISSION") &&
                    view.ArrowGlowMaterial.GetColor("_EmissionColor").maxColorComponent > 1f, "Preset glow material does not emit.");
                ConveyorTileAssets.Ensure();
                Check(brush.Prefab.GetComponents<ConveyorTileVisual>().Length == 1, "Asset setup duplicated the visual component.");
                Pass("Conveyor has three separate model slots and an HDR emissive preset; repeated setup preserves configured assets");
                GroupingAndGeneration();
                Editing();
                WorldOverlap();
                PhaseMath();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Runtime.unity");
                SessionState.SetBool(Pending, true); Subscribe(); EditorApplication.isPlaying = true;
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static LevelDefinition Level()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.Palette = palette; level.Size = new Vector2Int(20, 10); return level;
        }
        private static void Put(LevelDefinition level, int x, float speed = 1.25f) =>
            level.Put(new Vector2Int(x, 2), brush, new ConveyorPlacementSettings { Speed = speed });

        private static void GroupingAndGeneration()
        {
            var level = Level();
            Put(level, 2);
            Check(!ConveyorTileRules.Part(level, level.Placements[0]).HasValue, "Isolated conveyor received a single model.");
            Check(LevelPrefabBuilder.Validate(level).Count > 0, "Builder accepted a single-cell conveyor.");
            Put(level, 3);
            Check(ConveyorTileRules.Part(level, level.Placements[0]) == ConveyorPart.Left &&
                ConveyorTileRules.Part(level, level.Placements[1]) == ConveyorPart.Right, "Two-cell belt did not use two end modules.");
            GameObject pair = LevelPrefabBuilder.Save(level, Folder + "/Pair.prefab");
            Check(pair.GetComponentsInChildren<ConveyorTileVisual>(true).Length == 2 &&
                pair.GetComponentsInChildren<ConveyorArrowPulse>().Length == 0, "Two-cell belt generated a middle arrow.");
            for (int x = 4; x <= 6; x++) Put(level, x);
            GameObject right = LevelPrefabBuilder.Save(level, Folder + "/Right.prefab");
            int middles = 0;
            foreach (ConveyorTileVisual view in right.GetComponentsInChildren<ConveyorTileVisual>(true))
            {
                if (view.Part == ConveyorPart.Middle) middles++;
                Check(view.GetComponent<BoxCollider2D>().size == Vector2.one && view.GetComponent<BoxCollider2D>().offset == Vector2.zero,
                    "Model changed the original collision shape.");
                Check(view.GetComponent<WorldSurface>().Kind == SurfaceKind.Solid &&
                    Mathf.Approximately(view.GetComponent<WorldSurface>().ConveyorSpeed, 1.25f * palette.UnitSize), "Conveyor physics changed.");
            }
            Check(middles == 3 && right.GetComponentsInChildren<ConveyorArrowPulse>().Length == 3, "Long belt did not create one arrow per middle module.");
            foreach (LevelPlacement placement in level.Placements) ((ConveyorPlacementSettings)placement.Settings).Speed = -2f;
            GameObject left = LevelPrefabBuilder.Save(level, Folder + "/Left.prefab");
            foreach (ConveyorTileVisual view in left.GetComponentsInChildren<ConveyorTileVisual>(true))
            {
                Transform visual = view.transform.Find("Conveyor Visual (" + view.Part + ")");
                Check(visual != null && visual.localScale.x == (view.Part == ConveyorPart.Middle ? -1f : 1f), "Direction flipped the wrong module.");
            }
            AssetDatabase.CreateAsset(level, Folder + "/Belt.asset"); AssetDatabase.SaveAssetIfDirty(level);
            AssetDatabase.ImportAsset(Folder + "/Belt.asset", ImportAssetOptions.ForceUpdate);
            level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Folder + "/Belt.asset");
            Check(((ConveyorPlacementSettings)level.Placements[2].Settings).Speed == -2f, "Saved belt lost its speed.");
            ((ConveyorPlacementSettings)level.Placements[2].Settings).Speed = 2f;
            Check(LevelPrefabBuilder.Validate(level).Count > 0, "Opposing single-cell flow connected to its neighbours.");
            Pass("Single cells are rejected; two-cell and longer belts generate correct modules, preserve collision/speed, flip only middles and serialize settings");
        }

        private static void Editing()
        {
            var level = Level(); Put(level, 2); Put(level, 3); Put(level, 4);
            LevelPlacement middle = level.Placements[1];
            Check(LevelGridEditing.Apply(level, new[] { middle.Cell }, null, null, "geometry", true, false,
                () => Undo.RegisterCompleteObjectUndo(level, "Split conveyor")), "Erase failed.");
            Check(LevelPrefabBuilder.Validate(level).Count > 0, "Erasing a middle left two valid single cells.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(level.Placements.Count == 3 && LevelPrefabBuilder.Validate(level).Count == 0, "Undo did not restore the connected run.");
            LevelGridEditing.Mirror(level, true);
            Check(((ConveyorPlacementSettings)level.Placements[0].Settings).FacingLeft, "Mirror failed to reverse conveyor speed.");
            var copy = (ConveyorPlacementSettings)LevelGridEditing.CloneSettings(level.Placements[0].Settings);
            Check(copy.Speed == -1.25f && copy != level.Placements[0].Settings, "Copied conveyor shared or lost settings.");
            Object.DestroyImmediate(level);
            Pass("Erasing, undo, mirroring and copied settings update connectivity and retain conveyor direction");
        }

        private static void WorldOverlap()
        {
            var a = Level(); var b = Level();
            Put(a, 1); Put(a, 2); Put(a, 3); Put(b, 2); Put(b, 3);
            Check(WorldMapOverlap.ConflictCells(a, a.Footprint, Vector2Int.zero, b, b.Footprint, Vector2Int.zero).Contains(new Vector2Int(2, 2)),
                "World overlap treated a middle and end module as identical.");
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
            Pass("World overlap distinguishes conveyor end and middle modules using neighbouring context");
        }

        private static void PhaseMath()
        {
            foreach (bool left in new[] { false, true })
                for (int index = 0; index < 4; index++)
                {
                    double x = (left ? -1 : 1) * index, time = index * 0.2d + 0.01d;
                    Check(ConveyorArrowPulse.FlashAt(time, x, left, 1d, 0.05d), "Light sequence is opposite to motion.");
                    Check(!ConveyorArrowPulse.FlashAt(time + 0.1d, x, left, 1d, 0.05d), "Flash did not restore after its duration.");
                    Check(ConveyorArrowPulse.FlashAt(time + 10000d, x, left, 1d, 0.05d), "Cycle accumulated phase drift.");
                }
            Near(ConveyorArrowPulse.PhaseOffset(3.9d, false, 0.7d), 0.08d, "Forward phase remainder");
            Near(ConveyorArrowPulse.PhaseOffset(3.9d, true, 0.7d), 0.62d, "Reverse phase remainder");
            Near(ConveyorArrowPulse.PhaseOffset(-3.9d, false, 0.7d), 0.62d, "Negative-coordinate remainder");
            foreach (double x in new[] { -10000000.25d, -1d, 0d, 2.75d, 10000000.25d })
                foreach (bool left in new[] { false, true })
                {
                    double phase = ConveyorArrowPulse.PhaseOffset(x, left, 0.7d);
                    Check(phase >= 0d && phase < 0.7d, "Phase escaped its period.");
                }
            Check(!ConveyorArrowPulse.FlashAt(1d, 0d, false, 0d, 0.1d), "Invalid period was accepted.");
            foreach (bool left in new[] { false, true })
            {
                double phase = ConveyorArrowPulse.PhaseOffset(-3.5d, left, 1.5d);
                Near(ConveyorArrowPulse.IntensityAt(phase, -3.5d, left, 1.5d, 0.6d), 0d, "Fade starts dark");
                Near(ConveyorArrowPulse.IntensityAt(phase + 0.1d, -3.5d, left, 1.5d, 0.6d), 0.5d, "Fade-in midpoint");
                Near(ConveyorArrowPulse.IntensityAt(phase + 0.3d, -3.5d, left, 1.5d, 0.6d), 1d, "Fade holds peak");
                Near(ConveyorArrowPulse.IntensityAt(phase + 0.5d, -3.5d, left, 1.5d, 0.6d), 0.5d, "Fade-out midpoint");
                Near(ConveyorArrowPulse.IntensityAt(phase + 0.7d, -3.5d, left, 1.5d, 0.6d), 0d, "Fade ends dark");
                Near(ConveyorArrowPulse.IntensityAt(phase + 15000.1d, -3.5d, left, 1.5d, 0.6d), 0.5d, "Fade stays deterministic over many cycles");
            }
            Pass("Shared-clock flashes follow both motion directions; positive/negative world coordinates use only bounded phase remainders without accumulated drift");
        }

        private static void PlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false); runtime = Runtime(); deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += Tick;
        }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Conveyor runtime tests timed out.");
                if (runtime.MoveNext()) return;
                EditorApplication.update -= Tick; Finish();
            }
            catch (Exception exception) { EditorApplication.update -= Tick; Finish(exception); }
        }

        private static IEnumerator Runtime()
        {
            Material normal = AssetDatabase.LoadAssetAtPath<Material>(ConveyorTileAssets.ModelsFolder + "/ConveyorArrow.mat");
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(ConveyorTileAssets.GlowPath);
            var root = new GameObject("Material slot test");
            MeshRenderer renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterials = new[] { normal, normal };
            ConveyorArrowPulse pulse = root.AddComponent<ConveyorArrowPulse>();
            pulse.ArrowMaterials = new[] { new ConveyorArrowMaterialSlot { Renderer = renderer, MaterialIndex = 1 } };
            pulse.Configure(glow, 1f, 0.1f, false, true);
            pulse.RefreshAt(0.01d);
            Material animated = renderer.sharedMaterials[1];
            Check(pulse.IsLit && renderer.sharedMaterials[0] == normal && animated != normal && animated != glow,
                "Fade replaced a body material or failed to create a private arrow material.");
            Color lowEmission = animated.GetColor("_EmissionColor");
            pulse.RefreshAt(0.02d);
            Check(renderer.sharedMaterials[1] == animated && animated.GetColor("_EmissionColor").g > lowEmission.g,
                "Fade-in did not smoothly increase emission using the same material instance.");
            pulse.RefreshAt(0.05d);
            Check(animated.GetColor("_EmissionColor") == glow.GetColor("_EmissionColor"), "Fade did not reach the preset peak emission.");
            pulse.RefreshAt(0.09d);
            Check(animated.GetColor("_EmissionColor").g < glow.GetColor("_EmissionColor").g && pulse.IsLit,
                "Fade-out switched off abruptly instead of reducing emission.");
            Check(normal.GetColor("_EmissionColor").maxColorComponent == 0f, "Fade modified a shared source material.");
            pulse.RefreshAt(0.5d);
            Check(!pulse.IsLit && renderer.sharedMaterials[1] == normal, "Flash failed to restore original material.");
            pulse.RefreshAt(0.01d); pulse.enabled = false;
            Check(renderer.sharedMaterials[1] == normal, "Disabling arrow left the glow material behind.");
            pulse.enabled = true; pulse.Configure(glow, 1f, 0.1f, false, false); pulse.RefreshAt(0.01d);
            Check(!pulse.IsLit, "Stopped conveyor continued flashing.");
            Object.DestroyImmediate(root);
            Pass("Private arrow materials smoothly fade in and out, preserve body/shared materials, restore originals after flashes/disable, and stop when motion is zero");

            GameObject exported = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Left.prefab"));
            exported.transform.position = new Vector3(50f, 0f, 0f);
            yield return null;
            ConveyorTileVisual middle = null;
            foreach (ConveyorTileVisual tile in exported.GetComponentsInChildren<ConveyorTileVisual>())
                if (tile.Part == ConveyorPart.Middle) { middle = tile; break; }
            Check(middle != null && middle.FacingLeft, "Exported middle lost its direction.");
            ConveyorArrowPulse arrow = middle.transform.Find("Conveyor Visual (Middle)").GetComponentInChildren<ConveyorArrowPulse>();
            Check(arrow != null && arrow.FacingLeft && arrow.GlowMaterial == glow, "Exported arrow lost material/renderer/direction bindings.");
            Transform arrowGroup = arrow.transform.Find("Arrows");
            Check(arrowGroup != null && arrowGroup.childCount == 2 && arrow.ArrowMaterials.Length == 2,
                "Replacement model lost its two child arrow bindings.");
            Material[] originals = new Material[2];
            for (int i = 0; i < 2; i++)
            {
                Check(arrow.ArrowMaterials[i].Renderer.transform.IsChildOf(arrowGroup), "Flash still targets an obsolete placeholder.");
            }
            arrow.RefreshAt(ConveyorArrowPulse.PhaseOffset(arrow.transform.position.x, true, middle.FlashPeriod) + middle.FlashDuration + 0.1d);
            for (int i = 0; i < 2; i++) originals[i] = arrow.ArrowMaterials[i].Renderer.sharedMaterial;
            double phase = ConveyorArrowPulse.PhaseOffset(arrow.transform.position.x, true, middle.FlashPeriod);
            arrow.RefreshAt(phase + middle.FlashDuration * 0.5d); Check(arrow.IsLit, "Exported arrow did not flash at its world-coordinate phase.");
            for (int i = 0; i < 2; i++)
                Check(arrow.ArrowMaterials[i].Renderer.sharedMaterial.GetColor("_EmissionColor") == glow.GetColor("_EmissionColor"),
                    "One of the replacement chevrons failed to reach peak emission.");
            arrow.RefreshAt(phase + middle.FlashDuration + 0.1d);
            for (int i = 0; i < 2; i++)
                Check(arrow.ArrowMaterials[i].Renderer.sharedMaterial == originals[i], "Replacement chevron did not restore its original material.");
            middle.GetComponent<WorldSurface>().ConveyorSpeed = 2f;
            yield return null; yield return null;
            Check(!middle.FacingLeft && middle.transform.Find("Conveyor Visual (Middle)").localScale.x == 1f && !arrow.FacingLeft,
                "Runtime reversal failed to update middle model and arrow phase.");
            middle.GetComponent<WorldSurface>().ConveyorSpeed = -2f; middle.RefreshMotion();
            middle.GetComponent<WorldSurface>().ConveyorSpeed = 0f; middle.RefreshMotion();
            Check(middle.FacingLeft && !arrow.IsLit, "Stopping failed to preserve the last direction and turn off arrows.");
            Object.DestroyImmediate(exported);
            Pass("Exported prefab retains arrow bindings and uses world-space phase; runtime reversal flips middle models and stopped belts retain their last direction");

            if (RewindManager.Current != null) Object.DestroyImmediate(RewindManager.Current.gameObject);
            GameObject clockObject = new GameObject("Conveyor clock test");
            RewindManager manager = clockObject.AddComponent<RewindManager>(); manager.UseKeyboard = false;
            for (int i = 0; i < 30; i++) manager.SimulateStep(Time.fixedDeltaTime);
            double before = manager.SimulationTime;
            Near(ConveyorArrowPulse.Clock, before, "Shared simulation clock");
            Check(manager.BeginRewind(), "Cannot begin rewind for clock test."); manager.RewindBy(Time.fixedDeltaTime * 5);
            Check(manager.SimulationTime < before, "Simulation clock did not rewind.");
            Near(ConveyorArrowPulse.Clock, manager.SimulationTime, "Rewound arrow clock");
            manager.EndRewind(); Object.DestroyImmediate(clockObject);
            Pass("Arrow timing follows the shared simulation clock through rewind instead of accumulating per-object timers");
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                RenderPreview();
        }

        private static void RenderPreview()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            GameObject right = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Right.prefab"));
            GameObject left = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Left.prefab"));
            left.transform.position = new Vector3(0f, -2f, 0f);
            foreach (ConveyorArrowPulse arrow in right.GetComponentsInChildren<ConveyorArrowPulse>()) arrow.RefreshAt(1d);
            foreach (ConveyorArrowPulse arrow in left.GetComponentsInChildren<ConveyorArrowPulse>()) arrow.RefreshAt(1d);
            var cameraObject = new GameObject("Conveyor Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(4.5f, 1.5f, -10f);
            camera.orthographic = true; camera.orthographicSize = 2.6f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            var lightObject = new GameObject("Conveyor Preview Light");
            Light light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            var target = new RenderTexture(1080, 540, 24);
            var pixels = new Texture2D(1080, 540, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1080, 540), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../conveyor-preview.png"), pixels.EncodeToPNG());
                int brightArrowPixels = 0;
                foreach (Color32 pixel in pixels.GetPixels32())
                    if (pixel.g > 210 && pixel.b > 160 && pixel.r < pixel.g * 0.6f) brightArrowPixels++;
                Check(brightArrowPixels > 20, "Rendered emissive arrows are missing their fluorescent teal colour.");
                Pass("Graphics device rendered both conveyor directions and emissive/initial arrow materials to conveyor-preview.png");
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous; target.Release();
                Object.DestroyImmediate(pixels); Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
                Object.DestroyImmediate(right); Object.DestroyImmediate(left);
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }

        private static void Near(double actual, double expected, string message) => Check(Math.Abs(actual - expected) < 0.000001d, message);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Results, "PASS: " + message + "\n"); Debug.Log("CONVEYOR PASS: " + message); }
        private static void Finish(Exception exception = null)
        {
            if (exception != null) { File.AppendAllText(Results, "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
