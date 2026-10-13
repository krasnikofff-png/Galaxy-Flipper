"""
Рендер каждой постройки отдельно (Workbench, цвета вершин) — быстро посмотреть модели без Unity.
    /Applications/Blender.app/Contents/MacOS/Blender tools/buildings/buildings.blend --background --python tools/buildings/preview.py -- /путь/к/папке
"""
import bpy, math, sys
out = sys.argv[sys.argv.index("--") + 1]
sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sh = sc.display.shading
sh.light = "STUDIO"; sh.color_type = "VERTEX"; sh.show_shadows = True; sh.show_cavity = True
sh.show_specular_highlight = True
sc.render.resolution_x, sc.render.resolution_y = 800, 700
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 50
names = [o.name for o in bpy.data.objects if o.type == "MESH" and o.parent is None]
only = sys.argv[sys.argv.index("--") + 2:]
if only:
    names = [n for n in names if n in only]
for n in names:
    for o in bpy.data.objects:
        if o.type == "MESH": o.hide_render = (o.parent or o).name != n
    ob = bpy.data.objects[n]
    c = ob.location
    zc = 0.0 if n == "mine_gas" else 0.7
    dist = 6.0 if n == "mine_gas" else 5.0
    cam.location = (c.x + dist*0.55, c.y - dist*0.75, c.z + zc + dist*0.45)
    d = (c.x - cam.location.x, c.y - cam.location.y, c.z + zc*0.6 - cam.location.z)
    import mathutils
    cam.rotation_euler = mathutils.Vector(d).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath = f"{out}/{n}.png"
    bpy.ops.render.render(write_still=True)
