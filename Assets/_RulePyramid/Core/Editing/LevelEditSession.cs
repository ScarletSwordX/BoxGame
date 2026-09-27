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

        public override void Apply(LevelDefinition draft)
        {
            var list = new List<EntityDefinition>(draft.entities ?? Array.Empty<EntityDefinition>());
            if (Entity == null || string.IsNullOrEmpty(Entity.id)) throw new ArgumentException("Entity needs an id");
            foreach (var existing in list)
                if (existing.id == Entity.id) throw new ArgumentException("Duplicate entity id: " + Entity.id);
            if (!AuthoringOperations.Contains(draft, Entity.cell)) throw new ArgumentException("目标格超出地图边界：" + Entity.cell);
            if (LevelCloner.ExpandTerrain(draft.terrain).Contains(Entity.cell)) throw new ArgumentException("目标格已有地形：" + Entity.cell);
            foreach (var existing in list)
                if (existing.cell == Entity.cell) throw new ArgumentException("目标格已有物体或词牌：" + Entity.cell);
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
        GridCellBox[] _beforeTerrain;

        public override void Apply(LevelDefinition draft)
        {
            _removed.Clear();
            if (Kind == "Terrain")
            {
                _beforeTerrain = LevelCloner.Clone(draft).terrain;
                AuthoringOperations.RemoveTerrain(draft, Cell, Cell);
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
                draft.terrain = _beforeTerrain;
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
        GridCellBox[] _beforeTerrain;

        public override void Apply(LevelDefinition draft)
        {
            _beforeTerrain = LevelCloner.Clone(draft).terrain;
            AuthoringOperations.AddTerrain(draft, Cell, Cell);
        }

        public override void Undo(LevelDefinition draft)
        {
            draft.terrain = _beforeTerrain;
        }
    }

    public sealed class MoveEntityCommand : EditCommand
    {
        public string Id;
        public GridCell From;
        public GridCell To;

        public override void Apply(LevelDefinition draft)
        {
            foreach (var e in draft.entities ?? Array.Empty<EntityDefinition>())
            {
                if (e.id == Id)
                {
                    From = e.cell;
                    if (!AuthoringOperations.Contains(draft, To))
                        throw new ArgumentOutOfRangeException(nameof(To), "Outside level bounds: " + To);
                    e.cell = To;
                    return;
                }
            }
            throw new ArgumentException("Unknown entity id: " + Id);
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
        public string ActiveStageId { get; private set; }
        sealed class EditRecord
        {
            public string Label;
            public LevelDefinition Before;
            public LevelDefinition After;
            public int BeforeRevision;
            public int AfterRevision;
            public string BeforeStageId;
            public string AfterStageId;
        }
        readonly Stack<EditRecord> _undo = new Stack<EditRecord>();
        readonly Stack<EditRecord> _redo = new Stack<EditRecord>();
        int _revision;
        int _savedRevision;
        int _nextRevision = 1;
        GameSession _playtest;

        public bool CanUndo => _playtest == null && _undo.Count > 0;
        public bool CanRedo => _playtest == null && _redo.Count > 0;
        public string UndoLabel => _undo.Count > 0 ? _undo.Peek().Label : null;
        public string RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

        public LevelEditSession(LevelDefinition draft, string sourcePath = null)
        {
            Draft = LevelCloner.Clone(draft);
            if (Draft.stagePlan != null) StageAuthoring.EnsurePlan(Draft);
            ActiveStageId = Draft.stagePlan?.stages[0].id;
            LoadActiveStage();
            SourcePath = sourcePath;
            CurrentY = 1;
        }

        public void Apply(EditCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            Edit(command.GetType().Name, command.Apply);
        }

        public void Edit(string label, Action<LevelDefinition> mutation)
        {
            if (mutation == null) throw new ArgumentNullException(nameof(mutation));
            if (_playtest != null) throw new InvalidOperationException("Stop playtest before editing");
            var before = LevelCloner.Clone(Draft);
            var after = LevelCloner.Clone(Draft);
            var beforeStageId = ActiveStageId;
            mutation(after);
            FlushStage(after, ActiveStageId);
            if (after.stagePlan != null && Array.Find(after.stagePlan.stages, s => s != null && s.id == ActiveStageId) == null)
            {
                ActiveStageId = after.stagePlan.stages[0].id;
                LoadStage(after, ActiveStageId);
            }
            int next = _nextRevision++;
            _undo.Push(new EditRecord { Label = label, Before = before, After = LevelCloner.Clone(after), BeforeRevision = _revision, AfterRevision = next,
                BeforeStageId = beforeStageId,
                AfterStageId = ActiveStageId });
            _redo.Clear();
            Draft = after;
            _revision = next;
            Dirty = _revision != _savedRevision;
        }

        public bool Undo()
        {
            if (_playtest != null || _undo.Count == 0) return false;
            var record = _undo.Pop();
            Draft = LevelCloner.Clone(record.Before);
            ActiveStageId = record.BeforeStageId;
            LoadActiveStage();
            _redo.Push(record);
            _revision = record.BeforeRevision;
            Dirty = _revision != _savedRevision;
            return true;
        }

        public bool Redo()
        {
            if (_playtest != null || _redo.Count == 0) return false;
            var record = _redo.Pop();
            Draft = LevelCloner.Clone(record.After);
            ActiveStageId = record.AfterStageId;
            LoadActiveStage();
            _undo.Push(record);
            _revision = record.AfterRevision;
            Dirty = _revision != _savedRevision;
            return true;
        }

        public ValidationReport Validate()
        {
            var stageReport = LevelValidator.ValidateStageAuthoring(Draft, ActiveStageId);
            if (!stageReport.CanPlaytest) return stageReport;
            if (Draft.designContract != null && Draft.referenceSolutions != null
                && Draft.referenceSolutions.Length < Draft.designContract.minimumSolutionFamilies)
                stageReport.Add(ValidationSeverity.Warning, "SOLUTIONS", "当前阶段参考解尚未录制完成，可先试玩并录制");
            try
            {
                foreach (var issue in LevelValidator.ValidateForPlaytest(BuildPlaytestLevel()).Issues)
                    stageReport.Issues.Add(issue);
            }
            catch (Exception ex)
            {
                stageReport.Add(ValidationSeverity.StructureError, "STAGE_PROJECT", ex.Message);
            }
            return stageReport;
        }

        public bool TryStartPlaytest(out GameSession session, out string error)
        {
            session = null;
            error = null;
            var report = Validate();
            if (!report.CanPlaytest)
            {
                error = report.ToString();
                return false;
            }
            _playtest = new GameSession(BuildPlaytestLevel());
            session = _playtest;
            return true;
        }

        public GameSession Playtest => _playtest;

        public void SwitchStage(string id)
        {
            if (_playtest != null) throw new InvalidOperationException("Stop playtest before switching stage");
            if (Draft.stagePlan == null || Array.Find(Draft.stagePlan.stages, s => s != null && s.id == id) == null)
                throw new ArgumentException("未知阶段：" + id);
            FlushStage(Draft, ActiveStageId);
            ActiveStageId = id;
            LoadActiveStage();
        }

        public LevelDefinition BuildPlaytestLevel()
        {
            var projected = BuildStandaloneMap(Draft.id);
            if (projected.designContract != null)
                projected.designContract.minimumSolutionFamilies = 0;
            return projected;
        }

        public LevelDefinition BuildStandaloneMap(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("地图 ID 不能为空");
            var copy = LevelCloner.Clone(Draft);
            if (copy.stagePlan != null)
            {
                FlushStage(copy, ActiveStageId);
                var sourceJson = copy.authoringSourceJson;
                copy = StageAuthoring.Project(copy, ActiveStageId);
                copy.authoringSourceJson = sourceJson;
            }
            copy.id = id;
            return copy;
        }

        void LoadActiveStage() { LoadStage(Draft, ActiveStageId); }

        static void LoadStage(LevelDefinition level, string id)
        {
            if (level.stagePlan == null) return;
            var stage = Array.Find(level.stagePlan.stages, s => s != null && s.id == id);
            level.entities = LevelCloner.CloneEntities(stage.entities);
            level.tutorial = LevelCloner.CloneTutorial(stage.tutorial);
            level.referenceSolutions = LevelCloner.CloneSolutions(stage.referenceSolutions);
        }

        static void FlushStage(LevelDefinition level, string id)
        {
            if (level.stagePlan == null) return;
            var stage = Array.Find(level.stagePlan.stages, s => s != null && s.id == id);
            if (stage == null) return;
            stage.entities = LevelCloner.CloneEntities(level.entities);
            stage.tutorial = LevelCloner.CloneTutorial(level.tutorial);
            stage.referenceSolutions = LevelCloner.CloneSolutions(level.referenceSolutions);
        }

        public void StopPlaytest()
        {
            _playtest = null;
        }

        public void MarkSaved()
        {
            _savedRevision = _revision;
            Dirty = false;
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
