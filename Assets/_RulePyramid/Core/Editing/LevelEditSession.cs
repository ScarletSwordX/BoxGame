using System;
using System.Collections.Generic;

namespace RulePyramid.Core
{
    public abstract class EditCommand
    {
        public abstract void Apply(LevelDefinition draft);
        public abstract void Undo(LevelDefinition draft);
    }

    public sealed class PlaceEntityCommand : EditCommand
    {
        public EntityDefinition Entity;
        EntityDefinition _removed;

        public override void Apply(LevelDefinition draft)
        {
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].cell.Equals(Entity.cell) && list[i].kind == Entity.kind)
                {
                    _removed = list[i];
                    list.RemoveAt(i);
                    break;
                }
            }
            list.Add(Clone(Entity));
            draft.entities = list.ToArray();
        }

        public override void Undo(LevelDefinition draft)
        {
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].id == Entity.id)
                {
                    list.RemoveAt(i);
                    break;
                }
            }
            if (_removed != null) list.Add(Clone(_removed));
            draft.entities = list.ToArray();
        }

        static EntityDefinition Clone(EntityDefinition e)
        {
            return new EntityDefinition
            {
                id = e.id,
                kind = e.kind,
                subject = e.subject,
                color = e.color,
                token = e.token,
                cell = e.cell,
                anchored = e.anchored
            };
        }
    }

    public sealed class EraseAtCommand : EditCommand
    {
        public GridCell Cell;
        public string Kind;
        readonly List<EntityDefinition> _removed = new List<EntityDefinition>();
        readonly List<GridCellBox> _removedTerrain = new List<GridCellBox>();

        public override void Apply(LevelDefinition draft)
        {
            _removed.Clear();
            _removedTerrain.Clear();
            if (Kind == "Terrain")
            {
                var boxes = new List<GridCellBox>(draft.terrain ?? Array.Empty<GridCellBox>());
                var remaining = new List<GridCellBox>();
                foreach (var box in boxes)
                {
                    bool covers = Cell.x >= box.min.x && Cell.x <= box.max.x
                        && Cell.y >= box.min.y && Cell.y <= box.max.y
                        && Cell.z >= box.min.z && Cell.z <= box.max.z;
                    if (covers && box.min.x == box.max.x && box.min.y == box.max.y && box.min.z == box.max.z)
                    {
                        _removedTerrain.Add(box);
                    }
                    else remaining.Add(box);
                }
                if (_removedTerrain.Count == 0)
                {
                    remaining.Add(new GridCellBox { min = Cell, max = Cell });
                    // erase single cell by not adding; instead punch by adding nothing and storing a 1-cell box we remove
                }
                // Replace 1-cell terrain only
                var rebuilt = new List<GridCellBox>();
                var cells = LevelCloner.ExpandTerrain(draft.terrain);
                if (cells.Remove(Cell))
                    _removedTerrain.Add(new GridCellBox { min = Cell, max = Cell });
                foreach (var c in cells)
                    rebuilt.Add(new GridCellBox { min = c, max = c });
                draft.terrain = rebuilt.ToArray();
                return;
            }

            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            var kept = new List<EntityDefinition>();
            foreach (var e in list)
            {
                if (e.cell.Equals(Cell) && (Kind == null || e.kind == Kind))
                    _removed.Add(e);
                else kept.Add(e);
            }
            draft.entities = kept.ToArray();
        }

        public override void Undo(LevelDefinition draft)
        {
            if (Kind == "Terrain")
            {
                var cells = LevelCloner.ExpandTerrain(draft.terrain);
                foreach (var box in _removedTerrain)
                    cells.Add(box.min);
                var rebuilt = new List<GridCellBox>();
                foreach (var c in cells) rebuilt.Add(new GridCellBox { min = c, max = c });
                draft.terrain = rebuilt.ToArray();
                return;
            }
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            list.AddRange(_removed);
            draft.entities = list.ToArray();
        }
    }

    public sealed class PlaceTerrainCommand : EditCommand
    {
        public GridCell Cell;

        public override void Apply(LevelDefinition draft)
        {
            var cells = LevelCloner.ExpandTerrain(draft.terrain);
            cells.Add(Cell);
            var rebuilt = new List<GridCellBox>();
            foreach (var c in cells) rebuilt.Add(new GridCellBox { min = c, max = c });
            draft.terrain = rebuilt.ToArray();
        }

        public override void Undo(LevelDefinition draft)
        {
            var cells = LevelCloner.ExpandTerrain(draft.terrain);
            cells.Remove(Cell);
            var rebuilt = new List<GridCellBox>();
            foreach (var c in cells) rebuilt.Add(new GridCellBox { min = c, max = c });
            draft.terrain = rebuilt.ToArray();
        }
    }

    public sealed class MoveEntityCommand : EditCommand
    {
        public string Id;
        public GridCell From;
        public GridCell To;

        public override void Apply(LevelDefinition draft)
        {
            foreach (var e in draft.entities)
            {
                if (e.id == Id)
                {
                    From = e.cell;
                    e.cell = To;
                    return;
                }
            }
        }

        public override void Undo(LevelDefinition draft)
        {
            foreach (var e in draft.entities)
            {
                if (e.id == Id)
                {
                    e.cell = From;
                    return;
                }
            }
        }
    }

    public sealed class LevelEditSession
    {
        public LevelDefinition Draft { get; private set; }
        public string SourcePath;
        public bool Dirty { get; private set; }
        public int CurrentY;
        readonly Stack<EditCommand> _undo = new Stack<EditCommand>();
        readonly Stack<EditCommand> _redo = new Stack<EditCommand>();
        GameSession _playtest;

        public LevelEditSession(LevelDefinition draft, string sourcePath = null)
        {
            Draft = LevelCloner.Clone(draft);
            SourcePath = sourcePath;
            CurrentY = 1;
        }

        public void Apply(EditCommand command)
        {
            if (_playtest != null) return;
            command.Apply(Draft);
            _undo.Push(command);
            _redo.Clear();
            Dirty = true;
        }

        public bool Undo()
        {
            if (_playtest != null || _undo.Count == 0) return false;
            var cmd = _undo.Pop();
            cmd.Undo(Draft);
            _redo.Push(cmd);
            Dirty = _undo.Count > 0;
            return true;
        }

        public bool Redo()
        {
            if (_playtest != null || _redo.Count == 0) return false;
            var cmd = _redo.Pop();
            cmd.Apply(Draft);
            _undo.Push(cmd);
            Dirty = true;
            return true;
        }

        public ValidationReport Validate()
        {
            return LevelValidator.ValidateForPlaytest(Draft);
        }

        public bool TryStartPlaytest(out GameSession session, out string error)
        {
            session = null;
            error = null;
            var report = LevelValidator.ValidateForPlaytest(Draft);
            if (!report.CanPlaytest)
            {
                error = report.ToString();
                return false;
            }
            _playtest = new GameSession(Draft);
            session = _playtest;
            return true;
        }

        public GameSession Playtest => _playtest;

        public void StopPlaytest()
        {
            _playtest = null;
        }

        public void MarkSaved()
        {
            Dirty = false;
            _undo.Clear();
            _redo.Clear();
        }

        public string NextEntityId(string prefix)
        {
            int n = 1;
            var used = new HashSet<string>();
            if (Draft.entities != null)
                foreach (var e in Draft.entities) used.Add(e.id);
            while (used.Contains(prefix + n)) n++;
            return prefix + n;
        }
    }
}
