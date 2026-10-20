using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TapTap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Rendering checks use a real graphics device. Never run this against an open editing project.
public static class WorldExplorationMaskValidation
{
    private const string Output = "Logs/ExplorationMask";
    private static readonly List<string> results = new List<string>();
    private const string PlayKey = "TapTap.ExplorationMask.PlayCheck";
    private static int playStage;
    private static int resumeFrame;
    private static double deadline;

    [InitializeOnLoadMethod]
    private static void ResumePlayCheck()
    {
        if (!SessionState.GetBool(PlayKey, false)) return;
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update -= TickPlayCheck;
        EditorApplication.update += TickPlayCheck;
    }

    public static void RunPlayBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        Directory.CreateDirectory(Output);
        EditorSceneManager.OpenScene("Assets/Scenes/WorldMaps/World_Character_Play.unity");
        SessionState.SetBool(PlayKey, true);
        ResumePlayCheck();
        EditorApplication.EnterPlaymode();
    }

    private static void TickPlayCheck()
    {
        if (!SessionState.GetBool(PlayKey, false)) return;
        try
        {
            Check(EditorApplication.timeSinceStartup < deadline, "Runtime validation timed out");
            if (!EditorApplication.isPlaying || Time.frameCount < 10 || Time.frameCount < resumeFrame) return;
            WorldMapProgression flow = WorldMapProgression.Current;
            var player = UnityEngine.Object.FindObjectOfType<PlayerController>();
            var rig = UnityEngine.Object.FindObjectOfType<DemoCamera>();
            if (flow == null || flow.CurrentRegion == null || player == null || rig == null) return;
            Time.timeScale = 0;
            var mask = flow.GetComponent<WorldExplorationMask>();
            Check(mask != null, "Authored world did not create its exploration mask");
            void CaptureRuntime(string name)
            {
                Texture2D image = Render(rig.ViewCamera, 1200, 750);
                try
                {
                    Color32[] pixels = image.GetPixels32();
                    Check(pixels.Count(p => p.r > 230 && p.g < 25 && p.b > 230) < pixels.Length / 1000, "Runtime has pink error materials");
                    Check(pixels.Count(p => p.r > 8 || p.g > 8 || p.b > 8) > 1000, "Runtime scene is completely masked");
                    File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
            }
            if (playStage == 0)
            {
                CaptureRuntime("runtime-01-start");
                var environment = mask.Background;
                Check(environment != null && environment.CanRender, "Authored EnvironmentParticle was not connected");
                var particles = environment.GetComponent<ParticleSystem>();
                Vector3 field = particles.shape.scale;
                Check(Mathf.Abs(particles.emission.rateOverTime.constant / (field.x * field.y) - 15f / 10000f) < 0.00001f,
                    "Actual environment particle density changed");
                Check(particles.main.maxParticles == Mathf.CeilToInt(1000 * environment.AreaRatio), "Actual particle capacity was not expanded");
                Bounds bounds = flow.GetComponent<WorldMapRoot>().WorldBounds;
                Vector3 fieldCenter = particles.transform.TransformPoint(particles.shape.position);
                Check(fieldCenter.x - field.x * 0.5f <= bounds.min.x && fieldCenter.x + field.x * 0.5f >= bounds.max.x
                    && fieldCenter.y - field.y * 0.5f <= bounds.min.y && fieldCenter.y + field.y * 0.5f >= bounds.max.y,
                    "Environment emitter does not cover the whole World");
                results.Add("PASS: Actual emitter covers the entire World at " + field.x + " x " + field.y
                    + ", rate " + particles.emission.rateOverTime.constant + "/s; original density retained.");
                // Compare over the complete World so sparse authored particles produce a stable sample.
                particles.Simulate(30f, false, true, true); particles.Pause(false);
                Camera view = rig.ViewCamera;
                float distance = (bounds.size.y * 0.6f + 16f) / Mathf.Tan(20f * Mathf.Deg2Rad);
                view.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.center.y, -distance), Quaternion.identity);
                view.usePhysicalProperties = false; view.fieldOfView = 40f;
                var withParticles = Render(view, 1200, 750);
                var liveParticles = new ParticleSystem.Particle[particles.particleCount];
                int liveCount = particles.GetParticles(liveParticles);
                particles.Clear(false);
                var baseOnly = Render(view, 1200, 750);
                particles.SetParticles(liveParticles, liveCount);
                environment.enabled = false;
                var withoutParticles = Render(view, 1200, 750);
                environment.enabled = true;
                try
                {
                    Color32[] live = withParticles.GetPixels32(), empty = baseOnly.GetPixels32(), fallback = withoutParticles.GetPixels32();
                    int particlePixels = 0;
                    for (int pixel = 0; pixel < live.Length; pixel++)
                        if (fallback[pixel].r < 3 && fallback[pixel].g < 3 && fallback[pixel].b < 3
                            && (Mathf.Abs(live[pixel].r - empty[pixel].r) > 4 || Mathf.Abs(live[pixel].g - empty[pixel].g) > 4
                                || Mathf.Abs(live[pixel].b - empty[pixel].b) > 4)) particlePixels++;
                    Check(particlePixels > 100, "Actual particles did not render into the masked background");
                    File.WriteAllBytes(Output + "/runtime-05-world-particles.png", withParticles.EncodeToPNG());
                    results.Add("PASS: Actual World backdrop renders particles in " + particlePixels + " previously masked pixels.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(withParticles); UnityEngine.Object.DestroyImmediate(baseOnly);
                    UnityEngine.Object.DestroyImmediate(withoutParticles);
                }
                rig.SetRegion(flow.CurrentRegion, true);
                CaptureRuntime("runtime-06-particles-room");
                environment.enabled = false;
                var masked = Render(rig.ViewCamera, 600, 375);
                environment.enabled = true;
                mask.enabled = false;
                var unmasked = Render(rig.ViewCamera, 600, 375);
                mask.enabled = true;
                try
                {
                    Color32[] before = masked.GetPixels32(), after = unmasked.GetPixels32();
                    int covered = before.Zip(after, (m, u) => m.r < 3 && m.g < 3 && m.b < 3 && (u.r > 8 || u.g > 8 || u.b > 8) ? 1 : 0).Sum();
                    Check(covered > before.Length / 100, "Authored scene background is not being covered");
                    File.WriteAllBytes(Output + "/runtime-00-without-mask.png", unmasked.EncodeToPNG());
                    results.Add("PASS: Authored play scene initializes and covers " + covered + " background pixels in the comparison render.");
                }
                finally { UnityEngine.Object.DestroyImmediate(masked); UnityEngine.Object.DestroyImmediate(unmasked); }
                var snapshot = flow.CaptureState();
                WorldMapModule initial = flow.CurrentModule;
                WorldMapModule next = flow.Modules.First(module => module != initial);
                var world = flow.GetComponent<WorldMapRoot>();
                player.TeleportJoined(world.FindRegion(next).EntrySpawnPosition);
                flow.UpdatePlayerRegion(player);
                Check(flow.CurrentModule == next && flow.HasDiscovered(initial) && flow.HasDiscovered(next), "Walking into another region lost discovery");
                rig.SetRegion(flow.CurrentRegion, true);
                RevealTransitionChecks(flow.CurrentRegion.GetComponent<RegionRevealView>(), rig.ViewCamera);
                CaptureRuntime("runtime-02-two-regions");
                flow.RestoreState(in snapshot);
                Check(flow.CurrentModule == initial && flow.HasDiscovered(next), "Rewind removed explored region");
                CaptureRuntime("runtime-03-after-rewind");
                results.Add("PASS: Real scene region crossing and rewind retain this run's explored union.");
                typeof(WorldMapProgression).GetMethod("Activate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(flow, new object[] { snapshot.CurrentIndex, true });
                resumeFrame = Time.frameCount + 3;
                playStage = 1;
                return;
            }
            Check(flow.Modules.Count(flow.HasDiscovered) == 1, "Refreshing the actual world retained old discovery");
            CaptureRuntime("runtime-04-after-reset");
            results.Add("PASS: Refreshing real scene models rebuilds the mask and resets discovery to one region.");
            FinishPlayCheck(0);
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error); Debug.LogException(error);
            FinishPlayCheck(1);
        }
    }

    private static void FinishPlayCheck(int code)
    {
        SessionState.SetBool(PlayKey, false);
        EditorApplication.update -= TickPlayCheck;
        File.WriteAllLines(Output + "/runtime-results.txt", results);
        EditorApplication.Exit(code);
    }

    private static void RevealTransitionChecks(RegionRevealView reveal, Camera camera)
    {
        Check(reveal.Discovered && reveal.VisibleOpacity == 0f, "First entry skipped the covered frame");
        Check(reveal.SceneRenderers.Any(renderer => renderer != null && !renderer.forceRenderingOff),
            "Discovered models were not prepared behind the mask");
        var update = typeof(RegionRevealView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        var advance = typeof(RegionRevealView).GetMethod("AdvanceReveal", BindingFlags.Instance | BindingFlags.NonPublic);
        update.Invoke(reveal, null); // The runtime check holds timeScale at zero.
        Check(reveal.VisibleOpacity == 0f, "Paused reveal advanced");
        Texture2D start = Render(camera, 600, 375), middle = null, end = null;
        try
        {
            advance.Invoke(reveal, new object[] { reveal.RevealDuration * 0.5f });
            float opacity = reveal.VisibleOpacity;
            Check(Mathf.Abs(opacity - 0.5f) < 0.001f, "Reveal has no intermediate opacity");
            reveal.SetDiscovered(true);
            reveal.RefreshVisuals();
            Check(reveal.VisibleOpacity == opacity, "Repeated discovery or visual refresh restarted the reveal");
            reveal.SetVisibilityOverride(camera, 0.8f);
            Check(Mathf.Abs(reveal.VisibleOpacity - 0.8f) < 0.001f, "Presentation override stopped taking precedence");
            reveal.SetVisibilityOverride(camera, 0f);
            Check(reveal.VisibleOpacity == opacity, "Removing an override erased discovery progress");
            middle = Render(camera, 600, 375);
            advance.Invoke(reveal, new object[] { reveal.RevealDuration });
            Check(reveal.VisibleOpacity == 1f, "Reveal did not complete");
            reveal.SetDiscovered(true);
            Check(reveal.VisibleOpacity == 1f, "Re-entry replayed a completed reveal");
            end = Render(camera, 600, 375);
            Color32[] a = start.GetPixels32(), b = middle.GetPixels32(), c = end.GetPixels32();
            int blended = 0;
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(c[i].r - a[i].r) > 30
                    && b[i].r > Mathf.Min(a[i].r, c[i].r) + 3
                    && b[i].r < Mathf.Max(a[i].r, c[i].r) - 3) blended++;
            Check(blended > 100, "Rendered mask did not blend through intermediate pixels: " + blended);
            File.WriteAllBytes(Output + "/runtime-reveal-start.png", start.EncodeToPNG());
            File.WriteAllBytes(Output + "/runtime-reveal-middle.png", middle.EncodeToPNG());
            File.WriteAllBytes(Output + "/runtime-reveal-end.png", end.EncodeToPNG());

            reveal.SetDiscovered(false, true);
            reveal.SetDiscovered(true);
            advance.Invoke(reveal, new object[] { reveal.RevealDuration * 0.25f });
            reveal.SetDiscovered(true, true);
            advance.Invoke(reveal, new object[] { reveal.RevealDuration });
            Check(reveal.VisibleOpacity == 1f, "Immediate reveal did not cancel the transition");
            reveal.SetDiscovered(false, true);
            advance.Invoke(reveal, new object[] { reveal.RevealDuration });
            Check(reveal.VisibleOpacity == 0f, "Reset allowed an old reveal to uncover the region");
            reveal.SetDiscovered(true, true);
            results.Add("PASS: First entry fades through " + blended + " real rendered pixels; pause, repeated entry, overrides, visual refresh and reset preserve the intended visibility.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(start);
            if (middle != null) UnityEngine.Object.DestroyImmediate(middle);
            if (end != null) UnityEngine.Object.DestroyImmediate(end);
        }
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
        Directory.CreateDirectory(Output);
        int exitCode = 0;
        try
        {
            Shader shader = Resources.Load<Shader>("Shaders/WorldExplorationMask");
            Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Mask shader failed to compile");
            Check(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Rendering/Settings/TapTapRenderer.asset")
                .rendererFeatures.Any(feature => feature is WorldExplorationMaskFeature && feature.isActive), "Feature not installed");
            RenderChecks();
            ParticleBackgroundChecks();
            VignetteBackgroundChecks();
            foreach (string check in new[] { "DiscoveryReset", "Rewind", "Visibility" })
                typeof(TapTap.Editor.CrossRegionRegressionTests).GetMethod(check, BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
            results.Add("PASS: Existing cross-region discovery reset, rewind and rendered visibility checks.");
            Debug.Log("EXPLORATION_MASK_VALIDATION_OK");
        }
        catch (Exception error)
        {
            results.Add("FAIL: " + error);
            Debug.LogException(error);
            exitCode = 1;
        }
        finally { File.WriteAllLines(Output + "/results.txt", results); }
        EditorApplication.Exit(exitCode);
    }

    private static void RenderChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("World").AddComponent<WorldMapRoot>();
        world.Source = ScriptableObject.CreateInstance<WorldMapDefinition>();
        var generated = new GameObject("Generated"); generated.transform.SetParent(world.transform, false);
        var a = Region(world, generated.transform, "A", 0);
        var b = Region(world, generated.transform, "B", 4);
        a.Source.UseCustomBoundary = true;
        a.Source.BoundaryCells.Clear();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                if (x < 2 || y < 2) a.Source.BoundaryCells.Add(new Vector2Int(x, y));
        a.Source.InvalidateFootprint();
        var red = Material(Color.red);
        var green = Material(Color.green);
        var cyan = Material(Color.cyan);
        Renderer outer = Cube(a.transform, new Vector3(-0.25f, 1, -0.5f), Vector3.one, red);
        Renderer neighbor = Cube(b.transform, new Vector3(1, 1, -0.5f), Vector3.one, green);
        Renderer background = Cube(null, new Vector3(3, 2, 8), new Vector3(80, 80, 0.1f), cyan);
        world.PrepareExploration(new HashSet<string> { world.Source.Modules[0].Id });
        var rig = new GameObject("Gameplay camera rig").AddComponent<DemoCamera>();
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.magenta;
        rig.Configure(null, camera, null); rig.SetRegion(a);
        camera.transform.position = new Vector3(3, 2, -16); camera.transform.rotation = Quaternion.identity;
        camera.fieldOfView = 40; camera.aspect = 1.5f;
        var images = new List<Texture2D>();
        Texture2D Capture(string name)
        {
            var image = Render(camera, 600, 400);
            images.Add(image);
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            return image;
        }
        try
        {
            Texture2D initial = Capture("01-first-region");
            Visible(initial, camera, new Vector3(1, 1, 0), "Interior background missing");
            Hidden(initial, camera, new Vector3(6, 1, 0), "Undiscovered background leaked");
            Hidden(initial, camera, new Vector3(3, 3, 0), "Concavity was filled by a bounding rectangle");
            Hidden(initial, camera, new Vector3(-2, 1, 0), "Outside of model contour leaked");
            Color model = Pixel(initial, camera, new Vector3(-0.55f, 1, -0.6f));
            Check(model.r > 0.7f && model.g < 0.1f, "Outer model silhouette clipped at logical region bounds");
            Check(neighbor.forceRenderingOff && outer.sharedMaterial == red && neighbor.sharedMaterial == green,
                "Exploration replaced materials or exposed hidden models");
            results.Add("PASS: Real mesh silhouette and interior visible; unexplored room, concavity and outside background fully black.");

            background.transform.position += Vector3.forward * 15;
            Texture2D deeper = Capture("02-deeper-background");
            Check(initial.GetPixels32().SequenceEqual(deeper.GetPixels32()), "Background Z changed the mask boundary");
            results.Add("PASS: Moving background from Z=8 to Z=23 leaves the complete output unchanged.");

            b.GetComponent<RegionRevealView>().SetDiscovered(true);
            Texture2D revealed = Capture("03-both-regions");
            Visible(revealed, camera, new Vector3(6, 1, 0), "New region did not reveal its background");
            Visible(revealed, camera, new Vector3(1, 1, 0), "Previously explored region was forgotten");
            Color other = Pixel(revealed, camera, new Vector3(5, 1, -0.6f));
            Check(other.g > 0.7f && other.r < 0.1f, "New region model missing");
            results.Add("PASS: Discovered rooms form one visible union and retain their own materials.");

            camera.transform.position = new Vector3(1, 3, -18);
            camera.transform.LookAt(new Vector3(3, 2, 0));
            camera.usePhysicalProperties = true; camera.sensorSize = new Vector2(36, 24);
            camera.focalLength = 45; camera.lensShift = new Vector2(0.1f, -0.08f);
            Texture2D angled = Capture("04-perspective-physical-lens");
            Visible(angled, camera, new Vector3(1, 1, 0), "Perspective/lens shift lost the visible region");
            Hidden(angled, camera, new Vector3(3, 3, 0), "Perspective/lens shift misaligned the concave boundary");
            results.Add("PASS: Translated/rotated perspective camera with physical lens shift stays aligned.");

            world.PrepareExploration(new HashSet<string> { world.Source.Modules[1].Id });
            Texture2D reset = Capture("05-new-run");
            Hidden(reset, camera, new Vector3(1, 1, 0), "New run retained the old explored room");
            Visible(reset, camera, new Vector3(6, 1, 0), "New run starting room is hidden");
            results.Add("PASS: Starting a new run resets the visible union to its starting room.");

            world.GetComponent<WorldExplorationMask>().enabled = false;
            Texture2D disabled = Capture("06-disabled");
            Visible(disabled, camera, new Vector3(3, 3, 0), "Disabling mask did not restore background");
            results.Add("PASS: Mask can be disabled without changing scene materials.");
        }
        finally
        {
            foreach (Texture2D image in images) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(rig.gameObject);
            UnityEngine.Object.DestroyImmediate(background.gameObject);
            var definition = world.Source;
            UnityEngine.Object.DestroyImmediate(world.gameObject);
            foreach (WorldMapModule module in definition.Modules) UnityEngine.Object.DestroyImmediate(module.Level);
            UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(red); UnityEngine.Object.DestroyImmediate(green); UnityEngine.Object.DestroyImmediate(cyan);
        }
    }

    private static void ParticleBackgroundChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("Particle world").AddComponent<WorldMapRoot>();
        world.Source = ScriptableObject.CreateInstance<WorldMapDefinition>();
        var generated = new GameObject("Generated"); generated.transform.SetParent(world.transform, false);
        LevelRegion a = Region(world, generated.transform, "A", 0);
        Region(world, generated.transform, "B", 4);
        var green = Material(Color.green);
        var red = Material(Color.red);
        Cube(a.transform, new Vector3(1, 1, 0), Vector3.one, green);
        Renderer wall = Cube(null, new Vector3(6, 1, 1), new Vector3(2, 2, 0.2f), red);
        var particles = new GameObject("EnvironmentParticle").AddComponent<ParticleSystem>();
        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false; main.prewarm = false; main.startSpeed = 0; main.startLifetime = 100;
        main.maxParticles = 100; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(2, 2, 1);
        var emission = particles.emission; emission.enabled = false; emission.rateOverTime = 2;
        var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        var particleMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        particleMaterial.SetColor("_BaseColor", Color.white);
        particleRenderer.sharedMaterial = particleMaterial;
        var background = particles.gameObject.AddComponent<WorldEnvironmentParticles>();
        background.WorldPadding = 0;
        world.PrepareExploration(new HashSet<string> { world.Source.Modules[0].Id });
        float rate = particles.emission.rateOverTime.constant;
        Check(Mathf.Abs(rate / (particles.shape.scale.x * particles.shape.scale.y) - 0.5f) < 0.0001f,
            "Expansion changed particle emission density");
        Check(main.maxParticles == 800, "Particle cap did not scale with expanded area");
        background.FitToWorld(world);
        Check(Mathf.Approximately(rate, particles.emission.rateOverTime.constant) && main.maxParticles == 800,
            "Repeated fitting compounded the particle rate or cap");
        Check(Mathf.Approximately(main.startSize.constant, 1f) && Mathf.Approximately(main.startLifetime.constant, 100f),
            "Fitting changed particle size or lifetime");
        results.Add("PASS: Area expansion keeps emission per square unit, scales capacity and does not compound on repeat.");
        var rig = new GameObject("Particle test rig").AddComponent<DemoCamera>();
        var camera = new GameObject("Camera").AddComponent<Camera>(); camera.transform.SetParent(rig.transform, false);
        camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.03f, 0.05f, 0.08f);
        rig.Configure(null, camera, null); rig.SetRegion(a);
        camera.transform.position = new Vector3(3, 2, -16); camera.transform.rotation = Quaternion.identity;
        camera.fieldOfView = 40; camera.aspect = 1.5f;
        particles.Simulate(0f, false, true, true);
        particles.SetParticles(new[]
        {
            new ParticleSystem.Particle { position = new Vector3(6, 1, 3), velocity = Vector3.zero,
                startSize = 0.8f, startLifetime = 100, remainingLifetime = 100, startColor = Color.yellow },
            new ParticleSystem.Particle { position = new Vector3(1, 1, 3), velocity = Vector3.zero,
                startSize = 0.8f, startLifetime = 100, remainingLifetime = 100, startColor = Color.yellow }
        }, 2);
        particles.Pause(false);
        Check(particles.particleCount == 2, "Manual particle fixture count: " + particles.particleCount);
        var images = new List<Texture2D>();
        try
        {
            Texture2D image = Render(camera, 600, 400); images.Add(image);
            File.WriteAllBytes(Output + "/07-particles-outside-mask.png", image.EncodeToPNG());
            Color outside = Pixel(image, camera, new Vector3(6, 1, 3));
            Check(outside.r > 0.7f && outside.g > 0.7f && outside.b < 0.2f,
                "Environment particle missing outside mask or occluded by scene wall: " + outside);
            Color inside = Pixel(image, camera, new Vector3(1, 1, 0));
            Check(inside.g > 0.7f && inside.r < 0.2f, "Isolated particles overlaid explored geometry");
            Color empty = Pixel(image, camera, new Vector3(6, 3, 0));
            Check(empty.b > 0.02f && empty.r < 0.2f, "Background base color missing or hidden geometry leaked");
            results.Add("PASS: Environment particles remain visible through scene walls outside the mask; explored geometry keeps normal occlusion.");
            particleRenderer.enabled = false;
            Texture2D disabled = Render(camera, 600, 400); images.Add(disabled);
            Hidden(disabled, camera, new Vector3(6, 1, 3), "Disabled background leaked a previous frame");
            results.Add("PASS: Disabling environment particles restores opaque fallback with no stale texture.");
        }
        finally
        {
            foreach (Texture2D image in images) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(rig.gameObject); UnityEngine.Object.DestroyImmediate(particles.gameObject);
            UnityEngine.Object.DestroyImmediate(wall.gameObject);
            var definition = world.Source; UnityEngine.Object.DestroyImmediate(world.gameObject);
            foreach (WorldMapModule module in definition.Modules) UnityEngine.Object.DestroyImmediate(module.Level);
            UnityEngine.Object.DestroyImmediate(definition);
            UnityEngine.Object.DestroyImmediate(green); UnityEngine.Object.DestroyImmediate(red); UnityEngine.Object.DestroyImmediate(particleMaterial);
        }
    }

    private static void VignetteBackgroundChecks()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var world = new GameObject("Vignette world").AddComponent<WorldMapRoot>();
        world.Source = ScriptableObject.CreateInstance<WorldMapDefinition>();
        var generated = new GameObject("Generated"); generated.transform.SetParent(world.transform, false);
        LevelRegion region = Region(world, generated.transform, "Room", 0);
        world.PrepareExploration(new HashSet<string> { world.Source.Modules[0].Id });
        var mask = world.GetComponent<WorldExplorationMask>();
        mask.MaskColor = Color.white;
        var rig = new GameObject("Vignette rig").AddComponent<DemoCamera>();
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.SetParent(rig.transform, false);
        camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
        rig.Configure(null, camera, null); rig.SetRegion(region);
        camera.transform.SetPositionAndRotation(new Vector3(2, 2, -12), Quaternion.identity);
        camera.fieldOfView = 40; camera.allowMSAA = false;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true; data.volumeLayerMask = 1 << 31;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.value = 0.4f; vignette.smoothness.value = 0.65f;
        var volume = new GameObject("Test vignette").AddComponent<Volume>();
        volume.gameObject.layer = 31; volume.isGlobal = true; volume.sharedProfile = profile;
        GameObject particleObject = null;
        Material particleMaterial = null;
        try
        {
            void Compare(string name, int width, int height)
            {
                mask.enabled = false;
                Texture2D reference = Render(camera, width, height);
                mask.enabled = true;
                Texture2D actual = Render(camera, width, height);
                try
                {
                    Color32[] expected = reference.GetPixels32(), observed = actual.GetPixels32();
                    int largestError = 0;
                    for (int i = 0; i < expected.Length; i++)
                    {
                        largestError = Mathf.Max(largestError, Mathf.Abs(expected[i].r - observed[i].r),
                            Mathf.Abs(expected[i].g - observed[i].g), Mathf.Abs(expected[i].b - observed[i].b));
                    }
                    File.WriteAllBytes(Output + "/vignette-" + name + ".png", actual.EncodeToPNG());
                    Check(largestError <= 3, name + " differs from URP's own vignette by " + largestError + "/255");
                    results.Add("PASS: " + name + " matches URP across explored and masked pixels (max error " + largestError + "/255).");
                }
                finally { UnityEngine.Object.DestroyImmediate(reference); UnityEngine.Object.DestroyImmediate(actual); }
            }

            Compare("fallback", 600, 400);
            vignette.center.value = new Vector2(0.35f, 0.65f);
            vignette.color.value = new Color(0.1f, 0.3f, 0.5f);
            vignette.rounded.value = true;
            Compare("offset-rounded-wide", 640, 360);
            Compare("offset-rounded-tall", 360, 640);
            volume.weight = 0.4f;
            Compare("blended-volume", 600, 400);
            volume.weight = 1f;
            var reveal = region.GetComponent<RegionRevealView>();
            reveal.SetDiscovered(false, true); reveal.SetDiscovered(true);
            typeof(RegionRevealView).GetMethod("AdvanceReveal", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(reveal, new object[] { reveal.RevealDuration * 0.5f });
            Compare("half-revealed", 600, 400);
            reveal.SetDiscovered(true, true);

            var particles = new GameObject("Vignette particles").AddComponent<ParticleSystem>();
            particleObject = particles.gameObject;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.prewarm = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.enabled = false;
            particleMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            particleMaterial.SetColor("_BaseColor", Color.white);
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
            particleObject.AddComponent<WorldEnvironmentParticles>();
            mask.RefreshRegions(world);
            particles.SetParticles(new[] { new ParticleSystem.Particle
            {
                position = new Vector3(-2.5f, 3, 3), startSize = 0.8f,
                startLifetime = 100, remainingLifetime = 100, startColor = Color.cyan
            } }, 1);
            particles.Pause(false);
            Compare("particle-background", 600, 400);
            vignette.intensity.value = 0f;
            Compare("zero-intensity", 600, 400);
            vignette.intensity.value = 0.4f;
            data.renderPostProcessing = false;
            Compare("post-processing-disabled", 600, 400);
            data.renderPostProcessing = true;
            volume.enabled = false;
            Compare("volume-disabled", 600, 400);
        }
        finally
        {
            if (particleObject != null) UnityEngine.Object.DestroyImmediate(particleObject);
            if (particleMaterial != null) UnityEngine.Object.DestroyImmediate(particleMaterial);
            UnityEngine.Object.DestroyImmediate(volume.gameObject);
            UnityEngine.Object.DestroyImmediate(profile);
            UnityEngine.Object.DestroyImmediate(rig.gameObject);
            var definition = world.Source;
            UnityEngine.Object.DestroyImmediate(world.gameObject);
            foreach (WorldMapModule module in definition.Modules) UnityEngine.Object.DestroyImmediate(module.Level);
            UnityEngine.Object.DestroyImmediate(definition);
        }
    }

    private static LevelRegion Region(WorldMapRoot world, Transform parent, string name, int x)
    {
        var region = new GameObject(name).AddComponent<LevelRegion>();
        region.transform.SetParent(parent, false); region.transform.localPosition = new Vector3(x, 0, 0);
        region.Size = new Vector2Int(4, 4);
        region.Source = ScriptableObject.CreateInstance<LevelDefinition>(); region.Source.Size = region.Size;
        var module = new WorldMapModule { Level = region.Source, Position = new Vector2Int(x, 0) };
        region.WorldModuleId = module.Id; world.Source.Modules.Add(module);
        return region;
    }

    private static Material Material(Color color)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetColor("_BaseColor", color);
        return material;
    }

    private static Renderer Cube(Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(parent, false); cube.transform.localPosition = position; cube.transform.localScale = scale;
        var renderer = cube.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        return renderer;
    }

    private static Texture2D Render(Camera camera, int width, int height)
    {
        var target = new RenderTexture(width, height, 24);
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            return image;
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static Color Pixel(Texture2D image, Camera camera, Vector3 world)
    {
        Vector3 viewport = camera.WorldToViewportPoint(world);
        Check(viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1, "Test sample outside camera");
        return image.GetPixel(Mathf.FloorToInt(viewport.x * image.width), Mathf.FloorToInt(viewport.y * image.height));
    }
    private static void Hidden(Texture2D image, Camera camera, Vector3 point, string message)
        => Check(Pixel(image, camera, point).maxColorComponent < 0.02f, message);
    private static void Visible(Texture2D image, Camera camera, Vector3 point, string message)
        => Check(Pixel(image, camera, point).g > 0.7f && Pixel(image, camera, point).b > 0.7f, message);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
