"""Report actual terrain occupancy retention and draw an authoring plan, not a Game View."""
import hashlib
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'docs/level-design/evidence'
AXES = ('x', 'y', 'z')

def cells(box):
    return {(x,y,z) for x in range(box['min']['x'],box['max']['x']+1)
            for y in range(box['min']['y'],box['max']['y']+1)
            for z in range(box['min']['z'],box['max']['z']+1)}

def write(path, content):
    path.write_bytes(content.replace('\r\n','\n').replace('\n','\r\n').encode('utf-8'))

def run():
    maps=[]; terrain=[]; rows=[]; differences=[]
    for i in (1,2,3):
        path=ROOT/f'Assets/_RulePyramid/Content/LevelDrafts/L3P{i}.json'
        data=json.loads(path.read_text(encoding='utf-8')); maps.append(data)
        occupied=set().union(*(cells(b) for b in data['terrain']));terrain.append(occupied)
        assert all(data['bounds']['max'][a]-data['bounds']['min'][a]+1 == n for a,n in zip(AXES,(18,6,12)))
        rows.append({'id':data['id'],'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
                     'terrainCells':len(occupied),'nonFloorCells':sum(y>0 for x,y,z in occupied)})
    for i in (0,1):
        old,new=terrain[i:i+2];common=old&new
        elevated={c for c in old if c[1]>0}
        differences.append({'from':maps[i]['id'],'to':maps[i+1]['id'],
            'retained':len(common),'previousTotal':len(old),'retentionPercent':round(100*len(common)/len(old),4),
            'retainedNonFloor':len(elevated&new),'previousNonFloor':len(elevated),
            'nonFloorRetentionPercent':round(100*len(elevated&new)/len(elevated),4),
            'addedCells':sorted(new-old),'removedCells':sorted(old-new)})
    assert terrain[1]==terrain[2], 'P2 and P3 must share every terrain cell'
    assert differences[0]['nonFloorRetentionPercent']>=99
    for id in ('lava_01','goal'):
        a=next(e['cell'] for e in maps[1]['entities'] if e['id']==id)
        b=next(e['cell'] for e in maps[2]['entities'] if e['id']==id)
        assert a==b
    report={'scope':'Terrain occupancy, not terrain-box IDs or object WALL counts; non-floor excludes Y=0.',
            'maps':rows,'transitions':differences,'p2p3IdenticalTerrain':True,'p2p3SameLavaAndGoalCoordinates':True}
    write(OUT/'L03-inheritance-geometry.json',json.dumps(report,ensure_ascii=False,indent=2)+'\n')
    svg=['<svg xmlns="http://www.w3.org/2000/svg" width="1490" height="510" viewBox="0 0 1490 510">',
         '<rect width="1490" height="510" fill="#fbf8f1"/>',
         '<g font-family="Microsoft YaHei, sans-serif" fill="#293246">',
         '<text x="24" y="30" font-size="20">L03：同一骨架，三次重布</text>',
         '<text x="24" y="53" font-size="12">作者俯视示意（Z 向上，Y 为高度），不是游戏截图。三阶段均为 18×6×12。</text>']
    titles=['P1  共用 IS · 门洞与低旗台','P2  同一旗台升高 · 拆桥引入岩浆','P3  地形完全保留 · 回到左室双控']
    notes=['32 步；旗帜 Y3，桥面身体格 Y3','16 步；旗帜 Y5，岩浆 Y1','20 步；ROBOT 左室，ROCK 右室']
    for i,(data,occupied) in enumerate(zip(maps,terrain)):
        ox=35+i*490;oy=112;size=24
        svg.append(f'<text x="{ox}" y="85" font-size="16">{titles[i]}</text>')
        heights={(x,z):max(c[1] for c in occupied if c[0]==x and c[2]==z) for x in range(18) for z in range(12)}
        changed=set() if i==0 else {(x,z) for x,y,z in terrain[i]^terrain[i-1]}
        for (x,z),height in heights.items():
            px=ox+x*size;py=oy+(11-z)*size
            fill=['#f1ece0','#e0e7e6','#d8e4e8','#aebcc9','#bac7dd','#8695aa'][height]
            stroke='#d27526' if (x,z) in changed else '#fff'
            svg.append(f'<rect x="{px}" y="{py}" width="24" height="24" fill="{fill}" stroke="{stroke}" stroke-width="{2 if (x,z) in changed else 1}"/>')
        for e in data['entities']:
            x,y,z=(e['cell'][a] for a in AXES);cx=ox+x*size+12;cy=oy+(11-z)*size+12
            if e['kind']=='Text':
                label=e['token'];svg.append(f'<rect x="{cx-11}" y="{cy-9}" width="22" height="18" rx="2" fill="#fff5d8" stroke="#4f6381"/>')
                svg.append(f'<text x="{cx}" y="{cy+2.8}" text-anchor="middle" font-size="{5.5 if len(label)>5 else 6.8}">{html.escape(label)}</text>')
            else:
                label={'ROBOT':'R','ROCK':'K','FLAG':'F','LAVA':'L','WALL':'w'}[e['subject']]
                fill={'ROBOT':'#ca6660','ROCK':'#7099c8','FLAG':'#dc91ad','LAVA':'#f07936','WALL':'#73818b'}[e['subject']]
                svg.append(f'<circle cx="{cx}" cy="{cy}" r="9" fill="{fill}"/><text x="{cx}" y="{cy+4}" text-anchor="middle" font-size="11" fill="white">{label}</text>')
        for x in range(18):svg.append(f'<text x="{ox+x*size+12}" y="{oy-6}" text-anchor="middle" font-size="8">{x}</text>')
        for z in range(12):svg.append(f'<text x="{ox-7}" y="{oy+(11-z)*size+16}" text-anchor="end" font-size="8">{z}</text>')
        svg.append(f'<text x="{ox}" y="425" font-size="12">{notes[i]}</text>')
    svg+=['<text x="35" y="460" font-size="13">P1→P2：非底板地形保留 383/385 = 99.48%　｜　P2→P3：405/405 = 100%</text>',
          '<text x="35" y="486" font-size="12">橙框 = 与前阶段不同的地形列；R 机器人 / K 岩块 / F 旗帜 / L 岩浆 / w 墙。词牌、角色按阶段初态重布。</text>','</g></svg>']
    write(OUT/'L03-inheritance-overview.svg','\n'.join(svg)+'\n')
    print(json.dumps(differences,ensure_ascii=True))

if __name__=='__main__':run()
