using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorAxisHandleMathTests
    {
        [TestCase(EditorMoveAxis.X, 1, 0, 0)]
        [TestCase(EditorMoveAxis.Y, 0, 1, 0)]
        [TestCase(EditorMoveAxis.Z, 0, 0, 1)]
        public void PositiveAndNegativeOffsetsFollowOneAxis(EditorMoveAxis axis, int x, int y, int z)
        {
            Assert.AreEqual(new GridCell(x * 3, y * 3, z * 3), EditorAxisHandleMath.Offset(axis, 3));
            Assert.AreEqual(new GridCell(-x * 2, -y * 2, -z * 2), EditorAxisHandleMath.Offset(axis, -2));
            Assert.AreEqual(new Vector3(x, y, z), EditorAxisHandleMath.Direction(axis));
        }

        [Test]
        public void NoAxisProducesNoMovement()
        {
            Assert.AreEqual(Vector3.zero, EditorAxisHandleMath.Direction(EditorMoveAxis.None));
            Assert.AreEqual(new GridCell(0, 0, 0), EditorAxisHandleMath.Offset(EditorMoveAxis.None, 5));
        }

        [Test]
        public void DragProjectsDiagonalMovementOntoAxis()
        {
            var projectedUnit = new Vector2(12f, -16f);
            Assert.AreEqual(2, EditorAxisHandleMath.DragSteps(new Vector2(24f, -32f), projectedUnit));
            Assert.AreEqual(-2, EditorAxisHandleMath.DragSteps(new Vector2(-24f, 32f), projectedUnit));
            Assert.AreEqual(0, EditorAxisHandleMath.DragSteps(new Vector2(16f, 12f), projectedUnit));
        }

        [Test]
        public void DragRoundsToWholeGridSteps()
        {
            var projectedUnit = new Vector2(20f, 0f);
            Assert.AreEqual(1, EditorAxisHandleMath.DragSteps(new Vector2(11f, 9f), projectedUnit));
            Assert.AreEqual(0, EditorAxisHandleMath.DragSteps(new Vector2(9f, 100f), projectedUnit));
        }

        [Test]
        public void TinyOrInvalidProjectionCannotMoveBlock()
        {
            Assert.AreEqual(0, EditorAxisHandleMath.DragSteps(new Vector2(1000f, 0f), new Vector2(0.01f, 0f)));
            Assert.AreEqual(0, EditorAxisHandleMath.DragSteps(new Vector2(float.NaN, 0f), new Vector2(10f, 0f)));
            Assert.AreEqual(0, EditorAxisHandleMath.DragSteps(new Vector2(100f, 0f), new Vector2(float.PositiveInfinity, 0f)));
        }

        [Test]
        public void SegmentDistanceUsesClosestPointWithinEndpoints()
        {
            var start = new Vector2(1f, 2f);
            var end = new Vector2(11f, 2f);
            Assert.AreEqual(3f, EditorAxisHandleMath.SegmentDistance(new Vector2(6f, 5f), start, end), 0.0001f);
            Assert.AreEqual(5f, EditorAxisHandleMath.SegmentDistance(new Vector2(14f, 6f), start, end), 0.0001f);
            Assert.AreEqual(5f, EditorAxisHandleMath.SegmentDistance(new Vector2(4f, 6f), start, start), 0.0001f);
        }
    }
}
