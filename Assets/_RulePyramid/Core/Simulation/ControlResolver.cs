using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>控制权：唯一 YOU、重力约束、ActorId。</summary>
    public static class ControlResolver
    {
        public static EntityState Actor(IEnumerable<EntityState> entities, System.Func<EntityState, HashSet<string>> props, System.Func<EntityState, GravityMode> gravity)
        {
            var actors = new List<EntityState>();
            foreach (var e in entities)
            {
                if (props(e).Contains("YOU"))
                    actors.Add(e);
            }
            if (actors.Count > 1)
                throw new RuleConflictException("MultipleYOU: this prototype uses one controlled entity");
            if (actors.Count == 0) return null;
            var actor = actors[0];
            if (gravity(actor) != GravityMode.Down)
                throw new RuleConflictException("ControlledGravity: direct YOU+HOVER/FLY input is outside this prototype");
            return actor;
        }

        public static string ActorId(EntityState actor)
        {
            return actor == null ? null : actor.Id;
        }
    }
}
