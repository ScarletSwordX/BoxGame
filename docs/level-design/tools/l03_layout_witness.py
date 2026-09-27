"""L03 bounded layout witness, NOT the Unity simulator.
Only the authored non-conflicting routes are covered: XZ words, push chains,
independent blocked YOU, gravity, top BOUNCY, HOT/MELT, distinct-instance WIN.
No claim for general simultaneous conflicts, transformations, FLY or J.
"""
import copy
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
DIRS = {'E': (1,0,0), 'W': (-1,0,0), 'N': (0,0,1), 'S': (0,0,-1)}
SUBJECTS = {'ROBOT','ROCK','LAVA','FLAG','WALL','SPRING','CLOUD'}
PROPS = {'YOU','STOP','PUSH','WIN','HOT','MELT','BOUNCY'}
def pos(c): return (c['x'],c['y'],c['z'])
def add(a,b): return tuple(x+y for x,y in zip(a,b))
def cells(box):
    a,b=pos(box['min']),pos(box['max'])
    return {(x,y,z) for x in range(a[0],b[0]+1) for y in range(a[1],b[1]+1) for z in range(a[2],b[2]+1)}

class Witness:
    def __init__(self, level):
        self.level=copy.deepcopy(level)
        self.bounds=cells(level['bounds'])
        self.terrain=set().union(*(cells(b) for b in level['terrain']))
        self.entities={e['id']:dict(e,p=pos(e['cell'])) for e in copy.deepcopy(level['entities'])}
        self.apex=set(); self.events=[]; self.winner=None
        self.refresh()
    def refresh(self):
        words={e['p']:e['token'] for e in self.entities.values() if e['kind']=='Text'}
        self.rules={s:set() for s in SUBJECTS}
        for origin,token in words.items():
            if token not in SUBJECTS: continue
            for direction in [(1,0,0),(0,0,-1)]:
                line=[]; p=origin
                while p in words:
                    line.append(words[p]);p=add(p,direction)
                subjects=[token];i=1
                while i+1<len(line) and line[i]=='AND' and line[i+1] in SUBJECTS:
                    subjects.append(line[i+1]);i+=2
                if i+1>=len(line) or line[i]!='IS' or line[i+1] not in PROPS:continue
                properties=[line[i+1]];i+=2
                while i+1<len(line) and line[i]=='AND' and line[i+1] in PROPS:
                    properties.append(line[i+1]);i+=2
                for s in subjects:self.rules[s].update(properties)
    def props(self,e): return self.rules.get(e['subject'],set()) if e['kind']=='Object' else set()
    def solid(self,e): return e['kind']=='Text' or bool(self.props(e)&{'STOP','PUSH','YOU'})
    def movable(self,e):return e['kind']=='Text' or ('PUSH' in self.props(e) and 'YOU' not in self.props(e))
    def occupants(self,p,exclude=None):return [e for e in self.entities.values() if e['p']==p and e['id']!=exclude]
    def blocker(self,p,mover):
        if p not in self.bounds or p in self.terrain:return 'terrain'
        return next((e for e in self.occupants(p,mover['id']) if self.solid(mover) and self.solid(e)),None)
    def contact(self,e):
        if e['id'] not in self.entities:return
        if 'MELT' in self.props(e) and any('HOT' in self.props(o) for o in self.occupants(e['p'],e['id'])):
            self.events.append({'kind':'destroyed','id':e['id']});del self.entities[e['id']];self.apex.discard(e['id']);return
        if 'YOU' in self.props(e):
            goal=next((o for o in self.occupants(e['p'],e['id']) if 'WIN' in self.props(o)),None)
            if goal:self.winner=(e['id'],goal['id'])
    def move(self,e,direction,steer=False):
        dest=add(e['p'],direction);hit=self.blocker(dest,e)
        if hit=='terrain':return False
        chain=[]
        while hit:
            if steer or not self.movable(hit):return False
            chain.append(hit);q=add(hit['p'],direction);hit=self.blocker(q,hit)
            if hit=='terrain':return False
        for item in reversed(chain):item['p']=add(item['p'],direction)
        e['p']=dest
        if chain:self.events.append({'kind':'pushed','ids':[i['id'] for i in chain]})
        self.refresh();self.contact(e);return True
    def settle(self,e):
        if e['id'] in self.apex or e['id'] not in self.entities:return
        descended=False
        for _ in range(64):
            below=add(e['p'],(0,-1,0))
            bounce=next((o for o in self.occupants(below,e['id']) if 'BOUNCY' in self.props(o)),None)
            if descended and bounce:
                contact=e['p'];rise=0
                for _ in range(3):
                    q=add(e['p'],(0,1,0))
                    if self.blocker(q,e):break
                    e['p']=q;rise+=1
                if rise:self.apex.add(e['id'])
                self.events.append({'kind':'bounce','id':e['id'],'surface':bounce['id'],'contact':contact,'apex':e['p']})
                return
            if self.blocker(below,e):return
            e['p']=below;descended=True;self.contact(e)
            if e['id'] not in self.entities:return
        raise AssertionError('Fall did not settle')
    def command(self,cmd,reverse=False):
        assert cmd in DIRS, 'Witness only covers cardinal route inputs'
        # Explicit assumption: newly activated YOU begins receiving input next command.
        active=sorted(e['id'] for e in self.entities.values() if 'YOU' in self.props(e))
        if reverse:active.reverse()
        before=set(self.apex);self.apex.clear();events_start=len(self.events)
        for id in active:
            if id in self.entities:self.move(self.entities[id],DIRS[cmd],steer=id in before)
        for e in list(self.entities.values()):self.settle(e)
        self.refresh()
        return {'command':cmd,'actors':{e['id']:e['p'] for e in self.entities.values() if 'YOU' in self.props(e)},'rules':{k:sorted(v) for k,v in self.rules.items() if v},'events':self.events[events_start:],'winner':self.winner}
    def signature(self):return sorted((e['id'],e['p']) for e in self.entities.values()),sorted(self.apex),self.winner

def static_checks(level):
    bounds=cells(level['bounds']);terrain=set().union(*(cells(b) for b in level['terrain']))
    assert terrain<=bounds
    ids=[e['id'] for e in level['entities']];positions=[pos(e['cell']) for e in level['entities']]
    assert len(ids)==len(set(ids));assert len(positions)==len(set(positions))
    assert all(p in bounds and p not in terrain for p in positions)
    assert all(add(p,(0,-1,0)) in terrain for p in positions), 'Every authored initial entity has terrain support'
    assert all(not e['anchored'] for e in level['entities'])
    assert 'stagePlan' not in level and 'fixedRules' not in level
    return {'uniqueIds':True,'distinctInitialCells':True,'insideBounds':True,'initialTerrainSupport':True,'ordinaryMovableWords':True}

def run():
    results=[]
    for stage in [2,3]:
        path=ROOT/f'Assets/_RulePyramid/Content/LevelDrafts/L3P{stage}.json'
        level=json.loads(path.read_text(encoding='utf-8'));checks=static_checks(level)
        world=Witness(level);reverse=Witness(level);trace=[];order_independent=True
        for command in level['referenceSolutions'][0]['commands']:
            trace.append(world.command(command));reverse.command(command,reverse=True)
            order_independent &= world.signature()==reverse.signature()
        expected=(level['referenceSolutions'][0]['mustControlAtWin'],'goal')
        assert world.winner==expected,(stage,world.winner,trace[-1])
        assert order_independent
        assert 'HOT' in world.rules['LAVA'] and 'BOUNCY' in world.rules['LAVA']
        assert any(e['kind']=='bounce' for e in world.events)
        assert not any(e['kind']=='destroyed' for e in world.events)
        negative=copy.deepcopy(level);negative['entities']=[e for e in negative['entities'] if e['token']!='BOUNCY']
        missing_bounce=Witness(negative)
        for cmd in level['referenceSolutions'][0]['commands']:missing_bounce.command(cmd)
        assert missing_bounce.winner is None
        no_and=None
        if stage==3:
            assert len(trace[-1]['actors'])==2
            negative=copy.deepcopy(level);negative['entities']=[e for e in negative['entities'] if e['token']!='AND']
            missing_and=Witness(negative)
            for cmd in level['referenceSolutions'][0]['commands']:missing_and.command(cmd)
            no_and=missing_and.winner is None;assert no_and
        results.append({'id':level['id'],'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'dimensions':tuple(level['bounds']['max'][a]-level['bounds']['min'][a]+1 for a in ['x','y','z']),'staticChecks':checks,'witnessRouteReachedGoal':True,'turns':len(trace),'winner':world.winner,'reverseActorIterationSame':order_independent,'withoutBouncyReferenceFails':True,'withoutAndReferenceFails':no_and,'trace':trace})
    output={'tool':'l03_layout_witness.py','scope':'Bounded Python design witness; NOT Unity runtime validation or exhaustive search','assumptions':['New YOU receives input starting next command','Separate actor regions; no shared destination/push conflicts','Cardinal route only; J, FLY, HOVER and transformations not modelled','Top bounce intercepts descent above even a non-solid LAVA before entry'],'results':results}
    path=ROOT/'docs/level-design/evidence/L03-layout-witness.json'
    path.write_bytes((json.dumps(output,ensure_ascii=False,indent=2)+'\n').replace('\n','\r\n').encode('utf-8'))
    print(json.dumps([{'id':r['id'],'turns':r['turns'],'winner':r['winner'],'actorOrderIndependent':r['reverseActorIterationSame']} for r in results],ensure_ascii=False))

if __name__=='__main__':run()
