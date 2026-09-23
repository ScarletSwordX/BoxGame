"""Rule Workshop v0.9 finite design-reference checker with noun transformations.
Python 3.10+, standard library only: python reference_check.py
Uses the bundled authored JSON. Not a game runtime, Unity test, or general proof.
Derived from the uploaded v0.5 reference model, with new mechanics and new fixtures.
"""
from __future__ import annotations
import copy
import json
from pathlib import Path
from typing import Any

DIRECTIONS={'E':(1,0,0),'W':(-1,0,0),'N':(0,0,1),'S':(0,0,-1)}
PUSH_COMMANDS={'P'+d for d in DIRECTIONS}
SUBJECTS={'ROBOT','ROCK','CLOUD','SPRING','FLAG'}
PROPS={'YOU','PUSH','STOP','HOVER','FLY','WIN','BOUNCY'}
UP=(0,1,0); DOWN=(0,-1,0)
COMMANDS=[*DIRECTIONS,*sorted(PUSH_COMMANDS),'J','WAIT']
LEGACY_JUMPS={'JE','JW','JN','JS'}
ROOT=Path(__file__).resolve().parent

def add(a,b): return tuple(x+y for x,y in zip(a,b))

class RuleConflict(ValueError):
    pass


class _VictoryCommitted(Exception):
    """Internal short-circuit: stop a successful transaction at its winning microstep."""


class Model:
    """RW-v0.9 finite graybox witness model, not a complete Unity implementation.

    One YOU; implicit solid; default gravity. PUSH and STOP both imply solid.
    Dynamic collisions require both solids. Only terrain blocks all entities.
    Supported test layouts have a physical bottom; void death is not modeled.
    """
    def __init__(self, spec: dict[str, Any]):
        if 'fixedRules' in spec or 'baseRules' in spec or 'ruleOverrides' in spec:
            raise ValueError('Forbidden non-spatial rule source; use world TEXT entities')
        self.spec = copy.deepcopy(spec)
        self.entities = copy.deepcopy(spec['entities'])
        self.terrain: set[tuple[int,int,int]] = set()
        self.camera = 0
        self.log: list[dict[str, Any]] = []
        self.history: list[dict[str, Any]] = []
        self.apex: dict[str, dict[str, Any]] = {}
        self.forced_fall: set[str] = set()
        self.pressed: set[str] = set()
        self.transformed_this_turn: set[str] = set()
        self.won_latched = False
        self.win_record = None
        for x0,x1,y0,y1,z0,z1 in spec['solidBoxes']:
            self.terrain.update((x,y,z) for x in range(x0,x1+1)
                                for y in range(y0,y1+1) for z in range(z0,z1+1))
        ids = [e['id'] for e in self.entities]
        assert len(ids) == len(set(ids)), 'Duplicate entity ID'
        self.refresh()
        self.validate_state()
        actors = [e for e in self.entities if 'YOU' in self.props(e)]
        assert len(actors) == 1, 'Authored initial state must have exactly one YOU.'
        assert self.solid(actors[0]) and self.gravity(actors[0]) == 'Down', 'Implicit player defaults required in these witnesses.'
        self.check_win('LevelLoaded', interrupt=False)

    def entity(self, eid):
        return next(e for e in self.entities if e['id'] == eid)

    def actor(self):
        actors = [e for e in self.entities if 'YOU' in self.props(e)]
        if len(actors) > 1:
            raise RuleConflict('MultipleYOU: this prototype uses one controlled entity')
        if actors and self.gravity(actors[0]) != 'Down':
            raise RuleConflict('ControlledGravity: direct YOU+HOVER/FLY input is outside this prototype')
        return actors[0] if actors else None

    def actor_id(self):
        actor = self.actor()
        return actor['id'] if actor else None

    def props(self, e):
        return self.rules.get(e.get('subject'),set()) if e['kind'] == 'OBJECT' else set()

    def refresh(self):
        """Parse properties and noun rewrites without mutating world entities.

        Homogeneous property AND lists are preserved. Noun output is one noun;
        RHS noun/property mixtures or multiple noun outputs are diagnosed.
        Prefix behavior for old property-only sentences remains unchanged.
        """
        rules = {c:set() for c in SUBJECTS}
        targets = {c:set() for c in SUBJECTS}
        sources = []
        property_sources = []
        def parse(ts, source):
            if not ts or ts[0] not in SUBJECTS:
                return
            subjects = [ts[0]]; i = 1
            while i+1 < len(ts) and ts[i] == 'AND' and ts[i+1] in SUBJECTS:
                subjects.append(ts[i+1]); i += 2
            if i >= len(ts) or ts[i] != 'IS':
                return
            i += 1
            if i >= len(ts):
                return
            if ts[i] in SUBJECTS:
                target = ts[i]
                if i+2 < len(ts) and ts[i+1] == 'AND' and ts[i+2] in (SUBJECTS | PROPS):
                    raise RuleConflict('TransformRhs: exactly one noun target; use separate sentences')
                for subject in subjects:
                    # Self-identity is a no-op, not a protection rule in this prototype.
                    if subject != target:
                        targets[subject].add(target)
                        sources.append(dict(source=subject,target=target,origin=list(source[:i+1])))
                return
            if ts[i] not in PROPS:
                return
            properties = [ts[i]]; i += 1
            while i+1 < len(ts) and ts[i] == 'AND':
                if ts[i+1] in SUBJECTS:
                    raise RuleConflict('MixedRhs: properties and noun conversion use separate sentences')
                if ts[i+1] not in PROPS:
                    break
                properties.append(ts[i+1]); i += 2
            for subject in subjects:
                rules[subject].update(properties)
                for prop in properties:
                    property_sources.append(dict(subject=subject,property=prop,textIds=list(source[:i])))
        tokens = {tuple(e['pos']): e for e in self.entities if e['kind'] == 'TEXT'}
        for pos, word in sorted(tokens.items()):
            if word['token'] not in SUBJECTS:
                continue
            for d in [(1,0,0),(0,0,1)]:
                q = pos; ts = []; ids = []
                while q in tokens:
                    ts.append(tokens[q]['token']); ids.append(tokens[q]['id']); q = add(q,d)
                parse(ts, ids)
        ambiguous = {k:sorted(v) for k,v in targets.items() if len(v)>1}
        if ambiguous:
            raise RuleConflict('MultipleTransformTargets: '+repr(ambiguous))
        self.rules = rules
        self.transforms = {k:next(iter(v)) for k,v in targets.items() if v}
        self.transform_sources = sources
        self.property_sources = property_sources

    def rule_signature(self):
        return dict(properties={k:sorted(v) for k,v in self.rules.items()},
                    transformations=dict(sorted(self.transforms.items())))

    def pending_transforms(self):
        # All source subjects are captured before applying any of this batch.
        return [(e,e['subject'],self.transforms[e['subject']])
                for e in sorted(self.entities,key=lambda v:v['id'])
                if e['kind']=='OBJECT' and e['id'] not in self.transformed_this_turn
                and e.get('subject') in self.transforms]

    def resolve_rules(self, reason):
        old_actor = self.actor_id()
        old_rules = self.rule_signature()
        self.refresh()
        new_rules = self.rule_signature()
        if old_rules != new_rules:
            self.event('RulesChanged', before=old_rules, after=new_rules)
        changes = self.pending_transforms()
        for e,before,after in changes:
            e['subject'] = after
            self.transformed_this_turn.add(e['id'])
        # Properties are derived from current subject; never copied from the old noun.
        self.validate_state()
        for e,before,after in changes:
            origins = [s['origin'] for s in self.transform_sources
                       if s['source']==before and s['target']==after]
            self.event('Transformed',id=e['id'],beforeSubject=before,afterSubject=after,
                       cell=list(e['pos']),sources=origins)
        new_actor = self.actor_id()
        if old_actor != new_actor:
            self.event('ControlChanged',oldId=old_actor,newId=new_actor)
        self.check_win(reason)

    def solid(self, e):
        return e['kind'] == 'TEXT' or bool({'STOP','PUSH','YOU'} & self.props(e))

    def gravity(self, e):
        if e.get('anchored',False): return 'Anchored'
        if 'FLY' in self.props(e): return 'Up'
        if 'HOVER' in self.props(e): return 'Hover'
        return 'Down'

    def out(self, p):
        x0,x1,y0,y1,z0,z1 = self.spec['bounds']; x,y,z = p
        return not (x0 <= x <= x1 and y0 <= y <= y1 and z0 <= z <= z1)

    def blocks_pair(self, a, b):
        # Dynamic overlap is symmetric: only two effective solids exclude each other.
        # Terrain is checked separately and blocks all dynamic entities.
        return self.solid(a) and self.solid(b)

    def validate_state(self):
        self.actor()
        cells={}
        for e in self.entities:
            p=tuple(e['pos'])
            assert not self.out(p), ('out of bounds',e['id'],p)
            assert p not in self.terrain, ('terrain overlap',e['id'],p)
            for other in cells.get(p,[]):
                
                if self.blocks_pair(e,other):
                    raise RuleConflict(f'illegal pair overlap: {e["id"]}, {other["id"]}, {p}')
            cells.setdefault(p,[]).append(e)

    def occupant(self, p, ignore=(), mover=None):
        if self.out(p) or p in self.terrain: return 'WALL'
        hits=[]
        for e in self.entities:
            if e['id'] in ignore or tuple(e['pos'])!=p: continue
            if (self.solid(e) if mover is None else self.blocks_pair(mover,e)): hits.append(e)
        return sorted(hits,key=lambda e:e['id'])[0] if hits else None

    def free(self, p, ignore=(), mover=None):
        return self.occupant(p, ignore, mover) is None

    def movable(self, e):
        # Player-induced displacement requires PUSH, including vertical bump/press.
        return (isinstance(e, dict) and not e.get('anchored',False)
                and (e['kind']=='TEXT' or 'PUSH' in self.props(e))
                and 'YOU' not in self.props(e))

    def event(self, kind, **fields):
        self.log.append({'event':kind, **fields})

    def check_win(self, cause, *, interrupt=True):
        if self.won_latched:
            return True
        actors = sorted((e for e in self.entities if 'YOU' in self.props(e)), key=lambda e:e['id'])
        goals = sorted((e for e in self.entities if 'WIN' in self.props(e)), key=lambda e:e['id'])
        pair = next(((a,b) for a in actors for b in goals
                     if a['id'] != b['id'] and a['pos'] == b['pos']), None)
        if pair is None:
            return False
        a,b = pair
        self.won_latched = True
        self.win_record = {'youId':a['id'], 'winId':b['id'], 'cell':list(a['pos']), 'cause':cause}
        self.event('Won', **self.win_record)
        if interrupt:
            raise _VictoryCommitted
        return True

    def commit(self, moves, reason):
        """Commit a whole push/head-bump/ride group, then parse+validate+check WIN.
        No victory evaluation is permitted between endpoints of the same atomic group.
        """
        old_actor = self.actor_id()
        staged = [(e, list(e['pos']), list(dest)) for e,dest in moves]
        for e,_,dest in staged:
            e['pos'] = dest
        for e,old,dest in staged:
            self.event(reason, id=e['id'], **{'from':old, 'to':dest})
            if (reason in ('Pushed','HeadBump','LandingPress')
                    and e['id'] != old_actor and old != dest):
                self.event('ActiveInteraction', entityKind=e['kind'], id=e['id'],
                           commandEffect=reason, before=old, after=dest)
        self.resolve_rules(reason)

    def shift(self, e, d, reason):
        self.commit([(e,add(tuple(e['pos']),d))], reason)

    def plan_push(self, first, d):
        chain=[]; current=first
        for _ in range(len(self.entities)+1):
            if not self.movable(current):
                return None
            chain.append(current)
            current=self.occupant(add(tuple(current['pos']),d), mover=current)
            if current is None:
                return chain
        raise AssertionError('Unexpected push chain cycle')

    def bounce(self, mover, surface):
        contact = list(mover['pos'])
        assert self.spec.get('bounceRiseCells',3) == 3
        self.event('BounceStarted',id=mover['id'],surfaceId=surface['id'],contactPos=contact,requestedRise=3)
        rose = 0
        for _ in range(3):
            if not self.free(add(tuple(mover['pos']),UP),ignore=(mover['id'],),mover=mover):
                break
            self.shift(mover,UP,'BounceStep'); rose += 1
        self.event('LandingBounce', id=mover['id'], surfaceId=surface['id'], contactPos=contact,
                   apexPos=list(mover['pos']), requestedRise=3, actualRise=rose)
        if rose:
            self.apex[mover['id']] = {'surfaceId':surface['id'], 'contactPos':contact,
                                     'apexPos':list(mover['pos']), 'riseCells':rose, 'resumeForcedFall':True}
            self.event('BounceApexReached',id=mover['id'],pos=list(mover['pos']))
        else:
            self.event('BounceBlocked',id=mover['id'],surfaceId=surface['id'])
        self.forced_fall.discard(mover['id'])

    def land(self, mover, allow_press=False):
        if mover['id'] in self.apex:
            return
        descended = False
        for _ in range(128):
            if mover['id'] not in self.forced_fall and self.gravity(mover) != 'Down':
                return
            dest = add(tuple(mover['pos']),DOWN)
            hit = self.occupant(dest,ignore=(mover['id'],),mover=mover)
            if hit is None:
                self.shift(mover,DOWN,'PlayerFell' if 'YOU' in self.props(mover) else 'GravityFall')
                descended = True
                continue
            if isinstance(hit,dict) and not self.solid(hit):
                raise ValueError('Unsupported solid-to-hollow gravity contact in witness subset')
            if descended and isinstance(hit,dict) and 'BOUNCY' in self.props(hit):
                self.bounce(mover,hit)
                return
            if (descended and allow_press and mover['id'] not in self.pressed and self.movable(hit)
                    and self.free(add(tuple(hit['pos']),DOWN),mover=hit)):
                self.commit([(hit,add(tuple(hit['pos']),DOWN)),(mover,dest)],'LandingPress')
                self.pressed.add(mover['id'])
            self.forced_fall.discard(mover['id'])
            return
        raise AssertionError('Landing microstep cap')

    def _settle(self):
        seen = set()
        for _ in range(128):
            key = self.fingerprint()+repr(sorted(self.pressed))+repr(sorted(self.transformed_this_turn))
            if key in seen:
                raise AssertionError('Automatic-resolution cycle')
            seen.add(key); before = key
            for e in sorted(self.entities,key=lambda v:(-v['pos'][1],v['id'])):
                if (e['kind']!='OBJECT' or 'YOU' in self.props(e) or e['id'] in self.apex
                        or e['id'] in self.forced_fall or self.gravity(e) != 'Up'):
                    continue
                group = [e]
                player = self.actor()
                if (self.solid(e) and player is not None
                        and player['id'] not in self.apex
                        and player['id'] not in self.forced_fall
                        and tuple(player['pos'])==add(tuple(e['pos']),UP)):
                    group.append(player)
                ids = tuple(v['id'] for v in group)
                if all(self.free(add(tuple(v['pos']),UP),ignore=ids,mover=v) for v in group):
                    self.commit([(v,add(tuple(v['pos']),UP)) for v in group],'FlyOrRide')
            for e in sorted(self.entities,key=lambda v:(v['pos'][1],v['id'])):
                if e['id'] in self.apex:
                    continue
                forced = e['id'] in self.forced_fall
                natural = self.gravity(e) == 'Down'
                if forced or natural:
                    self.land(e,allow_press=('YOU' in self.props(e)))
            if before == self.fingerprint()+repr(sorted(self.pressed))+repr(sorted(self.transformed_this_turn)):
                return
        raise AssertionError('Resolution microstep cap')

    def settle(self):
        """Public fixture helper, not a hidden Unity update tick."""
        if self.won_latched:
            return
        try:
            self.refresh(); self.validate_state(); self.check_win('RulesResolved'); self._settle()
        except _VictoryCommitted:
            pass

    def won(self):
        return self.won_latched

    def phase(self):
        return 'Won' if self.won_latched else ('NoControl' if self.actor() is None else ('BounceApex' if self.actor_id() in self.apex else 'Grounded'))

    def snapshot(self):
        return {'entities':copy.deepcopy(sorted(self.entities,key=lambda e:e['id'])),'apex':copy.deepcopy(self.apex),
                'forcedFall':sorted(self.forced_fall),'won':self.won_latched,'winRecord':copy.deepcopy(self.win_record)}

    def restore(self, snapshot):
        self.entities = copy.deepcopy(snapshot['entities'])
        self.apex = copy.deepcopy(snapshot['apex'])
        self.forced_fall = set(snapshot['forcedFall'])
        self.won_latched = snapshot['won']; self.win_record = copy.deepcopy(snapshot['winRecord'])
        self.pressed.clear(); self.transformed_this_turn.clear(); self.refresh(); self.validate_state()

    def fingerprint(self):
        return json.dumps(self.snapshot(),sort_keys=True)

    def undo(self):
        if not self.history:
            return False
        self.restore(self.history.pop()); return True

    def command(self, cmd):
        if cmd in ('CAM+','CAM-'):
            self.camera = (self.camera+(1 if cmd=='CAM+' else -1))%4
            return True
        if cmd in LEGACY_JUMPS:
            return False
        if cmd not in COMMANDS:
            raise ValueError(f'Unknown command: {cmd}')
        if self.won_latched:
            return False
        self.transformed_this_turn.clear()
        snap = self.snapshot(); oldlog = len(self.log)
        player = self.actor()
        if player is None:
            return False
        actor_id = player['id']; had_apex = actor_id in self.apex
        self.forced_fall.update(self.apex); self.apex.clear(); self.pressed.clear()
        success = False
        try:
            if had_apex:
                if cmd in ('WAIT','J'):
                    self.event('ApexReleased',id=actor_id,steer=None); success=True
                elif cmd in DIRECTIONS:
                    d=DIRECTIONS[cmd]
                    if self.free(add(tuple(player['pos']),d),ignore=(actor_id,),mover=player):
                        self.shift(player,d,'ApexSteer'); success=True
            elif cmd == 'J':
                up=add(tuple(player['pos']),UP); hit=self.occupant(up,mover=player)
                if hit is None:
                    self.shift(player,UP,'JumpApex'); success=True
                elif self.movable(hit) and self.free(add(up,UP),mover=hit):
                    self.commit([(hit,add(up,UP)),(player,up)],'HeadBump'); success=True
            elif cmd in DIRECTIONS or cmd in PUSH_COMMANDS:
                pushing=cmd in PUSH_COMMANDS
                d=DIRECTIONS[cmd[1:] if pushing else cmd]
                dest=add(tuple(player['pos']),d)
                hit=self.occupant(dest,mover=player)
                if hit is None:
                    self.shift(player,d,'PushModeWalk' if pushing else 'Walk'); success=True
                elif pushing:
                    chain=self.plan_push(hit,d)
                    if chain is not None:
                        self.commit([(e,add(tuple(e['pos']),d)) for e in chain]+[(player,dest)],'Pushed')
                        success=True
                elif (self.free(add(tuple(player['pos']),UP),ignore=(actor_id,),mover=player)
                      and self.free(add(dest,UP),ignore=(actor_id,),mover=player)):
                    # Climb is one atomic action. It is not downward contact or a free air jump.
                    self.commit([(player,add(dest,UP))],'Climbed'); success=True
            if not success:
                self.restore(snap); del self.log[oldlog:]; return False
            self.resolve_rules('AfterManualPhase')
            current_actor = self.actor()
            if current_actor is not None:
                self.land(current_actor,allow_press=True)
            self._settle()
        except _VictoryCommitted:
            success=True  # Valid terminal microstep: retain it; do not roll back or finish gravity.
        except RuleConflict as exc:
            self.restore(snap); del self.log[oldlog:]
            self.last_rejection = str(exc)
            return False
        except Exception:
            self.restore(snap); del self.log[oldlog:]; raise
        self.history.append(snap)
        return success



def read_level_specs(root=ROOT):
    """Read the current strict schema. No silent migration of earlier contracts."""
    catalog=json.loads((root/'catalog.json').read_text(encoding='utf-8'))
    if catalog.get('schemaVersion')!=9 or catalog.get('mechanicsVersion')!='RW-v0.9':
        raise ValueError('Unsupported catalog schema/mechanics')
    def xyz(c): return [c['x'],c['y'],c['z']]
    def extent(b):
        a,b=b['min'],b['max'];return [a['x'],b['x'],a['y'],b['y'],a['z'],b['z']]
    result=[]
    for relative in catalog['levels']:
        path=(root/relative).resolve()
        if root.resolve() not in path.parents: raise ValueError('Unsafe level path')
        raw=json.loads(path.read_text(encoding='utf-8'))
        if raw.get('schemaVersion')!=9 or raw.get('mechanicsVersion')!='RW-v0.9':
            raise ValueError('Explicit redesign required; old data is not v0.9')
        forbidden = {'fixedRules','baseRules','ruleOverrides','precomputedRules','activeRules'} & set(raw)
        if forbidden:
            raise ValueError('Forbidden rule fields: '+repr(sorted(forbidden)))
        spec=dict(id=raw['id'],title=raw['title'],bounds=extent(raw['bounds']),
            solidBoxes=[extent(b) for b in raw['terrain']],
            entities=[dict(id=e['id'],kind=e['kind'].upper(),subject=e.get('subject',''),
                token=e.get('token',''),pos=xyz(e['cell']),anchored=e['anchored']) for e in raw['entities']],
            **raw['options'])
        expected=dict(actionMode='MoveClimbHoldPush',winMode='DistinctEntitiesSameCell',
            gravityMode='WorldDownExceptHoverOrFly',collisionMode='SolidPairsTerrainUniversal',
            solidityMode='YouPushStopOrText',supportMode='StrictBelow',
            controlMode='SingleYouTransfer_NoControlUndo',bounceRiseCells=3,
            transformationMode='PermanentSingleTarget_SimultaneousOncePerEntityPerCommand',
            ruleSourceMode='WorldTextOnly',textMobilityMode='AllWordsMovable_GeometryAccess')
        if any(spec.get(k)!=v for k,v in expected.items()): raise ValueError('Unknown mechanics option')
        for e in spec['entities']:
            if e['kind'] not in ('OBJECT','TEXT'): raise ValueError('Unknown entity kind')
            if e['kind']=='OBJECT' and (e['subject'] not in SUBJECTS or e['anchored']):
                raise ValueError('Unknown subject or hidden object anchoring')
            if e['kind']=='TEXT' and e['token'] not in SUBJECTS|PROPS|{'IS','AND'}:
                raise ValueError('Unknown token')
        if any(e['anchored'] for e in spec['entities']):
            raise ValueError('Authored v0.9 entities must be dynamic; use actual terrain to restrict access')
        if not any(e['kind']=='TEXT' for e in spec['entities']):
            raise ValueError('No world words in authored level')
        if len(raw['referenceSolutions']) < raw['designContract']['minimumSolutionFamilies']:
            raise ValueError('Missing authored witness family')
        for route in raw['referenceSolutions']:
            if any(c not in COMMANDS for c in route['commands']): raise ValueError('Unknown command')
        result.append((raw,spec,path))
    return result


def active_interaction(events):
    # A design audit, deliberately not queried by Model.check_win.
    return any(e['event'] in ('ActiveInteraction','RulesChanged') for e in events)


def audit_no_interaction(spec, cap=20000):
    """Exhaust the ACTUAL finite model with interactive edges removed.

    All ten commands are tried: normal walk/climb, push-intent walk, in-place
    jump/head bump, apex steer/release. A head bump or landing press that moves
    an object is filtered even if the input was J or an ordinary direction.
    No event counter is added to the game state or victory condition.
    """
    from collections import deque
    model=Model(spec)
    queue=deque([(model.snapshot(),[])])
    seen={model.fingerprint()}
    attempts=allowed=filtered=rejected=0
    while queue:
        snap,path=queue.popleft()
        for command in COMMANDS:
            model.restore(snap);model.history.clear();model.log.clear()
            attempts+=1
            if not model.command(command):
                rejected+=1;continue
            if active_interaction(model.log):
                filtered+=1;continue
            allowed+=1
            if model.won():
                return dict(status='TRAVERSAL_WIN_FOUND',path=path+[command],
                            states=len(seen),attempts=attempts)
            key=model.fingerprint()
            if key not in seen:
                if len(seen)>=cap:
                    return dict(status='INCONCLUSIVE_LIMIT',states=len(seen),attempts=attempts,cap=cap)
                seen.add(key);queue.append((model.snapshot(),path+[command]))
    return dict(status='EXHAUSTED_NO_INTERACTION_WIN',states=len(seen),attempts=attempts,
        allowedTransitions=allowed,filteredInteractiveTransitions=filtered,rejectedCommands=rejected)



def inspect_world_sources(model):
    """Audit live parser facts against their real TEXT IDs; never supplies a rule."""
    texts={e['id']:e for e in model.entities if e['kind']=='TEXT'}
    facts=[]
    def check_ids(ids):
        assert isinstance(ids,list) and len(ids)>=3
        assert len(ids)==len(set(ids)) and all(i in texts for i in ids)
        positions=[tuple(texts[i]['pos']) for i in ids]
        direction=tuple(b-a for a,b in zip(positions[0],positions[1]))
        assert direction in ((1,0,0),(0,0,1))
        assert all(add(a,direction)==b for a,b in zip(positions,positions[1:]))
        return [texts[i]['token'] for i in ids]
    for source in model.property_sources:
        tokens=check_ids(source['textIds'])
        assert source['property'] in model.rules[source['subject']]
        facts.append(dict(kind='Property',subject=source['subject'],result=source['property'],
                          textIds=source['textIds'],tokens=tokens))
    for source in model.transform_sources:
        tokens=check_ids(source['origin'])
        assert model.transforms[source['source']]==source['target']
        facts.append(dict(kind='Transformation',subject=source['source'],result=source['target'],
                          textIds=source['origin'],tokens=tokens))
    parsed_props={(s['subject'],s['result']) for s in facts if s['kind']=='Property'}
    wanted={(subject,p) for subject,ps in model.rules.items() for p in ps}
    assert parsed_props==wanted, ('property without world source',wanted-parsed_props)
    assert {(s['subject'],s['result']) for s in facts if s['kind']=='Transformation'}==set(model.transforms.items())
    return facts


def run_source_and_mechanic_checks(loaded):
    """Small current-version regressions, all using real spatial word data.

    Editor-like fixture changes below are explicitly test setup; they are not
    game actions and never bypass the Text-only parser.
    """
    results=[]
    cases={raw['id']:(raw,spec) for raw,spec,_ in loaded}
    def check(name,fn):
        fn();results.append(dict(name=name,passed=True))
    def fresh(i):return Model(cases[i][1])
    def play(i,s='A'):
        raw,spec=cases[i];m=Model(spec)
        route=next(r for r in raw['referenceSolutions'] if r['id']==s)
        for c in route['commands']:assert m.command(c)
        return m
    for raw,spec,_ in loaded:
        def per_level(raw=raw,spec=spec):
            assert not any(k in raw for k in ('fixedRules','baseRules','ruleOverrides','activeRules'))
            assert all(not e['anchored'] for e in raw['entities'])
            m=Model(spec);facts=inspect_world_sources(m);assert facts
            assert len([e for e in m.entities if e['kind']=='TEXT'])>=3
            main_z=raw['presentation']['mainAreaBounds']['min']['z']
            assert any(e['kind']=='TEXT' and e['pos'][2]>=main_z for e in m.entities)
            words=[e for e in m.entities if e['kind']=='TEXT']
            assert all(add(tuple(e['pos']),DOWN) in m.terrain or any(
                other['id']!=e['id'] and tuple(other['pos'])==add(tuple(e['pos']),DOWN) and m.solid(other)
                for other in m.entities) for e in words), 'All authored words have real terrain or solid-object support'
        check(raw['id']+' schema, world sources, movable words and real support',per_level)
    def reject_injection():
        spec=copy.deepcopy(cases['L01'][1]);spec['fixedRules']=['ROBOT IS YOU']
        try:Model(spec)
        except ValueError as e:assert 'non-spatial' in str(e)
        else:raise AssertionError('non-spatial rule accepted')
    check('Non-spatial fixedRules injection is rejected, not combined or silently ignored',reject_injection)
    def delete_all():
        for _,spec,_ in loaded:
            m=Model(spec);m.entities=[e for e in m.entities if e['kind']!='TEXT'];m.refresh()
            assert not any(m.rules.values()) and not m.transforms and m.actor() is None
    check('Deleting all world words removes all explicit properties and transformations',delete_all)
    def token_not_id():
        m=fresh('L01');m.entity('w_win')['token']='PUSH';assert m.command('PN')
        assert 'PUSH' in m.rules['FLAG'] and 'WIN' not in m.rules['FLAG']
    check('Displayed token and parsed token share data; stable ID w_win does not force WIN',token_not_id)
    def l1_active():
        m=fresh('L01');assert 'WIN' not in m.rules['FLAG'];assert m.command('PN')
        assert 'WIN' in m.rules['FLAG'] and not m.won()
        assert any(e['event']=='ActiveInteraction' and e['entityKind']=='TEXT' for e in m.log)
    check('Level 1 requires a real word push to activate FLAG WIN',l1_active)
    def l2_active():
        m=fresh('L02');assert not m.solid(m.entity('rock')) and not m.movable(m.entity('rock'))
        assert m.command('PN');assert m.solid(m.entity('rock')) and m.movable(m.entity('rock'))
    check('Level 2 PUSH changes actual rock solidity and manual displacement permission',l2_active)
    def replaces_no_backup():
        m=fresh('L03');assert m.rules['ROCK']=={'STOP'};assert m.command('PN')
        assert m.rules['ROCK']=={'PUSH'}
    check('Replacing STOP with PUSH removes STOP; no hidden fallback sentence survives',replaces_no_backup)
    def source_can_move():
        spec=copy.deepcopy(cases['L01'][1])
        next(e for e in spec['entities'] if e['id']=='robot_01')['pos']=[1,1,-4]
        m=Model(spec);f=m.fingerprint();assert m.command('PN')
        assert m.actor() is None and m.phase()=='NoControl'
        assert m.undo() and m.fingerprint()==f
    check('Protected YOU source is ordinary movable text; reaching it in a test fixture allows breaking control',source_can_move)
    def geometry_is_real():
        spec=copy.deepcopy(cases['L01'][1]);next(e for e in spec['entities'] if e['id']=='robot_01')['pos']=[1,1,0]
        m=Model(spec);f=m.fingerprint()
        assert not m.command('S') and not m.command('PS') and m.fingerprint()==f
        assert (1,1,-1) in m.terrain and (1,5,-1) in m.terrain
    check('The source-area barrier is actual Terrain, not a cannot-push flag or UI exclusion zone',geometry_is_real)
    def noun_and_is_movable():
        m=fresh('L03')
        assert m.movable(m.entity('w_rock')) and m.movable(m.entity('w_is'))
    check('Main-puzzle noun and IS words are not anchored as attribute-only slots',noun_and_is_movable)
    def mode_distinction():
        # Dedicated predictable fixture from L02 after rule activation.
        m=fresh('L02');assert m.command('PN') and m.command('S');r=m.entity('rock')['pos'].copy()
        assert m.command('E') and m.entity('rock')['pos']==r and m.actor()['pos']==[3,2,0]
        assert m.undo();assert m.command('PE') and m.entity('rock')['pos']==[4,1,0]
    check('Ordinary movement climbs a PUSH rock; push-intent moves it instead',mode_distinction)
    def stop_no_push():
        m=fresh('L03');next(e for e in m.entities if e['id']=='robot_01')['pos']=[4,2,1];m.refresh()
        f=m.fingerprint();assert not m.command('PE') and m.fingerprint()==f
        assert m.command('E') and m.entity('rock')['pos']==[5,2,1]
    check('STOP without PUSH blocks manual displacement but remains climbable',stop_no_push)
    def no_self_win():
        m=fresh('L09')
        for c in ['N','E','N','PW']:assert m.command(c)
        assert {'YOU','WIN'}<=m.props(m.actor()) and not m.won()
    check('YOU and WIN on one identity never cause self-victory',no_self_win)
    def conversion():
        m=play('L08','B');assert m.entity('rock')['subject']=='FLAG'
        assert m.win_record['winId']=='rock' and m.entity('rock')['id']=='rock'
        assert not m.solid(m.entity('rock'))
    check('World noun words perform ROCK to FLAG conversion and recompute target solidity',conversion)
    def transformed_body():
        m=play('L12','C');assert m.actor_id()=='spring';assert m.entity('robot_01')['subject']=='FLAG'
        assert m.win_record['youId']=='spring' and m.win_record['winId']=='robot_01'
    check('World IS transfer plus ROBOT IS FLAG preserves the distinct-body victory path',transformed_body)
    def bounce_and_lift():
        a=play('L11','A');b=play('L11','B')
        assert any(e['event']=='LandingBounce' for e in a.log)
        assert not any(e['event']=='FlyOrRide' for e in a.log)
        assert any(e['event']=='FlyOrRide' for e in b.log)
        assert not any(e['event']=='LandingBounce' for e in b.log)
    check('Same L11 initial data retains bounce and lift as different solutions',bounce_and_lift)
    def camera():
        m=fresh('L01');f=m.fingerprint();facts=inspect_world_sources(m)
        for _ in range(4):assert m.command('CAM+')
        assert f==m.fingerprint() and facts==inspect_world_sources(m) and not m.history
    check('Four-view observation never changes rules or advances world time',camera)
    def old_jump():
        m=fresh('L01');f=m.fingerprint()
        for c in LEGACY_JUMPS:assert not m.command(c)
        assert m.fingerprint()==f
    check('Adding visible source words does not restore directional jumps',old_jump)
    def gravity_word():
        m=fresh('L01');m.entity('w_win')['pos']=[2,3,1];m.refresh();m.settle()
        assert m.entity('w_win')['pos']==[2,1,1] and 'WIN' not in m.rules['FLAG']
    check('Ordinary text falls under world-default gravity and is not implicitly hovering',gravity_word)
    def no_player_fallback():
        m=fresh('L01');e=m.entity('src_01_03_you');e['token']='WIN';m.refresh()
        assert m.actor() is None and not m.rules['ROBOT']&{'YOU'}
    check('Removing the visible YOU word never silently leaves a robot controller active',no_player_fallback)
    def side_no_win():
        m=fresh('L01');assert m.command('PN') and m.command('S') and m.command('E');assert not m.won()
        assert m.command('E') and m.won()
    check('Face adjacency to the hollow target still does not win',side_no_win)
    def positives_for_audit():
        spec=copy.deepcopy(cases['L01'][1])
        # Author a complete WIN sentence before play as an intentional negative sample.
        next(e for e in spec['entities'] if e['id']=='w_win')['pos']=[2,1,2]
        audit=audit_no_interaction(spec)
        assert audit['status']=='TRAVERSAL_WIN_FOUND'
    check('No-interaction search detects a deliberately authored traversal-only counterexample',positives_for_audit)
    def cap_not_proof():
        assert audit_no_interaction(cases['L02'][1],cap=1)['status']=='INCONCLUSIVE_LIMIT'
    check('Search limits are inconclusive, not no-bypass proofs',cap_not_proof)
    return results


def main():
    import random,hashlib
    from collections import Counter
    loaded=read_level_specs()
    assert [r['id'] for r,_,_ in loaded]==[f'L{i:02}' for i in range(1,13)]
    checks=run_source_and_mechanic_checks(loaded)
    reports=[]
    for raw,spec,path in loaded:
        m=Model(spec);assert not m.pending_transforms(), ('initial-conversion',raw['id'])
        initial=m.fingerprint();m.settle()
        assert m.fingerprint()==initial and not m.won(),('unstable/initial-win',raw['id'])
        initial_sources=inspect_world_sources(m)
        route_reports=[]
        for route in raw['referenceSolutions']:
            m=Model(spec);assert m.fingerprint()==initial
            rows=[];first_interaction=None;first_word=None
            for step,c in enumerate(route['commands'],1):
                start=len(m.log)
                assert m.command(c),(raw['id'],route['id'],step,c,getattr(m,'last_rejection','rejected'))
                assert m.won()==(step==len(route['commands'])),('premature/missing win',raw['id'],route['id'],step)
                delta=m.log[start:];inspect_world_sources(m)
                if first_interaction is None and active_interaction(delta):first_interaction=step
                if first_word is None and any(e['event']=='ActiveInteraction' and e['entityKind']=='TEXT' for e in delta):first_word=step
                actor=m.actor()
                rows.append(dict(step=step,command=c,actorId=m.actor_id(),actorCell=actor['pos'].copy() if actor else None,
                    phase=m.phase(),events=sorted({e['event'] for e in delta})))
            kinds={e['event'] for e in m.log}
            assert first_interaction is not None
            if raw['designContract']['requireWordInteractionOnWitness']:assert first_word is not None
            for key,w in [('mustWinWith','winId'),('mustControlAtWin','youId')]:
                if key in route:assert m.win_record[w]==route[key],(raw['id'],route['id'],key,m.win_record)
            assert all(x in kinds for x in route.get('requireEvents',[]))
            assert not any(x in kinds for x in route.get('forbidEvents',[]))
            final=m.fingerprint()
            signature=dict(youId=m.win_record['youId'],winId=m.win_record['winId'],winCell=m.win_record['cell'],
                usedBounce='LandingBounce' in kinds,usedLift='FlyOrRide' in kinds,transferredControl='ControlChanged' in kinds,usedTransformation='Transformed' in kinds)
            rr=dict(id=route['id'],name=route['name'],family=route['family'],passed=True,
                commandCount=len(route['commands']),firstInteractionStep=first_interaction,firstWordInteractionStep=first_word,
                win=m.win_record,mechanismSignature=signature,eventCounts=dict(Counter(e['event'] for e in m.log)),
                interactions=[e for e in m.log if e['event']=='ActiveInteraction'],
                controlChanges=[e for e in m.log if e['event']=='ControlChanged'],
                transformations=[e for e in m.log if e['event']=='Transformed'],trace=rows,
                finalSubjects={e['id']:e.get('subject','') for e in m.entities if e['kind']=='OBJECT'},
                finalRuleSources=inspect_world_sources(m))
            for _ in route['commands']:assert m.undo()
            assert m.fingerprint()==initial and not m.history
            for seed in range(4):
                variant=copy.deepcopy(spec);random.Random(seed).shuffle(variant['entities']);mm=Model(variant)
                for c in route['commands']:assert mm.command(c)
                assert mm.fingerprint()==final,('entity insertion order',raw['id'],route['id'],seed)
            route.update(status='ReferenceModelPassed_NotUnityVerified',expectedTrace=rows,expectedWin=rr['win'])
            route_reports.append(rr)
        assert any(r['firstWordInteractionStep'] is not None for r in route_reports), ('no demonstrated reachable word',raw['id'])
        signatures={json.dumps(r['mechanismSignature'],sort_keys=True) for r in route_reports}
        assert len(signatures)>=raw['designContract']['minimumSolutionFamilies']
        audit=audit_no_interaction(spec)
        assert audit['status']=='EXHAUSTED_NO_INTERACTION_WIN',(raw['id'],audit)
        path.write_text(json.dumps(raw,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        main_z=raw['presentation']['mainAreaBounds']['min']['z']
        reports.append(dict(id=raw['id'],title=raw['title'],stableInitial=True,
            sameInitialForAllRoutes=True,undoFullRestore=True,entityOrderSeeds=[0,1,2,3],
            requiredInteraction=True,noInteractionAudit=audit,worldTextOnly=True,
            initialRuleSources=initial_sources,
            wordCount=sum(e['kind']=='TEXT' for e in spec['entities']),
            mainAreaWordCount=sum(e['kind']=='TEXT' and e['pos'][2]>=main_z for e in spec['entities']),
            anchoredWordCount=0,distinctWitnessSignatures=len(signatures),routes=route_reports,
            dataSHA256=hashlib.sha256(path.read_bytes()).hexdigest()))
        print(raw['id'],f'{len(route_reports)} route(s)',audit['status'],f"{audit['states']} states",flush=True)
    multi=sum(r['distinctWitnessSignatures']>=2 for r in reports);total_routes=sum(len(r['routes']) for r in reports)
    assert multi>=4
    output=dict(mechanicsVersion='RW-v0.9',levelCount=12,witnessRouteCount=total_routes,
        multiSolutionLevelCount=multi,minimumRequiredMultiSolutionLevelCount=4,
        finiteRegressionCount=len(checks),allFiniteRegressionsPassed=True,
        worldWordCount=sum(r['wordCount'] for r in reports),
        mainAreaWordCount=sum(r['mainAreaWordCount'] for r in reports),
        noInteractionSearchStates=sum(r['noInteractionAudit']['states'] for r in reports),
        noInteractionCommandAttempts=sum(r['noInteractionAudit']['attempts'] for r in reports),
        interactionAuditPassedLevels=[r['id'] for r in reports],
        validationScope='Current finite Python graybox model; 12 no-interaction subgraphs exhausted; live word-source auditing. NOT Unity/visual/editor/input/playtest verification or all-solutions enumeration.',
        unsupportedInReference=['Void removal/death','multiple simultaneous YOU','direct YOU+HOVER/FLY inputs','Unity visuals/input/editor','one-to-many noun copying/mixed RHS'],
        checks=checks,levels=reports)
    (ROOT/'validation_results.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'PASS: {total_routes} paths; {multi} multi-solution levels; {len(checks)} checks; all 12 no-interaction searches exhausted.')


if __name__=='__main__':
    if not __debug__:
        raise SystemExit('Run without -O: regression assertions must stay enabled.')
    main()
