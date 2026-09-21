using System;

namespace RulePyramid.Core
{
    [Serializable]
    public struct GridCell : IEquatable<GridCell>
    {
        public int x;
        public int y;
        public int z;

        public GridCell(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public GridCell Add(int dx, int dy, int dz)
        {
            return new GridCell(x + dx, y + dy, z + dz);
        }

        public GridCell Add(GridCell other)
        {
            return Add(other.x, other.y, other.z);
        }

        public bool Equals(GridCell other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCell other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ z;
                return hash;
            }
        }

        public override string ToString()
        {
            return "(" + x + "," + y + "," + z + ")";
        }

        public static bool operator ==(GridCell a, GridCell b) => a.Equals(b);
        public static bool operator !=(GridCell a, GridCell b) => !a.Equals(b);

        public static readonly GridCell Up = new GridCell(0, 1, 0);
        public static readonly GridCell Down = new GridCell(0, -1, 0);
        public static readonly GridCell East = new GridCell(1, 0, 0);
        public static readonly GridCell West = new GridCell(-1, 0, 0);
        public static readonly GridCell North = new GridCell(0, 0, 1);
        public static readonly GridCell South = new GridCell(0, 0, -1);
    }
}
