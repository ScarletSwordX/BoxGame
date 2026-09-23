using System;
using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public static class LevelJsonSerializer
    {
        public static LevelDefinition FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentException("JSON is empty");
            var level = JsonUtility.FromJson<LevelDefinition>(json);
            if (level == null)
                throw new InvalidOperationException("Failed to parse level JSON");
            Normalize(level);
            var report = LevelValidator.ValidateStructure(level);
            if (report.HasStructureErrors)
                throw new InvalidOperationException(report.ToString());
            return level;
        }

        public static string ToJson(LevelDefinition level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            Normalize(level);
            return JsonUtility.ToJson(level, true);
        }

        static void Normalize(LevelDefinition level)
        {
            if (level.terrain == null) level.terrain = Array.Empty<GridCellBox>();
            if (level.entities == null) level.entities = Array.Empty<EntityDefinition>();
            if (level.fixedRules != null && level.fixedRules.Length > 0)
                throw new InvalidOperationException("Forbidden non-spatial rule source; use world TEXT entities");
            if (level.fixedRules == null) level.fixedRules = Array.Empty<FixedRuleData>();
            if (level.tutorial != null)
            {
                if (level.tutorial.hints == null) level.tutorial.hints = Array.Empty<string>();
                if (level.tutorial.risks == null) level.tutorial.risks = Array.Empty<string>();
            }
            if (level.camera != null)
            {
                if (level.camera.yawDegrees == null)
                    level.camera.yawDegrees = new[] { 45f, 135f, 225f, 315f };
                if (level.camera.initialSlot == 0 && level.camera.slot != 0)
                    level.camera.initialSlot = level.camera.slot;
                else if (level.camera.slot == 0 && level.camera.initialSlot != 0)
                    level.camera.slot = level.camera.initialSlot;
            }
            if (level.entities != null)
            {
                foreach (var e in level.entities)
                {
                    if (e == null) continue;
                    if (string.IsNullOrEmpty(e.subject) && !string.IsNullOrEmpty(e.color))
                        e.subject = e.color;
                    if (e.token == null) e.token = "";
                    if (e.subject == null) e.subject = "";
                }
            }
            if (level.referenceSolutions == null || level.referenceSolutions.Length == 0)
            {
                if (level.referenceSolution != null)
                    level.referenceSolutions = new[] { level.referenceSolution };
                else
                    level.referenceSolutions = Array.Empty<ReferenceSolutionData>();
            }
            foreach (var sol in level.referenceSolutions)
            {
                if (sol == null) continue;
                if (sol.commands == null) sol.commands = Array.Empty<string>();
                if (sol.requireEvents == null) sol.requireEvents = Array.Empty<string>();
                if (sol.forbidEvents == null) sol.forbidEvents = Array.Empty<string>();
            }
            if (level.referenceSolution != null && level.referenceSolution.commands == null)
                level.referenceSolution.commands = Array.Empty<string>();
        }
    }
}
