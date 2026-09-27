using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Runtime
{
    /// <summary>顶点箭头只查询当前一格是否可侧移，不模拟落地或搜索解法。</summary>
    public static class BounceApexArrows
    {
        public static bool CanSteer(WorldModel world, WorldDirection direction)
        {
            if (world == null || world.Phase != MotionPhase.BounceApex) return false;
            var actor = world.Entity(world.ActorId);
            var offset = WorldDirections.ToOffset(direction);
            if (actor == null || offset == default(GridCell)) return false;
            // 侧移目标与自身格不同；与顶点输入使用同一 Free / BlocksPair 判定。
            return world.Free(actor.Cell.Add(offset), null, actor);
        }

        public static Vector2 ProjectDirection(Camera camera, Vector3 origin, WorldDirection direction)
        {
            if (camera == null) return Vector2.zero;
            var offset = WorldDirections.ToOffset(direction);
            var a = camera.WorldToViewportPoint(origin);
            var b = camera.WorldToViewportPoint(origin + new Vector3(offset.x, offset.y, offset.z));
            var rect = camera.pixelRect;
            return new Vector2((b.x - a.x) * rect.width, (a.y - b.y) * rect.height).normalized;
        }
    }
}
