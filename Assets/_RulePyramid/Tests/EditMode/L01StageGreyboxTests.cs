using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class L01StageGreyboxTests
    {
        static LevelDefinition Draft(string id = "p1")
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/L1P" + id.Substring(1) + ".json");
            Assert.IsTrue(File.Exists(path), "Missing L01 greybox: " + path);
            return LevelJsonSerializer.FromJson(File.ReadAllText(path));
        }

        static LevelDefinition Stage(LevelDefinition draft, string id)
        {
            return Draft(id);
        }

        static void AssertCellInside(GridCell cell, GridCellBox bounds)
        {
            Assert.That(cell.x, Is.InRange(bounds.min.x, bounds.max.x));
            Assert.That(cell.y, Is.InRange(bounds.min.y, bounds.max.y));
            Assert.That(cell.z, Is.InRange(bounds.min.z, bounds.max.z));
        }

        [Test]
        public void IndependentMapsHaveExactBoundsAndCompleteSingleLayerFloor()
        {
            var draft = Draft();
            Assert.IsNull(draft.stagePlan, "新阶段必须是真正的独立地图");
            var expectations = new[]
            {
                new { Id = "p1", Min = new GridCell(1, 0, 2), Max = new GridCell(7, 1, 4) },
                new { Id = "p2", Min = new GridCell(1, 0, 2), Max = new GridCell(7, 1, 8) },
                new { Id = "p3", Min = new GridCell(0, 0, 0), Max = new GridCell(11, 1, 9) }
            };
            foreach (var expected in expectations)
            {
                var stage = Stage(draft, expected.Id);
                Assert.IsNull(stage.stagePlan, expected.Id);
                var session = new LevelEditSession(stage);
                Assert.AreEqual(stage.bounds.min, session.Draft.bounds.min);
                Assert.AreEqual(stage.bounds.max, session.Draft.bounds.max);
                Assert.IsTrue(session.TryStartPlaytest(out var game, out var error), error);
                Assert.AreEqual(stage.bounds.min, game.Level.bounds.min);
                Assert.AreEqual(stage.bounds.max, game.Level.bounds.max);
                session.StopPlaytest();
                Assert.AreEqual(expected.Min, stage.bounds.min, expected.Id);
                Assert.AreEqual(expected.Max, stage.bounds.max, expected.Id);
                var floor = LevelCloner.ExpandTerrain(stage.terrain);
                int width = expected.Max.x - expected.Min.x + 1;
                int depth = expected.Max.z - expected.Min.z + 1;
                Assert.AreEqual(width * depth, floor.Count, expected.Id + " floor cell count");
                for (int x = expected.Min.x; x <= expected.Max.x; x++)
                for (int z = expected.Min.z; z <= expected.Max.z; z++)
                    Assert.IsTrue(floor.Contains(new GridCell(x, 0, z)), expected.Id + " missing floor " + x + "," + z);
                Assert.IsTrue(floor.All(cell => cell.y == 0), expected.Id + " contains a stone column");
                Assert.IsTrue(stage.entities.All(entity => entity.cell.y == 1), expected.Id + " entity height");
                Assert.IsTrue(stage.entities.All(entity =>
                    entity.cell.x >= expected.Min.x && entity.cell.x <= expected.Max.x &&
                    entity.cell.z >= expected.Min.z && entity.cell.z <= expected.Max.z), expected.Id + " entity bounds");
            }
        }

        [Test]
        public void EachInitialStateIsLegalAndEveryReferenceWinsWithItsSpecifiedObject()
        {
            var draft = Draft();
            Assert.IsFalse(LevelValidator.ValidateStageSave(draft).HasStructureErrors);
            foreach (var id in new[] { "p1", "p2", "p3" })
            {
                var stage = Stage(draft, id);
                var report = LevelValidator.ValidateForPlaytest(stage);
                Assert.IsTrue(report.CanPlaytest, id + ": " + report);
                Assert.IsFalse(LevelValidator.ValidateAuthoringOccupancy(stage).HasStructureErrors, id);
                Assert.IsNotNull(WorldModel.FromLevel(stage).Actor(), id + " must start with control");
                Assert.IsNotEmpty(stage.referenceSolutions, id);
                foreach (var solution in stage.referenceSolutions)
                {
                    var result = ReplayRunner.Run(stage, solution);
                    Assert.IsTrue(result.Won, id + "/" + solution.id + ": " + result.FailReason);
                    Assert.AreEqual("robot_01", result.ActorIdAtWin, id + "/" + solution.id);
                    if (solution.id == "P3-A") Assert.That(result.WinId, Does.StartWith("wall_"));
                    if (solution.id == "P3-B") Assert.AreEqual("goal", result.WinId);
                }
            }
        }

        [Test]
        public void OnlyP1HasOneExactMovementPrompt()
        {
            var draft = Draft();
            var first = Stage(draft, "p1");
            Assert.IsEmpty(first.tutorial.hints);
            Assert.AreEqual(1, first.tutorial.regions.Length);
            var prompt = first.tutorial.regions[0];
            Assert.IsTrue(prompt.enabled);
            Assert.AreEqual("方向键 / WASD 移动", prompt.text);
            Assert.AreEqual(6f, prompt.durationSeconds);
            Assert.AreEqual(first.entities.Single(e => e.id == "robot_01").cell, prompt.bounds.min);
            Assert.AreEqual(prompt.bounds.min, prompt.bounds.max);
            foreach (var id in new[] { "p2", "p3" })
            {
                var tutorial = Stage(draft, id).tutorial;
                Assert.IsEmpty(tutorial.hints, id);
                Assert.IsEmpty(tutorial.regions, id);
                Assert.IsTrue(string.IsNullOrEmpty(tutorial.objective), id);
                Assert.IsTrue(string.IsNullOrEmpty(tutorial.concept), id);
                Assert.IsTrue(string.IsNullOrEmpty(tutorial.observation), id);
                Assert.IsTrue(string.IsNullOrEmpty(tutorial.necessity), id);
            }
        }

        [Test]
        public void AirWallBlocksWalkingPushingClimbingAndJumpingOutsideThePlayableLayer()
        {
            var draft = Draft();
            var edge = Stage(draft, "p3");
            edge.entities.Single(e => e.id == "robot_01").cell = new GridCell(11, 1, 0);
            var edgeWorld = WorldModel.FromLevel(edge);
            Assert.IsFalse(edgeWorld.TryCommand("E", out _));
            Assert.IsFalse(edgeWorld.TryCommand("S", out _));
            Assert.AreEqual(new GridCell(11, 1, 0), edgeWorld.Actor().Cell);

            var word = Stage(draft, "p3");
            word.entities.Single(e => e.id == "robot_01").cell = new GridCell(8, 1, 2);
            var wordWorld = WorldModel.FromLevel(word);
            Assert.IsTrue(wordWorld.TryCommand("E", out _), wordWorld.LastRejection);
            Assert.IsTrue(wordWorld.TryCommand("E", out _), wordWorld.LastRejection);
            Assert.AreEqual(new GridCell(11, 1, 2), wordWorld.Entity("text_6").Cell);
            Assert.IsFalse(wordWorld.TryCommand("E", out _));
            Assert.AreEqual(new GridCell(11, 1, 2), wordWorld.Entity("text_6").Cell);
            AssertCellInside(wordWorld.Actor().Cell, word.bounds);

            var wall = Stage(draft, "p2");
            wall.entities.Single(e => e.id == "robot_01").cell = new GridCell(3, 1, 4);
            var wallWorld = WorldModel.FromLevel(wall);
            Assert.IsFalse(wallWorld.TryCommand("N", out _), "WALL IS STOP must not be climbed at Y=2");
            Assert.IsFalse(wallWorld.TryCommand("J", out _), "jump must not leave Y=1");
            Assert.AreEqual(new GridCell(3, 1, 4), wallWorld.Actor().Cell);
        }

        [Test]
        public void IndependentMapSessionsDoNotMutateOtherMaps()
        {
            var draft = Draft();
            var before = LevelJsonSerializer.ToJson(draft);
            var p1 = Stage(draft, "p1");
            var p2 = Stage(draft, "p2");
            var p3 = Stage(draft, "p3");
            var p2Start = p2.entities.Single(e => e.id == "robot_01").cell;
            var p3Start = p3.entities.Single(e => e.id == "robot_01").cell;
            var session = new GameSession(p1);
            foreach (var command in p1.referenceSolutions[0].commands)
                Assert.IsTrue(session.TryExecute(command), command + ": " + session.LastRejectReason);
            Assert.IsTrue(session.Won);
            Assert.AreEqual(p2Start, p2.entities.Single(e => e.id == "robot_01").cell);
            Assert.AreEqual(p3Start, p3.entities.Single(e => e.id == "robot_01").cell);
            Assert.AreEqual(p2Start, Stage(draft, "p2").entities.Single(e => e.id == "robot_01").cell);
            Assert.AreEqual(p3Start, Stage(draft, "p3").entities.Single(e => e.id == "robot_01").cell);
            Assert.AreEqual(before, LevelJsonSerializer.ToJson(draft));
        }

        [Test]
        public void P1AllowsWalkingVictoryWhileP2AndP3RequireWorldInteraction()
        {
            var draft = Draft();
            Assert.AreEqual("TRAVERSAL_WIN_FOUND", InteractionAudit.AuditNoInteraction(Stage(draft, "p1")).Status);
            foreach (var id in new[] { "p2", "p3" })
            {
                var audit = InteractionAudit.AuditNoInteraction(Stage(draft, id), 20000);
                Assert.AreEqual("EXHAUSTED_NO_INTERACTION_WIN", audit.Status,
                    id + " states=" + audit.States + " attempts=" + audit.Attempts);
            }
        }
    }
}
