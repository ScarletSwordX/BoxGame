using System;
using System.Collections.Generic;
using System.Text;

namespace RulePyramid.Core
{
    public enum ValidationSeverity
    {
        StructureError,
        PlaytestError,
        Warning
    }

    public sealed class ValidationIssue
    {
        public ValidationSeverity Severity;
        public string Code;
        public string Message;
    }

    public sealed class ValidationReport
    {
        public List<ValidationIssue> Issues = new List<ValidationIssue>();
        public bool HasStructureErrors
        {
            get
            {
                foreach (var i in Issues)
                    if (i.Severity == ValidationSeverity.StructureError) return true;
                return false;
            }
        }

        public bool CanPlaytest
        {
            get
            {
                foreach (var i in Issues)
                    if (i.Severity != ValidationSeverity.Warning) return false;
                return true;
            }
        }

        public void Add(ValidationSeverity severity, string code, string message)
        {
            Issues.Add(new ValidationIssue { Severity = severity, Code = code, Message = message });
        }

        public override string ToString()
        {
            if (Issues.Count == 0) return "OK";
            var sb = new StringBuilder();
            foreach (var i in Issues)
                sb.Append(i.Severity).Append(' ').Append(i.Code).Append(": ").Append(i.Message).AppendLine();
            return sb.ToString();
        }
    }

    public static class LevelValidator
    {
        public static ValidationReport ValidateStageSave(LevelDefinition level)
        {
            var report = ValidateStageAuthoring(level);
            if (level == null) return report;
            foreach (var issue in ValidateAuthoringOccupancy(level).Issues)
                report.Add(issue.Severity, issue.Code, "当前编辑布局：" + issue.Message);
            if (level.stagePlan == null || report.HasStructureErrors) return report;
            foreach (var stage in level.stagePlan.stages)
            {
                var snapshot = LevelCloner.Clone(level);
                snapshot.stagePlan = null;
                snapshot.entities = LevelCloner.CloneEntities(stage.entities);
                foreach (var issue in ValidateAuthoringOccupancy(snapshot).Issues)
                    report.Add(issue.Severity, issue.Code, "阶段 " + stage.name + "：" + issue.Message);
                foreach (var entity in stage.entities ?? Array.Empty<EntityDefinition>())
                    if (entity != null && !AuthoringOperations.Contains(level, entity.cell))
                        report.Add(ValidationSeverity.StructureError, "STAGE_ENTITY_BOUNDS", "阶段 " + stage.name + " 的实体 " + entity.id + " 超出地图");
            }
            return report;
        }

        public static ValidationReport ValidateStageAuthoring(LevelDefinition level, string stageId = null)
        {
            var report = new ValidationReport();
            if (level?.stagePlan == null) return report;
            if (!MapResize.TryValidateBounds(level.bounds, out var boundsError))
            { report.Add(ValidationSeverity.StructureError, "BOUNDS", boundsError); return report; }
            var stages = level.stagePlan.stages ?? Array.Empty<StageDefinition>();
            if (stages.Length == 0) { report.Add(ValidationSeverity.StructureError, "STAGES_EMPTY", "至少需要一个阶段"); return report; }
            string boundaryMode = level.stagePlan.boundaryMode;
            if (!string.IsNullOrEmpty(boundaryMode) && boundaryMode != "Stone" && boundaryMode != "AirWall")
                report.Add(ValidationSeverity.StructureError, "STAGE_BOUNDARY_MODE", "未知阶段边界模式：" + boundaryMode);
            var ids = new HashSet<string>();
            foreach (var stage in stages)
                if (stage == null || string.IsNullOrWhiteSpace(stage.id) || !ids.Add(stage.id))
                    report.Add(ValidationSeverity.StructureError, "STAGE_ID", "阶段 ID 缺失或重复");
            var stageRegions = level.stagePlan.regions ?? Array.Empty<StageRegion>();
            var regionIds = new HashSet<string>();
            for (int regionIndex = 0; regionIndex < stageRegions.Length; regionIndex++)
            {
                var region = stageRegions[regionIndex];
                if (region == null || region.bounds == null || !ids.Contains(region.stageId)
                    || !MapResize.TryValidateBounds(region.bounds, out _)
                    || !AuthoringOperations.Contains(level, region.bounds.min)
                    || !AuthoringOperations.Contains(level, region.bounds.max))
                    report.Add(ValidationSeverity.StructureError, "STAGE_REGION", "阶段归属区域无效或超出地图");
                if (region == null) continue;
                if (string.IsNullOrWhiteSpace(region.id) || !regionIds.Add(region.id))
                    report.Add(ValidationSeverity.StructureError, "STAGE_REGION_ID", "阶段归属区域 ID 缺失或重复");
                if (region.bounds == null) continue;
                for (int earlier = 0; earlier < regionIndex; earlier++)
                {
                    var other = stageRegions[earlier]?.bounds;
                    if (other != null && region.bounds.min.x <= other.max.x && other.min.x <= region.bounds.max.x
                        && region.bounds.min.y <= other.max.y && other.min.y <= region.bounds.max.y
                        && region.bounds.min.z <= other.max.z && other.min.z <= region.bounds.max.z)
                        report.Add(ValidationSeverity.StructureError, "STAGE_REGION_OVERLAP", "阶段归属区域重叠");
                }
            }
            if (stageId != null && Array.Find(stages, s => s != null && s.id == stageId) == null)
            { report.Add(ValidationSeverity.StructureError, "STAGE_UNKNOWN", "所选阶段不存在"); return report; }
            foreach (var selected in stages)
            {
                if (selected == null || (stageId != null && selected.id != stageId)) continue;
                if (boundaryMode == "AirWall" && !report.HasStructureErrors)
                {
                    try { StageAuthoring.Project(level, selected.id); }
                    catch (InvalidOperationException ex)
                    { report.Add(ValidationSeverity.StructureError, "STAGE_AIRWALL_SHAPE", ex.Message); }
                    catch (ArgumentException ex)
                    { report.Add(ValidationSeverity.StructureError, "STAGE_AIRWALL_SHAPE", ex.Message); }
                }
                foreach (var entity in selected.entities ?? Array.Empty<EntityDefinition>())
                    if (entity != null && !StageAuthoring.IsOpen(level, selected.id, entity.cell))
                        report.Add(ValidationSeverity.PlaytestError, "STAGE_ENTITY_CLOSED", "阶段 " + selected.name + " 的实体 " + entity.id + " 位于未开放区域：" + entity.cell);
            }
            return report;
        }

        /// <summary>只检查编辑状态下可见方块的占格冲突，供草稿保存单独使用。</summary>
        public static ValidationReport ValidateAuthoringOccupancy(LevelDefinition level)
        {
            var report = new ValidationReport();
            if (level == null) return report;
            var boxes = level.terrain ?? Array.Empty<GridCellBox>();
            var terrain = new Dictionary<GridCell, int>();
            var reportedPairs = new HashSet<long>();
            bool boundsValid = MapResize.TryValidateBounds(level.bounds, out _);
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                if (!boundsValid || box == null || !ValidBox(box)
                    || !CellInBox(box.min, level.bounds) || !CellInBox(box.max, level.bounds)) continue;
                for (long x = box.min.x; x <= box.max.x; x++)
                for (long y = box.min.y; y <= box.max.y; y++)
                for (long z = box.min.z; z <= box.max.z; z++)
                {
                    var cell = new GridCell((int)x, (int)y, (int)z);
                    if (terrain.TryGetValue(cell, out var first))
                    {
                        long pair = ((long)first << 32) | (uint)i;
                        if (reportedPairs.Add(pair))
                            report.Add(ValidationSeverity.StructureError, "TERRAIN_OVERLAP",
                                "地形 " + boxes[first].id + " 与 " + box.id + " 重叠：" + cell);
                    }
                    else terrain.Add(cell, i);
                }
            }
            var occupied = new Dictionary<GridCell, string>();
            foreach (var entity in level.entities ?? Array.Empty<EntityDefinition>())
            {
                if (entity == null) continue;
                if (occupied.TryGetValue(entity.cell, out var previous))
                    report.Add(ValidationSeverity.StructureError, "ENTITY_OVERLAP",
                        "实体 " + previous + " 与 " + entity.id + " 同格：" + entity.cell);
                else occupied.Add(entity.cell, entity.id);
                if (terrain.TryGetValue(entity.cell, out var terrainIndex))
                    report.Add(ValidationSeverity.StructureError, "ENTITY_TERRAIN_OVERLAP",
                        "实体 " + entity.id + " 与地形 " + boxes[terrainIndex].id + " 同格：" + entity.cell);
            }
            return report;
        }

        static bool ValidBox(GridCellBox box)
        {
            return box.min.x <= box.max.x && box.min.y <= box.max.y && box.min.z <= box.max.z;
        }

        static bool CellInBox(GridCell cell, GridCellBox box)
        {
            return cell.x >= box.min.x && cell.x <= box.max.x
                && cell.y >= box.min.y && cell.y <= box.max.y
                && cell.z >= box.min.z && cell.z <= box.max.z;
        }

        static readonly Dictionary<string, string> ExpectedOptions = new Dictionary<string, string>
        {
            { "actionMode", "MoveAutoPush" },
            { "winMode", "DistinctEntitiesSameCell" },
            { "gravityMode", "WorldDownExceptHoverOrFly" },
            { "collisionMode", "SolidPairsTerrainUniversal" },
            { "solidityMode", "YouPushStopOrText" },
            { "supportMode", "StrictBelow" },
            { "controlMode", "SingleYouTransfer_NoControlUndo" },
            { "transformationMode", "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand" },
            { "ruleSourceMode", "WorldTextOnly" },
            { "textMobilityMode", "AllWordsMovable_GeometryAccess" }
        };

        public static ValidationReport ValidateStructure(LevelDefinition level)
        {
            var report = new ValidationReport();
            if (level == null)
            {
                report.Add(ValidationSeverity.StructureError, "NULL", "Level is null");
                return report;
            }
            if (level.schemaVersion != Tokens.SchemaVersion)
                report.Add(ValidationSeverity.StructureError, "SCHEMA", "schemaVersion must be 9");
            if (level.mechanicsVersion != Tokens.MechanicsVersion)
                report.Add(ValidationSeverity.StructureError, "MECHANICS", "mechanicsVersion must be RW-v0.9");
            if (level.fixedRules != null && level.fixedRules.Length > 0)
                report.Add(ValidationSeverity.StructureError, "FIXED", "fixedRules forbidden; use world TEXT entities");
            if (string.IsNullOrEmpty(level.id))
                report.Add(ValidationSeverity.StructureError, "ID", "Missing level id");
            bool boundsValid = MapResize.TryValidateBounds(level.bounds, out var boundsError);
            if (!boundsValid)
                report.Add(ValidationSeverity.StructureError, "BOUNDS", boundsError);
            if (level.options == null)
            {
                report.Add(ValidationSeverity.StructureError, "OPTIONS", "Missing options");
            }
            else
            {
                // 旧标签仅兼容加载，不能恢复 Shift 推动或登攀。
                if (level.options.actionMode != "MoveClimbHoldPush")
                    AssertOption(report, level.options.actionMode, ExpectedOptions["actionMode"], "actionMode");
                AssertOption(report, level.options.winMode, ExpectedOptions["winMode"], "winMode");
                AssertOption(report, level.options.gravityMode, ExpectedOptions["gravityMode"], "gravityMode");
                AssertOption(report, level.options.collisionMode, ExpectedOptions["collisionMode"], "collisionMode");
                AssertOption(report, level.options.solidityMode, ExpectedOptions["solidityMode"], "solidityMode");
                AssertOption(report, level.options.supportMode, ExpectedOptions["supportMode"], "supportMode");
                AssertOption(report, level.options.controlMode, ExpectedOptions["controlMode"], "controlMode");
                AssertOption(report, level.options.transformationMode, ExpectedOptions["transformationMode"], "transformationMode");
                AssertOption(report, level.options.ruleSourceMode, ExpectedOptions["ruleSourceMode"], "ruleSourceMode");
                AssertOption(report, level.options.textMobilityMode, ExpectedOptions["textMobilityMode"], "textMobilityMode");
                if (level.options.bounceRiseCells != 3)
                    report.Add(ValidationSeverity.StructureError, "BOUNCE", "bounceRiseCells must be 3");
            }

            if (level.tutorial?.regions != null)
            {
                var regionIds = new HashSet<string>();
                foreach (var region in level.tutorial.regions)
                {
                    if (region == null || string.IsNullOrWhiteSpace(region.id) || !regionIds.Add(region.id))
                    {
                        report.Add(ValidationSeverity.StructureError, "REGION_ID", "教学区域 ID 缺失或重复");
                        continue;
                    }
                    if (!region.enabled) continue;
                    var b = region.bounds;
                    if (b == null || b.min.x > b.max.x || b.min.y > b.max.y || b.min.z > b.max.z
                        || !AuthoringOperations.Contains(level, b.min) || !AuthoringOperations.Contains(level, b.max))
                        report.Add(ValidationSeverity.PlaytestError, "REGION_BOUNDS", region.name + "：教学区域必须位于关卡内");
                    if (region.durationSeconds <= 0 || float.IsNaN(region.durationSeconds) || float.IsInfinity(region.durationSeconds))
                        report.Add(ValidationSeverity.PlaytestError, "REGION_TIME", region.name + "：展示秒数必须大于零");
                    if (string.IsNullOrWhiteSpace(region.text))
                        report.Add(ValidationSeverity.Warning, "REGION_TEXT", region.name + "：未填写正文，不会展示");
                }
            }
            if (level.terrain != null)
            {
                foreach (var box in level.terrain)
                {
                    if (box == null)
                    {
                        report.Add(ValidationSeverity.StructureError, "TERRAIN_BOUNDS", "存在空地形盒");
                        continue;
                    }
                    if (box.min.x > box.max.x || box.min.y > box.max.y || box.min.z > box.max.z
                        || !boundsValid || !AuthoringOperations.Contains(level, box.min) || !AuthoringOperations.Contains(level, box.max))
                    {
                        report.Add(ValidationSeverity.StructureError, "TERRAIN_BOUNDS", "地形 " + box.id + " 必须是地图内的有效盒：" + box.min + " 至 " + box.max);
                    }
                }
            }
            foreach (var issue in ValidateAuthoringOccupancy(level).Issues)
                report.Issues.Add(issue);
            var ids = new HashSet<string>();
            int textCount = 0;
            if (level.entities != null)
            {
                foreach (var e in level.entities)
                {
                    if (e == null || string.IsNullOrEmpty(e.id))
                    {
                        report.Add(ValidationSeverity.StructureError, "ENTITY", "Entity missing id");
                        continue;
                    }
                    if (!ids.Add(e.id))
                        report.Add(ValidationSeverity.StructureError, "DUP_ID", "Duplicate entity id " + e.id);
                    var kind = e.kind ?? "";
                    bool isObject = string.Equals(kind, "Object", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(kind, "Color", StringComparison.OrdinalIgnoreCase);
                    bool isText = string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase);
                    if (!isObject && !isText)
                        report.Add(ValidationSeverity.StructureError, "KIND", e.id + " has illegal kind");
                    if (e.anchored)
                        report.Add(ValidationSeverity.StructureError, "ANCHORED", e.id + " v0.9 entities must not be anchored");
                    if (isObject)
                    {
                        var subject = string.IsNullOrEmpty(e.subject) ? e.color : e.subject;
                        if (!Tokens.IsSubject(subject))
                            report.Add(ValidationSeverity.StructureError, "SUBJECT", e.id + " missing legal subject");
                        if (!string.IsNullOrEmpty(e.token))
                            report.Add(ValidationSeverity.Warning, "TOKEN", e.id + " object entity should have empty token");
                    }
                    else if (isText)
                    {
                        textCount++;
                        if (!Tokens.IsLegalWord(e.token))
                            report.Add(ValidationSeverity.StructureError, "TOKEN", e.id + " unknown token " + e.token);
                    }
                    if (level.bounds != null)
                    {
                        var bounds = new GridBounds(level.bounds.min, level.bounds.max);
                        if (!bounds.Contains(e.cell))
                            report.Add(ValidationSeverity.StructureError, "OOB", e.id + " outside bounds");
                    }
                }
            }
            if (textCount == 0)
                report.Add(ValidationSeverity.StructureError, "WORDS", "No world TEXT entities; rules must be spatial");

            if (level.designContract != null && level.referenceSolutions != null)
            {
                if (level.referenceSolutions.Length < level.designContract.minimumSolutionFamilies)
                    report.Add(ValidationSeverity.StructureError, "SOLUTIONS", "referenceSolutions fewer than minimumSolutionFamilies");
            }

            void CheckCommands(string[] commands, string label)
            {
                if (commands == null) return;
                foreach (var c in commands)
                {
                    if (Tokens.LegacyJumps.Contains(c))
                        report.Add(ValidationSeverity.PlaytestError, "LEGACY", label + " contains removed jump " + c);
                    else if (!Tokens.Commands.Contains(c))
                        report.Add(ValidationSeverity.PlaytestError, "CMD", label + " unknown command " + c);
                }
            }

            if (level.referenceSolutions != null)
            {
                foreach (var sol in level.referenceSolutions)
                    CheckCommands(sol?.commands, "referenceSolutions");
            }
            CheckCommands(level.referenceSolution?.commands, "referenceSolution");
            return report;
        }

        static void AssertOption(ValidationReport report, string actual, string expected, string name)
        {
            if (actual != expected)
                report.Add(ValidationSeverity.StructureError, "OPTION", name + " must be " + expected);
        }

        public static ValidationReport ValidateForPlaytest(LevelDefinition level)
        {
            var report = ValidateStructure(level);
            if (report.HasStructureErrors) return report;
            try
            {
                var world = WorldModel.FromLevel(level);
                int youCount = 0;
                foreach (var e in world.Entities)
                    if (PropertyResolver.HasYou(e, world.Rules)) youCount++;
                if (youCount != 1)
                    report.Add(ValidationSeverity.PlaytestError, "YOU", "Playtest requires exactly one YOU, found " + youCount);
                if (world.WonLatched)
                    report.Add(ValidationSeverity.PlaytestError, "WON", "Initial state already won");
                var before = world.Fingerprint();
                world.Settle();
                if (world.Fingerprint() != before)
                    report.Add(ValidationSeverity.PlaytestError, "UNSTABLE", "Initial state is not stable under gravity");
                if (world.WonLatched && before != world.Fingerprint())
                    report.Add(ValidationSeverity.PlaytestError, "SETTLE_WIN", "Initial settle produced a win");
            }
            catch (Exception ex)
            {
                report.Add(ValidationSeverity.PlaytestError, "BUILD", ex.Message);
            }
            return report;
        }
    }

    public static class InitialStateBuilder
    {
        public static WorldModel Build(LevelDefinition level)
        {
            var report = LevelValidator.ValidateStructure(level);
            if (report.HasStructureErrors)
                throw new InvalidOperationException(report.ToString());
            return WorldModel.FromLevel(level);
        }
    }
}
