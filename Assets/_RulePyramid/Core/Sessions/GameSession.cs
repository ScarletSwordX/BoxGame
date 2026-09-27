using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public sealed class GameSession
    {
        public LevelDefinition Level { get; private set; }
        public WorldModel World { get; private set; }
        public int TurnCount { get; private set; }
        public string LastRejectReason { get; private set; }
        public IReadOnlyList<GridCell> LastControlledCells { get; private set; } = Array.Empty<GridCell>();

        public GameSession(LevelDefinition level)
        {
            Level = LevelCloner.Clone(level);
            World = WorldModel.FromLevel(Level);
            TurnCount = 0;
        }

        public MotionPhase Phase => World.Phase;
        public bool Won => World.WonLatched;

        public bool TryExecute(string command)
        {
            LastRejectReason = null;
            LastControlledCells = Array.Empty<GridCell>();
            if (World.WonLatched && command != null && command != "WAIT")
            {
                LastRejectReason = "Already won";
                return false;
            }
            var visited = new List<GridCell>();
            void Record(GridCell cell) => visited.Add(cell);
            World.ControlledCellVisited += Record;
            bool ok;
            string message;
            try { ok = World.TryCommand(command, out message); }
            finally { World.ControlledCellVisited -= Record; }
            if (!ok)
            {
                LastRejectReason = message ?? World.LastRejection ?? "Rejected";
                return false;
            }
            TurnCount++;
            LastControlledCells = visited.ToArray();
            return true;
        }

        public bool Undo()
        {
            LastControlledCells = Array.Empty<GridCell>();
            if (!World.Undo()) return false;
            if (TurnCount > 0) TurnCount--;
            LastRejectReason = null;
            return true;
        }

        public void Restart()
        {
            LastControlledCells = Array.Empty<GridCell>();
            World = WorldModel.FromLevel(Level);
            TurnCount = 0;
            LastRejectReason = null;
        }
    }
}
