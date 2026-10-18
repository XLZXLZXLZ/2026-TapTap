using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap.Editor
{
    public static partial class RewindRegressionTests
    {
        public static void RunPlayerAnimationBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
            try
            {
                PlayerModelAssets.ReplacePlayerModels();
                ValidatePlayerModels();
                ValidateSkinnedAnimation();
                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) RenderPlayerAnimation();
                RunBatch();
            }
            catch (Exception exception)
            {
                File.AppendAllText(Path.Combine(Application.dataPath, "../player-animation-results.txt"), exception + "\n");
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static Vector3[] SkinVertices(GameObject model)
        {
            var vertices = new List<Vector3>();
            var mesh = new Mesh();
            try
            {
                foreach (SkinnedMeshRenderer skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(mesh, true);
                    Matrix4x4 matrix = model.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in mesh.vertices) vertices.Add(matrix.MultiplyPoint3x4(vertex));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            return vertices.ToArray();
        }

        private static float VertexDifference(Vector3[] a, Vector3[] b)
        {
            Check(a.Length > 0 && a.Length == b.Length, "Skinned vertex topology changed");
            float distance = 0f;
            for (int i = 0; i < a.Length; i++) distance = Mathf.Max(distance, Vector3.Distance(a[i], b[i]));
            return distance;
        }

        private static void ValidateSkinnedAnimation()
        {
            var messages = new List<string>();
            AnimationClip walk = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.WalkPath);
            AnimationClip idle = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.IdlePath);
            Check(walk.length > 0.9f && walk.length < 1.05f && Mathf.Abs(walk.frameRate - 24f) < 0.01f,
                "Walk must contain only the authored 1-24 frame cycle at 24 fps");
            Check(AnimationUtility.GetAnimationClipSettings(walk).loopTime, "Walk does not loop");
            Check(idle.length > 1.2f && idle.length < 1.4f && Mathf.Abs(idle.frameRate - 24f) < 0.01f,
                "Idle must contain stand.blend's authored 1-32 frame motion at 24 fps");
            Check(AnimationUtility.GetAnimationClipSettings(idle).loopTime, "Idle does not loop");
            GameObject playerObject = PrefabUtility.LoadPrefabContents(PlayerModelAssets.PlayerPath);
            try
            {
                PlayerController player = playerObject.GetComponent<PlayerController>();
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                {
                    Animator animator = entity.Visual.GetComponentInChildren<Animator>();
                    var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                    var walkState = Array.Find(controller.layers[0].stateMachine.states, state => state.state.name == "Walk").state;
                    Check(walkState != null && walkState.motion == walk && Mathf.Abs(walkState.speed - 1.3f) < 0.001f,
                        entity.name + " must use the shared Walk clip at 1.3x speed");
                    Check(animator.avatar != null && animator.avatar.isValid, "Missing valid Generic avatar");
                    SkinnedMeshRenderer[] skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
                    Check(skins.Length > 0 && Array.TrueForAll(skins, skin => skin.bones.Length > 0 && Array.TrueForAll(skin.bones, bone => bone != null)),
                        "Mesh is not bound to live bones");
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(walk))
                        Check(string.IsNullOrEmpty(binding.path) || animator.transform.Find(binding.path) != null,
                            entity.name + " cannot resolve animation bone: " + binding.path);
                    foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(idle))
                        Check(string.IsNullOrEmpty(binding.path) || animator.transform.Find(binding.path) != null,
                            entity.name + " cannot resolve Idle bone: " + binding.path);
                    idle.SampleAnimation(animator.gameObject, 0f);
                    Vector3[] rest = SkinVertices(animator.gameObject);
                    Vector3 rootPosition = animator.transform.localPosition;
                    walk.SampleAnimation(animator.gameObject, walk.length * 0.1f);
                    Vector3[] first = SkinVertices(animator.gameObject);
                    walk.SampleAnimation(animator.gameObject, walk.length * 0.6f);
                    float movement = VertexDifference(first, SkinVertices(animator.gameObject));
                    Check(movement > 0.01f, entity.name + " has animation curves but its mesh does not move");
                    Check(Vector3.Distance(rootPosition, animator.transform.localPosition) < 0.001f, "Walk translates the visual root");
                    idle.SampleAnimation(animator.gameObject, 0f);
                    Check(VertexDifference(rest, SkinVertices(animator.gameObject)) < 0.001f, "Idle does not restore the rest pose");
                    idle.SampleAnimation(animator.gameObject, idle.length * 0.5f);
                    float idleMovement = VertexDifference(rest, SkinVertices(animator.gameObject));
                    Check(idleMovement > 0.01f, entity.name + " Idle is a static reset instead of the stand animation");
                    Check(Vector3.Distance(rootPosition, animator.transform.localPosition) < 0.001f, "Idle translates the visual root");
                    messages.Add("PASS: " + entity.name + " skins=" + skins.Length + " Walk vertex movement=" + movement
                        + "; Idle vertex movement=" + idleMovement);
                }
                messages.Add("PASS: Shared Idle: " + idle.length + "s at 24fps; loop enabled; all bone paths resolve");
                messages.Add("PASS: Shared Walk: " + walk.length + "s at " + walk.frameRate + "fps; loop enabled; all bone paths resolve");
                File.WriteAllLines(Path.Combine(Application.dataPath, "../player-animation-results.txt"), messages);
            }
            finally { PrefabUtility.UnloadPrefabContents(playerObject); }
        }

        private static void PlayerWalkingAnimation()
        {
            using (var f = new Fixture())
            {
                f.Floor(); f.CreatePlayer(true);
                PlayerView view = FacingView(f);
                Animator MakeAnimator(MovableEntity entity, string part)
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/3D Resources/Player/" + part + "_rigged.fbx");
                    GameObject model = UnityEngine.Object.Instantiate(source, entity.Visual.Find("Facing"));
                    Animator animator = model.GetComponent<Animator>();
                    animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animator/Player/" + part + ".controller");
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Update(0f);
                    return animator;
                }
                Animator lower = MakeAnimator(f.Body, "down"), upper = MakeAnimator(f.Head, "up");
                view.ConfigureModels(f.Body.Visual.Find("Facing"), f.Head.Visual.Find("Facing"), lower, upper);
                Vector3[] rest = SkinVertices(lower.gameObject);
                void Animate(float dt)
                {
                    int frames = Mathf.CeilToInt(dt / Time.fixedDeltaTime);
                    for (int i = 0; i < frames; i++)
                    {
                        view.RenderMovementAnimations(RewindManager.Rewinding);
                        lower.Update(dt / frames); upper.Update(dt / frames);
                    }
                }
                f.Input.SetExternalInput(1f, false); f.Step(); Animate(0.12f);
                Check(lower.GetBool("Moving") && upper.GetBool("Moving"), "Connected halves did not start walking");
                Check(lower.GetCurrentAnimatorStateInfo(0).IsName("Walk") && upper.GetCurrentAnimatorStateInfo(0).IsName("Walk"),
                    "Animator never entered Walk");
                float skinMovement = VertexDifference(rest, SkinVertices(lower.gameObject));
                Check(skinMovement > 0.01f, "Animator does not move the actual skinned model; vertex movement=" + skinMovement
                    + " normalized time=" + lower.GetCurrentAnimatorStateInfo(0).normalizedTime);
                Animate(0.2f);
                Check(Mathf.Abs(lower.GetCurrentAnimatorStateInfo(0).normalizedTime - upper.GetCurrentAnimatorStateInfo(0).normalizedTime) < 0.01f,
                    "Connected machines are out of phase");
                f.Detach(f.Spawn, f.Spawn + new Vector2(5f, 3f));
                f.Input.SetExternalInput(1f, false); f.Step(3); Animate(0.12f);
                Check(lower.GetBool("Moving") && !upper.GetBool("Moving") && upper.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
                    "Detached head did not idle while lower half walked; lower=" + lower.GetBool("Moving")
                    + " upper=" + upper.GetBool("Moving") + " idle=" + upper.GetCurrentAnimatorStateInfo(0).IsName("Idle")
                    + " grounded=" + f.Body.Motor.Grounded + " phase=" + f.Player.Phase);
                var joined = f.Player.CaptureState(); joined.Phase = PlayerPhase.Joined;
                f.Player.RestoreState(in joined);
                f.Head.Motor.Teleport(f.Body.Motor.Position + Vector2.up * f.Config.JoinedOffset);
                f.Input.SetExternalInput(1f, false);
                f.Step(); Animate(0.12f);
                Check(upper.GetBool("Moving") && Mathf.Abs(lower.GetCurrentAnimatorStateInfo(0).normalizedTime
                    - upper.GetCurrentAnimatorStateInfo(0).normalizedTime) < 0.01f, "Rejoining restarted the upper gait out of phase; lower="
                    + lower.GetCurrentAnimatorStateInfo(0).normalizedTime + " upper=" + upper.GetCurrentAnimatorStateInfo(0).normalizedTime);
                f.Step(4);
                Check(f.Manager.BeginRewind(), "Could not begin walking rewind");
                float frozen = lower.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Animate(0.2f);
                Check(lower.speed == 0f && upper.speed == 0f, "Rewind did not pause the animators");
                Check(Mathf.Abs(lower.GetCurrentAnimatorStateInfo(0).normalizedTime - frozen) < 0.001f, "Gait advanced while rewinding");
                f.Manager.RewindBy(Time.fixedDeltaTime * 2f); f.Manager.EndRewind();
                f.Input.SetExternalInput(0f, false); f.Step(); Animate(0.12f);
                Check(lower.speed == 1f && upper.speed == 1f && !lower.GetBool("Moving") && !upper.GetBool("Moving"),
                    "Animation did not resume and idle after rewind");
                Vector3[] stopped = SkinVertices(lower.gameObject);
                AnimationClip idle = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.IdlePath);
                float idleTime = Mathf.Repeat(lower.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f) * idle.length;
                idle.SampleAnimation(lower.gameObject, idleTime);
                float stoppedDifference = VertexDifference(stopped, SkinVertices(lower.gameObject));
                // This fixture lives 10,000 units from the origin; allow two millimetres for float precision after moving.
                Check(stoppedDifference < 0.002f, "Stopping did not blend into the authored Idle pose; vertex delta=" + stoppedDifference
                    + " idle=" + lower.GetCurrentAnimatorStateInfo(0).IsName("Idle") + " blending=" + lower.IsInTransition(0));
            }
        }

        private static void RenderPlayerAnimation()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Animation preview camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1.5f;
            camera.transform.position = new Vector3(0f, 1.15f, -8f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.19f);
            var light = new GameObject("Preview light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f);
            var lower = new Animator[2]; var upper = new Animator[2];
            var snapshots = new Dictionary<SkinnedMeshRenderer, Mesh>();
            for (int i = 0; i < 2; i++)
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelAssets.PlayerPath), scene);
                root.transform.position = new Vector3(i == 0 ? -1.1f : 1.1f, 0f, 0f);
                PlayerController player = root.GetComponent<PlayerController>();
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer is SpriteRenderer || renderer is LineRenderer || renderer is ParticleSystemRenderer) renderer.enabled = false;
                foreach (MovableEntity entity in new[] { player.Body, player.Head })
                    entity.Visual.Find("Facing").localRotation = Quaternion.Euler(0f, 120f, 0f);
                if (i == 1) player.Head.transform.localPosition += Vector3.up * 0.6f;
                lower[i] = player.Body.Visual.GetComponentInChildren<Animator>();
                upper[i] = player.Head.Visual.GetComponentInChildren<Animator>();
                // Synchronous batch captures do not advance Unity's skinning render frame.
                // Bake each posed skin to a preview-only static mesh for every captured frame.
                foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var proxy = new GameObject("Preview mesh");
                    proxy.transform.SetParent(skin.transform, false);
                    var mesh = new Mesh(); mesh.MarkDynamic();
                    proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                    proxy.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    snapshots.Add(skin, mesh);
                    skin.enabled = false;
                }
            }
            AnimationClip walk = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.WalkPath);
            AnimationClip idle = PlayerModelAssets.AnimationClipAt(PlayerModelAssets.IdlePath);
            string directory = Path.Combine(Application.dataPath, "../player-animation-frames");
            Directory.CreateDirectory(directory);
            var target = new RenderTexture(960, 540, 24);
            var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                for (int frame = 0; frame < 24; frame++)
                {
                    float time = walk.length * frame / 24f;
                    for (int i = 0; i < 2; i++)
                    {
                        walk.SampleAnimation(lower[i].gameObject, time);
                        (i == 0 ? walk : idle).SampleAnimation(upper[i].gameObject, i == 0 ? time : 0f);
                    }
                    foreach (KeyValuePair<SkinnedMeshRenderer, Mesh> snapshot in snapshots)
                        snapshot.Key.BakeMesh(snapshot.Value, true);
                    camera.Render(); RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(directory, frame.ToString("D2") + ".png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = previous;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
                foreach (Mesh mesh in snapshots.Values) UnityEngine.Object.DestroyImmediate(mesh);
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }
    }
}
