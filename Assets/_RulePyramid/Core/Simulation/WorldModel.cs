using System;
using System.Collections.Generic;
using System.Text;

namespace RulePyramid.Core
{
    /// <summary>RW-v0.9 权威模拟；世界 Text 为唯一显式规则来源。</summary>
    public sealed class WorldModel
    {
        public LevelDefinition Spec;
        public List<EntityState> Entities = new List<EntityState>();
        public HashSet<GridCell> Terrain = new HashSet<GridCell>();
        public RuleSet Rules = new RuleSet();
        public List<PropertyRuleSource> PropertySources = new List<PropertyRuleSource>();
        public Dictionary<string, string> Transforms = new Dictionary<string, string>();
        public List<TransformSource> TransformSources = new List<TransformSource>();
        public Dictionary<string, BounceApexState> Apex = new Dictionary<string, BounceApexState>();
        public HashSet<string> ForcedFall = new HashSet<string>();
        public HashSet<string> Pressed = new HashSet<string>();
        public HashSet<string> TransformedThisTurn = new HashSet<string>();
        public bool WonLatched;
        public WinRecord WinRecord;
        public readonly List<SimEvent> Log = new List<SimEvent>();
        public readonly List<WorldSnapshot> History = new List<WorldSnapshot>();
        /// <summary>Fired after each valid atomic rule resolution with the current YOU cell.</summary>
        public event Action<GridCell> ControlledCellVisited;
        public int BounceRiseCells = 3;
        public string LastRejection;

        public MotionPhase Phase
        {
            get
            {
                if (WonLatched) return MotionPhase.Won;
                EntityState actor;
                try { actor = Actor(); }
                catch (RuleConflictException) { actor = null; }
                if (actor == null) return MotionPhase.NoControl;
                if (Apex.ContainsKey(actor.Id)) return MotionPhase.BounceApex;
                return MotionPhase.Grounded;
            }
        }

        public string ActorId
        {
            get
            {
                try
                {
                    var a = Actor();
                    return a == null ? null : a.Id;
                }
                catch (RuleConflictException)
                {
                    return null;
                }
            }
        }

        public static WorldModel FromLevel(LevelDefinition level, bool settleInitial = false)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (level.fixedRules != null && level.fixedRules.Length > 0)
                throw new InvalidOperationException("Forbidden non-spatial rule source; use world TEXT entities");
            var world = new WorldModel { Spec = LevelCloner.Clone(level) };
            if (world.Spec.options != null && world.Spec.options.bounceRiseCells > 0)
                world.BounceRiseCells = world.Spec.options.bounceRiseCells;
            world.Terrain = LevelCloner.ExpandTerrain(world.Spec.terrain);
            if (world.Spec.entities != null)
            {
                var ids = new HashSet<string>();
                foreach (var def in world.Spec.entities)
                {
                    if (!ids.Add(def.id))
                        throw new InvalidOperationException("Duplicate entity ID");
                    var subject = def.subject ?? "";
                    if (string.IsNullOrEmpty(subject) && !string.IsNullOrEmpty(def.color))
                        subject = def.color;
                    world.Entities.Add(new EntityState
                    {
                        Id = def.id,
                        Kind = EntityState.ParseKind(def.kind),
                        Subject = subject,
                        Token = def.token ?? "",
                        Cell = def.cell,
                        Anchored = def.anchored
                    });
                }
            }
            world.Refresh();
            world.ValidateState();
            var actors = new List<EntityState>();
            foreach (var e in world.Entities)
                if (world.Props(e).Contains("YOU")) actors.Add(e);
            if (actors.Count != 1)
                throw new InvalidOperationException("Authored initial state must have exactly one YOU.");
            if (!world.Solid(actors[0]) || world.Gravity(actors[0]) != GravityMode.Down)
                throw new InvalidOperationException("Implicit player defaults required in these witnesses.");
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
            foreach (var id in TransformedThisTurn) clone.TransformedThisTurn.Add(id);
            clone.Refresh();
            return clone;
        }

        public EntityState Entity(string id)
        {
            foreach (var e in Entities)
                if (e.Id == id) return e;
            return null;
        }

        public HashSet<string> Props(EntityState e)
        {
            if (e == null || e.Kind != EntityKind.Object) return new HashSet<string>();
            return Rules.Get(e.Subject);
        }

        public EntityState Actor()
        {
            return ControlResolver.Actor(Entities, Props, Gravity);
        }

        public EntityState FindYou()
        {
            try { return Actor(); }
            catch (RuleConflictException) { return null; }
        }

        public bool Solid(EntityState e) => PropertyResolver.IsSolid(e, Rules);

        public GravityMode Gravity(EntityState e) => PropertyResolver.ResolveGravity(e, Rules);

        public void Refresh()
        {
            var rules = new RuleSet();
            var targets = new Dictionary<string, HashSet<string>>();
            foreach (var s in Tokens.Subjects) targets[s] = new HashSet<string>();
            var sources = new List<TransformSource>();
            var propertySources = new List<PropertyRuleSource>();

            void Parse(IList<string> ts, object source)
            {
                if (ts == null || ts.Count == 0 || !Tokens.IsSubject(ts[0])) return;
                var subjects = new List<string> { ts[0] };
                int i = 1;
                while (i + 1 < ts.Count && ts[i] == "AND" && Tokens.IsSubject(ts[i + 1]))
                {
                    subjects.Add(ts[i + 1]);
                    i += 2;
                }
                if (i >= ts.Count || ts[i] != "IS") return;
                i++;
                if (i >= ts.Count) return;
                if (Tokens.IsSubject(ts[i]))
                {
                    var target = ts[i];
                    if (i + 2 < ts.Count && ts[i + 1] == "AND"
                        && (Tokens.IsSubject(ts[i + 2]) || Tokens.IsProp(ts[i + 2])))
                        throw new RuleConflictException("TransformRhs: exactly one noun target; use separate sentences");
                    foreach (var subject in subjects)
                    {
                        if (subject == target) continue;
                        targets[subject].Add(target);
                        sources.Add(new TransformSource { Source = subject, Target = target, Origin = source });
                    }
                    return;
                }
                if (!Tokens.IsProp(ts[i])) return;
                var properties = new List<string> { ts[i] };
                i++;
                while (i + 1 < ts.Count && ts[i] == "AND")
                {
                    if (Tokens.IsSubject(ts[i + 1]))
                        throw new RuleConflictException("MixedRhs: properties and noun conversion use separate sentences");
                    if (!Tokens.IsProp(ts[i + 1])) break;
                    properties.Add(ts[i + 1]);
                    i += 2;
                }
                foreach (var subject in subjects)
                foreach (var property in properties)
                {
                    rules[subject].Add(property);
                    propertySources.Add(new PropertyRuleSource { Subject = subject, Property = property, TextIds = new List<string>((IEnumerable<string>)source).ToArray() });
                }
            }

            if (Spec?.fixedRules != null && Spec.fixedRules.Length > 0)
                throw new RuleConflictException("Forbidden non-spatial rule source; use world TEXT entities");

            var textAt = new Dictionary<GridCell, EntityState>();
            foreach (var e in Entities)
            {
                if (e.Kind == EntityKind.Text)
                    textAt[e.Cell] = e;
            }
            var positions = new List<GridCell>(textAt.Keys);
            positions.Sort((a, b) =>
            {
                int c = a.x.CompareTo(b.x);
                if (c != 0) return c;
                c = a.y.CompareTo(b.y);
                if (c != 0) return c;
                return a.z.CompareTo(b.z);
            });
            foreach (var pos in positions)
            {
                var word = textAt[pos];
                if (!Tokens.IsSubject(word.Token)) continue;
                // 俯视图中 +Z 朝上；名词须位于 IS 的左侧或上侧。
                foreach (var d in new[] { GridCell.East, GridCell.South })
                {
                    var ts = new List<string>();
                    var ids = new List<string>();
                    var q = pos;
                    while (textAt.TryGetValue(q, out var at))
                    {
                        ts.Add(at.Token);
                        ids.Add(at.Id);
                        q = q.Add(d);
                    }
                    Parse(ts, ids);
                }
            }

            var ambiguous = new Dictionary<string, List<string>>();
            foreach (var kv in targets)
            {
                if (kv.Value.Count > 1)
                {
                    var list = new List<string>(kv.Value);
                    list.Sort(StringComparer.Ordinal);
                    ambiguous[kv.Key] = list;
                }
            }
            if (ambiguous.Count > 0)
            {
                var sb = new StringBuilder("MultipleTransformTargets: ");
                foreach (var kv in ambiguous)
                    sb.Append(kv.Key).Append('=').Append(string.Join(",", kv.Value)).Append(';');
                throw new RuleConflictException(sb.ToString());
            }

            Rules = rules;
            Transforms = new Dictionary<string, string>();
            foreach (var kv in targets)
            {
                if (kv.Value.Count == 0) continue;
                foreach (var t in kv.Value) { Transforms[kv.Key] = t; break; }
            }
            TransformSources = sources;
            PropertySources = propertySources;
        }

        string RuleSignature()
        {
            var sb = new StringBuilder();
            var props = Rules.SortedSignature();
            var keys = new List<string>(props.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
                sb.Append(k).Append(':').Append(string.Join(",", props[k])).Append('|');
            var tk = new List<string>(Transforms.Keys);
            tk.Sort(StringComparer.Ordinal);
            foreach (var k in tk)
                sb.Append('T').Append(k).Append('>').Append(Transforms[k]).Append('|');
            return sb.ToString();
        }

        public List<(EntityState entity, string before, string after)> PendingTransforms()
        {
            return TransformationResolver.PendingTransforms(Entities, Transforms, TransformedThisTurn);
        }

        public void ResolveRules(string reason)
        {
            var oldActor = ActorIdSafe();
            var oldRules = RuleSignature();
            Refresh();
            var newRules = RuleSignature();
            if (oldRules != newRules)
            {
                Log.Add(new SimEvent
                {
                    Kind = "RulesChanged",
                    Before = oldRules,
                    After = newRules
                });
            }
            var changes = PendingTransforms();
            TransformationResolver.Apply(changes, TransformedThisTurn);
            ValidateState();
            foreach (var change in changes)
            {
                var origins = new List<object>();
                foreach (var s in TransformSources)
                {
                    if (s.Source == change.before && s.Target == change.after)
                        origins.Add(s.Origin);
                }
                Log.Add(new SimEvent
                {
                    Kind = "Transformed",
                    EntityId = change.entity.Id,
                    BeforeSubject = change.before,
                    AfterSubject = change.after,
                    Cell = change.entity.Cell,
                    Sources = origins
                });
            }
            var newActor = ActorIdSafe();
            if (oldActor != newActor)
            {
                Log.Add(new SimEvent
                {
                    Kind = "ControlChanged",
                    OldId = oldActor,
                    NewId = newActor
                });
            }
            if (newActor != null)
                ControlledCellVisited?.Invoke(Entity(newActor).Cell);
            CheckWin(reason);
        }

        string ActorIdSafe()
        {
            try { return ControlResolver.ActorId(Actor()); }
            catch (RuleConflictException) { return null; }
        }

        GridBounds Bounds()
        {
            if (Spec?.bounds == null) return new GridBounds();
            return new GridBounds(Spec.bounds.min, Spec.bounds.max);
        }

        public bool Out(GridCell p) => !Bounds().Contains(p);

        public bool BlocksPair(EntityState a, EntityState b) => CollisionPolicy.BlocksPair(a, b, Rules);

        public void ValidateState()
        {
            Actor();
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
                    if (BlocksPair(e, other))
                        throw new RuleConflictException("illegal pair overlap: " + e.Id + ", " + other.Id + ", " + e.Cell);
                }
                list.Add(e);
            }
        }

        public OccupantHit Occupant(GridCell p, HashSet<string> ignore = null, EntityState mover = null)
        {
            if (Out(p) || Terrain.Contains(p)) return OccupantHit.Wall;
            var hits = new List<EntityState>();
            foreach (var e in Entities)
            {
                if (ignore != null && ignore.Contains(e.Id)) continue;
                if (e.Cell != p) continue;
                bool blocks = mover == null ? Solid(e) : BlocksPair(mover, e);
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
            if (e == null || e.Anchored) return false;
            if (e.Kind == EntityKind.Text) return true;
            if (e.Kind != EntityKind.Object) return false;
            var props = Props(e);
            return props.Contains("PUSH") && !props.Contains("YOU");
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
            var oldActor = ActorIdSafe();
            var staged = new List<(EntityState e, GridCell old, GridCell dest)>();
            foreach (var move in moves)
                staged.Add((move.entity, move.entity.Cell, move.dest));
            foreach (var s in staged) s.e.Cell = s.dest;
            foreach (var s in staged)
            {
                Log.Add(SimEvent.Move(reason, s.e.Id, s.old, s.dest));
                if ((reason == "Pushed" || reason == "HeadBump" || reason == "LandingPress")
                    && s.e.Id != oldActor && s.old != s.dest)
                {
                    Log.Add(new SimEvent
                    {
                        Kind = "ActiveInteraction",
                        EntityKind = s.e.Kind.ToString(),
                        EntityId = s.e.Id,
                        CommandEffect = reason,
                        From = s.old,
                        To = s.dest
                    });
                }
            }
            ResolveRules(reason);
        }

        public void Shift(EntityState e, GridCell d, string reason)
        {
            Commit(new List<(EntityState, GridCell)> { (e, e.Cell.Add(d)) }, reason);
        }

        public List<EntityState> PlanPush(EntityState first, GridCell d)
        {
            var chain = new List<EntityState>();
            var current = first;
            for (int n = 0; n < Entities.Count + 1; n++)
            {
                if (!Movable(current)) return null;
                chain.Add(current);
                var hit = Occupant(current.Cell.Add(d), null, current);
                if (hit.IsEmpty) return chain;
                if (hit.IsWall) return null;
                current = hit.Entity;
            }
            throw new InvalidOperationException("Unexpected push chain cycle");
        }

        void Bounce(EntityState mover, EntityState surface)
        {
            var contact = mover.Cell;
            if (BounceRiseCells != 3)
                throw new InvalidOperationException("bounceRiseCells must be 3");
            Log.Add(new SimEvent
            {
                Kind = "BounceStarted",
                EntityId = mover.Id,
                SurfaceId = surface.Id,
                ContactPos = contact,
                RequestedRise = 3
            });
            int rose = 0;
            var ignore = new HashSet<string> { mover.Id };
            for (int i = 0; i < 3; i++)
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
                RequestedRise = 3,
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
                Log.Add(new SimEvent { Kind = "BounceApexReached", EntityId = mover.Id, ApexPos = mover.Cell, Cell = mover.Cell });
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
                if (!ForcedFall.Contains(mover.Id) && Gravity(mover) != GravityMode.Down)
                    return;
                var dest = mover.Cell.Add(GridCell.Down);
                var hit = Occupant(dest, ignore, mover);
                if (hit.IsEmpty)
                {
                    string reason = Props(mover).Contains("YOU") ? "PlayerFell" : "GravityFall";
                    Shift(mover, GridCell.Down, reason);
                    descended = true;
                    continue;
                }
                if (hit.IsEntity && !Solid(hit.Entity))
                    throw new UnsupportedContactException("Unsupported solid-to-hollow gravity contact in witness subset");
                if (descended && hit.IsEntity && PropertyResolver.HasBouncy(hit.Entity, Rules))
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
            for (int n = 0; n < 128; n++)
            {
                string key = Fingerprint() + PressedKey() + TransformedKey();
                if (!seen.Add(key))
                    throw new InvalidOperationException("Automatic-resolution cycle");
                string before = key;

                var upList = new List<EntityState>(Entities);
                upList.Sort((a, b) =>
                {
                    int cmp = b.Cell.y.CompareTo(a.Cell.y);
                    return cmp != 0 ? cmp : string.CompareOrdinal(a.Id, b.Id);
                });
                foreach (var e in upList)
                {
                    if (e.Kind != EntityKind.Object) continue;
                    if (Props(e).Contains("YOU")) continue;
                    if (Apex.ContainsKey(e.Id) || ForcedFall.Contains(e.Id)) continue;
                    if (Gravity(e) != GravityMode.Up) continue;
                    var group = new List<EntityState> { e };
                    EntityState player = null;
                    try { player = Actor(); } catch (RuleConflictException) { }
                    if (Solid(e) && player != null
                        && !Apex.ContainsKey(player.Id)
                        && !ForcedFall.Contains(player.Id)
                        && player.Cell == e.Cell.Add(GridCell.Up))
                    {
                        group.Add(player);
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
                    bool natural = Gravity(e) == GravityMode.Down;
                    if (forced || natural)
                        Land(e, allowPress: Props(e).Contains("YOU"));
                }

                if (before == Fingerprint() + PressedKey() + TransformedKey()) return;
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
            var sorted = new List<EntityState>(Entities);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            foreach (var e in sorted) snap.Entities.Add(e.Clone());
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
            TransformedThisTurn.Clear();
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
                sb.Append(id).Append('@').Append(e.Cell).Append('/').Append(e.Subject).Append(';');
            }
            var apexIds = new List<string>(Apex.Keys);
            apexIds.Sort(StringComparer.Ordinal);
            foreach (var id in apexIds) sb.Append("A:").Append(id).Append(';');
            var fall = new List<string>(ForcedFall);
            fall.Sort(StringComparer.Ordinal);
            foreach (var id in fall) sb.Append("F:").Append(id).Append(';');
            sb.Append(WonLatched ? "W" : "R");
            if (WinRecord != null)
                sb.Append(WinRecord.YouId).Append(WinRecord.WinId).Append(WinRecord.Cell);
            return sb.ToString();
        }

        string PressedKey()
        {
            var list = new List<string>(Pressed);
            list.Sort(StringComparer.Ordinal);
            return string.Join(",", list);
        }

        string TransformedKey()
        {
            var list = new List<string>(TransformedThisTurn);
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
            cmd = Tokens.NormalizeCommand(cmd);
            message = null;
            LastRejection = null;
            if (cmd == "CAM+" || cmd == "CAM-")
            {
                message = "Player camera is fixed";
                LastRejection = message;
                return false;
            }
            if (Tokens.LegacyJumps.Contains(cmd))
            {
                message = "Removed directional jump";
                return false;
            }
            if (!Tokens.Commands.Contains(cmd))
            {
                message = "Unknown command: " + cmd;
                throw new ArgumentException(message);
            }
            if (WonLatched)
            {
                message = "Already won";
                return false;
            }

            TransformedThisTurn.Clear();
            var snap = Snapshot();
            int oldLog = Log.Count;
            EntityState player;
            try { player = Actor(); }
            catch (RuleConflictException ex)
            {
                message = ex.Message;
                LastRejection = ex.Message;
                return false;
            }
            if (player == null)
            {
                message = "NoControl";
                return false;
            }
            string actorId = player.Id;
            bool hadApex = Apex.ContainsKey(actorId);
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
                        Log.Add(new SimEvent { Kind = "ApexReleased", EntityId = actorId, Steer = null });
                        success = true;
                    }
                    else if (WorldDirections.TryParse(cmd, out var dir))
                    {
                        var d = WorldDirections.ToOffset(dir);
                        if (Free(player.Cell.Add(d), new HashSet<string> { actorId }, player))
                        {
                            Shift(player, d, "ApexSteer");
                            success = true;
                        }
                    }
                }
                else if (cmd == "J")
                {
                    var up = player.Cell.Add(GridCell.Up);
                    var hit = Occupant(up, null, player);
                    if (hit.IsEmpty)
                    {
                        Shift(player, GridCell.Up, "JumpApex");
                        success = true;
                    }
                    else if (hit.IsEntity && Movable(hit.Entity) && Free(up.Add(GridCell.Up), null, hit.Entity))
                    {
                        Commit(new List<(EntityState, GridCell)>
                        {
                            (hit.Entity, up.Add(GridCell.Up)),
                            (player, up)
                        }, "HeadBump");
                        success = true;
                    }
                }
                else if (WorldDirections.TryParse(cmd, out var walkDir))
                {
                    var d = WorldDirections.ToOffset(walkDir);
                    var dest = player.Cell.Add(d);
                    var hit = Occupant(dest, null, player);
                    if (hit.IsEmpty)
                    {
                        Shift(player, d, "Walk");
                        success = true;
                    }
                    else if (hit.IsEntity)
                    {
                        var chain = PlanPush(hit.Entity, d);
                        if (chain != null)
                        {
                            var moves = new List<(EntityState, GridCell)>();
                            foreach (var e in chain) moves.Add((e, e.Cell.Add(d)));
                            moves.Add((player, dest));
                            Commit(moves, "Pushed");
                            success = true;
                        }
                    }
                    // 方向输入统一尝试平移或推动；不可推、推链堵塞时失败，绝不自动登攀。
                }

                if (!success)
                {
                    Restore(snap);
                    TrimLog(oldLog);
                    message = "Rejected";
                    return false;
                }

                ResolveRules("AfterManualPhase");
                EntityState currentActor = null;
                try { currentActor = Actor(); } catch (RuleConflictException) { }
                if (currentActor != null)
                    Land(currentActor, allowPress: true);
                SettleInternal();
            }
            catch (VictoryCommittedException)
            {
                success = true;
            }
            catch (RuleConflictException ex)
            {
                Restore(snap);
                TrimLog(oldLog);
                LastRejection = ex.Message;
                message = ex.Message;
                return false;
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
