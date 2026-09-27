"""Rule Workshop v0.8 finite design-reference checker with noun transformations.
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
    """RW-v0.8 finite graybox witness model, not a complete Unity implementation.

    One YOU; implicit solid; default gravity. PUSH and STOP both imply solid.
    Dynamic collisions require both solids. Only terrain blocks all entities.
    Supported test layouts have a physical bottom; void death is not modeled.
    """
    def __init__(self, spec: dict[str, Any]):
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
                        sources.append(dict(source=subject,target=target,origin=source))
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
        for idx,text in enumerate(self.spec['fixedRules']):
            parse(text.split(), 'fixed:'+str(idx))
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



def run_checks():
    """Finite rule/motion regression fixtures; not Unity/editor/input tests."""
    results=[]
    def check(name,fn):
        fn();results.append({'name':name,'passed':True})
    def ob(eid,s,p): return dict(id=eid,kind='OBJECT',subject=s,token='',pos=list(p),anchored=False)
    def tx(eid,t,p,a=False): return dict(id=eid,kind='TEXT',subject='',token=t,pos=list(p),anchored=a)
    def fixture(player=(0,1,0),extras=(),rules=(),boxes=()):
        return dict(id='fixture',bounds=[-4,9,0,12,-4,6],solidBoxes=[[-4,9,0,0,-4,6],*boxes],
                    entities=[ob('player','ROBOT',player),*copy.deepcopy(list(extras))],
                    fixedRules=['ROBOT IS YOU',*rules],bounceRiseCells=3)
    def expect_unchanged(m,cmd):
        before=m.fingerprint();n=len(m.history)
        assert not m.command(cmd)
        assert m.fingerprint()==before and len(m.history)==n
    def defaults():
        m=Model(fixture());p=m.entity('player')
        assert m.props(p)=={'YOU'} and m.solid(p) and m.gravity(p)=='Down' and not m.movable(p)
    check('YOU gives player solidity, not PUSH or visible FALL',defaults)
    def material_table():
        for rules,solid,push in [([],False,False),(['ROCK IS STOP'],True,False),
                (['ROCK IS PUSH'],True,True),(['ROCK IS PUSH AND STOP'],True,True)]:
            m=Model(fixture(extras=[ob('r','ROCK',(1,1,0))],rules=rules));r=m.entity('r')
            assert m.solid(r)==solid and m.movable(r)==push
    check('Neither/STOP/PUSH/both material truth table',material_table)
    def stop_falls():
        m=Model(fixture(extras=[ob('r','ROCK',(2,4,0))],rules=['ROCK IS STOP']));m.settle()
        assert m.entity('r')['pos']==[2,1,0]
    check('STOP does not anchor or cancel gravity',stop_falls)
    def push_falls():
        m=Model(fixture(extras=[ob('r','ROCK',(2,4,0))],rules=['ROCK IS PUSH']));m.settle()
        assert m.entity('r')['pos']==[2,1,0]
    check('PUSH objects have default downward gravity',push_falls)
    def hover():
        m=Model(fixture(extras=[ob('c','CLOUD',(2,4,0))],rules=['CLOUD IS HOVER']));m.settle()
        assert m.entity('c')['pos']==[2,4,0] and not m.solid(m.entity('c'))
    check('HOVER has no implicit solidity',hover)
    def ghost():
        m=Model(fixture(extras=[ob('c','CLOUD',(1,1,0))]));assert m.command('E')
        assert m.entity('player')['pos']==m.entity('c')['pos']==[1,1,0]
    check('Hollow objects can share the player cell',ghost)
    def pair_symmetric():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0)),ob('f','FLAG',(2,1,0))],rules=['ROCK IS PUSH']))
        r,f=m.entity('r'),m.entity('f');assert not m.blocks_pair(r,f) and not m.blocks_pair(f,r)
        assert m.command('PE') and r['pos']==f['pos']==[2,1,0]
    check('Solid/non-solid dynamic overlap is symmetric, not a WIN exception',pair_symmetric)
    def terrain_blocks_ghost():
        m=Model(fixture(extras=[ob('f','FLAG',(2,4,0))],boxes=[[2,2,1,2,0,0]]));m.settle()
        assert m.entity('f')['pos']==[2,3,0]
    check('Terrain supports even non-solid falling objects',terrain_blocks_ghost)
    def move_climbs():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0))],rules=['ROCK IS PUSH']));assert m.command('E')
        assert m.entity('r')['pos']==[1,1,0] and m.entity('player')['pos']==[1,2,0]
        assert not any(e['event']=='Pushed' for e in m.log)
    check('Normal direction climbs a PUSH rock without moving it',move_climbs)
    def explicit_push():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0))],rules=['ROCK IS PUSH']));assert m.command('PE')
        assert m.entity('r')['pos']==[2,1,0] and m.entity('player')['pos']==[1,1,0]
    check('Push command moves object and player as one group',explicit_push)
    def stop_no_fallback():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0))],rules=['ROCK IS STOP']))
        expect_unchanged(m,'PE');assert m.command('E') and m.entity('player')['pos']==[1,2,0]
    check('Failed push of STOP never falls back to climbing',stop_no_fallback)
    def push_empty():
        m=Model(fixture());assert m.command('PE') and m.entity('player')['pos']==[1,1,0]
    check('Push mode on empty ground gives flat movement',push_empty)
    def mixed_chain():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0)),tx('t','AND',(2,1,0))],rules=['ROCK IS PUSH']))
        assert m.command('PE') and m.entity('r')['pos']==[2,1,0] and m.entity('t')['pos']==[3,1,0]
    check('Mixed word/rock PUSH chains use one contract',mixed_chain)
    def chain_blocked():
        m=Model(fixture(extras=[ob('r','ROCK',(1,1,0)),tx('t','AND',(2,1,0))],rules=['ROCK IS PUSH'],boxes=[[3,3,1,1,0,0]]))
        expect_unchanged(m,'PE')
    check('Blocked push chain makes no partial moves',chain_blocked)
    def anchored_word():
        m=Model(fixture(extras=[tx('t','AND',(1,1,0),True)]));expect_unchanged(m,'PE')
        assert m.command('E')
    check('Anchored text is not pushable but is climbable',anchored_word)
    def head_clear():
        m=Model(fixture(boxes=[[1,1,1,1,0,0],[0,0,2,2,0,0]]));expect_unchanged(m,'E')
    check('Climbing checks clearance above the starting body cell',head_clear)
    def tall_obstacle():
        m=Model(fixture(boxes=[[1,1,1,2,0,0]]));expect_unchanged(m,'E')
    check('Two-cell obstacle cannot be climbed from floor height',tall_obstacle)
    def no_cross_gap():
        m=Model(fixture(player=(0,3,0),boxes=[[0,0,1,2,0,0],[2,2,1,2,0,0]]))
        assert m.command('E') and m.entity('player')['pos']==[1,1,0]
    check('Normal movement into a gap falls; it is not a directional jump',no_cross_gap)
    def old_jump():
        m=Model(fixture())
        for cmd in LEGACY_JUMPS:expect_unchanged(m,cmd)
        assert m.command('J') and m.entity('player')['pos']==[0,1,0]
    check('Directional jumps absent; in-place jump returns in the same column',old_jump)
    def words_fall():
        m=Model(fixture(extras=[tx('t','AND',(2,4,0))]));m.settle()
        assert m.entity('t')['pos']==[2,1,0]
    check('Movable words use world gravity',words_fall)
    def top_push():
        m=Model(fixture(extras=[ob('r','ROCK',(0,2,0))],rules=['ROCK IS PUSH AND HOVER']))
        assert m.command('J') and m.entity('r')['pos']==[0,3,0]
    check('Head bump can raise a PUSH HOVER object',top_push)
    def top_stop():
        m=Model(fixture(extras=[ob('r','ROCK',(0,2,0))],rules=['ROCK IS STOP AND HOVER']))
        expect_unchanged(m,'J')
    check('STOP without PUSH cannot be displaced by a head bump',top_stop)
    def press():
        m=Model(fixture(player=(0,5,0),extras=[ob('r','ROCK',(0,2,0))],rules=['ROCK IS PUSH AND HOVER']))
        m.settle();assert m.entity('r')['pos']==[0,1,0]
        assert sum(e['event']=='LandingPress' and e.get('id')=='player' for e in m.log)==1
    check('Landing can press a PUSH HOVER object down once',press)
    def climb_no_bounce():
        m=Model(fixture(extras=[ob('s','SPRING',(1,1,0))],rules=['SPRING IS STOP AND BOUNCY']))
        assert m.command('E') and m.phase()=='Grounded' and m.entity('player')['pos']==[1,2,0]
        assert m.command('J') and m.phase()=='BounceApex' and m.entity('player')['pos']==[1,5,0]
    check('Climbing onto spring is not landing; jump then fall bounces three cells',climb_no_bounce)
    def bouncy_needs_solid():
        m=Model(fixture(extras=[ob('s','SPRING',(1,1,0))],rules=['SPRING IS BOUNCY']))
        assert m.command('E') and m.entity('player')['pos']==[1,1,0] and m.phase()=='Grounded'
    check('BOUNCY alone supplies no collision or support surface',bouncy_needs_solid)
    def bounce_pressure_priority():
        m=Model(fixture(player=(0,5,0),extras=[ob('r','ROCK',(0,2,0))],rules=['ROCK IS PUSH AND HOVER AND BOUNCY']))
        m.settle();assert m.entity('r')['pos']==[0,2,0] and m.entity('player')['pos']==[0,6,0]
        assert not any(e['event']=='LandingPress' for e in m.log)
    check('BOUNCY consumes landing before downward press',bounce_pressure_priority)
    def bounce_ceiling():
        m=Model(fixture(player=(0,2,0),extras=[ob('s','SPRING',(0,1,0))],rules=['SPRING IS STOP AND BOUNCY'],boxes=[[0,0,4,4,0,0]]))
        assert m.command('J') and m.phase()=='BounceApex' and m.entity('player')['pos']==[0,3,0]
        assert next(e for e in m.log if e['event']=='LandingBounce')['actualRise']==1
    check('Bounce checks each cell and truncates below a ceiling',bounce_ceiling)
    def apex_restrictions():
        m=Model(fixture(player=(0,2,0),extras=[ob('s','SPRING',(0,1,0)),ob('r','ROCK',(1,5,0))],
            rules=['SPRING IS STOP AND BOUNCY','ROCK IS PUSH AND HOVER']))
        assert m.command('J');expect_unchanged(m,'PE');expect_unchanged(m,'E')
        assert m.command('N') and m.entity('player')['pos']==[0,1,1] and m.phase()=='Grounded'
    check('Apex permits one clear side-step, never push or climb; rejection preserves apex',apex_restrictions)
    def neighbors():
        for d in [*DIRECTIONS.values(),UP,DOWN,(1,1,1)]:
            m=Model(fixture(player=(0,4,0),extras=[ob('g','FLAG',add((0,4,0),d))],rules=['FLAG IS WIN']))
            assert not m.won()
    check('Six face neighbors and diagonal do not win',neighbors)
    def win_enter():
        m=Model(fixture(extras=[ob('g','FLAG',(1,1,0))],rules=['FLAG IS WIN']));before=m.fingerprint()
        assert m.command('E') and m.won() and m.undo() and m.fingerprint()==before
    check('Enter hollow WIN; undo restores the entire winning command',win_enter)
    def win_fall():
        m=Model(fixture(extras=[ob('g','FLAG',(0,4,0))],rules=['FLAG IS WIN']));m.settle()
        assert m.won() and m.entity('g')['pos']==[0,1,0]
    check('Falling WIN can enter YOU and stops on the winning cell',win_fall)
    def material_win():
        m=Model(fixture(extras=[ob('g','FLAG',(1,1,0))],rules=['FLAG IS WIN AND PUSH']))
        assert m.command('E') and not m.won() and m.entity('player')['pos']==[1,2,0]
    check('WIN never overrides solidity supplied by PUSH',material_win)
    def same_identity():
        m=Model(fixture(rules=['ROBOT IS WIN']));assert not m.won()
    check('YOU WIN on one entity does not win against itself',same_identity)
    def grammar():
        es=[tx('a','ROCK',(0,2,2),True),tx('b','IS',(1,2,2),True),tx('c','PUSH',(2,2,2),True),
            tx('d','AND',(3,2,2),True)]
        m=Model(fixture(extras=es));assert m.rules['ROCK']=={'PUSH'}
        m.entities.append(tx('e','BOUNCY',(4,2,2),True));m.refresh();assert m.rules['ROCK']=={'PUSH','BOUNCY'}
    check('AND keeps a valid prefix and combines properties',grammar)
    def axes():
        for d,expected in [((1,0,0),True),((0,0,1),True),((-1,0,0),False),((0,1,0),False),((1,1,0),False)]:
            es=[tx(str(i),t,tuple(2+i*d[j] for j in range(3)),True) for i,t in enumerate(['ROCK','IS','PUSH'])]
            m=Model(fixture(extras=es));assert ('PUSH' in m.rules['ROCK'])==expected
    check('Only same-height +X and +Z sentences parse',axes)
    def grav_modes():
        m=Model(fixture(extras=[ob('c','CLOUD',(2,4,0))],rules=['CLOUD IS HOVER AND FLY']))
        assert m.gravity(m.entity('c'))=='Up'
        m.spec['fixedRules']=['ROBOT IS YOU','CLOUD IS HOVER'];m.refresh();assert m.gravity(m.entity('c'))=='Hover'
        m.spec['fixedRules']=['ROBOT IS YOU'];m.refresh();assert m.gravity(m.entity('c'))=='Down'
    check('Gravity resolves FLY > HOVER > default Down without stale properties',grav_modes)
    def ride():
        m=Model(fixture(player=(0,2,0),extras=[ob('c','CLOUD',(0,1,0))],rules=['CLOUD IS STOP AND FLY'],boxes=[[0,0,6,6,0,0]]))
        m.settle();assert m.entity('player')['pos']==[0,5,0] and m.entity('c')['pos']==[0,4,0]
    check('STOP FLY can carry player and stops the group below the ceiling',ride)
    def camera():
        m=Model(fixture());before=m.fingerprint()
        for _ in range(4):assert m.command('CAM+')
        assert m.fingerprint()==before and m.camera==0 and not m.history
    check('Camera rotation changes no world state or undo history',camera)
    return results


def read_level_specs(root=ROOT):
    """Read the current strict schema. No silent migration of earlier contracts."""
    catalog=json.loads((root/'catalog.json').read_text(encoding='utf-8'))
    if catalog.get('schemaVersion')!=8 or catalog.get('mechanicsVersion')!='RW-v0.8':
        raise ValueError('Unsupported catalog schema/mechanics')
    def xyz(c): return [c['x'],c['y'],c['z']]
    def extent(b):
        a,b=b['min'],b['max'];return [a['x'],b['x'],a['y'],b['y'],a['z'],b['z']]
    result=[]
    for relative in catalog['levels']:
        path=(root/relative).resolve()
        if root.resolve() not in path.parents: raise ValueError('Unsafe level path')
        raw=json.loads(path.read_text(encoding='utf-8'))
        if raw.get('schemaVersion')!=8 or raw.get('mechanicsVersion')!='RW-v0.8':
            raise ValueError('Explicit redesign required; old data is not v0.8')
        spec=dict(id=raw['id'],title=raw['title'],bounds=extent(raw['bounds']),
            solidBoxes=[extent(b) for b in raw['terrain']],
            entities=[dict(id=e['id'],kind=e['kind'].upper(),subject=e.get('subject',''),
                token=e.get('token',''),pos=xyz(e['cell']),anchored=e['anchored']) for e in raw['entities']],
            fixedRules=[' '.join(r['tokens']) for r in raw['fixedRules']],**raw['options'])
        expected=dict(actionMode='MoveClimbHoldPush',winMode='DistinctEntitiesSameCell',
            gravityMode='WorldDownExceptHoverOrFly',collisionMode='SolidPairsTerrainUniversal',
            solidityMode='YouPushStopOrText',supportMode='StrictBelow',
            controlMode='SingleYouTransfer_NoControlUndo',bounceRiseCells=3,
            transformationMode='PermanentSingleTarget_SimultaneousOncePerEntityPerCommand')
        if any(spec.get(k)!=v for k,v in expected.items()): raise ValueError('Unknown mechanics option')
        for e in spec['entities']:
            if e['kind'] not in ('OBJECT','TEXT'): raise ValueError('Unknown entity kind')
            if e['kind']=='OBJECT' and (e['subject'] not in SUBJECTS or e['anchored']):
                raise ValueError('Unknown subject or hidden object anchoring')
            if e['kind']=='TEXT' and e['token'] not in SUBJECTS|PROPS|{'IS','AND'}:
                raise ValueError('Unknown token')
        for line in spec['fixedRules']:
            if any(t not in SUBJECTS|PROPS|{'IS','AND'} for t in line.split()):
                raise ValueError('Unknown fixed-rule token')
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


def run_v07_checks():
    """New local checks for controller transfer and authoring-audit semantics."""
    checks=[]
    def check(name, fn): fn();checks.append(dict(name=name,passed=True))
    def ob(i,s,p):return dict(id=i,kind='OBJECT',subject=s,token='',pos=list(p),anchored=False)
    def tx(i,t,p,a=False):return dict(id=i,kind='TEXT',subject='',token=t,pos=list(p),anchored=a)
    def role_fixture(flag=(5,1,0),robot_stop=False):
        return dict(id='role-fixture',bounds=[-2,7,0,7,-2,5],solidBoxes=[[-2,7,0,0,-2,5]],
            entities=[ob('body_a','ROBOT',(1,1,1)),ob('body_b','FLAG',flag),
                tx('subject_a','ROBOT',(0,1,2),True),tx('connector','IS',(1,1,2)),tx('you_a','YOU',(2,1,2),True),
                tx('subject_b','FLAG',(0,1,3),True),tx('you_b','YOU',(2,1,3),True)],
            fixedRules=['ROBOT IS WIN','FLAG IS WIN']+(['ROBOT IS STOP'] if robot_stop else []),bounceRiseCells=3)
    def transfer():
        m=Model(role_fixture());assert m.actor_id()=='body_a';assert m.command('PN')
        assert m.actor_id()=='body_b' and not m.won()
        assert not m.solid(m.entity('body_a')) and m.solid(m.entity('body_b'))
        assert m.entity('body_a')['subject']=='ROBOT' and m.entity('body_b')['subject']=='FLAG'
        assert m.command('N');assert m.entity('body_b')['pos']==[5,1,1]
    check('IS atomically transfers YOU to another stable ID, without noun transformation',transfer)
    def transfer_undo():
        m=Model(role_fixture());before=m.fingerprint();assert m.command('PN');assert m.undo()
        assert m.fingerprint()==before and m.actor_id()=='body_a'
    check('Undo restores controller, implicit solidity and all word positions',transfer_undo)
    def no_self_win():
        m=Model(role_fixture());assert m.props(m.actor())=={'YOU','WIN'} and not m.won()
    check('YOU+WIN is not same-entity auto-victory',no_self_win)
    def no_control():
        m=Model(role_fixture());m.entity('body_a')['pos']=[1,1,3];m.refresh();before=m.fingerprint()
        assert m.command('PS') and m.actor() is None and m.phase()=='NoControl'
        lost=m.fingerprint();assert not m.command('E') and m.fingerprint()==lost
        assert m.undo() and m.fingerprint()==before
    check('Breaking YOU creates recoverable NoControl, not a hidden fallback robot',no_control)
    def rule_win_atom():
        m=Model(role_fixture(flag=(1,1,2)))
        assert m.command('PN') and m.won()
        assert m.win_record['youId']=='body_b' and m.win_record['winId']=='body_a'
        assert m.win_record['cell']==[1,1,2]
    check('Overlap plus control-rule change can win in the same committed atomic group',rule_win_atom)
    def keep_stop():
        m=Model(role_fixture(robot_stop=True));assert m.command('PN')
        assert m.solid(m.entity('body_a')) and 'STOP' in m.props(m.entity('body_a'))
        a,b=m.entity('body_a'),m.entity('body_b')
        assert m.blocks_pair(a,b) and not m.won()
    check('Losing YOU never silently removes an explicit STOP from the former body',keep_stop)
    def multiple_reject():
        spec=role_fixture();spec['fixedRules'].append('ROBOT IS YOU')
        m=Model(spec);before=m.fingerprint();assert not m.command('PN')
        assert m.fingerprint()==before and not m.history
        assert 'MultipleYOU' in m.last_rejection
    check('Unimplemented multi-YOU combination is explicit rollback, not arbitrary selection',multiple_reject)
    def gravity_control_reject():
        spec=role_fixture();spec['fixedRules'].append('FLAG IS HOVER')
        m=Model(spec);before=m.fingerprint();assert not m.command('PN')
        assert m.fingerprint()==before and 'ControlledGravity' in m.last_rejection
    check('Unsupported direct controlled HOVER/FLY is diagnosed rather than silently mis-simulated',gravity_control_reject)
    def post_transfer_win_and_undo():
        m=Model(role_fixture());assert m.command('PN');n=1
        for c in ['W','W','W','W','N','N']:
            assert m.command(c);n+=1
        assert m.won() and m.win_record['winId']=='body_a'
        assert m.undo() and not m.won() and m.actor_id()=='body_b'
    check('Transferred body can enter former body; winning undo preserves new control until transfer is undone',post_transfer_win_and_undo)
    def diagnostics_not_gate():
        spec=dict(id='no-gate',bounds=[0,2,0,3,0,0],solidBoxes=[[0,2,0,0,0,0]],
            entities=[ob('any_id','ROBOT',(0,1,0)),ob('goal','FLAG',(1,1,0))],
            fixedRules=['ROBOT IS YOU','FLAG IS WIN'],bounceRiseCells=3)
        m=Model(spec);assert m.command('E') and m.won() and not active_interaction(m.log)
    check('Victory never reads interaction metadata or a tutorial-completion counter',diagnostics_not_gate)
    def head_bump_counts():
        spec=dict(id='bump-audit',bounds=[0,3,0,6,0,2],solidBoxes=[[0,3,0,0,0,2]],
          entities=[ob('r','ROBOT',(1,1,1)),ob('stone','ROCK',(1,2,1))],
          fixedRules=['ROBOT IS YOU','ROCK IS PUSH AND HOVER'],bounceRiseCells=3)
        m=Model(spec);assert m.command('J') and active_interaction(m.log)
        assert any(e['event']=='ActiveInteraction' and e['commandEffect']=='HeadBump' for e in m.log)
    check('No-interaction search filters a real head bump, not only P-prefixed commands',head_bump_counts)
    def cap_not_proof():
        _,spec,_=read_level_specs()[1]
        assert audit_no_interaction(spec,cap=1)['status']=='INCONCLUSIVE_LIMIT'
    check('A search limit is inconclusive, never reported as no bypass',cap_not_proof)
    return checks


def run_v08_checks():
    """Local transformation contracts, not a claim about arbitrary level solvability."""
    checks=[]
    def check(name, fn): fn();checks.append(dict(name=name,passed=True))
    def ob(i,s,p=(3,1,0)):return dict(id=i,kind='OBJECT',subject=s,token='',pos=list(p),anchored=False)
    def tx(i,t,p):return dict(id=i,kind='TEXT',subject='',token=t,pos=list(p),anchored=False)
    def fixture(extras=(),rules=(),player=(0,1,0)):
        return dict(id='transform-unit',bounds=[-3,10,0,12,-3,8],solidBoxes=[[-3,10,0,0,-3,8]],
            entities=[ob('body','ROBOT',player),*copy.deepcopy(list(extras))],
            fixedRules=['ROBOT IS YOU',*rules],bounceRiseCells=3)
    def model(extras=(),rules=(),player=(0,1,0)):return Model(fixture(extras,rules,player))
    def basic():
        m=model([ob('r','ROCK')],['ROCK IS PUSH','ROCK IS FLAG','FLAG IS WIN'])
        before=len(m.entities);assert m.command('N');r=m.entity('r')
        assert r['id']=='r' and r['subject']=='FLAG' and r['pos']==[3,1,0]
        assert len(m.entities)==before and m.props(r)=={'WIN'} and not m.solid(r)
    check('Noun conversion preserves ID and cell; applies target properties rather than old PUSH',basic)
    def robot_no_sticky():
        m=model(rules=['ROBOT IS FLAG','FLAG IS WIN']);before=m.fingerprint()
        assert m.command('E') and m.entity('body')['subject']=='FLAG'
        assert m.actor() is None and m.phase()=='NoControl' and not m.won()
        assert not m.command('E');assert m.undo() and m.fingerprint()==before
    check('ROBOT IS FLAG really changes the controlled body; no sticky YOU; NoControl is undoable',robot_no_sticky)
    def keep_by_target():
        m=model(rules=['ROBOT IS FLAG','FLAG IS YOU','FLAG IS WIN'])
        assert m.command('N') and m.actor_id()=='body' and m.entity('body')['subject']=='FLAG'
        assert m.props(m.actor())=={'YOU','WIN'} and not m.won()
    check('Converted body stays controllable only with target YOU; its own YOU+WIN does not win',keep_by_target)
    def all_instances():
        m=model([ob('a','ROCK'),ob('b','ROCK',(4,1,0)),ob('c','CLOUD',(5,1,0))],['ROCK IS FLAG'])
        assert m.command('N');assert [m.entity(i)['subject'] for i in ['a','b','c']]==['FLAG','FLAG','CLOUD']
    check('A global noun sentence affects every matching object, not just the touched instance',all_instances)
    def text_not_object():
        m=model([ob('a','ROCK'),tx('word','ROCK',(7,1,0))],['ROCK IS FLAG']);assert m.command('N')
        assert m.entity('word')['kind']=='TEXT' and m.entity('word')['token']=='ROCK'
    check('A word spelling ROCK is not a ROCK object and never converts with it',text_not_object)
    def persistence():
        m=model([ob('r','ROCK')],['ROCK IS FLAG']);assert m.command('N')
        m.spec['fixedRules'].remove('ROCK IS FLAG');m.refresh();assert m.command('S')
        assert m.entity('r')['subject']=='FLAG'
    check('Removing a transformation sentence does not reverse completed identity changes',persistence)
    def reverse():
        m=model([ob('r','ROCK')],['ROCK IS FLAG']);assert m.command('N')
        m.spec['fixedRules']=['ROBOT IS YOU','FLAG IS ROCK'];m.refresh();assert m.command('S')
        assert m.entity('r')['subject']=='ROCK'
    check('An explicit reverse noun sentence can change the same ID back later',reverse)
    def target_hover():
        m=model([ob('r','ROCK',(3,4,0))],['ROCK IS FLAG','FLAG IS HOVER']);assert m.command('N')
        assert m.entity('r')['pos']==[3,4,0] and m.gravity(m.entity('r'))=='Hover'
    check('Target HOVER applies immediately before automatic falling',target_hover)
    def old_hover_lost():
        m=model([ob('r','CLOUD',(3,4,0))],['CLOUD IS HOVER','CLOUD IS ROCK','ROCK IS PUSH']);assert m.command('N')
        assert m.entity('r')['subject']=='ROCK' and m.entity('r')['pos']==[3,1,0]
    check('Old noun HOVER is not inherited; converted rock obeys default world gravity',old_hover_lost)
    def simultaneous():
        spec=fixture([ob('a','ROCK'),ob('b','CLOUD',(4,1,0))],['ROCK IS CLOUD','CLOUD IS ROCK'])
        m=Model(spec);assert m.command('N');assert m.entity('a')['subject']=='CLOUD' and m.entity('b')['subject']=='ROCK'
        final=m.fingerprint();spec['entities'].reverse();mm=Model(spec);assert mm.command('N');assert mm.fingerprint()==final
    check('All conversion sources are sampled together; swaps do not depend on entity iteration order',simultaneous)
    def chain():
        m=model([ob('r','ROCK')],['ROCK IS CLOUD','CLOUD IS FLAG']);assert m.command('N')
        assert m.entity('r')['subject']=='CLOUD';assert m.command('S');assert m.entity('r')['subject']=='FLAG'
    check('A to B to C is one conversion per entity per accepted command, not transitive closure',chain)
    def cycle():
        m=model([ob('r','ROCK')],['ROCK IS CLOUD','CLOUD IS ROCK'])
        for c,expected in [('N','CLOUD'),('S','ROCK'),('N','CLOUD'),('S','ROCK')]:
            assert m.command(c);assert m.entity('r')['subject']==expected
    check('Two-way noun cycles alternate on accepted commands, not within one endless transaction',cycle)
    def many_substeps():
        m=model([ob('r','ROCK',(3,5,0))],['ROCK IS HOVER','ROCK IS CLOUD','CLOUD IS FLAG'])
        assert m.command('N');assert m.entity('r')['pos']==[3,1,0] and m.entity('r')['subject']=='CLOUD'
        assert len([e for e in m.log if e['event']=='Transformed' and e['id']=='r'])==1
    check('Multiple gravity substeps never convert the same entity a second time in that command',many_substeps)
    def invalid_no_tick():
        m=model([ob('r','ROCK')],['ROCK IS FLAG']);before=m.fingerprint()
        assert not m.command('WAIT');assert m.fingerprint()==before and not m.history
    check('Rejected grounded WAIT does not advance noun conversion or undo history',invalid_no_tick)
    def camera_no_tick():
        m=model([ob('r','ROCK')],['ROCK IS FLAG']);before=m.fingerprint()
        assert m.command('CAM+') and m.fingerprint()==before and not m.history
    check('Camera rotation does not advance noun transformations',camera_no_tick)
    def rule_only_win():
        m=model([ob('r','ROCK',(1,1,0))],['ROCK IS FLAG','FLAG IS WIN'])
        assert m.command('E') and m.won() and m.win_record['winId']=='r'
    check('Conversion can create a valid WIN pair on already shared coordinates',rule_only_win)
    def no_intermediate_win():
        m=model([ob('r','ROCK',(1,1,0))],['ROCK IS WIN','ROCK IS CLOUD'])
        assert m.command('E') and not m.won() and not m.props(m.entity('r'))
    check('Victory is checked after the conversion batch; transient old-noun WIN does not latch',no_intermediate_win)
    def solid_conflict():
        m=model([ob('c','CLOUD'),ob('s','SPRING')],['CLOUD IS ROCK','ROCK IS STOP','SPRING IS STOP'])
        before=m.fingerprint();assert not m.command('N');assert m.fingerprint()==before and not m.history
        assert 'overlap' in m.last_rejection
    check('Conversion-caused two-solid overlap rolls back the whole command rather than ejecting objects',solid_conflict)
    def multiple_you():
        m=model([ob('r','ROCK')],['ROCK IS ROBOT']);before=m.fingerprint()
        assert not m.command('N') and m.fingerprint()==before and not m.history
        assert 'MultipleYOU' in m.last_rejection
    check('Conversion respects the existing single-YOU boundary and reports all-or-nothing failure',multiple_you)
    def controlled_gravity():
        m=model(rules=['ROBOT IS CLOUD','CLOUD IS YOU AND HOVER']);before=m.fingerprint()
        assert not m.command('N') and m.fingerprint()==before and 'ControlledGravity' in m.last_rejection
    check('Conversion does not silently invent direct controlled HOVER flight inputs',controlled_gravity)
    def duplicate():
        m=model([ob('r','ROCK')],['ROCK IS FLAG','ROCK IS FLAG']);assert m.command('N')
        assert len([e for e in m.log if e['event']=='Transformed'])==1
    check('Duplicate noun rules do not create duplicate entities or repeated conversions',duplicate)
    def ambiguous():
        try:model([ob('r','ROCK')],['ROCK IS FLAG','ROCK IS CLOUD'])
        except RuleConflict as e:assert 'MultipleTransformTargets' in str(e)
        else:raise AssertionError('ambiguous target accepted')
    check('Different simultaneous target nouns are an explicit conflict, not random choice or cloning',ambiguous)
    def self_identity():
        m=model([ob('r','ROCK')],['ROCK IS ROCK']);assert m.command('N')
        assert m.entity('r')['subject']=='ROCK' and not any(e['event']=='Transformed' for e in m.log)
    check('A IS A is a no-op, not a duplication or an implicit transformation lock',self_identity)
    def self_with_other():
        m=model([ob('r','ROCK')],['ROCK IS ROCK','ROCK IS FLAG']);assert m.command('N')
        assert m.entity('r')['subject']=='FLAG'
    check('Self-identity does not override an explicit different target in this contract',self_with_other)
    def lhs_and():
        m=model([ob('a','ROCK'),ob('b','CLOUD',(4,1,0))],['ROCK AND CLOUD IS FLAG']);assert m.command('N')
        assert m.entity('a')['subject']==m.entity('b')['subject']=='FLAG'
    check('AND may group source nouns for a single target',lhs_and)
    def rhs_unsupported():
        for text in ['ROCK IS FLAG AND CLOUD','ROCK IS FLAG AND WIN','ROCK IS WIN AND FLAG']:
            try:model([ob('r','ROCK')],[text])
            except RuleConflict:pass
            else:raise AssertionError(text)
    check('Noun output fan-out and mixed noun-property RHS have explicit diagnostics',rhs_unsupported)
    def transfer_batch():
        m=model([ob('c','CLOUD')],['ROBOT IS FLAG','CLOUD IS ROBOT','FLAG IS WIN'])
        assert m.command('N') and m.actor_id()=='c' and m.entity('body')['subject']=='FLAG'
        assert m.entity('c')['subject']=='ROBOT' and not m.won()
    check('Simultaneous replacement can move control safely; no validation of a temporary half-batch',transfer_batch)
    def no_template_copy():
        m=model([ob('r','ROCK'),ob('f','FLAG',(8,1,0))],['ROCK IS FLAG']);assert m.command('N')
        assert len(m.entities)==3 and m.entity('r')['pos']==[3,1,0] and m.entity('f')['pos']==[8,1,0]
    check('Conversion changes one existing ID in place; it neither teleports to nor consumes a target instance',no_template_copy)
    def audit_not_inflated():
        m=model([ob('r','ROCK')],['ROCK IS FLAG']);assert m.command('N')
        assert not active_interaction(m.log)
    check('Automatic conversion alone is not counted as the required active word/object interaction',audit_not_inflated)
    def undo_win():
        m=model([ob('r','ROCK',(1,1,0))],['ROCK IS FLAG','FLAG IS WIN']);before=m.fingerprint()
        assert m.command('E') and m.won();assert m.undo() and m.fingerprint()==before
    check('Winning undo restores subject identities, control, properties and the victory pair together',undo_win)
    return checks


def main():
    import random,hashlib
    from collections import Counter
    loaded=read_level_specs()
    assert [r['id'] for r,_,_ in loaded]==[f'L{i:02}' for i in range(1,13)]
    checks=run_checks()+run_v07_checks()+run_v08_checks()
    reports=[]
    for raw,spec,path in loaded:
        m=Model(spec);assert not m.pending_transforms(), ('initial-conversion',raw['id']);initial=m.fingerprint();m.settle()
        assert m.fingerprint()==initial and not m.won(),('unstable/initial-win',raw['id'])
        route_reports=[]
        for route in raw['referenceSolutions']:
            m=Model(spec);assert m.fingerprint()==initial
            rows=[];first_interaction=None
            for step,c in enumerate(route['commands'],1):
                start=len(m.log)
                assert m.command(c),(raw['id'],route['id'],step,c,'rejected')
                assert m.won()==(step==len(route['commands'])),('premature/missing win',raw['id'],route['id'],step)
                delta=m.log[start:]
                if first_interaction is None and active_interaction(delta):first_interaction=step
                actor=m.actor()
                rows.append(dict(step=step,command=c,actorId=m.actor_id(),actorCell=actor['pos'].copy() if actor else None,
                    phase=m.phase(),events=sorted({e['event'] for e in delta})))
            kinds={e['event'] for e in m.log}
            if raw['designContract']['requireActiveInteraction']:assert first_interaction is not None
            for key,w in [('mustWinWith','winId'),('mustControlAtWin','youId')]:
                if key in route:assert m.win_record[w]==route[key],(raw['id'],route['id'],key,m.win_record)
            assert all(x in kinds for x in route.get('requireEvents',[]))
            assert not any(x in kinds for x in route.get('forbidEvents',[]))
            final=m.fingerprint()
            signature=dict(youId=m.win_record['youId'],winId=m.win_record['winId'],winCell=m.win_record['cell'],
                usedBounce='LandingBounce' in kinds,usedLift='FlyOrRide' in kinds,transferredControl='ControlChanged' in kinds,usedTransformation='Transformed' in kinds)
            rr=dict(id=route['id'],name=route['name'],family=route['family'],passed=True,
                commandCount=len(route['commands']),firstInteractionStep=first_interaction,
                win=m.win_record,mechanismSignature=signature,
                eventCounts=dict(Counter(e['event'] for e in m.log)),
                interactions=[e for e in m.log if e['event']=='ActiveInteraction'],
                controlChanges=[e for e in m.log if e['event']=='ControlChanged'],
                transformations=[e for e in m.log if e['event']=='Transformed'],
                finalSubjects={e['id']:e.get('subject','') for e in m.entities if e['kind']=='OBJECT'},trace=rows)
            for _ in route['commands']:assert m.undo()
            assert m.fingerprint()==initial and not m.history
            for seed in range(4):
                variant=copy.deepcopy(spec);random.Random(seed).shuffle(variant['entities'])
                mm=Model(variant)
                for c in route['commands']:assert mm.command(c)
                assert mm.fingerprint()==final,('entity insertion order',raw['id'],route['id'],seed)
            route['status']='ReferenceModelPassed_NotUnityVerified'
            route['expectedTrace']=rows
            route['expectedWin']=rr['win']
            route_reports.append(rr)
        signatures={json.dumps(r['mechanismSignature'],sort_keys=True) for r in route_reports}
        assert len(signatures)>=raw['designContract']['minimumSolutionFamilies']
        audit=audit_no_interaction(spec)
        if raw['designContract']['requireActiveInteraction']:
            assert audit['status']=='EXHAUSTED_NO_INTERACTION_WIN',(raw['id'],audit)
        else:
            assert audit['status']=='TRAVERSAL_WIN_FOUND'
            audit['expectedForTutorial']=True
        path.write_text(json.dumps(raw,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        reports.append(dict(id=raw['id'],title=raw['title'],stableInitial=True,
            sameInitialForAllRoutes=True,undoFullRestore=True,entityOrderSeeds=[0,1,2,3],
            requiredInteraction=raw['designContract']['requireActiveInteraction'],noInteractionAudit=audit,
            distinctWitnessSignatures=len(signatures),routes=route_reports,
            dataSHA256=hashlib.sha256(path.read_bytes()).hexdigest()))
        print(raw['id'],f'{len(route_reports)} route(s)',audit['status'],f"{audit['states']} states")
    multi=sum(r['distinctWitnessSignatures']>=2 for r in reports)
    total_routes=sum(len(r['routes']) for r in reports)
    assert multi>=4
    output=dict(mechanicsVersion='RW-v0.8',levelCount=12,witnessRouteCount=total_routes,
        multiSolutionLevelCount=multi,minimumRequiredMultiSolutionLevelCount=4,
        finiteRegressionCount=len(checks),allFiniteRegressionsPassed=True,
        interactionAuditPassedLevels=[r['id'] for r in reports if r['requiredInteraction']],
        validationScope='Authored finite Python graybox model only; 11 exhaustive no-interaction subgraph searches. Not all-solutions enumeration, Unity, visual, input, editor, or playtest validation.',
        unsupportedInReference=['Void removal/death fixtures','multiple simultaneous YOU','direct YOU+HOVER/FLY input','Unity physics/rendering/input/editor','one-to-many noun copying / mixed RHS syntax'],
        checks=checks,levels=reports)
    (ROOT/'validation_results.json').write_text(json.dumps(output,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f'PASS: 12 levels; {total_routes} witnesses; {multi} multi-solution levels; {len(checks)} finite checks; 11 no-interaction searches exhausted.')

if __name__=='__main__':
    main()
