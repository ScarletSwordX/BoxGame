using System;

namespace RulePyramid.Core
{
    /// <summary>调整关卡整数格边界。缩小时保留所有内容，越界则拒绝操作。</summary>
    public static class MapResize
    {
        public const int MaxAxisSize = 64;

        public static bool TryValidateBounds(GridCellBox bounds, out string error)
        {
            if (bounds == null)
            {
                error = "缺少地图边界";
                return false;
            }
            long width = (long)bounds.max.x - bounds.min.x + 1;
            long height = (long)bounds.max.y - bounds.min.y + 1;
            long depth = (long)bounds.max.z - bounds.min.z + 1;
            if (width < 1 || height < 1 || depth < 1)
            {
                error = "地图边界的最小格不能大于最大格";
                return false;
            }
            if (width > MaxAxisSize || height > MaxAxisSize || depth > MaxAxisSize)
            {
                error = "地图每轴最多 " + MaxAxisSize + " 格，当前为 " + width + "×" + height + "×" + depth;
                return false;
            }
            error = null;
            return true;
        }

        public static void Resize(LevelDefinition draft, GridCell origin, GridCell size)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (size.x < 1 || size.x > MaxAxisSize || size.y < 1 || size.y > MaxAxisSize || size.z < 1 || size.z > MaxAxisSize)
                throw new ArgumentOutOfRangeException(nameof(size), "地图每轴必须为 1 至 " + MaxAxisSize + " 格");

            long maxX = (long)origin.x + size.x - 1;
            long maxY = (long)origin.y + size.y - 1;
            long maxZ = (long)origin.z + size.z - 1;
            if (maxX > int.MaxValue || maxY > int.MaxValue || maxZ > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(origin), "地图最大格超出整数坐标范围");
            var max = new GridCell((int)maxX, (int)maxY, (int)maxZ);

            foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
            {
                if (entity == null) continue;
                if (!Contains(origin, max, entity.cell))
                    throw new ArgumentException("实体 " + entity.id + " 位于新地图外：" + entity.cell);
            }
            foreach (var box in draft.terrain ?? Array.Empty<GridCellBox>())
            {
                if (box == null) continue;
                if (!ValidBox(box) || !Contains(origin, max, box.min) || !Contains(origin, max, box.max))
                    throw new ArgumentException("地形 " + box.id + " 超出新地图或边界无效：" + box.min + " 至 " + box.max);
            }
            foreach (var region in draft.tutorial?.regions ?? Array.Empty<RegionTutorialData>())
            {
                if (region == null) continue;
                var box = region.bounds;
                if (box == null || !ValidBox(box) || !Contains(origin, max, box.min) || !Contains(origin, max, box.max))
                    throw new ArgumentException("教学区域 " + region.id + " 超出新地图或边界无效");
            }
            foreach (var stage in draft.stagePlan?.stages ?? Array.Empty<StageDefinition>())
            {
                if (stage == null) continue;
                foreach (var entity in stage.entities ?? Array.Empty<EntityDefinition>())
                    if (entity != null && !Contains(origin, max, entity.cell))
                        throw new ArgumentException("阶段 " + stage.id + " 的实体 " + entity.id + " 位于新地图外：" + entity.cell);
                foreach (var region in stage.tutorial?.regions ?? Array.Empty<RegionTutorialData>())
                {
                    var box = region?.bounds;
                    if (box == null || !ValidBox(box) || !Contains(origin, max, box.min) || !Contains(origin, max, box.max))
                        throw new ArgumentException("阶段 " + stage.id + " 的教学区域超出新地图或边界无效");
                }
            }
            foreach (var region in draft.stagePlan?.regions ?? Array.Empty<StageRegion>())
            {
                var box = region?.bounds;
                if (box == null || !ValidBox(box) || !Contains(origin, max, box.min) || !Contains(origin, max, box.max))
                    throw new ArgumentException("阶段归属区域超出新地图或边界无效");
            }

            if (draft.bounds == null) draft.bounds = new GridCellBox();
            draft.bounds.min = origin;
            draft.bounds.max = max;
        }

        static bool Contains(GridCell min, GridCell max, GridCell cell)
        {
            return cell.x >= min.x && cell.x <= max.x && cell.y >= min.y && cell.y <= max.y && cell.z >= min.z && cell.z <= max.z;
        }

        static bool ValidBox(GridCellBox box)
        {
            return box.min.x <= box.max.x && box.min.y <= box.max.y && box.min.z <= box.max.z;
        }
    }
}
