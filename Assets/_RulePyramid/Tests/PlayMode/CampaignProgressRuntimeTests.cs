#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RulePyramid.Tests.PlayMode
{
    public class CampaignProgressRuntimeTests
    {
        sealed class MenuFixture
        {
            public GameObject Host;
            public PanelSettings Panel;
            public UIDocument Document;
            public GameBootstrap Game;

            public void Dispose()
            {
                if (Host != null) Object.Destroy(Host);
                if (Panel != null) Object.Destroy(Panel);
            }

            public void Click(string id)
            {
                var button = Document.rootVisualElement.Q<Button>(id);
                Assert.IsNotNull(button);
                using (var evt = ClickEvent.GetPooled())
                {
                    evt.target = button;
                    button.SendEvent(evt);
                }
            }
        }

        static LevelDefinition Map(string id)
        {
            return new LevelDefinition
            {
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = id, title = id,
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 3, 4) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 0, 4) } },
                entities = new[]
                {
                    new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT", cell = new GridCell(1, 1, 1) },
                    new EntityDefinition { id = "flag", kind = "Object", subject = "FLAG", cell = new GridCell(2, 1, 1) },
                    Word("robot_noun", "ROBOT", 0, 3), Word("robot_is", "IS", 1, 3),
                    Word("robot_you", "YOU", 2, 3), Word("flag_noun", "FLAG", 0, 4),
                    Word("flag_is", "IS", 1, 4), Word("flag_win", "WIN", 2, 4)
                },
                options = new OptionsData
                {
                    actionMode = "MoveAutoPush", winMode = "YouAndWinSameCell",
                    gravityMode = "WorldDownExceptHoverOrFly", collisionMode = "SolidPairsTerrainUniversal",
                    solidityMode = "YouPushStopOrText", supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo", bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly", textMobilityMode = "AllWordsMovable_GeometryAccess"
                }
            };
        }

        static EntityDefinition Word(string id, string token, int x, int z)
        {
            return new EntityDefinition { id = id, kind = "Text", token = token, cell = new GridCell(x, 1, z) };
        }

        static MenuFixture Create(LevelCatalog catalog)
        {
            var fixture = new MenuFixture { Host = new GameObject("存档菜单测试") };
            fixture.Host.SetActive(false);
            fixture.Document = fixture.Host.AddComponent<UIDocument>();
            fixture.Panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_RulePyramid/UIToolkit/PanelSettings.asset"));
            fixture.Document.panelSettings = fixture.Panel;
            fixture.Document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/_RulePyramid/UIToolkit/Hud.uxml");
            var hud = fixture.Host.AddComponent<HudController>();
            hud.document = fixture.Document;
            fixture.Game = fixture.Host.AddComponent<GameBootstrap>();
            fixture.Game.hud = hud;
            fixture.Game.catalog = catalog;
            fixture.Game.stageWinPause = 0f;
            fixture.Game.stageExpansionDuration = 0f;
            fixture.Host.SetActive(true);
            return fixture;
        }

        static bool ContinueVisible(MenuFixture fixture)
        {
            return fixture.Document.rootVisualElement.Q<Button>("continueButton").style.display.value == DisplayStyle.Flex;
        }

        [UnityTest]
        public IEnumerator WinningFinalStagePersistsProgressAndContinueLoadsTheNextLevel()
        {
            const string key = CampaignProgress.LastCompletedLevelKey;
            bool hadSave = PlayerPrefs.HasKey(key);
            string oldSave = PlayerPrefs.GetString(key, "");
            PlayerPrefs.DeleteKey(key);
            var maps = new[]
            {
                new TextAsset(LevelJsonSerializer.ToJson(Map("L1P1"))),
                new TextAsset(LevelJsonSerializer.ToJson(Map("L1P2"))),
                new TextAsset(LevelJsonSerializer.ToJson(Map("L2P1"))),
                new TextAsset(LevelJsonSerializer.ToJson(Map("L3P1")))
            };
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { maps[0], maps[2], maps[3] };
            catalog.stageSequences = new[] { new LevelStageSequence { id = "L01", maps = new[] { maps[0], maps[1] } } };
            MenuFixture fixture = null;
            try
            {
                fixture = Create(catalog);
                yield return null;
                Assert.IsTrue(fixture.Game.IsMainMenu);
                Assert.IsFalse(ContinueVisible(fixture));
                fixture.Click("startButton");
                Assert.AreEqual("L1P1", fixture.Game.Session.Level.id);
                fixture.Game.Submit("E");
                Assert.IsTrue(fixture.Game.Session.Won);
                Assert.IsFalse(PlayerPrefs.HasKey(key), "完成中间阶段不应写入关卡存档");
                fixture.Click("stageContinueButton");
                float deadline = Time.realtimeSinceStartup + 3f;
                while (fixture.Game.IsStageTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual("L1P2", fixture.Game.Session.Level.id);
                fixture.Game.Submit("E");
                Assert.IsTrue(fixture.Game.Session.Won);
                Assert.AreEqual("L1P1", PlayerPrefs.GetString(key));
                fixture.Dispose();
                fixture = null;
                yield return null;

                fixture = Create(catalog);
                yield return null;
                Assert.IsTrue(ContinueVisible(fixture));
                fixture.Click("startButton");
                Assert.AreEqual("L1P1", fixture.Game.Session.Level.id, "Play 仍从首关开始");
                fixture.Dispose();
                fixture = null;
                yield return null;

                fixture = Create(catalog);
                yield return null;
                fixture.Click("continueButton");
                Assert.AreEqual("L2P1", fixture.Game.Session.Level.id);
                fixture.Game.Submit("E");
                Assert.IsTrue(fixture.Game.Session.Won);
                Assert.AreEqual("L2P1", PlayerPrefs.GetString(key));
                CampaignProgress.RecordCompleted(catalog, 0);
                Assert.AreEqual("L2P1", PlayerPrefs.GetString(key), "重玩早期关卡不能倒退存档");
                fixture.Dispose();
                fixture = null;
                yield return null;

                fixture = Create(catalog);
                yield return null;
                fixture.Click("continueButton");
                Assert.AreEqual("L3P1", fixture.Game.Session.Level.id);
                fixture.Game.Submit("E");
                Assert.IsTrue(fixture.Game.Session.Won);
                Assert.AreEqual("L3P1", PlayerPrefs.GetString(key));
                fixture.Dispose();
                fixture = null;
                yield return null;

                fixture = Create(catalog);
                yield return null;
                Assert.IsFalse(ContinueVisible(fixture), "通关全部关卡后已无下一关");
            }
            finally
            {
                fixture?.Dispose();
                Object.Destroy(catalog);
                foreach (var map in maps) Object.Destroy(map);
                if (hadSave) PlayerPrefs.SetString(key, oldSave);
                else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
            yield return null;
        }
    }
}
#endif
