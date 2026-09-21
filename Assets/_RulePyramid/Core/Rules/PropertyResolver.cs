using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class PropertyResolver
    {
        public static HashSet<string> ExplicitProperties(EntityState entity, RuleSet rules)
        {
            if (entity == null || entity.Kind != EntityKind.Color) return new HashSet<string>();
            return rules.Get(entity.Color);
        }

        public static bool IsSolid(EntityState entity, RuleSet rules)
        {
            if (entity == null) return false;
            if (entity.Kind == EntityKind.Text) return true;
            var props = ExplicitProperties(entity, rules);
            return props.Contains("BLOCK") || props.Contains("YOU");
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
            return entity != null && entity.Kind == EntityKind.Color && rules.Has(entity.Color, "YOU");
        }

        public static bool HasWin(EntityState entity, RuleSet rules)
        {
            return entity != null && entity.Kind == EntityKind.Color && rules.Has(entity.Color, "WIN");
        }

        public static bool HasJump(EntityState entity, RuleSet rules)
        {
            return IsSolid(entity, rules) && ExplicitProperties(entity, rules).Contains("JUMP");
        }
    }
}
