using System.Collections.Generic;

namespace RulePyramid.Core
{
    public sealed class RuleSet
    {
        readonly Dictionary<string, HashSet<string>> _bySubject = new Dictionary<string, HashSet<string>>();

        public RuleSet()
        {
            foreach (var subject in Tokens.Subjects)
                _bySubject[subject] = new HashSet<string>();
        }

        public HashSet<string> this[string subject]
        {
            get
            {
                if (!_bySubject.TryGetValue(subject, out var set))
                {
                    set = new HashSet<string>();
                    _bySubject[subject] = set;
                }
                return set;
            }
        }

        public IEnumerable<string> Subjects => _bySubject.Keys;

        public bool Has(string subject, string property)
        {
            return !string.IsNullOrEmpty(subject) && _bySubject.TryGetValue(subject, out var set) && set.Contains(property);
        }

        public HashSet<string> Get(string subject)
        {
            if (string.IsNullOrEmpty(subject) || !_bySubject.TryGetValue(subject, out var set))
                return new HashSet<string>();
            return new HashSet<string>(set);
        }

        public Dictionary<string, List<string>> SortedSignature()
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var kv in _bySubject)
            {
                var list = new List<string>(kv.Value);
                list.Sort(System.StringComparer.Ordinal);
                result[kv.Key] = list;
            }
            return result;
        }
    }

    /// <summary>薄包装：实际解析在 WorldModel.Refresh；保留供外部查询。</summary>
    public static class RuleParser
    {
        /// <summary>
        /// 兼容入口：仍接受 fixedRuleLines 参数以保持编译。
        /// v0.9 生产路径不得使用 fixedRules / fixedRuleLines 注入；规则只能来自世界 Text。
        /// 完整语义以 WorldModel.Refresh 为准。
        /// </summary>
        public static RuleSet Parse(IEnumerable<string> fixedRuleLines, IEnumerable<EntityState> entities)
        {
            // 无变换诊断的兼容入口；v0.9 生产不得传入 fixedRuleLines。
            var rules = new RuleSet();
            if (fixedRuleLines != null)
            {
                foreach (var line in fixedRuleLines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ParsePropertyOnly(line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries), rules);
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
                if (!Tokens.IsSubject(kv.Value)) continue;
                foreach (var axis in new[] { GridCell.East, GridCell.North })
                {
                    var ts = new List<string>();
                    var q = kv.Key;
                    while (tokens.TryGetValue(q, out var token))
                    {
                        ts.Add(token);
                        q = q.Add(axis);
                    }
                    ParsePropertyOnly(ts, rules);
                }
            }
            return rules;
        }

        static void ParsePropertyOnly(IList<string> ts, RuleSet rules)
        {
            if (ts == null || ts.Count == 0 || !Tokens.IsSubject(ts[0])) return;
            var subjects = new List<string> { ts[0] };
            int i = 1;
            while (i + 1 < ts.Count && ts[i] == "AND" && Tokens.IsSubject(ts[i + 1]))
            {
                subjects.Add(ts[i + 1]);
                i += 2;
            }
            if (i >= ts.Count || ts[i] != "IS") return;
            i++;
            if (i >= ts.Count) return;
            if (Tokens.IsSubject(ts[i])) return;
            if (!Tokens.IsProp(ts[i])) return;
            var properties = new List<string> { ts[i] };
            i++;
            while (i + 1 < ts.Count && ts[i] == "AND" && Tokens.IsProp(ts[i + 1]))
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
