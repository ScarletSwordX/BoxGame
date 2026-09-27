using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class StageProgressionRuntimeTests
    {
        sealed class Fixture
        {
            public GameObject Host;
            public LevelCatalog Catalog;
            public TextAsset[] Maps;
            public GameBootstrap Bootstrap;
            public WorldView View;

            public void Dispose()
            {
                if (Host != null) Object.Destroy(Host);
                if (Catalog != null) Object.Destroy(Catalog);
                if (Maps != null)
                    foreach (var map in Maps)
                        if (map != null) Object.Destroy(map);
            }
        }

        static TextAsset Map(int stage)
        {
            var path = Path.Combine(Application.dataPath,
                "_RulePyramid/Content/LevelDrafts/L1P" + stage + ".json");
            Assert.IsTrue(File.Exists(path), "缺少运行阶段地图：" + path);
            return new TextAsset(File.ReadAllText(path));
        }

        static Fixture Create(bool withNextChapter = false)
        {
            var fixture = new Fixture();
            fixture.Maps = new[] { Map(1), Map(2), Map(3) };
            fixture.Catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            fixture.Catalog.levels = withNextChapter
                ? new[] { fixture.Maps[0], fixture.Maps[1] }
                : new[] { fixture.Maps[0] };
            fixture.Catalog.stageSequences = new[]
            {
                new LevelStageSequence { id = "L01", maps = fixture.Maps }
            };
            fixture.Host = new GameObject("StageProgressionRuntimeTests");
            fixture.Host.SetActive(false);
            var viewHost = new GameObject("Stage WorldView");
            viewHost.transform.SetParent(fixture.Host.transform, false);
            fixture.View = viewHost.AddComponent<WorldView>();
            fixture.Bootstrap = fixture.Host.AddComponent<GameBootstrap>();
            fixture.Bootstrap.catalog = fixture.Catalog;
            fixture.Bootstrap.worldView = fixture.View;
            fixture.Bootstrap.stageWinPause = 0.03f;
            fixture.Bootstrap.stageExpansionDuration = 0.03f;
            fixture.Host.SetActive(true);
            return fixture;
        }

        static ReferenceSolutionData Solution(GameSession session, string id)
        {
            var solution = session.Level.referenceSolutions.Single(item => item.id == id);
            Assert.IsNotNull(solution.commands);
            return solution;
        }

        static void SubmitSolution(GameBootstrap bootstrap, string id)
        {
            foreach (var command in Solution(bootstrap.Session, id).commands)
            {
                int before = bootstrap.Session.TurnCount;
                bootstrap.Submit(command);
                Assert.AreEqual(before + 1, bootstrap.Session.TurnCount,
                    bootstrap.Session.Level.id + "/" + id + " rejected " + command);
            }
            Assert.IsTrue(bootstrap.Session.Won, id);
        }

        static IEnumerator WaitForStage(GameBootstrap bootstrap, int stage)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while ((bootstrap.CurrentStageIndex != stage || bootstrap.IsStageTransitioning) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(stage, bootstrap.CurrentStageIndex, "阶段推进超时");
            Assert.IsFalse(bootstrap.IsStageTransitioning);
        }

        [Test]
        public void CatalogTreatsAStageSequenceAsOneChapterAndKeepsSingleMapFallback()
        {
            var maps = new[] { Map(1), Map(2), Map(3) };
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            try
            {
                catalog.levels = new[] { maps[0], maps[1] };
                catalog.stageSequences = new[]
                {
                    new LevelStageSequence { id = "L01", maps = maps }
                };
                Assert.AreEqual(2, catalog.Count);
                Assert.AreEqual(3, catalog.StageCount(0));
                Assert.AreEqual(1, catalog.StageCount(1));
                Assert.AreEqual("L1P1", catalog.Load(0).id);
                Assert.AreEqual("L1P2", catalog.LoadStage(0, 1).id);
                Assert.AreEqual("L1P3", catalog.LoadStage(0, 2).id);
                Assert.AreEqual("L1P2", catalog.Load(1).id);
                Assert.AreEqual(0, catalog.IndexOf("L01"));
                Assert.AreEqual(0, catalog.IndexOf("L1P1"));
                Assert.AreEqual(0, catalog.IndexOf("L1P2"));
                Assert.AreEqual(0, catalog.IndexOf("L1P3"));
                Assert.AreEqual(-1, catalog.IndexOf("missing"));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                foreach (var map in maps) Object.DestroyImmediate(map);
            }
        }

        [UnityTest]
        public IEnumerator StandardSolutionsAdvanceTwiceAndFinalWinOpensNextChapter()
        {
            var fixture = Create(true);
            try
            {
                yield return null;
                var bootstrap = fixture.Bootstrap;
                Assert.AreEqual("L1P1", bootstrap.Session.Level.id);
                Assert.AreEqual(0, bootstrap.CurrentStageIndex);
                Assert.AreEqual(3, bootstrap.CurrentStageCount);
                Assert.IsTrue(bootstrap.HasNextStage);
                Assert.AreEqual("p1_move", bootstrap.RegionTutorial.Current?.id);
                Assert.IsTrue(fixture.View.TryGetView("robot_01", out _));
                int firstViewGeneration = fixture.View.Generation;

                SubmitSolution(bootstrap, "P1-A");
                var first = bootstrap.Session;
                Assert.IsTrue(bootstrap.IsStageTransitioning);
                int firstTurns = first.TurnCount;
                bootstrap.Submit("E");
                bootstrap.Undo();
                bootstrap.Restart();
                bootstrap.NextLevel();
                Assert.AreSame(first, bootstrap.Session, "过渡期间输入不可改动当前胜利状态");
                Assert.AreEqual(firstTurns, first.TurnCount);
                Assert.IsTrue(first.Won);
                yield return WaitForStage(bootstrap, 1);
                Assert.AreEqual("L1P2", bootstrap.Session.Level.id);
                Assert.IsNull(bootstrap.RegionTutorial.Current, "P2 不沿用 P1 的移动提示");
                Assert.Greater(fixture.View.Generation, firstViewGeneration,
                    "下一阶段应完成地图视图扩展与实体重布");
                Assert.IsTrue(fixture.View.TryGetView("robot_01", out _));
                Assert.AreNotSame(first, bootstrap.Session);
                Assert.AreEqual(0, bootstrap.Session.TurnCount);
                Assert.IsFalse(bootstrap.Session.Undo(), "下一阶段不得继承上一阶段的撤销历史");
                Assert.AreEqual(49, LevelCloner.ExpandTerrain(bootstrap.Session.Level.terrain).Count);

                var secondStart = bootstrap.Session.World.FindYou().Cell;
                bootstrap.Submit(Solution(bootstrap.Session, "P2-A").commands[0]);
                Assert.AreEqual(1, bootstrap.Session.TurnCount);
                bootstrap.Restart();
                Assert.AreEqual(1, bootstrap.CurrentStageIndex, "重开只重开本阶段");
                Assert.AreEqual(secondStart, bootstrap.Session.World.FindYou().Cell);
                Assert.AreEqual(0, bootstrap.Session.TurnCount);
                SubmitSolution(bootstrap, "P2-A");
                var second = bootstrap.Session;
                Assert.IsTrue(bootstrap.IsStageTransitioning);
                yield return WaitForStage(bootstrap, 2);
                Assert.AreEqual("L1P3", bootstrap.Session.Level.id);
                Assert.IsNull(bootstrap.RegionTutorial.Current, "P3 不沿用上一阶段提示");
                Assert.IsTrue(fixture.View.TryGetView("robot_01", out _));
                Assert.AreNotSame(second, bootstrap.Session);
                Assert.AreEqual(0, bootstrap.Session.TurnCount);
                Assert.IsFalse(bootstrap.Session.Undo());
                Assert.AreEqual(120, LevelCloner.ExpandTerrain(bootstrap.Session.Level.terrain).Count);
                Assert.AreEqual(fixture.Catalog.LoadStage(0, 2).entities.Single(e => e.id == "robot_01").cell,
                    bootstrap.Session.World.FindYou().Cell, "新阶段从固定出生点开始");

                SubmitSolution(bootstrap, "P3-A");
                Assert.IsFalse(bootstrap.HasNextStage);
                Assert.IsFalse(bootstrap.IsStageTransitioning);
                Assert.AreEqual(2, bootstrap.CurrentStageIndex);
                Assert.AreEqual("wall_7_7", bootstrap.Session.World.WinRecord.WinId);
                bootstrap.NextLevel();
                Assert.AreEqual("L1P2", bootstrap.Session.Level.id, "末阶段胜利后进入下一章节");
                Assert.AreEqual(0, bootstrap.CurrentStageIndex);
                Assert.AreEqual(1, bootstrap.CurrentStageCount);
            }
            finally { fixture.Dispose(); }
        }

        [UnityTest]
        public IEnumerator AlternativeFinalSolutionWinsAndManualLoadCancelsAnInFlightTransition()
        {
            var fixture = Create();
            try
            {
                yield return null;
                var bootstrap = fixture.Bootstrap;
                SubmitSolution(bootstrap, "P1-A");
                Assert.IsTrue(bootstrap.IsStageTransitioning);
                bootstrap.LoadIndex(0);
                Assert.IsFalse(bootstrap.IsStageTransitioning);
                Assert.AreEqual("L1P1", bootstrap.Session.Level.id);
                Assert.AreEqual(0, bootstrap.Session.TurnCount);
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.AreEqual(0, bootstrap.CurrentStageIndex,
                    "取消的协程不得在新地图加载后继续推进");

                SubmitSolution(bootstrap, "P1-A");
                yield return WaitForStage(bootstrap, 1);
                SubmitSolution(bootstrap, "P2-A");
                yield return WaitForStage(bootstrap, 2);
                SubmitSolution(bootstrap, "P3-B");
                Assert.AreEqual("goal", bootstrap.Session.World.WinRecord.WinId);
                Assert.AreEqual(2, bootstrap.CurrentStageIndex);
                Assert.IsFalse(bootstrap.HasNextStage);
                Assert.IsFalse(bootstrap.IsStageTransitioning);
            }
            finally { fixture.Dispose(); }
        }
    }
}
