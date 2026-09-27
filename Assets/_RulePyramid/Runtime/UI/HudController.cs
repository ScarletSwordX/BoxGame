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

        public void Bind(GameBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
            if (document == null) document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;
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
            root.Q<Button>("nextButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.NextLevel());
        }

        public void Refresh(GameSession session, string reject, RegionTutorialSession regionTutorial = null)
        {
            if (session == null) return;
            if (_title != null) _title.text = session.Level.title + "  (" + session.Level.id + ")";
            bool transitioning = _bootstrap != null && _bootstrap.IsStageTransitioning;
            if (_phase != null) _phase.text = _bootstrap != null && _bootstrap.CurrentStageCount > 1
                ? "阶段 " + (_bootstrap.CurrentStageIndex + 1) + " / " + _bootstrap.CurrentStageCount
                : session.Phase.ToString();
            if (_stageTransition != null)
            {
                if (transitioning) _stageTransition.RemoveFromClassList("hidden");
                else _stageTransition.AddToClassList("hidden");
            }
            foreach (var id in new[] { "undoButton", "restartButton", "winUndoButton", "winRestartButton", "hintButton" })
                _root?.Q<Button>(id)?.SetEnabled(!transitioning);
            _root?.Q<Button>("nextButton")?.SetEnabled(!transitioning && (_bootstrap == null || _bootstrap.HasNextLevel));
            if (_turns != null) _turns.text = "回合 " + session.TurnCount;
            if (_rules != null) _rules.text = FormatRules(session.World.Rules);
            if (_objective != null && session.Level.tutorial != null)
                _objective.text = !string.IsNullOrEmpty(session.Level.tutorial.objective)
                    ? session.Level.tutorial.objective
                    : (session.Level.tutorial.concept ?? "");
            if (_reject != null) _reject.text = reject ?? "";
            if (_hintButton != null)
                _hintButton.style.display = (regionTutorial != null && regionTutorial.Count > 0)
                    || session.Level.tutorial?.hints == null || session.Level.tutorial.hints.Length == 0
                    ? DisplayStyle.None : DisplayStyle.Flex;
            if (_hint != null)
                _hint.text = GlobalStatusHint.DisplayText(session, regionTutorial?.Current?.text, _manualHint);
            if (_winPanel != null)
            {
                if (session.Won && !transitioning && (_bootstrap == null || !_bootstrap.HasNextStage)) _winPanel.RemoveFromClassList("hidden");
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
                _hint.text = GlobalStatusHint.DisplayText(_bootstrap?.Session,
                    _bootstrap?.RegionTutorial?.Current?.text, _manualHint);
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
