#if UNITY_EDITOR
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RulePyramid.Tests.PlayMode
{
    public class CompletionPresentationTests
    {
        GameObject _host;
        GameBootstrap _game;
        UIDocument _document;
        PanelSettings _panel;
        LevelCatalog _catalog;
        TextAsset[] _maps;
        bool _hadSave;
        string _oldSave;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            _hadSave = PlayerPrefs.HasKey(CampaignProgress.LastCompletedLevelKey);
            _oldSave = PlayerPrefs.GetString(CampaignProgress.LastCompletedLevelKey, "");
            _maps = new[] { Map("P1"), Map("P2"), Map("NEXT") };
            _catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            _catalog.levels = new[] { _maps[0], _maps[2] };
            _catalog.stageSequences = new[] { new LevelStageSequence { id = "TEST", maps = new[] { _maps[0], _maps[1] } } };
            _host = new GameObject("阶段结算测试"); _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_RulePyramid/UIToolkit/PanelSettings.asset"));
            _document.panelSettings = _panel;
            _document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_RulePyramid/UIToolkit/Hud.uxml");
            var hud = _host.AddComponent<HudController>(); hud.document = _document;
            _game = _host.AddComponent<GameBootstrap>(); _game.hud = hud; _game.catalog = _catalog;
            _game.worldView = _host.AddComponent<WorldView>();
            _game.animator = _host.AddComponent<EventAnimator>(); _game.animator.worldView = _game.worldView;
            _game.animator.stepDuration = .1f; _game.stageExpansionDuration = .12f;
            _host.SetActive(true); yield return null; _game.StartGame();
        }

        static TextAsset Map(string id)
        {
            var map = new LevelDefinition
            {
                schemaVersion = 9, mechanicsVersion = "RW-v0.9", id = id, title = "Completion test",
                bounds = new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 3, 4) },
                terrain = new[] { new GridCellBox { min = new GridCell(0, 0, 0), max = new GridCell(4, 0, 4) } },
                entities = new[]
                {
                    new EntityDefinition { id = "robot", kind = "Object", subject = "ROBOT", cell = new GridCell(1, 1, 1) },
                    new EntityDefinition { id = "flag", kind = "Object", subject = "FLAG", cell = new GridCell(2, 1, 1) },
                    Word("rn", "ROBOT", 0, 3), Word("ri", "IS", 1, 3), Word("ry", "YOU", 2, 3),
                    Word("fn", "FLAG", 0, 4), Word("fi", "IS", 1, 4), Word("fw", "WIN", 2, 4)
                },
                options = new OptionsData
                {
                    actionMode = "MoveAutoPush", winMode = "YouAndWinSameCell", gravityMode = "WorldDownExceptHoverOrFly",
                    collisionMode = "SolidPairsTerrainUniversal", solidityMode = "YouPushStopOrText", supportMode = "StrictBelow",
                    controlMode = "SingleYouTransfer_NoControlUndo", bounceRiseCells = 3,
                    transformationMode = "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand",
                    ruleSourceMode = "WorldTextOnly", textMobilityMode = "AllWordsMovable_GeometryAccess"
                }
            };
            return new TextAsset(LevelJsonSerializer.ToJson(map));
        }
        static EntityDefinition Word(string id, string token, int x, int z) => new EntityDefinition
            { id = id, kind = "Text", token = token, cell = new GridCell(x, 1, z) };
        void Click(string id)
        {
            var b = _document.rootVisualElement.Q<Button>(id);
            using (var e = ClickEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        }
        IEnumerator Ready()
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (!_game.IsCompletionReady) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
            yield return null;
        }
        IEnumerator Advanced()
        {
            float deadline = Time.realtimeSinceStartup + 3f;
            while (_game.IsStageTransitioning) { Assert.Less(Time.realtimeSinceStartup, deadline); yield return null; }
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_hadSave) PlayerPrefs.SetString(CampaignProgress.LastCompletedLevelKey, _oldSave);
            else PlayerPrefs.DeleteKey(CampaignProgress.LastCompletedLevelKey);
            PlayerPrefs.Save();
            Object.Destroy(_host); Object.Destroy(_panel); Object.Destroy(_catalog);
            foreach (var map in _maps) Object.Destroy(map);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CompletionWaitsForAnimationAndClickThenStartsAnIndependentStage()
        {
            _game.Submit("E");
            Assert.IsTrue(_game.Session.Won); Assert.IsFalse(_game.IsCompletionReady);
            _game.ContinueStage(); Assert.IsFalse(_game.IsStageTransitioning);
            yield return Ready();
            var won = _game.Session; int turns = won.TurnCount;
            yield return new WaitForSeconds(.2f);
            Assert.AreSame(won, _game.Session); Assert.IsTrue(_game.IsStageComplete);
            Assert.IsFalse(_game.IsLevelComplete); Assert.IsFalse(_game.IsStageTransitioning);
            Assert.AreEqual(DisplayStyle.Flex, _document.rootVisualElement.Q("stageCompletePanel").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, _document.rootVisualElement.Q("winPanel").resolvedStyle.display);
            _game.Submit("E"); _game.NextLevel(); Assert.AreEqual(turns, won.TurnCount);
            Click("stageContinueButton"); Click("stageContinueButton");
            Assert.IsTrue(_game.IsStageTransitioning);
            _game.Undo(); _game.Restart(); Assert.AreSame(won, _game.Session);
            yield return Advanced();
            Assert.AreEqual(1, _game.CurrentStageIndex); Assert.AreEqual(0, _game.Session.TurnCount);
            Assert.IsFalse(_game.Session.Undo()); Assert.IsFalse(_game.IsCompletionReady);
            _game.Submit("E"); yield return Ready();
            Assert.IsTrue(_game.IsLevelComplete); Assert.IsFalse(_game.IsStageComplete);
            Assert.AreEqual("Level complete", _document.rootVisualElement.Q<Label>("levelCompleteTitle").text);
            Click("nextButton"); Assert.AreEqual("NEXT", _game.Session.Level.id);
            _game.Submit("E"); yield return Ready();
            Assert.AreEqual("Campaign complete", _document.rootVisualElement.Q<Label>("levelCompleteTitle").text);
            Assert.AreEqual(DisplayStyle.None, _document.rootVisualElement.Q("nextButton").resolvedStyle.display);
        }

        [UnityTest]
        public IEnumerator CompletionUndoRestartPauseAndRecreatedDocumentRemainUsable()
        {
            _game.Submit("E"); yield return Ready(); _game.Undo();
            Assert.IsFalse(_game.Session.Won); Assert.AreEqual(0, _game.Session.TurnCount);
            _game.Submit("E"); yield return Ready(); _game.Restart();
            Assert.IsFalse(_game.Session.Won); Assert.AreEqual(0, _game.CurrentStageIndex);
            _game.Submit("E"); yield return Ready(); _game.TogglePause(); _game.ContinueStage();
            Assert.IsFalse(_game.IsStageTransitioning); _game.ResumeGame();
            _game.enabled = false; yield return null; _game.enabled = true; yield return null;
            Assert.AreEqual(0, _game.CurrentStageIndex); Assert.IsTrue(_game.IsStageComplete);
            _document.enabled = false; yield return null; _document.enabled = true; yield return null; yield return null;
            Click("stageContinueButton"); yield return Advanced(); Assert.AreEqual(1, _game.CurrentStageIndex);
        }

        [UnityTest]
        public IEnumerator FailedStageLoadPreservesCompletionAndCanRetry()
        {
            _game.Submit("E"); yield return Ready(); var won = _game.Session;
            var bad = new TextAsset("{}"); _catalog.stageSequences[0].maps[1] = bad;
            LogAssert.Expect(LogType.Error, new Regex("Could not load the next stage"));
            _game.ContinueStage(); Assert.AreSame(won, _game.Session);
            Assert.IsTrue(_game.IsStageComplete); Assert.IsFalse(_game.IsStageTransitioning);
            Assert.IsTrue(_document.rootVisualElement.Q<Button>("stageContinueButton").enabledSelf);
            _catalog.stageSequences[0].maps[1] = _maps[1]; Object.Destroy(bad);
            Click("stageContinueButton"); yield return Advanced(); Assert.AreEqual(1, _game.CurrentStageIndex);
        }
    }
}
#endif
