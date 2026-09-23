using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class PropertyResolver
    {
        public static HashSet<string> ExplicitProperties(EntityState entity, RuleSet rules)
        {
            if (entity == null || entity.Kind != EntityKind.Object) return new HashSet<string>();
            return rules.Get(entity.Subject);
        }

        public static bool IsSolid(EntityState entity, RuleSet rules)
        {
            if (entity == null) return false;
            if (entity.Kind == EntityKind.Text) return true;
            var props = ExplicitProperties(entity, rules);
            return props.Contains("STOP") || props.Contains("PUSH") || props.Contains("YOU");
        }

        public static GravityMode ResolveGravity(EntityState entity, RuleSet rules)
        {
            if (entity != null && entity.Anchored) return GravityMode.Anchored;
            var props = ExplicitProperties(entity, rules);
            if (props.Contains("FLY")) return GravityMode.Up;
            if (props.Contains("HOVER")) return GravityMode.Hover;
            return GravityMode.Down;
        }

        public static bool HasYou(EntityState entity, RuleSet rules)
        {
            return entity != null && entity.Kind == EntityKind.Object && rules.Has(entity.Subject, "YOU");
        }

        public static bool HasWin(EntityState entity, RuleSet rules)
        {
            return entity != null && entity.Kind == EntityKind.Object && rules.Has(entity.Subject, "WIN");
        }

        public static bool HasBouncy(EntityState entity, RuleSet rules)
        {
            return ExplicitProperties(entity, rules).Contains("BOUNCY");
        }

        public static bool HasPush(EntityState entity, RuleSet rules)
        {
            return ExplicitProperties(entity, rules).Contains("PUSH");
        }

        /// <summary>遗留名；BOUNCY 语义。</summary>
        public static bool HasJump(EntityState entity, RuleSet rules) => HasBouncy(entity, rules);
    }
}
