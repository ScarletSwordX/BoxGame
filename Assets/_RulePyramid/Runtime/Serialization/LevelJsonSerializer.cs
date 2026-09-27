using System;
using RulePyramid.Core;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace RulePyramid.Runtime
{
    public static class LevelJsonSerializer
    {
        public static LevelDefinition FromJson(string json)
        {
            var level = FromDraftJson(json);
            if (level.stagePlan?.stages != null && level.stagePlan.stages.Length > 1)
                throw new InvalidOperationException("多阶段草稿请在关卡编辑器中选择阶段独立试玩；游戏内自动推进尚未实现。");
            var report = LevelValidator.ValidateStructure(level);
            if (report.HasStructureErrors)
                throw new InvalidOperationException(report.ToString());
            return level;
        }

        // 作者可以保存并重新打开尚未拼完 YOU 或参考解的草稿。
        public static LevelDefinition FromDraftJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("关卡 JSON 为空");
            var source = JObject.Parse(json);
            if (source.Property("fixedRules") != null)
                throw new InvalidOperationException("旧 fixedRules 数据需要迁移，请使用空间词牌。");
            var level = JsonUtility.FromJson<LevelDefinition>(json);
            if (level == null || level.schemaVersion != Tokens.SchemaVersion || level.mechanicsVersion != Tokens.MechanicsVersion)
                throw new InvalidOperationException("不支持的关卡版本，请先迁移到 RW-v0.9。");
            // JsonUtility 会把缺失的可序列化类字段物化为空对象，不能据此恢复旧单解。
            if (!(source["referenceSolution"] is JObject)) level.referenceSolution = null;
            if (!(source["stagePlan"] is JObject)) level.stagePlan = null;
            Normalize(level);
            level.authoringSourceJson = json;
            return level;
        }

        public static string ToJson(LevelDefinition level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            var copy = LevelCloner.Clone(level);
            Normalize(copy);
            if (copy.stagePlan != null && copy.stagePlan.stages != null && copy.stagePlan.stages.Length > 0)
            {
                var first = copy.stagePlan.stages[0];
                copy.entities = LevelCloner.CloneEntities(first.entities);
                copy.tutorial = LevelCloner.CloneTutorial(first.tutorial);
                copy.referenceSolutions = LevelCloner.CloneSolutions(first.referenceSolutions);
            }
            var current = JObject.Parse(JsonUtility.ToJson(copy));
            current.Remove("fixedRules");
            current.Remove("referenceSolution");
            current.Remove("authoringSourceJson");
            if (copy.stagePlan == null) current.Remove("stagePlan");
            // 未被本版编辑器识别的设计元数据也必须往返保留。
            var original = string.IsNullOrEmpty(level.authoringSourceJson) ? new JObject() : JObject.Parse(level.authoringSourceJson);
            original.Remove("fixedRules");
            original.Remove("referenceSolution");
            if (copy.stagePlan == null) original.Remove("stagePlan");
            return Merge(original, current).ToString(Newtonsoft.Json.Formatting.Indented);
        }

        static JToken Merge(JToken original, JToken current)
        {
            if (current is JObject obj)
            {
                var merged = original is JObject old ? (JObject)old.DeepClone() : new JObject();
                foreach (var property in obj.Properties()) merged[property.Name] = Merge(merged[property.Name], property.Value);
                return merged;
            }
            if (current is JArray array)
            {
                var result = new JArray();
                foreach (var item in array)
                {
                    JToken previous = null;
                    var id = (item as JObject)?["id"]?.Value<string>();
                    if (id != null && original is JArray oldArray)
                        foreach (var oldItem in oldArray)
                            if ((oldItem as JObject)?["id"]?.Value<string>() == id) { previous = oldItem; break; }
                    result.Add(Merge(previous, item));
                }
                return result;
            }
            return current.DeepClone();
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
                if (level.referenceSolution != null && !string.IsNullOrEmpty(level.referenceSolution.id))
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
            if (level.stagePlan != null)
            {
                StageAuthoring.EnsurePlan(level);
                foreach (var stage in level.stagePlan.stages)
                {
                    if (stage == null) continue;
                    if (stage.entities == null) stage.entities = Array.Empty<EntityDefinition>();
                    if (stage.referenceSolutions == null) stage.referenceSolutions = Array.Empty<ReferenceSolutionData>();
                }
            }
        }
    }
}
