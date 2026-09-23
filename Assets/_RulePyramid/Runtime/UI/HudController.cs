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
        int _hintIndex;
        GameBootstrap _bootstrap;

        public void Bind(GameBootstrap bootstrap)
        {
            _bootstrap = bootstrap;
            if (document == null) document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null) return;
            _title = root.Q<Label>("levelTitle");
            _phase = root.Q<Label>("phaseLabel");
            _turns = root.Q<Label>("turnLabel");
            _rules = root.Q<Label>("rulesLabel");
            _objective = root.Q<Label>("objectiveLabel");
            _hint = root.Q<Label>("hintLabel");
            _reject = root.Q<Label>("rejectLabel");
            _winPanel = root.Q("winPanel");
            root.Q<Button>("hintButton")?.RegisterCallback<ClickEvent>(_ => ShowNextHint());
            root.Q<Button>("undoButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Undo());
            root.Q<Button>("restartButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Restart());
            root.Q<Button>("winUndoButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Undo());
            root.Q<Button>("winRestartButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.Restart());
            root.Q<Button>("nextButton")?.RegisterCallback<ClickEvent>(_ => bootstrap.NextLevel());
        }

        public void Refresh(GameSession session, string reject)
        {
            if (session == null) return;
            if (_title != null) _title.text = session.Level.title + "  (" + session.Level.id + ")";
            if (_phase != null) _phase.text = session.Phase.ToString();
            if (_turns != null) _turns.text = "回合 " + session.TurnCount;
            if (_rules != null) _rules.text = FormatRules(session.World.Rules);
            if (_objective != null && session.Level.tutorial != null)
                _objective.text = !string.IsNullOrEmpty(session.Level.tutorial.objective)
                    ? session.Level.tutorial.objective
                    : (session.Level.tutorial.concept ?? "");
            if (_reject != null) _reject.text = reject ?? "";
            if (_winPanel != null)
            {
                if (session.Won) _winPanel.RemoveFromClassList("hidden");
                else _winPanel.AddToClassList("hidden");
            }
        }

        public void ResetHints()
        {
            _hintIndex = 0;
            if (_hint != null) _hint.text = "";
        }

        void ShowNextHint()
        {
            var tutorial = _bootstrap != null ? _bootstrap.Session?.Level.tutorial : null;
            if (tutorial?.hints == null || tutorial.hints.Length == 0) return;
            if (_hintIndex >= tutorial.hints.Length) _hintIndex = tutorial.hints.Length - 1;
            if (_hint != null) _hint.text = tutorial.hints[_hintIndex];
            if (_hintIndex < tutorial.hints.Length - 1) _hintIndex++;
        }

        static string FormatRules(RuleSet rules)
        {
            var sb = new StringBuilder();
            foreach (var subject in new[] { "ROBOT", "ROCK", "CLOUD", "SPRING", "FLAG" })
            {
                var props = new List<string>(rules[subject]);
                if (props.Count == 0) continue;
                props.Sort();
                sb.Append(subject).Append(" IS ").Append(string.Join(" AND ", props)).Append('\r\n');
            }
            return sb.ToString();
        }
    }
}
