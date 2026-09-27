using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>所有 YOU 同时受控；仍拒绝直接控制 HOVER/FLY。</summary>
    public static class ControlResolver
    {
        public static EntityState Actor(IEnumerable<EntityState> entities, System.Func<EntityState, HashSet<string>> props, System.Func<EntityState, GravityMode> gravity)
        {
            var actors = Actors(entities, props, gravity);
            return actors.Count == 0 ? null : actors[0];
        }

        public static List<EntityState> Actors(IEnumerable<EntityState> entities, System.Func<EntityState, HashSet<string>> props, System.Func<EntityState, GravityMode> gravity)
        {
            var actors = new List<EntityState>();
            foreach (var e in entities)
            {
                if (props(e).Contains("YOU"))
                    actors.Add(e);
            }
            foreach (var actor in actors)
                if (gravity(actor) != GravityMode.Down)
                    throw new RuleConflictException("ControlledGravity: direct YOU+HOVER/FLY input is outside this prototype");
            actors.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return actors;
        }

        public static string ActorId(EntityState actor)
        {
            return actor == null ? null : actor.Id;
        }
    }
}
