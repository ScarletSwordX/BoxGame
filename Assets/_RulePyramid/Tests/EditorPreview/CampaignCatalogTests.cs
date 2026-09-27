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
        public void ProductionAssetAndManifestOnlyLoadTheTwoStagedChapters()
        {
            var manifest = PrototypeSetup.ReadCatalogManifest();
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/_RulePyramid/Config/LevelCatalog.asset");
            Assert.AreEqual(2, catalog.Count);
            Assert.AreEqual(2, manifest.levels.Length);
            for (int chapter = 0; chapter < 2; chapter++)
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
            Assert.AreEqual(-1, catalog.IndexOf("L03"));
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
