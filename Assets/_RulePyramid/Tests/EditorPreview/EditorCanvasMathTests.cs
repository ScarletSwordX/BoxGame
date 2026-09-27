using NUnit.Framework;
using RulePyramid.Core;
using RulePyramid.Editor;
using UnityEngine;

namespace RulePyramid.Tests.EditorPreview
{
    public class EditorCanvasMathTests
    {
        static GridCellBox Box(int minX, int minZ, int maxX, int maxZ)
        {
            return new GridCellBox
            {
                min = new GridCell(minX, 0, minZ),
                max = new GridCell(maxX, 0, maxZ)
            };
        }

        static Rect TargetRect(EditorCanvasFit fit, GridCellBox map, GridCellBox target)
        {
            float x = 24f + fit.Pan.x + (target.min.x - map.min.x) * fit.Zoom;
            float y = 26f + fit.Pan.y + (map.max.z - target.max.z) * fit.Zoom;
            return new Rect(x, y,
                (target.max.x - target.min.x + 1) * fit.Zoom,
                (target.max.z - target.min.z + 1) * fit.Zoom);
        }

        [Test]
        public void FullMapFitsInsideMarginsAndIsCentered()
        {
            var map = Box(-12, -8, 19, 7);
            var viewport = new Rect(0, 0, 800, 520);
            var fit = EditorCanvasMath.Fit(viewport, map, map);
            var result = TargetRect(fit, map, map);

            Assert.AreEqual(23.5f, fit.Zoom, .001f);
            Assert.AreEqual(viewport.center.x, result.center.x, .001f);
            Assert.AreEqual(viewport.center.y, result.center.y, .001f);
            Assert.GreaterOrEqual(result.xMin, viewport.xMin + 24f - .001f);
            Assert.LessOrEqual(result.xMax, viewport.xMax - 24f + .001f);
            Assert.GreaterOrEqual(result.yMin, viewport.yMin + 26f - .001f);
            Assert.LessOrEqual(result.yMax, viewport.yMax - 26f + .001f);
        }

        [Test]
        public void SelectionUsesMapOriginAndViewportPosition()
        {
            var map = Box(-30, -20, 30, 20);
            var target = Box(-23, 8, -20, 9);
            var viewport = new Rect(85, 120, 450, 260);
            var fit = EditorCanvasMath.Fit(viewport, map, target);
            var result = TargetRect(fit, map, target);

            Assert.AreEqual(100f, fit.Zoom, .001f);
            Assert.AreEqual(viewport.center.x, result.center.x, .001f);
            Assert.AreEqual(viewport.center.y, result.center.y, .001f);
        }

        [Test]
        public void ThinMapFitsUsingInclusiveCellCount()
        {
            var map = Box(4, -3, 4, 16);
            var fit = EditorCanvasMath.Fit(new Rect(0, 0, 320, 252), map, map);
            var result = TargetRect(fit, map, map);

            Assert.AreEqual(10f, fit.Zoom, .001f);
            Assert.AreEqual(200f, result.height, .001f);
            Assert.AreEqual(10f, result.width, .001f);
        }

        [Test]
        public void Fits64By64InDualView()
        {
            var map = Box(0, 0, 63, 63);
            var fit = EditorCanvasMath.Fit(new Rect(0, 0, 320, 260), map, map);
            var result = TargetRect(fit, map, map);

            Assert.AreEqual(3.25f, fit.Zoom, .001f);
            Assert.LessOrEqual(result.xMax, 320f);
            Assert.LessOrEqual(result.yMax, 260f);
        }

        [Test]
        public void SingleCellIsCenteredAtMaximumZoom()
        {
            var map = Box(-8, -5, 9, 12);
            var target = Box(-3, 7, -3, 7);
            var viewport = new Rect(0, 0, 380, 250);
            var fit = EditorCanvasMath.Fit(viewport, map, target);
            var result = TargetRect(fit, map, target);

            Assert.AreEqual(100f, fit.Zoom, .001f);
            Assert.AreEqual(100f, result.width, .001f);
            Assert.AreEqual(100f, result.height, .001f);
            Assert.AreEqual(viewport.center.x, result.center.x, .001f);
            Assert.AreEqual(viewport.center.y, result.center.y, .001f);
        }

        [Test]
        public void ResizingViewportRecomputesZoomAndKeepsTargetCentered()
        {
            var map = Box(-10, -10, 21, 21);
            var small = new Rect(0, 0, 360, 260);
            var large = new Rect(0, 0, 680, 500);
            var first = EditorCanvasMath.Fit(small, map, map);
            var second = EditorCanvasMath.Fit(large, map, map);

            Assert.Greater(second.Zoom, first.Zoom);
            Assert.AreEqual(small.center.x, TargetRect(first, map, map).center.x, .001f);
            Assert.AreEqual(small.center.y, TargetRect(first, map, map).center.y, .001f);
            Assert.AreEqual(large.center.x, TargetRect(second, map, map).center.x, .001f);
            Assert.AreEqual(large.center.y, TargetRect(second, map, map).center.y, .001f);
        }

        [Test]
        public void TinyViewportKeepsFiniteMinimumZoom()
        {
            var map = Box(0, 0, 63, 63);
            var fit = EditorCanvasMath.Fit(new Rect(0, 0, 20, 20), map, map);
            Assert.AreEqual(.1f, fit.Zoom, .001f);
            Assert.IsFalse(float.IsNaN(fit.Pan.x) || float.IsNaN(fit.Pan.y));
        }
    }
}
