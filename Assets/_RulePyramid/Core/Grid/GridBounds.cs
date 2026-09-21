using System;

namespace RulePyramid.Core
{
    [Serializable]
    public struct GridBounds
    {
        public GridCell min;
        public GridCell max;

        public GridBounds(GridCell min, GridCell max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Contains(GridCell cell)
        {
            return cell.x >= min.x && cell.x <= max.x
                && cell.y >= min.y && cell.y <= max.y
                && cell.z >= min.z && cell.z <= max.z;
        }
    }
}
