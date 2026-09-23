using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public enum EntityKind
    {
        Object,
        Text
    }

    public enum GravityMode
    {
        Down,
        Hover,
        Up,
        Anchored
    }

    public enum MotionPhase
    {
        Grounded,
        BounceApex,
        NoControl,
        Won
    }

    public sealed class EntityState
    {
        public string Id;
        public EntityKind Kind;
        public string Subject;
        public string Token;
        public GridCell Cell;
        public bool Anchored;

        public EntityState Clone()
        {
            return new EntityState
            {
                Id = Id,
                Kind = Kind,
                Subject = Subject,
                Token = Token,
                Cell = Cell,
                Anchored = Anchored
            };
        }

        public static EntityKind ParseKind(string kind)
        {
            if (string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase)) return EntityKind.Text;
            if (string.Equals(kind, "Object", StringComparison.OrdinalIgnoreCase)) return EntityKind.Object;
            // 遗留 Color 视为 Object
            if (string.Equals(kind, "Color", StringComparison.OrdinalIgnoreCase)) return EntityKind.Object;
            throw new ArgumentException("Unknown entity kind: " + kind);
        }
    }

    public sealed class BounceApexState
    {
        public string SurfaceId;
        public GridCell ContactPos;
        public GridCell ApexPos;
        public int RiseCells;
        public bool ResumeForcedFall = true;

        public BounceApexState Clone()
        {
            return new BounceApexState
            {
                SurfaceId = SurfaceId,
                ContactPos = ContactPos,
                ApexPos = ApexPos,
                RiseCells = RiseCells,
                ResumeForcedFall = ResumeForcedFall
            };
        }
    }

    public sealed class WinRecord
    {
        public string YouId;
        public string WinId;
        public GridCell Cell;
        public string Cause;

        public WinRecord Clone()
        {
            return new WinRecord { YouId = YouId, WinId = WinId, Cell = Cell, Cause = Cause };
        }
    }

    public sealed class TransformSource
    {
        public string Source;
        public string Target;
        public object Origin;
    }

    public sealed class WorldSnapshot
    {
        public List<EntityState> Entities;
        public Dictionary<string, BounceApexState> Apex;
        public List<string> ForcedFall;
        public bool Won;
        public WinRecord WinRecord;
    }
}
