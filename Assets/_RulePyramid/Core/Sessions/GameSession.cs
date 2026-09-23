using System;

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
            if (World.WonLatched && command != null && command != "WAIT" && command != "CAM+" && command != "CAM-")
            {
                LastRejectReason = "Already won";
                return false;
            }
            bool ok = World.TryCommand(command, out var message);
            if (!ok)
            {
                LastRejectReason = message ?? World.LastRejection ?? "Rejected";
                return false;
            }
            if (command != "CAM+" && command != "CAM-")
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
