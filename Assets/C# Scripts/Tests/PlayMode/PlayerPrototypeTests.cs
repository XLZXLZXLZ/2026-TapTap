using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace TapTap.Tests
{
    public sealed class PlayerPrototypeTests
    {
        private const float StepDuration = 0.02f;
        private readonly Vector2 origin = new Vector2(1000f, 1000f);

        private GameObject fixture;
        private PlayerConfig config;
        private PlayerInput input;
        private PlayerController player;
        private MovableEntity body;
        private MovableEntity head;
        private RespawnService respawn;
        private SimulationMode2D previousSimulationMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;

            fixture = new GameObject("Player prototype test fixture");
            config = ScriptableObject.CreateInstance<PlayerConfig>();
            config.ExtensionDuration = 0.4f;
            config.RetractionDuration = 0.25f;
            config.RespawnDuration = 0.1f;
            config.RecallCooldown = 0.05f;

            input = fixture.AddComponent<PlayerInput>();
            input.SetExternalInput(0f, false);
            input.enabled = false;
            body = CreateEntity("Body", EntityPart.Body, new Vector2(0f, 0.4f));
            head = CreateEntity("Head", EntityPart.Head, new Vector2(0f, 0.4f + config.JoinedOffset));
            respawn = fixture.AddComponent<RespawnService>();
            respawn.Configure(config);
            respawn.SetCheckpoint(ToWorld(new Vector2(0f, 0.4f)));

            player = fixture.AddComponent<PlayerController>();
            player.enabled = false;
            player.Configure(input, config, body, head, respawn, null);
            CreateSurface("Floor", new Vector2(0f, -0.5f), new Vector2(40f, 1f), SurfaceKind.Solid);

            Physics2D.SyncTransforms();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(fixture);
            Object.Destroy(config);
            yield return null;
            Physics2D.simulationMode = previousSimulationMode;
        }

        [Test]
        public void EarlySpaceReleaseWaitsForHoldingBeforeRetraction()
        {
            input.SetExternalInput(0f, true);
            Step();
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Extending));

            input.SetExternalInput(0f, false);
            Step();
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Extending));

            float previousHeight = head.Motor.Position.y;
            bool reachedHolding = false;
            for (int i = 0; i < 40; i++)
            {
                Step();
                Assert.That(head.Motor.Position.y, Is.GreaterThanOrEqualTo(previousHeight - 0.002f));
                previousHeight = head.Motor.Position.y;

                if (player.Phase == PlayerPhase.Holding)
                {
                    reachedHolding = true;
                    break;
                }

                Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Extending));
            }

            Assert.That(reachedHolding, Is.True, "A queued release must visibly pass through Holding.");
            float holdingHeight = head.Motor.Position.y;
            Step();
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Retracting));
            Step();
            Assert.That(head.Motor.Position.y, Is.LessThan(holdingHeight));
        }

        [Test]
        public void CeilingStopsExtensionAndLeavingCeilingDoesNotResumeIt()
        {
            CreateSurface("Ceiling", new Vector2(0f, 3f), new Vector2(2f, 0.5f), SurfaceKind.Solid);
            Physics2D.SyncTransforms();

            input.SetExternalInput(0f, true);
            StepUntil(PlayerPhase.Holding, 40);
            float stoppedOffset = head.Motor.Position.y - body.Motor.Position.y;
            Assert.That(stoppedOffset, Is.LessThan(config.JoinedOffset + config.MaxExtension - 0.2f));

            input.SetExternalInput(1f, true);
            Step(60);

            Assert.That(body.Motor.Position.x, Is.GreaterThan(origin.x + 2f));
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Holding));
            Assert.That(head.Motor.Position.y - body.Motor.Position.y, Is.EqualTo(stoppedOffset).Within(0.003f));
        }

        [TestCase(false, 0f)]
        [TestCase(true, 0f)]
        [TestCase(false, 1000f)]
        [TestCase(true, 1000f)]
        public void UnobstructedRetractionReturnsToJoinedAndRemainsStable(bool releaseEarly, float coordinate)
        {
            Vector2 testOrigin = Vector2.one * coordinate;
            fixture.transform.position = testOrigin - origin;
            config.ExtensionDuration = 0.45f;
            config.RetractionDuration = 0.35f;
            player.TeleportJoined(testOrigin + Vector2.up * 0.4f);
            respawn.SetCheckpoint(body.Motor.Position);
            Physics2D.SyncTransforms();

            input.SetExternalInput(0f, true);
            if (releaseEarly) Step();
            else StepUntil(PlayerPhase.Holding, 40);
            input.SetExternalInput(0f, false);
            StepUntil(PlayerPhase.Joined, 80);

            Vector2 joinedOffset = head.Motor.Position - body.Motor.Position;
            Assert.That(joinedOffset.x, Is.EqualTo(0f).Within(0.0002f));
            Assert.That(joinedOffset.y, Is.EqualTo(config.ToWorld(config.JoinedOffset)).Within(0.0002f));
            for (int i = 0; i < 30; i++)
            {
                Step();
                Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Joined));
                Assert.That(Vector2.Distance(head.Motor.Position - body.Motor.Position, joinedOffset), Is.LessThan(0.0002f));
            }

            input.SetExternalInput(0f, true);
            Step();
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Extending));
        }

        [Test]
        public void HeadObstacleStopsBothEntitiesWithoutChangingTheirOffset()
        {
            input.SetExternalInput(0f, true);
            StepUntil(PlayerPhase.Holding, 40);
            float headHeight = head.Motor.Position.y - origin.y;
            Vector2 initialOffset = head.Motor.Position - body.Motor.Position;
            CreateSurface("Head-only wall", new Vector2(2f, headHeight), new Vector2(0.5f, 1f), SurfaceKind.Solid);
            Physics2D.SyncTransforms();

            input.SetExternalInput(1f, true);
            Step(80);

            Assert.That(body.Motor.Position.x, Is.GreaterThan(origin.x + 1f));
            Assert.That(body.Motor.Position.x, Is.LessThan(origin.x + 1.42f));
            Assert.That(Vector2.Distance(head.Motor.Position - body.Motor.Position, initialOffset), Is.LessThan(0.002f));
        }

        [Test]
        public void OneWayPlatformAllowsAscentAndStopsDescentAtItsTop()
        {
            MovableEntity probe = CreateEntity("One-way probe", EntityPart.Head, new Vector2(8f, 0.8f));
            CreateSurface("One-way", new Vector2(8f, 2f), new Vector2(4f, 0.25f), SurfaceKind.OneWay);
            Physics2D.SyncTransforms();

            for (int i = 0; i < 20; i++)
            {
                probe.Motor.BeginStep();
                MoveResult upward = probe.Motor.Move(Vector2.up * 0.2f);
                probe.Motor.Commit();
                Physics2D.Simulate(StepDuration);
                Assert.That(upward.Blocked, Is.False);
            }

            Assert.That(probe.Motor.Position.y, Is.EqualTo(origin.y + 4.8f).Within(0.004f));

            bool blocked = false;
            for (int i = 0; i < 20 && !blocked; i++)
            {
                probe.Motor.BeginStep();
                MoveResult downward = probe.Motor.Move(Vector2.down * 0.2f);
                probe.Motor.Commit();
                Physics2D.Simulate(StepDuration);
                blocked = downward.Blocked;
            }

            Assert.That(blocked, Is.True);
            Assert.That(probe.Motor.Position.y, Is.EqualTo(origin.y + 2.525f + config.Skin).Within(0.02f));
        }

        [Test]
        public void StationaryEntityRetainsItsGroundSupport()
        {
            body.Motor.BeginStep();
            Assert.That(body.Motor.Grounded, Is.True);

            MoveResult result = body.Motor.Move(Vector2.zero, head);

            Assert.That(result.Blocked, Is.False);
            Assert.That(body.Motor.Grounded, Is.True);
            Assert.That(body.Motor.SupportSurface, Is.Not.Null);
        }

        [Test]
        public void ReachingWallDoesNotBlockTangentialMovement()
        {
            MovableEntity probe = CreateEntity("Sliding probe", EntityPart.Other, new Vector2(8f, 0.4f));
            CreateSurface("Slide wall", new Vector2(9.3f, 1.5f), new Vector2(0.5f, 4f), SurfaceKind.Solid);
            Physics2D.SyncTransforms();

            probe.Motor.BeginStep();
            MoveResult horizontal = probe.Motor.Move(Vector2.right);
            MoveResult vertical = probe.Motor.Move(Vector2.up);
            probe.Motor.Commit();
            Physics2D.Simulate(StepDuration);

            Assert.That(horizontal.Blocked, Is.True);
            Assert.That(vertical.Blocked, Is.False);
            Assert.That(probe.Motor.Position.x, Is.EqualTo(origin.x + 8.7f).Within(0.02f));
            Assert.That(probe.Motor.Position.y, Is.EqualTo(origin.y + 1.4f).Within(0.003f));
        }

        [Test]
        public void SolidPlatformBlocksPullAndLeavesHeadAsAnIndependentEntity()
        {
            ReachDetached();

            Assert.That(head.Motor.Position.y, Is.GreaterThan(body.Motor.Position.y + config.JoinedOffset));
            float headX = head.Motor.Position.x;
            float bodyX = body.Motor.Position.x;
            input.SetExternalInput(1f, false);
            Step(10);
            Assert.That(body.Motor.Position.x, Is.GreaterThan(bodyX + 0.1f));
            Assert.That(head.Motor.Position.x, Is.EqualTo(headX).Within(0.002f));
        }

        [Test]
        public void NormalPullThroughOneWayPlatformLaunchesOneGridHeight()
        {
            CreateSurface("One-way ledge", new Vector2(4f, 3f), new Vector2(2f, 0.5f), SurfaceKind.OneWay);
            ExtendAndMoveOverLedge();
            input.SetExternalInput(0f, false);
            StepUntil(PlayerPhase.Joined, 100);

            Assert.That(body.Velocity.y, Is.EqualTo(config.LaunchSpeed(config.NormalLaunchHeight)).Within(0.01f));
            Assert.That(head.Motor.Position.y - body.Motor.Position.y, Is.EqualTo(config.JoinedOffset).Within(0.003f));
            Assert.That(body.Motor.Position.y, Is.GreaterThan(origin.y + 2f));
        }

        [Test]
        public void DistantRecallAlignsGraduallyAndLaunchesTwoGridHeights()
        {
            ReachDetached();
            fixture.transform.Find("Solid ledge").GetComponent<BoxCollider2D>().enabled = false;
            fixture.transform.Find("Floor").GetComponent<BoxCollider2D>().enabled = false;
            CreateSurface("Recall support", new Vector2(4f, 3f), new Vector2(2f, 0.5f), SurfaceKind.OneWay);

            float headX = head.Motor.Position.x;
            body.Motor.Teleport(new Vector2(headX - 0.2f, head.Motor.Position.y - 6.5f));
            body.Velocity = Vector2.zero;
            Physics2D.SyncTransforms();
            input.SetExternalInput(0f, true);
            Step(4);
            float startingX = body.Motor.Position.x;
            input.SetExternalInput(0f, false);
            Step();

            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Pulling));
            Assert.That(body.Motor.Position.x, Is.GreaterThan(startingX));
            Assert.That(body.Motor.Position.x, Is.LessThan(headX - 0.01f), "Alignment must not teleport horizontally.");

            StepUntil(PlayerPhase.Joined, 140);
            Assert.That(body.Motor.Position.x, Is.EqualTo(headX).Within(0.003f));
            Assert.That(body.Velocity.y, Is.EqualTo(config.LaunchSpeed(config.DistantLaunchHeight)).Within(0.01f));
            Assert.That(config.DistantLaunchHeight, Is.EqualTo(2f));
        }

        [Test]
        public void DetachedHeadDeathReturnsOnlyHead()
        {
            ReachDetached();
            Step(30);
            float bodyX = body.Motor.Position.x;
            BoxCollider2D lethal = CreateLethalZone(head.Motor.Position);
            Step();

            Assert.That(head.IsReturning, Is.True);
            Assert.That(body.IsReturning, Is.False);
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Detached));
            lethal.enabled = false;
            Step(10);

            Assert.That(head.IsReturning, Is.False);
            Assert.That(head.Motor.Position.x, Is.EqualTo(origin.x).Within(0.003f));
            Assert.That(body.Motor.Position.x, Is.EqualTo(bodyX).Within(0.003f));
        }

        [Test]
        public void ConnectedHeadDeathReturnsBothEntitiesJoined()
        {
            input.SetExternalInput(0f, true);
            StepUntil(PlayerPhase.Holding, 40);
            BoxCollider2D lethal = CreateLethalZone(head.Motor.Position);
            Step();

            Assert.That(body.IsReturning, Is.True);
            Assert.That(head.IsReturning, Is.True);
            lethal.enabled = false;
            Step(10);

            Assert.That(body.IsReturning, Is.False);
            Assert.That(head.IsReturning, Is.False);
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Joined));
            Assert.That(body.Motor.Position.x, Is.EqualTo(origin.x).Within(0.003f));
            Assert.That(head.Motor.Position.y - body.Motor.Position.y, Is.EqualTo(config.JoinedOffset).Within(0.003f));
        }

        [Test]
        public void BodyReturningOntoHeadTriggersSpringBounce()
        {
            ReachDetached();
            Step(30);
            head.Motor.Teleport(ToWorld(new Vector2(0f, 0.4f)));
            head.Velocity = Vector2.zero;
            BoxCollider2D lethal = CreateLethalZone(body.Motor.Position);
            Step();

            Assert.That(body.IsReturning, Is.True);
            Assert.That(head.IsReturning, Is.False);
            lethal.enabled = false;
            float greatestUpwardSpeed = 0f;
            for (int i = 0; i < 20; i++)
            {
                Step();
                greatestUpwardSpeed = Mathf.Max(greatestUpwardSpeed, body.Velocity.y);
            }

            Assert.That(body.IsReturning, Is.False);
            Assert.That(body.Motor.Position.x, Is.EqualTo(origin.x).Within(0.003f));
            Assert.That(greatestUpwardSpeed, Is.EqualTo(config.LaunchSpeed(config.SpringHeight)).Within(0.01f));
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Detached));
        }

        [Test]
        public void OnlyBodyCanUpdateCheckpoint()
        {
            ReachDetached();
            Step(30);
            GameObject flagObject = new GameObject("Test checkpoint");
            flagObject.transform.SetParent(fixture.transform);
            flagObject.transform.position = head.Motor.Position;
            BoxCollider2D collider = flagObject.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one * 0.2f;
            collider.isTrigger = true;
            CheckpointFlag flag = flagObject.AddComponent<CheckpointFlag>();
            Physics2D.SyncTransforms();
            Vector2 previousCheckpoint = respawn.Checkpoint;
            Step();

            Assert.That(Vector2.Distance(respawn.Checkpoint, previousCheckpoint), Is.LessThan(0.002f));

            flagObject.transform.position = body.Motor.Position;
            Physics2D.SyncTransforms();
            Step();
            Assert.That(Vector2.Distance(respawn.Checkpoint, flag.SpawnPosition), Is.LessThan(0.002f));
        }

        [Test]
        public void JoinedPairLandingOnLooseHeadBouncesTogether()
        {
            MovableEntity spring = CreateEntity("Loose spring head", EntityPart.Head, new Vector2(8f, 0.4f));
            spring.SpringEnabled = true;
            spring.SpringHeight = config.ToWorld(config.SpringHeight);
            player.TeleportJoined(ToWorld(new Vector2(8f, 3f)));
            Physics2D.SyncTransforms();

            int bounceEffects = 0;
            player.Effect += effect =>
            {
                if (effect == PlayerEffect.Bounce)
                    bounceEffects++;
            };

            for (int i = 0; i < 80 && bounceEffects == 0; i++)
                Step();

            Assert.That(bounceEffects, Is.EqualTo(1));
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Joined));
            Assert.That(body.Velocity.y, Is.EqualTo(config.LaunchSpeed(config.SpringHeight)).Within(0.01f));
            Assert.That(head.Velocity.y, Is.EqualTo(body.Velocity.y).Within(0.002f));
            Assert.That(head.Motor.Position.y - body.Motor.Position.y, Is.EqualTo(config.JoinedOffset).Within(0.003f));
        }

        [Test]
        public void FailedRecallSpamBuffersAtMostOneAttemptDuringCooldown()
        {
            ReachDetached();
            Step(30);
            config.RecallCooldown = 0.3f;
            body.Motor.Teleport(new Vector2(head.Motor.Position.x + 2f, origin.y + 0.4f));
            body.Velocity = Vector2.zero;
            Physics2D.SyncTransforms();

            int failures = 0;
            player.Effect += effect =>
            {
                if (effect == PlayerEffect.RecallFailed)
                    failures++;
            };

            input.SetExternalInput(0f, true);
            Step();
            input.SetExternalInput(0f, false);
            Step();
            Assert.That(failures, Is.EqualTo(1));

            for (int i = 0; i < 12; i++)
            {
                input.SetExternalInput(0f, true);
                input.SetExternalInput(0f, false);
            }

            Step(5);
            Assert.That(failures, Is.EqualTo(1));
            Step(30);
            Assert.That(failures, Is.EqualTo(2));
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Detached));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void JoinedPairBlocksThinObstacleTouchingOnlyItsMiddle(float direction)
        {
            CreateSurface("Thin middle obstacle", new Vector2(direction * 2f, 0.85f),
                new Vector2(0.5f, 0.04f), SurfaceKind.Solid);
            Physics2D.SyncTransforms();
            Vector2 initialOffset = head.Motor.Position - body.Motor.Position;

            input.SetExternalInput(direction, false);
            Step(40);

            float progress = direction * (body.Motor.Position.x - origin.x);
            Assert.That(progress, Is.GreaterThan(1f));
            Assert.That(progress, Is.LessThan(1.42f), "Joined collision must include the space between head and body.");
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Joined));
            Assert.That(Vector2.Distance(head.Motor.Position - body.Motor.Position, initialOffset), Is.LessThan(0.003f));
        }

        [TestCase(PlayerPhase.Holding)]
        [TestCase(PlayerPhase.Extending)]
        public void ExtendedMagneticConnectionAllowsWallInsideItsGap(PlayerPhase expectedPhase)
        {
            if (expectedPhase == PlayerPhase.Extending)
                config.ExtensionDuration = 2f;

            input.SetExternalInput(0f, true);
            if (expectedPhase == PlayerPhase.Holding)
                StepUntil(PlayerPhase.Holding, 40);
            else
                Step(10);

            Assert.That(player.Phase, Is.EqualTo(expectedPhase));
            CreateSurface("Magnetic gap obstacle", new Vector2(2f, 1.2f),
                new Vector2(0.5f, 0.04f), SurfaceKind.Solid);
            Physics2D.SyncTransforms();

            input.SetExternalInput(1f, true);
            Step(40);

            Assert.That(body.Motor.Position.x, Is.GreaterThan(origin.x + 3.5f));
            Assert.That(head.Motor.Position.x, Is.EqualTo(body.Motor.Position.x).Within(0.003f));
            Assert.That(player.Phase, Is.EqualTo(expectedPhase));
        }

        [TestCase(1f)]
        [TestCase(-1f)]
        public void OtherMovingEntityCannotSweepThroughJoinedMiddle(float direction)
        {
            MovableEntity probe = CreateEntity("Thin moving probe", EntityPart.Other,
                new Vector2(-direction * 2f, 0.85f));
            probe.Motor.Collider.size = new Vector2(0.2f, 0.04f);
            Physics2D.SyncTransforms();

            probe.Motor.BeginStep(StepDuration);
            SweepResult result = probe.Motor.Sweep(Vector2.right * (direction * 4f));

            Assert.That(result.Blocked, Is.True, "Other entities must also see Joined as a complete outline.");
            Assert.That(result.Fraction, Is.InRange(0.3f, 0.45f));
            Assert.That(result.Normal.x * direction, Is.LessThan(-0.5f));
        }

        [TestCase(0.25f, 0.012f)]
        [TestCase(-0.25f, 0.012f)]
        [TestCase(1f, 0.012f)]
        [TestCase(-1f, 0.012f)]
        [TestCase(1.25f, 0.031f)]
        [TestCase(-1.25f, 0.031f)]
        public void LooseHeadConveyorTransportContinuesUntilCompletelyPastEdge(float conveyorSpeed, float initialOverlap)
        {
            const float conveyorX = 8f;
            const float conveyorHalfWidth = 1f;
            const float headHalfWidth = 0.35f;
            const float standingHeight = 3.65f;
            float direction = Mathf.Sign(conveyorSpeed);
            CreateSurface("Edge conveyor", new Vector2(conveyorX, 3f), new Vector2(2f, 0.5f), SurfaceKind.Solid);
            WorldSurface conveyor = fixture.transform.Find("Edge conveyor").GetComponent<WorldSurface>();
            conveyor.Configure(SurfaceKind.Solid, conveyorSpeed);
            MovableEntity looseHead = CreateEntity("Conveyor head", EntityPart.Head,
                new Vector2(conveyorX + direction * (conveyorHalfWidth + headHalfWidth - initialOverlap), standingHeight));
            Physics2D.SyncTransforms();

            for (int i = 0; i < 100; i++)
            {
                looseHead.Motor.BeginStep(StepDuration);
                looseHead.SimulateFree(StepDuration, 0f);
                looseHead.Motor.Commit();
                Physics2D.Simulate(StepDuration);
            }

            float distancePastCenter = direction * (looseHead.Motor.Position.x - origin.x - conveyorX);
            Assert.That(distancePastCenter, Is.GreaterThan(conveyorHalfWidth + headHalfWidth + 0.001f),
                "Transport must continue until the full collision width has left the conveyor.");
            Assert.That(looseHead.Motor.Position.y, Is.LessThan(origin.y + standingHeight - 0.5f));
            Assert.That(looseHead.Motor.SupportSurface, Is.Not.SameAs(conveyor));
        }

        [TestCase(SurfaceKind.Solid, false, 1f)]
        [TestCase(SurfaceKind.Solid, true, 1f)]
        [TestCase(SurfaceKind.OneWay, false, 1f)]
        [TestCase(SurfaceKind.OneWay, true, 1f)]
        [TestCase(SurfaceKind.Solid, false, -1f)]
        [TestCase(SurfaceKind.Solid, true, -1f)]
        [TestCase(SurfaceKind.OneWay, false, -1f)]
        [TestCase(SurfaceKind.OneWay, true, -1f)]
        public void ConveyorCarriesHeadFullyOntoAdjacentPlatformRegardlessOfCreationOrder(
            SurfaceKind neighborKind, bool neighborFirst, float direction)
        {
            const float conveyorX = 8f;
            const float halfWidth = 2f;
            const float headHalfWidth = 0.35f;
            const float standingHeight = 3.8f;
            Vector2 neighborPosition = new Vector2(conveyorX + direction * 4f, 3f);
            Vector2 conveyorPosition = new Vector2(conveyorX, 3f);
            Vector2 size = new Vector2(4f, 0.8f);
            if (neighborFirst)
            {
                CreateSurface("Conveyor neighbor", neighborPosition, size, neighborKind);
                CreateSurface("Joined edge conveyor", conveyorPosition, size, SurfaceKind.Solid);
            }
            else
            {
                CreateSurface("Joined edge conveyor", conveyorPosition, size, SurfaceKind.Solid);
                CreateSurface("Conveyor neighbor", neighborPosition, size, neighborKind);
            }

            WorldSurface conveyor = fixture.transform.Find("Joined edge conveyor").GetComponent<WorldSurface>();
            WorldSurface neighbor = fixture.transform.Find("Conveyor neighbor").GetComponent<WorldSurface>();
            conveyor.Configure(SurfaceKind.Solid, direction * 1.25f);
            MovableEntity looseHead = CreateEntity("Neighbor-bound head", EntityPart.Head,
                new Vector2(conveyorX + direction * (halfWidth - 0.057f), standingHeight));
            Physics2D.SyncTransforms();

            for (int i = 0; i < 80; i++)
            {
                looseHead.Motor.BeginStep(StepDuration);
                looseHead.SimulateFree(StepDuration, 0f);
                looseHead.Motor.Commit();
                Physics2D.Simulate(StepDuration);
            }

            float distancePastCenter = direction * (looseHead.Motor.Position.x - origin.x - conveyorX);
            Assert.That(distancePastCenter, Is.InRange(halfWidth + headHalfWidth - 0.002f, halfWidth + headHalfWidth + 0.04f),
                "The adjacent zero-speed platform must not take over while the head still overlaps the belt.");
            Assert.That(looseHead.Motor.Position.y, Is.EqualTo(origin.y + standingHeight).Within(0.015f));
            Assert.That(looseHead.Motor.Grounded, Is.True);
            Assert.That(looseHead.Motor.SupportSurface, Is.SameAs(neighbor));
            float stoppedX = looseHead.Motor.Position.x;
            for (int i = 0; i < 10; i++)
            {
                looseHead.Motor.BeginStep(StepDuration);
                looseHead.SimulateFree(StepDuration, 0f);
                looseHead.Motor.Commit();
                Physics2D.Simulate(StepDuration);
            }
            Assert.That(looseHead.Motor.Position.x, Is.EqualTo(stoppedX).Within(0.002f));
        }

        [Test]
        public void PullCannotJoinWithThinSolidObstacleInsideMiddleGap()
        {
            CreateSurface("One-way ledge", new Vector2(4f, 3f), new Vector2(2f, 0.5f), SurfaceKind.OneWay);
            CreateSurface("Join gap obstacle", new Vector2(4f, 3.2f), new Vector2(0.5f, 0.04f), SurfaceKind.Solid);
            ExtendAndMoveOverLedge();
            input.SetExternalInput(0f, false);
            StepUntil(PlayerPhase.Detached, 100);

            Assert.That(body.Motor.Position.y, Is.GreaterThan(origin.y + 2.5f));
            Assert.That(body.Motor.Position.y + body.Motor.Size.y * 0.5f, Is.LessThan(origin.y + 3.18f));
            Assert.That(head.Motor.Position.y - head.Motor.Size.y * 0.5f, Is.GreaterThan(origin.y + 3.22f));
            Assert.That(body.IsReturning || head.IsReturning, Is.False);
        }

        [Test]
        public void JoinedPairStraddlingOneWayPlatformFallsThroughWithoutHeadSupport()
        {
            fixture.transform.Find("Floor").GetComponent<BoxCollider2D>().enabled = false;
            CreateSurface("Middle one-way conveyor", new Vector2(0f, 0.8f),
                new Vector2(4f, 0.2f), SurfaceKind.OneWay);
            WorldSurface platform = fixture.transform.Find("Middle one-way conveyor").GetComponent<WorldSurface>();
            platform.Configure(SurfaceKind.OneWay, 2f);
            Physics2D.SyncTransforms();
            Vector2 initialOffset = head.Motor.Position - body.Motor.Position;
            float initialX = body.Motor.Position.x;

            for (int i = 0; i < 20; i++)
            {
                Step();
                Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Joined));
                Assert.That(Vector2.Distance(head.Motor.Position - body.Motor.Position, initialOffset), Is.LessThan(0.003f));
                Assert.That(body.Motor.Position.x, Is.EqualTo(initialX).Within(0.003f),
                    "A platform passing through the joined middle cannot drive the pair as a conveyor.");
            }

            Assert.That(head.Motor.Position.y + head.Motor.Size.y * 0.5f, Is.LessThan(origin.y + 0.65f),
                "The head cannot hang the joined pair on a one-way platform already above the body bottom.");
            Assert.That(body.Velocity.y, Is.LessThan(0f));
            Assert.That(head.Velocity.y, Is.EqualTo(body.Velocity.y).Within(0.002f));
        }

        [Test]
        public void JoinedMiddleContactWithLethalZoneReturnsBothParts()
        {
            BoxCollider2D lethal = CreateLethalZone(ToWorld(new Vector2(0f, 0.85f)));
            lethal.size = new Vector2(0.2f, 0.04f);
            Physics2D.SyncTransforms();

            Step();

            Assert.That(body.IsReturning, Is.True);
            Assert.That(head.IsReturning, Is.True);
        }

        [Test]
        public void JoinedPairSlidingAlongWallDoesNotTreatWallAsGround()
        {
            fixture.transform.Find("Floor").GetComponent<BoxCollider2D>().enabled = false;
            CreateSurface("Wall beside falling pair", new Vector2(1.3f, 1.5f),
                new Vector2(0.5f, 8f), SurfaceKind.Solid);
            player.TeleportJoined(ToWorld(new Vector2(0.7f, 3f)));
            Physics2D.SyncTransforms();

            for (int i = 0; i < 20; i++)
            {
                Step();
                Assert.That(body.Motor.Grounded, Is.False);
            }

            Assert.That(body.Velocity.y, Is.EqualTo(-config.WorldGravity * StepDuration * 20).Within(0.01f));
        }

        [Test]
        public void JoinedHeadContactDoesNotUpdateBodyCheckpoint()
        {
            GameObject flagObject = new GameObject("Head-only checkpoint");
            flagObject.transform.SetParent(fixture.transform);
            flagObject.transform.position = head.Motor.Position;
            BoxCollider2D collider = flagObject.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one * 0.2f;
            collider.isTrigger = true;
            flagObject.AddComponent<CheckpointFlag>();
            Physics2D.SyncTransforms();
            Vector2 previousCheckpoint = respawn.Checkpoint;

            Step();

            Assert.That(Vector2.Distance(respawn.Checkpoint, previousCheckpoint), Is.LessThan(0.002f));
        }

        [Test]
        public void OrdinaryHeadRetractionEmitsOneReturnEffect()
        {
            int returns = 0;
            int assemblyPulses = 0;
            player.Effect += effect =>
            {
                if (effect == PlayerEffect.HeadReturned) returns++;
                if (effect == PlayerEffect.Joined || effect == PlayerEffect.DownwardJoined) assemblyPulses++;
            };
            input.SetExternalInput(0f, true);
            StepUntil(PlayerPhase.Holding, 40);
            input.SetExternalInput(0f, false);
            StepUntil(PlayerPhase.Joined, 60);
            Step(30);

            Assert.That(returns, Is.EqualTo(1));
            Assert.That(assemblyPulses, Is.EqualTo(0));
        }

        [TestCase(8f, 1)]
        [TestCase(2f, 0)]
        public void JoinedLandingEffectUsesImpactSpeedAndDoesNotRepeat(float fallSpeed, int expectedEffects)
        {
            player.TeleportJoined(ToWorld(new Vector2(0f, 0.45f)));
            body.Velocity = head.Velocity = Vector2.down * fallSpeed;
            Physics2D.SyncTransforms();
            int landings = 0;
            player.Effect += effect => { if (effect == PlayerEffect.Landed) landings++; };

            Step(40);

            Assert.That(body.Motor.Grounded, Is.True);
            Assert.That(landings, Is.EqualTo(expectedEffects));
        }

        [TestCase(EntityPart.Body)]
        [TestCase(EntityPart.Head)]
        public void DetachedPartLandingEmitsOneImpactEffect(EntityPart fallingPart)
        {
            ReachDetached();
            Step(30);
            MovableEntity falling = fallingPart == EntityPart.Body ? body : head;
            falling.Motor.Teleport(ToWorld(new Vector2(8f, 1.5f)));
            falling.Velocity = Vector2.down * 8f;
            Physics2D.SyncTransforms();
            int landings = 0;
            player.Effect += effect => { if (effect == PlayerEffect.Landed) landings++; };

            Step(40);

            Assert.That(falling.Motor.Grounded, Is.True);
            Assert.That(landings, Is.EqualTo(1));
            Assert.That(player.Phase, Is.EqualTo(PlayerPhase.Detached));
        }

        [Test]
        public void GroundRestDoesNotRepeatLandingEffectEvenWithLowThreshold()
        {
            config.LandingShakeSpeed = 0.1f;
            Step(3);
            int landings = 0;
            player.Effect += effect => { if (effect == PlayerEffect.Landed) landings++; };

            Step(40);

            Assert.That(landings, Is.EqualTo(0));
        }

        [Test]
        public void AssemblyZoomGrowsAndRecoversWithoutInstantJump()
        {
            EffectManager effects = CreateEffectManager();
            float initialTimeScale = Time.timeScale;
            effects.Play(PlayerEffect.Joined, null);
            Assert.That(effects.ZoomPulse, Is.EqualTo(0f));
            float previous = 0f;
            float peak = 0f;
            for (int i = 0; i < 50; i++)
            {
                effects.Simulate(0.01f);
                Assert.That(Mathf.Abs(effects.ZoomPulse - previous), Is.LessThan(0.07f));
                Assert.That(effects.ZoomPulse, Is.InRange(0f, 0.30001f));
                peak = Mathf.Max(peak, effects.ZoomPulse);
                previous = effects.ZoomPulse;
            }
            Assert.That(peak, Is.GreaterThan(0.28f));
            Assert.That(effects.ZoomPulse, Is.EqualTo(0f));
            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale));
        }

        [Test]
        public void RepeatedAssemblyContinuesZoomFromCurrentValue()
        {
            EffectManager effects = CreateEffectManager();
            float initialTimeScale = Time.timeScale;
            effects.Play(PlayerEffect.Joined, null);
            effects.Simulate(0.11f);
            float current = effects.ZoomPulse;
            Assert.That(current, Is.GreaterThan(0f));

            effects.Play(PlayerEffect.DownwardJoined, null);
            Assert.That(effects.ZoomPulse, Is.EqualTo(current));
            effects.Simulate(0.01f);
            Assert.That(Mathf.Abs(effects.ZoomPulse - current), Is.LessThan(0.025f));
            effects.Simulate(0.5f);
            Assert.That(effects.ZoomPulse, Is.EqualTo(0f));
            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale));
        }

        [TestCase(PlayerEffect.HeadReturned)]
        [TestCase(PlayerEffect.Landed)]
        public void ReturnAndLandingProduceBriefShakeWithoutZoomOrSlowMotion(PlayerEffect effect)
        {
            EffectManager effects = CreateEffectManager();
            float initialTimeScale = Time.timeScale;
            effects.Play(effect, null);
            Assert.That(effects.ShakeOffset.sqrMagnitude, Is.GreaterThan(0.000000001f));
            Assert.That(effects.ZoomPulse, Is.EqualTo(0f));
            Assert.That(Time.timeScale, Is.EqualTo(initialTimeScale));
            effects.Simulate(0.13f);
            Assert.That(effects.ShakeOffset, Is.EqualTo(Vector2.zero));
        }

        [UnityTest]
        public IEnumerator LoadedCameraRetainsAuthoredSizeAndShakeOrigin()
        {
            EffectManager effects = CreateEffectManager();
            GameObject sourceRig = new GameObject("Camera template");
            sourceRig.transform.SetParent(fixture.transform, false);
            sourceRig.SetActive(false);
            GameObject sourceShake = new GameObject("Authored shake origin");
            sourceShake.transform.SetParent(sourceRig.transform, false);
            Vector3 authoredOrigin = new Vector3(0.2f, 0.1f, 0f);
            sourceShake.transform.localPosition = authoredOrigin;
            GameObject cameraObject = new GameObject("Authored camera");
            cameraObject.transform.SetParent(sourceShake.transform, false);
            Camera sourceCamera = cameraObject.AddComponent<Camera>();
            sourceCamera.enabled = false;
            sourceCamera.orthographic = true;
            sourceCamera.orthographicSize = 8.25f;
            sourceRig.AddComponent<DemoCamera>().Configure(player, sourceCamera, sourceShake.transform);
            GameObject loadedRig = Object.Instantiate(sourceRig, fixture.transform);
            loadedRig.SetActive(true);
            Camera loadedCamera = loadedRig.GetComponentInChildren<Camera>();
            Transform loadedShake = loadedCamera.transform.parent;
            yield return null;
            yield return null;

            Assert.That(loadedCamera.orthographicSize, Is.EqualTo(8.25f));
            Assert.That(loadedShake.localPosition, Is.EqualTo(authoredOrigin));
            effects.Play(PlayerEffect.Joined, null);
            Assert.That(loadedCamera.orthographicSize, Is.EqualTo(8.25f));
            effects.Simulate(0.01f);
            yield return null;
            yield return null;
            Assert.That(loadedCamera.orthographicSize, Is.EqualTo(8.25f - effects.ZoomPulse).Within(0.0001f));
            effects.Simulate(0.5f);
            yield return null;
            yield return null;
            Assert.That(loadedCamera.orthographicSize, Is.EqualTo(8.25f));
        }

        private EffectManager CreateEffectManager()
        {
            GameObject effectsObject = new GameObject("Test effects");
            effectsObject.transform.SetParent(fixture.transform, false);
            EffectManager effects = effectsObject.AddComponent<EffectManager>();
            effects.enabled = false;
            return effects;
        }

        private MovableEntity CreateEntity(string name, EntityPart part, Vector2 localPosition)
        {
            GameObject entityObject = new GameObject(name);
            entityObject.transform.SetParent(fixture.transform);
            entityObject.transform.position = ToWorld(localPosition);
            Rigidbody2D rigidbody = entityObject.AddComponent<Rigidbody2D>();
            rigidbody.bodyType = RigidbodyType2D.Kinematic;
            BoxCollider2D collider = entityObject.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.7f, 0.8f);
            entityObject.AddComponent<KinematicMotor2D>();
            MovableEntity entity = entityObject.AddComponent<MovableEntity>();
            entity.Configure(part, true, config.Gravity);
            return entity;
        }

        private void CreateSurface(string name, Vector2 localPosition, Vector2 size, SurfaceKind kind)
        {
            GameObject surfaceObject = new GameObject(name);
            surfaceObject.transform.SetParent(fixture.transform);
            surfaceObject.transform.position = ToWorld(localPosition);
            BoxCollider2D collider = surfaceObject.AddComponent<BoxCollider2D>();
            collider.size = size;
            WorldSurface surface = surfaceObject.AddComponent<WorldSurface>();
            surface.Configure(kind, 0f);
        }

        private BoxCollider2D CreateLethalZone(Vector2 worldPosition)
        {
            GameObject zoneObject = new GameObject("Lethal zone");
            zoneObject.transform.SetParent(fixture.transform);
            zoneObject.transform.position = worldPosition;
            BoxCollider2D collider = zoneObject.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one * 0.2f;
            collider.isTrigger = true;
            zoneObject.AddComponent<LethalZone>();
            Physics2D.SyncTransforms();
            return collider;
        }

        private void ReachDetached()
        {
            CreateSurface("Solid ledge", new Vector2(4f, 3f), new Vector2(2f, 0.5f), SurfaceKind.Solid);
            ExtendAndMoveOverLedge();
            input.SetExternalInput(0f, false);
            StepUntil(PlayerPhase.Detached, 100);
        }

        private void ExtendAndMoveOverLedge()
        {
            Physics2D.SyncTransforms();
            input.SetExternalInput(0f, true);
            StepUntil(PlayerPhase.Holding, 40);
            input.SetExternalInput(1f, true);
            for (int i = 0; i < 100 && body.Motor.Position.x < origin.x + 4f; i++)
                Step();
            Assert.That(body.Motor.Position.x, Is.InRange(origin.x + 3.8f, origin.x + 4.2f));
        }

        private Vector2 ToWorld(Vector2 localPosition)
        {
            return origin + localPosition;
        }

        private void Step(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                player.SimulateStep(StepDuration);
                Physics2D.Simulate(StepDuration);
            }
        }

        private void StepUntil(PlayerPhase expectedPhase, int maximumSteps)
        {
            for (int i = 0; i < maximumSteps; i++)
            {
                Step();
                if (player.Phase == expectedPhase)
                    return;
            }

            Assert.Fail("Expected phase " + expectedPhase + ", actual " + player.Phase + ".");
        }
    }
}
