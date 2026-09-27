using System;
using System.Collections.Generic;
using RulePyramid.Core;

namespace RulePyramid.Editor
{
    [Flags]
    public enum EditorCategory
    {
        None = 0,
        Terrain = 1,
        Object = 2,
        Text = 4,
        All = Terrain | Object | Text
    }

    public struct EditorSample
    {
        public EditorCategory Category { get; }
        public string Value { get; }
        public string EntityId { get; }
        public GridCell Cell { get; }

        public EditorSample(EditorCategory category, string value, string entityId, GridCell cell)
        {
            Category = category;
            Value = value;
            EntityId = entityId;
            Cell = cell;
        }
    }

    public static class EditorPicking
    {
        public static bool Allows(EditorCategory mask, EntityKind kind)
        {
            var category = kind == EntityKind.Text ? EditorCategory.Text : EditorCategory.Object;
            return (mask & category) != 0;
        }

        public static bool Eligible(EditorCategory mask, EntityKind kind, string id, ISet<string> locked)
        {
            return Allows(mask, kind) && (locked == null || string.IsNullOrEmpty(id) || !locked.Contains(id));
        }

        public static IReadOnlyList<EditorSample> Candidates(LevelDefinition level, GridCell cell,
            EditorCategory mask, ISet<string> locked)
        {
            var result = new List<EditorSample>();
            if (level == null) return result;

            if ((mask & EditorCategory.Terrain) != 0 && level.terrain != null)
            {
                foreach (var box in level.terrain)
                {
                    if (box == null || !Contains(box, cell)) continue;
                    result.Add(new EditorSample(EditorCategory.Terrain,
                        string.IsNullOrEmpty(box.appearance) ? "Stone" : box.appearance, null, cell));
                }
            }

            if (level.entities != null)
            {
                foreach (var entity in level.entities)
                {
                    if (entity == null || entity.cell != cell) continue;
                    var kind = EntityState.ParseKind(entity.kind);
                    if (!Eligible(mask, kind, entity.id, locked)) continue;
                    result.Add(new EditorSample(kind == EntityKind.Text ? EditorCategory.Text : EditorCategory.Object,
                        kind == EntityKind.Text ? entity.token : entity.subject, entity.id, cell));
                }
            }

            return result;
        }

        static bool Contains(GridCellBox box, GridCell cell)
        {
            return cell.x >= box.min.x && cell.x <= box.max.x
                && cell.y >= box.min.y && cell.y <= box.max.y
                && cell.z >= box.min.z && cell.z <= box.max.z;
        }
    }
}
