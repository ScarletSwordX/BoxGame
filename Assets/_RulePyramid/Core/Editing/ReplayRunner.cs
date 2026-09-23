using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>同一初态独立会话回放参考解。</summary>
    public static class ReplayRunner
    {
        public sealed class ReplayResult
        {
            public string SolutionId;
            public bool Won;
            public string FailReason;
            public string ActorIdAtWin;
            public string WinId;
            public HashSet<string> EventKinds = new HashSet<string>();
            public int Turns;
        }

        public static ReplayResult Run(LevelDefinition level, ReferenceSolutionData solution)
        {
            var result = new ReplayResult { SolutionId = solution != null ? solution.id : null };
            if (level == null || solution == null || solution.commands == null)
            {
                result.FailReason = "Missing level or solution";
                return result;
            }
            var session = new GameSession(level);
            foreach (var cmd in solution.commands)
            {
                if (!session.TryExecute(cmd))
                {
                    result.FailReason = "Rejected " + cmd + ": " + session.LastRejectReason;
                    return result;
                }
                foreach (var ev in session.World.Log)
                    if (!string.IsNullOrEmpty(ev.Kind)) result.EventKinds.Add(ev.Kind);
            }
            result.Turns = session.TurnCount;
            result.Won = session.Won;
            if (session.World.WinRecord != null)
            {
                result.ActorIdAtWin = session.World.WinRecord.YouId;
                result.WinId = session.World.WinRecord.WinId;
            }
            if (!result.Won)
            {
                result.FailReason = "Did not win";
                return result;
            }
            if (!string.IsNullOrEmpty(solution.mustControlAtWin) &&
                !string.Equals(result.ActorIdAtWin, solution.mustControlAtWin, StringComparison.Ordinal))
            {
                result.FailReason = "mustControlAtWin expected " + solution.mustControlAtWin + " got " + result.ActorIdAtWin;
                result.Won = false;
                return result;
            }
            if (!string.IsNullOrEmpty(solution.mustWinWith) &&
                !string.Equals(result.WinId, solution.mustWinWith, StringComparison.Ordinal))
            {
                result.FailReason = "mustWinWith expected " + solution.mustWinWith + " got " + result.WinId;
                result.Won = false;
                return result;
            }
            if (solution.requireEvents != null)
            {
                foreach (var req in solution.requireEvents)
                {
                    if (string.IsNullOrEmpty(req)) continue;
                    if (!result.EventKinds.Contains(req))
                    {
                        result.FailReason = "Missing requireEvent " + req;
                        result.Won = false;
                        return result;
                    }
                }
            }
            if (solution.forbidEvents != null)
            {
                foreach (var forb in solution.forbidEvents)
                {
                    if (string.IsNullOrEmpty(forb)) continue;
                    if (result.EventKinds.Contains(forb))
                    {
                        result.FailReason = "Hit forbidEvent " + forb;
                        result.Won = false;
                        return result;
                    }
                }
            }
            return result;
        }

        public static List<ReplayResult> RunAll(LevelDefinition level)
        {
            var list = new List<ReplayResult>();
            if (level?.referenceSolutions == null) return list;
            foreach (var sol in level.referenceSolutions)
                list.Add(Run(level, sol));
            return list;
        }
    }

    /// <summary>无互动子图审核（开发工具）；不参与胜利判定。</summary>
    public static class InteractionAudit
    {
        public static bool IsActiveInteraction(IEnumerable<SimEvent> events)
        {
            if (events == null) return false;
            foreach (var e in events)
            {
                if (e.Kind == "ActiveInteraction" || e.Kind == "RulesChanged")
                    return true;
            }
            return false;
        }

        public sealed class AuditResult
        {
            public string Status;
            public int States;
            public int Attempts;
            public string[] Path;
        }

        public static AuditResult AuditNoInteraction(LevelDefinition level, int cap = 20000)
        {
            var model = WorldModel.FromLevel(level);
            var queue = new Queue<(WorldSnapshot snap, List<string> path)>();
            queue.Enqueue((model.Snapshot(), new List<string>()));
            var seen = new HashSet<string> { model.Fingerprint() };
            int attempts = 0;
            var commands = new[] { "E", "W", "N", "S", "PE", "PW", "PN", "PS", "J", "WAIT" };
            while (queue.Count > 0)
            {
                var (snap, path) = queue.Dequeue();
                foreach (var command in commands)
                {
                    model.Restore(snap);
                    model.History.Clear();
                    model.Log.Clear();
                    attempts++;
                    if (!model.TryCommand(command, out _))
                        continue;
                    if (IsActiveInteraction(model.Log))
                        continue;
                    if (model.WonLatched)
                    {
                        var winPath = new List<string>(path) { command };
                        return new AuditResult
                        {
                            Status = "TRAVERSAL_WIN_FOUND",
                            States = seen.Count,
                            Attempts = attempts,
                            Path = winPath.ToArray()
                        };
                    }
                    var key = model.Fingerprint();
                    if (seen.Add(key))
                    {
                        if (seen.Count >= cap)
                            return new AuditResult { Status = "INCONCLUSIVE_LIMIT", States = seen.Count, Attempts = attempts };
                        queue.Enqueue((model.Snapshot(), new List<string>(path) { command }));
                    }
                }
            }
            return new AuditResult
            {
                Status = "EXHAUSTED_NO_INTERACTION_WIN",
                States = seen.Count,
                Attempts = attempts
            };
        }
    }
}
