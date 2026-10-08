using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace RulePyramid.Tests.PlayMode
{
    public class StagedCampaignRuntimeTests
    {
        [UnityTest]
        public IEnumerator AllRealChaptersPlayEveryStageWithoutLeakingState()
        {
            var manifest = JsonUtility.FromJson<LevelCatalogManifest>(File.ReadAllText(
                Path.Combine(Application.dataPath, "_RulePyramid/Content/Levels/catalog.json")));
            var maps = manifest.stageSequences.Select(sequence => sequence.maps.Select(path =>
                new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "_RulePyramid/Content", path)))).ToArray()).ToArray();
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = maps.Select(sequence => sequence[0]).ToArray();
            catalog.stageSequences = maps.Select((sequence, index) => new LevelStageSequence
                { id = manifest.stageSequences[index].id, maps = sequence }).ToArray();
            var host = new GameObject("StagedCampaignRuntimeTests");
            host.SetActive(false);
            var view = host.AddComponent<WorldView>();
            var bootstrap = host.AddComponent<GameBootstrap>();
            bootstrap.catalog = catalog;
            bootstrap.worldView = view;
            bootstrap.stageWinPause = 0.01f;
            bootstrap.stageExpansionDuration = 0.02f;
            try
            {
                Assert.AreEqual(3, catalog.Count, "正式游戏包含三关分阶段地图");
                host.SetActive(true);
                yield return null;
                for (int chapter = 0; chapter < catalog.Count; chapter++)
                {
                    Assert.AreEqual(3, catalog.StageCount(chapter));
                    for (int stage = 0; stage < 3; stage++)
                    {
                        var expected = new GameSession(catalog.LoadStage(chapter, stage));
                        Assert.AreEqual("L" + (chapter + 1) + "P" + (stage + 1), bootstrap.Session.Level.id);
                        Assert.AreEqual(stage, bootstrap.CurrentStageIndex);
                        Assert.AreEqual(expected.World.Fingerprint(), bootstrap.Session.World.Fingerprint());
                        Assert.AreEqual(0, bootstrap.Session.TurnCount);
                        Assert.IsFalse(bootstrap.Session.Undo(), "阶段切换清空撤销历史");
                        if (chapter == 1 && stage == 2)
                        {
                            Assert.AreEqual(new GridCell(1, 1, 1), bootstrap.Session.World.Actor().Cell);
                            Assert.IsFalse(bootstrap.Session.World.Terrain.Contains(new GridCell(7, 3, 3)),
                                "P2 的绕行柱必须在 P3 删除，而不是只扩图叠加");
                        }
                        var commands = bootstrap.Session.Level.referenceSolutions[0].commands;
                        bootstrap.Submit(commands[0]);
                        bootstrap.Restart();
                        Assert.AreEqual(expected.World.Fingerprint(), bootstrap.Session.World.Fingerprint(), "重开只恢复当前阶段");
                        foreach (var command in commands)
                        {
                            int turns = bootstrap.Session.TurnCount;
                            bootstrap.Submit(command);
                            Assert.AreEqual(turns + 1, bootstrap.Session.TurnCount, command + " 被拒绝");
                        }
                        Assert.IsTrue(bootstrap.Session.Won);
                        if (stage < 2)
                        {
                            var old = bootstrap.Session;
                            int generation = view.Generation;
                            bootstrap.ContinueStage();
                Assert.IsTrue(bootstrap.IsStageTransitioning);
                            bootstrap.Submit("E"); bootstrap.Undo(); bootstrap.Restart(); bootstrap.NextLevel();
                            Assert.AreSame(old, bootstrap.Session, "过渡锁定防止重复推进或改写残局");
                            float deadline = Time.realtimeSinceStartup + 5f;
                            while (bootstrap.IsStageTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                            Assert.IsFalse(bootstrap.IsStageTransitioning, "阶段切换超时");
                            Assert.Greater(view.Generation, generation);
                            Assert.AreNotSame(old, bootstrap.Session);
                        }
                        else
                        {
                            Assert.IsFalse(bootstrap.HasNextStage);
                            Assert.IsFalse(bootstrap.IsStageTransitioning);
                            Assert.AreEqual(chapter + 1 < catalog.Count, bootstrap.HasNextLevel);
                            if (chapter == 1) Assert.AreEqual("rock_01", bootstrap.Session.World.WinRecord.YouId);
                        }
                    }
                    bootstrap.NextLevel();
                }
                Assert.AreEqual("L3P3", bootstrap.Session.Level.id, "完成最后一关不循环回旧关卡");
                Assert.IsTrue(bootstrap.Session.Won);
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(catalog);
                foreach (var sequence in maps) foreach (var map in sequence) Object.Destroy(map);
            }
        }
    }
}
