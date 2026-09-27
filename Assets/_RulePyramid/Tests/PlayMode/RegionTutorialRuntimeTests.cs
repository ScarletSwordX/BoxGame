using System;
using System.Collections;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace RulePyramid.Tests.PlayMode
{
    public class RegionTutorialRuntimeTests
    {
        static LevelDefinition Level(GridCell playerCell, GridCell triggerCell, float duration)
        {
            return new LevelDefinition
            {
                schemaVersion = 9,
                mechanicsVersion = "RW-v0.9",
                id = "tutorial-runtime",
                title = "提示运行测试",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(5, 5, 5) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(5, 0, 5) } },
                entities = new[]
                {
                    new EntityDefinition { id = "player", kind = "Object", subject = "ROBOT", cell = playerCell },
                    new EntityDefinition { id = "subject", kind = "Text", token = "ROBOT", cell = new GridCell(0, 1, 0) },
                    new EntityDefinition { id = "is", kind = "Text", token = "IS", cell = new GridCell(1, 1, 0) },
                    new EntityDefinition { id = "you", kind = "Text", token = "YOU", cell = new GridCell(2, 1, 0) }
                },
                options = new OptionsData
                {
                    actionMode = "MoveClimbHoldPush",
                    winMode = "DistinctEntitiesSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly",
                    collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText",
                    supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo",
                    bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly",
                    textMobilityMode = "AllWordsMovable_GeometryAccess"
                },
                tutorial = new TutorialData
                {
                    regions = new[]
                    {
                        new RegionTutorialData
                        {
                            id = "trigger", name = "测试区域", text = "进入区域",
                            bounds = new GridCellBox { min = triggerCell, max = triggerCell },
                            durationSeconds = duration, enabled = true
                        }
                    }
                }
            };
        }

        static GameBootstrap CreateBootstrap(LevelDefinition level, out GameObject host,
            out LevelCatalog catalog, out TextAsset asset)
        {
            asset = new TextAsset(LevelJsonSerializer.ToJson(level));
            catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { asset };
            host = new GameObject("RegionTutorialRuntimeTests");
            host.SetActive(false);
            var bootstrap = host.AddComponent<GameBootstrap>();
            bootstrap.catalog = catalog;
            host.SetActive(true);
            return bootstrap;
        }

        [UnityTest]
        public IEnumerator InitialRegionShowsImmediatelyExpiresAndResetsOnlyOnFreshLoad()
        {
            GameObject host = null;
            LevelCatalog catalog = null;
            TextAsset asset = null;
            try
            {
                var initial = new GridCell(1, 1, 1);
                var bootstrap = CreateBootstrap(Level(initial, initial, 0.5f), out host, out catalog, out asset);
                yield return null;

                Assert.AreEqual("trigger", bootstrap.RegionTutorial.Current?.id);
                Assert.AreEqual(RegionTutorialState.Showing, bootstrap.RegionTutorial.StateAt(0));

                yield return new WaitForSecondsRealtime(0.7f);
                Assert.IsNull(bootstrap.RegionTutorial.Current);
                Assert.AreEqual(RegionTutorialState.Shown, bootstrap.RegionTutorial.StateAt(0));

                bootstrap.Submit("E");
                Assert.AreEqual(1, bootstrap.Session.TurnCount);
                bootstrap.Undo();
                Assert.AreEqual(initial, bootstrap.Session.World.FindYou().Cell);
                Assert.IsNull(bootstrap.RegionTutorial.Current);
                bootstrap.Restart();
                Assert.IsNull(bootstrap.RegionTutorial.Current);
                Assert.AreEqual(RegionTutorialState.Shown, bootstrap.RegionTutorial.StateAt(0));

                bootstrap.LoadIndex(0);
                Assert.AreEqual("trigger", bootstrap.RegionTutorial.Current?.id);
                Assert.AreEqual(RegionTutorialState.Showing, bootstrap.RegionTutorial.StateAt(0));
            }
            finally
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                if (catalog != null) UnityEngine.Object.Destroy(catalog);
                if (asset != null) UnityEngine.Object.Destroy(asset);
            }
        }

        [UnityTest]
        public IEnumerator BootstrapSubmitTriggersRegionPassedDuringAutomaticFall()
        {
            GameObject host = null;
            LevelCatalog catalog = null;
            TextAsset asset = null;
            try
            {
                var midway = new GridCell(2, 2, 1);
                var bootstrap = CreateBootstrap(Level(new GridCell(1, 3, 1), midway, 3f), out host, out catalog, out asset);
                yield return null;
                Assert.IsNull(bootstrap.RegionTutorial.Current);

                bootstrap.Submit("E");
                Assert.AreEqual(new GridCell(2, 1, 1), bootstrap.Session.World.FindYou().Cell);
                Assert.AreEqual("trigger", bootstrap.RegionTutorial.Current?.id);
                Assert.AreEqual(RegionTutorialState.Showing, bootstrap.RegionTutorial.StateAt(0));

                bootstrap.Undo();
                Assert.AreEqual(new GridCell(1, 3, 1), bootstrap.Session.World.FindYou().Cell);
                Assert.AreEqual("trigger", bootstrap.RegionTutorial.Current?.id);
                bootstrap.Restart();
                Assert.AreEqual("trigger", bootstrap.RegionTutorial.Current?.id);
            }
            finally
            {
                if (host != null) UnityEngine.Object.Destroy(host);
                if (catalog != null) UnityEngine.Object.Destroy(catalog);
                if (asset != null) UnityEngine.Object.Destroy(asset);
            }
        }
    }
}
