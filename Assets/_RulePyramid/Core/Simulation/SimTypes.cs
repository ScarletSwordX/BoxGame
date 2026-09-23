using System.Collections.Generic;

namespace RulePyramid.Core
{
    public sealed class SimEvent
    {
        public string Kind;
        public string EntityId;
        public GridCell From;
        public GridCell To;
        public string SurfaceId;
        public GridCell ContactPos;
        public GridCell ApexPos;
        public int RequestedRise;
        public int ActualRise;
        public string YouId;
        public string WinId;
        public GridCell Cell;
        public string Cause;
        public string OldId;
        public string NewId;
        public string BeforeSubject;
        public string AfterSubject;
        public string Message;
        public string EntityKind;
        public string CommandEffect;
        public string Steer;
        public object Before;
        public object After;
        public object Sources;

        public static SimEvent Move(string kind, string id, GridCell from, GridCell to)
        {
            return new SimEvent { Kind = kind, EntityId = id, From = from, To = to };
        }
    }

    public enum CommandKind
    {
        Move,
        PushMove,
        JumpInPlace,
        ResumeBounceDescent,
        Wait,
        Camera
    }

    public sealed class SimCommand
    {
        public CommandKind Kind;
        public WorldDirection Direction;
        public string Raw;
        public bool IsPush;

        public static bool TryParse(string token, out SimCommand command, out string error)
        {
            command = null;
            error = null;
            if (string.IsNullOrEmpty(token))
            {
                error = "Empty command";
                return false;
            }
            if (Tokens.LegacyJumps.Contains(token))
            {
                error = "Removed directional jump";
                return false;
            }
            if (token == "CAM+" || token == "CAM-")
            {
                command = new SimCommand { Kind = CommandKind.Camera, Raw = token };
                return true;
            }
            if (Tokens.PushCommands.Contains(token))
            {
                var dirToken = token.Substring(1);
                if (!WorldDirections.TryParse(dirToken, out var pushDir))
                {
                    error = "Unknown command: " + token;
                    return false;
                }
                command = new SimCommand
                {
                    Kind = CommandKind.PushMove,
                    Direction = pushDir,
                    Raw = token,
                    IsPush = true
                };
                return true;
            }
            if (WorldDirections.TryParse(token, out var dir))
            {
                command = new SimCommand { Kind = CommandKind.Move, Direction = dir, Raw = token };
                return true;
            }
            if (token == "J")
            {
                command = new SimCommand { Kind = CommandKind.JumpInPlace, Raw = token };
                return true;
            }
            if (token == "WAIT")
            {
                command = new SimCommand { Kind = CommandKind.Wait, Raw = token };
                return true;
            }
            error = "Unknown command: " + token;
            return false;
        }
    }

    public enum StepStatus
    {
        Accepted,
        Rejected,
        Unsupported,
        SimulationError
    }

    public sealed class StepResult
    {
        public StepStatus Status;
        public WorldModel World;
        public List<SimEvent> Events = new List<SimEvent>();
        public string Message;
        public bool Won;

        public static StepResult Rejected(string message)
        {
            return new StepResult { Status = StepStatus.Rejected, Message = message };
        }
    }

    public sealed class VictoryCommittedException : System.Exception
    {
        public VictoryCommittedException() : base("Won") { }
    }

    public sealed class RuleConflictException : System.Exception
    {
        public RuleConflictException(string message) : base(message) { }
    }

    public sealed class UnsupportedContactException : System.Exception
    {
        public UnsupportedContactException(string message) : base(message) { }
    }
}
