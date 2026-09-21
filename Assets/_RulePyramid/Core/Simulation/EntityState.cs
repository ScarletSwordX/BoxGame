using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public enum EntityKind
    {
        Color,
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
        Won
    }

    public sealed class EntityState
    {
        public string Id;
        public EntityKind Kind;
        public string Color;
        public string Token;
        public GridCell Cell;
        public bool Anchored;

        public EntityState Clone()
        {
            return new EntityState
            {
                Id = Id,
                Kind = Kind,
                Color = Color,
                Token = Token,
                Cell = Cell,
                Anchored = Anchored
            };
        }

        public static EntityKind ParseKind(string kind)
        {
            if (string.Equals(kind, "Text", StringComparison.OrdinalIgnoreCase)) return EntityKind.Text;
            if (string.Equals(kind, "Color", StringComparison.OrdinalIgnoreCase)) return EntityKind.Color;
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

    public sealed class WorldSnapshot
    {
        public List<EntityState> Entities;
        public Dictionary<string, BounceApexState> Apex;
        public List<string> ForcedFall;
        public bool Won;
        public WinRecord WinRecord;
    }
}
