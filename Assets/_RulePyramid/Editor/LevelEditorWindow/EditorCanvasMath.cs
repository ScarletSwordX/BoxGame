using System;
using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Editor
{
    public struct EditorCanvasFit
    {
        public float Zoom;
        public Vector2 Pan;
    }

    public static class EditorCanvasMath
    {
        const float LeftMargin = 24f;
        const float TopMargin = 26f;
        const float MinimumZoom = .1f;
        const float MaximumZoom = 100f;

        public static EditorCanvasFit Fit(Rect viewport, GridCellBox map, GridCellBox target)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (map.min.x > map.max.x || map.min.z > map.max.z)
                throw new ArgumentException("Map bounds are inverted.", nameof(map));
            if (target.min.x > target.max.x || target.min.z > target.max.z)
                throw new ArgumentException("Target bounds are inverted.", nameof(target));

            double columns = (double)target.max.x - target.min.x + 1;
            double rows = (double)target.max.z - target.min.z + 1;
            float usableWidth = Mathf.Max(0f, viewport.width - 2f * LeftMargin);
            float usableHeight = Mathf.Max(0f, viewport.height - 2f * TopMargin);
            float zoom = Mathf.Clamp((float)Math.Min(usableWidth / columns, usableHeight / rows), MinimumZoom, MaximumZoom);

            double centerX = ((double)target.min.x + target.max.x) / 2d - map.min.x + .5d;
            double centerY = map.max.z - ((double)target.min.z + target.max.z) / 2d + .5d;
            var pan = new Vector2(
                viewport.center.x - LeftMargin - (float)(centerX * zoom),
                viewport.center.y - TopMargin - (float)(centerY * zoom));
            return new EditorCanvasFit { Zoom = zoom, Pan = pan };
        }
    }
}
