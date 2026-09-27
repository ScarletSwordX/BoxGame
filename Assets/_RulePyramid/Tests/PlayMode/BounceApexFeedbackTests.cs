using System.Collections;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class BounceApexFeedbackTests
    {
        static EntityDefinition Word(string id, string token, int x, int z) =>
            new EntityDefinition { id = id, kind = "Text", token = token, cell = new GridCell(x, 1, z) };

        static LevelDefinition Level(string subject = "ROBOT") => new LevelDefinition
        {
            schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "apex-feedback", title = "弹跳提示测试",
            bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(8, 8, 8) },
            terrain = new[]
            {
                new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(8, 0, 8) },
                new GridCellBox { min = new GridCell(3, 5, 2), max = new GridCell(3, 5, 2) }
            },
            options = new OptionsData
            {
                actionMode = "MoveClimbHoldPush", winMode = "DistinctEntitiesSameCell",
                gravityMode = "WorldDownExceptHoverOrFly", collisionMode = "SolidPairsTerrainUniversal",
                solidityMode = "YouPushStopOrText", supportMode = "StrictBelow",
                controlMode = "SingleYouTransfer_NoControlUndo", bounceRiseCells = 3,
                transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                ruleSourceMode = "WorldTextOnly", textMobilityMode = "AllWordsMovable_GeometryAccess"
            },
            entities = new[]
            {
                new EntityDefinition { id = "actor", kind = "Object", subject = subject, cell = new GridCell(2, 2, 2) },
                new EntityDefinition { id = "spring", kind = "Object", subject = "SPRING", cell = new GridCell(2, 1, 2) },
                Word("you-noun", subject, 0, 0), Word("you-is", "IS", 1, 0), Word("you-prop", "YOU", 2, 0),
                Word("stop-noun", "SPRING", 0, 5), Word("stop-is", "IS", 1, 5), Word("stop-prop", "STOP", 2, 5),
                Word("bounce-noun", "SPRING", 0, 7), Word("bounce-is", "IS", 1, 7), Word("bounce-prop", "BOUNCY", 2, 7)
            }
        };

        static GameSession AtApex(string subject = "ROBOT")
        {
            var session = new GameSession(Level(subject));
            Assert.IsTrue(session.TryExecute("J"));
            Assert.AreEqual(MotionPhase.BounceApex, session.Phase);
            Assert.AreEqual(new GridCell(2, 5, 2), session.World.FindYou().Cell);
            return session;
        }

        [TestCase("ROBOT")]
        [TestCase("CLOUD")]
        public void WaitsForAnimationThenDelaysHintsWithoutAdvancingWorld(string subject)
        {
            var session = AtApex(subject);
            var cue = new BounceApexCueState();
            cue.Tick(session, true, 10f, 1.5f);
            Assert.IsFalse(cue.Waiting);
            Assert.Zero(cue.Strength);
            cue.Tick(session, false, 1.4f, 1.5f);
            Assert.IsTrue(cue.Waiting);
            Assert.AreEqual("actor", cue.ActorId);
            Assert.AreEqual(1f, cue.Strength);
            Assert.Zero(cue.HintAlpha);
            cue.Tick(session, false, 0.25f, 1.5f);
            Assert.Greater(cue.HintAlpha, 0f);
            int history = session.World.History.Count;
            int log = session.World.Log.Count;
            cue.Tick(session, false, 600f, 1.5f);
            Assert.AreEqual(1f, cue.Strength, "持续等待期间不自动移除色散和压暗。");
            Assert.AreEqual(1f, cue.HintAlpha);
            Assert.AreEqual(1, session.TurnCount);
            Assert.AreEqual(history, session.World.History.Count);
            Assert.AreEqual(log, session.World.Log.Count);
            Assert.AreEqual(new GridCell(2, 5, 2), session.World.FindYou().Cell);
        }

        [Test]
        public void InvalidDirectionKeepsCueAndValidSteerHidesHintsImmediately()
        {
            var session = AtApex();
            var cue = new BounceApexCueState();
            cue.Tick(session, false, 2f, 1.5f);
            Assert.IsFalse(session.TryExecute("E"));
            cue.Tick(session, false, 0.1f, 1.5f);
            Assert.IsTrue(cue.Waiting);
            Assert.AreEqual(1f, cue.HintAlpha);
            Assert.IsTrue(session.TryExecute("N"));
            cue.Tick(session, true, 0.01f, 1.5f);
            Assert.IsFalse(cue.Waiting);
            Assert.Zero(cue.HintAlpha);
            Assert.Greater(cue.Strength, 0f, "离开顶点时短暂淡出。");
            cue.Tick(session, false, 0.2f, 1.5f);
            Assert.Zero(cue.Strength);
        }

        [Test]
        public void ReleaseRebounceAndUndoStartAFreshHintDelay()
        {
            var session = AtApex();
            var cue = new BounceApexCueState();
            cue.Tick(session, false, 2f, 1.5f);
            Assert.IsTrue(session.TryExecute("WAIT"));
            cue.Tick(session, true, 0.5f, 1.5f);
            Assert.Zero(cue.HintAlpha);
            cue.Tick(session, false, 0.1f, 1.5f);
            Assert.Zero(cue.HintAlpha);
            Assert.IsTrue(session.Undo());
            cue.Tick(session, false, 0.1f, 1.5f);
            Assert.IsTrue(cue.Waiting);
            Assert.Zero(cue.HintAlpha);
        }

        [Test]
        public void RestartWinNoControlAndNullSessionRemoveCue()
        {
            var session = AtApex();
            var cue = new BounceApexCueState();
            cue.Tick(session, false, 2f, 1.5f);
            session.Restart();
            cue.Tick(session, false, 0f, 1.5f);
            Assert.Zero(cue.Strength);
            session = AtApex();
            cue.Tick(session, false, 2f, 1.5f);
            session.World.WonLatched = true;
            cue.Tick(session, false, 1f, 1.5f);
            Assert.IsFalse(cue.Waiting);
            Assert.Zero(cue.Strength);
            session.World.WonLatched = false;
            session.World.Entity("you-prop").Token = "STOP";
            session.World.Refresh();
            cue.Tick(session, false, 1f, 1.5f);
            Assert.IsFalse(cue.Waiting);
            cue.Tick(null, false, 0f, 1.5f);
            Assert.IsNull(cue.ActorId);
            Assert.Zero(cue.HintAlpha);
        }

        [TestCase(WorldDirection.North, true)]
        [TestCase(WorldDirection.South, true)]
        [TestCase(WorldDirection.West, true)]
        [TestCase(WorldDirection.East, false)]
        public void ArrowAvailabilityMatchesActualApexInputWithoutMutatingWorld(WorldDirection direction, bool expected)
        {
            var session = AtApex();
            int logs = session.World.Log.Count;
            int history = session.World.History.Count;
            var position = session.World.FindYou().Cell;
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(expected, BounceApexArrows.CanSteer(session.World, direction));
            Assert.AreEqual(logs, session.World.Log.Count);
            Assert.AreEqual(history, session.World.History.Count);
            Assert.AreEqual(position, session.World.FindYou().Cell);
            Assert.AreEqual(expected, session.TryExecute(WorldDirections.ToToken(direction)));
        }

        [TestCase("Text", false)]
        [TestCase("STOP", false)]
        [TestCase("PUSH", false)]
        [TestCase("Ghost", true)]
        [TestCase("Bounds", false)]
        public void ArrowsRespectTextSolidityAndBoundsWithoutOfferingPushOrClimb(string obstacle, bool expected)
        {
            var session = AtApex();
            var world = session.World;
            if (obstacle == "Bounds")
            {
                world.FindYou().Cell = new GridCell(0, 5, 2);
                world.Apex[world.ActorId].ApexPos = world.FindYou().Cell;
            }
            else
            {
                world.Entities.Add(new EntityState
                {
                    Id = "obstacle", Kind = obstacle == "Text" ? EntityKind.Text : EntityKind.Object,
                    Subject = "ROCK", Token = "ROCK", Cell = new GridCell(1, 5, 2)
                });
                if (obstacle == "STOP" || obstacle == "PUSH")
                {
                    string[] tokens = { "ROCK", "IS", obstacle };
                    for (int i = 0; i < tokens.Length; i++)
                        world.Entities.Add(new EntityState
                        {
                            Id = "obstacle-rule-" + i, Kind = EntityKind.Text,
                            Token = tokens[i], Cell = new GridCell(i, 1, 4)
                        });
                }
                world.Refresh();
            }
            Assert.AreEqual(expected, BounceApexArrows.CanSteer(world, WorldDirection.West));
            // 每类障碍都对照真实顶点输入的执行结果。
            Assert.AreEqual(expected, session.TryExecute("W"));
        }

        [Test]
        public void ArrowsAreUnavailableOutsideControlledApex()
        {
            Assert.IsFalse(BounceApexArrows.CanSteer(null, WorldDirection.North));
            var session = new GameSession(Level());
            Assert.IsFalse(BounceApexArrows.CanSteer(session.World, WorldDirection.North));
            session = AtApex();
            session.World.WonLatched = true;
            Assert.IsFalse(BounceApexArrows.CanSteer(session.World, WorldDirection.North));
        }

        [Test]
        public void ProjectedArrowsFollowMapAxesAndInputMapping()
        {
            var host = new GameObject("Apex arrow projection");
            try
            {
                var camera = host.AddComponent<Camera>();
                var slots = host.AddComponent<CameraSlotsController>();
                slots.Snap();
                var origin = new Vector3(2f, 5f, 2f);
                var north = BounceApexArrows.ProjectDirection(camera, origin, slots.ScreenToWorld(Vector2.up));
                var south = BounceApexArrows.ProjectDirection(camera, origin, slots.ScreenToWorld(Vector2.down));
                var west = BounceApexArrows.ProjectDirection(camera, origin, slots.ScreenToWorld(Vector2.left));
                var east = BounceApexArrows.ProjectDirection(camera, origin, slots.ScreenToWorld(Vector2.right));
                Assert.Less(north.x, 0f);
                Assert.Less(north.y, 0f, "北向在固定第一视角投影到左上。");
                Assert.Greater(east.x, 0f);
                Assert.Less(east.y, 0f);
                Assert.Less(Vector2.Distance(north, -south), 0.001f);
                Assert.Less(Vector2.Distance(east, -west), 0.001f);
                Assert.AreEqual(1f, north.magnitude, 0.001f);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator BootstrapAutomaticallyBindsEffectAndCleansUpOnUndoRestartAndDisable()
        {
            var host = new GameObject("Bounce feedback integration");
            host.SetActive(false);
            var cameraHost = new GameObject("Bounce feedback camera");
            var camera = cameraHost.AddComponent<Camera>();
            var slots = cameraHost.AddComponent<CameraSlotsController>();
            var worldHost = new GameObject("Bounce feedback world");
            var world = worldHost.AddComponent<WorldView>();
            var animator = worldHost.AddComponent<EventAnimator>();
            animator.worldView = world;
            animator.stepDuration = 0.05f;
            var asset = new TextAsset(LevelJsonSerializer.ToJson(Level()));
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { asset };
            var bootstrap = host.AddComponent<GameBootstrap>();
            bootstrap.catalog = catalog;
            bootstrap.cameraSlots = slots;
            bootstrap.animator = animator;
            bootstrap.worldView = world;
            try
            {
                host.SetActive(true);
                yield return null;
                var effect = camera.GetComponent<BounceApexFeedback>();
                Assert.IsNotNull(effect);
                Assert.AreSame(bootstrap, effect.bootstrap);
                bootstrap.Submit("J");
                Assert.IsTrue(animator.IsPlaying);
                yield return null;
                Assert.IsFalse(effect.State.Waiting, "逻辑到顶但动画未结束，不显示提示。");
                float deadline = Time.realtimeSinceStartup + 5f;
                while (animator.IsPlaying && Time.realtimeSinceStartup < deadline) yield return null;
                yield return null;
                Assert.IsFalse(animator.IsPlaying);
                Assert.IsTrue(effect.State.Waiting);
                Assert.Zero(effect.State.HintAlpha);
                effect.State.Tick(bootstrap.Session, false, 2f, 1.5f);
                yield return null;
                Assert.AreEqual(1f, effect.State.HintAlpha);
                Assert.IsFalse(BounceApexArrows.CanSteer(bootstrap.Session.World, WorldDirection.East));
                bootstrap.Undo();
                Assert.Zero(effect.State.Strength);
                bootstrap.Submit("J");
                animator.CancelAndSnap(bootstrap.Session.World);
                yield return null;
                Assert.IsTrue(effect.State.Waiting);
                bootstrap.Restart();
                Assert.Zero(effect.State.Strength);
                bootstrap.Submit("J");
                animator.CancelAndSnap(bootstrap.Session.World);
                yield return null;
                effect.enabled = false;
                Assert.Zero(effect.State.Strength);
                Assert.Zero(effect.State.HintAlpha);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(cameraHost);
                Object.Destroy(worldHost);
                Object.Destroy(catalog);
                Object.Destroy(asset);
            }
        }
    }
}
