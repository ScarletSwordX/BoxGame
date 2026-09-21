using System;
using System.Collections.Generic;
using System.Text;

namespace RulePyramid.Core
{
    public sealed class WorldModel
    {
        public LevelDefinition Spec;
        public List<EntityState> Entities = new List<EntityState>();
        public HashSet<GridCell> Terrain = new HashSet<GridCell>();
        public RuleSet Rules = new RuleSet();
        public Dictionary<string, BounceApexState> Apex = new Dictionary<string, BounceApexState>();
        public HashSet<string> ForcedFall = new HashSet<string>();
        public HashSet<string> Pressed = new HashSet<string>();
        public bool WonLatched;
        public WinRecord WinRecord;
        public readonly List<SimEvent> Log = new List<SimEvent>();
        public readonly List<WorldSnapshot> History = new List<WorldSnapshot>();
        public int BounceRiseCells = 3;

        public MotionPhase Phase
        {
            get
            {
                if (WonLatched) return MotionPhase.Won;
                var you = FindYou();
                if (you != null && Apex.ContainsKey(you.Id)) return MotionPhase.BounceApex;
                return MotionPhase.Grounded;
            }
        }

        public static WorldModel FromLevel(LevelDefinition level, bool settleInitial = false)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            var world = new WorldModel { Spec = LevelCloner.Clone(level) };
            if (world.Spec.options != null && world.Spec.options.bounceRiseCells > 0)
                world.BounceRiseCells = world.Spec.options.bounceRiseCells;
            world.Terrain = LevelCloner.ExpandTerrain(world.Spec.terrain);
            if (world.Spec.entities != null)
            {
                foreach (var def in world.Spec.entities)
                {
                    world.Entities.Add(new EntityState
                    {
                        Id = def.id,
                        Kind = EntityState.ParseKind(def.kind),
                        Color = def.color ?? "",
                        Token = def.token ?? "",
                        Cell = def.cell,
                        Anchored = def.anchored
                    });
                }
            }
            world.Refresh();
            world.ValidateState();
            world.CheckWin("LevelLoaded", interrupt: false);
            if (settleInitial)
                world.Settle();
            return world;
        }

        public WorldModel CloneLive()
        {
            var clone = new WorldModel
            {
                Spec = Spec,
                BounceRiseCells = BounceRiseCells,
                Terrain = Terrain,
                WonLatched = WonLatched,
                WinRecord = WinRecord == null ? null : WinRecord.Clone()
            };
            foreach (var e in Entities) clone.Entities.Add(e.Clone());
            foreach (var kv in Apex) clone.Apex[kv.Key] = kv.Value.Clone();
            foreach (var id in ForcedFall) clone.ForcedFall.Add(id);
            foreach (var id in Pressed) clone.Pressed.Add(id);
            clone.Refresh();
            return clone;
        }

        public EntityState Entity(string id)
        {
            foreach (var e in Entities)
                if (e.Id == id) return e;
            return null;
        }

        public EntityState FindYou()
        {
            EntityState found = null;
            foreach (var e in Entities)
            {
                if (!PropertyResolver.HasYou(e, Rules)) continue;
                if (found != null) return found;
                found = e;
            }
            return found;
        }

        public IEnumerable<string> FixedRuleLines()
        {
            if (Spec?.fixedRules == null) yield break;
            foreach (var rule in Spec.fixedRules)
            {
                if (rule?.tokens == null) continue;
                yield return string.Join(" ", rule.tokens);
            }
        }

        public void Refresh()
        {
            Rules = RuleParser.Parse(FixedRuleLines(), Entities);
        }

        GridBounds Bounds()
        {
            if (Spec?.bounds == null) return new GridBounds();
            return new GridBounds(Spec.bounds.min, Spec.bounds.max);
        }

        public bool Out(GridCell p)
        {
            return !Bounds().Contains(p);
        }

        public OccupantHit Occupant(GridCell p, HashSet<string> ignore = null, EntityState mover = null)
        {
            if (Out(p) || Terrain.Contains(p)) return OccupantHit.Wall;
            var hits = new List<EntityState>();
            foreach (var e in Entities)
            {
                if (ignore != null && ignore.Contains(e.Id)) continue;
                if (e.Cell != p) continue;
                bool blocks = mover == null
                    ? PropertyResolver.IsSolid(e, Rules)
                    : CollisionPolicy.BlocksPair(mover, e, Rules);
                if (blocks) hits.Add(e);
            }
            if (hits.Count == 0) return OccupantHit.None;
            hits.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return new OccupantHit { Entity = hits[0] };
        }

        public bool Free(GridCell p, HashSet<string> ignore = null, EntityState mover = null)
        {
            return Occupant(p, ignore, mover).IsEmpty;
        }

        public bool Movable(EntityState e)
        {
            return e != null && !e.Anchored;
        }

        public void ValidateState()
        {
            var cells = new Dictionary<GridCell, List<EntityState>>();
            foreach (var e in Entities)
            {
                if (Out(e.Cell))
                    throw new InvalidOperationException("out of bounds " + e.Id + " " + e.Cell);
                if (Terrain.Contains(e.Cell))
                    throw new InvalidOperationException("terrain overlap " + e.Id + " " + e.Cell);
                if (!cells.TryGetValue(e.Cell, out var list))
                {
                    list = new List<EntityState>();
                    cells[e.Cell] = list;
                }
                foreach (var other in list)
                {
                    if (CollisionPolicy.BlocksPair(e, other, Rules))
                        throw new InvalidOperationException("illegal pair overlap " + e.Id + " " + other.Id);
                }
                list.Add(e);
            }
        }

        public bool CheckWin(string cause, bool interrupt = true)
        {
            if (WonLatched) return true;
            if (!WinEvaluator.TryFind(Entities, Rules, out var record)) return false;
            record.Cause = cause;
            WonLatched = true;
            WinRecord = record;
            Log.Add(new SimEvent
            {
                Kind = "Won",
                YouId = record.YouId,
                WinId = record.WinId,
                Cell = record.Cell,
                Cause = cause,
                EntityId = record.YouId
            });
            if (interrupt) throw new VictoryCommittedException();
            return true;
        }

        public void Commit(List<(EntityState entity, GridCell dest)> moves, string reason)
        {
            var staged = new List<(EntityState e, GridCell old, GridCell dest)>();
            foreach (var move in moves)
                staged.Add((move.entity, move.entity.Cell, move.dest));
            foreach (var s in staged) s.e.Cell = s.dest;
            foreach (var s in staged)
                Log.Add(SimEvent.Move(reason, s.e.Id, s.old, s.dest));
            Refresh();
            ValidateState();
            CheckWin(reason);
        }

        public void Shift(EntityState e, GridCell d, string reason)
        {
            Commit(new List<(EntityState, GridCell)> { (e, e.Cell.Add(d)) }, reason);
        }

        public List<EntityState> PlanTextPush(EntityState first, GridCell d)
        {
            var chain = new List<EntityState>();
            var current = first;
            for (int n = 0; n < Entities.Count + 1; n++)
            {
                if (current == null || current.Kind != EntityKind.Text || !Movable(current))
                    return null;
                chain.Add(current);
                var hit = Occupant(current.Cell.Add(d));
                if (hit.IsEmpty) return chain;
                if (hit.IsWall) return null;
                current = hit.Entity;
            }
            throw new InvalidOperationException("Unexpected text push chain cycle");
        }

        void Bounce(EntityState mover, EntityState surface)
        {
            var contact = mover.Cell;
            Log.Add(new SimEvent
            {
                Kind = "BounceStarted",
                EntityId = mover.Id,
                SurfaceId = surface.Id,
                ContactPos = contact,
                RequestedRise = BounceRiseCells
            });
            int rose = 0;
            var ignore = new HashSet<string> { mover.Id };
            for (int i = 0; i < BounceRiseCells; i++)
            {
                if (!Free(mover.Cell.Add(GridCell.Up), ignore, mover)) break;
                Shift(mover, GridCell.Up, "BounceStep");
                rose++;
            }
            Log.Add(new SimEvent
            {
                Kind = "LandingBounce",
                EntityId = mover.Id,
                SurfaceId = surface.Id,
                ContactPos = contact,
                ApexPos = mover.Cell,
                RequestedRise = BounceRiseCells,
                ActualRise = rose
            });
            if (rose > 0)
            {
                Apex[mover.Id] = new BounceApexState
                {
                    SurfaceId = surface.Id,
                    ContactPos = contact,
                    ApexPos = mover.Cell,
                    RiseCells = rose,
                    ResumeForcedFall = true
                };
                Log.Add(new SimEvent { Kind = "BounceApexReached", EntityId = mover.Id, ApexPos = mover.Cell });
            }
            else
            {
                Log.Add(new SimEvent { Kind = "BounceBlocked", EntityId = mover.Id, SurfaceId = surface.Id });
            }
            ForcedFall.Remove(mover.Id);
        }

        public void Land(EntityState mover, bool allowPress)
        {
            if (Apex.ContainsKey(mover.Id)) return;
            bool descended = false;
            var ignore = new HashSet<string> { mover.Id };
            for (int n = 0; n < 128; n++)
            {
                var dest = mover.Cell.Add(GridCell.Down);
                var hit = Occupant(dest, ignore, mover);
                if (hit.IsEmpty)
                {
                    string reason = PropertyResolver.HasYou(mover, Rules) ? "PlayerFell" : "GravityFall";
                    Shift(mover, GridCell.Down, reason);
                    descended = true;
                    continue;
                }
                if (hit.IsEntity && !PropertyResolver.IsSolid(hit.Entity, Rules))
                    throw new UnsupportedContactException("Unsupported solid-to-hollow gravity contact");
                if (descended && hit.IsEntity && PropertyResolver.HasJump(hit.Entity, Rules))
                {
                    Bounce(mover, hit.Entity);
                    return;
                }
                if (descended && allowPress && !Pressed.Contains(mover.Id) && hit.IsEntity && Movable(hit.Entity)
                    && Free(hit.Entity.Cell.Add(GridCell.Down), null, hit.Entity))
                {
                    Commit(new List<(EntityState, GridCell)>
                    {
                        (hit.Entity, hit.Entity.Cell.Add(GridCell.Down)),
                        (mover, dest)
                    }, "LandingPress");
                    Pressed.Add(mover.Id);
                }
                ForcedFall.Remove(mover.Id);
                return;
            }
            throw new InvalidOperationException("Landing microstep cap");
        }

        void SettleInternal()
        {
            var seen = new HashSet<string>();
            for (int n = 0; n < 4096; n++)
            {
                string key = Fingerprint() + PressedKey();
                if (!seen.Add(key))
                    throw new InvalidOperationException("Automatic-resolution cycle");
                string before = key;
                var you = FindYou();

                var upList = new List<EntityState>(Entities);
                upList.Sort((a, b) =>
                {
                    int cmp = b.Cell.y.CompareTo(a.Cell.y);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
                });
                foreach (var e in upList)
                {
                    if (e.Kind != EntityKind.Color) continue;
                    if (you != null && e.Id == you.Id) continue;
                    if (Apex.ContainsKey(e.Id) || ForcedFall.Contains(e.Id)) continue;
                    if (PropertyResolver.ResolveGravity(e, Rules) != GravityMode.Up) continue;
                    var group = new List<EntityState> { e };
                    if (PropertyResolver.IsSolid(e, Rules) && you != null && !Apex.ContainsKey(you.Id)
                        && you.Cell == e.Cell.Add(GridCell.Up))
                    {
                        group.Add(you);
                    }
                    var ids = new HashSet<string>();
                    foreach (var v in group) ids.Add(v.Id);
                    bool allFree = true;
                    foreach (var v in group)
                    {
                        if (!Free(v.Cell.Add(GridCell.Up), ids, v))
                        {
                            allFree = false;
                            break;
                        }
                    }
                    if (allFree)
                    {
                        var moves = new List<(EntityState, GridCell)>();
                        foreach (var v in group) moves.Add((v, v.Cell.Add(GridCell.Up)));
                        Commit(moves, "FlyOrRide");
                    }
                }

                var downList = new List<EntityState>(Entities);
                downList.Sort((a, b) =>
                {
                    int cmp = a.Cell.y.CompareTo(b.Cell.y);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
                });
                foreach (var e in downList)
                {
                    if (Apex.ContainsKey(e.Id)) continue;
                    bool forced = ForcedFall.Contains(e.Id);
                    bool natural = PropertyResolver.ResolveGravity(e, Rules) == GravityMode.Down;
                    if (forced || natural)
                        Land(e, allowPress: you != null && e.Id == you.Id);
                }

                if (before == Fingerprint() + PressedKey()) return;
            }
            throw new InvalidOperationException("Resolution microstep cap");
        }

        public void Settle()
        {
            if (WonLatched) return;
            try
            {
                Refresh();
                ValidateState();
                CheckWin("RulesResolved");
                SettleInternal();
            }
            catch (VictoryCommittedException)
            {
            }
        }

        public WorldSnapshot Snapshot()
        {
            var snap = new WorldSnapshot
            {
                Entities = new List<EntityState>(),
                Apex = new Dictionary<string, BounceApexState>(),
                ForcedFall = new List<string>(ForcedFall),
                Won = WonLatched,
                WinRecord = WinRecord == null ? null : WinRecord.Clone()
            };
            foreach (var e in Entities) snap.Entities.Add(e.Clone());
            foreach (var kv in Apex) snap.Apex[kv.Key] = kv.Value.Clone();
            return snap;
        }

        public void Restore(WorldSnapshot snapshot)
        {
            Entities.Clear();
            foreach (var e in snapshot.Entities) Entities.Add(e.Clone());
            Apex.Clear();
            foreach (var kv in snapshot.Apex) Apex[kv.Key] = kv.Value.Clone();
            ForcedFall.Clear();
            foreach (var id in snapshot.ForcedFall) ForcedFall.Add(id);
            WonLatched = snapshot.Won;
            WinRecord = snapshot.WinRecord == null ? null : snapshot.WinRecord.Clone();
            Pressed.Clear();
            Refresh();
            ValidateState();
        }

        public string Fingerprint()
        {
            var sb = new StringBuilder();
            var ids = new List<string>();
            foreach (var e in Entities) ids.Add(e.Id);
            ids.Sort(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var e = Entity(id);
                sb.Append(id).Append('@').Append(e.Cell).Append(';');
            }
            var apexIds = new List<string>(Apex.Keys);
            apexIds.Sort(StringComparer.Ordinal);
            foreach (var id in apexIds) sb.Append("A:").Append(id).Append(';');
            var fall = new List<string>(ForcedFall);
            fall.Sort(StringComparer.Ordinal);
            foreach (var id in fall) sb.Append("F:").Append(id).Append(';');
            sb.Append(WonLatched ? "W" : "R");
            return sb.ToString();
        }

        string PressedKey()
        {
            var list = new List<string>(Pressed);
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list);
        }

        public bool Undo()
        {
            if (History.Count == 0) return false;
            var snap = History[History.Count - 1];
            History.RemoveAt(History.Count - 1);
            Restore(snap);
            return true;
        }

        public bool TryCommand(string cmd, out string message)
        {
            message = null;
            if (Tokens.LegacyJumps.Contains(cmd))
            {
                message = "Removed directional jump";
                return false;
            }
            if (!Tokens.SupportedCommands.Contains(cmd))
            {
                message = "Unknown command: " + cmd;
                throw new ArgumentException(message);
            }
            if (WonLatched)
            {
                message = "Already won";
                return false;
            }

            var snap = Snapshot();
            int oldLog = Log.Count;
            var you = FindYou();
            if (you == null)
            {
                message = "No YOU entity";
                return false;
            }
            bool hadApex = Apex.ContainsKey(you.Id);
            foreach (var id in new List<string>(Apex.Keys)) ForcedFall.Add(id);
            Apex.Clear();
            Pressed.Clear();
            bool success = false;
            try
            {
                if (hadApex)
                {
                    if (cmd == "WAIT" || cmd == "J")
                    {
                        Log.Add(new SimEvent { Kind = "ApexReleased", EntityId = you.Id });
                        success = true;
                    }
                    else if (WorldDirections.TryParse(cmd, out var dir))
                    {
                        var d = WorldDirections.ToOffset(dir);
                        if (Free(you.Cell.Add(d), new HashSet<string> { you.Id }, you))
                        {
                            Shift(you, d, "ApexSteer");
                            success = true;
                        }
                    }
                }
                else if (cmd == "J")
                {
                    var up = you.Cell.Add(GridCell.Up);
                    var hit = Occupant(up, null, you);
                    if (hit.IsEmpty)
                    {
                        Shift(you, GridCell.Up, "JumpApex");
                        success = true;
                    }
                    else if (hit.IsEntity && Movable(hit.Entity) && Free(up.Add(GridCell.Up), null, hit.Entity))
                    {
                        Commit(new List<(EntityState, GridCell)>
                        {
                            (hit.Entity, up.Add(GridCell.Up)),
                            (you, up)
                        }, "HeadBump");
                        success = true;
                    }
                }
                else if (WorldDirections.TryParse(cmd, out var walkDir))
                {
                    var d = WorldDirections.ToOffset(walkDir);
                    var dest = you.Cell.Add(d);
                    var hit = Occupant(dest, null, you);
                    if (hit.IsEmpty)
                    {
                        Shift(you, d, "Walk");
                        success = true;
                    }
                    else if (hit.IsEntity && hit.Entity.Kind == EntityKind.Text && Movable(hit.Entity))
                    {
                        var chain = PlanTextPush(hit.Entity, d);
                        if (chain != null)
                        {
                            var moves = new List<(EntityState, GridCell)>();
                            foreach (var e in chain) moves.Add((e, e.Cell.Add(d)));
                            moves.Add((you, dest));
                            Commit(moves, "TextPushed");
                            success = true;
                        }
                    }
                }

                if (!success)
                {
                    Restore(snap);
                    TrimLog(oldLog);
                    message = "Rejected";
                    return false;
                }

                Land(you, allowPress: true);
                SettleInternal();
            }
            catch (VictoryCommittedException)
            {
                success = true;
            }
            catch (UnsupportedContactException ex)
            {
                Restore(snap);
                TrimLog(oldLog);
                message = ex.Message;
                return false;
            }
            catch
            {
                Restore(snap);
                TrimLog(oldLog);
                throw;
            }

            History.Add(snap);
            return true;
        }

        void TrimLog(int oldCount)
        {
            if (Log.Count > oldCount)
                Log.RemoveRange(oldCount, Log.Count - oldCount);
        }
    }

    public static class SimulationEngine
    {
        public static StepResult TryStep(WorldModel source, string command)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (Tokens.LegacyJumps.Contains(command))
                return StepResult.Rejected("Removed directional jump");

            var working = source;
            try
            {
                bool accepted = working.TryCommand(command, out var message);
                if (!accepted)
                    return new StepResult { Status = StepStatus.Rejected, World = working, Message = message, Events = working.Log };
                return new StepResult
                {
                    Status = StepStatus.Accepted,
                    World = working,
                    Events = working.Log,
                    Won = working.WonLatched,
                    Message = message
                };
            }
            catch (Exception ex)
            {
                return new StepResult
                {
                    Status = StepStatus.SimulationError,
                    World = working,
                    Message = ex.Message,
                    Events = working.Log
                };
            }
        }
    }
}
