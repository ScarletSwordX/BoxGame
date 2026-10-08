using System.Collections.Generic;
using System.Text;
using RulePyramid.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace RulePyramid.Runtime
{
    public class HudController : MonoBehaviour
    {
        public UIDocument document;
        Label _title;
        Label _phase;
        Label _turns;
        Label _rules;
        Label _objective;
        Label _hint;
        Label _reject;
        VisualElement _winPanel;
        VisualElement _root, _stageTransition;
        Button _hintButton;
        int _hintIndex;
        string _manualHint;
        GameBootstrap _bootstrap;
        public bool HasMenus => _root?.Q("mainMenu") != null;

        public void Bind(GameBootstrap bootstrap)
        {
            if (document == null) document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;
            if (_bootstrap == bootstrap && _root == root) return;
            _bootstrap = bootstrap;
            _root = root;
            _stageTransition = root.Q("stageTransition");
            _title = root.Q<Label>("levelTitle");
            _phase = root.Q<Label>("phaseLabel");
            _turns = root.Q<Label>("turnLabel");
            _rules = root.Q<Label>("rulesLabel");
            _objective = root.Q<Label>("objectiveLabel");
            _hint = root.Q<Label>("hintLabel");
            _reject = root.Q<Label>("rejectLabel");
            _winPanel = root.Q("winPanel");
            _hintButton = root.Q<Button>("hintButton");
            _hintButton?.RegisterCallback<ClickEvent>(_ => ShowNextHint());
            root.Q<Button>("undoButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Undo());
            root.Q<Button>("restartButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Restart());
            root.Q<Button>("winUndoButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Undo());
            root.Q<Button>("winRestartButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Restart());
            root.Q<Button>("stageContinueButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.ContinueStage());
            root.Q<Button>("nextButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.NextLevel());
            root.Q<Button>("startButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.StartGame());
            root.Q<Button>("continueButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.ContinueGame());
            root.Q<Button>("quitButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.QuitGame());
            root.Q<Button>("pauseButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.TogglePause());
            root.Q<Button>("resumeButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.ResumeGame());
            root.Q<Button>("pauseQuitButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.QuitGame());
            // 游戏中空格用于跳跃，避免已点击的 HUD 按钮再次响应空格。
            foreach (var id in new[] { "undoButton", "restartButton", "hintButton", "pauseButton",
                "winUndoButton", "winRestartButton", "nextButton", "stageContinueButton" })
            {
                var button = root.Q<Button>(id);
                if (button != null) button.focusable = false;
            }
        }

        void LateUpdate()
        {
            // UIDocument 重新启用或编辑器重建面板后，旧元素及其回调已失效。
            if (_bootstrap == null || document == null || document.rootVisualElement == _root) return;
            Bind(_bootstrap);
            Refresh(_bootstrap.Session, null, _bootstrap.RegionTutorial);
            RefreshMenus();
        }

        public void RefreshMenus()
        {
            if (_root == null || _bootstrap == null) return;
            SetVisible("mainMenu", _bootstrap.IsMainMenu);
            SetVisible("pauseMenu", _bootstrap.IsPaused);
            SetVisible("gameHud", !_bootstrap.IsMenuOpen);
            var start = _root.Q<Button>("startButton");
            start?.SetEnabled(_bootstrap.catalog != null && _bootstrap.catalog.Count > 0);
            SetVisible("continueButton", _bootstrap.IsMainMenu && _bootstrap.HasContinue);
            if (_bootstrap.IsMainMenu) start?.Focus();
            else if (_bootstrap.IsPaused)
            {
                var context = _root.Q<Label>("pauseContext");
                if (context != null) context.text = PlayerText.English(_bootstrap.Session.Level.title) + "  /  Stage "
                    + (_bootstrap.CurrentStageIndex + 1) + " / " + _bootstrap.CurrentStageCount;
                _root.Q<Button>("resumeButton")?.Focus();
            }
            else _root.focusController?.focusedElement?.Blur();
        }

        void SetVisible(string id, bool visible)
        {
            var element = _root.Q(id);
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Refresh(GameSession session, string reject, RegionTutorialSession regionTutorial = null)
        {
            if (session == null) return;
            if (_title != null) _title.text = PlayerText.English(session.Level.title) + "  (" + session.Level.id + ")";
            bool transitioning = _bootstrap != null && _bootstrap.IsStageTransitioning;
            if (_phase != null) _phase.text = _bootstrap != null && _bootstrap.CurrentStageCount > 1
                ? "Stage " + (_bootstrap.CurrentStageIndex + 1) + " / " + _bootstrap.CurrentStageCount
                : session.Phase.ToString();
            if (_stageTransition != null)
            {
                if (_stageTransition is Label transitionLabel && transitioning)
                    transitionLabel.text = "Stage " + (_bootstrap.CurrentStageIndex + 1) + " clear  ·  Stage " + (_bootstrap.CurrentStageIndex + 2);
                if (transitioning) _stageTransition.RemoveFromClassList("hidden");
                else _stageTransition.AddToClassList("hidden");
            }
            foreach (var id in new[] { "undoButton", "restartButton", "winUndoButton", "winRestartButton", "hintButton" })
                _root?.Q<Button>(id)?.SetEnabled(!transitioning);
            _root?.Q<Button>("nextButton")?.SetEnabled(!transitioning && (_bootstrap == null || _bootstrap.HasNextLevel));
            bool stageComplete = _bootstrap != null && _bootstrap.IsStageComplete;
            SetVisible("stageCompletePanel", stageComplete);
            foreach (var id in new[] { "top", "left", "bottom" }) SetVisible(id, !stageComplete);
            _root?.Q<Button>("stageContinueButton")?.SetEnabled(stageComplete);
            var completeTitle = _root?.Q<Label>("levelCompleteTitle");
            if (completeTitle != null) completeTitle.text = _bootstrap != null && !_bootstrap.HasNextLevel
                ? "Campaign complete" : "Level complete";
            SetVisible("nextButton", _bootstrap == null || _bootstrap.HasNextLevel);
            if (_turns != null) _turns.text = "Moves " + session.TurnCount;
            if (_rules != null) _rules.text = FormatRules(session.World.Rules);
            if (_objective != null && session.Level.tutorial != null)
                _objective.text = !string.IsNullOrEmpty(session.Level.tutorial.objective)
                    ? PlayerText.English(session.Level.tutorial.objective)
                    : PlayerText.English(session.Level.tutorial.concept);
            if (_reject != null) _reject.text = reject ?? "";
            if (_hintButton != null)
                _hintButton.style.display = (regionTutorial != null && regionTutorial.Count > 0)
                    || session.Level.tutorial?.hints == null || session.Level.tutorial.hints.Length == 0
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (_hint != null)
            {
                _hint.EnableInClassList("no-control", GlobalStatusHint.Current(session) != null);
                _hint.text = PlayerText.English(GlobalStatusHint.DisplayText(session, regionTutorial?.Current?.text, _manualHint));
            }
            if (_winPanel != null)
            {
                if (_bootstrap != null ? _bootstrap.IsLevelComplete : session.Won) _winPanel.RemoveFromClassList("hidden");
                else _winPanel.AddToClassList("hidden");
            }
        }

        public void ResetHints()
        {
            _hintIndex = 0;
            _manualHint = null;
            if (_hint != null) _hint.text = "";
        }

        void ShowNextHint()
        {
            var tutorial = _bootstrap != null ? _bootstrap.Session?.Level.tutorial : null;
            if (tutorial?.hints == null || tutorial.hints.Length == 0) return;
            if (_hintIndex >= tutorial.hints.Length) _hintIndex = tutorial.hints.Length - 1;
            _manualHint = tutorial.hints[_hintIndex];
            if (_hint != null)
            {
                _hint.EnableInClassList("no-control", GlobalStatusHint.Current(_bootstrap?.Session) != null);
                _hint.text = PlayerText.English(GlobalStatusHint.DisplayText(_bootstrap?.Session,
                    _bootstrap?.RegionTutorial?.Current?.text, _manualHint));
            }
            if (_hintIndex < tutorial.hints.Length - 1) _hintIndex++;
        }

        static string FormatRules(RuleSet rules)
        {
            var sb = new StringBuilder();
            foreach (var subject in new[] { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG", "WALL", "LAVA" })
            {
                var props = new List<string>(rules[subject]);
                if (props.Count == 0) continue;
                props.Sort();
                sb.Append(subject).Append(" IS ").Append(string.Join(" AND ", props)).Append("\r\n");
            }
            return sb.ToString();
        }
    }
}
