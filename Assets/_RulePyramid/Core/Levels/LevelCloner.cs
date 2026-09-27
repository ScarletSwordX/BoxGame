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
                tutorial = CloneTutorial(source.tutorial),
                stagePlan = CloneStagePlan(source.stagePlan),
                authoringSourceJson = source.authoringSourceJson
            };
            return clone;
        }

        static GridCellBox CloneBox(GridCellBox box)
        {
            if (box == null) return null;
            return new GridCellBox { id = box.id, min = box.min, max = box.max, appearance = box.appearance };
        }

        static GridCellBox[] CloneBoxes(GridCellBox[] boxes)
        {
            if (boxes == null) return Array.Empty<GridCellBox>();
            var result = new GridCellBox[boxes.Length];
            for (int i = 0; i < boxes.Length; i++) result[i] = CloneBox(boxes[i]);
            return result;
        }

        public static EntityDefinition[] CloneEntities(EntityDefinition[] entities)
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
                transformationMode = o.transformationMode,
                ruleSourceMode = o.ruleSourceMode,
                textMobilityMode = o.textMobilityMode
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

        public static TutorialData CloneTutorial(TutorialData t)
        {
            if (t == null) return null;
            return new TutorialData
            {
                objective = t.objective,
                concept = t.concept,
                observation = t.observation,
                necessity = t.necessity,
                hints = t.hints == null ? Array.Empty<string>() : (string[])t.hints.Clone(),
                risks = t.risks == null ? Array.Empty<string>() : (string[])t.risks.Clone(),
                regions = CloneRegions(t.regions)
            };
        }

        static RegionTutorialData[] CloneRegions(RegionTutorialData[] regions)
        {
            if (regions == null) return Array.Empty<RegionTutorialData>();
            var result = new RegionTutorialData[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                var region = regions[i];
                if (region == null) continue;
                result[i] = new RegionTutorialData
                {
                    id = region.id,
                    name = region.name,
                    text = region.text,
                    bounds = CloneBox(region.bounds),
                    durationSeconds = region.durationSeconds,
                    enabled = region.enabled
                };
            }
            return result;
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

        public static ReferenceSolutionData[] CloneSolutions(ReferenceSolutionData[] solutions)
        {
            if (solutions == null) return Array.Empty<ReferenceSolutionData>();
            var result = new ReferenceSolutionData[solutions.Length];
            for (int i = 0; i < solutions.Length; i++)
                result[i] = CloneSolution(solutions[i]);
            return result;
        }

        public static StagePlanData CloneStagePlan(StagePlanData plan)
        {
            if (plan == null) return null;
            var stages = plan.stages ?? Array.Empty<StageDefinition>();
            var regions = plan.regions ?? Array.Empty<StageRegion>();
            var copy = new StagePlanData { boundaryMode = plan.boundaryMode,
                stages = new StageDefinition[stages.Length], regions = new StageRegion[regions.Length] };
            for (int i = 0; i < stages.Length; i++)
            {
                var s = stages[i];
                if (s == null) continue;
                copy.stages[i] = new StageDefinition { id = s.id, name = s.name, lesson = s.lesson,
                    entities = CloneEntities(s.entities), tutorial = CloneTutorial(s.tutorial),
                    referenceSolutions = CloneSolutions(s.referenceSolutions) };
            }
            for (int i = 0; i < regions.Length; i++)
            {
                var r = regions[i];
                if (r != null) copy.regions[i] = new StageRegion { id = r.id, stageId = r.stageId, bounds = CloneBox(r.bounds) };
            }
            return copy;
        }

        public static HashSet<GridCell> ExpandTerrain(IEnumerable<GridCellBox> boxes)
        {
            var set = new HashSet<GridCell>();
            if (boxes == null) return set;
            foreach (var box in boxes)
            {
                if (box == null) continue;
                if (!MapResize.TryValidateBounds(box, out var error))
                    throw new ArgumentException("地形盒无效：" + error);
                for (long x = box.min.x; x <= box.max.x; x++)
                for (long y = box.min.y; y <= box.max.y; y++)
                for (long z = box.min.z; z <= box.max.z; z++)
                    set.Add(new GridCell((int)x, (int)y, (int)z));
            }
            return set;
        }
    }
}
