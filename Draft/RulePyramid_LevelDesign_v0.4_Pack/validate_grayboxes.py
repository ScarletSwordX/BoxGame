"""LD-v0.4 deterministic, single-YOU graybox witness checker.
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
PROPS = {'YOU', 'BLOCK', 'FALL', 'FLY', 'WIN', 'JUMP'}
UP = (0,1,0)
DOWN = (0,-1,0)
COMMANDS = [*DIRECTIONS, 'J', 'WAIT']
LEGACY_JUMPS = {'JE', 'JW', 'JN', 'JS'}
ROOT = Path(__file__).resolve().parent


def add(a, b):
    return tuple(x+y for x,y in zip(a,b))


def levels():
    payload = json.loads((ROOT/'six_levels_graybox.json').read_text(encoding='utf-8'))
    if payload.get('schema') != 'DesignGraybox-v0.4' or payload.get('schemaVersion') != 4:
        raise ValueError('Incompatible schema. Explicitly redesign/migrate earlier mechanics; do not relabel.')
    for spec in payload['levels']:
        if (spec.get('winMode') != 'DistinctEntitiesSameCell' or
            spec.get('winCheckMode') != 'AfterAtomicLogicChange' or
            spec.get('actionMode') != 'FourWayMoveInPlaceJumpApexSteer'):
            raise ValueError(f"Wrong v0.4 mechanics in {spec['id']}")
        if any(cmd in LEGACY_JUMPS for cmd in spec['solution']):
            raise ValueError(f"Removed directional jump in {spec['id']}")
    return payload['levels']


class _VictoryCommitted(Exception):
    """Internal short-circuit: stop a successful transaction at its winning microstep."""


class Model:
    """One YOU with explicit BLOCK + FALL; direct support; discrete bounce apex.

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
        assert {'BLOCK','FALL'} <= self.props(actors[0]), 'Explicit player BLOCK + FALL required.'
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
        return e['kind'] == 'TEXT' or 'BLOCK' in self.props(e)

    def out(self, p):
        x0,x1,y0,y1,z0,z1 = self.spec['bounds']; x,y,z = p
        return not (x0 <= x <= x1 and y0 <= y <= y1 and z0 <= z <= z1)

    def validate_state(self):
        solids = {}
        for e in self.entities:
            p = tuple(e['pos'])
            assert not self.out(p), ('out of bounds', e['id'], p)
            assert p not in self.terrain, ('terrain overlap', e['id'], p)
            if self.solid(e):
                assert p not in solids, ('illegal solid overlap', solids.get(p), e['id'], p)
                solids[p] = e['id']

    def occupant(self, p, ignore=(), mover=None):
        if self.out(p) or p in self.terrain:
            return 'WALL'
        hits = []
        for e in self.entities:
            if e['id'] in ignore or tuple(e['pos']) != p or not self.solid(e):
                continue
            if (mover is not None and mover['kind'] == 'COLOR' and not self.solid(mover)
                    and 'YOU' in self.props(e)):
                continue  # Explicit hollow-COLOR <-> YOU sensor pair, independent of WIN.
            hits.append(e)
        assert len(hits) <= 1, ('illegal solid occupancy', p)
        return hits[0] if hits else None

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
                        or e['id'] in self.forced_fall or 'FLY' not in self.props(e)):
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
                natural = 'FALL' in self.props(e) and 'FLY' not in self.props(e)
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


def focused_checks(specs):
    from collections import deque
    results=[]
    def check(name,fn):
        fn(); results.append({'name':name,'passed':True})
    def fixture(*,player=(0,1,0),goal=None,pink='',spring=None,blue='',terrain=(),texts=(),entities=()):
        es=[{'id':'player','kind':'COLOR','color':'RED','pos':list(player)}]
        if goal is not None:
            es.append({'id':'goal','kind':'COLOR','color':'PINK','pos':list(goal)})
        if spring is not None:
            es.append({'id':'spring','kind':'COLOR','color':'BLUE','pos':list(spring)})
        es += list(texts)+list(entities)
        return {'id':'fixture','bounds':[-3,6,0,12,-3,3],
                'solidBoxes':[[-3,6,0,0,-3,3],*terrain],
                'fixedRules':['RED IS YOU AND BLOCK AND FALL']+([pink] if pink else [])+([blue] if blue else []),
                'entities':es,'bounceRiseCells':3}
    def word(eid,token,pos,anchored=False):
        return {'id':eid,'kind':'TEXT','token':token,'pos':list(pos),'anchored':anchored}
    def spring_model(**kwargs):
        params=dict(player=(0,4,0),spring=(0,1,0),blue='BLUE IS BLOCK AND JUMP')
        params.update(kwargs); return Model(fixture(**params))

    def all_face_contacts_fail():
        for delta in [*DIRECTIONS.values(),UP,DOWN]:
            m=Model(fixture(player=(0,3,0),goal=add((0,3,0),delta),pink='PINK IS WIN'))
            assert not m.won(),delta
    check('All six face-neighbor positions fail; no adjacency fallback',all_face_contacts_fail)
    def corners_fail():
        for pos in [(1,3,1),(1,4,1),(0,5,0)]:
            assert not Model(fixture(player=(0,3,0),goal=pos,pink='PINK IS WIN')).won()
    check('Edge/corner neighbors and separated projected cells are not victories',corners_fail)
    def enter():
        m=Model(fixture(goal=(1,1,0),pink='PINK IS WIN'));assert m.command('E') and m.won()
        assert m.win_record['cell']==[1,1,0] and m.entity('goal')['pos']==[1,1,0]
        assert not m.command('W');assert m.undo() and not m.won() and m.entity('player')['pos']==[0,1,0]
    check('Enter hollow WIN: immediate same-cell victory; terminal input locked; undo restores pre-win',enter)
    def inactive():
        m=Model(fixture(goal=(1,1,0)));assert m.command('E') and not m.won()
    check('PINK alone is not a target; inactive hollow COLOR can be entered',inactive)
    def self_win():
        s=fixture();s['fixedRules'].append('RED IS WIN');m=Model(s)
        assert not m.won();assert m.command('E') and not m.won()
    check('A single entity carrying YOU and WIN cannot match itself',self_win)
    def no_hidden_unblock():
        m=Model(fixture(goal=(1,1,0),pink='PINK IS BLOCK AND WIN'));before=m.fingerprint()
        assert not m.command('E') and m.fingerprint()==before and not m.won()
        assert 'BLOCK' in m.props(m.entity('goal'))
    check('WIN never cancels BLOCK: solid target remains inaccessible, not an adjacency goal',no_hidden_unblock)
    def reverse_fall():
        m=Model(fixture(goal=(0,4,0),pink='PINK IS WIN AND FALL'));m.settle()
        assert m.won() and m.entity('goal')['pos']==[0,1,0] and m.win_record['cause']=='GravityFall'
    check('Hollow WIN falling into YOU also wins; sensor interaction is bidirectional',reverse_fall)
    def reverse_fly():
        m=Model(fixture(player=(0,4,0),goal=(0,1,0),pink='PINK IS WIN AND FLY'))
        # In-flight fixture only: force target to ascend before player gravity in this targeted unit.
        try:
            for _ in range(3):
                m.shift(m.entity('goal'),UP,'FlyOrRide')
        except _VictoryCommitted:
            pass
        assert m.won() and m.entity('goal')['pos']==[0,4,0]
    check('Hollow WIN ascending into YOU wins at the first shared cell',reverse_fly)
    def jump_transit():
        m=Model(fixture(goal=(0,2,0),pink='PINK IS WIN'))
        assert m.command('J') and m.won() and m.entity('player')['pos']==[0,2,0]
        assert m.win_record['cause']=='JumpApex'
    check('In-place jump entering WIN wins before falling back; no decision-point-only check',jump_transit)
    def fall_transit():
        m=Model(fixture(player=(0,5,0),goal=(0,3,0),pink='PINK IS WIN'));m.settle()
        assert m.won() and m.entity('player')['pos']==[0,3,0]
    check('Player descent stops at the first overlapping WIN grid cell',fall_transit)
    def bounce_transit():
        m=spring_model(player=(0,3,0),goal=(0,4,0),pink='PINK IS WIN')
        m.settle();assert m.won() and m.entity('player')['pos']==[0,4,0] and m.win_record['cause']=='BounceStep'
        assert 'player' not in m.apex  # No phantom completed apex after an earlier terminal step.
    check('Intermediate bounce cell can win; remaining ascent is not executed',bounce_transit)
    def no_goal_support():
        m=Model(fixture(player=(0,3,0),goal=(0,2,0)))
        m.settle();assert m.entity('player')['pos']==[0,1,0] and m.entity('goal')['pos']==[0,2,0]
        assert not any(e['event']=='LandingPress' for e in m.log)
    check('Non-BLOCK COLOR neither supports nor receives landing press from YOU',no_goal_support)
    def no_goal_bump():
        m=Model(fixture(goal=(0,2,0)))
        assert m.command('J') and m.entity('goal')['pos']==[0,2,0]
        assert not any(e['event']=='HeadBump' for e in m.log)
    check('Non-BLOCK COLOR is not head-bumped by YOU',no_goal_bump)
    def sensor_not_win_exception():
        m=Model(fixture(goal=(0,4,0),pink='PINK IS FALL'));m.settle()
        assert not m.won() and m.entity('goal')['pos']==m.entity('player')['pos']==[0,1,0]
    check('Bidirectional hollow/actor overlap applies even without WIN',sensor_not_win_exception)
    def hollow_world_collision():
        m=Model(fixture(player=(3,1,0),goal=(0,5,0),pink='PINK IS FALL',
                        spring=(0,2,0),blue='BLUE IS BLOCK'));m.settle()
        assert m.entity('goal')['pos']==[0,3,0]
    check('A hollow falling goal still stops above terrain/non-YOU BLOCK support',hollow_world_collision)

    def rule_atomic_old_cell():
        words=[word('p','PINK',(2,1,-2),True),word('i','IS',(2,1,-1),True),word('w','WIN',(1,1,0))]
        m=Model(fixture(goal=(0,1,0),texts=words));assert m.command('E')
        assert 'WIN' in m.rules['PINK'] and not m.won() and m.entity('player')['pos']==[1,1,0]
    check('Atomic push + rule activation does not win at the actor cell it just left',rule_atomic_old_cell)
    def rule_atomic_new_cell():
        words=[word('p','PINK',(2,1,-2),True),word('i','IS',(2,1,-1),True),word('w','WIN',(1,1,0))]
        m=Model(fixture(goal=(1,1,0),texts=words));assert m.command('E') and m.won()
        assert m.win_record['cell']==[1,1,0]
    check('Atomic push activates WIN at final occupied cell and triggers immediately',rule_atomic_new_cell)
    def activation_overlap():
        words=[word('p','PINK',(-2,3,0),True),word('i','IS',(-1,3,0),True),
               word('b','BLOCK',(0,2,0)),word('a','AND',(1,3,0),True),word('w','WIN',(2,3,0),True)]
        m=Model(fixture(goal=(0,2,0),texts=words));before=m.fingerprint()
        try:
            m.command('J')
        except AssertionError as exc:
            assert 'solid overlap' in str(exc)
        else:
            raise AssertionError('Illegal materialization not rejected')
        assert m.fingerprint()==before and not m.won() and not m.history
    check('Invalid BLOCK materialization is rejected before WIN; full transaction restored',activation_overlap)
    def atomic_ride_win():
        m=Model(fixture(player=(0,2,0),spring=(0,1,0),blue='BLUE IS BLOCK AND FLY',
                        goal=(0,3,0),pink='PINK IS WIN'))
        m.settle();assert m.won() and m.entity('spring')['pos']==[0,2,0] and m.entity('player')['pos']==[0,3,0]
    check('FLY rider/platform moves atomically and can win during ascent',atomic_ride_win)
    def saved_win():
        m=Model(fixture(goal=(1,1,0),pink='PINK IS WIN'));m.command('E')
        n=Model(fixture(goal=(1,1,0),pink='PINK IS WIN'));n.restore(json.loads(json.dumps(m.snapshot())))
        assert n.fingerprint()==m.fingerprint() and n.won() and not n.command('J')
    check('Won latch and winning IDs/cell survive snapshot JSON round-trip',saved_win)

    def removed_ground_jumps():
        m=Model(fixture());before=m.fingerprint()
        for cmd in LEGACY_JUMPS:
            assert not m.command(cmd) and m.fingerprint()==before and not m.history
        assert m.command('J') and m.entity('player')['pos']==[0,1,0]
        assert not any(e['event']=='DirectionalJump' for e in m.log)
    check('All four directional ground-jump commands rejected; in-place J has no X/Z motion',removed_ground_jumps)
    def no_auto_climb():
        m=Model(fixture(terrain=[[1,1,1,1,0,0]]));before=m.fingerprint()
        assert not m.command('E') and m.fingerprint()==before
        assert m.command('J') and m.entity('player')['pos']==[0,1,0]
    check('Four-way walking does not auto-step or auto-hop onto a one-cell obstacle',no_auto_climb)
    def no_ground_wait():
        m=Model(fixture());before=m.fingerprint();assert not m.command('WAIT') and before==m.fingerprint()
    check('WAIT is only apex descent, not a new grounded action',no_ground_wait)
    def spring_ground():
        m=spring_model(player=(-1,1,0));before=m.fingerprint()
        assert not m.command('E') and m.fingerprint()==before
    check('Horizontal collision with JUMP surface does not bounce either object',spring_ground)
    def basic():
        m=spring_model();m.settle()
        assert m.entity('player')['pos']==[0,5,0] and m.entity('spring')['pos']==[0,1,0]
        assert m.apex['player']['contactPos']==[0,2,0] and m.apex['player']['riseCells']==3
    check('JUMP raises the falling mover three cells from contact; surface stays put',basic)
    def high_drop():
        m=spring_model(player=(0,10,0));m.settle();assert m.entity('player')['pos']==[0,5,0]
    check('Drop height does not change the three-cell rebound',high_drop)
    def stationary():
        m=spring_model(player=(0,2,0));m.settle();assert not m.apex
    check('Standing on JUMP with no downward displacement does not bounce',stationary)
    def walk_then_jump():
        m=spring_model(player=(-1,2,0),terrain=[[-1,-1,1,1,0,0]])
        assert m.command('E') and not m.apex and m.entity('player')['pos']==[0,2,0]
        assert m.command('J') and m.entity('player')['pos']==[0,5,0] and 'player' in m.apex
    check('Walk onto same-height JUMP: no bounce; in-place jump and land: bounce',walk_then_jump)
    def wrong_owner():
        s=fixture(player=(0,4,0),spring=(0,1,0),blue='BLUE IS BLOCK')
        s['fixedRules'].append('RED IS JUMP');m=Model(s);m.settle()
        assert not m.apex and m.entity('player')['pos']==[0,2,0]
    check('JUMP on the falling object alone is insufficient',wrong_owner)
    def no_block_jump():
        m=spring_model(blue='BLUE IS JUMP');m.settle();assert not m.apex and m.entity('player')['pos']==[0,1,0]
    check('JUMP without BLOCK supplies no landing contact',no_block_jump)
    def jump_priority():
        m=spring_model(player=(0,6,0),spring=(0,3,0));m.settle()
        assert m.entity('spring')['pos']==[0,3,0] and m.entity('player')['pos']==[0,7,0]
        assert not any(e['event']=='LandingPress' for e in m.log)
    check('Bounce consumes impact before ordinary downward press',jump_priority)
    def ceiling():
        m=spring_model(terrain=[[0,0,5,5,0,0]]);m.settle()
        assert m.entity('player')['pos']==[0,4,0] and m.apex['player']['riseCells']==2
    check('Ceiling truncates bounce at first blocked grid cell',ceiling)
    def word_ceiling():
        m=spring_model(texts=[word('ceiling','AND',(0,5,0))]);m.settle()
        assert m.entity('ceiling')['pos']==[0,5,0] and m.entity('player')['pos']==[0,4,0]
    check('Passive bounce does not head-bump movable overhead text',word_ceiling)
    def no_apex_jump():
        m=spring_model();m.settle();before=m.fingerprint()
        for cmd in LEGACY_JUMPS:
            assert not m.command(cmd) and before==m.fingerprint()
    check('Directional-jump commands also rejected at apex',no_apex_jump)
    def apex_wait_camera():
        m=spring_model();m.settle();before=m.fingerprint()
        for _ in range(4):
            m.command('CAM+')
        assert before==m.fingerprint() and not m.history
        assert m.command('WAIT') and before==m.fingerprint() and len(m.history)==1
        assert m.undo() and before==m.fingerprint()
        assert m.command('J') and before==m.fingerprint()
    check('Apex camera is timeless; WAIT/Space produces one bounded repeat bounce, not extra height',apex_wait_camera)
    def apex_blocked():
        m=spring_model(terrain=[[1,1,5,5,0,0]]);m.settle();before=m.fingerprint()
        assert not m.command('E') and before==m.fingerprint() and not m.history
    check('Blocked apex direction consumes no motion or turn',apex_blocked)
    def apex_steer():
        m=spring_model(terrain=[[1,1,1,4,0,0]]);m.settle();before=m.fingerprint()
        assert m.command('E') and m.entity('player')['pos']==[1,5,0] and not m.apex
        assert m.undo() and m.fingerprint()==before
    check('One-cell apex steer lands; undo restores motion phase and permits a different choice',apex_steer)
    def nonyou_bounce():
        m=spring_model(player=(-2,1,2),entities=[{'id':'crate','kind':'COLOR','color':'PINK','pos':[0,4,0]}])
        m.spec['fixedRules'].append('PINK IS BLOCK AND FALL');m.refresh();m.settle()
        assert 'crate' in m.apex and m.entity('crate')['pos']==[0,5,0]
        before=m.fingerprint();assert m.command('E') and m.entity('crate')['pos']==[0,5,0]
        assert m.undo() and before==m.fingerprint()
    check('Non-YOU falling BLOCK bounces too; next accepted world action resumes it',nonyou_bounce)
    def saved_apex():
        m=spring_model();m.settle();n=spring_model();n.restore(json.loads(json.dumps(m.snapshot())))
        assert n.fingerprint()==m.fingerprint()
        assert n.command('WAIT') and m.command('WAIT') and n.fingerprint()==m.fingerprint()
    check('Apex and forced descent survive snapshot round-trip',saved_apex)

    def l2_win_disabled():
        m=Model(specs[1])
        for c in ['E','N','E','E','E','S']:
            assert m.command(c)
        assert m.entity('player')['pos']==m.entity('goal')['pos'] and not m.won()
    check('L02 reaching inactive goal without completing the sentence does not win',l2_win_disabled)
    def l3_press():
        m=Model(specs[2]);assert m.command('S')
        assert m.entity('t_fall')['pos']==[2,1,0] and m.entity('bridge')['pos']==[4,1,0]
        assert len([e for e in m.log if e['event']=='LandingPress' and e['id']=='t_fall'])==1
        assert m.command('E') and m.entity('player')['pos']==[3,2,0]
    check('L03 presses FALL once, lowers bridge, and exits at equal height without directional jump',l3_press)
    def l4_socket():
        m=Model(specs[3]);assert m.command('N') and 'JUMP' in m.rules['BLUE']
        assert not m.command('N') and m.entity('t_jump')['pos']==[0,3,2]
        assert m.undo() and 'JUMP' not in m.rules['BLUE']
    check('L04 rule socket stays constrained; undo restores the disconnected JUMP',l4_socket)
    def l5_boarding():
        m=Model(specs[4]);assert m.command('S') and m.command('S')
        assert m.entity('player')['pos']==[3,2,1] and 'FLY' not in m.rules['BLUE']
        assert m.command('S') and m.entity('player')['pos']==[3,5,0] and m.entity('lift_front')['pos']==[3,4,0]
        assert m.command('E') and m.entity('lift_front')['pos']==[3,5,0]
        assert not m.won()  # Next E, into the hollow target, is still necessary.
    check('L05 level boarding, AND activation, atomic passenger headroom, and no adjacent finish',l5_boarding)
    def l6_second():
        m=Model(specs[5])
        for c in specs[5]['solution'][:5]:
            assert m.command(c)
        assert m.entity('player')['pos']==[5,5,0] and not m.apex
        assert m.command('J') and m.entity('player')['pos']==[5,8,0] and 'player' in m.apex
    check('L06 second spring explicitly requires landing after the equal-height walk',l6_second)
    def l6_goal():
        m=Model(specs[5])
        for c in specs[5]['solution'][:11]:
            assert m.command(c)
        assert m.entity('goal')['pos']==[10,8,0] and {'WIN','FALL'} <= m.rules['PINK']
        assert 'BLOCK' not in m.rules['PINK'] and not m.won()
    check('L06 FALL lowers hollow WIN onto the top platform without reintroducing BLOCK',l6_goal)

    def forbidden_subset(spec,predicate,limit=5000):
        initial=Model(spec);queue=deque([initial.snapshot()]);seen={initial.fingerprint()}
        while queue:
            state=queue.popleft()
            for cmd in COMMANDS:
                m=Model(spec);m.restore(state)
                if not m.command(cmd) or predicate(m):
                    continue
                assert not m.won(),('Bypass found',spec['id'],cmd,m.entity('player')['pos'])
                key=m.fingerprint()
                if key in seen:
                    continue
                seen.add(key)
                assert len(seen)<=limit,('Subset cap, not exhausted',spec['id'])
                queue.append(m.snapshot())
        return len(seen)
    subsets=[
        (specs[2],'L03 without BLUE FALL',lambda m:'FALL' in m.rules['BLUE']),
        (specs[3],'L04 without any bounce',lambda m:any(e['event']=='BounceStarted' for e in m.log)),
        (specs[4],'L05 without BLUE FLY',lambda m:'FLY' in m.rules['BLUE']),
        (specs[5],'L06 without any bounce',lambda m:any(e['event']=='BounceStarted' for e in m.log)),
        (specs[5],'L06 without second spring',lambda m:any(e['event']=='BounceStarted' and e['surfaceId']=='spring_high' for e in m.log))]
    for spec,name,predicate in subsets:
        count=forbidden_subset(spec,predicate)
        results.append({'name':name,'passed':True,'reachableStates':count,
                        'scope':'Exhausted only the named restricted transition subset in this reference model; not all solutions or Unity.'})
    return results


def validate():
    specs=levels(); results=[]
    for spec in specs:
        m=Model(spec); start=m.fingerprint()
        assert not m.won(),(spec['id'],'initial victory')
        m.settle(); assert m.fingerprint()==start,(spec['id'],'unstable start')
        assert not any('WIN' in m.props(e) and 'BLOCK' in m.props(e) for e in m.entities), 'Teaching target blocked'
        trace=[]
        for i,cmd in enumerate(spec['solution'],1):
            assert not m.won(),(spec['id'],'early victory',i)
            assert m.command(cmd),(spec['id'],'blocked command',i,cmd,m.entity('player')['pos'])
            trace.append({'step':i,'command':cmd,'player':list(m.entity('player')['pos']),
                          'phase':m.phase(),'won':m.won()})
        assert m.won(),(spec['id'],'no final victory')
        assert m.entity('player')['pos']==m.entity('goal')['pos'],(spec['id'],'not same-cell')
        finals={e['id']:list(e['pos']) for e in m.entities}; events=copy.deepcopy(m.log)
        record=copy.deepcopy(m.win_record)
        while m.undo():
            pass
        assert m.fingerprint()==start,(spec['id'],'undo incomplete')
        for _ in range(4):
            m.command('CAM+')
        assert m.fingerprint()==start,(spec['id'],'camera changed world')
        results.append({'id':spec['id'],'title':spec['title'],'trace':trace,'finalPositions':finals,
                        'winRecord':record,'events':events,'initialStable':True,'initialNotWon':True,
                        'witnessPassed':True,'undoWithMotionPhaseAndWinPassed':True,'cameraInvariantPassed':True})
    return {'mechanicsVersion':'LD-v0.4','scope':'Single-YOU graybox intended paths and stated regressions only. Not Unity validation.',
            'levels':results,'additionalChecks':focused_checks(specs),
            'unverified':['Unity compilation, play mode, build, input, animation, camera and editor',
                          'All possible solutions and arbitrary rule combinations; shortest/unique solutions',
                          'Multi-YOU, moving JUMP surfaces and simultaneous competing fallers',
                          'Experimental edge support and fully general text bounce']}


if __name__ == '__main__':
    report=validate()
    (ROOT/'validation_results.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for r in report['levels']:
        print(r['id'],r['title'],'PASS',r['finalPositions']['player'])
    print('Additional checks:',len(report['additionalChecks']),'PASS')
