using System;
using System.Linq;
using NUnit.Framework;
using RulePyramid.Editor;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RulePyramid.Tests.EditorPreview
{
    public class CampaignCatalogTests
    {
        [Test]
        public void ProductionAssetAndManifestLoadAllThreeStagedChapters()
        {
            var manifest = PrototypeSetup.ReadCatalogManifest();
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/_RulePyramid/Config/LevelCatalog.asset");
            Assert.AreEqual(3, catalog.Count);
            Assert.AreEqual(3, manifest.levels.Length);
            for (int chapter = 0; chapter < 3; chapter++)
            {
                Assert.AreEqual(3, catalog.StageCount(chapter));
                Assert.AreEqual(chapter, catalog.IndexOf("L" + (chapter + 1).ToString("00")));
                for (int stage = 0; stage < 3; stage++)
                {
                    var id = "L" + (chapter + 1) + "P" + (stage + 1);
                    Assert.AreEqual(id, catalog.LoadStage(chapter, stage).id);
                    Assert.AreEqual(chapter, catalog.IndexOf(id));
                    StringAssert.EndsWith(manifest.stageSequences[chapter].maps[stage],
                        AssetDatabase.GetAssetPath(catalog.stageSequences[chapter].maps[stage]));
                }
            }
            Assert.AreEqual(2, catalog.IndexOf("L03"));
            Assert.AreEqual(-1, catalog.IndexOf("L04"));
        }

        [Test]
        public void SecondChapterFinalWinOpensThirdChapterFirstStage()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/_RulePyramid/Config/LevelCatalog.asset");
            var host = new GameObject("正式目录章节跳转测试");
            host.SetActive(false);
            try
            {
                var game = host.AddComponent<GameBootstrap>();
                game.catalog = catalog;
                game.LoadIndex(catalog.IndexOf("L02"));
                var activate = typeof(GameBootstrap).GetMethod("ActivateStage",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var finalStage = new RulePyramid.Core.GameSession(catalog.LoadStage(1, 2));
                activate.Invoke(game, new object[] { finalStage, 2, false });
                Assert.AreEqual("L2P3", game.Session.Level.id);
                Assert.IsFalse(game.HasNextStage);
                Assert.IsTrue(game.HasNextLevel);
                game.NextLevel();
                Assert.AreSame(finalStage, game.Session, "未获胜不能提前跳关");

                // 只构造胜利后的导航状态；此测试不声称验证 L2P3 解法。
                finalStage.World.WonLatched = true;
                game.NextLevel();
                Assert.AreEqual("L3P1", game.Session.Level.id);
                Assert.AreEqual(0, game.CurrentStageIndex);
                Assert.AreEqual(3, game.CurrentStageCount);
                Assert.IsTrue(game.HasNextStage);
                Assert.IsFalse(game.HasNextLevel);
                Assert.AreEqual(new RulePyramid.Core.GameSession(catalog.LoadStage(2, 0)).World.Fingerprint(),
                    game.Session.World.Fingerprint());
                Assert.IsFalse(game.Session.Undo(), "跨章节不继承撤销历史");

                var last = new RulePyramid.Core.GameSession(catalog.LoadStage(2, 2));
                activate.Invoke(game, new object[] { last, 2, false });
                last.World.WonLatched = true;
                game.NextLevel();
                Assert.AreSame(last, game.Session, "第三关结束不循环或跳进旧关卡");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ImportUsesVariableSequenceLengthsAndRejectsMissingMapsAtomically()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            try
            {
                var manifest = PrototypeSetup.ReadCatalogManifest();
                manifest.stageSequences[1].maps = manifest.stageSequences[1].maps.Take(2).ToArray();
                PrototypeSetup.ApplyCatalogManifest(catalog, manifest);
                Assert.AreEqual(3, catalog.StageCount(0));
                Assert.AreEqual(2, catalog.StageCount(1));
                var before = catalog.levels;
                var beforeSequences = catalog.stageSequences;
                manifest.stageSequences[1].maps[1] = "LevelDrafts/DoesNotExist.json";
                Assert.Throws<InvalidOperationException>(() => PrototypeSetup.ApplyCatalogManifest(catalog, manifest));
                Assert.AreSame(before, catalog.levels);
                Assert.AreSame(beforeSequences, catalog.stageSequences);
            }
            finally { Object.DestroyImmediate(catalog); }
        }
    }
}
