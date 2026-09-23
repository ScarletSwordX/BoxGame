using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>名词变形：pending_transforms 捕获与批量应用。</summary>
    public static class TransformationResolver
    {
        public static List<(EntityState entity, string before, string after)> PendingTransforms(
            IEnumerable<EntityState> entities,
            Dictionary<string, string> transforms,
            HashSet<string> transformedThisTurn)
        {
            var list = new List<(EntityState, string, string)>();
            var sorted = new List<EntityState>(entities);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var e in sorted)
            {
                if (e.Kind != EntityKind.Object) continue;
                if (transformedThisTurn != null && transformedThisTurn.Contains(e.Id)) continue;
                if (string.IsNullOrEmpty(e.Subject)) continue;
                if (transforms == null || !transforms.TryGetValue(e.Subject, out var after)) continue;
                list.Add((e, e.Subject, after));
            }
            return list;
        }

        public static void Apply(
            List<(EntityState entity, string before, string after)> changes,
            HashSet<string> transformedThisTurn)
        {
            foreach (var change in changes)
            {
                change.entity.Subject = change.after;
                transformedThisTurn.Add(change.entity.Id);
            }
        }
    }
}
