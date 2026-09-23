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
                designContract = CloneDesignContract(source.designContract),
                referenceSolutions = CloneSolutions(source.referenceSolutions),
                referenceSolution = CloneSolution(source.referenceSolution),
                tutorial = CloneTutorial(source.tutorial)
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
                    subject = e.subject ?? "",
                    token = e.token ?? "",
                    cell = e.cell,
                    anchored = e.anchored,
                    color = e.color ?? ""
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
                actionMode = o.actionMode,
                winMode = o.winMode,
                gravityMode = o.gravityMode,
                collisionMode = o.collisionMode,
                solidityMode = o.solidityMode,
                supportMode = o.supportMode,
                controlMode = o.controlMode,
                bounceRiseCells = o.bounceRiseCells,
                transformationMode = o.transformationMode
            };
        }

        static CameraData CloneCamera(CameraData c)
        {
            if (c == null) return null;
            return new CameraData
            {
                initialSlot = c.initialSlot,
                slot = c.slot,
                pitchDegrees = c.pitchDegrees,
                yawDegrees = c.yawDegrees == null ? Array.Empty<float>() : (float[])c.yawDegrees.Clone(),
                inputMode = c.inputMode,
                orthographic = c.orthographic
            };
        }

        static DesignContractData CloneDesignContract(DesignContractData d)
        {
            if (d == null) return null;
            return new DesignContractData
            {
                requireActiveInteraction = d.requireActiveInteraction,
                minimumSolutionFamilies = d.minimumSolutionFamilies,
                interactionIsAuthoringConstraint = d.interactionIsAuthoringConstraint
            };
        }

        static TutorialData CloneTutorial(TutorialData t)
        {
            if (t == null) return null;
            return new TutorialData
            {
                objective = t.objective,
                concept = t.concept,
                observation = t.observation,
                necessity = t.necessity,
                hints = t.hints == null ? Array.Empty<string>() : (string[])t.hints.Clone(),
                risks = t.risks == null ? Array.Empty<string>() : (string[])t.risks.Clone()
            };
        }

        static ReferenceSolutionData CloneSolution(ReferenceSolutionData s)
        {
            if (s == null) return null;
            return new ReferenceSolutionData
            {
                id = s.id,
                name = s.name,
                family = s.family,
                commands = s.commands == null ? Array.Empty<string>() : (string[])s.commands.Clone(),
                mustControlAtWin = s.mustControlAtWin,
                mustWinWith = s.mustWinWith,
                requireEvents = s.requireEvents == null ? Array.Empty<string>() : (string[])s.requireEvents.Clone(),
                forbidEvents = s.forbidEvents == null ? Array.Empty<string>() : (string[])s.forbidEvents.Clone(),
                expectedFinalStatus = s.expectedFinalStatus,
                status = s.status,
                claim = s.claim
            };
        }

        static ReferenceSolutionData[] CloneSolutions(ReferenceSolutionData[] solutions)
        {
            if (solutions == null) return Array.Empty<ReferenceSolutionData>();
            var result = new ReferenceSolutionData[solutions.Length];
            for (int i = 0; i < solutions.Length; i++)
                result[i] = CloneSolution(solutions[i]);
            return result;
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
