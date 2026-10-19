using System.Collections.Generic;
using UnityEngine;

namespace TapTap
{
    [ExecuteAlways, DisallowMultipleComponent]
    [DefaultExecutionOrder(-120)]
    [AddComponentMenu("TapTap/World/Progress Key")]
    [RequireComponent(typeof(BoxCollider2D), typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProgressKey : MonoBehaviour
    {
        [Header("拾取轻推镜头（真实秒数，不改变游戏速度）")]
        [Range(0.01f, 0.6f)] public float ZoomAmount = 0.06f;
        [Min(0.01f)] public float ZoomInDuration = 0.22f;
        [Min(0f)] public float ZoomHoldDuration = 0.05f;
        [Min(0.01f)] public float ZoomOutDuration = 0.3f;
        public Color Tint = Color.white;
        [Header("正弦浮动")]
        [Min(0f)] public float FloatAmplitude = 0.06f;
        [Min(0.1f)] public float FloatPeriod = 2.4f;
        [Header("白色荧光")]
        public Material GlowParticleMaterial;
        public ParticleSystem PickupBurstPrefab;
        [Tooltip("拾取时使用的较弱粒子；原 Pickup Burst 保留用于开门。")]
        public ParticleSystem PickupFeedbackPrefab;
        [Min(0f)] public float GlowParticleRate = 12f;
        [Header("携带：相对身体，单位为格")]
        public Vector2 FollowOffset = new Vector2(0.8f, 0.65f);
        [Min(0.01f)] public float FollowSmoothTime = 0.16f;
        [Min(0f)] public float CarryFloatAmplitude = 0.08f;
        private PlayerController carrier;
        private Transform carryVisual;
        private int facing = 1;
        private bool unlocking;
        public Transform CarryVisual => carryVisual;
        private ParticleSystem glowParticles, pickupBurst;
        private Mesh mesh;
        private MeshRenderer visual;
        private BoxCollider2D trigger;
        private bool visualDirty;
        private Vector3[] restVertices, floatingVertices;
        private float floatPhase, floatOffset;
        public bool IsCollected { get; private set; }

        private void OnEnable()
        {
            floatPhase = floatOffset = 0f;
            RebuildVisual();
            ApplyStage();
            if (Application.isPlaying) EnsureGlowParticles();
        }

        public void ApplyStage()
        {
            if (!Application.isPlaying) { SetCollected(false); return; }
            GameStageProgression stage = GameStageProgression.ForScene(gameObject.scene);
            if (stage.Stage == GameStage.CarryingKey && stage.BindCarriedKey(this))
            {
                if (carrier == null) BeginCarry(ReturnJourneyContact.FindPlayer(gameObject.scene));
            }
            else SetCollected(stage.Stage >= GameStage.CarryingKey);
        }

        public bool TryCollect(PlayerController player) => !IsCollected
            && GameStageProgression.ForScene(gameObject.scene).Collect(this, player);

        public void SetCollected(bool value)
        {
            IsCollected = value;
            if (visual == null) visual = GetComponent<MeshRenderer>();
            if (trigger == null) trigger = GetComponent<BoxCollider2D>();
            visual.enabled = trigger.enabled = !value;
            if (!value) { carrier = null; unlocking = false; DestroyCarryVisual(); }
            if (glowParticles != null)
            {
                if (value) glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                else if (isActiveAndEnabled && !glowParticles.isPlaying) glowParticles.Play();
            }
        }

        public void BeginCarry(PlayerController player)
        {
            if (player == null) return;
            carrier = player;
            SetCollected(true);
            if (carryVisual != null) return;
            var owner = new GameObject("Carried Progress Key");
            // This renderer is outside region roots, so leaving region 19 cannot hide it.
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(owner, gameObject.scene);
            owner.transform.position = transform.position;
            owner.transform.rotation = transform.rotation;
            owner.transform.localScale = transform.lossyScale;
            owner.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = owner.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = visual.sharedMaterial;
            renderer.sortingOrder = 13;
            carryVisual = owner.transform;
        }

        public void BeginUnlock() => unlocking = true;
        public void ResumeCarry() => unlocking = false;

        public void Consume()
        {
            SetCollected(true);
            carrier = null;
            unlocking = false;
            DestroyCarryVisual();
        }

        private void DestroyCarryVisual()
        {
            if (carryVisual != null) Destroy(carryVisual.gameObject);
            carryVisual = null;
        }

        private void LateUpdate()
        {
            if (carrier == null || carryVisual == null || unlocking || PauseMenu.IsOpen) return;
            if (Mathf.Abs(carrier.Body.Velocity.x) > 0.01f && !carrier.Body.IsReturning)
                facing = carrier.Body.Velocity.x > 0f ? 1 : -1;
            float unit = carrier.Config.UnitSize;
            float clock = RewindManager.Current != null ? (float)RewindManager.Current.SimulationTime : Time.time;
            Vector3 target = carrier.Body.IsReturning ? (Vector3)carrier.PresentationPosition
                : RewindManager.Rewinding ? (Vector3)carrier.Body.Motor.Position
                : carrier.Body.Visual != null ? carrier.Body.Visual.position : carrier.Body.transform.position;
            target += new Vector3(-facing * FollowOffset.x, FollowOffset.y
                + Mathf.Sin(clock * Mathf.PI * 2f / Mathf.Max(0.1f, FloatPeriod)) * CarryFloatAmplitude, -0.06f) * unit;
            float distance = Vector3.Distance(carryVisual.position, target);
            float blend = RewindManager.Rewinding || distance > unit * 3f ? 1f
                : 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, FollowSmoothTime));
            carryVisual.position = Vector3.Lerp(carryVisual.position, target, blend);
        }

        private void EnsureGlowParticles()
        {
            if (glowParticles != null || GlowParticleMaterial == null) return;
            var owner = new GameObject("Key White Glow");
            owner.transform.SetParent(transform, false);
            owner.transform.localPosition = new Vector3(-0.01f, 0.09f, -0.04f);
            glowParticles = owner.AddComponent<ParticleSystem>();
            glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = glowParticles.main;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = Color.white;
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = glowParticles.emission;
            emission.rateOverTime = GlowParticleRate;
            var shape = glowParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.3f;
            shape.radiusThickness = 1f;
            var fade = glowParticles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var size = glowParticles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));
            var renderer = owner.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GlowParticleMaterial;
            renderer.sortingOrder = 13;
            if (!IsCollected) glowParticles.Play();
        }

        public void PlayPickupBurst()
        {
            PlayBurst(PickupFeedbackPrefab != null ? PickupFeedbackPrefab : PickupBurstPrefab,
                carryVisual != null ? carryVisual.position : transform.TransformPoint(Vector3.up * floatOffset));
        }

        public void PlayUnlockBurst(Vector3 position) => PlayBurst(PickupBurstPrefab, position);

        private void PlayBurst(ParticleSystem prefab, Vector3 position)
        {
            if (prefab == null) return;
            if (pickupBurst != null) Destroy(pickupBurst.gameObject);
            pickupBurst = Instantiate(prefab, position, prefab.transform.rotation);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(pickupBurst.gameObject, gameObject.scene);
            pickupBurst.transform.localScale = Vector3.Scale(prefab.transform.localScale, transform.lossyScale);
            pickupBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var particles in pickupBurst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.loop = false;
                main.useUnscaledTime = true;
            }
            var rootMain = pickupBurst.main;
            rootMain.stopAction = ParticleSystemStopAction.Destroy;
            pickupBurst.GetComponent<ParticleSystemRenderer>().sortingOrder = 14;
            pickupBurst.Play(true);
        }

        public void RebuildVisual()
        {
            if (mesh == null) mesh = new Mesh { name = "Progress key silhouette", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 center = new Vector2(-0.23f, 0.12f);
                Vector2 va = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 vb = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(vertices, triangles, center + va * 0.23f, center + va * 0.13f,
                    center + vb * 0.13f, center + vb * 0.23f);
            }
            Quad(vertices, triangles, new Vector2(-0.04f, 0.17f), new Vector2(-0.04f, 0.07f),
                new Vector2(0.43f, 0.07f), new Vector2(0.43f, 0.17f));
            for (int i = 0; i < 2; i++)
            {
                float x = 0.2f + i * 0.14f;
                Quad(vertices, triangles, new Vector2(x, 0.08f), new Vector2(x, -0.12f),
                    new Vector2(x + 0.09f, -0.12f), new Vector2(x + 0.09f, 0.08f));
            }
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Tint;
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.colors = colors;
            restVertices = vertices.ToArray();
            floatingVertices = new Vector3[restVertices.Length];
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            visual = GetComponent<MeshRenderer>();
            if (visual.sharedMaterial == null) visual.sharedMaterial = Resources.Load<Material>("Materials/IndicatorVisual");
            visual.sortingOrder = 12;
            trigger = GetComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = new Vector2(0.94f, 0.52f);
            trigger.offset = new Vector2(-0.01f, 0.09f);
        }

        private static void Quad(List<Vector3> vertices, List<int> triangles, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            int index = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 3);
        }

        private void OnValidate() => visualDirty = true;
        private void OnDisable()
        {
            DestroyCarryVisual();
            carrier = null;
            if (glowParticles != null) glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (pickupBurst != null) Destroy(pickupBurst.gameObject);
        }
        private void Update()
        {
            if (visualDirty)
            {
                visualDirty = false;
                RebuildVisual();
            }
            if (!Application.isPlaying || IsCollected || mesh == null || restVertices == null) return;
            floatPhase = Mathf.Repeat(floatPhase + Time.deltaTime * Mathf.PI * 2f / Mathf.Max(0.1f, FloatPeriod), Mathf.PI * 2f);
            floatOffset = Mathf.Sin(floatPhase) * Mathf.Max(0f, FloatAmplitude);
            Vector3 offset = Vector3.up * floatOffset;
            for (int i = 0; i < restVertices.Length; i++) floatingVertices[i] = restVertices[i] + offset;
            mesh.SetVertices(floatingVertices);
            mesh.RecalculateBounds();
            if (glowParticles != null)
                glowParticles.transform.localPosition = new Vector3(-0.01f, 0.09f + floatOffset, -0.04f);
        }
        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
