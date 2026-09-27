using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>Editor-only stage authoring. Regions mark the first stage at which a cell opens.</summary>
    public static class StageAuthoring
    {
        public static void EnsurePlan(LevelDefinition level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.stagePlan == null) level.stagePlan = new StagePlanData();
            if (level.stagePlan.stages == null || level.stagePlan.stages.Length == 0)
                level.stagePlan.stages = new[] { new StageDefinition { id = "stage_1", name = "阶段 1", lesson = "",
                    entities = LevelCloner.CloneEntities(level.entities), tutorial = LevelCloner.CloneTutorial(level.tutorial),
                    referenceSolutions = LevelCloner.CloneSolutions(level.referenceSolutions) } };
            if (level.stagePlan.regions == null) level.stagePlan.regions = Array.Empty<StageRegion>();
        }

        public static string AddStage(LevelDefinition level, string name)
        {
            EnsurePlan(level);
            var stages = new List<StageDefinition>(level.stagePlan.stages);
            string id = "stage_" + Guid.NewGuid().ToString("N");
            stages.Add(new StageDefinition { id = id, name = string.IsNullOrWhiteSpace(name) ? "阶段 " + (stages.Count + 1) : name,
                lesson = "", entities = LevelCloner.CloneEntities(level.entities),
                tutorial = LevelCloner.CloneTutorial(level.tutorial), referenceSolutions = Array.Empty<ReferenceSolutionData>() });
            level.stagePlan.stages = stages.ToArray();
            return id;
        }

        public static void RemoveStage(LevelDefinition level, string id, string reassignToId)
        {
            EnsurePlan(level);
            var stages = new List<StageDefinition>(level.stagePlan.stages);
            if (stages.Count <= 1) throw new InvalidOperationException("至少保留一个阶段");
            if (!stages.Exists(s => s.id == id)) throw new ArgumentException("未知阶段：" + id);
            if (id == reassignToId || !stages.Exists(s => s.id == reassignToId)) throw new ArgumentException("请选择其它现有阶段接收区域");
            var transferredImplicit = new List<StageRegion>();
            if (stages[0].id == id)
            {
                if (!MapResize.TryValidateBounds(level.bounds, out var error))
                    throw new ArgumentException("地图边界无效：" + error);
                var unpainted = new List<GridCellBox> { CopyBox(level.bounds) };
                foreach (var region in level.stagePlan.regions)
                {
                    if (region?.bounds == null || !MapResize.TryValidateBounds(region.bounds, out _)
                        || !InBox(region.bounds.min, level.bounds) || !InBox(region.bounds.max, level.bounds))
                        throw new ArgumentException("阶段区域无效或超出地图");
                    var remaining = new List<GridCellBox>();
                    foreach (var box in unpainted) remaining.AddRange(Subtract(box, region.bounds));
                    unpainted = remaining;
                }
                foreach (var box in unpainted)
                    transferredImplicit.Add(new StageRegion { id = "region_" + Guid.NewGuid().ToString("N"),
                        stageId = reassignToId, bounds = box });
            }
            stages.RemoveAll(s => s.id == id);
            level.stagePlan.stages = stages.ToArray();
            foreach (var region in level.stagePlan.regions)
                if (region != null && region.stageId == id) region.stageId = reassignToId;
            if (transferredImplicit.Count > 0)
            {
                var regions = new List<StageRegion>(level.stagePlan.regions);
                regions.AddRange(transferredImplicit);
                level.stagePlan.regions = regions.ToArray();
            }
        }

        public static void MoveStage(LevelDefinition level, string id, int newIndex)
        {
            EnsurePlan(level);
            var stages = new List<StageDefinition>(level.stagePlan.stages);
            int old = stages.FindIndex(s => s != null && s.id == id);
            if (old < 0 || newIndex < 0 || newIndex >= stages.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            var item = stages[old];
            stages.RemoveAt(old);
            stages.Insert(newIndex, item);
            level.stagePlan.stages = stages.ToArray();
        }

        public static string StageAt(LevelDefinition level, GridCell cell)
        {
            EnsurePlan(level);
            var regions = level.stagePlan.regions;
            for (int i = regions.Length - 1; i >= 0; i--)
            {
                var r = regions[i];
                if (r != null && InBox(cell, r.bounds)) return r.stageId;
            }
            return level.stagePlan.stages[0].id;
        }

        public static void PaintBox(LevelDefinition level, GridCellBox box, string stageId)
        {
            EnsurePlan(level);
            if (!MapResize.TryValidateBounds(box, out var error) || !InBox(box.min, level.bounds) || !InBox(box.max, level.bounds))
                throw new ArgumentException("阶段区域超出地图或边界无效：" + error);
            if (stageId != null && Array.Find(level.stagePlan.stages, s => s != null && s.id == stageId) == null)
                throw new ArgumentException("未知阶段：" + stageId);
            // Cut the painted cuboid out of existing regions; stored regions never overlap.
            var result = new List<StageRegion>();
            foreach (var region in level.stagePlan.regions)
            {
                if (region == null || region.bounds == null) continue;
                if (!Overlaps(region.bounds, box))
                { result.Add(new StageRegion { id = region.id, stageId = region.stageId, bounds = CopyBox(region.bounds) }); continue; }
                foreach (var remainder in Subtract(region.bounds, box))
                    result.Add(new StageRegion { id = "region_" + Guid.NewGuid().ToString("N"), stageId = region.stageId, bounds = remainder });
            }
            if (stageId != null && stageId != level.stagePlan.stages[0].id)
                result.Add(new StageRegion { id = "region_" + Guid.NewGuid().ToString("N"), stageId = stageId, bounds = CopyBox(box) });
            level.stagePlan.regions = result.ToArray();
        }

        public static LevelDefinition Project(LevelDefinition level, string id)
        {
            EnsurePlan(level);
            if (!MapResize.TryValidateBounds(level.bounds, out var boundsError))
                throw new ArgumentException("地图边界无效：" + boundsError);
            foreach (var box in level.terrain ?? Array.Empty<GridCellBox>())
                if (box == null || !MapResize.TryValidateBounds(box, out _)
                    || !InBox(box.min, level.bounds) || !InBox(box.max, level.bounds))
                    throw new ArgumentException("地形盒无效或超出地图：" + box?.id);
            foreach (var region in level.stagePlan.regions ?? Array.Empty<StageRegion>())
                if (region?.bounds == null || !MapResize.TryValidateBounds(region.bounds, out _)
                    || !InBox(region.bounds.min, level.bounds) || !InBox(region.bounds.max, level.bounds))
                    throw new ArgumentException("阶段区域无效或超出地图");
            int index = Array.FindIndex(level.stagePlan.stages, s => s != null && s.id == id);
            if (index < 0) throw new ArgumentException("未知阶段：" + id);
            var stage = level.stagePlan.stages[index];
            var copy = LevelCloner.Clone(level);
            copy.stagePlan = null;
            copy.authoringSourceJson = null;
            copy.entities = LevelCloner.CloneEntities(stage.entities);
            copy.tutorial = LevelCloner.CloneTutorial(stage.tutorial);
            copy.referenceSolutions = LevelCloner.CloneSolutions(stage.referenceSolutions);
            string boundaryMode = level.stagePlan.boundaryMode;
            if (!string.IsNullOrEmpty(boundaryMode) && boundaryMode != "Stone" && boundaryMode != "AirWall")
                throw new ArgumentException("未知阶段边界模式：" + boundaryMode);
            bool airWall = boundaryMode == "AirWall";
            if (!airWall && (index == level.stagePlan.stages.Length - 1 || level.stagePlan.regions.Length == 0))
                return copy;
            // Closed cells are visible stone barriers in standalone editor playtest.
            var closed = new HashSet<GridCell>();
            foreach (var region in level.stagePlan.regions)
            {
                if (region?.bounds == null) continue;
                int first = Array.FindIndex(level.stagePlan.stages, s => s != null && s.id == region.stageId);
                if (first < 0) throw new ArgumentException("阶段区域引用未知阶段：" + region.stageId);
                if (first <= index) continue;
                var b = region.bounds;
                for (long y = b.min.y; y <= b.max.y; y++)
                for (long z = b.min.z; z <= b.max.z; z++)
                for (long x = b.min.x; x <= b.max.x; x++)
                    closed.Add(new GridCell((int)x, (int)y, (int)z));
            }
            if (airWall)
            {
                long openCount = 0;
                int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
                var bounds = level.bounds;
                for (long y = bounds.min.y; y <= bounds.max.y; y++)
                for (long z = bounds.min.z; z <= bounds.max.z; z++)
                for (long x = bounds.min.x; x <= bounds.max.x; x++)
                {
                    if (closed.Contains(new GridCell((int)x, (int)y, (int)z))) continue;
                    openCount++;
                    minX = Math.Min(minX, (int)x); maxX = Math.Max(maxX, (int)x);
                    minY = Math.Min(minY, (int)y); maxY = Math.Max(maxY, (int)y);
                    minZ = Math.Min(minZ, (int)z); maxZ = Math.Max(maxZ, (int)z);
                }
                if (openCount == 0)
                    throw new InvalidOperationException("空气墙阶段没有开放区域：" + id);
                long volume = ((long)maxX - minX + 1) * ((long)maxY - minY + 1) * ((long)maxZ - minZ + 1);
                if (openCount != volume)
                    throw new InvalidOperationException("空气墙阶段的累计开放区域必须是完整长方体：" + id);
                copy.bounds = new GridCellBox { id = bounds.id,
                    min = new GridCell(minX, minY, minZ), max = new GridCell(maxX, maxY, maxZ) };
                var clipped = new List<GridCellBox>();
                foreach (var box in copy.terrain ?? Array.Empty<GridCellBox>())
                {
                    if (!Overlaps(box, copy.bounds)) continue;
                    clipped.Add(Slice(box,
                        Math.Max(box.min.x, minX), Math.Min(box.max.x, maxX),
                        Math.Max(box.min.y, minY), Math.Min(box.max.y, maxY),
                        Math.Max(box.min.z, minZ), Math.Min(box.max.z, maxZ)));
                }
                copy.terrain = clipped.ToArray();
                return copy;
            }
            if (closed.Count == 0) return copy;
            var openTerrain = new Dictionary<string, HashSet<GridCell>>();
            foreach (var box in copy.terrain ?? Array.Empty<GridCellBox>())
            {
                if (box == null) continue;
                var appearance = box.appearance ?? "Stone";
                if (!openTerrain.TryGetValue(appearance, out var cells))
                    openTerrain.Add(appearance, cells = new HashSet<GridCell>());
                for (long y = box.min.y; y <= box.max.y; y++)
                for (long z = box.min.z; z <= box.max.z; z++)
                for (long x = box.min.x; x <= box.max.x; x++)
                {
                    var cell = new GridCell((int)x, (int)y, (int)z);
                    if (!closed.Contains(cell)) cells.Add(cell);
                }
            }
            var projected = new List<GridCellBox>();
            foreach (var pair in openTerrain) projected.AddRange(Runs(pair.Value, pair.Key, "stage_open_" + projected.Count + "_"));
            projected.AddRange(Runs(closed, "Stone", "stage_closed_"));
            copy.terrain = projected.ToArray();
            return copy;
        }

        public static bool IsOpen(LevelDefinition level, string stageId, GridCell cell)
        {
            EnsurePlan(level);
            int current = Array.FindIndex(level.stagePlan.stages, s => s != null && s.id == stageId);
            string owner = StageAt(level, cell);
            int first = Array.FindIndex(level.stagePlan.stages, s => s != null && s.id == owner);
            return current >= 0 && first >= 0 && first <= current;
        }

        static GridCellBox[] Runs(HashSet<GridCell> cells, string appearance, string prefix)
        {
            var remaining = new HashSet<GridCell>(cells);
            var boxes = new List<GridCellBox>();
            foreach (var cell in cells)
            {
                if (!remaining.Remove(cell)) continue;
                int end = cell.x;
                while (end < int.MaxValue && remaining.Remove(new GridCell(end + 1, cell.y, cell.z))) end++;
                boxes.Add(new GridCellBox { id = prefix + boxes.Count, appearance = appearance,
                    min = cell, max = new GridCell(end, cell.y, cell.z) });
            }
            return boxes.ToArray();
        }

        static IEnumerable<GridCellBox> Subtract(GridCellBox source, GridCellBox cut)
        {
            int ax = Math.Max(source.min.x, cut.min.x), bx = Math.Min(source.max.x, cut.max.x);
            int ay = Math.Max(source.min.y, cut.min.y), by = Math.Min(source.max.y, cut.max.y);
            int az = Math.Max(source.min.z, cut.min.z), bz = Math.Min(source.max.z, cut.max.z);
            if (ax > bx || ay > by || az > bz) { yield return CopyBox(source); yield break; }
            if (source.min.x < ax) yield return Slice(source, source.min.x, ax - 1, source.min.y, source.max.y, source.min.z, source.max.z);
            if (bx < source.max.x) yield return Slice(source, bx + 1, source.max.x, source.min.y, source.max.y, source.min.z, source.max.z);
            if (source.min.y < ay) yield return Slice(source, ax, bx, source.min.y, ay - 1, source.min.z, source.max.z);
            if (by < source.max.y) yield return Slice(source, ax, bx, by + 1, source.max.y, source.min.z, source.max.z);
            if (source.min.z < az) yield return Slice(source, ax, bx, ay, by, source.min.z, az - 1);
            if (bz < source.max.z) yield return Slice(source, ax, bx, ay, by, bz + 1, source.max.z);
        }

        static GridCellBox Slice(GridCellBox source, int x1, int x2, int y1, int y2, int z1, int z2)
        { return new GridCellBox { id = source.id, appearance = source.appearance,
            min = new GridCell(x1, y1, z1), max = new GridCell(x2, y2, z2) }; }

        static GridCellBox CopyBox(GridCellBox b)
        { return new GridCellBox { id = b.id, appearance = b.appearance, min = b.min, max = b.max }; }

        static bool InBox(GridCell c, GridCellBox b)
        { return b != null && c.x >= b.min.x && c.x <= b.max.x && c.y >= b.min.y && c.y <= b.max.y && c.z >= b.min.z && c.z <= b.max.z; }

        static bool Overlaps(GridCellBox a, GridCellBox b)
        { return a.min.x <= b.max.x && b.min.x <= a.max.x && a.min.y <= b.max.y && b.min.y <= a.max.y
            && a.min.z <= b.max.z && b.min.z <= a.max.z; }
    }
}
