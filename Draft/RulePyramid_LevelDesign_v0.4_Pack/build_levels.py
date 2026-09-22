"""Graybox data authoring helper. Uses only the Python standard library."""
from __future__ import annotations
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent

def cube(eid, color, pos):
    return {'id': eid, 'kind': 'COLOR', 'color': color, 'pos': list(pos), 'anchored': False}
def word(eid, token, pos, anchored=False):
    return {'id': eid, 'kind': 'TEXT', 'token': token, 'pos': list(pos), 'anchored': anchored}

def build_levels():
    # LD-v0.4: same-cell victory; no directional ground jump; standalone data authoring.
    common = ['RED IS YOU', 'RED IS BLOCK', 'RED IS FALL', 'PINK IS WIN']
    out = [
        dict(id='L01', title='填入那个位置', bounds=[0,5,0,5,0,3], fixedRules=common,
             solidBoxes=[[0,5,0,0,0,3],[2,2,1,3,0,2]],
             entities=[cube('player','RED',(0,1,0)),cube('goal','PINK',(4,1,0))],
             solution=['CAM+','N','N','N','E','E','E','S','S','S','E']),
        dict(id='L02', title='把一句话顶完整', bounds=[0,5,0,5,0,1],
             fixedRules=common[:3],
             solidBoxes=[[0,5,0,0,0,1],[0,2,1,1,0,0]],
             entities=[cube('player','RED',(1,2,0)),cube('goal','PINK',(5,1,0)),
                       word('t_pink','PINK',(1,3,0),True),word('t_is','IS',(2,3,0),True),word('t_win','WIN',(2,2,0))],
             solution=['E','N','E','S','J','E','E']),
        dict(id='L03', title='落地写下重力', bounds=[0,6,0,6,0,2],fixedRules=common+['BLUE IS BLOCK'],
             solidBoxes=[[0,6,0,0,0,2],[2,2,1,3,1,1],[3,3,1,1,0,0],[1,1,2,2,0,0],[5,5,1,1,0,0]],
             entities=[cube('player','RED',(2,4,1)),cube('goal','PINK',(5,2,0)),cube('bridge','BLUE',(4,4,0)),
                       word('t_blue','BLUE',(0,1,0),True),word('t_is','IS',(1,1,0),True),word('t_fall','FALL',(2,2,0))],
             solution=['S','E','E','E']),
        dict(id='L04',title='落下去，弹上来',bounds=[-2,5,0,8,-1,3],fixedRules=common+['BLUE IS BLOCK'],
             solidBoxes=[[-2,5,0,0,-1,3],[-2,1,1,2,0,3],[3,5,1,4,0,0],
                         [2,5,1,8,-1,-1],[2,5,1,8,1,1],
                         [-1,-1,3,3,1,1],[1,1,3,3,1,2],[0,0,3,3,3,3],[0,0,4,4,2,2]],
             entities=[cube('player','RED',(0,3,0)),cube('goal','PINK',(5,5,0)),cube('spring','BLUE',(2,1,0)),
                       word('t_blue','BLUE',(-2,3,2),True),word('t_is','IS',(-1,3,2),True),word('t_jump','JUMP',(0,3,1))],
             solution=['N','S','E','E','E','E','E']),
        dict(id='L05',title='让脚下成为电梯',bounds=[0,5,0,7,-1,3],fixedRules=common,
             solidBoxes=[[0,5,0,0,-1,3],[4,5,1,4,0,0],[3,3,6,6,0,1],[3,3,1,1,2,3]],
             entities=[cube('player','RED',(3,2,3)),cube('goal','PINK',(5,5,0)),
                       cube('lift_front','BLUE',(3,1,0)),cube('lift_rear','BLUE',(3,1,1)),
                       word('t_blue','BLUE',(0,2,-1),True),word('t_is','IS',(1,2,-1),True),
                       word('t_block','BLOCK',(2,2,-1),True),word('t_and','AND',(3,2,0)),word('t_fly','FLY',(4,2,-1),True)],
             solution=['S','S','S','E','E']),
        dict(id='L06',title='借两次落差，迎回塔冠',bounds=[0,10,0,14,0,1],fixedRules=common+['BLUE IS BLOCK AND JUMP'],
             solidBoxes=[[0,10,0,0,0,1],[0,1,1,2,0,0],[0,5,1,14,1,1],
                         [3,4,1,4,0,0],[5,5,1,3,0,0],[6,10,1,7,0,1]],
             entities=[cube('player','RED',(0,3,0)),cube('goal','PINK',(10,13,0)),
                       cube('spring_low','BLUE',(2,1,0)),cube('spring_high','BLUE',(5,4,0)),
                       word('t_pink','PINK',(6,10,1),True),word('t_is','IS',(7,10,1),True),word('t_fall','FALL',(8,9,1))],
             solution=['E','E','E','E','E','J','E','E','E','N','J','S','E','E']),
    ]
    for level in out:
        level.update(mechanicsVersion='LD-v0.4', supportMode='StrictBelow',
                     jumpMode='LandingBounce3', bounceRiseCells=3,
                     decisionMode='GroundedOrBounceApex', ruleAxes=['PositiveX','PositiveZ'],
                     winMode='DistinctEntitiesSameCell', winCheckMode='AfterAtomicLogicChange',
                     actionMode='FourWayMoveInPlaceJumpApexSteer',
                     schemaVersion=4,
                     camera={'initialSlot':0,'pitchDegrees':35.264,'yawDegrees':[45,135,225,315],
                             'inputMode':'CameraRelativeGrid'})
    return out

if __name__ == '__main__':
    data = {'schema':'DesignGraybox-v0.4','schemaVersion':4,'mechanicsVersion':'LD-v0.4',
            'warning':'Design witness data, not a Unity project. fixedRules must be visible and traceable. Read the Markdown for collision and apex semantics.',
            'levels':build_levels()}
    (ROOT/'six_levels_graybox.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
