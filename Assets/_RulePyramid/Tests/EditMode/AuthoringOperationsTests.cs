using System;
using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class AuthoringOperationsTests
    {
        static LevelDefinition Fixture()
        {
            return new LevelDefinition
            {
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 4, 4) },
                terrain = new[]
                {
                    new GridCellBox { id = "stone", min = new GridCell(0, 0, 0), max = new GridCell(2, 0, 0), appearance = "brick" }
                },
                entities = new[]
                {
                    new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 1) }
                }
            };
        }

        [Test]
        public void ErasingPartOfTerrainPreservesAppearanceAndOriginalId()
        {
            var level = Fixture();
            AuthoringOperations.RemoveTerrain(level, new GridCell(1, 0, 0), new GridCell(1, 0, 0));
            Assert.AreEqual(2, level.terrain.Length);
            Assert.AreEqual("brick", level.terrain[0].appearance);
            Assert.AreEqual("brick", level.terrain[1].appearance);
            Assert.AreEqual("stone", level.terrain[0].id);
            Assert.IsFalse(LevelCloner.ExpandTerrain(level.terrain).Contains(new GridCell(1, 0, 0)));
        }

        [Test]
        public void TransactionFailureDoesNotChangeDraftOrUndoHistory()
        {
            var session = new LevelEditSession(Fixture());
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Edit("move", draft =>
                AuthoringOperations.MoveEntities(draft, new[] { "rock" }, new GridCell(10, 0, 0))));
            Assert.AreEqual(new GridCell(1, 1, 1), session.Draft.entities[0].cell);
            Assert.IsFalse(session.Dirty);
            Assert.IsFalse(session.Undo());
        }

        [Test]
        public void SavePointSurvivesUndoAndRedo()
        {
            var session = new LevelEditSession(Fixture());
            session.Edit("move", draft => AuthoringOperations.MoveEntities(draft, new[] { "rock" }, new GridCell(1, 0, 0)));
            session.MarkSaved();
            Assert.IsFalse(session.Dirty);
            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Dirty);
            Assert.IsTrue(session.Redo());
            Assert.IsFalse(session.Dirty);
        }

        [Test]
        public void SentencePlacementCreatesIndependentTextWithUniqueIds()
        {
            var level = Fixture();
            var ids = AuthoringOperations.PlaceSentence(level, new[] { "ROCK", "IS", "PUSH" }, new GridCell(0, 1, 2), true);
            Assert.AreEqual(3, ids.Length);
            Assert.AreNotEqual(ids[0], ids[1]);
            Assert.AreEqual(new GridCell(2, 1, 2), level.entities[3].cell);
            Assert.AreEqual("Text", level.entities[3].kind);
        }

        [Test]
        public void VerticalSentencePlacementReadsTopToBottomAndRejectsOutsideMap()
        {
            var level = Fixture();
            AuthoringOperations.PlaceSentence(level, new[] { "ROCK", "IS", "PUSH" }, new GridCell(4, 1, 4), false);
            Assert.AreEqual(new GridCell(4, 1, 4), level.entities[1].cell);
            Assert.AreEqual(new GridCell(4, 1, 3), level.entities[2].cell);
            Assert.AreEqual(new GridCell(4, 1, 2), level.entities[3].cell);
            Assert.Throws<ArgumentOutOfRangeException>(() => AuthoringOperations.PlaceSentence(
                level, new[] { "ROCK", "IS", "PUSH" }, new GridCell(3, 1, 1), false));
            Assert.AreEqual(4, level.entities.Length, "失败放置不得留下半句");
        }

        [Test]
        public void BoxTerrainStaysCompactAndCannotCoverAnEntity()
        {
            var level = Fixture();
            AuthoringOperations.AddTerrain(level, new GridCell(0, 2, 2), new GridCell(3, 3, 3), "glass");
            Assert.AreEqual(2, level.terrain.Length);
            Assert.AreEqual(new GridCell(3, 3, 3), level.terrain[1].max);
            Assert.Throws<ArgumentException>(() => AuthoringOperations.AddTerrain(level, new GridCell(1, 1, 1), new GridCell(2, 1, 1)));
            Assert.AreEqual(2, level.terrain.Length);
        }

        [Test]
        public void CuttingInteriorOfBoxProducesAtMostSixBoxes()
        {
            var level = Fixture();
            level.terrain = new[] { new GridCellBox { id = "solid", min = new GridCell(0, 0, 0), max = new GridCell(4, 4, 4), appearance = "stone" } };
            AuthoringOperations.RemoveTerrain(level, new GridCell(2, 2, 2), new GridCell(2, 2, 2));
            Assert.LessOrEqual(level.terrain.Length, 6);
            Assert.AreEqual(124, LevelCloner.ExpandTerrain(level.terrain).Count);
            foreach (var box in level.terrain) Assert.AreEqual("stone", box.appearance);
        }

        [Test]
        public void EntityMovesRejectTerrainWordsAndOtherObjects()
        {
            var level = Fixture();
            level.entities = new[]
            {
                level.entities[0],
                new EntityDefinition { id = "other", kind = "Object", subject = "FLAG", cell = new GridCell(2, 1, 1) },
                new EntityDefinition { id = "word", kind = "Text", token = "IS", cell = new GridCell(1, 1, 2) }
            };
            Assert.Throws<ArgumentException>(() => AuthoringOperations.MoveEntities(level, new[] { "rock" }, new GridCell(0, -1, -1)));
            Assert.Throws<ArgumentException>(() => AuthoringOperations.CopyEntities(level, new[] { "rock" }, new GridCell(0, 0, 1)));
            Assert.Throws<ArgumentException>(() => AuthoringOperations.MoveEntities(level, new[] { "rock" }, new GridCell(1, 0, 0)));
            Assert.Throws<ArgumentException>(() => AuthoringOperations.CopyEntities(level, new[] { "rock" }, new GridCell(1, 0, 0)));
            Assert.AreEqual(new GridCell(1, 1, 1), level.entities[0].cell);
        }

        [Test]
        public void ObjectStrokeDeduplicatesCellsAndRejectsAnyOccupiedTargetAtomically()
        {
            var level = Fixture();
            var a = new GridCell(0, 1, 1);
            var b = new GridCell(0, 1, 2);
            var ids = AuthoringOperations.PlaceObjects(level, new[] { a, a, b }, "FLAG");
            Assert.AreEqual(2, ids.Length);
            Assert.AreNotEqual(ids[0], ids[1]);
            Assert.AreEqual(a, level.entities[1].cell);
            var before = level.entities;
            Assert.Throws<ArgumentException>(() => AuthoringOperations.PlaceObjects(level,
                new[] { new GridCell(4, 1, 4), b }, "FLAG"));
            Assert.AreSame(before, level.entities);
            Assert.Throws<ArgumentException>(() => AuthoringOperations.PlaceObjects(level,
                new[] { new GridCell(4, 1, 4), new GridCell(1, 0, 0) }, "FLAG"));
            Assert.AreSame(before, level.entities);
        }

        [Test]
        public void TerrainPlacementRejectsExistingTerrainWithoutPartialChange()
        {
            var level = Fixture();
            var before = level.terrain;
            Assert.Throws<ArgumentException>(() => AuthoringOperations.AddTerrain(level,
                new GridCell(2, 0, 0), new GridCell(3, 0, 0), "brick"));
            Assert.AreSame(before, level.terrain);
        }

        [Test]
        public void OccupancyValidationFindsEveryKindOfOverlap()
        {
            var level = Fixture();
            level.terrain = new[]
            {
                level.terrain[0],
                new GridCellBox { id = "overlap", min = new GridCell(2, 0, 0), max = new GridCell(3, 0, 0) }
            };
            level.entities = new[]
            {
                level.entities[0],
                new EntityDefinition { id = "word", kind = "Text", token = "IS", cell = new GridCell(1, 1, 1) },
                new EntityDefinition { id = "floorObject", kind = "Object", subject = "FLAG", cell = new GridCell(2, 0, 0) }
            };
            var report = LevelValidator.ValidateAuthoringOccupancy(level);
            CollectionAssert.Contains(ReportCodes(report), "TERRAIN_OVERLAP");
            CollectionAssert.Contains(ReportCodes(report), "ENTITY_OVERLAP");
            CollectionAssert.Contains(ReportCodes(report), "ENTITY_TERRAIN_OVERLAP");
        }

        static string[] ReportCodes(ValidationReport report)
        {
            var codes = new string[report.Issues.Count];
            for (int i = 0; i < codes.Length; i++) codes[i] = report.Issues[i].Code;
            return codes;
        }

        [Test]
        public void SentenceRejectsUnknownAndOccupiedWords()
        {
            var level = Fixture();
            Assert.Throws<ArgumentException>(() => AuthoringOperations.PlaceSentence(level, new[] { "UNKNOWN" }, new GridCell(0, 1, 2), true));
            Assert.Throws<ArgumentException>(() => AuthoringOperations.PlaceSentence(level, new[] { "ROCK" }, new GridCell(1, 0, 0), true));
            level.entities = new[] { level.entities[0], new EntityDefinition { id = "word", kind = "Text", token = "IS", cell = new GridCell(0, 1, 2) } };
            Assert.Throws<ArgumentException>(() => AuthoringOperations.PlaceSentence(level, new[] { "ROCK" }, new GridCell(0, 1, 2), true));
            Assert.AreEqual(2, level.entities.Length);
        }

        [Test]
        public void RecorderKeepsOnlyWorldCommandsAndUndoMatchesHistory()
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/L01.json");
            var level = LevelJsonSerializer.FromJson(File.ReadAllText(path));
            var recorder = new PlaytestRecorder(new GameSession(level));
            Assert.IsFalse(recorder.TryExecute("CAM+"));
            Assert.AreEqual(0, recorder.CommandCount);
            string first = level.referenceSolutions[0].commands[0];
            Assert.IsTrue(recorder.TryExecute(first));
            Assert.AreEqual(1, recorder.CommandCount);
            Assert.IsTrue(recorder.Undo());
            Assert.AreEqual(0, recorder.CommandCount);
            Assert.Throws<InvalidOperationException>(() => recorder.ToSolution("new", "new"));
            recorder.Restart();
            foreach (var command in level.referenceSolutions[0].commands)
                Assert.IsTrue(recorder.TryExecute(command), recorder.Session.LastRejectReason);
            Assert.IsTrue(recorder.Won);
            Assert.AreEqual(level.referenceSolutions[0].commands.Length, recorder.ToSolution("new", "new").commands.Length);
        }
    }
}
