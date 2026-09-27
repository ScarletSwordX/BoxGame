using System;
using NUnit.Framework;
using RulePyramid.Core;

namespace RulePyramid.Tests.EditMode
{
    public class SelectionTransformTests
    {
        static LevelDefinition Fixture()
        {
            return new LevelDefinition
            {
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 3, 7) },
                terrain = new[]
                {
                    new GridCellBox { id = "floor", min = new GridCell(0, 0, 0), max = new GridCell(2, 0, 0), appearance = "stone" },
                    new GridCellBox { id = "mark", min = new GridCell(0, 0, 2), max = new GridCell(0, 0, 2), appearance = "grass" }
                },
                entities = new[]
                {
                    new EntityDefinition { id = "rock", kind = "Object", subject = "ROCK", cell = new GridCell(1, 1, 1) },
                    new EntityDefinition { id = "word", kind = "Text", token = "IS", cell = new GridCell(1, 1, 2) }
                }
            };
        }

        [Test]
        public void CopyMixedSelectionKeepsOriginalsAndAppearance()
        {
            var level = Fixture();
            var copied = SelectionTransform.Apply(level, new[] { "rock" },
                new[] { new GridCell(0, 0, 0), new GridCell(1, 0, 0) }, new GridCell(3, 0, 0), true);
            Assert.AreEqual(1, copied.Length);
            Assert.AreNotEqual("rock", copied[0]);
            Assert.AreEqual(new GridCell(1, 1, 1), level.entities[0].cell);
            Assert.AreEqual(new GridCell(4, 1, 1), level.entities[2].cell);
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(3, 0, 0)));
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(4, 0, 0)));
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(0, 0, 0)));
            Assert.LessOrEqual(level.terrain.Length, 4);
        }

        [Test]
        public void MovingOverSelectedSourceCellsIsAllowedAndCompacted()
        {
            var level = Fixture();
            SelectionTransform.Apply(level, Array.Empty<string>(),
                new[] { new GridCell(0, 0, 0), new GridCell(1, 0, 0), new GridCell(2, 0, 0) },
                new GridCell(1, 0, 0), false);
            Assert.IsNull(AppearanceAt(level, new GridCell(0, 0, 0)));
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(1, 0, 0)));
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(3, 0, 0)));
            Assert.AreEqual("grass", AppearanceAt(level, new GridCell(0, 0, 2)));
            Assert.AreEqual(2, level.terrain.Length);
        }

        [Test]
        public void InvalidMixedCopyLeavesBothArraysUntouched()
        {
            var level = Fixture();
            var entities = level.entities;
            var terrain = level.terrain;
            Assert.Throws<ArgumentException>(() => SelectionTransform.Apply(level, new[] { "word" },
                new[] { new GridCell(0, 0, 0) }, new GridCell(0, 0, 2), true));
            Assert.AreSame(entities, level.entities);
            Assert.AreSame(terrain, level.terrain);
            Assert.Throws<ArgumentOutOfRangeException>(() => SelectionTransform.Apply(level, new[] { "rock" },
                Array.Empty<GridCell>(), new GridCell(int.MaxValue, 0, 0), false));
            Assert.AreSame(entities, level.entities);
            Assert.AreSame(terrain, level.terrain);
        }

        [Test]
        public void CopyTerrainRejectsSameAppearanceTargetAtomically()
        {
            var level = Fixture();
            var terrain = level.terrain;
            Assert.Throws<ArgumentException>(() => SelectionTransform.Apply(level, Array.Empty<string>(),
                new[] { new GridCell(0, 0, 0), new GridCell(1, 0, 0) }, new GridCell(1, 0, 0), true));
            Assert.AreSame(terrain, level.terrain);
            Assert.AreEqual("stone", AppearanceAt(level, new GridCell(2, 0, 0)));
            Assert.AreEqual(2, level.terrain.Length);
        }

        [Test]
        public void ObjectMoveRejectsAnotherObjectAndKeepsDraft()
        {
            var level = Fixture();
            level.entities = new[]
            {
                level.entities[0], level.entities[1],
                new EntityDefinition { id = "flag", kind = "Object", subject = "FLAG", cell = new GridCell(2, 1, 1) }
            };
            var entities = level.entities;
            Assert.Throws<ArgumentException>(() => SelectionTransform.Apply(level, new[] { "rock" },
                Array.Empty<GridCell>(), new GridCell(1, 0, 0), false));
            Assert.AreSame(entities, level.entities);
            Assert.AreEqual(new GridCell(1, 1, 1), level.entities[0].cell);
        }

        [Test]
        public void TextConflictFailsAtomically()
        {
            var level = Fixture();
            var entities = level.entities;
            Assert.Throws<ArgumentException>(() => SelectionTransform.Apply(level, new[] { "word" },
                Array.Empty<GridCell>(), new GridCell(0, 0, -1), true));
            Assert.AreSame(entities, level.entities);
        }

        [Test]
        public void SessionUndoRestoresMixedMove()
        {
            var session = new LevelEditSession(Fixture());
            session.Edit("移动选择", draft => SelectionTransform.Apply(draft, new[] { "rock" },
                new[] { new GridCell(0, 0, 0) }, new GridCell(3, 0, 0), false));
            Assert.AreEqual(new GridCell(4, 1, 1), session.Draft.entities[0].cell);
            Assert.IsNull(AppearanceAt(session.Draft, new GridCell(0, 0, 0)));
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(new GridCell(1, 1, 1), session.Draft.entities[0].cell);
            Assert.AreEqual("stone", AppearanceAt(session.Draft, new GridCell(0, 0, 0)));
        }

        static string AppearanceAt(LevelDefinition level, GridCell cell)
        {
            foreach (var box in level.terrain)
                if (cell.x >= box.min.x && cell.x <= box.max.x
                    && cell.y >= box.min.y && cell.y <= box.max.y
                    && cell.z >= box.min.z && cell.z <= box.max.z) return box.appearance;
            return null;
        }
    }
}
