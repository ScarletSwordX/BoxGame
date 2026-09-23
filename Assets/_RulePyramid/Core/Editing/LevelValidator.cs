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
        static readonly Dictionary<string, string> ExpectedOptions = new Dictionary<string, string>
        {
            { "actionMode", "MoveClimbHoldPush" },
            { "winMode", "DistinctEntitiesSameCell" },
            { "gravityMode", "WorldDownExceptHoverOrFly" },
            { "collisionMode", "SolidPairsTerrainUniversal" },
            { "solidityMode", "YouPushStopOrText" },
            { "supportMode", "StrictBelow" },
            { "controlMode", "SingleYouTransfer_NoControlUndo" },
            { "transformationMode", "PermanentSingleTarget_SimultaneousOncePerEntityPerCommand" }
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
                report.Add(ValidationSeverity.StructureError, "SCHEMA", "schemaVersion must be 8");
            if (level.mechanicsVersion != Tokens.MechanicsVersion)
                report.Add(ValidationSeverity.StructureError, "MECHANICS", "mechanicsVersion must be RW-v0.8");
            if (string.IsNullOrEmpty(level.id))
                report.Add(ValidationSeverity.StructureError, "ID", "Missing level id");
            if (level.bounds == null)
                report.Add(ValidationSeverity.StructureError, "BOUNDS", "Missing bounds");
            if (level.options == null)
            {
                report.Add(ValidationSeverity.StructureError, "OPTIONS", "Missing options");
            }
            else
            {
                AssertOption(report, level.options.actionMode, ExpectedOptions["actionMode"], "actionMode");
                AssertOption(report, level.options.winMode, ExpectedOptions["winMode"], "winMode");
                AssertOption(report, level.options.gravityMode, ExpectedOptions["gravityMode"], "gravityMode");
                AssertOption(report, level.options.collisionMode, ExpectedOptions["collisionMode"], "collisionMode");
                AssertOption(report, level.options.solidityMode, ExpectedOptions["solidityMode"], "solidityMode");
                AssertOption(report, level.options.supportMode, ExpectedOptions["supportMode"], "supportMode");
                AssertOption(report, level.options.controlMode, ExpectedOptions["controlMode"], "controlMode");
                AssertOption(report, level.options.transformationMode, ExpectedOptions["transformationMode"], "transformationMode");
                if (level.options.bounceRiseCells != 3)
                    report.Add(ValidationSeverity.StructureError, "BOUNCE", "bounceRiseCells must be 3");
            }

            var terrainCells = LevelCloner.ExpandTerrain(level.terrain);
            var ids = new HashSet<string>();
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
                    if (isObject)
                    {
                        var subject = string.IsNullOrEmpty(e.subject) ? e.color : e.subject;
                        if (!Tokens.IsSubject(subject))
                            report.Add(ValidationSeverity.StructureError, "SUBJECT", e.id + " missing legal subject");
                        if (!string.IsNullOrEmpty(e.token))
                            report.Add(ValidationSeverity.Warning, "TOKEN", e.id + " object entity should have empty token");
                        if (e.anchored)
                            report.Add(ValidationSeverity.StructureError, "ANCHORED", e.id + " Object cannot be anchored");
                    }
                    else if (isText)
                    {
                        if (!Tokens.IsLegalWord(e.token))
                            report.Add(ValidationSeverity.StructureError, "TOKEN", e.id + " unknown token " + e.token);
                    }
                    if (level.bounds != null)
                    {
                        var bounds = new GridBounds(level.bounds.min, level.bounds.max);
                        if (!bounds.Contains(e.cell))
                            report.Add(ValidationSeverity.StructureError, "OOB", e.id + " outside bounds");
                    }
                    if (terrainCells.Contains(e.cell))
                        report.Add(ValidationSeverity.StructureError, "TERRAIN", e.id + " overlaps terrain");
                }
            }

            if (level.fixedRules != null)
            {
                foreach (var rule in level.fixedRules)
                {
                    if (rule?.tokens == null) continue;
                    foreach (var t in rule.tokens)
                    {
                        if (!Tokens.IsLegalWord(t))
                            report.Add(ValidationSeverity.StructureError, "FIXED", "Unknown fixed token " + t);
                    }
                }
            }

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
