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

        [UnityTest]
        public IEnumerator AddedTerrainRisesBesideVisibleOldFloorThenNextStateRebuilds()
        {
            var host = new GameObject("StageExpansionViewTests");
            var view = host.AddComponent<WorldView>();
            var old = World(3, 1);
            var next = World(5, 3);
            try
            {
                view.Rebuild(old);
                Assert.IsTrue(view.TryGetView("robot", out var oldRobot));
                var expansion = view.ExpandTo(next, 0.18f);
                Assert.IsTrue(expansion.MoveNext());
                Assert.IsNotNull(host.transform.Find("Terrain floor"));
                Assert.IsTrue(host.transform.Find("Terrain floor").gameObject.activeSelf);
                Assert.IsFalse(oldRobot.gameObject.activeSelf);
                Assert.IsNotNull(host.transform.Find("Expanded Terrain (3,0,0)"));
                Assert.IsNull(host.transform.Find("Expanded Terrain (4,0,0)"),
                    "The outer ring should appear after the inner ring.");

                int frames = 0;
                while (expansion.MoveNext())
                {
                    Assert.Less(++frames, 120, "Expansion should finish within its allotted duration.");
                    yield return expansion.Current;
                }
                yield return null;
                Assert.IsTrue(view.TryGetView("robot", out var newRobot));
                Assert.AreEqual(new Vector3(3, 1, 1), newRobot.position);
                Assert.IsTrue(newRobot.gameObject.activeSelf);
                Assert.IsNull(host.transform.Find("Expanded Terrain (3,0,0)"));
                Assert.AreEqual(new Vector3(5, 1, 3), host.transform.Find("Terrain floor").localScale);
            }
            finally
            {
                Object.Destroy(host);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CancelledExpansionRestoresPreviousStageAndDeletesTemporaryTiles()
        {
            var host = new GameObject("StageExpansionCancelTests");
            var view = host.AddComponent<WorldView>();
            try
            {
                view.Rebuild(World(3, 1));
                int generation = view.Generation;
                var expansion = view.ExpandTo(World(5, 3), 1f);
                Assert.IsTrue(expansion.MoveNext());
                Assert.IsNotNull(host.transform.Find("Expanded Terrain (3,0,0)"));
                view.CancelExpansion();
                Assert.IsFalse(expansion.MoveNext());
                Assert.AreEqual(generation, view.Generation);
                Assert.IsTrue(view.TryGetView("robot", out var robot));
                Assert.IsTrue(robot.gameObject.activeSelf);
                Assert.AreEqual(new Vector3(1, 1, 1), robot.position);
                yield return null;
                Assert.IsNull(host.transform.Find("Expanded Terrain (3,0,0)"));
                Assert.IsNotNull(host.transform.Find("Terrain floor"));
            }
            finally
            {
                Object.Destroy(host);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoAddedTerrainStillRebuildsTheNextAuthoredStage()
        {
            var host = new GameObject("StageExpansionNoDeltaTests");
            var view = host.AddComponent<WorldView>();
            try
            {
                view.Rebuild(World(4, 1));
                int generation = view.Generation;
                Assert.IsFalse(view.ExpandTo(World(4, 3), 1f).MoveNext());
                Assert.AreEqual(generation + 1, view.Generation);
                Assert.IsTrue(view.TryGetView("robot", out var robot));
                Assert.AreEqual(new Vector3(3, 1, 1), robot.position);
            }
            finally
            {
                Object.Destroy(host);
            }
            yield return null;
        }
    }
}
