using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>把实体和逐格选择的地形作为一次原子操作移动或复制。</summary>
    public static class SelectionTransform
    {
        public static string[] Apply(LevelDefinition draft, IEnumerable<string> entityIds,
            IEnumerable<GridCell> terrainCells, GridCell offset, bool copy)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!MapResize.TryValidateBounds(draft.bounds,out var boundsError)) throw new ArgumentException(boundsError);
            var requestedIds = new HashSet<string>(entityIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var requestedTerrain = new HashSet<GridCell>(terrainCells ?? Array.Empty<GridCell>());
            if (requestedIds.Count == 0 && requestedTerrain.Count == 0)
                throw new ArgumentException("未选择实体或地形");

            // 所有变换和碰撞检查先在副本中完成；提交只发生在最后。
            var changed = LevelCloner.Clone(draft);
            var selected = new List<EntityDefinition>();
            foreach (var entity in changed.entities)
                if (entity != null && requestedIds.Remove(entity.id)) selected.Add(entity);
            if (requestedIds.Count != 0) throw new ArgumentException("选择中包含不存在的实体 ID");

            var terrain = new Dictionary<GridCell, string>();
            foreach (var box in changed.terrain)
            {
                if (box == null) continue;
                if (!AuthoringOperations.Contains(draft,box.min) || !AuthoringOperations.Contains(draft,box.max))
                    throw new ArgumentException("地形盒超出地图范围");
                if (!MapResize.TryValidateBounds(box, out var error))
                    throw new ArgumentException("地形盒无效：" + error);
                for (long x = box.min.x; x <= box.max.x; x++)
                for (long y = box.min.y; y <= box.max.y; y++)
                for (long z = box.min.z; z <= box.max.z; z++)
                {
                    var cell = new GridCell((int)x, (int)y, (int)z);
                    if (terrain.ContainsKey(cell)) throw new ArgumentException("已有重叠地形：" + cell);
                    terrain.Add(cell, box.appearance);
                }
            }
            var selectedAppearance = new Dictionary<GridCell, string>();
            foreach (var cell in requestedTerrain)
            {
                if (!terrain.TryGetValue(cell, out var appearance))
                    throw new ArgumentException("选择中包含没有地形的格：" + cell);
                selectedAppearance.Add(cell, appearance);
            }

            var destinations = new Dictionary<GridCell, string>();
            foreach (var pair in selectedAppearance)
            {
                var target = Translate(draft, pair.Key, offset);
                destinations[target] = pair.Value;
            }
            foreach (var entity in selected)
                Translate(draft, entity.cell, offset);

            if (!copy)
                foreach (var cell in requestedTerrain) terrain.Remove(cell);
            foreach (var pair in destinations)
            {
                if (terrain.ContainsKey(pair.Key))
                    throw new ArgumentException("目标格已有地形：" + pair.Key);
                terrain.Add(pair.Key, pair.Value);
            }

            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in selected) selectedIds.Add(entity.id);
            var finalEntities = new List<EntityDefinition>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in changed.entities)
            {
                if (entity == null) continue;
                usedIds.Add(entity.id);
                if (!copy && selectedIds.Contains(entity.id)) entity.cell = Translate(draft, entity.cell, offset);
                finalEntities.Add(entity);
            }
            var resultIds = new List<string>();
            if (copy)
            {
                foreach (var entity in selected)
                {
                    var id = NextId(usedIds, entity.id);
                    finalEntities.Add(new EntityDefinition
                    {
                        id = id, kind = entity.kind, subject = entity.subject, token = entity.token,
                        cell = Translate(draft, entity.cell, offset), anchored = entity.anchored, color = entity.color
                    });
                    resultIds.Add(id);
                }
            }
            else
                foreach (var entity in selected) resultIds.Add(entity.id);

            var entityCells = new HashSet<GridCell>();
            foreach (var entity in finalEntities)
            {
                if (terrain.ContainsKey(entity.cell))
                    throw new ArgumentException("地形与实体重叠：" + entity.cell);
                if (!entityCells.Add(entity.cell))
                    throw new ArgumentException("多个实体重叠：" + entity.cell);
            }

            changed.entities = finalEntities.ToArray();
            if (requestedTerrain.Count != 0) changed.terrain = PackTerrain(changed.bounds, terrain);
            draft.entities = changed.entities;
            draft.terrain = changed.terrain;
            return resultIds.ToArray();
        }

        static GridCell Translate(LevelDefinition draft, GridCell cell, GridCell offset)
        {
            long x = (long)cell.x + offset.x;
            long y = (long)cell.y + offset.y;
            long z = (long)cell.z + offset.z;
            var bounds = draft.bounds;
            if (bounds == null || x < bounds.min.x || x > bounds.max.x || y < bounds.min.y || y > bounds.max.y
                || z < bounds.min.z || z > bounds.max.z)
                throw new ArgumentOutOfRangeException(nameof(offset), "目标格超出地图边界");
            return new GridCell((int)x, (int)y, (int)z);
        }

        static string NextId(HashSet<string> used, string source)
        {
            string prefix = string.IsNullOrEmpty(source) ? "entity_copy_" : source + "_copy_";
            int next = 1;
            while (!used.Add(prefix + next)) next++;
            return prefix + next;
        }

        static GridCellBox[] PackTerrain(GridCellBox bounds, Dictionary<GridCell, string> terrain)
        {
            var remaining = new HashSet<GridCell>(terrain.Keys);
            var packed = new List<GridCellBox>();
            int nextId = 1;
            for (long y = bounds.min.y; y <= bounds.max.y; y++)
            for (long z = bounds.min.z; z <= bounds.max.z; z++)
            for (long x = bounds.min.x; x <= bounds.max.x; x++)
            {
                var first = new GridCell((int)x, (int)y, (int)z);
                if (!remaining.Contains(first)) continue;
                string appearance = terrain[first];
                long maxX = x;
                while (maxX < bounds.max.x && Matches(maxX + 1, y, z, appearance)) maxX++;
                long maxZ = z;
                while (maxZ < bounds.max.z && MatchesRow(x, maxX, y, maxZ + 1, appearance)) maxZ++;
                long maxY = y;
                while (maxY < bounds.max.y && MatchesPlane(x, maxX, maxY + 1, z, maxZ, appearance)) maxY++;
                for (long yy = y; yy <= maxY; yy++)
                for (long zz = z; zz <= maxZ; zz++)
                for (long xx = x; xx <= maxX; xx++) remaining.Remove(new GridCell((int)xx, (int)yy, (int)zz));
                packed.Add(new GridCellBox
                {
                    id = "terrain_" + nextId++, min = first,
                    max = new GridCell((int)maxX, (int)maxY, (int)maxZ), appearance = appearance
                });
            }
            return packed.ToArray();

            bool Matches(long xx, long yy, long zz, string appearance)
            {
                var cell = new GridCell((int)xx, (int)yy, (int)zz);
                return remaining.Contains(cell) && string.Equals(terrain[cell], appearance, StringComparison.Ordinal);
            }
            bool MatchesRow(long minX, long maxX, long yy, long zz, string appearance)
            {
                for (long xx = minX; xx <= maxX; xx++)
                    if (!Matches(xx, yy, zz, appearance)) return false;
                return true;
            }
            bool MatchesPlane(long minX, long maxX, long yy, long minZ, long maxZ, string appearance)
            {
                for (long zz = minZ; zz <= maxZ; zz++)
                    if (!MatchesRow(minX, maxX, yy, zz, appearance)) return false;
                return true;
            }
        }
    }
}
