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
            if (level.fixedRules == null) level.fixedRules = Array.Empty<FixedRuleData>();
            if (level.options != null && level.options.ruleAxes == null)
                level.options.ruleAxes = Array.Empty<string>();
            if (level.tutorial != null && level.tutorial.hints == null)
                level.tutorial.hints = Array.Empty<string>();
            if (level.referenceSolution != null && level.referenceSolution.commands == null)
                level.referenceSolution.commands = Array.Empty<string>();
            if (level.camera != null && level.camera.yawDegrees == null)
                level.camera.yawDegrees = new[] { 45f, 135f, 225f, 315f };
        }
    }
}
