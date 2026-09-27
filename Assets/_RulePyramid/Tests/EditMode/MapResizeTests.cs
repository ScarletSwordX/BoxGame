using System;
using NUnit.Framework;
using RulePyramid.Core;

namespace RulePyramid.Tests.EditMode
{
    public class MapResizeTests
    {
        static LevelDefinition Draft()
        {
            return new LevelDefinition
            {
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(7, 7, 7) },
                terrain = new[] { new GridCellBox { id = "floor", min = new GridCell(0, 0, 0), max = new GridCell(7, 0, 7) } },
                entities = new[] { new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT", cell = new GridCell(4, 1, 4) } },
                tutorial = new TutorialData
                {
                    regions = new[]
                    {
                        new RegionTutorialData { id = "tip", enabled = true, bounds = new GridCellBox { min = new GridCell(6, 1, 6), max = new GridCell(7, 2, 7) } }
                    }
                }
            };
        }

        [Test]
        public void FillFloorCovers64By64AtNegativeOriginAndIsUndoable()
        {
            var draft=Draft();
            MapResize.Resize(draft,new GridCell(-10,-2,-10),new GridCell(64,64,64));
            var session=new LevelEditSession(draft);
            session.Edit("铺地板",AuthoringOperations.FillFloor);
            var terrain=LevelCloner.ExpandTerrain(session.Draft.terrain);
            for(int x=-10;x<=53;x++)for(int z=-10;z<=53;z++)Assert.IsTrue(terrain.Contains(new GridCell(x,-2,z)));
            Assert.AreEqual(2,session.Draft.terrain.Length,"整片新增地板只需一个盒");
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(1,session.Draft.terrain.Length);
            Assert.IsTrue(session.Redo());
            int count=session.Draft.terrain.Length;
            AuthoringOperations.FillFloor(session.Draft);
            Assert.AreEqual(count,session.Draft.terrain.Length,"重复填充不能重复叠地形");
        }

        [Test]
        public void FillFloorPreservesExistingAppearanceAndRejectsEntityAtomically()
        {
            var draft=Draft();
            draft.terrain[0].max=new GridCell(2,0,2);
            draft.terrain[0].appearance="TransparentGlass";
            AuthoringOperations.FillFloor(draft);
            Assert.AreEqual("TransparentGlass",draft.terrain[0].appearance);
            Assert.AreEqual(64,LevelCloner.ExpandTerrain(draft.terrain).Count);
            var blocked=Draft();
            blocked.entities[0].cell=new GridCell(4,0,4);
            var before=blocked.terrain;
            Assert.That(()=>AuthoringOperations.FillFloor(blocked),Throws.ArgumentException.With.Message.Contains("robot"));
            Assert.AreSame(before,blocked.terrain);
        }

        [Test]
        public void SupportsExactly64CellsPerAxisAndRejects65()
        {
            var draft = Draft();
            MapResize.Resize(draft, new GridCell(-10, -10, -10), new GridCell(64, 64, 64));
            Assert.AreEqual(new GridCell(53, 53, 53), draft.bounds.max);
            Assert.IsTrue(MapResize.TryValidateBounds(draft.bounds, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapResize.Resize(draft, new GridCell(-10, -10, -10), new GridCell(65, 64, 64)));
            Assert.AreEqual(new GridCell(53, 53, 53), draft.bounds.max);
            draft.bounds.max.x++;
            Assert.IsFalse(MapResize.TryValidateBounds(draft.bounds, out _));
            Assert.IsTrue(LevelValidator.ValidateStructure(draft).HasStructureErrors);
        }

        [Test]
        public void CannotShrinkAcrossEntityTerrainOrTutorialRegion()
        {
            var draft = Draft();
            var original = draft.bounds.max;
            Assert.That(() => MapResize.Resize(draft, new GridCell(0, 0, 0), new GridCell(4, 8, 8)), Throws.ArgumentException.With.Message.Contains("robot"));
            Assert.That(() => MapResize.Resize(draft, new GridCell(0, 0, 0), new GridCell(8, 8, 7)), Throws.ArgumentException.With.Message.Contains("floor"));
            draft.terrain = Array.Empty<GridCellBox>();
            Assert.That(() => MapResize.Resize(draft, new GridCell(0, 0, 0), new GridCell(8, 8, 7)), Throws.ArgumentException.With.Message.Contains("tip"));
            Assert.AreEqual(original, draft.bounds.max);
        }

        [Test]
        public void ResizeIsOneUndoableSessionEditAndFailureKeepsHistory()
        {
            var session = new LevelEditSession(Draft());
            Assert.Throws<ArgumentException>(() => session.Edit("缩小", d => MapResize.Resize(d, new GridCell(0, 0, 0), new GridCell(4, 4, 4))));
            Assert.IsFalse(session.CanUndo);
            Assert.IsFalse(session.Dirty);
            session.Edit("扩大", d => MapResize.Resize(d, new GridCell(0, 0, 0), new GridCell(16, 16, 16)));
            Assert.AreEqual(new GridCell(15, 15, 15), session.Draft.bounds.max);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(new GridCell(7, 7, 7), session.Draft.bounds.max);
            Assert.IsTrue(session.Redo());
            Assert.AreEqual(new GridCell(15, 15, 15), session.Draft.bounds.max);
        }

        [Test]
        public void InvalidOrOutOfBoundsTerrainIsRejectedBeforeExpansion()
        {
            var draft = Draft();
            draft.terrain = new[] { new GridCellBox { id = "giant", min = new GridCell(int.MinValue, 0, 0), max = new GridCell(int.MaxValue, 0, 0) } };
            var report = LevelValidator.ValidateStructure(draft);
            Assert.That(report.Issues, Has.Some.Matches<ValidationIssue>(i => i.Code == "TERRAIN_BOUNDS"));
            draft.terrain[0].min = new GridCell(7, 0, 0);
            draft.terrain[0].max = new GridCell(6, 0, 0);
            report = LevelValidator.ValidateStructure(draft);
            Assert.That(report.Issues, Has.Some.Matches<ValidationIssue>(i => i.Code == "TERRAIN_BOUNDS"));
        }

        [Test]
        public void TerrainExpansionAtIntegerMaximumTerminates()
        {
            var cell = new GridCell(int.MaxValue, int.MaxValue, int.MaxValue);
            var cells = LevelCloner.ExpandTerrain(new[] { new GridCellBox { min = cell, max = cell } });
            Assert.AreEqual(1, cells.Count);
            Assert.IsTrue(cells.Contains(cell));
            Assert.Throws<ArgumentException>(() => LevelCloner.ExpandTerrain(new[] { new GridCellBox { min = new GridCell(0,0,0), max = new GridCell(64,0,0) } }));
        }

        [Test]
        public void RejectsCoordinateOverflowWithoutMutation()
        {
            var draft = Draft();
            Assert.Throws<ArgumentOutOfRangeException>(() => MapResize.Resize(draft, new GridCell(int.MaxValue, 0, 0), new GridCell(2, 1, 1)));
            Assert.AreEqual(new GridCell(7, 7, 7), draft.bounds.max);
        }
    }
}
