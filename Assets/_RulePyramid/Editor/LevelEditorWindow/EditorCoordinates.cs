using RulePyramid.Core;
using UnityEngine;

namespace RulePyramid.Editor
{
    // 只转换作者界面；关卡、规则解析和继承中的坐标保持原样。
    public static class EditorCoordinates
    {
        public static GridCell Display(GridCellBox bounds, GridCell stored) =>
            new GridCell(checked(bounds.max.x - stored.x), stored.y, checked(bounds.max.z - stored.z));

        public static GridCell Stored(GridCellBox bounds, GridCell display) => Display(bounds, display);

        public static GridCell Delta(GridCell delta) => new GridCell(checked(-delta.x), delta.y, checked(-delta.z));

        public static Vector3 Direction(Vector3 direction) => new Vector3(-direction.x, direction.y, -direction.z);

        public static GridCell ResizeMinimum(GridCell upperRight, GridCell size) =>
            new GridCell(checked(upperRight.x - size.x + 1), upperRight.y, checked(upperRight.z - size.z + 1));
    }

    public partial class LevelEditorWindow
    {
        GridCellBox CoordinateBounds => Playing ? _session.Playtest.Level.bounds : _session.Draft.bounds;
        GridCell DisplayCell(GridCell cell) => EditorCoordinates.Display(CoordinateBounds, cell);
        GridCell StoredCell(GridCell cell) => EditorCoordinates.Stored(CoordinateBounds, cell);

        void MoveSelectionToDisplay(GridCell target)
        {
            var selected = System.Linq.Enumerable.FirstOrDefault(VisibleEntities, e => _selected.Contains(e.Id));
            if (selected == null) return;
            var stored = StoredCell(target);
            MoveSelection(new GridCell(stored.x - selected.Cell.x, stored.y - selected.Cell.y, stored.z - selected.Cell.z), false);
        }
    }
}
