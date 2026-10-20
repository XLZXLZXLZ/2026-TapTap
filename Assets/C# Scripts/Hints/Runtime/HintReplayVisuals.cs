using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TapTap.Hints
{
    internal sealed class HintReplayVisuals
    {
        internal readonly List<Transform> Transforms = new List<Transform>();
        internal readonly List<Renderer> Renderers = new List<Renderer>();
        internal readonly List<HintReplayNode> Layout = new List<HintReplayNode>();
        private readonly List<int> childCounts = new List<int>();
        private readonly List<int> siblingIndices = new List<int>();

        internal HintReplayVisuals(Transform body, Transform head)
        {
            Add(body, -1);
            Add(head, -1);
        }

        private void Add(Transform source, int parent)
        {
            int index = Transforms.Count;
            Transforms.Add(source);
            childCounts.Add(source.childCount);
            siblingIndices.Add(source.GetSiblingIndex());
            int count = 0;
            foreach (Renderer renderer in source.GetComponents<Renderer>())
                if (renderer is SkinnedMeshRenderer || renderer is SpriteRenderer
                    || renderer is MeshRenderer && source.GetComponent<MeshFilter>() != null)
                { Renderers.Add(renderer); count++; }
            Layout.Add(new HintReplayNode { Name = source.name, Parent = parent, RendererCount = count });
            for (int i = 0; i < source.childCount; i++) Add(source.GetChild(i), index);
        }

        internal bool Matches(HintReplayNode[] expected)
        {
            if (expected == null || expected.Length != Layout.Count) return false;
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] == null || expected[i].Name != Layout[i].Name || expected[i].Parent != Layout[i].Parent
                    || expected[i].RendererCount != Layout[i].RendererCount) return false;
            return true;
        }

        internal bool StillValid()
        {
            for (int i = 0; i < Transforms.Count; i++)
            {
                Transform node = Transforms[i];
                if (node == null || node.name != Layout[i].Name) return false;
                if (node.childCount != childCounts[i]) return false;
                int parent = Layout[i].Parent;
                if (parent >= 0 && (node.parent != Transforms[parent] || node.GetSiblingIndex() != siblingIndices[i])) return false;
            }
            foreach (Renderer renderer in Renderers) if (renderer == null) return false;
            return true;
        }

        internal void CopyRenderers(Transform[] targets, Material material, List<Renderer> output)
        {
            var mapping = new Dictionary<Transform, Transform>();
            for (int i = 0; i < Transforms.Count; i++) mapping.Add(Transforms[i], targets[i]);
            foreach (Renderer source in Renderers)
            {
                Transform target = mapping[source.transform];
                Renderer copy;
                if (source is SkinnedMeshRenderer skin)
                {
                    var clone = target.gameObject.AddComponent<SkinnedMeshRenderer>();
                    clone.sharedMesh = skin.sharedMesh;
                    Transform[] bones = skin.bones;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == null) continue;
                        if (!mapping.TryGetValue(bones[i], out Transform bone))
                            throw new InvalidOperationException("角色骨骼位于显示层级之外，无法创建独立虚影。请将骨骼包含在 Visual 下。");
                        bones[i] = bone;
                    }
                    clone.bones = bones;
                    if (skin.rootBone != null && mapping.TryGetValue(skin.rootBone, out Transform rootBone)) clone.rootBone = rootBone;
                    clone.localBounds = skin.localBounds;
                    clone.updateWhenOffscreen = true;
                    clone.quality = skin.quality;
                    for (int i = 0; skin.sharedMesh != null && i < skin.sharedMesh.blendShapeCount; i++)
                        clone.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                    copy = clone;
                }
                else if (source is SpriteRenderer sprite)
                {
                    var clone = target.gameObject.AddComponent<SpriteRenderer>();
                    clone.sprite = sprite.sprite;
                    clone.flipX = sprite.flipX; clone.flipY = sprite.flipY;
                    clone.drawMode = sprite.drawMode; clone.size = sprite.size;
                    clone.color = Color.white;
                    copy = clone;
                }
                else
                {
                    target.gameObject.AddComponent<MeshFilter>().sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
                    copy = target.gameObject.AddComponent<MeshRenderer>();
                }
                int slots = Mathf.Max(1, source.sharedMaterials.Length);
                var materials = new Material[slots];
                for (int i = 0; i < slots; i++) materials[i] = material;
                copy.sharedMaterials = materials;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                copy.lightProbeUsage = LightProbeUsage.Off;
                copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
                copy.sortingLayerID = source.sortingLayerID;
                copy.sortingOrder = source.sortingOrder + 1;
                copy.enabled = false;
                output.Add(copy);
            }
        }
    }
}
