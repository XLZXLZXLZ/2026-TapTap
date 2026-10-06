using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace TapTap.Tests
{
    public sealed class PlayerVisualTests
    {
        private GameObject fixture;
        private PlayerConfig config;
        private PlayerVisualConfig visuals;
        private PlayerController controller;
        private PlayerView view;
        private MovableEntity body, head;

        [SetUp]
        public void SetUp()
        {
            fixture = new GameObject("Player visual fixture");
            config = ScriptableObject.CreateInstance<PlayerConfig>();
            visuals = ScriptableObject.CreateInstance<PlayerVisualConfig>();
            body = CreateEntity("Body", new Vector3(1000f, 1000f, 0f));
            head = CreateEntity("Head", new Vector3(1000f, 1001f, 0f));
            var input = fixture.AddComponent<PlayerInput>();
            input.enabled = false;
            var respawn = fixture.AddComponent<RespawnService>();
            controller = fixture.AddComponent<PlayerController>();
            controller.enabled = false;
            controller.Configure(input, config, body, head, respawn, null);
            view = fixture.AddComponent<PlayerView>();
            view.enabled = false;
            view.ConfigureVisuals(visuals);
            view.Configure(controller, body, head, config);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(config);
            Object.DestroyImmediate(visuals);
        }

        [Test]
        public void BreathingIsSubtleAndDoesNotMovePhysicsOrFeet()
        {
            Vector3 bodyPosition = body.transform.position;
            Vector3 headPosition = head.transform.position;
            float originalFoot = body.Visual.localPosition.y - body.Motor.Collider.size.y * 0.5f;
            view.Simulate(1f / (visuals.BreathingFrequency * 4f));
            Assert.That(body.Visual.localScale.y, Is.InRange(1.001f, 1.01f));
            Assert.That(head.Visual.localScale.y, Is.InRange(1.001f, 1.01f));
            Assert.That(body.Visual.localPosition.y - body.Visual.localScale.y * body.Motor.Collider.size.y * 0.5f,
                Is.EqualTo(originalFoot).Within(0.0001f));
            Assert.That(body.transform.position, Is.EqualTo(bodyPosition));
            Assert.That(head.transform.position, Is.EqualTo(headPosition));
            visuals.BreathingAmplitude = 0f;
            view.Simulate(0.01f);
            Assert.That(body.Visual.localScale, Is.EqualTo(Vector3.one));
            Assert.That(head.Visual.localScale, Is.EqualTo(Vector3.one));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void MagneticMovementLagsAndLeansThenSettlesWithoutMovingColliders(float direction)
        {
            SetPhase(PlayerPhase.Holding);
            visuals.BreathingAmplitude = 0f;
            Vector3 bodyPosition = body.transform.position;
            Vector3 headPosition = head.transform.position;
            body.Velocity = Vector2.right * direction * config.MoveSpeed;
            view.Simulate(0.02f);
            float earlyLag = Mathf.Abs(head.Visual.localPosition.x);
            Assert.That(head.Visual.localPosition.x * direction, Is.LessThan(0f));
            Assert.That(earlyLag, Is.LessThan(visuals.MagneticLagDistance));
            Assert.That(Mathf.DeltaAngle(0f, head.Visual.localEulerAngles.z) * direction, Is.LessThan(0f));
            view.Simulate(0.3f);
            Assert.That(Mathf.Abs(head.Visual.localPosition.x), Is.GreaterThan(earlyLag));
            Assert.That(Mathf.Abs(head.Visual.localPosition.x), Is.LessThanOrEqualTo(visuals.MagneticLagDistance));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, head.Visual.localEulerAngles.z)), Is.InRange(3f, 4f));
            Assert.That(body.transform.position, Is.EqualTo(bodyPosition));
            Assert.That(head.transform.position, Is.EqualTo(headPosition));
            body.Velocity = Vector2.zero;
            view.Simulate(0.5f);
            Assert.That(Mathf.Abs(head.Visual.localPosition.x), Is.LessThan(0.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(0f, head.Visual.localEulerAngles.z)), Is.LessThan(0.1f));
        }

        [Test]
        public void DetachedHeadAndReturningPartsAreNotPulledByVisualMotion()
        {
            SetPhase(PlayerPhase.Detached);
            body.Velocity = Vector2.right * config.MoveSpeed;
            view.Simulate(0.2f);
            Assert.That(head.Visual.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(head.Visual.localRotation, Is.EqualTo(Quaternion.identity));
            body.IsReturning = true;
            body.Visual.localScale = new Vector3(0.2f, 0.3f, 1f);
            view.Simulate(0.2f);
            Assert.That(body.Visual.localScale, Is.EqualTo(new Vector3(0.2f, 0.3f, 1f)));
        }

        [Test]
        public void MagneticLineEndpointsFollowVisualLagAndLean()
        {
            SetPhase(PlayerPhase.Holding);
            GameObject lineObject = new GameObject("Magnetic line");
            lineObject.transform.SetParent(fixture.transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            MagneticConnectionView magnet = lineObject.AddComponent<MagneticConnectionView>();
            magnet.Configure(line, new SpriteRenderer[0], 0.075f, 2.1f, 18);
            view.ConfigureEffects(magnet, null, null);
            body.Velocity = Vector2.right * config.MoveSpeed;
            view.Simulate(0.15f);
            Vector3 from = body.Visual.position + body.Visual.up * body.Motor.Size.y * 0.42f;
            Vector3 to = head.Visual.position - head.Visual.up * head.Motor.Size.y * 0.42f;
            Assert.That(Vector3.Distance(line.GetPosition(0), from), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(line.GetPosition(line.positionCount - 1), to), Is.LessThan(0.0001f));
        }

        private MovableEntity CreateEntity(string name, Vector3 position)
        {
            GameObject entityObject = new GameObject(name);
            entityObject.transform.SetParent(fixture.transform, false);
            entityObject.transform.position = position;
            MovableEntity entity = entityObject.AddComponent<MovableEntity>();
            entity.enabled = false;
            entityObject.GetComponent<BoxCollider2D>().size = new Vector2(0.7f, 0.8f);
            entity.Visual = new GameObject("Visual").transform;
            entity.Visual.SetParent(entityObject.transform, false);
            return entity;
        }

        private void SetPhase(PlayerPhase phase) => typeof(PlayerController)
            .GetField("phase", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, phase);
    }
}
