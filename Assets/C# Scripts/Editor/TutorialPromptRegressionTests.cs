using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    // Run in an isolated project, without -quit: play-mode verification exits Unity on completion.
    [InitializeOnLoad]
    public static class TutorialPromptRegressionTests
    {
        private const string Pending = "TapTap.TutorialPromptTests.Pending";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/TutorialPrompts"));
        private static TutorialPromptPresenter presenter;
        private static int stage, lastFrame;
        private static double deadline, since;
        private static bool buffered;
        private static RewindManager rewind;
        private static PlayerController triggerPlayer;
        private static TutorialPromptTrigger2D triggerProbe;
        private static Vector3 intactVertex;

        static TutorialPromptRegressionTests()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
                stage = 0; lastFrame = -1; buffered = false;
                deadline = EditorApplication.timeSinceStartup + 45;
                EditorApplication.update += Tick;
            };
        }

        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("请在独立批处理项目运行此验证。");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/results.txt", "");
            try
            {
                ValidateAssets();
                VerifyInput();
                VerifyTriggerGeometry();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                TutorialPromptPresenter preview = InstantiatePresenter();
                for (int i = 0; i < 4; i++)
                {
                    TutorialPromptView view = Object.Instantiate(Prompt(i), preview.transform, false);
                    RectTransform rect = (RectTransform)view.transform;
                    rect.anchorMin = rect.anchorMax = preview.ScreenAnchor;
                    rect.anchoredPosition = Vector2.zero;
                    view.Label.text = view.RichText;
                    Canvas.ForceUpdateCanvases();
                    view.Label.ForceMeshUpdate(true, true);
                    // TMP 3.0.7 overwrites textInfo.spriteCount at the end of mesh generation.
                    Check(view.Label.textInfo.characterInfo.Take(view.Label.textInfo.characterCount)
                        .Count(c => c.elementType == TMP_TextElementType.Sprite) == 1, "Prompt " + i + " has no inline key sprite");
                    Check(view.Label.textInfo.characterInfo.All(c => c.character != '\u25A1'), "Missing font glyph");
                    Capture(preview, 1920, 1080, TutorialPromptAssets.Names[i]);
                    if (i == 3)
                    {
                        Capture(preview, 1280, 720, "04_BulletTime_720p");
                        Capture(preview, 2560, 1080, "04_BulletTime_ultrawide");
                    }
                    Object.DestroyImmediate(view.gameObject);
                }
                Pass("All four TMP prompts and key sprites rendered at 1080p; Ctrl prompt also rendered at 720p and ultrawide");
                VerifyFade(preview);
                Object.DestroyImmediate(preview.gameObject);
                SessionState.SetBool(Pending, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error) { Finish(error); }
        }

        public static void BuildAndRunBatch()
        {
            TutorialPromptAssets.Build();
            RunBatch();
        }

        private static TutorialPromptView Prompt(int index) => AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPromptAssets.PromptPath(index)).GetComponent<TutorialPromptView>();
        private static TutorialPromptPresenter InstantiatePresenter()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPromptAssets.Folder + "/TutorialPresenter.prefab"));
            TutorialPromptPresenter result = root.GetComponent<TutorialPromptPresenter>();
            result.UseKeyboard = false;
            return result;
        }

        private static void ValidateAssets()
        {
            Check(Prompt(0).Suffix == " 使头部悬浮", "Requested hover wording differs");
            Check(Prompt(0).NextPromptPrefab == Prompt(1), "Hover must chain to recall");
            for (int i = 0; i < 4; i++)
            {
                TutorialPromptView view = Prompt(i);
                Check(view.Label.font == AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TutorialPromptAssets.FontPath), "Wrong font");
                Check(view.Shatter != null && view.PanelGroup != view.TextGroup, "Exit effect references incomplete");
                Check(!view.Label.raycastTarget && !view.PanelGroup.blocksRaycasts && !view.TextGroup.blocksRaycasts, "Hint intercepts input");
                Check(view.Label.spriteAsset.spriteCharacterTable.Count == 3, "Missing keyboard sprite");
                if (i > 0) Check(view.NextPromptPrefab == null, "Unexpected chain");
            }
            foreach (int i in new[] { 0, 2, 3 })
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPromptAssets.Folder + "/Trigger_" + TutorialPromptAssets.Names[i] + ".prefab");
                Check(root.GetComponent<BoxCollider2D>().isTrigger, "Trigger is solid");
                Check(root.GetComponent<TutorialPromptTrigger2D>().PromptPrefab == Prompt(i), "Trigger has wrong prompt");
            }
            Shader shader = Shader.Find("TapTap/UI/Tutorial Panel");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Panel shader has errors");
            Pass("Prefab references, exact wording, chain, font, raycasts and transparent panel shader");
        }

        private static void VerifyInput()
        {
            var release = new TutorialPromptInput(TutorialPromptCompletion.ReleaseAfterHold, false);
            release.Observe(false, false, true);
            Check(!release.Completed, "An unarmed release completed the prompt");
            release.Observe(true, true, false);
            Check(!release.Completed, "Hold prompt completed on press");
            release.Observe(false, false, true);
            Check(release.Completed, "Hold/release was missed");
            var heldAtEntry = new TutorialPromptInput(TutorialPromptCompletion.ReleaseAfterHold, true);
            heldAtEntry.Observe(false, false, true);
            Check(heldAtEntry.Completed, "Held-at-entry release was missed");
            var press = new TutorialPromptInput(TutorialPromptCompletion.Press, true);
            press.Observe(true, false, false);
            Check(!press.Completed, "Already held key manufactured a fresh press");
            press.Synchronize(false); press.Observe(true, false, false);
            Check(press.Completed, "Fresh press was missed");
            var suspended = new TutorialPromptInput(TutorialPromptCompletion.ReleaseAfterHold, true);
            suspended.Synchronize(false); suspended.Observe(false, false, false);
            Check(!suspended.Completed, "Pause manufactured a release");
            var controls = new GameObject("Gameplay input queue").AddComponent<PlayerInput>();
            controls.SetExternalInput(0, true); controls.SetExternalInput(0, false);
            Check(controls.TryConsume(out MagnetInput first) && first == MagnetInput.Press
                && controls.TryConsume(out MagnetInput second) && second == MagnetInput.Release, "Gameplay edges were lost");
            Object.DestroyImmediate(controls.gameObject);
            Pass("Release arming, held-at-entry, fresh presses, pause synchronization and gameplay input queue");
        }

        private static void VerifyTriggerGeometry()
        {
            var player = new GameObject("Body collision probe", typeof(Rigidbody2D), typeof(BoxCollider2D));
            player.GetComponent<BoxCollider2D>().size = Vector2.one * 0.2f;
            KinematicMotor2D motor = player.AddComponent<KinematicMotor2D>();
            motor.Initialize();
            var root = new GameObject("Trigger probe", typeof(BoxCollider2D));
            root.GetComponent<BoxCollider2D>().size = new Vector2(0.2f, 3f);
            TutorialPromptTrigger2D trigger = root.AddComponent<TutorialPromptTrigger2D>();
            motor.Teleport(new Vector2(-2, 0));
            Check(!trigger.TouchesBody(motor), "Outside body falsely triggers");
            motor.StageTranslation(new Vector2(4, 0));
            Check(trigger.TouchesBody(motor), "Fast pass through narrow trigger was missed");
            motor.Teleport(new Vector2(0, 0));
            Check(trigger.TouchesBody(motor), "Inside body missed");
            root.transform.rotation = Quaternion.Euler(0, 0, 45);
            root.transform.localScale = new Vector3(2, 1, 1);
            motor.Teleport(new Vector2(1f, 1f));
            Check(!trigger.TouchesBody(motor), "Rotated box used an oversized world AABB");
            motor.Teleport(new Vector2(0.6f, -0.6f));
            Check(trigger.TouchesBody(motor), "Rotated/scaled trigger missed body");
            Object.DestroyImmediate(root); Object.DestroyImmediate(player);
            Pass("Body overlap, fast swept crossing, rotated/scaled trigger accuracy");
        }

        private static void VerifyFade(TutorialPromptPresenter root)
        {
            TutorialPromptView view = Object.Instantiate(Prompt(0), root.transform, false);
            Canvas.ForceUpdateCanvases();
            view.Label.ForceMeshUpdate(true, true);
            Vector3[] original = view.Label.textInfo.meshInfo[0].vertices.ToArray();
            view.Exit();
            Tween fade = (Tween)view.GetType().GetField("transition", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            fade.Goto(view.PanelFadeDuration * 0.5f, false);
            Check(view.PanelGroup.alpha > 0f && view.PanelGroup.alpha < 1f, "Panel does not fade");
            Check(Mathf.Abs(view.PanelGroup.alpha - view.TextGroup.alpha) < 0.0001f, "Text and panel do not fade together");
            Check(view.Shatter == null || !view.Shatter.IsPlaying, "Exit still plays the shatter effect");
            Check(original.SequenceEqual(view.Label.textInfo.meshInfo[0].vertices), "Exit moves glyphs");
            fade.Complete();
            Check(view.ExitFinished && view.PanelGroup.alpha == 0f && view.TextGroup.alpha == 0f, "Fade does not finish cleanly");
            Object.DestroyImmediate(view.gameObject);
            Pass("Text, key sprite and panel fade together without glyph motion");
        }
        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Tutorial interaction timeout at stage " + stage);
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                TutorialPromptView view = presenter != null ? presenter.ActiveView : null;
                if (stage == 0)
                {
                    presenter = InstantiatePresenter();
                    presenter.Request(Prompt(0)); presenter.Request(Prompt(2)); presenter.Request(Prompt(3));
                    stage = 1;
                }
                else if (stage == 1 && view != null && !view.IsEntering)
                {
                    Check(view.Key == TutorialPromptKey.Space, "Wrong initial prompt");
                    int index = Array.FindIndex(view.Label.textInfo.characterInfo, c => c.isVisible);
                    TMP_CharacterInfo glyph = view.Label.textInfo.characterInfo[index];
                    intactVertex = view.Label.textInfo.meshInfo[glyph.materialReferenceIndex].vertices[glyph.vertexIndex];
                    presenter.SubmitInput(TutorialPromptKey.Space, true, true, false);
                    presenter.SubmitInput(TutorialPromptKey.Space, false, false, true);
                    since = EditorApplication.timeSinceStartup;
                    stage = 2;
                }
                else if (stage == 2 && view != null && view.IsExiting)
                {
                    if (!buffered)
                    {
                        presenter.SubmitInput(TutorialPromptKey.Space, true, true, false);
                        presenter.SubmitInput(TutorialPromptKey.Space, false, false, true);
                        buffered = true;
                    }
                    int index = Array.FindIndex(view.Label.textInfo.characterInfo, c => c.isVisible);
                    TMP_CharacterInfo glyph = view.Label.textInfo.characterInfo[index];
                    Vector3 vertex = view.Label.textInfo.meshInfo[glyph.materialReferenceIndex].vertices[glyph.vertexIndex];
                    Check((vertex - intactVertex).sqrMagnitude < 0.001f, "Fade moved glyph mesh");
                    Check(Mathf.Abs(view.TextGroup.alpha - view.PanelGroup.alpha) < 0.0001f, "Text and panel fade are not synchronized");
                    Capture(presenter, 1920, 1080, "05_Fade");
                    Pass("Text and panel fade together; next input buffered during exit");
                    stage = 3;
                }
                else if (stage == 3 && view != null && view.Prefix == "再按 ") stage = 4;
                else if (stage == 4 && view != null && view.Key == TutorialPromptKey.Z)
                {
                    Pass("Recall chain takes priority over queued Z prompt and consumes buffered second press");
                    presenter.SubmitInput(TutorialPromptKey.Z, false, false, true);
                    GameplayActionLocks.Acquire(presenter, GameplayAction.Input);
                    presenter.SubmitInput(TutorialPromptKey.Z, true, true, false);
                    presenter.SubmitInput(TutorialPromptKey.Z, false, false, true);
                    since = EditorApplication.timeSinceStartup;
                    stage = 5;
                }
                else if (stage == 5 && EditorApplication.timeSinceStartup > since + 0.45)
                {
                    Check(view != null && view.Key == TutorialPromptKey.Z && !view.IsExiting, "Blocked input completed Z prompt");
                    GameplayActionLocks.Release(presenter);
                    presenter.SubmitInput(TutorialPromptKey.Z, true, true, false);
                    rewind = RewindManager.Current != null ? RewindManager.Current
                        : new GameObject("Rewind test").AddComponent<RewindManager>();
                    rewind.UseKeyboard = false;
                    rewind.SimulateStep(Time.fixedDeltaTime);
                    Check(rewind.BeginRewind(), "Could not enter actual rewind");
                    Check(RewindManager.Rewinding, "Rewind state missing");
                    presenter.SubmitInput(TutorialPromptKey.Z, false, false, true);
                    stage = 6;
                }
                else if (stage == 6 && view != null && view.IsExiting)
                {
                    rewind.EndRewind();
                    Pass("Input lock ignores press/release; Z release completes even during actual rewind");
                    stage = 7;
                }
                else if (stage == 7 && view != null && view.Key == TutorialPromptKey.Control && !view.IsEntering)
                {
                    Time.timeScale = 0.5f;
                    presenter.SubmitInput(TutorialPromptKey.Control, true, true, false);
                    since = EditorApplication.timeSinceStartup;
                    stage = 8;
                }
                else if (stage == 8 && view == null && presenter.QueuedCount == 0)
                {
                    Check(EditorApplication.timeSinceStartup - since < 2.5, "UI animation slowed with game time");
                    Pass("Ctrl press completes at half speed; queue drains and UI uses real time");
                    presenter.Request(Prompt(0));
                    presenter.enabled = false;
                    Check(presenter.ActiveView == null && presenter.QueuedCount == 0, "Disabled presenter leaks state");
                    Pass("Disable clears active view, queue and chain");
                    Time.timeScale = 1f;
                    presenter.enabled = true;
                    triggerPlayer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Player.prefab")).GetComponent<PlayerController>();
                    triggerPlayer.TeleportJoined(new Vector2(4f, 0f));
                    triggerPlayer.enabled = false;
                    triggerPlayer.GetComponent<PlayerInput>().UseKeyboard = false;
                    triggerPlayer.Head.Motor.Teleport(Vector2.zero);
                    triggerProbe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TutorialPromptAssets.Folder + "/Trigger_01_Hover.prefab")).GetComponent<TutorialPromptTrigger2D>();
                    triggerProbe.Player = triggerPlayer;
                    since = EditorApplication.timeSinceStartup;
                    stage = 9;
                }
                else if (stage == 9 && EditorApplication.timeSinceStartup > since + 0.15)
                {
                    Check(!triggerProbe.HasTriggered && presenter.ActiveView == null, "Detached head incorrectly activated the tutorial");
                    triggerPlayer.Body.Motor.Teleport(Vector2.zero);
                    since = EditorApplication.timeSinceStartup; stage = 10;
                }
                else if (stage == 10 && EditorApplication.timeSinceStartup > since + 0.15)
                {
                    Check(triggerProbe.HasTriggered && presenter.ActiveView != null, "Actual body entry did not activate the prefab");
                    triggerPlayer.Body.Motor.Teleport(new Vector2(4f, 0f));
                    since = EditorApplication.timeSinceStartup; stage = 11;
                }
                else if (stage == 11 && EditorApplication.timeSinceStartup > since + 0.15)
                {
                    triggerPlayer.Body.Motor.Teleport(Vector2.zero);
                    since = EditorApplication.timeSinceStartup; stage = 12;
                }
                else if (stage == 12 && EditorApplication.timeSinceStartup > since + 0.15)
                {
                    Check(presenter.QueuedCount == 0, "Once-only trigger queued a repeated tutorial");
                    Pass("Actual player prefab: head ignored, body triggers, re-entry remains once-only");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
        }

        private static void Capture(TutorialPromptPresenter root, int width, int height, string name)
        {
            var cameraRoot = new GameObject("Tutorial Preview Camera", typeof(Camera));
            Camera camera = cameraRoot.GetComponent<Camera>();
            camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.17f, 0.23f); camera.cullingMask = 1 << 5;
            var target = new RenderTexture(width, height, 24);
            target.Create(); camera.targetTexture = target;
            Canvas canvas = root.GetComponent<Canvas>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.enabled = false; canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1f;
            canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            if (name != "05_Fade")
            {
                float scale = Mathf.Min(width / 1920f, height / 1080f);
                int centerY = Mathf.RoundToInt(height * root.ScreenAnchor.y);
                Color background = image.GetPixel(10, 10);
                Color edge = image.GetPixel(Mathf.RoundToInt(width / 2f - 645f * scale), centerY);
                Color center = image.GetPixel(width / 2, centerY + Mathf.RoundToInt(40f * scale));
                Check(Mathf.Abs(edge.grayscale - background.grayscale) < 0.03f, "Panel retains white background");
                Check(center.grayscale < background.grayscale * 0.9f,
                    "Panel did not render: center=" + center + ", background=" + background);
            }
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; scaler.enabled = true;
            Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(cameraRoot);
        }

        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Pass(string text) { File.AppendAllText(Output + "/results.txt", "PASS: " + text + "\n"); }
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; SessionState.SetBool(Pending, false);
            if (error != null) { File.AppendAllText(Output + "/results.txt", "FAIL: " + error + "\n"); Debug.LogException(error); }
            Time.timeScale = 1f;
            EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
