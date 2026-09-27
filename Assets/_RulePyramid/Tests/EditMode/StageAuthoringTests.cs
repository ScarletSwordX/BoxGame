using System;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditMode
{
    public class StageAuthoringTests
    {
        static LevelDefinition Draft()
        {
            var level = new LevelDefinition
            {
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = "stage-test",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 2, 4) },
                terrain = new[] { new GridCellBox { id = "floor", appearance = "TransparentGlass",
                    min = new GridCell(0, 0, 0), max = new GridCell(4, 0, 4) } },
                entities = new[] { new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT", cell = new GridCell(1, 1, 1) } },
                referenceSolutions = Array.Empty<ReferenceSolutionData>()
            };
            return level;
        }

        static LevelEditSession LegacySession()
        {
            var level = Draft();
            StageAuthoring.EnsurePlan(level);
            return new LevelEditSession(level);
        }

        [Test]
        public void PaintOverwritesOnlyTargetVolumeAndReorderingUsesStableIds()
        {
            var level = Draft();
            var second = StageAuthoring.AddStage(level, "检验");
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(2, 0, 0), max = new GridCell(4, 2, 4) }, second);
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(3, 1, 1), max = new GridCell(3, 1, 1) }, null);
            Assert.AreEqual(second, StageAuthoring.StageAt(level, new GridCell(3, 0, 1)));
            Assert.AreEqual(level.stagePlan.stages[0].id, StageAuthoring.StageAt(level, new GridCell(3, 1, 1)));
            StageAuthoring.MoveStage(level, second, 0);
            Assert.AreEqual(second, StageAuthoring.StageAt(level, new GridCell(3, 0, 1)));
            StageAuthoring.RemoveStage(level, second, level.stagePlan.stages[1].id);
            Assert.AreEqual(1, level.stagePlan.stages.Length);
        }

        [Test]
        public void SessionSwitchesIndependentSnapshotsAndUndoRestoresSelectedStage()
        {
            var session = LegacySession();
            string first = session.ActiveStageId;
            string second = null;
            session.Edit("新增", d => second = StageAuthoring.AddStage(d, "变式"));
            session.SwitchStage(second);
            session.Edit("移动", d => d.entities[0].cell = new GridCell(2, 1, 2));
            session.SwitchStage(first);
            Assert.AreEqual(new GridCell(1, 1, 1), session.Draft.entities[0].cell);
            session.SwitchStage(second);
            Assert.AreEqual(new GridCell(2, 1, 2), session.Draft.entities[0].cell);
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(second, session.ActiveStageId);
            Assert.AreEqual(new GridCell(1, 1, 1), session.Draft.entities[0].cell);
        }

        [Test]
        public void RemovingFirstStageTransfersUnpaintedSpaceToChosenLaterStageAndUndoRestoresIt()
        {
            var session = LegacySession();
            var first = session.ActiveStageId;
            string second = null, third = null;
            session.Edit("新增阶段", d => second = StageAuthoring.AddStage(d, "检验"));
            session.Edit("新增阶段", d => third = StageAuthoring.AddStage(d, "变式"));
            session.Edit("绘制归属", d =>
            {
                StageAuthoring.PaintBox(d, new GridCellBox { min = new GridCell(1, 0, 1), max = new GridCell(2, 2, 2) }, second);
                StageAuthoring.PaintBox(d, new GridCellBox { min = new GridCell(4, 0, 4), max = new GridCell(4, 2, 4) }, third);
            });
            var secondRegionId = session.Draft.stagePlan.regions[0].id;
            var thirdRegionId = session.Draft.stagePlan.regions[1].id;
            Assert.AreEqual(first, StageAuthoring.StageAt(session.Draft, new GridCell(0, 1, 0)));
            session.Edit("删除首阶段", d => StageAuthoring.RemoveStage(d, first, third));
            Assert.AreEqual(second, session.ActiveStageId);
            Assert.AreEqual(secondRegionId, session.Draft.stagePlan.regions[0].id);
            Assert.AreEqual(thirdRegionId, session.Draft.stagePlan.regions[1].id);
            for (int x = 0; x <= 4; x++)
            for (int y = 0; y <= 2; y++)
            for (int z = 0; z <= 4; z++)
            {
                var cell = new GridCell(x, y, z);
                var expected = x >= 1 && x <= 2 && z >= 1 && z <= 2 ? second : third;
                Assert.AreEqual(expected, StageAuthoring.StageAt(session.Draft, cell), "归属错误：" + cell);
            }
            Assert.IsTrue(session.Undo());
            Assert.AreEqual(first, session.ActiveStageId);
            Assert.AreEqual(2, session.Draft.stagePlan.regions.Length);
            Assert.AreEqual(first, StageAuthoring.StageAt(session.Draft, new GridCell(0, 1, 0)));
            Assert.AreEqual(second, StageAuthoring.StageAt(session.Draft, new GridCell(1, 1, 1)));
            Assert.AreEqual(third, StageAuthoring.StageAt(session.Draft, new GridCell(4, 1, 4)));
        }

        [Test]
        public void ClosedStageCellsAreStoneAndCannotContainCurrentEntity()
        {
            var level = Draft();
            StageAuthoring.EnsurePlan(level);
            string first = level.stagePlan.stages[0].id;
            var second = StageAuthoring.AddStage(level, "检验");
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(2, 0, 0), max = new GridCell(4, 2, 4) }, second);
            var projection = StageAuthoring.Project(level, first);
            Assert.IsTrue(LevelCloner.ExpandTerrain(projection.terrain).Contains(new GridCell(4, 1, 4)));
            Assert.IsFalse(LevelCloner.ExpandTerrain(projection.terrain).Contains(new GridCell(1, 1, 1)));
            Assert.IsTrue(Array.Exists(projection.terrain, b => b.appearance == "TransparentGlass"));
            level.stagePlan.stages[0].entities[0].cell = new GridCell(4, 1, 4);
            Assert.IsTrue(Array.Exists(LevelValidator.ValidateStageAuthoring(level, first).Issues.ToArray(), i => i.Code == "STAGE_ENTITY_CLOSED"));
        }

        [Test]
        public void AirWallProjectsCumulativeRectanglesAndClipsSharedTerrain()
        {
            var level = Draft();
            level.terrain = new[]
            {
                new GridCellBox { id = "floor", appearance = "TransparentGlass",
                    min = new GridCell(0, 0, 0), max = new GridCell(4, 0, 4) },
                new GridCellBox { id = "edge", appearance = "Stone",
                    min = new GridCell(4, 1, 0), max = new GridCell(4, 2, 4) }
            };
            var second = StageAuthoring.AddStage(level, "检验");
            var third = StageAuthoring.AddStage(level, "变式");
            level.stagePlan.boundaryMode = "AirWall";
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(2, 0, 0), max = new GridCell(3, 2, 4) }, second);
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(4, 0, 0), max = new GridCell(4, 2, 4) }, third);

            var firstProjection = StageAuthoring.Project(level, level.stagePlan.stages[0].id);
            Assert.AreEqual(new GridCell(0, 0, 0), firstProjection.bounds.min);
            Assert.AreEqual(new GridCell(1, 2, 4), firstProjection.bounds.max);
            Assert.AreEqual(1, firstProjection.terrain.Length);
            Assert.AreEqual(new GridCell(1, 0, 4), firstProjection.terrain[0].max);
            Assert.IsNull(firstProjection.stagePlan);
            Assert.AreEqual(new GridCell(4, 2, 4), level.bounds.max);
            Assert.AreEqual(new GridCell(4, 0, 4), level.terrain[0].max);

            var secondProjection = StageAuthoring.Project(level, second);
            Assert.AreEqual(new GridCell(3, 2, 4), secondProjection.bounds.max);
            Assert.AreEqual(1, secondProjection.terrain.Length);
            var thirdProjection = StageAuthoring.Project(level, third);
            Assert.AreEqual(new GridCell(4, 2, 4), thirdProjection.bounds.max);
            Assert.AreEqual(2, thirdProjection.terrain.Length);
            Assert.AreEqual("AirWall", LevelCloner.Clone(level).stagePlan.boundaryMode);
        }

        [Test]
        public void AirWallRejectsNonRectangularAndEmptyOpenAreas()
        {
            var level = Draft();
            var second = StageAuthoring.AddStage(level, "检验");
            level.stagePlan.boundaryMode = "AirWall";
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(1, 0, 1), max = new GridCell(1, 2, 1) }, second);
            var first = level.stagePlan.stages[0].id;
            Assert.Throws<InvalidOperationException>(() => StageAuthoring.Project(level, first));
            Assert.IsTrue(Array.Exists(LevelValidator.ValidateStageAuthoring(level, first).Issues.ToArray(),
                issue => issue.Code == "STAGE_AIRWALL_SHAPE"));

            StageAuthoring.PaintBox(level, level.bounds, second);
            Assert.Throws<InvalidOperationException>(() => StageAuthoring.Project(level, first));
            Assert.IsTrue(Array.Exists(LevelValidator.ValidateStageAuthoring(level, first).Issues.ToArray(),
                issue => issue.Code == "STAGE_AIRWALL_SHAPE"));
        }

        [Test]
        public void SaveUsesFirstStageAtTopLevelAndPreservesOtherStages()
        {
            var session = LegacySession();
            string second = null;
            session.Edit("新增", d => second = StageAuthoring.AddStage(d, "检验"));
            session.SwitchStage(second);
            session.Edit("改实体", d => d.entities[0].cell = new GridCell(3, 1, 3));
            var json = JObject.Parse(LevelJsonSerializer.ToJson(session.Draft));
            Assert.AreEqual(1, json["entities"]?[0]?["cell"]?["x"]?.Value<int>());
            Assert.AreEqual(3, json["stagePlan"]?["stages"]?[1]?["entities"]?[0]?["cell"]?["x"]?.Value<int>());
            var reopened = LevelJsonSerializer.FromDraftJson(json.ToString());
            Assert.AreEqual(2, reopened.stagePlan.stages.Length);
            Assert.AreEqual(3, reopened.stagePlan.stages[1].entities[0].cell.x);
            Assert.Throws<InvalidOperationException>(() => LevelJsonSerializer.FromJson(json.ToString()),
                "运行时不能把多阶段草稿误当成单阶段完整地图");
            json["stagePlan"]["stages"][1]["futureNote"] = "保留";
            var withMetadata = LevelJsonSerializer.FromDraftJson(json.ToString());
            Assert.AreEqual("保留", JObject.Parse(LevelJsonSerializer.ToJson(withMetadata))["stagePlan"]?["stages"]?[1]?["futureNote"]?.Value<string>());
        }

        [Test]
        public void LegacyDraftWithoutStagePlanKeepsDirectTopLevelChanges()
        {
            var source = JObject.Parse(LevelJsonSerializer.ToJson(Draft()));
            Assert.IsNull(source["stagePlan"]);
            var loaded = LevelJsonSerializer.FromDraftJson(source.ToString());
            Assert.IsNull(loaded.stagePlan);
            loaded.entities[0].cell = new GridCell(2, 1, 2);
            var saved = JObject.Parse(LevelJsonSerializer.ToJson(loaded));
            Assert.IsNull(saved["stagePlan"]);
            Assert.AreEqual(2, saved["entities"]?[0]?["cell"]?["x"]?.Value<int>());
        }

        [Test]
        public void ShrinkChecksHiddenStageAndPaintedRegions()
        {
            var level = Draft();
            var second = StageAuthoring.AddStage(level, "检验");
            level.stagePlan.stages[1].entities[0].cell = new GridCell(4, 1, 4);
            level.entities = Array.Empty<EntityDefinition>();
            level.terrain = Array.Empty<GridCellBox>();
            Assert.Throws<ArgumentException>(() => MapResize.Resize(level, new GridCell(0, 0, 0), new GridCell(4, 3, 5)));
            level.stagePlan.stages[1].entities = Array.Empty<EntityDefinition>();
            StageAuthoring.PaintBox(level, new GridCellBox { min = new GridCell(4, 1, 1), max = new GridCell(4, 1, 1) }, second);
            Assert.Throws<ArgumentException>(() => MapResize.Resize(level, new GridCell(0, 0, 0), new GridCell(4, 3, 5)));
        }

        [Test]
        public void NewStageCanPlaytestBeforeRecordingRequiredReferenceSolution()
        {
            var path = Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/L01.json");
            var level = LevelJsonSerializer.FromJson(File.ReadAllText(path));
            level.designContract.minimumSolutionFamilies = 1;
            var session = new LevelEditSession(level);
            string second = null;
            session.Edit("新增阶段", d => second = StageAuthoring.AddStage(d, "录制前"));
            session.SwitchStage(second);
            Assert.AreEqual(0, session.Draft.referenceSolutions.Length);
            Assert.IsTrue(session.TryStartPlaytest(out var playtest, out var error), error);
            Assert.IsNotNull(playtest);
            session.StopPlaytest();
        }
    }
}
