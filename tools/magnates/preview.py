"""
Быстрый просмотр бюстов магнатов в Blender (без Unity): портрет каждого в PNG.

    /Applications/Blender.app/Contents/MacOS/Blender tools/magnates/magnates.blend --background --python tools/magnates/preview.py -- путь/папка

Цвет — из вершин; акцентные детали красятся в цвет магната, светящиеся — светятся. Настоящий вид — в Unity.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

ACCENTS = {
    "magnate_hugo": (1.0, 0.3, 0.27), "magnate_zizi": (1.0, 0.6, 0.15), "magnate_matilda": (0.8, 0.6, 1.0),
    "magnate_nova": (1.0, 0.35, 0.75), "magnate_orpheus": (0.35, 0.65, 1.0), "magnate_xenon": (0.2, 0.85, 0.85),
    "magnate_shadow": (0.6, 0.6, 0.7), "magnate_host": (0.95, 0.77, 0.35),
}


def material():
    m = bpy.data.materials.new("вершины")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    bsdf.inputs["Specular"].default_value = 0.4
    return m


def main():
    out = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "/tmp/magnates"
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 480, 600
    scene.render.film_transparent = False
    scene.world = bpy.data.worlds.new("мир")
    scene.world.color = (0.02, 0.025, 0.04)
    scene.eevee.use_soft_shadows = True
    scene.eevee.taa_render_samples = 32
    mat = material()

    cam = bpy.data.objects.new("камера", bpy.data.cameras.new("камера"))
    scene.collection.objects.link(cam)
    cam.data.lens = 70
    scene.camera = cam
    key = bpy.data.objects.new("ключ", bpy.data.lights.new("ключ", "AREA"))
    key.data.energy = 600
    key.data.size = 2
    fill = bpy.data.objects.new("заполнение", bpy.data.lights.new("заполнение", "AREA"))
    fill.data.energy = 180
    fill.data.size = 3
    fill.data.color = (0.7, 0.8, 1.0)
    rim = bpy.data.objects.new("контур", bpy.data.lights.new("контур", "AREA"))
    rim.data.energy = 700
    rim.data.size = 1
    for l in (key, fill, rim):
        scene.collection.objects.link(l)

    models = [o for o in scene.objects if o.name.startswith("magnate_") and "__" not in o.name]
    for ob in models:
        for part in [ob] + list(ob.children):
            if part.type == "MESH":
                part.data.materials.clear()
                part.data.materials.append(mat)
    for ob in models:
        for other in models:
            hide = other != ob
            other.hide_render = hide
            for c in other.children:
                c.hide_render = hide
        base = ob.location
        target = base + Vector((0, 0, 1.25))
        cam.location = base + Vector((0.55, -4.6, 1.55))
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        key.location = base + Vector((-2.2, -2.6, 3.0))
        key.rotation_euler = (target - key.location).to_track_quat("-Z", "Y").to_euler()
        fill.location = base + Vector((2.8, -2.5, 1.4))
        fill.rotation_euler = (target - fill.location).to_track_quat("-Z", "Y").to_euler()
        rim.location = base + Vector((1.2, 2.5, 2.6))
        rim.rotation_euler = (target - rim.location).to_track_quat("-Z", "Y").to_euler()
        rim.data.color = ACCENTS.get(ob.name, (1, 1, 1))
        scene.render.filepath = os.path.join(out, ob.name + ".png")
        bpy.ops.render.render(write_still=True)
        print("Портрет:", scene.render.filepath)


main()
