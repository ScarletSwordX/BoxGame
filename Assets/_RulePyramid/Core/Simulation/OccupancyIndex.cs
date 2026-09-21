using System.Collections.Generic;

namespace RulePyramid.Core
{
    public sealed class OccupancyIndex
    {
        readonly Dictionary<GridCell, List<string>> _cells = new Dictionary<GridCell, List<string>>();

        public void Rebuild(IEnumerable<EntityState> entities)
        {
            _cells.Clear();
            foreach (var e in entities)
            {
                if (!_cells.TryGetValue(e.Cell, out var list))
                {
                    list = new List<string>();
                    _cells[e.Cell] = list;
                }
                list.Add(e.Id);
            }
        }

        public IReadOnlyList<string> Get(GridCell cell)
        {
            if (_cells.TryGetValue(cell, out var list)) return list;
            return Empty;
        }

        static readonly string[] Empty = new string[0];
    }
}
