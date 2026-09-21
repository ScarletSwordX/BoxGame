using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class WinEvaluator
    {
        public static bool TryFind(IEnumerable<EntityState> entities, RuleSet rules, out WinRecord record)
        {
            record = null;
            var actors = new List<EntityState>();
            var goals = new List<EntityState>();
            foreach (var e in entities)
            {
                if (PropertyResolver.HasYou(e, rules)) actors.Add(e);
                if (PropertyResolver.HasWin(e, rules)) goals.Add(e);
            }
            actors.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            goals.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var a in actors)
            foreach (var b in goals)
            {
                if (a.Id != b.Id && a.Cell == b.Cell)
                {
                    record = new WinRecord { YouId = a.Id, WinId = b.Id, Cell = a.Cell };
                    return true;
                }
            }
            return false;
        }
    }
}
