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
            if (World.WonLatched && command != null && command != "WAIT")
            {
                LastRejectReason = "Already won";
                return false;
            }
            bool ok = World.TryCommand(command, out var message);
            if (!ok)
            {
                LastRejectReason = message ?? "Rejected";
                return false;
            }
            TurnCount++;
            return true;
        }

        public bool Undo()
        {
            if (!World.Undo()) return false;
            if (TurnCount > 0) TurnCount--;
            LastRejectReason = null;
            return true;
        }

        public void Restart()
        {
            World = WorldModel.FromLevel(Level);
            TurnCount = 0;
            LastRejectReason = null;
        }
    }
}
