"""Blender 4.2: reproducible low-poly game meshes, one mesh/material per identity."""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Assets/_RulePyramid/Art/Objects"
SOURCE = ROOT / "Art/SourceModels"
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
parts = []

def finish(obj, swatch=0):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if not obj.data.uv_layers: obj.data.uv_layers.new(name="UVMap")
    for uv in obj.data.uv_layers.active.data:
        uv.uv = ((swatch + .5) / 4, .5)
    parts.append(obj)
    return obj

def box(loc, size, bevel=.035, swatch=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod=o.modifiers.new("Small bevel", 'BEVEL'); mod.width=bevel; mod.segments=1
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(o,swatch)

def sphere(loc,size,swatch=0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, location=loc)
    o=bpy.context.object;o.scale=size
    return finish(o,swatch)

def cylinder(loc,radius,depth,swatch=0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=depth, location=loc)
    return finish(bpy.context.object,swatch)

def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    if len(parts) > 1: bpy.ops.object.join()
    o=bpy.context.object;o.name=name;o.data.name=name+"_Mesh"
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    o.data.materials.clear();o.data.materials.append(mat)
    for poly in o.data.polygons: poly.material_index=0
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+".fbx")), use_selection=True,
        object_types={'MESH'}, axis_forward='-Z', axis_up='Y', global_scale=1,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_anim=False,
        use_mesh_modifiers=True, add_leaf_bones=False)
    o.hide_set(True);parts.clear()
    return o

mat=bpy.data.materials.new("Identity atlas");mat.diffuse_color=(.7,.7,.7,1)
# Robot: front points Blender -Y, which exports toward Unity's viewer.
# Cut the front upper edge into a visor plane visible from the steep game camera.
hp=[(-.265,-.015),(.265,-.015),(.265,.28),(.04,.475),(-.265,.475)]
hv=[(x,y,z) for x in [-.31,.31] for y,z in hp]
hf=[(4,3,2,1,0),(5,6,7,8,9)]
for i in range(5): hf.append((i,(i+1)%5,(i+1)%5+5,i+5))
hm=bpy.data.meshes.new("Visor head");hm.from_pydata(hv,[],hf);hm.update()
ho=bpy.data.objects.new("Visor head",hm);bpy.context.collection.objects.link(ho);finish(ho)
box((0,0,-.17),(.48,.39,.30),.045)
for x in [-.14,.14]:
    box((x,0,-.405),(.18,.35,.19),.02,2)
for x in [-.34,.34]: box((x,0,-.12),(.13,.27,.34),.035,2)
box((0,.17,.38),(.48,.025,.27),.012,1).rotation_euler.x = math.radians(49)
for x in [-.13,.13]: box((x,.12,.44),(.09,.02,.09),.008,3).rotation_euler.x = math.radians(49)
box((0,.25,.34),(.19,.018,.028),.005,3).rotation_euler.x = math.radians(49)
export("ROBOT")

bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1)
o=bpy.context.object;random.seed(49)
for v in o.data.vertices:
    v.co *= random.uniform(.83,1.07)
    v.co.z=max(v.co.z,-.68)
mins=[min(v.co[i] for v in o.data.vertices) for i in range(3)]
maxs=[max(v.co[i] for v in o.data.vertices) for i in range(3)]
for v in o.data.vertices:
    v.co.x=(v.co.x-(mins[0]+maxs[0])/2)/(maxs[0]-mins[0])*.94
    v.co.y=(v.co.y-(mins[1]+maxs[1])/2)/(maxs[1]-mins[1])*.9
    v.co.z=(v.co.z-mins[2])/(maxs[2]-mins[2])-.5
finish(o);export("ROCK")

sphere((0,0,.04),(.38,.34,.43))
sphere((-.24,0,-.10),(.25,.28,.32))
sphere((.23,.02,-.09),(.24,.27,.32))
sphere((0,-.19,-.12),(.30,.22,.27))
export("CLOUD")

cylinder((0,0,-.455),.43,.09,2)
# Open annular top plate keeps the real coil visible from the fixed 75-degree camera.
rv=[];rf=[]
for z in [.41,.50]:
    for radius in [.35,.43]:
        for i in range(16):
            a=i/16*math.tau;rv.append((radius*math.cos(a),radius*math.sin(a),z))
for i in range(16):
    j=(i+1)%16
    rf.extend([(i,j,j+16,i+16),(i+32,i+48,j+48,j+32),
               (i,i+32,j+32,j),(i+16,j+16,j+48,i+48)])
rm=bpy.data.meshes.new("Open top plate");rm.from_pydata(rv,[],rf);rm.update()
ro=bpy.data.objects.new("Open top plate",rm);bpy.context.collection.objects.link(ro);finish(ro,2)
verts=[];faces=[];steps=200;sides=6;loops=4.5
for i in range(steps+1):
    a=i/steps*math.tau*loops
    c=Vector((.31*math.cos(a),.31*math.sin(a),-.365+i/steps*.73))
    radial=Vector((math.cos(a),math.sin(a),0));up=Vector((0,0,1))
    for j in range(sides):
        b=j/sides*math.tau
        verts.append(c+.051*(math.cos(b)*radial+math.sin(b)*up))
for i in range(steps):
    for j in range(sides):
        k=i*sides+j;n=i*sides+(j+1)%sides
        faces.append((k,n,n+sides,k+sides))
mesh=bpy.data.meshes.new("Coil");mesh.from_pydata(verts,[],faces);mesh.update()
o=bpy.data.objects.new("Coil",mesh);bpy.context.collection.objects.link(o);finish(o)
export("SPRING")

cylinder((-.20,0,-.44),.25,.12,2)
cylinder((-.20,0,0),.045,.92,2)
box((.065,0,.27),(.50,.045,.31),.015).rotation_euler.x = math.radians(40)
export("FLAG")

box((0,0,0),(.18,.18,1),.025,2)
for z in [-.20,.22]:
    box((0,0,z),(.96,.13,.13),.025)
    box((0,0,z),(.13,.96,.13),.025)
export("WALL")

lava=box((0,0,0),(.96,.96,1),.045)
# Planar UV coordinates on each face, suitable for seamlessly scrolling cracks.
uvs=lava.data.uv_layers.active.data
for poly in lava.data.polygons:
    axis=max(range(3),key=lambda i:abs(poly.normal[i]))
    axes=[i for i in range(3) if i!=axis]
    for loop in poly.loop_indices:
        co=lava.data.vertices[lava.data.loops[loop].vertex_index].co
        uvs[loop].uv=(co[axes[0]]+.5,co[axes[1]]+.5)
export("LAVA")

for o in bpy.context.scene.objects: o.hide_set(False)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/"RuleWorkshop_Objects.blend"))
print("Exported seven game identities")
