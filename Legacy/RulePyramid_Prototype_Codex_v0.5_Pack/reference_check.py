"""RP-v0.5 deterministic, single-YOU graybox witness checker.
Python 3.10+, standard library only. Run: python validate_grayboxes.py
Reads authored JSON; does not overwrite it. Not a Unity runtime or general solver.
The checks intentionally model atomic grid transitions, not animation colliders.
"""
from __future__ import annotations
import copy
import json
from pathlib import Path
from typing import Any

DIRECTIONS = {'E': (1,0,0), 'W': (-1,0,0), 'N': (0,0,1), 'S': (0,0,-1)}
COLORS = {'RED', 'BLUE', 'PINK'}
PROPS = {'YOU', 'BLOCK', 'HOVER', 'FLY', 'WIN', 'JUMP'}
UP = (0,1,0)
DOWN = (0,-1,0)
COMMANDS = [*DIRECTIONS, 'J', 'WAIT']
LEGACY_JUMPS = {'JE', 'JW', 'JN', 'JS'}
ROOT = Path(__file__).resolve().parent


def add(a, b):
    return tuple(x+y for x,y in zip(a,b))


def levels():
    """Read the SAME canonical JSON files the Unity importer should use.
    Compact tuples below are internal model data, not a second persistent format.
    """
    catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf-8'))
    if catalog.get('schemaVersion')!=5 or catalog.get('mechanicsVersion')!='RP-v0.5':
        raise ValueError('Unsupported catalog/version')
    def xyz(c): return [c['x'],c['y'],c['z']]
    def extent(b):
        lo,hi=b['min'],b['max']
        return [lo['x'],hi['x'],lo['y'],hi['y'],lo['z'],hi['z']]
    result=[]
    for relative in catalog['levels']:
        f=(ROOT/relative).resolve()
        if ROOT not in f.parents: raise ValueError('Unsafe catalog path')
        raw=json.loads(f.read_text(encoding='utf-8'))
        if raw.get('schemaVersion')!=5 or raw.get('mechanicsVersion')!='RP-v0.5':
            raise ValueError('Explicit redesign required; do not relabel legacy levels')
        spec=dict(id=raw['id'],title=raw['title'],bounds=extent(raw['bounds']),
                  solidBoxes=[extent(b) for b in raw['terrain']],
                  entities=[dict(id=e['id'],kind=e['kind'].upper(),color=e['color'],token=e['token'],pos=xyz(e['cell']),anchored=e['anchored']) for e in raw['entities']],
                  fixedRules=[' '.join(r['tokens']) for r in raw['fixedRules']],
                  solution=raw['referenceSolution']['commands'],camera=raw['camera'],**raw['options'])
        for e in spec['entities']:
            if e['kind']=='TEXT' and e['token'] not in COLORS|PROPS|{'IS','AND'}:
                raise ValueError(('Unknown token',e['token']))
        for rule in spec['fixedRules']:
            if any(t not in COLORS|PROPS|{'IS','AND'} for t in rule.split()): raise ValueError('Unknown fixed token')
        if any(c not in COMMANDS for c in spec['solution']): raise ValueError('Unsupported replay command')
        assert spec['actionMode']=='FourWayMoveInPlaceJumpApexSteer'
        assert spec['winMode']=='DistinctEntitiesSameCell'
        assert spec['gravityMode']=='WorldDownExceptHoverOrFly'
        assert spec['playerBlockMode']=='ImplicitFromYou'
        result.append(spec)
    return result


class _VictoryCommitted(Exception):
    """Internal short-circuit: stop a successful transaction at its winning microstep."""


class Model:
    """One YOU with implicit BLOCK and world-default gravity; direct support; discrete bounce apex.

    Non-BLOCK COLOR and YOU are mutually non-blocking (a bidirectional sensor pair).
    Non-BLOCK movers still stop at terrain, text and non-YOU BLOCK objects.
    This collision-layer contract is not a WIN exception: WIN never deletes BLOCK.
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
        assert len(actors) == 1 and actors[0]['id'] == 'player', 'Checker supports one player.'
        assert self.solid(actors[0]) and self.gravity(actors[0]) == 'Down', 'Implicit player defaults required in these witnesses.'
        self.check_win('LevelLoaded', interrupt=False)

    def entity(self, eid):
        return next(e for e in self.entities if e['id'] == eid)

    def props(self, e):
        return self.rules.get(e.get('color'),set()) if e['kind'] == 'COLOR' else set()

    def refresh(self):
        rules = {c:set() for c in COLORS}
        def parse(ts):
            if not ts or ts[0] not in COLORS:
                return
            subjects = [ts[0]]; i = 1
            while i+1 < len(ts) and ts[i] == 'AND' and ts[i+1] in COLORS:
                subjects.append(ts[i+1]); i += 2
            if i >= len(ts) or ts[i] != 'IS':
                return
            i += 1
            if i >= len(ts) or ts[i] not in PROPS:
                return
            properties = [ts[i]]; i += 1
            while i+1 < len(ts) and ts[i] == 'AND' and ts[i+1] in PROPS:
                properties.append(ts[i+1]); i += 2
            for subject in subjects:
                rules[subject].update(properties)
        for text in self.spec['fixedRules']:
            parse(text.split())
        tokens = {tuple(e['pos']): e['token'] for e in self.entities if e['kind'] == 'TEXT'}
        for p, token in tokens.items():
            if token not in COLORS:
                continue
            for d in [(1,0,0),(0,0,1)]:
                q = p; ts = []
                while q in tokens:
                    ts.append(tokens[q]); q = add(q,d)
                parse(ts)
        self.rules = rules

    def solid(self, e):
        return e['kind'] == 'TEXT' or bool({'BLOCK','YOU'} & self.props(e))

    def gravity(self, e):
        if e.get('anchored',False): return 'Anchored'
        if 'FLY' in self.props(e): return 'Up'
        if 'HOVER' in self.props(e): return 'Hover'
        return 'Down'

    def out(self, p):
        x0,x1,y0,y1,z0,z1 = self.spec['bounds']; x,y,z = p
        return not (x0 <= x <= x1 and y0 <= y <= y1 and z0 <= z <= z1)

    def blocks_pair(self, a, b):
        if a['kind']=='TEXT' or b['kind']=='TEXT': return True
        pa,pb=self.props(a),self.props(b)
        if ('YOU' in pa and not self.solid(b)) or ('YOU' in pb and not self.solid(a)): return False
        return self.solid(a) or self.solid(b)

    def validate_state(self):
        cells={}
        for e in self.entities:
            p=tuple(e['pos'])
            assert not self.out(p), ('out of bounds',e['id'],p)
            assert p not in self.terrain, ('terrain overlap',e['id'],p)
            for other in cells.get(p,[]):
                assert not self.blocks_pair(e,other), ('illegal pair overlap',e['id'],other['id'],p)
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
        return isinstance(e,dict) and not e.get('anchored',False)

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
        staged = [(e, list(e['pos']), list(dest)) for e,dest in moves]
        for e,_,dest in staged:
            e['pos'] = dest
        for e,old,dest in staged:
            self.event(reason, id=e['id'], **{'from':old, 'to':dest})
        self.refresh()
        self.validate_state()
        self.check_win(reason)

    def shift(self, e, d, reason):
        self.commit([(e,add(tuple(e['pos']),d))], reason)

    def plan_text_push(self, first, d):
        chain = []; current = first
        for _ in range(len(self.entities)+1):
            if not (isinstance(current,dict) and current['kind']=='TEXT' and self.movable(current)):
                return None
            chain.append(current)
            current = self.occupant(add(tuple(current['pos']),d))
            if current is None:
                return chain
        raise AssertionError('Unexpected text push chain cycle')

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
            dest = add(tuple(mover['pos']),DOWN)
            hit = self.occupant(dest,ignore=(mover['id'],),mover=mover)
            if hit is None:
                self.shift(mover,DOWN,'PlayerFell' if mover['id']=='player' else 'GravityFall')
                descended = True
                continue
            if isinstance(hit,dict) and not self.solid(hit):
                raise ValueError('Unsupported solid-to-hollow gravity contact in witness subset')
            if descended and isinstance(hit,dict) and 'JUMP' in self.props(hit):
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
        seen = set(); player = self.entity('player')
        for _ in range(128):
            key = self.fingerprint()+repr(sorted(self.pressed))
            if key in seen:
                raise AssertionError('Automatic-resolution cycle')
            seen.add(key); before = key
            for e in sorted(self.entities,key=lambda v:(-v['pos'][1],v['id'])):
                if (e['kind']!='COLOR' or e['id']=='player' or e['id'] in self.apex
                        or e['id'] in self.forced_fall or self.gravity(e) != 'Up'):
                    continue
                group = [e]
                if (self.solid(e) and 'player' not in self.apex
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
                    self.land(e,allow_press=(e['id']=='player'))
            if before == self.fingerprint()+repr(sorted(self.pressed)):
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
        return 'Won' if self.won_latched else ('BounceApex' if 'player' in self.apex else 'Grounded')

    def snapshot(self):
        return {'entities':copy.deepcopy(self.entities),'apex':copy.deepcopy(self.apex),
                'forcedFall':sorted(self.forced_fall),'won':self.won_latched,'winRecord':copy.deepcopy(self.win_record)}

    def restore(self, snapshot):
        self.entities = copy.deepcopy(snapshot['entities'])
        self.apex = copy.deepcopy(snapshot['apex'])
        self.forced_fall = set(snapshot['forcedFall'])
        self.won_latched = snapshot['won']; self.win_record = copy.deepcopy(snapshot['winRecord'])
        self.pressed.clear(); self.refresh(); self.validate_state()

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
        snap = self.snapshot(); oldlog = len(self.log)
        player = self.entity('player'); had_apex = 'player' in self.apex
        self.forced_fall.update(self.apex); self.apex.clear(); self.pressed.clear()
        success = False
        try:
            if had_apex:
                if cmd in ('WAIT','J'):
                    self.event('ApexReleased',id='player',steer=None); success=True
                elif cmd in DIRECTIONS:
                    d=DIRECTIONS[cmd]
                    if self.free(add(tuple(player['pos']),d),ignore=('player',),mover=player):
                        self.shift(player,d,'ApexSteer'); success=True
            elif cmd == 'J':
                up=add(tuple(player['pos']),UP); hit=self.occupant(up,mover=player)
                if hit is None:
                    self.shift(player,UP,'JumpApex'); success=True
                elif self.movable(hit) and self.free(add(up,UP),mover=hit):
                    self.commit([(hit,add(up,UP)),(player,up)],'HeadBump'); success=True
            elif cmd in DIRECTIONS:
                d=DIRECTIONS[cmd]; dest=add(tuple(player['pos']),d)
                hit=self.occupant(dest,mover=player)
                if hit is None:
                    self.shift(player,d,'Walk'); success=True
                elif isinstance(hit,dict) and hit['kind']=='TEXT' and self.movable(hit):
                    chain=self.plan_text_push(hit,d)
                    if chain is not None:
                        self.commit([(e,add(tuple(e['pos']),d)) for e in chain]+[(player,dest)],'TextPushed')
                        success=True
            if not success:
                self.restore(snap); del self.log[oldlog:]; return False
            self.land(player,allow_press=True)
            self._settle()
        except _VictoryCommitted:
            success=True  # Valid terminal microstep: retain it; do not roll back or finish gravity.
        except Exception:
            self.restore(snap); del self.log[oldlog:]; raise
        self.history.append(snap)
        return success


def run_checks():
    """Finite witness checks, NOT exhaustive correctness or solvability proofs."""
    results=[]
    def check(name, fn):
        fn(); results.append({'name':name,'passed':True})
    def color(eid,c,p):return dict(id=eid,kind='COLOR',color=c,pos=list(p),anchored=False)
    def word(eid,t,p,anchored=False):return dict(id=eid,kind='TEXT',token=t,pos=list(p),anchored=anchored)
    def fixture(player=(0,1,0), extras=(), rules=(), boxes=()):
        return dict(id='fixture',bounds=[-4,7,0,12,-4,4],solidBoxes=[[-4,7,0,0,-4,4],*boxes],
                    entities=[color('player','RED',player),*copy.deepcopy(list(extras))],
                    fixedRules=['RED IS YOU',*rules],bounceRiseCells=3)
    def defaults():
        m=Model(fixture());p=m.entity('player')
        assert m.props(p)=={'YOU'} and m.solid(p) and m.gravity(p)=='Down'
    check('Only explicit YOU; player is effectively solid and world-gravitating',defaults)
    def text_fall():
        m=Model(fixture(extras=[word('t','AND',(2,4,0))]));m.settle()
        assert m.entity('t')['pos']==[2,1,0]
    check('Movable text falls without any FALL rule',text_fall)
    def hollow_fall():
        m=Model(fixture(extras=[color('h','PINK',(2,4,0))]));m.settle()
        assert not m.solid(m.entity('h')) and m.entity('h')['pos']==[2,1,0]
    check('Hollow non-player colors also fall to world terrain',hollow_fall)
    def anchored():
        m=Model(fixture(extras=[word('t','AND',(2,4,0),True)]));m.settle()
        assert m.entity('t')['pos']==[2,4,0] and m.gravity(m.entity('t'))=='Anchored'
    check('Visibly anchored text does not fall',anchored)
    def hover():
        m=Model(fixture(extras=[color('h','BLUE',(2,4,0))],rules=['BLUE IS HOVER']));m.settle()
        assert m.entity('h')['pos']==[2,4,0] and not m.solid(m.entity('h'))
    check('HOVER retains height but does not supply BLOCK',hover)
    def priority():
        m=Model(fixture(extras=[color('h','BLUE',(2,4,0))],rules=['BLUE IS HOVER AND FLY']));assert m.gravity(m.entity('h'))=='Up'
        m.spec['fixedRules']=['RED IS YOU','BLUE IS HOVER'];m.refresh();assert m.gravity(m.entity('h'))=='Hover'
        m.spec['fixedRules']=['RED IS YOU'];m.refresh();assert m.gravity(m.entity('h'))=='Down'
    check('Gravity derives as FLY > HOVER > world Down; no stale defaults',priority)
    def identity():
        m=Model(fixture());p=m.entity('player');m.spec['fixedRules']=[];m.refresh();assert not m.solid(p)
        m.spec['fixedRules']=['RED IS BLOCK'];m.refresh();assert m.solid(p)
    check('Implicit BLOCK follows YOU, not the red color or a sticky component',identity)
    def and_prefix():
        es=[word('a','BLUE',(0,2,2),True),word('b','IS',(1,2,2),True),word('c','BLOCK',(2,2,2),True),word('d','FLY',(4,2,2),True)]
        m=Model(fixture(extras=es));assert m.rules['BLUE']=={'BLOCK'}
        m.entities.append(word('e','AND',(3,2,2),True));m.refresh();assert m.rules['BLUE']=={'BLOCK','FLY'}
    check('Incomplete AND suffix does not erase a valid prefix',and_prefix)
    def axes():
        for vector,expected in [((1,0,0),True),((0,0,1),True),((-1,0,0),False),((0,1,0),False),((1,1,0),False)]:
            base=(2,4,2);es=[word(str(i),t,tuple(base[j]+i*vector[j] for j in range(3)),True) for i,t in enumerate(['BLUE','IS','BLOCK'])]
            m=Model(fixture(extras=es));assert ('BLOCK' in m.rules['BLUE'])==expected
    check('Only +X and +Z same-height straight sentences parse',axes)
    def head_text():
        m=Model(fixture(extras=[word('t','AND',(0,2,0))]));assert m.command('J')
        assert m.entity('player')['pos']==[0,1,0] and m.entity('t')['pos']==[0,2,0]
        assert any(e['event']=='HeadBump' for e in m.log) and any(e['event']=='GravityFall' and e.get('id')=='t' for e in m.log)
    check('Head-bumped normal text falls back; no hidden levitation',head_text)
    def head_hover():
        m=Model(fixture(extras=[color('h','BLUE',(0,2,0))],rules=['BLUE IS BLOCK AND HOVER']));assert m.command('J')
        assert m.entity('h')['pos']==[0,3,0] and m.entity('player')['pos']==[0,1,0]
    check('Head-bumped HOVER block legitimately retains its new height',head_hover)
    def press_hover():
        m=Model(fixture(player=(0,5,0),extras=[color('h','BLUE',(0,2,0))],rules=['BLUE IS BLOCK AND HOVER']));m.settle()
        assert m.entity('h')['pos']==[0,1,0]
        assert sum(e['event']=='LandingPress' and e.get('id')=='player' for e in m.log)==1
    check('Landing press can move a HOVER solid down by one when clear',press_hover)
    def jump_priority():
        m=Model(fixture(player=(0,5,0),extras=[color('h','BLUE',(0,2,0))],rules=['BLUE IS BLOCK AND HOVER AND JUMP']));m.settle()
        assert m.entity('h')['pos']==[0,2,0] and m.entity('player')['pos']==[0,6,0]
        assert m.phase()=='BounceApex' and not any(e['event']=='LandingPress' for e in m.log)
    check('JUMP consumes landing before press; rise measured from contact +3',jump_priority)
    def no_auto_climb():
        m=Model(fixture(boxes=[[1,1,1,1,0,0]]));before=m.fingerprint()
        assert not m.command('E') and before==m.fingerprint()
        assert m.command('J') and m.entity('player')['pos']==[0,1,0]
    check('No auto-step and no ordinary-jump horizontal displacement',no_auto_climb)
    def old_jumps():
        m=Model(fixture());before=m.fingerprint()
        for c in LEGACY_JUMPS:assert not m.command(c)
        assert before==m.fingerprint() and not m.history
    check('Removed directional-jump commands are rejected without ticking',old_jumps)
    def neighbors():
        for delta in [*DIRECTIONS.values(),UP,DOWN,(1,1,1)]:
            m=Model(fixture(player=(0,3,0),extras=[color('goal','PINK',add((0,3,0),delta))],rules=['PINK IS WIN']))
            assert not m.won()
    check('All six face neighbors and a corner fail the victory predicate',neighbors)
    def enter():
        m=Model(fixture(extras=[color('goal','PINK',(1,1,0))],rules=['PINK IS WIN']));assert m.command('E') and m.won()
        assert m.undo() and not m.won() and m.entity('player')['pos']==[0,1,0]
    check('Enter hollow WIN and undo the entire winning command',enter)
    def reverse():
        m=Model(fixture(extras=[color('goal','PINK',(0,4,0))],rules=['PINK IS WIN']));m.settle()
        assert m.won() and m.win_record['cell']==[0,1,0]
    check('Default gravity can bring a hollow WIN into a BLOCK player',reverse)
    def not_win():
        m=Model(fixture(extras=[color('goal','PINK',(1,1,0))]));assert m.command('E') and not m.won()
        m=Model(fixture(rules=['RED IS WIN']));assert not m.won()
    check('Inactive target and single-entity YOU+WIN do not self-win',not_win)
    def win_block():
        m=Model(fixture(extras=[color('goal','PINK',(1,1,0))],rules=['PINK IS BLOCK AND WIN']))
        assert not m.command('E') and not m.won()
    check('WIN does not cancel BLOCK or enable contact victory',win_block)
    def mid_bounce():
        m=Model(fixture(player=(0,3,0),extras=[color('spring','BLUE',(0,1,0)),color('goal','PINK',(0,4,0))],rules=['BLUE IS BLOCK AND JUMP','PINK IS HOVER AND WIN']))
        m.settle();assert m.won() and m.entity('player')['pos']==[0,4,0] and 'player' not in m.apex
    check('Mid-bounce target hit commits victory before the normal apex',mid_bounce)
    def apex_input():
        s=next(s for s in levels() if s['id']=='L04');m=Model(s)
        assert m.command('N') and m.command('E') and m.phase()=='BounceApex'
        before=m.fingerprint();m.command('CAM+');assert m.fingerprint()==before
        m.terrain.add((4,5,1));before=m.fingerprint();n=len(m.history)
        assert not m.command('E') and m.fingerprint()==before and len(m.history)==n
    check('Camera and rejected apex steer preserve the pending apex',apex_input)
    def removed_hover():
        s=next(s for s in levels() if s['id']=='L03');m=Model(s)
        assert m.entity('bridge')['pos']==[3,5,2]
        assert m.command('E') and m.command('N')
        assert 'HOVER' not in m.rules['BLUE'] and m.entity('bridge')['pos']==[3,2,2]
        assert m.undo() and m.entity('bridge')['pos']==[3,5,2] and 'HOVER' in m.rules['BLUE']
    check('Breaking HOVER restores fall in the same command; undo restores both',removed_hover)
    def fly_ride():
        s=next(s for s in levels() if s['id']=='L05');m=Model(s)
        for c in s['solution'][:3]:assert m.command(c)
        assert m.entity('lift_front')['pos']==[3,4,0] and m.entity('player')['pos']==[3,5,0]
        assert m.entity('t_and')['pos']==[3,2,-1] and 'FLY' in m.rules['BLUE']
    check('AND stays physically supported while BLOCK+FLY carries the player',fly_ride)
    return results


def validate_witnesses():
    reports=[]
    for spec in levels():
        m=Model(spec);before=m.fingerprint();m.settle()
        assert m.fingerprint()==before and not m.won(), ('Unstable initial witness',spec['id'])
        steps=[]
        for index,cmd in enumerate(spec['solution'],1):
            assert not m.won(), ('Early win',spec['id'],index)
            assert m.command(cmd), ('Rejected witness command',spec['id'],index,cmd)
            steps.append(dict(step=index,command=cmd,player=list(m.entity('player')['pos']),phase=m.phase(),
                              rules={c:sorted(p) for c,p in m.rules.items()},goal=list(m.entity('goal')['pos'])))
        assert m.won(),('Witness not won',spec['id'])
        win=copy.deepcopy(m.win_record);events=copy.deepcopy(m.log)
        assert m.undo() and not m.won()
        # Scramble input list order; compare normalized final states, not raw list order.
        other=copy.deepcopy(spec);other['entities'].reverse();n=Model(other)
        for cmd in other['solution']:assert n.command(cmd)
        assert n.win_record==win
        reports.append(dict(levelId=spec['id'],initialStable=True,winOnlyOnLastCommand=True,undoFromWin=True,
                            reversedEntityOrderSameWin=True,steps=steps,win=win,events=events))
    return reports


if __name__=='__main__':
    if not __debug__:
        raise RuntimeError('Do not use python -O: this witness checker requires assertions.')
    import argparse
    parser=argparse.ArgumentParser(description='Check RP-v0.5 design witnesses; not Unity or an exhaustive solver.')
    parser.add_argument('--output',type=Path,help='Optional generated validation report path (level files are never edited).')
    args=parser.parse_args()
    report=dict(mechanicsVersion='RP-v0.5',scope='Finite Python design witnesses only; Unity, full state-space search, complex contacts and editor interactions are NOT certified.',
                levels=validate_witnesses(),checks=run_checks())
    if args.output:
        args.output.parent.mkdir(parents=True,exist_ok=True)
        args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(f"Passed {len(report['levels'])} level witnesses and {len(report['checks'])} focused checks. Not a Unity validation.")
