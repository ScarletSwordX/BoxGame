using NUnit.Framework;
using RulePyramid.Core;

namespace RulePyramid.Tests.EditMode
{
    public class RegionTutorialSessionTests
    {
        static GameSession PlaySession(GridCell playerCell, RegionTutorialData region)
        {
            return new GameSession(new LevelDefinition
            {
                id = "region-test",
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(5, 5, 5) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(5, 0, 5) } },
                entities = new[]
                {
                    new EntityDefinition { id = "player", kind = "Object", subject = "ROBOT", cell = playerCell },
                    new EntityDefinition { id = "subject", kind = "Text", token = "ROBOT", cell = new GridCell(0, 1, 0) },
                    new EntityDefinition { id = "is", kind = "Text", token = "IS", cell = new GridCell(1, 1, 0) },
                    new EntityDefinition { id = "you", kind = "Text", token = "YOU", cell = new GridCell(2, 1, 0) }
                },
                tutorial = new TutorialData { regions = new[] { region } }
            });
        }

        static WorldModel World(GridCell robotCell, GridCell rockCell, bool rockIsYou = false)
        {
            var world = new WorldModel();
            world.Entities.Add(new EntityState { Id = "robot", Kind = EntityKind.Object, Subject = "ROBOT", Cell = robotCell });
            world.Entities.Add(new EntityState { Id = "rock", Kind = EntityKind.Object, Subject = "ROCK", Cell = rockCell });
            world.Rules[rockIsYou ? "ROCK" : "ROBOT"].Add("YOU");
            return world;
        }

        static RegionTutorialData Region(string id, GridCell min, GridCell max, float seconds)
        {
            return new RegionTutorialData
            {
                id = id,
                name = id,
                text = "提示 " + id,
                bounds = new GridCellBox { min = min, max = max },
                durationSeconds = seconds,
                enabled = true
            };
        }

        [Test]
        public void InitialPositionQueuesSimultaneousPromptsInListOrderAndTimesEachFromDisplay()
        {
            var cell = new GridCell(1, 2, 3);
            var session = new RegionTutorialSession(new TutorialData
            {
                regions = new[] { Region("first", cell, cell, 2f), Region("second", cell, cell, 3f) }
            });

            Assert.IsTrue(session.Observe(World(cell, new GridCell(8, 8, 8))));
            Assert.AreEqual("first", session.Current.id);
            Assert.AreEqual(RegionTutorialState.Showing, session.StateAt(0));
            Assert.AreEqual(RegionTutorialState.Queued, session.StateAt(1));
            Assert.IsFalse(session.Observe(World(cell, cell)));
            Assert.IsTrue(session.Tick(2.5));
            Assert.AreEqual("second", session.Current.id);
            Assert.AreEqual(3d, session.RemainingSeconds, 0.0001d);
            Assert.AreEqual(RegionTutorialState.Shown, session.StateAt(0));
            Assert.IsTrue(session.Tick(3));
            Assert.IsNull(session.Current);
            Assert.AreEqual(RegionTutorialState.Shown, session.StateAt(1));
        }

        [Test]
        public void LongFrameDoesNotSkipQueuedPromptsBeforeTheyAreDisplayed()
        {
            var cell = new GridCell(1, 1, 1);
            var session = new RegionTutorialSession(new TutorialData
            {
                regions = new[] { Region("first", cell, cell, 1), Region("second", cell, cell, 2), Region("third", cell, cell, 3) }
            });
            session.Observe(World(cell, cell));
            session.Tick(30);
            Assert.AreEqual("second", session.Current.id);
            Assert.AreEqual(2, session.RemainingSeconds);
            Assert.AreEqual(RegionTutorialState.Queued, session.StateAt(2));
        }

        [Test]
        public void OnlyCurrentYouTriggersAndObservedPromptSurvivesWorldRewind()
        {
            var inside = new GridCell(1, 1, 1);
            var outside = new GridCell(2, 1, 1);
            var session = new RegionTutorialSession(new TutorialData
            {
                regions = new[] { Region("transfer", inside, inside, 1f) }
            });

            Assert.IsFalse(session.Observe(World(outside, inside)));
            Assert.IsTrue(session.Observe(World(outside, inside, rockIsYou: true)));
            Assert.AreEqual("transfer", session.Current.id);
            session.Tick(1d);
            Assert.IsFalse(session.Observe(World(outside, inside, rockIsYou: true)));
            Assert.AreEqual(RegionTutorialState.Shown, session.StateAt(0));
            Assert.IsFalse(session.Observe(World(inside, outside)));
        }

        [Test]
        public void MultipleYouObserveEveryRegionInAuthorOrderRegardlessOfEntityOrder()
        {
            var first = new GridCell(1, 1, 1);
            var second = new GridCell(3, 1, 1);
            var world = World(second, first);
            world.Rules["ROCK"].Add("YOU");
            foreach (bool reverse in new[] { false, true })
            {
                if (reverse) world.Entities.Reverse();
                var prompts = new RegionTutorialSession(new TutorialData
                {
                    regions = new[] { Region("first", first, first, 1), Region("second", second, second, 1) }
                });
                Assert.IsTrue(prompts.Observe(world));
                Assert.AreEqual("first", prompts.Current.id);
                Assert.AreEqual(RegionTutorialState.Queued, prompts.StateAt(1));
                Assert.IsFalse(prompts.Observe(world));
            }
        }

        [Test]
        public void CloningCopiesRegionsAndBounds()
        {
            var level = new LevelDefinition
            {
                tutorial = new TutorialData { regions = new[] { Region("a", new GridCell(0, 0, 0), new GridCell(1, 1, 1), 2f) } },
                authoringSourceJson = "source"
            };
            var clone = level.Clone();
            clone.tutorial.regions[0].bounds.max = new GridCell(9, 9, 9);
            Assert.AreEqual(new GridCell(1, 1, 1), level.tutorial.regions[0].bounds.max);
            Assert.AreEqual("source", clone.authoringSourceJson);
        }

        [Test]
        public void SuccessfulCommandExposesIntermediateYouCellsForRegionObservation()
        {
            var midway = new GridCell(2, 2, 1);
            var game = PlaySession(new GridCell(1, 3, 1), Region("fall-through", midway, midway, 2f));
            var tutorial = new RegionTutorialSession(game.Level.tutorial);
            Assert.IsFalse(tutorial.Observe(game));

            Assert.IsTrue(game.TryExecute("E"));
            CollectionAssert.Contains(game.LastControlledCells, midway);
            Assert.AreEqual(new GridCell(2, 1, 1), game.World.FindYou().Cell);
            Assert.IsTrue(tutorial.Observe(game));
            Assert.AreEqual("fall-through", tutorial.Current.id);
        }

        [Test]
        public void RejectedCommandDoesNotPublishIntermediateVisits()
        {
            var target = new GridCell(1, 1, 1);
            var game = PlaySession(new GridCell(0, 1, 1), Region("never", target, target, 2f));
            var tutorial = new RegionTutorialSession(game.Level.tutorial);

            Assert.IsFalse(game.TryExecute("W"));
            Assert.AreEqual(0, game.LastControlledCells.Count);
            Assert.IsFalse(tutorial.Observe(game));
            Assert.IsNull(tutorial.Current);
        }
    }
}
