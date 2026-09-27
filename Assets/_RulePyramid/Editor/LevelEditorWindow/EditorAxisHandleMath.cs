using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Editor
{
    public enum EditorMoveAxis
    {
        None = -1,
        X = 0,
        Y = 1,
        Z = 2
    }

    public static class EditorAxisHandleMath
    {
        public const float MinimumProjectedLengthSqr = 0.001f;

        public static Vector3 Direction(EditorMoveAxis axis)
        {
            switch (axis)
            {
                case EditorMoveAxis.X: return Vector3.right;
                case EditorMoveAxis.Y: return Vector3.up;
                case EditorMoveAxis.Z: return Vector3.forward;
                default: return Vector3.zero;
            }
        }

        public static GridCell Offset(EditorMoveAxis axis, int steps)
        {
            switch (axis)
            {
                case EditorMoveAxis.X: return new GridCell(steps, 0, 0);
                case EditorMoveAxis.Y: return new GridCell(0, steps, 0);
                case EditorMoveAxis.Z: return new GridCell(0, 0, steps);
                default: return new GridCell(0, 0, 0);
            }
        }

        public static int DragSteps(Vector2 mouseDelta, Vector2 projectedUnit)
        {
            float unitLengthSqr = projectedUnit.sqrMagnitude;
            if (!IsFinite(mouseDelta.x) || !IsFinite(mouseDelta.y) ||
                !IsFinite(projectedUnit.x) || !IsFinite(projectedUnit.y) ||
                !IsFinite(unitLengthSqr) || unitLengthSqr < MinimumProjectedLengthSqr)
                return 0;

            float steps = Vector2.Dot(mouseDelta, projectedUnit) / unitLengthSqr;
            if (!IsFinite(steps)) return 0;
            if (steps >= int.MaxValue) return int.MaxValue;
            if (steps <= int.MinValue) return int.MinValue;
            return Mathf.RoundToInt(steps);
        }

        public static float SegmentDistance(Vector2 mouse, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= Mathf.Epsilon) return Vector2.Distance(mouse, start);

            float fraction = Mathf.Clamp01(Vector2.Dot(mouse - start, segment) / lengthSqr);
            return Vector2.Distance(mouse, start + fraction * segment);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
