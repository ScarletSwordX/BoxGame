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

    /// <summary>双方均为实体时才互相排斥（RW-v0.9）。</summary>
    public static class CollisionPolicy
    {
        public static bool BlocksPair(EntityState a, EntityState b, RuleSet rules)
        {
            if (a == null || b == null) return false;
            return PropertyResolver.IsSolid(a, rules) && PropertyResolver.IsSolid(b, rules);
        }

        public static bool CanShareCell(EntityState a, EntityState b, RuleSet rules)
        {
            return !BlocksPair(a, b, rules);
        }
    }
}
