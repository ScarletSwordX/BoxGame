namespace RulePyramid.Core
{
    public sealed class OccupantHit
    {
        public bool IsWall;
        public EntityState Entity;

        public static OccupantHit Wall { get; } = new OccupantHit { IsWall = true };
        public static OccupantHit None { get; } = new OccupantHit();
        public bool IsEmpty => !IsWall && Entity == null;
        public bool IsEntity => Entity != null;
    }

    public static class CollisionPolicy
    {
        public static bool BlocksPair(EntityState a, EntityState b, RuleSet rules)
        {
            if (a == null || b == null) return false;
            if (a.Kind == EntityKind.Text || b.Kind == EntityKind.Text) return true;
            bool youA = PropertyResolver.HasYou(a, rules);
            bool youB = PropertyResolver.HasYou(b, rules);
            bool solidA = PropertyResolver.IsSolid(a, rules);
            bool solidB = PropertyResolver.IsSolid(b, rules);
            if ((youA && !solidB) || (youB && !solidA)) return false;
            return solidA || solidB;
        }

        public static bool CanShareCell(EntityState a, EntityState b, RuleSet rules)
        {
            return !BlocksPair(a, b, rules);
        }
    }
}
