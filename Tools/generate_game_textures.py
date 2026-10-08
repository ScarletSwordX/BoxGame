"""Generate small repeatable material maps without external image assets."""
from pathlib import Path
import numpy as np
from PIL import Image
ROOT=Path(__file__).resolve().parents[1]/"Assets/_RulePyramid/Art/Objects"
ROOT.mkdir(parents=True,exist_ok=True)
a=np.zeros((32,128,3),dtype=np.uint8)
for i,c in enumerate([(255,255,255),(42,49,63),(177,184,194),(240,245,246)]): a[:,i*32:(i+1)*32]=c
Image.fromarray(a).save(ROOT/"IdentityAtlas.png")
n=256;y,x=np.mgrid[0:n,0:n]/n
rng=np.random.default_rng(817);seeds=rng.random((26,2));dist=[]
for sx,sy in seeds:
 dx=np.minimum(abs(x-sx),1-abs(x-sx));dy=np.minimum(abs(y-sy),1-abs(y-sy))
 dist.append(dx*dx+dy*dy)
d=np.sort(np.stack(dist),axis=0)
crack=np.clip(1-(d[1]-d[0])/.006,0,1)**2
noise=(np.sin(x*55+y*13)+np.cos(y*69-x*9))*2
base=np.stack([38+noise,27+noise,32+noise],axis=2)
glow=np.stack([crack*255,crack*95,crack*12],axis=2)
Image.fromarray(np.clip(base*(1-crack[:,:,None])+glow,0,255).astype(np.uint8)).save(ROOT/"LavaCrust.png")
Image.fromarray(glow.astype(np.uint8)).save(ROOT/"LavaEmission.png")
