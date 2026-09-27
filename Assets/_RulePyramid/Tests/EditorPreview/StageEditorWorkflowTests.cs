using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using RulePyramid.Runtime;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class StageEditorWorkflowTests
    {
        const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        static LevelDefinition Blank() => (LevelDefinition)typeof(LevelEditorWindow).GetMethod("CreateEmpty", StaticFlags).Invoke(null, null);

        [Test]
        public void IndependentMapRoundTripEditsUndoAndPlaytestDoNotCreateStagePlan()
        {
            var session = new LevelEditSession(Blank());
            Assert.IsNull(session.Draft.stagePlan);
            session.Edit("命名阶段地图", d => d.id = "L1P1");
            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.Redo());
            var loaded = LevelJsonSerializer.FromJson(LevelJsonSerializer.ToJson(session.Draft));
            var reopened = new LevelEditSession(loaded);
            Assert.AreEqual("L1P1", reopened.Draft.id);
            Assert.IsNull(reopened.Draft.stagePlan);
            Assert.IsNull(reopened.ActiveStageId);
            Assert.IsTrue(reopened.TryStartPlaytest(out var game, out var error), error);
            Assert.IsNull(game.Level.stagePlan);
            reopened.StopPlaytest();
        }

        [Test]
        public void LegacyExportKeepsCurrentEditsAndContractWithoutChangingSource()
        {
            var level = Blank();
            StageAuthoring.EnsurePlan(level);
            string second = StageAuthoring.AddStage(level, "第二阶段");
            var session = new LevelEditSession(level);
            session.SwitchStage(second);
            session.Edit("重布", d =>
            {
                d.entities.First(e => e.id == "player").cell = new GridCell(4, 1, 3);
                d.tutorial.objective = "第二阶段提示";
                d.referenceSolutions = new[] { new ReferenceSolutionData { id = "route", commands = new[] { "N" } } };
            });
            var map = session.BuildStandaloneMap("L1P2");
            Assert.IsNull(map.stagePlan);
            Assert.AreEqual("L1P2", map.id);
            Assert.AreEqual(new GridCell(4, 1, 3), map.entities.First(e => e.id == "player").cell);
            Assert.AreEqual("第二阶段提示", map.tutorial.objective);
            Assert.AreEqual("route", map.referenceSolutions.Single().id);
            Assert.AreEqual(session.Draft.designContract.minimumSolutionFamilies, map.designContract.minimumSolutionFamilies);
            map.terrain[0].max.x++;
            Assert.AreNotEqual(map.terrain[0].max.x, session.Draft.terrain[0].max.x);
            Assert.AreEqual(2, session.Draft.stagePlan.stages.Length);
            Assert.AreEqual(second, session.ActiveStageId);
            Assert.IsTrue(session.Dirty);
        }

        [Test]
        public void LegacyAirWallExportPreservesBoundsAndWritesStandaloneUtf8CrLf()
        {
            var level = Blank();
            StageAuthoring.EnsurePlan(level);
            string second = StageAuthoring.AddStage(level, "扩展");
            level.stagePlan.boundaryMode = "AirWall";
            var region = LevelCloner.Clone(level).bounds;
            region.min.x = region.max.x;
            StageAuthoring.PaintBox(level, region, second);
            var session = new LevelEditSession(level);
            var map = session.BuildStandaloneMap("L1P1");
            Assert.AreEqual(level.bounds.max.x - 1, map.bounds.max.x);
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            try
            {
                typeof(LevelEditorWindow).GetMethod("WriteDraftFile", StaticFlags).Invoke(null, new object[] { path, map });
                byte[] bytes = File.ReadAllBytes(path);
                Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191);
                for (int i = 0; i < bytes.Length; i++)
                    if (bytes[i] == 10) Assert.IsTrue(i > 0 && bytes[i - 1] == 13);
                var reopened = new LevelEditSession(LevelJsonSerializer.FromDraftJson(File.ReadAllText(path)));
                Assert.IsNull(reopened.Draft.stagePlan);
                Assert.AreEqual(map.bounds.max, reopened.Draft.bounds.max);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
