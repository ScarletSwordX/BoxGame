using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public static class LevelCloner
    {
        public static LevelDefinition Clone(LevelDefinition source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var clone = new LevelDefinition
            {
                schemaVersion = source.schemaVersion,
                mechanicsVersion = source.mechanicsVersion,
                id = source.id,
                title = source.title,
                bounds = CloneBox(source.bounds),
                terrain = CloneBoxes(source.terrain),
                entities = CloneEntities(source.entities),
                fixedRules = CloneRules(source.fixedRules),
                options = CloneOptions(source.options),
                camera = CloneCamera(source.camera),
                tutorial = CloneTutorial(source.tutorial),
                referenceSolution = CloneSolution(source.referenceSolution)
            };
            return clone;
        }

        static GridCellBox CloneBox(GridCellBox box)
        {
            if (box == null) return null;
            return new GridCellBox { min = box.min, max = box.max };
        }

        static GridCellBox[] CloneBoxes(GridCellBox[] boxes)
        {
            if (boxes == null) return Array.Empty<GridCellBox>();
            var result = new GridCellBox[boxes.Length];
            for (int i = 0; i < boxes.Length; i++) result[i] = CloneBox(boxes[i]);
            return result;
        }

        static EntityDefinition[] CloneEntities(EntityDefinition[] entities)
        {
            if (entities == null) return Array.Empty<EntityDefinition>();
            var result = new EntityDefinition[entities.Length];
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                result[i] = new EntityDefinition
                {
                    id = e.id,
                    kind = e.kind,
                    color = e.color ?? "",
                    token = e.token ?? "",
                    cell = e.cell,
                    anchored = e.anchored
                };
            }
            return result;
        }

        static FixedRuleData[] CloneRules(FixedRuleData[] rules)
        {
            if (rules == null) return Array.Empty<FixedRuleData>();
            var result = new FixedRuleData[rules.Length];
            for (int i = 0; i < rules.Length; i++)
            {
                var r = rules[i];
                var tokens = r.tokens == null ? Array.Empty<string>() : (string[])r.tokens.Clone();
                result[i] = new FixedRuleData { id = r.id, tokens = tokens };
            }
            return result;
        }

        static OptionsData CloneOptions(OptionsData o)
        {
            if (o == null) return null;
            return new OptionsData
            {
                supportMode = o.supportMode,
                jumpMode = o.jumpMode,
                bounceRiseCells = o.bounceRiseCells,
                decisionMode = o.decisionMode,
                ruleAxes = o.ruleAxes == null ? Array.Empty<string>() : (string[])o.ruleAxes.Clone(),
                winMode = o.winMode,
                winCheckMode = o.winCheckMode,
                actionMode = o.actionMode,
                gravityMode = o.gravityMode,
                playerBlockMode = o.playerBlockMode
            };
        }

        static CameraData CloneCamera(CameraData c)
        {
            if (c == null) return null;
            return new CameraData
            {
                initialSlot = c.initialSlot,
                pitchDegrees = c.pitchDegrees,
                yawDegrees = c.yawDegrees == null ? Array.Empty<float>() : (float[])c.yawDegrees.Clone(),
                inputMode = c.inputMode
            };
        }

        static TutorialData CloneTutorial(TutorialData t)
        {
            if (t == null) return null;
            return new TutorialData
            {
                objective = t.objective,
                hints = t.hints == null ? Array.Empty<string>() : (string[])t.hints.Clone()
            };
        }

        static ReferenceSolutionData CloneSolution(ReferenceSolutionData s)
        {
            if (s == null) return null;
            return new ReferenceSolutionData
            {
                commands = s.commands == null ? Array.Empty<string>() : (string[])s.commands.Clone(),
                expectedFinalStatus = s.expectedFinalStatus
            };
        }

        public static HashSet<GridCell> ExpandTerrain(IEnumerable<GridCellBox> boxes)
        {
            var set = new HashSet<GridCell>();
            if (boxes == null) return set;
            foreach (var box in boxes)
            {
                if (box == null) continue;
                for (int x = box.min.x; x <= box.max.x; x++)
                for (int y = box.min.y; y <= box.max.y; y++)
                for (int z = box.min.z; z <= box.max.z; z++)
                    set.Add(new GridCell(x, y, z));
            }
            return set;
        }
    }
}
