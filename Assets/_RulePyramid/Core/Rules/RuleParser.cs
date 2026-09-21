using System.Collections.Generic;

namespace RulePyramid.Core
{
    public sealed class RuleSet
    {
        readonly Dictionary<string, HashSet<string>> _byColor = new Dictionary<string, HashSet<string>>();

        public RuleSet()
        {
            foreach (var color in Tokens.Colors)
                _byColor[color] = new HashSet<string>();
        }

        public HashSet<string> this[string color]
        {
            get
            {
                if (!_byColor.TryGetValue(color, out var set))
                {
                    set = new HashSet<string>();
                    _byColor[color] = set;
                }
                return set;
            }
        }

        public IEnumerable<string> Colors => _byColor.Keys;

        public bool Has(string color, string property)
        {
            return !string.IsNullOrEmpty(color) && _byColor.TryGetValue(color, out var set) && set.Contains(property);
        }

        public HashSet<string> Get(string color)
        {
            if (string.IsNullOrEmpty(color) || !_byColor.TryGetValue(color, out var set))
                return new HashSet<string>();
            return new HashSet<string>(set);
        }
    }

    public static class RuleParser
    {
        public static RuleSet Parse(IEnumerable<string> fixedRuleLines, IEnumerable<EntityState> entities)
        {
            var rules = new RuleSet();
            if (fixedRuleLines != null)
            {
                foreach (var line in fixedRuleLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ParseSentence(line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries), rules);
                }
            }

            var tokens = new Dictionary<GridCell, string>();
            if (entities != null)
            {
                foreach (var e in entities)
                {
                    if (e.Kind == EntityKind.Text && !string.IsNullOrEmpty(e.Token))
                        tokens[e.Cell] = e.Token;
                }
            }

            foreach (var kv in tokens)
            {
                if (!Tokens.IsColor(kv.Value)) continue;
                foreach (var axis in new[] { GridCell.East, GridCell.North })
                {
                    var ts = new List<string>();
                    var q = kv.Key;
                    while (tokens.TryGetValue(q, out var token))
                    {
                        ts.Add(token);
                        q = q.Add(axis);
                    }
                    ParseSentence(ts, rules);
                }
            }

            return rules;
        }

        public static void ParseSentence(IList<string> ts, RuleSet rules)
        {
            if (ts == null || ts.Count == 0 || !Tokens.IsColor(ts[0])) return;
            var subjects = new List<string> { ts[0] };
            int i = 1;
            while (i + 1 < ts.Count && ts[i] == "AND" && Tokens.IsColor(ts[i + 1]))
            {
                subjects.Add(ts[i + 1]);
                i += 2;
            }
            if (i >= ts.Count || ts[i] != "IS") return;
            i++;
            if (i >= ts.Count || !Tokens.IsProperty(ts[i])) return;
            var properties = new List<string> { ts[i] };
            i++;
            while (i + 1 < ts.Count && ts[i] == "AND" && Tokens.IsProperty(ts[i + 1]))
            {
                properties.Add(ts[i + 1]);
                i += 2;
            }
            foreach (var subject in subjects)
            foreach (var property in properties)
                rules[subject].Add(property);
        }
    }
}
