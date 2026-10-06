using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TapTap.Tests
{
    public sealed class CameraEffectsTests
    {
        private GameObject fixture;
        private DemoCamera rig;
        private Camera camera;
        private Transform shake;
        private LevelRegion region;
        private EffectManager effects;
        private readonly Vector3 shakeOrigin = new Vector3(0.2f, 0.1f, 0f);

        [SetUp]
        public void SetUp()
        {
            fixture = new GameObject("Camera effects fixture");
            GameObject effectsObject = new GameObject("Effects");
            effectsObject.transform.SetParent(fixture.transform, false);
            effects = effectsObject.AddComponent<EffectManager>();
            effects.enabled = false;
            GameObject regionObject = new GameObject("Region");
            regionObject.transform.SetParent(fixture.transform, false);
            regionObject.transform.position = new Vector3(1000f, 1000f, 0f);
            region = regionObject.AddComponent<LevelRegion>();
            GameObject rigObject = new GameObject("Rig");
            rigObject.transform.SetParent(fixture.transform, false);
            shake = new GameObject("Shake").transform;
            shake.SetParent(rigObject.transform, false);
            shake.localPosition = shakeOrigin;
            GameObject cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(shake, false);
            cameraObject.transform.localPosition = new Vector3(0.15f, 0.05f, 0f);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 16f / 9f;
            rig = rigObject.AddComponent<DemoCamera>();
            rig.enabled = false;
            rig.Configure(null, camera, shake);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(fixture);

        [Test]
        public void LockedRegionAppliesEffectsWithoutDriftAndRestoresFraming()
        {
            rig.FrameRegionHorizontally(region);
            float baseline = camera.orthographicSize;
            Vector3 rigPosition = rig.transform.position;
            Vector3 cameraPosition = camera.transform.position;
            effects.Play(PlayerEffect.Joined, null);
            effects.Play(PlayerEffect.HeadLanded, null);
            effects.Simulate(0.035f);

            for (int i = 0; i < 10; i++) TickCamera();

            Assert.That(shake.localPosition, Is.Not.EqualTo(shakeOrigin));
            Assert.That(camera.orthographicSize, Is.LessThan(baseline));
            Assert.That(camera.orthographicSize, Is.GreaterThanOrEqualTo(baseline * 0.985f - 0.0001f));
            Assert.That(Vector3.Distance(rig.transform.position, rigPosition), Is.LessThan(0.0002f));
            AssertRegionVisible();

            effects.Simulate(0.5f);
            TickCamera();
            Assert.That(shake.localPosition, Is.EqualTo(shakeOrigin));
            Assert.That(camera.orthographicSize, Is.EqualTo(baseline).Within(0.0001f));
            Assert.That(Vector3.Distance(camera.transform.position, cameraPosition), Is.LessThan(0.0002f));
        }

        [TestCase(16f / 9f)]
        [TestCase(0.75f)]
        [TestCase(2.5f)]
        public void RegionRetainsHorizontalFramingAfterAspectChange(float aspect)
        {
            rig.FrameRegionHorizontally(region);
            camera.aspect = aspect;
            TickCamera();
            float fitted = region.WorldBounds.extents.x / aspect;
            Assert.That(camera.orthographicSize, Is.EqualTo(fitted * 0.97f).Within(0.0001f));
            AssertHorizontalEdgesVisible();
        }

        [TestCase(0f)]
        [TestCase(0.03f)]
        public void StrongEffectsCannotCropInteriorOrExpandCamera(float inset)
        {
            SetField(rig, "fixedRegionInset", inset);
            SetField(rig, "fixedZoomLimit", 0.1f);
            SetField(effects, "headLandedShakeStrength", 10f);
            SetField(effects, "zoomAmount", 10f);
            rig.FrameRegionHorizontally(region);
            float baseline = camera.orthographicSize;
            effects.Play(PlayerEffect.HeadLanded, null);
            effects.Play(PlayerEffect.Joined, null);
            effects.Simulate(0.07f);
            TickCamera();
            Assert.That(camera.orthographicSize, Is.LessThanOrEqualTo(baseline));
            Assert.That((shake.localPosition - shakeOrigin).magnitude,
                Is.LessThanOrEqualTo(baseline * 2f * 0.004f + 0.0001f));
            AssertHorizontalEdgesVisible();
        }

        [Test]
        public void ShakeNeverChangesCameraSize()
        {
            SetField(rig, "fixedRegionInset", 0.001f);
            SetField(effects, "headLandedShakeStrength", 10f);
            rig.FrameRegionHorizontally(region);
            float baseline = camera.orthographicSize;
            effects.Play(PlayerEffect.HeadLanded, null);
            for (int i = 0; i < 20; i++)
            {
                effects.Simulate(0.01f);
                TickCamera();
                Assert.That(camera.orthographicSize, Is.EqualTo(baseline).Within(0.0001f));
                AssertHorizontalEdgesVisible();
            }
        }

        [Test]
        public void LimitedZoomPreservesEntireCurveAndRepeatedTriggersAreContinuous()
        {
            rig.FrameRegionHorizontally(region);
            float baseline = camera.orthographicSize;
            effects.Play(PlayerEffect.Joined, null);
            effects.Simulate(0.035f);
            TickCamera();
            float halfAttack = baseline - camera.orthographicSize;
            effects.Simulate(0.035f);
            TickCamera();
            float peak = baseline - camera.orthographicSize;
            Assert.That(halfAttack, Is.EqualTo(peak * 0.5f).Within(0.0001f));
            effects.Simulate(0.09f);
            TickCamera();
            Assert.That(baseline - camera.orthographicSize, Is.EqualTo(peak * 0.5f).Within(0.0001f));
            float beforeRetrigger = camera.orthographicSize;
            effects.Play(PlayerEffect.DownwardJoined, null);
            TickCamera();
            Assert.That(camera.orthographicSize, Is.EqualTo(beforeRetrigger).Within(0.0001f));
            effects.Simulate(0.5f);
            TickCamera();
            Assert.That(camera.orthographicSize, Is.EqualTo(baseline).Within(0.0001f));
        }

        [Test]
        public void RuntimeReferenceAspectCannotChangeFirstFrameSize()
        {
            rig.FrameRegionHorizontally(region, 0.75f);
            float initialSize = camera.orthographicSize;
            TickCamera();
            Assert.That(camera.orthographicSize, Is.EqualTo(initialSize).Within(0.0001f));
        }

        [Test]
        public void InsetCropsOuterWallAndCannotCropPastItsInnerEdge()
        {
            rig.FrameRegionHorizontally(region);
            Bounds bounds = region.WorldBounds;
            Assert.That(camera.WorldToViewportPoint(new Vector3(bounds.min.x, bounds.center.y, 0f)).x, Is.LessThan(0f));
            Assert.That(camera.WorldToViewportPoint(new Vector3(bounds.max.x, bounds.center.y, 0f)).x, Is.GreaterThan(1f));
            SetField(rig, "fixedRegionInset", 0.25f);
            TickCamera();
            Assert.That(camera.orthographicSize, Is.EqualTo((bounds.extents.x - region.UnitSize) / camera.aspect).Within(0.0001f));
            AssertHorizontalEdgesVisible();
        }

        [Test]
        public void ShakeUsesConfiguredDurationAxesAndZeroStrength()
        {
            SetField(effects, "shakeDuration", 0.5f);
            SetField(effects, "shakeAxisWeight", new Vector2(0f, 1f));
            SetField(effects, "shakeFrequency", new Vector2(2f, 3f));
            effects.Play(PlayerEffect.Landed, null);
            effects.Simulate(0.13f);
            Assert.That(effects.ShakeOffset.x, Is.EqualTo(0f));
            Assert.That(Mathf.Abs(effects.ShakeOffset.y), Is.GreaterThan(0.001f));
            effects.Simulate(0.4f);
            Assert.That(effects.ShakeOffset, Is.EqualTo(Vector2.zero));
            SetField(effects, "landingShakeStrength", 0f);
            effects.Play(PlayerEffect.Landed, null);
            Assert.That(effects.ShakeOffset, Is.EqualTo(Vector2.zero));
        }

        private void TickCamera() => rig.SendMessage("LateUpdate");

        private void AssertHorizontalEdgesVisible()
        {
            Bounds bounds = region.WorldBounds;
            bounds.Expand(new Vector3(-2f * region.UnitSize, 0f, 0f));
            float left = camera.WorldToViewportPoint(new Vector3(bounds.min.x, bounds.center.y, bounds.center.z)).x;
            float right = camera.WorldToViewportPoint(new Vector3(bounds.max.x, bounds.center.y, bounds.center.z)).x;
            Assert.That(left, Is.GreaterThanOrEqualTo(-0.0001f));
            Assert.That(right, Is.LessThanOrEqualTo(1.0001f));
        }

        private void AssertRegionVisible()
        {
            Bounds bounds = region.WorldBounds;
            bounds.Expand(new Vector3(-2f * region.UnitSize, -2f * region.UnitSize, 0f));
            foreach (Vector3 corner in new[]
            {
                bounds.min, bounds.max,
                new Vector3(bounds.min.x, bounds.max.y, bounds.center.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.center.z)
            })
            {
                Vector3 viewport = camera.WorldToViewportPoint(corner);
                Assert.That(viewport.x, Is.InRange(-0.0001f, 1.0001f));
                Assert.That(viewport.y, Is.InRange(-0.0001f, 1.0001f));
                Assert.That(viewport.z, Is.GreaterThan(0f));
            }
        }

        private static void SetField<T>(T target, string name, object value)
        {
            typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }
    }
}
