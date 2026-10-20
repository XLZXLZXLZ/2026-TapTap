using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    [InitializeOnLoad]
    public static class WorldMapTravelValidation
    {
        private const string Key = "TapTap.MapTravel.Validation";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/MapTravel"));
        private static int stage, lastFrame = -1;
        private static double deadline;
        private static bool drewNext, spawned, held, travelled, arrived;
        private static Coroutine interrupted;

        static WorldMapTravelValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                deadline = EditorApplication.timeSinceStartup + 150d;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "results.txt"), "");
            try
            {
                PlayerSettings.productName = "TapTap Map Travel Validation";
                var material = Resources.Load<Material>("WorldMapSelection/TravelOrb");
                Check(material != null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader), "Travel glow shader/material invalid");
                var world = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>("Assets/Runtime/WorldMaps/World_Character.asset");
                WorldMapProgressStore.Clear(world);
                var modules = world.OrderedModules();
                WorldMapProgressStore.Complete(world, modules[6]);
                WorldMapProgressStore.Complete(world, modules[19]);
                EditorSceneManager.OpenScene(world.PlayScenePath);
                SessionState.SetBool(Key, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                Check(EditorApplication.timeSinceStartup < deadline, "Travel validation timeout at stage " + stage);
                var flow = WorldMapProgression.Current;
                if (flow == null || flow.CurrentModule == null || WorldMapSceneRouter.IsLoading) return;
                var view = flow.GetComponent<WorldMapAssemblyView>();
                var screen = view.NeonScreen ?? view.SceneScreen;
                Check(screen != null, "Authored map screen missing");
                if (stage == 0)
                {
                    Check(flow.CurrentIndex == 0, "Expected first room");
                    ValidateMotionCurve(screen);
                    Check(flow.CompleteCurrent(flow.CurrentRegion.GetComponentInChildren<LevelEndpoint>()), "Cannot trigger real endpoint transition");
                    stage = 1;
                }
                else if (stage == 1)
                {
                    var first = screen.Pieces.Single(piece => piece.ModuleId == flow.Modules[0].Id);
                    var next = screen.Pieces.Single(piece => piece.ModuleId == flow.Modules[1].Id);
                    if (screen.DrawingElapsed > 0f && next.Outline.Reveal < 1f)
                    {
                        Check(first.gameObject.activeSelf && first.Outline.Reveal == 1f, "Finished outline is being redrawn or hidden");
                        Check(next.gameObject.activeSelf, "Next outline hidden");
                        Check(screen.Pieces.Where(piece => piece.Sequence > 2).All(piece => !piece.gameObject.activeSelf), "Later saved completion leaked into transition");
                        if (!drewNext && next.Outline.Reveal > 0.25f && next.Outline.Reveal < 0.8f)
                        { Capture(screen, "01-next-outline.png"); drewNext = true; }
                    }
                    var travel = screen.ActiveTravel;
                    if (travel != null)
                    {
                        Check(Time.timeScale == 0f && WorldMapProgression.InputBlocked, "Travel did not retain gameplay pause");
                        Vector2 from = screen.Board.InverseTransformPoint(first.Number.transform.position);
                        Vector2 to = screen.Board.InverseTransformPoint(next.Number.transform.position);
                        Check(Vector2.Distance(travel.Origin, from) < 0.001f && Vector2.Distance(travel.Destination, to) < 0.001f, "Orb endpoints differ from label centers");
                        if (screen.Phase == NeonWorldMapScreen.TransitionPhase.OrbAppearing && travel.Orb.localScale.x > 0.4f)
                        {
                            Check(next.Outline.Reveal == 0f, "Next outline appeared before the orb started flying");
                            Check(Vector2.Distance(travel.Orb.anchoredPosition, from) < 0.001f, "Spawn moved away from current center");
                            if (!spawned) { Capture(screen, "02-orb-spawn.png"); spawned = true; }
                        }
                        float progress = Vector2.Distance(travel.Orb.anchoredPosition, from) / Vector2.Distance(from, to);
                        if (screen.Phase == NeonWorldMapScreen.TransitionPhase.OrbHolding)
                        {
                            Check(next.Outline.Reveal == 0f, "Next outline appeared during the orb's initial pause");
                            Check(Vector2.Distance(travel.Orb.anchoredPosition, from) < 0.001f
                                && Mathf.Approximately(travel.Orb.localScale.x, 1f), "Hold moved or rescaled orb");
                            if (!held) { Capture(screen, "02b-orb-hold.png", travel.Orb); held = true; }
                        }
                        if (!travelled && screen.Phase == NeonWorldMapScreen.TransitionPhase.OrbTravelling && progress > 0.4f && progress < 0.8f)
                        {
                            Check(next.Outline.Reveal > 0f && next.Outline.Reveal < 1f, "Next outline is not revealing during flight");
                            Check(travel.Trail.SampleCount > 2, "Moving orb has no trail history");
                            Capture(screen, "03-white-flight.png", travel.Orb);
                            travelled = true;
                        }
                        if (screen.Phase == NeonWorldMapScreen.TransitionPhase.OrbDisappearing)
                        {
                            Check(Vector2.Distance(travel.Orb.anchoredPosition, to) < 0.001f, "Orb disappeared away from next center");
                            if (!arrived) { Capture(screen, "04-orb-arrival.png"); arrived = true; }
                        }
                    }
                    if (flow.IsTransitioning) return;
                    Check(drewNext && spawned && held && travelled && arrived, "Not all animation phases were observed");
                    Check(flow.CurrentIndex == 1 && flow.HasCompleted(flow.Modules[0]) && !flow.HasCompleted(flow.Modules[1]), "Wrong activation or next room incorrectly marked completed");
                    Check(Time.timeScale > 0f && screen.ActiveTravel == null && !WorldMapProgression.InputBlocked, "Resume left paused input or orb");
                    Pass("Real room 1 -> 2 endpoint: next outline only, label-center spawn, unscaled DOTween scale/hold/flight/despawn, white glow/trail, progress and gameplay restoration");
                    Check(flow.RestartCurrent(), "Restart rejected");
                    stage = 2;
                }
                else if (stage == 2)
                {
                    Check(screen.ActiveTravel == null, "Restart spawned a travel orb");
                    if (flow.IsTransitioning) return;
                    Check(flow.CurrentIndex == 1 && Time.timeScale > 0f, "Restart changed room or time scale");
                    Check(flow.GoToSequence(flow.GetComponent<WorldMapRoot>().Source.SequenceNumber(flow.Modules[flow.CurrentIndex + 1])), "Skip rejected");
                    stage = 3;
                }
                else if (stage == 3)
                {
                    Check(screen.ActiveTravel == null, "Skip spawned a completion orb");
                    if (flow.IsTransitioning) return;
                    Check(flow.CurrentIndex == 2, "Skip target changed");
                    screen.Initialize(false);
                    Time.timeScale = 0f;
                    interrupted = screen.StartCoroutine(screen.Play(new System.Collections.Generic.HashSet<string> { flow.Modules[2].Id },
                        flow.Modules[3], 0.1f, 0f, flow.Modules[2]));
                    stage = 4;
                }
                else if (stage == 4)
                {
                    if (screen.Phase != NeonWorldMapScreen.TransitionPhase.OrbTravelling) return;
                    screen.HidePresentation();
                    screen.StopCoroutine(interrupted);
                    Time.timeScale = 1f;
                    stage = 5;
                }
                else if (stage == 5)
                {
                    Check(screen.ActiveTravel == null && Object.FindObjectsOfType<WorldMapTravelEffect>(true).Length == 0, "Interrupted transition leaked orb/tween objects");
                    Pass("Restart and skip retain existing behavior; interrupted travel removes orb and trail");
                    Check(flow.GoToSequence(flow.GetComponent<WorldMapRoot>().Source.SequenceNumber(flow.Modules[flow.LevelCount - 1])), "Cannot navigate to final room");
                    stage = 6;
                }
                else if (stage == 6)
                {
                    if (flow.IsTransitioning) return;
                    // The last authored world slots are placeholders without an endpoint.
                    // Exercise the terminal progression branch without a door walk fixture.
                    typeof(WorldMapProgression).GetMethod("StartTransition", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic).Invoke(flow, new object[] { flow.LevelCount, true, null });
                    stage = 7;
                }
                else if (stage == 7)
                {
                    Check(screen.ActiveTravel == null, "Final room spawned an orb without a destination");
                    if (flow.IsTransitioning) return;
                    Check(flow.IsComplete && Time.timeScale > 0f, "Final completion did not restore time");
                    Pass("Final room retains completion presentation without an invalid next-room destination");
                    Finish(null);
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        private static void Capture(NeonWorldMapScreen screen, string name, RectTransform orb = null)
        {
            Camera camera = screen.UICamera;
            RenderTexture previous = camera.targetTexture;
            RenderTexture active = RenderTexture.active;
            var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.DefaultHDR);
            target.Create(); camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); screen.SendMessage("FitBoard"); Canvas.ForceUpdateCanvases();
            camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(1600, 1000, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
            string colorFailure = null;
            if (orb != null)
            {
                Vector3 top = orb.TransformPoint(new Vector3(0f, orb.rect.height * 0.16f, 0f));
                Vector3 bottom = orb.TransformPoint(new Vector3(0f, -orb.rect.height * 0.16f, 0f));
                Vector3 a = camera.WorldToScreenPoint(top), b = camera.WorldToScreenPoint(bottom);
                Color topColor = image.GetPixel(Mathf.RoundToInt(a.x), Mathf.RoundToInt(a.y));
                Color bottomColor = image.GetPixel(Mathf.RoundToInt(b.x), Mathf.RoundToInt(b.y));
                bool white = new[] { topColor, bottomColor }.All(sample => sample.maxColorComponent > 0.1f
                    && sample.maxColorComponent - Mathf.Min(sample.r, sample.g, sample.b) < sample.maxColorComponent * 0.15f);
                if (!white)
                {
                    var graphic = orb.GetComponent<WorldMapTravelGraphic>();
                    Mesh mesh = graphic.canvasRenderer.GetMesh();
                    colorFailure = "Rendered orb is not white: " + topColor + " / " + bottomColor
                        + "; screen=" + a + "/" + b + "; world=" + orb.position + "; scale=" + orb.lossyScale
                        + "; vertices=" + mesh.vertexCount + "; cull=" + graphic.canvasRenderer.cull + "; color=" + graphic.color;
                }
            }
            Color[] pixels = image.GetPixels();
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && !target.sRGB)
                for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
            var png = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            png.SetPixels(pixels); png.Apply();
            File.WriteAllBytes(Path.Combine(Output, name), png.EncodeToPNG());
            RenderTexture.active = active; camera.targetTexture = previous;
            target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(image); Object.DestroyImmediate(png);
            Canvas.ForceUpdateCanvases(); screen.SendMessage("FitBoard");
            Check(colorFailure == null, colorFailure);
        }

        private static void ValidateMotionCurve(NeonWorldMapScreen screen)
        {
            NeonWorldMapScreen.TransitionPhase phase = NeonWorldMapScreen.TransitionPhase.None;
            var effect = WorldMapTravelEffect.Create(screen.Board, screen.Pieces[0].Number.transform,
                screen.Pieces[1].Number.transform, screen.OrbSize, screen.OrbTravelDuration, screen.OrbPauseDuration);
            var routine = effect.Play(value => phase = value);
            routine.MoveNext();
            var timeline = (DG.Tweening.Sequence)typeof(WorldMapTravelEffect).GetField("timeline",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(effect);
            DG.Tweening.TweenExtensions.Goto(timeline, 0.22f + screen.OrbPauseDuration * 0.5f, true);
            Check(Vector2.Distance(effect.Orb.anchoredPosition, effect.Origin) < 0.001f
                && Mathf.Approximately(effect.Orb.localScale.x, 1f), "Spawn/hold order is wrong");
            DG.Tweening.TweenExtensions.Goto(timeline, 0.22f + screen.OrbPauseDuration + screen.OrbTravelDuration * 0.25f, true);
            Check(Vector2.Distance(effect.Orb.anchoredPosition, Vector2.Lerp(effect.Origin, effect.Destination, 0.125f)) < 0.001f,
                "Movement does not follow InOutQuad (quarter time must cover 12.5% of distance)");
            DG.Tweening.TweenExtensions.Complete(timeline, true);
            Check(effect.Orb.localScale.sqrMagnitude < 0.0001f
                && Vector2.Distance(effect.Orb.anchoredPosition, effect.Destination) < 0.001f, "Despawn misses destination");
            effect.Cancel();
            Pass("DOTween: full-size stationary hold, InOutQuad quarter-time at 12.5% distance, zero-scale despawn at destination");
        }

        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Pass(string message) { File.AppendAllText(Path.Combine(Output, "results.txt"), "PASS: " + message + "\n"); Debug.Log(message); }
        private static void Finish(Exception exception)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            if (exception != null) { File.AppendAllText(Path.Combine(Output, "results.txt"), "FAIL: " + exception + "\n"); Debug.LogException(exception); }
            EditorApplication.Exit(exception == null ? 0 : 1);
        }
    }
}
