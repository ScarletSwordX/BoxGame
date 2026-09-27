using System;
using System.Collections.Generic;
using RulePyramid.Core;

namespace RulePyramid.Runtime
{
    // 只描述过场画面，不修改任何阶段的模拟坐标或作者初态。
    public sealed class StageTransitionLayout
    {
        public struct Surface
        {
            public GridCell Cell;
            public bool Glass, Structure;
        }

        public readonly List<Surface> Retained = new List<Surface>();
        public readonly List<Surface> Removed = new List<Surface>();
        public readonly List<Surface> Added = new List<Surface>();
        public GridCell NextOffset { get; private set; }
        public GridCellBox FramingBounds { get; private set; }

        public static StageTransitionLayout Between(WorldModel previous, WorldModel next)
        {
            if (previous == null || next == null) throw new ArgumentNullException();
            var oldBounds = previous.Spec.bounds;
            var newBounds = next.Spec.bounds;
            var plan = new StageTransitionLayout
            {
                NextOffset = new GridCell(oldBounds.max.x - newBounds.max.x, 0, oldBounds.max.z - newBounds.max.z)
            };
            var offset = plan.NextOffset;
            var min = newBounds.min.Add(offset);
            var max = newBounds.max.Add(offset);
            plan.FramingBounds = new GridCellBox
            {
                min = new GridCell(Math.Min(oldBounds.min.x, min.x), Math.Min(oldBounds.min.y, min.y), Math.Min(oldBounds.min.z, min.z)),
                max = new GridCell(Math.Max(oldBounds.max.x, max.x), Math.Max(oldBounds.max.y, max.y), Math.Max(oldBounds.max.z, max.z))
            };
            var before = Surfaces(previous, new GridCell());
            var after = Surfaces(next, offset);
            foreach (var pair in before)
                if (after.TryGetValue(pair.Key, out var surface) && SameAppearance(pair.Value, surface)) plan.Retained.Add(surface);
                else plan.Removed.Add(pair.Value);
            foreach (var pair in after)
                if (!before.TryGetValue(pair.Key, out var surface) || !SameAppearance(pair.Value, surface)) plan.Added.Add(pair.Value);
            return plan;
        }

        static bool SameAppearance(Surface a, Surface b) => a.Glass == b.Glass && a.Structure == b.Structure;

        static Dictionary<GridCell, Surface> Surfaces(WorldModel world, GridCell offset)
        {
            var result = new Dictionary<GridCell, Surface>();
            int ground = int.MaxValue;
            foreach (var cell in world.Terrain) ground = Math.Min(ground, cell.y);
            foreach (var cell in world.Terrain)
            {
                var aligned = cell.Add(offset);
                result[aligned] = new Surface { Cell = aligned, Structure = cell.y > ground };
            }
            if (world.Spec.terrain != null)
            foreach (var box in world.Spec.terrain)
            {
                if (box == null) continue;
                bool glass = string.Equals(box.appearance, "TransparentGlass", StringComparison.OrdinalIgnoreCase);
                for (int y = box.min.y; y <= box.max.y; y++)
                for (int z = box.min.z; z <= box.max.z; z++)
                for (int x = box.min.x; x <= box.max.x; x++)
                {
                    var aligned = new GridCell(x, y, z).Add(offset);
                    if (result.ContainsKey(aligned)) result[aligned] = new Surface { Cell = aligned, Glass = glass, Structure = box.min.y > ground };
                }
            }
            return result;
        }
    }
}
