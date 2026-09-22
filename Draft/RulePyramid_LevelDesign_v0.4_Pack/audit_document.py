"""Check authored level JSON and reference traces embedded in the v0.4 Markdown.
Run after validate_grayboxes.py. Python standard library only.
"""
import json
import re
from pathlib import Path

ROOT=Path(__file__).resolve().parent

def main():
    doc=(ROOT/'RulePyramid_LevelDesign_v0.4.md').read_text(encoding='utf-8')
    data=json.loads((ROOT/'six_levels_graybox.json').read_text(encoding='utf-8'))
    results=json.loads((ROOT/'validation_results.json').read_text(encoding='utf-8'))
    assert len(data['levels'])==len(results['levels'])==6
    for s,r in zip(data['levels'],results['levels']):
        eid=s['id']; assert eid==r['id']
        text=re.search(rf'<!-- BEGIN_LEVEL_DATA {eid} -->\s*```json\n(.*?)\n```\s*<!-- END_LEVEL_DATA {eid} -->',doc,re.S)
        assert text,('missing embedded data',eid)
        embedded=json.loads(text.group(1))
        assert embedded=={k:s[k] for k in embedded},('embedded data mismatch',eid)
        trace=re.search(rf'<!-- BEGIN_TRACE {eid} -->(.*?)<!-- END_TRACE {eid} -->',doc,re.S)
        assert trace,('missing embedded trace',eid)
        rows=re.findall(r'\| (\d+) \| `([^`]+)` \| `\(([^)]+)\)` \| `([^`]+)` \| (是|否) \|',trace.group(1))
        parsed=[dict(step=int(n),command=c,player=[int(x.strip()) for x in p.split(',')],phase=ph,won=w=='是')
                for n,c,p,ph,w in rows]
        assert parsed==r['trace'],('trace mismatch',eid)
        assert not any(cmd in {'JE','JW','JN','JS'} for cmd in s['solution'])
        assert r['trace'][-1]['won'] and not any(t['won'] for t in r['trace'][:-1])
        assert r['finalPositions']['player']==r['finalPositions']['goal']
    assert len(results['additionalChecks'])==50 and all(c['passed'] for c in results['additionalChecks'])
    assert 'DistinctEntitiesFaceAdjacent6AtDecision' not in doc
    print('PASS: six embedded levels, six trace tables, same-cell final positions, version and 50-check report.')

if __name__=='__main__':
    main()
