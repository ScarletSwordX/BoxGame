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
        public static ValidationReport ValidateStructure(LevelDefinition level)
        {
            var report = new ValidationReport();
            if (level == null)
            {
                report.Add(ValidationSeverity.StructureError, "NULL", "Level is null");
                return report;
            }
            if (level.schemaVersion != Tokens.SchemaVersion)
                report.Add(ValidationSeverity.StructureError, "SCHEMA", "schemaVersion must be 5");
            if (level.mechanicsVersion != Tokens.MechanicsVersion)
                report.Add(ValidationSeverity.StructureError, "MECHANICS", "mechanicsVersion must be RP-v0.5");
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
                AssertOption(report, level.options.supportMode, "StrictBelow", "supportMode");
                AssertOption(report, level.options.jumpMode, "LandingBounce3", "jumpMode");
                AssertOption(report, level.options.decisionMode, "GroundedOrBounceApex", "decisionMode");
                AssertOption(report, level.options.winMode, "DistinctEntitiesSameCell", "winMode");
                AssertOption(report, level.options.winCheckMode, "AfterAtomicLogicChange", "winCheckMode");
                AssertOption(report, level.options.actionMode, "FourWayMoveInPlaceJumpApexSteer", "actionMode");
                AssertOption(report, level.options.gravityMode, "WorldDownExceptHoverOrFly", "gravityMode");
                AssertOption(report, level.options.playerBlockMode, "ImplicitFromYou", "playerBlockMode");
                if (level.options.bounceRiseCells != 3)
                    report.Add(ValidationSeverity.StructureError, "BOUNCE", "bounceRiseCells must be 3");
            }

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
                    if (!string.Equals(kind, "Color", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase))
                        report.Add(ValidationSeverity.StructureError, "KIND", e.id + " has illegal kind");
                    if (string.Equals(kind, "Color", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Tokens.IsColor(e.color))
                            report.Add(ValidationSeverity.StructureError, "COLOR", e.id + " missing legal color");
                        if (!string.IsNullOrEmpty(e.token))
                            report.Add(ValidationSeverity.Warning, "TOKEN", e.id + " color entity should have empty token");
                        if (e.anchored)
                            report.Add(ValidationSeverity.StructureError, "ANCHORED", e.id + " Color cannot be anchored in P0");
                    }
                    else
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

            if (level.referenceSolution?.commands != null)
            {
                foreach (var c in level.referenceSolution.commands)
                {
                    if (Tokens.LegacyJumps.Contains(c))
                        report.Add(ValidationSeverity.PlaytestError, "LEGACY", "Reference solution contains removed jump " + c);
                    else if (!Tokens.SupportedCommands.Contains(c))
                        report.Add(ValidationSeverity.PlaytestError, "CMD", "Unknown reference command " + c);
                }
            }
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
