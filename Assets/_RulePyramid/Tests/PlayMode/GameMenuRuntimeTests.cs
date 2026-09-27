#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using RulePyramid.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace RulePyramid.Tests.PlayMode
{
    public class GameMenuRuntimeTests
    {
        GameObject _host;
        GameBootstrap _game;
        UIDocument _document;
        PanelSettings _panel;
        float _originalScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _originalScale = Time.timeScale;
            _host = new GameObject("菜单测试");
            _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _panel = Object.Instantiate(AssetDatabase.LoadAssetAtPath<PanelSettings>(
                "Assets/_RulePyramid/UIToolkit/PanelSettings.asset"));
            _document.panelSettings = _panel;
            _document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/_RulePyramid/UIToolkit/Hud.uxml");
            var hud = _host.AddComponent<HudController>();
            hud.document = _document;
            _game = _host.AddComponent<GameBootstrap>();
            _game.hud = hud;
            _game.catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(
                "Assets/_RulePyramid/Config/LevelCatalog.asset");
            _game.worldView = _host.AddComponent<WorldView>();
            _game.animator = _host.AddComponent<EventAnimator>();
            _game.animator.worldView = _game.worldView;
            _game.stageWinPause = 0.05f;
            _game.stageExpansionDuration = 0.5f;
            _host.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_host);
            Object.Destroy(_panel);
            yield return null;
            Time.timeScale = _originalScale;
        }

        void Click(string name)
        {
            var button = _document.rootVisualElement.Q<Button>(name);
            Assert.IsNotNull(button);
            using (var evt = ClickEvent.GetPooled())
            {
                evt.target = button;
                button.SendEvent(evt);
            }
        }

        [UnityTest]
        public IEnumerator RecreatedDocumentKeepsWorkingButtonsAndEnglishCampaignText()
        {
            _document.enabled = false;
            yield return null;
            _document.enabled = true;
            yield return null;
            yield return null;
            Click("startButton");
            Assert.IsFalse(_game.IsMainMenu);
            for (int level = 0; level < _game.catalog.Count; level++)
            for (int stage = 0; stage < _game.catalog.StageCount(level); stage++)
            {
                var map = _game.catalog.LoadStage(level, stage);
                AssertEnglish(PlayerText.English(map.title));
                AssertEnglish(PlayerText.English(map.tutorial?.objective));
                AssertEnglish(PlayerText.English(map.tutorial?.concept));
                if (map.tutorial?.hints != null)
                    foreach (var hint in map.tutorial.hints) AssertEnglish(PlayerText.English(hint));
                if (map.tutorial?.regions != null)
                    foreach (var region in map.tutorial.regions) AssertEnglish(PlayerText.English(region.text));
            }
            AssertEnglish(PlayerText.English(RulePyramid.Core.GlobalStatusHint.NoControlText));
            _document.rootVisualElement.Query<TextElement>().ForEach(element => AssertEnglish(element.text));
        }

        static void AssertEnglish(string text)
        {
            Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text ?? "", "[\\u3400-\\u9fff]"), text);
        }

        [UnityTest]
        public IEnumerator TitleWaitsForStartAndPauseBlocksAllGameActions()
        {
            Assert.IsTrue(_game.IsMainMenu);
            Assert.IsNull(_game.Session);
            _game.Submit("E");
            Assert.IsNull(_game.Session);
            Click("startButton");
            yield return null;
            Assert.IsFalse(_game.IsMainMenu);
            Assert.AreEqual("L1P1", _game.Session.Level.id);
            _game.Submit(_game.Session.Level.referenceSolutions[0].commands[0]);
            int turns = _game.Session.TurnCount;
            Assert.Greater(turns, 0);
            var session = _game.Session;
            var cell = session.World.FindYou().Cell;
            Click("pauseButton");
            Assert.IsTrue(_game.IsPaused);
            Assert.AreEqual(0f, Time.timeScale);
            _game.Submit("E");
            _game.Undo();
            _game.Restart();
            _game.NextLevel();
            _game.LoadIndex(1);
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.AreSame(session, _game.Session);
            Assert.AreEqual(turns, session.TurnCount);
            Assert.AreEqual(cell, session.World.FindYou().Cell);
            Assert.AreEqual(DisplayStyle.Flex, _document.rootVisualElement.Q("pauseMenu").resolvedStyle.display);
            Assert.AreEqual(DisplayStyle.None, _document.rootVisualElement.Q("gameHud").resolvedStyle.display);
            Click("resumeButton");
            Assert.IsFalse(_game.IsPaused);
            Assert.AreEqual(_originalScale, Time.timeScale);
            _game.Undo();
            Assert.AreEqual(turns - 1, session.TurnCount);
        }

        [UnityTest]
        public IEnumerator PauseFreezesMovementAnimationAndRestoresPreviousTimeScaleOnDisable()
        {
            Click("startButton");
            _game.animator.stepDuration = 0.5f;
            _game.Submit(_game.Session.Level.referenceSolutions[0].commands[0]);
            yield return null;
            Assert.IsTrue(_game.animator.IsPlaying);
            Assert.IsTrue(_game.worldView.TryGetView("robot_01", out var actor));
            Time.timeScale = 0.75f;
            _game.TogglePause();
            var position = actor.position;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(position, actor.position);
            _game.enabled = false;
            Assert.AreEqual(0.75f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator PauseDuringStageExpansionPreservesMapUntilResume()
        {
            Click("startButton");
            _game.animator.stepDuration = 0.001f;
            foreach (var command in _game.Session.Level.referenceSolutions[0].commands)
                _game.Submit(command);
            Assert.IsTrue(_game.IsStageTransitioning);
            yield return new WaitForSecondsRealtime(0.15f);
            _game.TogglePause();
            var session = _game.Session;
            int generation = _game.worldView.Generation;
            var transforms = _game.worldView.GetComponentsInChildren<Transform>();
            var positions = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++) positions[i] = transforms[i].position;
            yield return new WaitForSecondsRealtime(0.7f);
            Assert.AreSame(session, _game.Session);
            Assert.IsTrue(_game.IsStageTransitioning);
            Assert.AreEqual(generation, _game.worldView.Generation);
            for (int i = 0; i < transforms.Length; i++) Assert.AreEqual(positions[i], transforms[i].position);
            _game.ResumeGame();
            float deadline = Time.realtimeSinceStartup + 4f;
            while (_game.IsStageTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsFalse(_game.IsStageTransitioning);
            Assert.AreEqual(1, _game.CurrentStageIndex);
            Assert.AreEqual(0, _game.Session.TurnCount);
        }
    }
}
#endif
