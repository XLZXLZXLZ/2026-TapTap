using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TapTap.Editor
{
    public static class PlayerModelAssets
    {
        public const string PlayerPath = "Assets/Prefabs/Characters/Player.prefab";
        private const string ModelFolder = "Assets/Art/3D Resources/Player/";
        public const string WalkPath = ModelFolder + "walk_rigged.fbx";
        public const string IdlePath = ModelFolder + "idle_rigged.fbx";
        private const string AnimatorFolder = "Assets/Animator/Player";
        private const string MaterialFolder = "Assets/Art/Materials/Player";
        private const float JoinedVisualGap = 0.05f;

        [MenuItem("TapTap/维护/素材装配/更新绑定模型与走路动画")]
        public static void ReplacePlayerModels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出 Play Mode，再替换主角模型。");
            LevelPrefabBuilder.EnsureFolder(AnimatorFolder);
            ConfigureImports();
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                PlayerController player = root.GetComponent<PlayerController>();
                PlayerView view = root.GetComponent<PlayerView>();
                if (player == null || view == null || player.Config == null)
                    throw new InvalidOperationException("主角 Prefab 缺少控制器、表现组件或配置。");
                Transform lower = ReplacePart(player.Body, "down", player.Config, out Animator lowerAnimator, out GameObject lowerEyes);
                Transform upper = ReplacePart(player.Head, "up", player.Config, out Animator upperAnimator, out GameObject upperEyes);
                view.ConfigureModels(lower, upper, lowerAnimator, upperAnimator);
                PlayerBlinkView blink = root.GetComponent<PlayerBlinkView>();
                if (blink == null) blink = root.AddComponent<PlayerBlinkView>();
                blink.Configure(player, view, lowerEyes, upperEyes, EnsureBlinkClip("blue"), EnsureBlinkClip("red"));
                var lowerExpressions = AttachExpressions(lowerAnimator.gameObject, lowerEyes, "blue", "Lower");
                var upperExpressions = AttachExpressions(upperAnimator.gameObject, upperEyes, "red", "Upper");
                PlayerExpressionView expressions = root.GetComponent<PlayerExpressionView>();
                if (expressions == null) expressions = root.AddComponent<PlayerExpressionView>();
                expressions.Configure(player, view, blink, lowerExpressions, upperExpressions);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath, out bool saved);
                if (!saved) throw new InvalidOperationException("保存主角 Prefab 失败。");
                Debug.Log("主角模型、Idle/Walk、眨眼及磁吸表情已配置；保留现有视觉尺寸、位置与碰撞配置。");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform ReplacePart(MovableEntity entity, string modelName, PlayerConfig config,
            out Animator animator, out GameObject eyes)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + modelName + "_rigged.fbx");
            if (entity == null || entity.Visual == null || source == null)
                throw new InvalidOperationException("缺少上下半身实体、视觉节点或模型：" + modelName);
            Transform visual = entity.Visual;
            Transform oldFacing = visual.Find("Facing");
            Transform oldFit = oldFacing != null ? oldFacing.Find("Model Fit") : null;
            bool preservePlacement = oldFit != null;
            Vector3 fitPosition = preservePlacement ? oldFit.localPosition : Vector3.zero;
            Quaternion fitRotation = preservePlacement ? oldFit.localRotation : Quaternion.identity;
            Vector3 fitScale = preservePlacement ? oldFit.localScale : Vector3.one;
            Vector3 facingPosition = oldFacing != null ? oldFacing.localPosition : Vector3.zero;
            Quaternion facingRotation = oldFacing != null ? oldFacing.localRotation : Quaternion.Euler(0f, 180f, 0f);
            Vector3 facingScale = oldFacing != null ? oldFacing.localScale : Vector3.one;
            bool constrainProportions = preservePlacement
                && new SerializedObject(oldFit).FindProperty("m_ConstrainProportionsScale").boolValue;
            for (int i = visual.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(visual.GetChild(i).gameObject);
            SpriteRenderer sprite = visual.GetComponent<SpriteRenderer>();
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            BoxCollider2D box = entity.GetComponent<BoxCollider2D>();
            if (!preservePlacement)
            {
                visual.localPosition = box.offset;
                visual.localRotation = Quaternion.identity;
                visual.localScale = Vector3.one;
            }
            var facing = new GameObject("Facing").transform;
            facing.SetParent(visual, false);
            facing.localPosition = facingPosition;
            facing.localRotation = facingRotation;
            facing.localScale = facingScale;
            var fit = new GameObject("Model Fit").transform;
            fit.SetParent(facing, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, entity.gameObject.scene);
            model.transform.SetParent(fit, false);
            eyes = AttachEyes(model, modelName == "up" ? "red" : "blue", modelName == "up" ? "Upper" : "Lower");
            Bounds bounds = LocalBounds(fit);
            // Keep X/Z proportions, but fit height independently: these two authored halves have
            // different proportions, while gameplay intentionally gives them the same collision size.
            // Allow a small silhouette margin during turns instead of making deep models look tiny.
            float widestAngle = Mathf.Min(60f * Mathf.Deg2Rad, Mathf.Atan2(bounds.size.z, bounds.size.x));
            float width = bounds.size.x * Mathf.Cos(widestAngle) + bounds.size.z * Mathf.Sin(widestAngle);
            float height = Mathf.Min(box.size.y, config.JoinedOffset - JoinedVisualGap);
            float horizontalScale = Mathf.Min(box.size.x / Mathf.Max(0.0001f, bounds.size.x),
                box.size.x * 1.1f / Mathf.Max(0.0001f, width));
            Vector3 scale = new Vector3(horizontalScale, height / Mathf.Max(0.0001f, bounds.size.y), horizontalScale);
            // Existing placement is authored data. Auto-fit only when creating a
            // visual hierarchy for the first time, never while replacing its model.
            fit.localScale = preservePlacement ? fitScale : scale;
            fit.localPosition = preservePlacement ? fitPosition : -Vector3.Scale(bounds.center, scale);
            fit.localRotation = fitRotation;
            var serializedFit = new SerializedObject(fit);
            serializedFit.FindProperty("m_ConstrainProportionsScale").boolValue = constrainProportions;
            serializedFit.ApplyModifiedPropertiesWithoutUndo();
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = RenderMaterial(materials[i], modelName, i);
                renderer.sharedMaterials = materials;
                renderer.sortingOrder = 2;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = EnsureController(modelName);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            LevelPrefabBuilder.RecordInstanceOverrides(model);
            Debug.Log(modelName + " original bounds=" + bounds + "; visual scale=" + fit.localScale
                + "; preserved placement=" + preservePlacement + "; collider=" + box.size);
            return facing;
        }

        private static GameObject AttachEyes(GameObject model, string color, string boneName)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + "blink_" + color + "eyes_baked.fbx");
            if (source == null) throw new InvalidOperationException("缺少烘焙眼睛模型，请运行 Tools/Blender/export_player.py。");
            Transform bone = Array.Find(model.GetComponentsInChildren<Transform>(true), t => t.name == boneName);
            if (bone == null) throw new InvalidOperationException("缺少眼睛挂点骨骼：" + boneName);
            var eyes = (GameObject)PrefabUtility.InstantiatePrefab(source, model.scene);
            eyes.name = color == "red" ? "Red Eyes" : "Blue Eyes";
            eyes.transform.SetParent(model.transform, false);
            // Both FBXs share authored model coordinates. Preserve that placement
            // when moving the whole eye group beneath the live body bone.
            eyes.transform.SetParent(bone, true);
            foreach (Animation animation in eyes.GetComponentsInChildren<Animation>(true))
                UnityEngine.Object.DestroyImmediate(animation);
            foreach (Animator animator in eyes.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(animator);
            LevelPrefabBuilder.RecordInstanceOverrides(eyes);
            return eyes;
        }

        private static AnimationClip EnsureBlinkClip(string color)
        {
            AnimationClip source = AnimationClipAt(ModelFolder + "blink_" + color + "eyes_baked.fbx");
            string path = AnimatorFolder + "/Blink_" + color + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.name = "Blink_" + color;
            clip.legacy = true;
            clip.frameRate = 24f;
            int count = 0;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                if (binding.type != typeof(Transform) || string.IsNullOrEmpty(binding.path)
                    || !binding.propertyName.StartsWith("m_LocalScale", StringComparison.Ordinal)) continue;
                AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding));
                count++;
            }
            if (count < 2) throw new InvalidOperationException("眼睛 FBX 缺少烘焙缩放曲线：" + color);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        private static PlayerExpressionView.EyeSet AttachExpressions(GameObject model, GameObject normal,
            string color, string boneName)
        {
            Transform bone = Array.Find(model.GetComponentsInChildren<Transform>(true), t => t.name == boneName);
            Renderer[] renderers = normal.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            Transform normalPivot = EyePivot(model, bone, normal, "Normal Eyes Pivot", bounds.center);
            Transform happy = AttachExpression(model, bone, color, "happy", bounds.center);
            Transform sad = AttachExpression(model, bone, color, "sad", bounds.center);
            LevelPrefabBuilder.RecordInstanceOverrides(model);
            return new PlayerExpressionView.EyeSet(normalPivot, happy, sad);
        }

        private static Transform AttachExpression(GameObject model, Transform bone, string color,
            string expression, Vector3 center)
        {
            string path = ModelFolder + "New/" + expression + "_" + color + "eyes.fbx";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("缺少表情模型：" + path);
            var group = (GameObject)PrefabUtility.InstantiatePrefab(source, model.scene);
            group.transform.SetParent(model.transform, false);
            group.name = expression == "happy" ? "Happy Eyes" : "Sad Eyes";
            foreach (Renderer renderer in group.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = RenderMaterial(materials[i], color, i);
                renderer.sharedMaterials = materials;
                renderer.sortingOrder = 2;
            }
            return EyePivot(model, bone, group, expression == "happy" ? "Happy Eyes Pivot" : "Sad Eyes Pivot", center);
        }

        private static Transform EyePivot(GameObject model, Transform bone, GameObject group, string name, Vector3 center)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(model.transform, false);
            pivot.localPosition = model.transform.InverseTransformPoint(center);
            group.transform.SetParent(pivot, true);
            pivot.SetParent(bone, true);
            LevelPrefabBuilder.RecordInstanceOverrides(group);
            return pivot;
        }

        public static Bounds LocalBounds(Transform root)
        {
            bool found = false;
            Bounds result = default;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds bounds = renderer.localBounds;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    var baked = new Mesh();
                    // Bake with the renderer scale, then transform the resulting local vertices.
                    // The other overload scales bones a second time under a fitted, nonuniform parent.
                    skin.BakeMesh(baked, true);
                    bounds = baked.bounds;
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                Matrix4x4 matrix = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = matrix.MultiplyPoint3x4(corner);
                    if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                    else result.Encapsulate(point);
                }
            }
            if (!found) throw new InvalidOperationException("模型缺少可显示的网格。");
            return result;
        }

        private static AnimatorController EnsureController(string name)
        {
            string path = AnimatorFolder + "/" + name + ".controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if (!Array.Exists(controller.parameters, parameter => parameter.name == "Moving"))
                controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState idle = EnsureState(machine, "Idle");
            AnimatorState walk = EnsureState(machine, "Walk");
            idle.motion = AnimationClipAt(IdlePath);
            walk.motion = AnimationClipAt(WalkPath);
            machine.defaultState = idle;
            // PlayerView crossfades both machines with the same starting phase.
            // Separate transitions would restart the upper gait when it reconnects.
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private static AnimatorState EnsureState(AnimatorStateMachine machine, string name)
        {
            foreach (ChildAnimatorState child in machine.states)
                if (child.state.name == name) return child.state;
            return machine.AddState(name);
        }

        public static AnimationClip AnimationClipAt(string path)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            throw new InvalidOperationException("资源缺少动画片段：" + path);
        }

        private static void ConfigureImports()
        {
            foreach (string expression in new[] { "happy", "sad" })
            foreach (string color in new[] { "red", "blue" })
            {
                string path = ModelFolder + "New/" + expression + "_" + color + "eyes.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("缺少表情模型：" + path);
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importCameras = importer.importLights = false;
                importer.preserveHierarchy = true;
                importer.SaveAndReimport();
            }
            foreach (string color in new[] { "red", "blue" })
            {
                string path = ModelFolder + "blink_" + color + "eyes_baked.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("缺少烘焙眼睛：" + path);
                importer.animationType = ModelImporterAnimationType.Legacy;
                importer.importAnimation = true;
                importer.importCameras = importer.importLights = false;
                importer.preserveHierarchy = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                var clips = importer.defaultClipAnimations;
                if (clips.Length == 0) throw new InvalidOperationException("眼睛模型没有动画：" + path);
                var clip = clips[0];
                clip.name = "Blink_" + color;
                clip.firstFrame = 1f; clip.lastFrame = 9f;
                clip.loopTime = false;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
            }
            foreach (string part in new[] { "down", "up" })
            {
                string path = ModelFolder + part + "_rigged.fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("请先运行 Tools/Blender/export_player.py 导出绑定模型。");
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.optimizeGameObjects = false;
                importer.preserveHierarchy = true;
                importer.SaveAndReimport();
            }
            Avatar avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ModelFolder + "down_rigged.fbx");
            foreach (string path in new[] { WalkPath, IdlePath })
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) throw new InvalidOperationException("缺少导出的动画：" + path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.importAnimation = true;
                importer.optimizeGameObjects = false;
                importer.preserveHierarchy = true;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                if (clips.Length == 0) throw new InvalidOperationException("FBX 没有烘焙动作：" + path);
                ModelImporterClipAnimation clip = clips[0];
                clip.name = path == WalkPath ? "Walk" : "Idle";
                clip.loopTime = true;
                clip.loopPose = false;
                clip.lockRootPositionXZ = clip.lockRootHeightY = clip.lockRootRotation = true;
                clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = clip.keepOriginalOrientation = true;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
            }
        }

        private static Material RenderMaterial(Material source, string part, int index)
        {
            string authored = source == null ? null : source.name;
            string file = authored == "Red skin" ? "Upper" : authored == "Blue skin" ? "Lower"
                : authored == "Eye glow" ? "EyeGlow" : authored == "Limb rubber" ? "LimbRubber"
                : authored == "Silver rims" ? "SilverRims" : authored == "Magnet black" ? "MagnetBlack"
                : authored == "Magnetic core" ? "MagneticCore"
                : authored == "Imported light stem silver" ? "AntennaSilver"
                : authored == "Light bulb - warm emission" ? "AntennaLight" : null;
            if (authored == "Expression eye white") file = "ExpressionWhite";
            if (file != null)
            {
                Material authoredMaterial = AssetDatabase.LoadAssetAtPath<Material>(ModelFolder + "New/Materials/" + file + ".mat");
                if (authoredMaterial != null) return authoredMaterial;
            }
            if (source != null && source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                return source;
            string name = source != null ? source.name.Replace('/', '_').Replace('\\', '_') : "Default_" + index;
            string path = MaterialFolder + "/" + part + "_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            LevelPrefabBuilder.EnsureFolder(MaterialFolder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = part + " " + name };
            Color color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source != null && source.HasProperty("_Color") ? source.color : Color.white;
            Texture texture = source != null && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                : source != null && source.HasProperty("_MainTex") ? source.mainTexture : null;
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
