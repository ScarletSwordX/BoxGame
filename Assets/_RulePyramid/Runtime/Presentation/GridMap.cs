using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    public static class GridMap
    {
        public static Vector3 ToWorld(GridCell cell, VisualConfig config)
        {
            float size = config != null ? config.cellSize : 1f;
            Vector3 origin = config != null ? config.origin : Vector3.zero;
            return origin + new Vector3(cell.x * size, cell.y * size, cell.z * size);
        }
    }
}
