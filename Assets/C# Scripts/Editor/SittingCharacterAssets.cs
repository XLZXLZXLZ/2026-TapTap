using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapTap.Editor
{
    public static class SittingCharacterAssets
    {
        public const string PrefabPath = "Assets/Prefabs/Characters/SittingCharacter.prefab";
        private const string Models = "Assets/Art/3D Resources/Player/";
        private const string ModelPath = Models + "set_rigged.fbx";
        private const string ControllerPath = "Assets/Animator/Player/SittingIdle.controller";

        [MenuItem("TapTap/维护/素材装配/生成坐姿装饰角色")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode。");
            ConfigureImport();
            AnimationClip idle = PlayerModelAssets.AnimationClipAt(ModelPath);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Length == 0 ? machine.AddState("Idle") : machine.states[0].state;
            state.name = "Idle";
            state.motion = idle;
            state.writeDefaultValues = false;
            machine.defaultState = state;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);

            var root = new GameObject("SittingCharacter");
            try
            {
                var facing = new GameObject("Visual").transform;
                facing.SetParent(root.transform, false);
                facing.localRotation = Quaternion.Euler(0f, 180f, 0f);
                facing.localScale = Vector3.one * 0.5f;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
                model.transform.SetParent(facing, false);
                model.name = "Sitting Model";
                Animator animator = model.GetComponent<Animator>();
                if (animator == null) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var lower = AttachEyes(model, "blue", "Lower", "down");
                var upper = AttachEyes(model, "red", "Upper", "up");
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(MaterialFor).ToArray();
                    renderer.sortingOrder = 2;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                root.AddComponent<SceneryCharacterBlink>().Configure(lower, upper,
                    AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animator/Player/Blink_blue.anim"),
                    AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animator/Player/Blink_red.anim"));
                // Save a sitting pose as well, so placement is legible before entering Play Mode.
                idle.SampleAnimation(model, 0f);
                var bounds = PlayerModelAssets.LocalBounds(root.transform);
                facing.localPosition = new Vector3(0f, -bounds.min.y, 0f);
                LevelPrefabBuilder.RecordInstanceOverrides(model);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("保存坐姿装饰角色失败。");
            }
            finally { Object.DestroyImmediate(root); }
            ValidateAndRender();
            Debug.Log("坐姿装饰角色已生成并验证：" + PrefabPath);
        }

        private static void ConfigureImport()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("请先运行 Tools/Blender/export_sitting_character.py。");
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = false;
            importer.optimizeGameObjects = false;
            importer.preserveHierarchy = true;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            var clip = importer.defaultClipAnimations.First();
            clip.name = "SittingIdle";
            clip.loopTime = true;
            clip.loopPose = true;
            clip.lockRootPositionXZ = clip.lockRootHeightY = clip.lockRootRotation = true;
            clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = clip.keepOriginalOrientation = true;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
        }

        private static GameObject AttachEyes(GameObject model, string color, string boneName, string part)
        {
            Transform bone = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == boneName);
            // Derive the socket from the player's rest skeleton, rather than fitting eyes
            // in world space to the bent sitting pose. Both exports share this bind pose.
            var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Models + part + "_rigged.fbx"));
            try
            {
                var eyes = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Models + "blink_" + color + "eyes_baked.fbx"));
                eyes.name = color == "blue" ? "Blue Eyes" : "Red Eyes";
                eyes.transform.SetParent(reference.transform, false);
                eyes.transform.SetParent(reference.GetComponentsInChildren<Transform>(true).Single(t => t.name == boneName), true);
                eyes.transform.SetParent(bone, false);
                foreach (Animation animation in eyes.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(animation);
                foreach (Animator animator in eyes.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                LevelPrefabBuilder.RecordInstanceOverrides(eyes);
                return eyes;
            }
            finally { Object.DestroyImmediate(reference); }
        }

        private static Material MaterialFor(Material source)
        {
            string file;
            switch (source != null ? source.name : "")
            {
                case "Red skin": file = "Upper"; break;
                case "Blue skin": file = "Lower"; break;
                case "Eye glow": file = "EyeGlow"; break;
                case "Silver rims": file = "SilverRims"; break;
                case "Magnet black": file = "MagnetBlack"; break;
                case "Magnetic core": file = "MagneticCore"; break;
                case "Limb rubber": file = "LimbRubber"; break;
                case "Imported light stem silver": file = "AntennaSilver"; break;
                case "Light bulb - warm emission": file = "AntennaLight"; break;
                default:
                    if (source != null && source.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)) return source;
                    throw new InvalidOperationException("未知角色材质：" + (source != null ? source.name : "null"));
            }
            var result = AssetDatabase.LoadAssetAtPath<Material>(Models + "New/Materials/" + file + ".mat");
            if (result == null) throw new InvalidOperationException("缺少材质：" + file);
            return result;
        }

        [MenuItem("TapTap/验证/坐姿装饰角色")]
        public static void ValidateAndRender()
        {
            Directory.CreateDirectory("Logs/SittingCharacter");
            var preview = new PreviewRenderUtility();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            preview.AddSingleGO(root);
            try
            {
                if (root.GetComponentInChildren<PlayerController>(true) != null || root.GetComponentsInChildren<Collider2D>(true).Length != 0)
                    throw new InvalidOperationException("装饰角色不应包含玩家或碰撞逻辑。");
                if (root.GetComponentsInChildren<Animator>(true).Length != 1) throw new InvalidOperationException("应只有一个身体 Animator。");
                var animator = root.GetComponentInChildren<Animator>();
                var controller = (AnimatorController)animator.runtimeAnimatorController;
                if (controller.parameters.Length != 0 || controller.layers[0].stateMachine.states.Length != 1)
                    throw new InvalidOperationException("装饰角色应只有一个 Idle 状态。");
                var idle = (AnimationClip)controller.layers[0].stateMachine.defaultState.motion;
                if (!AnimationUtility.GetAnimationClipSettings(idle).loopTime) throw new InvalidOperationException("Idle 必须循环。");
                var upper = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Upper");
                var eyes = upper.Find("Red Eyes");
                if (eyes == null || root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Lower").Find("Blue Eyes") == null)
                    throw new InvalidOperationException("眼睛缺少正确的骨骼挂点。");
                var blink = root.GetComponent<SceneryCharacterBlink>();
                var eye = eyes.GetComponentsInChildren<MeshRenderer>()[0].transform;
                var blueEye = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Blue Eyes")
                    .GetComponentsInChildren<MeshRenderer>()[0].transform;
                blink.ResetBlink();
                Vector3 open = eye.localScale;
                Vector3 blueOpen = blueEye.localScale;
                idle.SampleAnimation(animator.gameObject, 0f);
                Vector3 pose = upper.localPosition;
                Vector3 eyeLocal = eyes.localPosition;
                var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>().First(s => s.name == "Leg_L");
                var baked = new Mesh();
                skin.BakeMesh(baked);
                Vector3[] firstVertices = baked.vertices;
                Render(preview, root, "Open");
                blink.Simulate(5.166667f);
                if ((open - eye.localScale).sqrMagnitude < 0.001f || (blueOpen - blueEye.localScale).sqrMagnitude < 0.001f)
                    throw new InvalidOperationException("眨眼没有改变两组眼睛。");
                Render(preview, root, "Blink");
                blink.Simulate(0.5f);
                if ((open - eye.localScale).sqrMagnitude > 0.00001f) throw new InvalidOperationException("眨眼后没有恢复。");
                idle.SampleAnimation(animator.gameObject, idle.length * 0.5f);
                if ((pose - upper.localPosition).sqrMagnitude < 0.00000001f) throw new InvalidOperationException("坐姿动画没有运动。");
                skin.BakeMesh(baked);
                Vector3[] nextVertices = baked.vertices;
                Object.DestroyImmediate(baked);
                if (!firstVertices.Where((vertex, index) => (vertex - nextVertices[index]).sqrMagnitude > 0.0001f).Any())
                    throw new InvalidOperationException("动画未驱动蒙皮。");
                if ((eyes.localPosition - eyeLocal).sqrMagnitude > 0.00000001f) throw new InvalidOperationException("眼睛挂点漂移。");
                Render(preview, root, "MidIdle");
                blink.enabled = false;
                if ((open - eye.localScale).sqrMagnitude > 0.00001f) throw new InvalidOperationException("禁用后未恢复睁眼。");
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    if (renderer.sharedMaterials.Any(m => m == null || !m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)))
                        throw new InvalidOperationException("材质未正确配置。");
                animator.Rebind();
                animator.Update(0f);
                Vector3 rootPosition = animator.transform.localPosition;
                for (int frame = 0; frame < 180; frame++) animator.Update(1f / 30f);
                if (!animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")
                    || (animator.transform.localPosition - rootPosition).sqrMagnitude > 0.000001f)
                    throw new InvalidOperationException("独立 Animator 未正常循环或产生根位移。");
                File.WriteAllText("Logs/SittingCharacter/validation.txt", "PASS: saved prefab; standalone single looping Idle over 6 seconds; animated skin; bone-mounted red/blue eyes; both blink and restore; disable resets; URP materials; no root drift.\nDuration=" + idle.length);
            }
            finally { preview.Cleanup(); }
        }

        private static void Render(PreviewRenderUtility preview, GameObject root, string name)
        {
            Bounds bounds = PlayerModelAssets.LocalBounds(root.transform);
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = bounds.extents.y * 1.25f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 100f;
            preview.camera.transform.position = bounds.center + new Vector3(3.5f, 1.5f, -8f);
            preview.camera.transform.LookAt(bounds.center);
            preview.camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].transform.rotation = Quaternion.Euler(40, 35, 0);
            preview.lights[1].intensity = 0.7f;
            preview.ambientColor = Color.gray;
            preview.BeginStaticPreview(new Rect(0, 0, 800, 800));
            preview.Render(true);
            Texture2D texture = preview.EndStaticPreview();
            File.WriteAllBytes("Logs/SittingCharacter/" + name + ".png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
