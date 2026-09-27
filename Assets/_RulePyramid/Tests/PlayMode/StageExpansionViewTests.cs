using System.Collections;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class StageExpansionViewTests
    {
        static WorldModel World(int floorWidth, int robotX)
        {
            return WorldModel.FromLevel(new LevelDefinition
            {
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                id = "expansion-test",
                bounds = new GridCellBox
                {
                    min = new GridCell(0, 0, 0),
                    max = new GridCell(floorWidth - 1, 1, 2)
                },
                terrain = new[]
                {
                    new GridCellBox { id = "floor", min = new GridCell(0, 0, 0),
                        max = new GridCell(floorWidth - 1, 0, 2) }
                },
                entities = new[]
                {
                    new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT",
                        cell = new GridCell(robotX, 1, 1) },
                    new EntityDefinition { id = "noun", kind = "Text", token = "ROBOT",
                        cell = new GridCell(0, 1, 2) },
                    new EntityDefinition { id = "is", kind = "Text", token = "IS",
                        cell = new GridCell(1, 1, 2) },
                    new EntityDefinition { id = "you", kind = "Text", token = "YOU",
                        cell = new GridCell(2, 1, 2) }
                }
            });
        }

        [Test]
        public void LayoutUsesUpperRightAnchorAndTracksRemovalAndAppearanceChanges()
        {
            var old = World(5, 1);
            var next = World(3, 1);
            var layout = StageTransitionLayout.Between(old, next);
            Assert.AreEqual(new GridCell(2, 0, 0), layout.NextOffset);
            Assert.AreEqual(9, layout.Retained.Count);
            Assert.AreEqual(6, layout.Removed.Count);
            Assert.AreEqual(0, layout.Added.Count);
            next.Spec.terrain[0].appearance = "TransparentGlass";
            layout = StageTransitionLayout.Between(old, next);
            Assert.AreEqual(0, layout.Retained.Count);
            Assert.AreEqual(15, layout.Removed.Count);
            Assert.AreEqual(9, layout.Added.Count);
            Assert.IsTrue(layout.Added.TrueForAll(s => s.Glass));
        }

        [UnityTest]
        public IEnumerator SameTerrainStillAnimatesEntityRearrangementAndSupportsLateCancellation()
        {
            var host = new GameObject("阶段重布测试");
            var view = host.AddComponent<WorldView>();
            try
            {
                var old = World(4, 1);
                view.Rebuild(old);
                var transition = view.ExpandTo(World(4, 3), .25f);
                Assert.IsTrue(transition.MoveNext(), "同地形也必须展示词牌与角色重布，不能跳切");
                float deadline = Time.realtimeSinceStartup + 3f;
                while (!view.TryGetView("robot", out var current) || current.position.x != 3f)
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline);
                    yield return null;
                    Assert.IsTrue(transition.MoveNext());
                }
                view.CancelExpansion();
                Assert.IsFalse(transition.MoveNext());
                Assert.IsTrue(view.TryGetView("robot", out var restored));
                Assert.AreEqual(new Vector3(1, 1, 1), restored.position);
                Assert.IsTrue(restored.gameObject.activeSelf);
                Assert.Greater(restored.localScale.x, 0f);
            }
            finally { Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemovedTerrainRetractsAndCancellationRestoresTheCompleteOldMap()
        {
            var host = new GameObject("地形收起测试");
            var view = host.AddComponent<WorldView>();
            try
            {
                view.Rebuild(World(5, 1));
                var transition = view.ExpandTo(World(3, 1), .3f);
                Assert.IsTrue(transition.MoveNext());
                Assert.IsFalse(host.transform.Find("Terrain floor").gameObject.activeSelf);
                var removed = host.transform.Find("Removed Terrain (0,0,0)");
                Assert.IsNotNull(removed);
                float deadline = Time.realtimeSinceStartup + 3f;
                while (removed.position.y == 0f && removed.gameObject.activeSelf)
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline);
                    yield return null;
                    Assert.IsTrue(transition.MoveNext());
                }
                Assert.Less(removed.position.y, 0f);
                view.CancelExpansion();
                Assert.IsFalse(transition.MoveNext());
                yield return null;
                Assert.IsNull(host.transform.Find("Removed Terrain (0,0,0)"));
                Assert.AreEqual(new Vector3(5, 1, 3), host.transform.Find("Terrain floor").localScale);
            }
            finally { Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AllSixProductionTransitionsLoadExactNextInitialState()
        {
            for (int chapter = 1; chapter <= 3; chapter++)
            for (int stage = 1; stage <= 2; stage++)
            {
                var firstPath = System.IO.Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/L" + chapter + "P" + stage + ".json");
                var nextPath = System.IO.Path.Combine(Application.dataPath, "_RulePyramid/Content/LevelDrafts/L" + chapter + "P" + (stage + 1) + ".json");
                var maps = new[] { new TextAsset(System.IO.File.ReadAllText(firstPath)), new TextAsset(System.IO.File.ReadAllText(nextPath)) };
                var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
                catalog.levels = new[] { maps[0] };
                catalog.stageSequences = new[] { new LevelStageSequence { id = "过场测试", maps = maps } };
                var host = new GameObject("L" + chapter + " P" + stage + " 过场");
                host.SetActive(false);
                var view = host.AddComponent<WorldView>();
                var game = host.AddComponent<GameBootstrap>();
                var lensHost = new GameObject("过场镜头");
                var lens = lensHost.AddComponent<Camera>();
                lens.aspect = 16f / 9f;
                var camera = lensHost.AddComponent<CameraSlotsController>();
                game.catalog = catalog; game.worldView = view; game.cameraSlots = camera;
                game.stageWinPause = 0f; game.stageExpansionDuration = .12f;
                try
                {
                    host.SetActive(true);
                    yield return null;
                    var previous = game.Session;
                    var next = new GameSession(catalog.LoadStage(0, 1));
                    // 只测试阶段管理与展示，不用失效的旧参考路线冒充解法验证。
                    previous.World.WonLatched = true;
                    game.NextLevel();
                    Assert.IsTrue(game.IsStageTransitioning, firstPath);
                    game.Submit("E"); game.Undo(); game.Restart(); game.NextLevel();
                    Assert.AreSame(previous, game.Session);
                    float deadline = Time.realtimeSinceStartup + 4f;
                    while (game.IsStageTransitioning)
                    {
                        Assert.Less(Time.realtimeSinceStartup, deadline, firstPath);
                        yield return null;
                    }
                    Assert.AreEqual(next.World.Fingerprint(), game.Session.World.Fingerprint(), nextPath);
                    Assert.AreEqual(0, game.Session.TurnCount);
                    Assert.IsFalse(game.Session.Undo());
                    foreach (var entity in next.World.Entities)
                    {
                        Assert.IsTrue(view.TryGetView(entity.Id, out var rendered), entity.Id);
                        Assert.AreEqual(GridMap.ToWorld(entity.Cell, null), rendered.position);
                        Assert.IsTrue(rendered.gameObject.activeSelf);
                    }
                    game.Restart();
                    Assert.AreEqual(next.World.Fingerprint(), game.Session.World.Fingerprint());
                    Assert.AreEqual(1, game.CurrentStageIndex);
                }
                finally
                {
                    Object.Destroy(host); Object.Destroy(lensHost); Object.Destroy(catalog);
                    foreach (var map in maps) Object.Destroy(map);
                }
                yield return null;
            }
        }

    }
}
