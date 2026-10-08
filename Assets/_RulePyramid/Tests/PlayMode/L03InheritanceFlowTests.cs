using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RulePyramid.Tests.PlayMode
{
    public class L03InheritanceFlowTests
    {
        [UnityTest]
        public IEnumerator ThreeDraftStagesAdvanceWithIndependentStateAndSharedFinalTerrain()
        {
            return RunStages(new[] { "L3P1", "L3P2", "L3P3" });
        }

        [UnityTest]
        public IEnumerator RuleFocusedStagesAdvanceWithIndependentStateAndSharedTerrain()
        {
            return RunStages(new[] { "L3P2", "L3P3" });
        }

        IEnumerator RunStages(string[] names)
        {
            var maps = new TextAsset[names.Length];
            for (int i = 0; i < names.Length; i++)
                maps[i] = new TextAsset(File.ReadAllText(Path.Combine(
                    Application.dataPath, "_RulePyramid/Content/LevelDrafts", names[i] + ".json")));

            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { maps[0] };
            catalog.stageSequences = new[] { new LevelStageSequence { id = "L03", maps = maps } };
            var host = new GameObject("L03InheritanceFlowTests");
            host.SetActive(false);
            var view = host.AddComponent<WorldView>();
            var bootstrap = host.AddComponent<GameBootstrap>();
            bootstrap.catalog = catalog;
            bootstrap.worldView = view;
            bootstrap.stageWinPause = 0f;
            bootstrap.stageExpansionDuration = 0.15f;

            try
            {
                host.SetActive(true);
                yield return null;
                Assert.AreEqual(1, catalog.Count);
                Assert.AreEqual(names.Length, catalog.StageCount(0));
                HashSet<GridCell> secondStageTerrain = null;

                for (int stage = 0; stage < names.Length; stage++)
                {
                    var expected = new GameSession(catalog.LoadStage(0, stage));
                    Assert.AreEqual(names[stage], bootstrap.Session.Level.id);
                    Assert.AreEqual(stage, bootstrap.CurrentStageIndex);
                    Assert.AreEqual(expected.World.Fingerprint(), bootstrap.Session.World.Fingerprint(),
                        names[stage] + " must load its authored initial state");
                    Assert.AreEqual(0, bootstrap.Session.TurnCount);
                    Assert.IsFalse(bootstrap.Session.Undo(), "A new stage must have no inherited undo history");

                    if (names[stage] == "L3P2")
                        secondStageTerrain = new HashSet<GridCell>(bootstrap.Session.World.Terrain);
                    if (names[stage] == "L3P3")
                    {
                        Assert.IsNotNull(secondStageTerrain);
                        Assert.AreEqual(secondStageTerrain.Count, bootstrap.Session.World.Terrain.Count);
                        foreach (var cell in secondStageTerrain)
                            Assert.IsTrue(bootstrap.Session.World.Terrain.Contains(cell),
                                "P3 must retain P2 terrain at " + cell);
                    }

                    var commands = bootstrap.Session.Level.referenceSolutions[0].commands;
                    bootstrap.Submit(commands[0]);
                    Assert.AreEqual(1, bootstrap.Session.TurnCount);
                    bootstrap.Restart();
                    Assert.AreEqual(stage, bootstrap.CurrentStageIndex, "Restart stays in the current stage");
                    Assert.AreEqual(expected.World.Fingerprint(), bootstrap.Session.World.Fingerprint());
                    Assert.AreEqual(0, bootstrap.Session.TurnCount);
                    Assert.IsFalse(bootstrap.Session.Undo());

                    foreach (var command in commands)
                    {
                        int turns = bootstrap.Session.TurnCount;
                        bootstrap.Submit(command);
                        Assert.AreEqual(turns + 1, bootstrap.Session.TurnCount,
                            names[stage] + " rejected command " + command);
                    }
                    Assert.IsTrue(bootstrap.Session.Won, names[stage] + " reference route did not win");

                    if (stage < names.Length - 1)
                    {
                        var previous = bootstrap.Session;
                        int generation = view.Generation;
                        bootstrap.ContinueStage();
                Assert.IsTrue(bootstrap.IsStageTransitioning);
                        float deadline = Time.realtimeSinceStartup + 5f;
                        while (bootstrap.IsStageTransitioning && Time.realtimeSinceStartup < deadline)
                            yield return null;
                        Assert.IsFalse(bootstrap.IsStageTransitioning, "L03 stage transition timed out");
                        Assert.AreNotSame(previous, bootstrap.Session);
                        Assert.Greater(view.Generation, generation,
                            "The existing WorldView must rebuild for the new stage");
                    }
                }

                Assert.IsFalse(bootstrap.HasNextStage);
                Assert.IsFalse(bootstrap.HasNextLevel);
                Assert.IsFalse(bootstrap.IsStageTransitioning);
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(catalog);
                foreach (var map in maps) Object.Destroy(map);
            }
        }
    }
}
