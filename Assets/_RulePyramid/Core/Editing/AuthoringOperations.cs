using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    /// <summary>纯草稿变换；由 LevelEditSession.Edit 包成一次撤销操作。</summary>
    public static class AuthoringOperations
    {
        /// <summary>补齐地图最低 Y 层的石质地板，保留已有地形及其外观。</summary>
        public static void FillFloor(LevelDefinition draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!MapResize.TryValidateBounds(draft.bounds, out var error)) throw new ArgumentException(error);
            var bounds = draft.bounds;
            int y = bounds.min.y;
            foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
                if (entity != null && entity.cell.y == y && Contains(draft, entity.cell))
                    throw new ArgumentException("底层地板与实体 " + entity.id + " 冲突，请先移动该实体。");
            var occupied = LevelCloner.ExpandTerrain(draft.terrain);
            var boxes = new List<GridCellBox>(draft.terrain ?? Array.Empty<GridCellBox>());
            var used = TerrainIds(boxes);
            var previous = new Dictionary<(int, int), GridCellBox>();
            for (long z = bounds.min.z; z <= bounds.max.z; z++)
            {
                var row = new Dictionary<(int, int), GridCellBox>();
                long x = bounds.min.x;
                while (x <= bounds.max.x)
                {
                    if (occupied.Contains(new GridCell((int)x, y, (int)z))) { x++; continue; }
                    int start = (int)x;
                    while (x < bounds.max.x && !occupied.Contains(new GridCell((int)(x + 1), y, (int)z))) x++;
                    var key = (start, (int)x);
                    if (previous.TryGetValue(key, out var box)) box.max.z = (int)z;
                    else
                    {
                        box = new GridCellBox { id = Unique(used, "floor_"), appearance = "Stone",
                            min = new GridCell(start, y, (int)z), max = new GridCell((int)x, y, (int)z) };
                        boxes.Add(box);
                    }
                    row.Add(key, box);
                    x++;
                }
                previous = row;
            }
            draft.terrain = boxes.ToArray();
        }

        public static void AddTerrain(LevelDefinition draft, GridCell min, GridCell max, string appearance = null)
        {
            CheckBox(draft, min, max);
            var boxes = new List<GridCellBox>(draft.terrain ?? Array.Empty<GridCellBox>());
            foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
                if (entity != null && Inside(entity.cell, min, max))
                    throw new ArgumentException("Terrain would overlap entity " + entity.id);
            foreach (var box in boxes)
                if (box != null && Overlaps(box, min, max))
                    throw new ArgumentException("目标格已有地形：" + box.id);
            var used = TerrainIds(boxes);
            boxes.Add(new GridCellBox { id = Unique(used, "terrain_"), min = min, max = max, appearance = appearance });
            draft.terrain = boxes.ToArray();
        }

        /// <summary>将一次物体笔刷轨迹作为原子操作放置；轨迹中的重复格只放一个物体。</summary>
        public static string[] PlaceObjects(LevelDefinition draft, IEnumerable<GridCell> cells, string subject)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (!Tokens.IsSubject(subject)) throw new ArgumentException("无效的物体类型：" + subject, nameof(subject));
            var targets = new List<GridCell>();
            var seen = new HashSet<GridCell>();
            foreach (var cell in cells)
                if (seen.Add(cell)) targets.Add(cell);
            var terrain = LevelCloner.ExpandTerrain(draft.terrain);
            var occupied = new HashSet<GridCell>();
            foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
                if (entity != null) occupied.Add(entity.cell);
            foreach (var cell in targets)
            {
                CheckCell(draft, cell);
                if (terrain.Contains(cell) || occupied.Contains(cell))
                    throw new ArgumentException("目标格已有可见方块：" + cell);
            }
            var entities = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            var used = EntityIds(entities);
            var result = new List<string>();
            foreach (var cell in targets)
            {
                var id = Unique(used, "object_");
                entities.Add(new EntityDefinition { id = id, kind = "Object", subject = subject, cell = cell });
                result.Add(id);
            }
            draft.entities = entities.ToArray();
            return result.ToArray();
        }

        public static void RemoveTerrain(LevelDefinition draft, GridCell min, GridCell max)
        {
            CheckBox(draft, min, max);
            var result = new List<GridCellBox>();
            var original = draft.terrain ?? Array.Empty<GridCellBox>();
            var used = TerrainIds(original);
            foreach (var box in original)
            {
                if (box == null) continue;
                if (!Overlaps(box, min, max)) { result.Add(box); continue; }
                var cutMin = new GridCell(Math.Max(box.min.x, min.x), Math.Max(box.min.y, min.y), Math.Max(box.min.z, min.z));
                var cutMax = new GridCell(Math.Min(box.max.x, max.x), Math.Min(box.max.y, max.y), Math.Min(box.max.z, max.z));
                bool first = true;
                void AddPiece(GridCell pieceMin, GridCell pieceMax)
                {
                    if (pieceMin.x > pieceMax.x || pieceMin.y > pieceMax.y || pieceMin.z > pieceMax.z) return;
                    result.Add(new GridCellBox
                    {
                        id = first && !string.IsNullOrEmpty(box.id) ? box.id : Unique(used, string.IsNullOrEmpty(box.id) ? "terrain_" : box.id + "_"),
                        min = pieceMin,
                        max = pieceMax,
                        appearance = box.appearance
                    });
                    first = false;
                }
                AddPiece(box.min, new GridCell(cutMin.x - 1, box.max.y, box.max.z));
                AddPiece(new GridCell(cutMax.x + 1, box.min.y, box.min.z), box.max);
                AddPiece(new GridCell(cutMin.x, box.min.y, box.min.z), new GridCell(cutMax.x, cutMin.y - 1, box.max.z));
                AddPiece(new GridCell(cutMin.x, cutMax.y + 1, box.min.z), new GridCell(cutMax.x, box.max.y, box.max.z));
                AddPiece(new GridCell(cutMin.x, cutMin.y, box.min.z), new GridCell(cutMax.x, cutMax.y, cutMin.z - 1));
                AddPiece(new GridCell(cutMin.x, cutMin.y, cutMax.z + 1), new GridCell(cutMax.x, cutMax.y, box.max.z));
            }
            draft.terrain = result.ToArray();
        }

        public static void MoveEntities(LevelDefinition draft, IEnumerable<string> ids, GridCell offset)
        {
            var selected = FindEntities(draft, ids);
            foreach (var entity in selected) CheckCell(draft, entity.cell.Add(offset));
            CheckEntityDestinations(draft, selected, offset, true);
            foreach (var entity in selected) entity.cell = entity.cell.Add(offset);
        }

        public static string[] CopyEntities(LevelDefinition draft, IEnumerable<string> ids, GridCell offset)
        {
            var selected = FindEntities(draft, ids);
            foreach (var entity in selected) CheckCell(draft, entity.cell.Add(offset));
            CheckEntityDestinations(draft, selected, offset, false);
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            var used = EntityIds(list);
            var result = new List<string>();
            foreach (var entity in selected)
            {
                string id = Unique(used, string.IsNullOrEmpty(entity.id) ? "entity_" : entity.id + "_copy_");
                list.Add(new EntityDefinition
                {
                    id = id, kind = entity.kind, subject = entity.subject, token = entity.token,
                    cell = entity.cell.Add(offset), anchored = entity.anchored, color = entity.color
                });
                result.Add(id);
            }
            draft.entities = list.ToArray();
            return result.ToArray();
        }

        public static string[] PlaceSentence(LevelDefinition draft, IEnumerable<string> tokens, GridCell start, bool alongX)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));
            var words = new List<string>(tokens);
            if (words.Count == 0) throw new ArgumentException("Sentence is empty", nameof(tokens));
            var cells = new List<GridCell>();
            var terrain = LevelCloner.ExpandTerrain(draft.terrain);
            for (int i = 0; i < words.Count; i++)
            {
                if (!Tokens.IsLegalWord(words[i])) throw new ArgumentException("Illegal token: " + words[i], nameof(tokens));
                var cell = start.Add(alongX ? i : 0, 0, alongX ? 0 : -i);
                CheckCell(draft, cell);
                if (terrain.Contains(cell))
                    throw new ArgumentException("Word overlaps terrain at " + cell);
                foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
                    if (entity != null && entity.cell == cell)
                        throw new ArgumentException("Word overlaps entity at " + cell);
                cells.Add(cell);
            }
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            var used = EntityIds(list);
            var result = new List<string>();
            for (int i = 0; i < words.Count; i++)
            {
                string id = Unique(used, "text_");
                list.Add(new EntityDefinition { id = id, kind = "Text", token = words[i], subject = "", cell = cells[i] });
                result.Add(id);
            }
            draft.entities = list.ToArray();
            return result.ToArray();
        }

        public static bool Contains(LevelDefinition draft, GridCell cell)
        {
            var bounds = draft?.bounds;
            return bounds != null && Inside(cell, bounds.min, bounds.max);
        }

        static void CheckCell(LevelDefinition draft, GridCell cell)
        {
            if (!Contains(draft, cell)) throw new ArgumentOutOfRangeException(nameof(cell), "Outside level bounds: " + cell);
        }

        static void CheckBox(LevelDefinition draft, GridCell min, GridCell max)
        {
            if (min.x > max.x || min.y > max.y || min.z > max.z) throw new ArgumentException("Box min exceeds max");
            CheckCell(draft, min);
            CheckCell(draft, max);
        }

        static bool Inside(GridCell c, GridCell min, GridCell max)
        {
            return c.x >= min.x && c.x <= max.x && c.y >= min.y && c.y <= max.y && c.z >= min.z && c.z <= max.z;
        }

        static bool Overlaps(GridCellBox b, GridCell min, GridCell max)
        {
            return b.min.x <= max.x && b.max.x >= min.x && b.min.y <= max.y && b.max.y >= min.y && b.min.z <= max.z && b.max.z >= min.z;
        }

        static IEnumerable<GridCell> Cells(GridCell min, GridCell max)
        {
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
                yield return new GridCell(x, y, z);
        }

        static HashSet<string> TerrainIds(IEnumerable<GridCellBox> boxes)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var box in boxes) if (box != null && !string.IsNullOrEmpty(box.id)) used.Add(box.id);
            return used;
        }

        static HashSet<string> EntityIds(IEnumerable<EntityDefinition> entities)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in entities) if (entity != null && !string.IsNullOrEmpty(entity.id)) used.Add(entity.id);
            return used;
        }

        static string Unique(HashSet<string> used, string prefix)
        {
            int n = 1;
            while (!used.Add(prefix + n)) n++;
            return prefix + n;
        }

        static List<EntityDefinition> FindEntities(LevelDefinition draft, IEnumerable<string> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
            if (wanted.Count == 0) throw new ArgumentException("No entity ids", nameof(ids));
            var selected = new List<EntityDefinition>();
            foreach (var entity in draft.entities ?? Array.Empty<EntityDefinition>())
                if (entity != null && wanted.Remove(entity.id)) selected.Add(entity);
            if (wanted.Count != 0) throw new ArgumentException("Unknown entity id");
            return selected;
        }

        static void CheckEntityDestinations(LevelDefinition draft, List<EntityDefinition> selected, GridCell offset, bool moving)
        {
            var terrain = LevelCloner.ExpandTerrain(draft.terrain);
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in selected) selectedIds.Add(entity.id);
            var destinations = new List<EntityDefinition>();
            foreach (var entity in selected)
            {
                var target = entity.cell.Add(offset);
                if (terrain.Contains(target)) throw new ArgumentException("Entity overlaps terrain at " + target);
                destinations.Add(new EntityDefinition { id = entity.id, kind = entity.kind, cell = target });
            }
            foreach (var target in destinations)
            {
                foreach (var existing in draft.entities ?? Array.Empty<EntityDefinition>())
                {
                    if (existing == null || (moving && selectedIds.Contains(existing.id)) || existing.cell != target.cell) continue;
                    throw new ArgumentException("目标格已有实体：" + target.cell);
                }
            }
            for (int i = 0; i < destinations.Count; i++)
            for (int j = i + 1; j < destinations.Count; j++)
                if (destinations[i].cell == destinations[j].cell)
                    throw new ArgumentException("多个实体目标格重合：" + destinations[i].cell);
        }
    }
}
