"""
Постройки на планетах: лоу-поли модели для 3D-окна планеты.

Запуск (создаёт модели, сохраняет buildings.blend рядом и выгружает FBX в Unity):
    /Applications/Blender.app/Contents/MacOS/Blender --background --python tools/buildings/make_buildings.py

Если поправили модели руками в buildings.blend, выгрузить только FBX:
    /Applications/Blender.app/Contents/MacOS/Blender tools/buildings/buildings.blend --background --python tools/buildings/make_buildings.py -- --export-only

Цвет — в цвете вершин (слой «Col»), материал в Unity один на всё. Альфа цвета — роль детали:
    1   — обычная деталь;
    0.5 — акцент: в Unity красится в цвет владельца (у экстрактора — в цвет ресурса);
    0   — свет: окна и огни, светятся и на ночной стороне.
Ось Z — вверх, основание модели — на z = 0, высота моделей около 2 единиц.
"""
import math
import os
import sys

import bmesh
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT_DIR = os.path.join(REPO, "unity", "Assets", "Resources", "Buildings")
BLEND = os.path.join(HERE, "buildings.blend")

NORMAL, ACCENT, LIGHT = 1.0, 0.5, 0.0

# Палитра: светлый металл, тёмный металл, бетон, камень, сигнальный жёлтый, стекло, тёплый свет.
METAL = (0.72, 0.74, 0.78)
DARK = (0.30, 0.32, 0.36)
CONCRETE = (0.58, 0.56, 0.52)
STONE = (0.62, 0.55, 0.45)
HAZARD = (0.95, 0.72, 0.12)
GLASS = (0.55, 0.78, 0.92)
WARM = (1.0, 0.82, 0.45)
RED = (1.0, 0.22, 0.15)
CYAN = (0.35, 1.0, 0.95)
WHITE = (1.0, 1.0, 1.0)

MODELS = ["mine_rig", "mine_gas", "mine_build", "extractor", "settlement", "school", "guard_tower", "ruins"]


# ----------------------------------------------------------------------
# Детали
# ----------------------------------------------------------------------

def paint(ob, color, role=NORMAL):
    me = ob.data
    layer = me.vertex_colors.get("Col") or me.vertex_colors.new(name="Col")
    for d in layer.data:
        d.color = (color[0], color[1], color[2], role)
    return ob


def active():
    return bpy.context.view_layer.objects.active


def box(size, loc, color, role=NORMAL, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    ob = active()
    ob.scale = size
    return paint(ob, color, role)


def cyl(r, h, loc, color, role=NORMAL, verts=8, r2=None, rot=(0, 0, 0)):
    """Цилиндр (или усечённый конус, если задан r2) с основанием на loc."""
    x, y, z = loc
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=h, location=(x, y, z + h / 2), rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=h, location=(x, y, z + h / 2), rotation=rot)
    return paint(active(), color, role)


def dome(r, loc, color, role=NORMAL, segments=10, rings=5):
    """Полусфера на loc."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings * 2, radius=r, location=loc)
    ob = active()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
    bm.to_mesh(ob.data)
    bm.free()
    return paint(ob, color, role)


def ball(r, loc, color, role=NORMAL, segments=8, rings=6):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=r, location=loc)
    return paint(active(), color, role)


def lattice(r_bottom, r_top, h, loc, color, levels=4, thickness=0.05):
    """Решётчатая башня: четырёхгранная пирамида с поясами, превращённая в каркас."""
    x, y, z = loc
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=r_bottom, radius2=r_top, depth=h, location=(x, y, z + h / 2), end_fill_type="NOTHING")
    ob = active()
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    side_edges = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-4]
    bmesh.ops.subdivide_edges(bm, edges=side_edges, cuts=levels - 1, use_grid_fill=True)
    bm.to_mesh(ob.data)
    bm.free()
    mod = ob.modifiers.new("Каркас", "WIREFRAME")
    mod.thickness = thickness
    mod.use_even_offset = False
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return paint(ob, color)


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = active()
    ob.name = name
    ob.data.name = name
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for poly in ob.data.polygons:
        poly.use_smooth = False
    return ob


# ----------------------------------------------------------------------
# Модели
# ----------------------------------------------------------------------

def mine_rig():
    """Буровая вышка: площадка, решётчатая башня с буром, цех, отвал породы, сигнальный огонь."""
    p = [
        cyl(1.0, 0.14, (0, 0, 0), CONCRETE, verts=10),
        cyl(0.92, 0.03, (0, 0, 0.14), HAZARD, verts=10),
        lattice(0.42, 0.12, 1.7, (0, 0, 0.17), METAL, levels=5),
        cyl(0.08, 1.5, (0, 0, 0.17), DARK, verts=6),
        cyl(0.18, 0.12, (0, 0, 1.87), DARK, verts=8),
        ball(0.09, (0, 0, 2.04), RED, LIGHT),
        box((0.55, 0.4, 0.42), (0.62, 0.0, 0.38), METAL),
        box((0.56, 0.41, 0.08), (0.62, 0.0, 0.36), WHITE, ACCENT),
        box((0.12, 0.02, 0.1), (0.5, -0.21, 0.45), WARM, LIGHT),
        box((0.12, 0.02, 0.1), (0.74, -0.21, 0.45), WARM, LIGHT),
        cyl(0.07, 0.6, (0.3, 0.0, 0.5), DARK, verts=6, rot=(0, math.radians(80), 0)),
        cyl(0.38, 0.32, (-0.6, 0.35, 0.14), STONE, verts=7, r2=0.05),
        cyl(0.3, 0.25, (-0.5, -0.45, 0.14), STONE, verts=7, r2=0.04),
    ]
    return join(p, "mine_rig")


def mine_gas():
    """Газодобывающая платформа: висит над облаками, заборник опущен вниз, баки, огни по кругу."""
    p = [
        cyl(1.0, 0.12, (0, 0, 0.0), METAL, verts=12),
        cyl(1.02, 0.04, (0, 0, 0.04), WHITE, ACCENT, verts=12),
        cyl(0.35, 1.0, (0, 0, -1.0), DARK, verts=8, r2=0.12),
        cyl(0.45, 0.12, (0, 0, -1.1), METAL, verts=8),
        cyl(0.22, 0.75, (0, 0, 0.12), METAL, verts=8),
        cyl(0.3, 0.12, (0, 0, 0.87), DARK, verts=8),
        box((0.3, 0.02, 0.12), (0, -0.23, 0.6), WARM, LIGHT),
        cyl(0.025, 0.5, (0, 0, 0.99), DARK, verts=4),
        ball(0.06, (0, 0, 1.52), RED, LIGHT),
    ]
    for i in range(3):
        a = math.radians(30 + 120 * i)
        p.append(ball(0.26, (0.62 * math.cos(a), 0.62 * math.sin(a), 0.36), WHITE, ACCENT, segments=10, rings=7))
    for i in range(8):
        a = math.radians(45 * i)
        p.append(ball(0.04, (0.96 * math.cos(a), 0.96 * math.sin(a), 0.14), WARM, LIGHT, segments=6, rings=4))
    return join(p, "mine_gas")


def mine_build():
    """Стройка: площадка, нижняя половина вышки, башенный кран с грузом, ящики."""
    p = [
        cyl(1.0, 0.14, (0, 0, 0), CONCRETE, verts=10),
        lattice(0.42, 0.28, 0.75, (0, 0, 0.14), METAL, levels=2),
        # Кран.
        lattice(0.09, 0.09, 1.9, (0.7, -0.5, 0.14), HAZARD, levels=6, thickness=0.035),
        box((1.5, 0.1, 0.1), (0.35, -0.5, 2.08), HAZARD),
        box((0.3, 0.18, 0.22), (1.2, -0.5, 2.0), DARK),
        box((0.02, 0.02, 0.8), (-0.2, -0.5, 1.63), DARK),
        box((0.2, 0.2, 0.16), (-0.2, -0.5, 1.15), WHITE, ACCENT),
        ball(0.05, (0.7, -0.5, 2.18), RED, LIGHT),
        # Ящики.
        box((0.24, 0.24, 0.2), (-0.55, 0.45, 0.24), HAZARD),
        box((0.2, 0.2, 0.18), (-0.3, 0.6, 0.23), WHITE, ACCENT),
        box((0.22, 0.22, 0.2), (-0.62, 0.2, 0.24), CONCRETE),
    ]
    return join(p, "mine_build")


def extractor():
    """Экстрактор: бак цвета ресурса, насос с качалкой, труба, огонёк."""
    p = [
        cyl(0.7, 0.1, (0, 0, 0), CONCRETE, verts=8),
        cyl(0.32, 0.7, (-0.2, 0, 0.1), WHITE, ACCENT, verts=10),
        dome(0.32, (-0.2, 0, 0.8), METAL, segments=10, rings=3),
        cyl(0.06, 0.2, (-0.2, 0, 1.08), DARK, verts=6),
        ball(0.05, (-0.2, 0, 1.32), WARM, LIGHT),
        # Качалка.
        box((0.08, 0.08, 0.5), (0.35, 0, 0.35), DARK),
        box((0.6, 0.07, 0.07), (0.38, 0, 0.62), METAL, rot=(0, math.radians(-15), 0)),
        box((0.12, 0.14, 0.18), (0.66, 0, 0.66), DARK, rot=(0, math.radians(-15), 0)),
        cyl(0.05, 0.4, (0.08, 0, 0.3), DARK, verts=6, rot=(0, math.radians(90), 0)),
    ]
    return join(p, "extractor")


def settlement():
    """Поселение жителей: купола разного размера, переходы между ними, огни окон."""
    domes = [((0, 0), 0.62), ((0.95, 0.35), 0.42), ((-0.7, 0.6), 0.38), ((0.2, -0.9), 0.34)]
    p = [cyl(1.45, 0.06, (0, 0, 0), CONCRETE, verts=12)]
    for (x, y), r in domes:
        p.append(cyl(r * 1.05, 0.1, (x, y, 0.06), METAL, verts=10))
        p.append(dome(r, (x, y, 0.16), GLASS, segments=12, rings=4))
        p.append(box((r * 0.5, 0.02, 0.07), (x, y - r * 0.92, 0.22), WARM, LIGHT))
    for (x, y), _ in domes[1:]:
        length = math.hypot(x, y)
        ang = math.atan2(y, x)
        p.append(cyl(0.08, length, (0, 0, 0.16), METAL, verts=6, rot=(0, math.radians(90), ang)))
        # Цилиндр повёрнут вокруг основания, а не центра — сдвигаем в середину перехода.
        p[-1].location = (x / 2, y / 2, 0.25)
    p.append(cyl(0.025, 0.5, (0, 0, 0.78), DARK, verts=4))
    p.append(box((0.22, 0.01, 0.13), (0.11, 0, 1.2), WHITE, ACCENT))
    return join(p, "settlement")


def school():
    """Школа: здание с двускатной крышей, ряд светящихся окон, шпиль с часами и флагом."""
    p = [
        box((1.3, 0.75, 0.55), (0, 0, 0.28), (0.85, 0.82, 0.72)),
        box((1.32, 0.77, 0.06), (0, 0, 0.03), CONCRETE),
        cyl(0.95, 0.45, (0, 0, 0.55), (0.55, 0.25, 0.2), verts=4, r2=0.02, rot=(0, 0, math.radians(45))),
        box((0.35, 0.35, 0.65), (0, 0, 0.88), (0.85, 0.82, 0.72)),
        cyl(0.27, 0.35, (0, 0, 1.2), (0.55, 0.25, 0.2), verts=4, r2=0.01, rot=(0, 0, math.radians(45))),
        ball(0.11, (0, -0.18, 1.0), WHITE, LIGHT, segments=10, rings=6),
        box((0.02, 0.02, 0.35), (0, 0, 1.7), DARK),
        box((0.22, 0.01, 0.13), (0.11, 0, 1.8), WHITE, ACCENT),
        box((0.2, 0.3, 0.3), (0, -0.45, 0.15), (0.85, 0.82, 0.72)),
        box((0.12, 0.02, 0.2), (0, -0.6, 0.13), DARK),
    ]
    for i in range(4):
        x = -0.5 + i * 0.33
        if abs(x) < 0.1:
            continue
        p.append(box((0.14, 0.02, 0.16), (x, -0.38, 0.32), WARM, LIGHT))
        p.append(box((0.14, 0.02, 0.16), (x, 0.38, 0.32), WARM, LIGHT))
    return join(p, "school")


def guard_tower():
    """Сторожевая башня: тонкая опора, площадка с прожектором, красный огонь, полоса цвета владельца."""
    p = [
        cyl(0.45, 0.12, (0, 0, 0), CONCRETE, verts=8),
        cyl(0.2, 1.4, (0, 0, 0.12), METAL, verts=8, r2=0.13),
        cyl(0.14, 0.08, (0, 0, 0.9), WHITE, ACCENT, verts=8),
        cyl(0.42, 0.08, (0, 0, 1.52), DARK, verts=8),
        cyl(0.3, 0.26, (0, 0, 1.6), METAL, verts=8),
        box((0.36, 0.02, 0.08), (0, -0.3, 1.74), RED, LIGHT),
        cyl(0.33, 0.06, (0, 0, 1.86), DARK, verts=8),
        cyl(0.08, 0.22, (0.2, -0.32, 1.66), DARK, verts=6, rot=(math.radians(60), 0, 0)),
        ball(0.06, (0.2, -0.45, 1.75), WARM, LIGHT),
        cyl(0.02, 0.35, (0, 0, 1.92), DARK, verts=4),
        ball(0.05, (0, 0, 2.3), RED, LIGHT),
    ]
    return join(p, "guard_tower")


def ruins():
    """Руины: обломки колонн по кругу, арка, упавшая колонна, светящийся кристалл в центре."""
    p = [cyl(1.2, 0.08, (0, 0, 0), (0.5, 0.46, 0.4), verts=10)]
    # Две высокие колонны (10° и 70°) — уцелевшая арка с перекладиной.
    heights = [1.1, 1.1, 0.8, 0.25, 0.6, 0.35]
    spots = []
    for i, h in enumerate(heights):
        a = math.radians(60 * i + 10)
        x, y = 0.85 * math.cos(a), 0.85 * math.sin(a)
        spots.append((x, y))
        p.append(cyl(0.12, h, (x, y, 0.08), STONE, verts=6))
        p.append(box((0.3, 0.3, 0.06), (x, y, 0.11), (0.5, 0.45, 0.38)))
    (x1, y1), (x2, y2) = spots[0], spots[1]
    span = math.hypot(x2 - x1, y2 - y1)
    p.append(box((span + 0.3, 0.22, 0.16), ((x1 + x2) / 2, (y1 + y2) / 2, 1.26), STONE, rot=(0, 0, math.atan2(y2 - y1, x2 - x1))))
    p.append(cyl(0.11, 0.8, (-0.55, -0.45, 0.19), STONE, verts=6, rot=(0, math.radians(90), math.radians(25))))
    p.append(cyl(0.2, 0.15, (0, 0, 0.08), (0.45, 0.4, 0.35), verts=6))
    p.append(cyl(0.12, 0.5, (0, 0, 0.23), CYAN, LIGHT, verts=5, r2=0.0))
    return join(p, "ruins")


# ----------------------------------------------------------------------
# Сборка и выгрузка
# ----------------------------------------------------------------------

def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    makers = {name: globals()[name] for name in MODELS}
    for i, name in enumerate(MODELS):
        ob = makers[name]()
        # В файле модели стоят в ряд, чтобы их было удобно смотреть; выгружаются из начала координат.
        ob.location = ((i % 4) * 3.2, -(i // 4) * 3.5, 0)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("Сохранено:", BLEND)


def export_all():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name in MODELS:
        ob = bpy.data.objects.get(name)
        if ob is None:
            print("Нет модели", name)
            continue
        saved = ob.location.copy()
        ob.location = (0, 0, 0)
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        path = os.path.join(OUT_DIR, name + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=path,
            use_selection=True,
            object_types={"MESH"},
            apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z",
            axis_up="Y",
            bake_space_transform=True,
            mesh_smooth_type="FACE",
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            bake_anim=False,
        )
        ob.location = saved
        print("Выгружено:", path, len(ob.data.vertices), "вершин")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--export-only" not in args:
        build_all()
    export_all()
