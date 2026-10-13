"""
Постройки на планетах: модели для 3D-окна планеты.

Запуск (создаёт модели, сохраняет buildings.blend рядом и выгружает FBX в Unity):
    /Applications/Blender.app/Contents/MacOS/Blender --background --python tools/buildings/make_buildings.py

Если поправили модели руками в buildings.blend, выгрузить только FBX:
    /Applications/Blender.app/Contents/MacOS/Blender tools/buildings/buildings.blend --background --python tools/buildings/make_buildings.py -- --export-only

Модели видны только в открытом окне планеты и крупно, поэтому детализированы: фаски, фермы из балок,
перила, лестницы, трубы, техника вокруг. Материал в Unity один на всё, свойства детали — в вершинах:
    цвет вершины RGB — цвет детали;
    альфа цвета — роль: 1 — обычная, 0.5 — акцент (в Unity красится в цвет владельца или ресурса),
                         0 — свет (окна и огни, светятся и на ночной стороне);
    UV.x — блеск: 0 — матовый бетон и камень, 0.5 — крашеный металл, 0.9 — стекло.
Ось Z — вверх, основание модели — на z = 0, высота моделей около 2 единиц, площадка — радиус около 1.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
OUT_DIR = os.path.join(REPO, "unity", "Assets", "Resources", "Buildings")
BLEND = os.path.join(HERE, "buildings.blend")

NORMAL, ACCENT, LIGHT = 1.0, 0.5, 0.0

METAL = (0.72, 0.74, 0.78)
STEEL = (0.52, 0.55, 0.6)
DARK = (0.26, 0.28, 0.32)
CONCRETE = (0.58, 0.56, 0.52)
ASPHALT = (0.24, 0.24, 0.26)
STONE = (0.62, 0.55, 0.45)
STONE_DARK = (0.46, 0.41, 0.34)
HAZARD = (0.95, 0.72, 0.12)
GLASS = (0.55, 0.78, 0.92)
WARM = (1.0, 0.82, 0.45)
RED = (1.0, 0.22, 0.15)
GREEN_LIGHT = (0.3, 1.0, 0.4)
CYAN = (0.35, 1.0, 0.95)
BLUE_GLOW = (0.45, 0.75, 1.0)
WHITE = (1.0, 1.0, 1.0)
PLASTER = (0.86, 0.82, 0.72)
ROOF = (0.58, 0.24, 0.18)
GOLD = (0.8, 0.64, 0.28)
SOLAR = (0.12, 0.2, 0.42)
SOIL = (0.35, 0.25, 0.15)
LAWN = (0.36, 0.55, 0.28)
LEAF = (0.27, 0.55, 0.22)
LEAF_DARK = (0.18, 0.42, 0.18)
TRUNK = (0.42, 0.28, 0.18)
WOOD = (0.55, 0.38, 0.22)
OBSIDIAN = (0.3, 0.3, 0.36)

MODELS = ["mine_rig", "mine_gas", "mine_build", "extractor", "settlement", "school", "guard_tower", "ruins"]

# Детали текущей модели; join() собирает их в один объект.
PARTS = []


# ----------------------------------------------------------------------
# Геометрия
# ----------------------------------------------------------------------

def mat(loc=(0, 0, 0), rot=(0, 0, 0)):
    return Matrix.Translation(Vector(loc)) @ Euler(rot).to_matrix().to_4x4()


def bevel_sharp(bm, width, angle=50):
    """Фаска на острых рёбрах (между гранями под углом больше angle градусов)."""
    if width <= 0:
        return
    edges = [e for e in bm.edges if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(angle)]
    if edges:
        bmesh.ops.bevel(bm, geom=edges, offset=width, segments=1, profile=0.5, affect="EDGES", clamp_overlap=True)


def emit(bm, color, role=NORMAL, shine=0.3, smooth=False, matrix=None):
    me = bpy.data.meshes.new("деталь")
    bm.to_mesh(me)
    bm.free()
    if matrix is not None:
        me.transform(matrix)
    ob = bpy.data.objects.new("деталь", me)
    bpy.context.scene.collection.objects.link(ob)
    vc = me.vertex_colors.new(name="Col")
    for d in vc.data:
        d.color = (color[0], color[1], color[2], role)
    uv = me.uv_layers.new(name="UVMap")
    for d in uv.data:
        d.uv = (shine, 0.5)
    for p in me.polygons:
        p.use_smooth = smooth
    PARTS.append(ob)
    return ob


def box(size, loc, color, role=NORMAL, shine=0.3, rot=(0, 0, 0), bevel=0.015):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bevel_sharp(bm, min(bevel, min(size) * 0.3))
    return emit(bm, color, role, shine, False, mat(loc, rot))


def cyl(r, h, loc, color, role=NORMAL, shine=0.3, segs=16, r2=None, rot=(0, 0, 0), bevel=0.0, smooth=True, caps=True,
        spin=0.0, scale=(1, 1, 1)):
    """Цилиндр или усечённый конус; основание — в loc (до поворота ось — Z).
    spin — поворот многоугольника вокруг оси, scale — сплющить сечение (для крыш)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r, radius2=r if r2 is None else r2, depth=h)
    bmesh.ops.translate(bm, vec=(0, 0, h / 2), verts=bm.verts)
    if spin:
        bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(spin, 3, "Z"), verts=bm.verts)
    if scale != (1, 1, 1):
        bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
    bevel_sharp(bm, bevel)
    return emit(bm, color, role, shine, smooth, mat(loc, rot))


def sphere(r, loc, color, role=NORMAL, shine=0.3, u=16, v=8, scale=(1, 1, 1), smooth=True, rot=(0, 0, 0)):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
    return emit(bm, color, role, shine, smooth, mat(loc, rot))


def dome(r, loc, color, role=NORMAL, shine=0.3, u=24, v=12, height=1.0, smooth=True, rot=(0, 0, 0)):
    """Полусфера на loc; height < 1 — приплюснутая."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    bmesh.ops.delete(bm, geom=[x for x in bm.verts if x.co.z < -1e-4], context="VERTS")
    bmesh.ops.scale(bm, vec=(1, 1, height), verts=bm.verts)
    return emit(bm, color, role, shine, smooth, mat(loc, rot))


def vault(r, length, loc, yaw, color, role=NORMAL, shine=0.9, segs=24):
    """Полуцилиндр-свод длиной length вдоль направления yaw, лежит на loc."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=length)
    bmesh.ops.delete(bm, geom=[x for x in bm.verts if x.co.y < -1e-4], context="VERTS")
    # Ось свода (Z) — горизонтально вдоль X, выпуклостью вверх.
    m = mat(loc, (0, 0, yaw)) @ Matrix.Rotation(math.pi / 2, 4, "Y") @ Matrix.Rotation(math.pi / 2, 4, "Z")
    return emit(bm, color, role, shine, True, m)


def prism_tri(width, height, depth, loc, color, role=NORMAL, shine=0.2, yaw=0.0):
    """Треугольная призма (фронтон): основание шириной width на loc, вершина вверху, толщина depth."""
    bm = bmesh.new()
    vs = []
    for y in (-depth / 2, depth / 2):
        vs.append([bm.verts.new((-width / 2, y, 0)), bm.verts.new((width / 2, y, 0)), bm.verts.new((0, y, height))])
    a, b = vs
    bm.faces.new((a[0], a[1], a[2]))
    bm.faces.new((b[2], b[1], b[0]))
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((a[j], a[i], b[i], b[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return emit(bm, color, role, shine, False, mat(loc, (0, 0, yaw)))


def rock(r, loc, color, rnd, shine=0.05):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
    for x in bm.verts:
        x.co *= 0.75 + rnd.random() * 0.5
    bmesh.ops.scale(bm, vec=(1, 1, 0.6), verts=bm.verts)
    return emit(bm, color, NORMAL, shine, False, mat(loc, (rnd.random() * 6, rnd.random() * 6, rnd.random() * 6)))


def rod(p1, p2, r, color, role=NORMAL, shine=0.5, segs=6, smooth=True):
    """Балка или труба от p1 до p2."""
    a, b = Vector(p1), Vector(p2)
    d = b - a
    length = d.length
    if length < 1e-6:
        return None
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r, radius2=r, depth=length)
    bmesh.ops.translate(bm, vec=(0, 0, length / 2), verts=bm.verts)
    m = Matrix.Translation(a) @ Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    return emit(bm, color, role, shine, smooth, m)


def polyline(points, r, color, role=NORMAL, shine=0.5, segs=6, joints=True):
    """Труба по ломаной: прямые куски и шарики в изгибах."""
    for a, b in zip(points, points[1:]):
        rod(a, b, r, color, role, shine, segs)
    if joints:
        for p in points[1:-1]:
            sphere(r * 1.15, p, color, role, shine, u=segs, v=max(4, segs // 2))


def ring(radius, z, r, color, role=NORMAL, shine=0.5, segs=24, center=(0, 0), tube=6):
    cx, cy = center
    pts = [(cx + radius * math.cos(2 * math.pi * i / segs), cy + radius * math.sin(2 * math.pi * i / segs), z) for i in range(segs + 1)]
    for a, b in zip(pts, pts[1:]):
        rod(a, b, r, color, role, shine, tube)


def railing_circle(radius, z, height, color, posts=16, center=(0, 0), r=0.012):
    cx, cy = center
    for i in range(posts):
        a = 2 * math.pi * i / posts
        x, y = cx + radius * math.cos(a), cy + radius * math.sin(a)
        rod((x, y, z), (x, y, z + height), r, color, segs=5)
    ring(radius, z + height, r, color, segs=posts * 2, center=center, tube=5)
    ring(radius, z + height * 0.5, r * 0.8, color, segs=posts * 2, center=center, tube=4)


def railing_line(p1, p2, height, color, posts=4, r=0.012):
    a, b = Vector(p1), Vector(p2)
    for i in range(posts):
        p = a.lerp(b, i / (posts - 1))
        rod(p, p + Vector((0, 0, height)), r, color, segs=5)
    up = Vector((0, 0, height))
    rod(a + up, b + up, r, color, segs=5)
    rod(a + up * 0.5, b + up * 0.5, r * 0.8, color, segs=4)


def ladder(p_bottom, p_top, width, color, side=(1, 0, 0), r=0.01, step=0.07):
    """Лестница от p_bottom до p_top; side — направление поперёк лестницы."""
    a, b = Vector(p_bottom), Vector(p_top)
    s = Vector(side).normalized() * width / 2
    rod(a - s, b - s, r, color, segs=4)
    rod(a + s, b + s, r, color, segs=4)
    n = max(2, int((b - a).length / step))
    for i in range(1, n):
        p = a.lerp(b, i / n)
        rod(p - s, p + s, r * 0.8, color, segs=4)


def lattice_tower(base, top, z0, height, levels, r, color, center=(0, 0), shine=0.5):
    """Четырёхгранная ферма: ноги, пояса на каждом уровне и раскосы крест-накрест."""
    cx, cy = center
    corners = [(1, 1), (-1, 1), (-1, -1), (1, -1)]

    def pt(i, level):
        t = level / levels
        half = base + (top - base) * t
        sx, sy = corners[i % 4]
        return (cx + sx * half, cy + sy * half, z0 + height * t)

    for i in range(4):
        rod(pt(i, 0), pt(i, levels), r * 1.3, color, shine=shine, segs=6)
    for lv in range(1, levels + 1):
        for i in range(4):
            rod(pt(i, lv), pt(i + 1, lv), r, color, shine=shine, segs=5)
    for lv in range(levels):
        for i in range(4):
            rod(pt(i, lv), pt(i + 1, lv + 1), r * 0.8, color, shine=shine, segs=4)
            rod(pt(i + 1, lv), pt(i, lv + 1), r * 0.8, color, shine=shine, segs=4)


def truss(p1, p2, size, r, color, panels=8, shine=0.5):
    """Треугольная ферма-стрела крана от p1 до p2 (горизонтальная)."""
    a, b = Vector(p1), Vector(p2)
    d = (b - a).normalized()
    side = d.cross(Vector((0, 0, 1))).normalized() * size / 2
    up = Vector((0, 0, size))
    chords = [(a - side, b - side), (a + side, b + side), (a + up, b + up)]
    for c in chords:
        rod(c[0], c[1], r * 1.2, color, shine=shine, segs=5)
    for i in range(panels + 1):
        p0 = [c[0].lerp(c[1], i / panels) for c in chords]
        rod(p0[0], p0[1], r, color, shine=shine, segs=4)
        rod(p0[0], p0[2], r, color, shine=shine, segs=4)
        rod(p0[1], p0[2], r, color, shine=shine, segs=4)
        if i < panels:
            p1_ = [c[0].lerp(c[1], (i + 1) / panels) for c in chords]
            rod(p0[0], p1_[2], r * 0.8, color, shine=shine, segs=4)
            rod(p0[1], p1_[2], r * 0.8, color, shine=shine, segs=4)


def pad(radius, height=0.12, stripes=True, segs=12):
    """Бетонная площадка с фаской и полосатым жёлто-чёрным краем."""
    cyl(radius, height, (0, 0, 0), CONCRETE, shine=0.05, segs=segs, bevel=0.025, smooth=False)
    if stripes:
        n = 28
        for i in range(n):
            a = 2 * math.pi * (i + 0.5) / n
            rr = radius * 0.94
            box((2 * math.pi * rr / n * 0.98, 0.07, 0.012), (rr * math.cos(a), rr * math.sin(a), height + 0.004),
                HAZARD if i % 2 else ASPHALT, shine=0.2, rot=(0, 0, a + math.pi / 2), bevel=0)


def lamp_post(x, y, z=0.0, h=0.55, color=STEEL):
    rod((x, y, z), (x, y, z + h), 0.014, color, segs=6)
    rod((x, y, z + h), (x + 0.08, y, z + h + 0.02), 0.01, color, segs=4)
    box((0.07, 0.04, 0.025), (x + 0.1, y, z + h + 0.01), DARK, bevel=0.005)
    box((0.05, 0.03, 0.006), (x + 0.1, y, z + h - 0.006), WARM, LIGHT, bevel=0)


def truck(x, y, z, angle, color=HAZARD):
    """Самосвал: рама, кабина со стеклом и фарами, кузов с породой, колёса."""
    rot = (0, 0, angle)
    m = mat((x, y, z), rot)

    def at(p):
        return tuple(m @ Vector(p))

    box((0.36, 0.15, 0.04), at((0, 0, 0.065)), DARK, shine=0.4, rot=rot, bevel=0.008)
    box((0.12, 0.16, 0.12), at((0.12, 0, 0.15)), color, shine=0.5, rot=rot, bevel=0.015)
    box((0.012, 0.13, 0.06), at((0.181, 0, 0.17)), GLASS, shine=0.95, rot=rot, bevel=0)
    for sy in (-0.055, 0.055):
        box((0.008, 0.025, 0.015), at((0.181, sy, 0.11)), WARM, LIGHT, rot=rot, bevel=0)
    box((0.22, 0.17, 0.1), at((-0.07, 0, 0.14)), color, shine=0.5, rot=rot, bevel=0.012)
    box((0.2, 0.15, 0.03), at((-0.07, 0, 0.19)), STONE_DARK, shine=0.05, rot=rot, bevel=0)
    for wx in (0.11, -0.04, -0.14):
        for wy in (0.085, -0.085):
            c = at((wx, wy, 0.045))
            cyl(0.045, 0.035, c, ASPHALT, shine=0.1, segs=12, rot=(math.pi / 2, 0, angle))
            cyl(0.022, 0.037, c, STEEL, shine=0.5, segs=8, rot=(math.pi / 2, 0, angle))


def window_row(center, along, count, spacing, size, color=WARM, rot_z=0.0):
    cx, cy, cz = center
    ax, ay = along
    for i in range(count):
        t = (i - (count - 1) / 2) * spacing
        box(size, (cx + ax * t, cy + ay * t, cz), color, LIGHT, rot=(0, 0, rot_z), bevel=0)


def tree(x, y, z, h=0.45, rnd=None):
    rnd = rnd or random.Random(1)
    cyl(0.025, h * 0.45, (x, y, z), TRUNK, shine=0.05, segs=6)
    for i in range(3):
        rr = h * (0.28 - i * 0.06)
        sphere(rr, (x + (rnd.random() - 0.5) * 0.05, y + (rnd.random() - 0.5) * 0.05, z + h * (0.45 + i * 0.17)),
               LEAF if i % 2 == 0 else LEAF_DARK, shine=0.1, u=8, v=6, smooth=False)


def flag(x, y, z, h=0.6):
    rod((x, y, z), (x, y, z + h), 0.01, STEEL, segs=5)
    sphere(0.016, (x, y, z + h + 0.01), GOLD, shine=0.8, u=6, v=4)
    box((0.2, 0.006, 0.12), (x + 0.1, y, z + h - 0.07), WHITE, ACCENT, shine=0.2, bevel=0)


def dish(x, y, z, r, tilt=0.6, yaw=0.0):
    """Тарелка антенны на мачте: чаша, облучатель на трёх распорках, огонёк."""
    rod((x, y, z), (x, y, z + 0.25), 0.015, STEEL, segs=6)
    box((0.06, 0.06, 0.04), (x, y, z + 0.25), DARK, bevel=0.005)
    m = mat((x, y, z + 0.28), (tilt, 0, yaw))
    dome(r, (x, y, z + 0.28), METAL, shine=0.6, u=20, v=6, height=0.35, rot=(math.pi + tilt, 0, yaw))
    feed = m @ Vector((0, 0, r * 0.8))
    for k in range(3):
        a = 2 * math.pi * k / 3
        rod(tuple(m @ Vector((r * 0.9 * math.cos(a), r * 0.9 * math.sin(a), -r * 0.3))), tuple(feed), 0.004, DARK, segs=3)
    sphere(0.018, tuple(feed), RED, LIGHT, u=6, v=4)


def join(name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in PARTS:
        p.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    me = ob.data
    me.use_auto_smooth = True
    me.auto_smooth_angle = math.radians(45)
    PARTS.clear()
    return ob


# ----------------------------------------------------------------------
# Модели
# ----------------------------------------------------------------------

def mine_rig():
    """Буровая: площадка, вышка-ферма с талями и буром, помост с перилами и лестницей,
    цех, баки, конвейер к отвалу, фонари, самосвал."""
    pad(1.1)
    z0 = 0.12
    # Помост буровой.
    box((0.9, 0.9, 0.05), (0, 0, z0 + 0.32), STEEL, shine=0.4)
    for sx in (1, -1):
        for sy in (1, -1):
            rod((sx * 0.4, sy * 0.4, z0), (sx * 0.4, sy * 0.4, z0 + 0.3), 0.025, DARK, segs=6)
    railing_line((-0.45, 0.45, z0 + 0.345), (0.45, 0.45, z0 + 0.345), 0.14, HAZARD, posts=5)
    railing_line((-0.45, -0.45, z0 + 0.345), (-0.45, 0.45, z0 + 0.345), 0.14, HAZARD, posts=5)
    railing_line((0.45, 0.45, z0 + 0.345), (0.45, -0.1, z0 + 0.345), 0.14, HAZARD, posts=3)
    for i in range(6):
        box((0.16, 0.12, 0.025), (0.53 + i * 0.07, -0.3, z0 + 0.3 - i * 0.055), STEEL, shine=0.4, bevel=0.004)
    # Вышка.
    lattice_tower(0.36, 0.1, z0 + 0.345, 1.75, 7, 0.016, METAL)
    top = z0 + 0.345 + 1.75
    box((0.3, 0.3, 0.1), (0, 0, top + 0.05), DARK, shine=0.5)
    cyl(0.1, 0.04, (0, 0.02, top + 0.16), STEEL, segs=16, rot=(math.pi / 2, 0, 0))
    rod((0, 0, top + 0.1), (0, 0, top + 0.24), 0.01, DARK, segs=4)
    sphere(0.05, (0, 0, top + 0.28), RED, LIGHT, u=10, v=6)
    # Тали, бур, роторный стол.
    box((0.12, 0.12, 0.18), (0, 0, z0 + 1.35), HAZARD, shine=0.5, bevel=0.01)
    for dx in (-0.03, 0.03):
        rod((dx, 0, top), (dx, 0, z0 + 1.44), 0.004, DARK, segs=3)
    rod((0, 0, z0 + 0.37), (0, 0, z0 + 1.26), 0.035, STEEL, shine=0.8, segs=10)
    cyl(0.16, 0.06, (0, 0, z0 + 0.345), DARK, shine=0.5, segs=20, bevel=0.01)
    # Цех с окнами, дверью, кондиционерами и трубой.
    cx, cy = 0.62, 0.45
    box((0.58, 0.4, 0.38), (cx, cy, z0 + 0.19), METAL, shine=0.4, bevel=0.02)
    box((0.62, 0.44, 0.04), (cx, cy, z0 + 0.4), DARK, shine=0.3)
    box((0.59, 0.41, 0.06), (cx, cy, z0 + 0.3), WHITE, ACCENT, shine=0.4, bevel=0.005)
    window_row((cx - 0.05, cy - 0.205, z0 + 0.18), (1, 0), 3, 0.13, (0.09, 0.01, 0.07))
    box((0.1, 0.01, 0.16), (cx + 0.22, cy - 0.205, z0 + 0.08), DARK, bevel=0)
    for dx in (-0.12, 0.08):
        box((0.12, 0.12, 0.07), (cx + dx, cy + 0.05, z0 + 0.455), STEEL, shine=0.5, bevel=0.01)
        cyl(0.04, 0.005, (cx + dx, cy + 0.05, z0 + 0.49), DARK, segs=12)
    cyl(0.035, 0.35, (cx + 0.2, cy + 0.12, z0 + 0.42), DARK, segs=10)
    polyline([(cx - 0.29, cy - 0.1, z0 + 0.25), (0.2, cy - 0.1, z0 + 0.25), (0.2, 0.1, z0 + 0.25), (0.05, 0.1, z0 + 0.25)], 0.025, STEEL)
    # Баки с поясами, куполами и лестницей.
    for (tx, ty) in ((-0.55, 0.55), (-0.25, 0.78)):
        cyl(0.14, 0.45, (tx, ty, z0), WHITE, ACCENT, shine=0.5, segs=20)
        dome(0.14, (tx, ty, z0 + 0.45), METAL, shine=0.6, u=20, v=8, height=0.5)
        for bz in (0.1, 0.3):
            cyl(0.145, 0.02, (tx, ty, z0 + bz), DARK, segs=20)
        ladder((tx + 0.15, ty, z0), (tx + 0.15, ty, z0 + 0.45), 0.06, STEEL, side=(0, 1, 0))
    polyline([(-0.41, 0.55, z0 + 0.05), (-0.2, 0.3, z0 + 0.05), (-0.05, 0.05, z0 + 0.05)], 0.02, STEEL)
    # Конвейер к отвалу: лента на опорах, бункер, куча породы.
    a, b = Vector((-0.2, -0.25, z0 + 0.32)), Vector((-0.72, -0.55, z0 + 0.62))
    d = b - a
    yaw = math.atan2(d.y, d.x)
    pitch = math.atan2(d.z, math.hypot(d.x, d.y))
    box((d.length, 0.1, 0.03), tuple((a + b) / 2), DARK, shine=0.3, rot=(0, -pitch, yaw), bevel=0.005)
    for t in (0.3, 0.7, 1.0):
        p = a.lerp(b, t)
        rod((p.x, p.y, z0), (p.x, p.y, p.z), 0.018, STEEL, segs=5)
    cyl(0.06, 0.12, tuple(b + Vector((0, 0, -0.04))), STEEL, segs=10, r2=0.13)
    cyl(0.42, 0.42, (-0.78, -0.58, z0), STONE, shine=0.05, segs=11, r2=0.06, smooth=False)
    rnd = random.Random(3)
    for i in range(10):
        ang = rnd.random() * 2 * math.pi
        rock(0.04 + rnd.random() * 0.03, (-0.78 + math.cos(ang) * 0.45, -0.58 + math.sin(ang) * 0.42, z0 + 0.02), STONE_DARK, rnd)
    lamp_post(0.85, -0.6, z0)
    lamp_post(-0.2, 0.98, z0)
    truck(0.45, -0.78, z0, math.radians(20))


def mine_gas():
    """Газодобывающая платформа: диск с перилами и огнями, башня управления с окнами, вертолётная площадка,
    сферические баки на опорах, длинный заборник с раструбом, двигатели снизу."""
    cyl(1.0, 0.12, (0, 0, 0), METAL, shine=0.5, segs=32, bevel=0.02)
    cyl(1.0, 0.28, (0, 0, -0.28), DARK, shine=0.4, segs=32)
    cyl(0.5, 0.25, (0, 0, -0.53), DARK, shine=0.4, segs=24, r2=1.0)
    cyl(1.015, 0.035, (0, 0, 0.05), WHITE, ACCENT, shine=0.5, segs=32)
    railing_circle(0.96, 0.12, 0.1, STEEL, posts=24)
    for i in range(16):
        a = 2 * math.pi * (i + 0.5) / 16
        sphere(0.022, (1.0 * math.cos(a), 1.0 * math.sin(a), 0.0), WARM, LIGHT, u=6, v=4)
    # Башня управления.
    cyl(0.28, 0.5, (0, 0, 0.12), METAL, shine=0.5, segs=20)
    cyl(0.4, 0.05, (0, 0, 0.62), DARK, shine=0.4, segs=24, bevel=0.01)
    railing_circle(0.38, 0.67, 0.08, STEEL, posts=16)
    cyl(0.26, 0.2, (0, 0, 0.67), GLASS, shine=0.9, segs=20)
    for i in range(12):
        a = 2 * math.pi * i / 12
        box((0.07, 0.012, 0.06), (0.282 * math.cos(a), 0.282 * math.sin(a), 0.45), WARM, LIGHT, rot=(0, 0, a + math.pi / 2), bevel=0)
    cyl(0.29, 0.04, (0, 0, 0.87), DARK, segs=20)
    dome(0.22, (0, 0, 0.91), METAL, shine=0.6, u=20, v=8, height=0.5)
    rod((0, 0, 0.98), (0, 0, 1.45), 0.012, STEEL, segs=5)
    for z in (1.15, 1.3):
        rod((-0.08, 0, z), (0.08, 0, z), 0.006, STEEL, segs=4)
    sphere(0.035, (0, 0, 1.47), RED, LIGHT, u=8, v=6)
    dish(0.14, -0.14, 0.98, 0.12, tilt=0.7, yaw=-0.6)
    # Вертолётная площадка.
    hx, hy = 0.55, -0.48
    cyl(0.3, 0.025, (hx, hy, 0.12), ASPHALT, shine=0.2, segs=24)
    box((0.04, 0.2, 0.004), (hx - 0.07, hy, 0.147), WHITE, shine=0.2, bevel=0)
    box((0.04, 0.2, 0.004), (hx + 0.07, hy, 0.147), WHITE, shine=0.2, bevel=0)
    box((0.14, 0.04, 0.004), (hx, hy, 0.147), WHITE, shine=0.2, bevel=0)
    for i in range(10):
        a = 2 * math.pi * i / 10
        sphere(0.012, (hx + 0.27 * math.cos(a), hy + 0.27 * math.sin(a), 0.15), BLUE_GLOW, LIGHT, u=6, v=4)
    # Баки.
    for (tx, ty) in ((0.2, 0.62), (-0.6, 0.25), (-0.35, -0.55)):
        for k in range(4):
            la = 2 * math.pi * k / 4 + 0.4
            rod((tx + 0.16 * math.cos(la), ty + 0.16 * math.sin(la), 0.12), (tx + 0.08 * math.cos(la), ty + 0.08 * math.sin(la), 0.3), 0.012, STEEL, segs=5)
        sphere(0.22, (tx, ty, 0.42), WHITE, ACCENT, shine=0.6, u=24, v=12)
        ring(0.222, 0.42, 0.01, DARK, segs=24, center=(tx, ty))
        polyline([(tx, ty, 0.2), (tx * 0.4, ty * 0.4, 0.2), (tx * 0.4, ty * 0.4, 0.14)], 0.018, STEEL)
    # Заборник.
    cyl(0.12, 1.3, (0, 0, -1.83), STEEL, shine=0.5, segs=16)
    for z in (-0.8, -1.1, -1.4):
        cyl(0.135, 0.03, (0, 0, z), DARK, segs=16)
    cyl(0.42, 0.35, (0, 0, -2.18), DARK, shine=0.5, segs=20, r2=0.12, caps=False)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rod((0.42 * math.cos(a), 0.42 * math.sin(a), -2.18), (0.12 * math.cos(a), 0.12 * math.sin(a), -1.83), 0.012, STEEL, segs=4)
    ring(0.42, -2.18, 0.02, HAZARD, segs=24)
    # Двигатели.
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        x, y = 0.68 * math.cos(a), 0.68 * math.sin(a)
        cyl(0.1, 0.16, (x, y, -0.44), DARK, shine=0.5, segs=12, r2=0.07)
        cyl(0.095, 0.01, (x, y, -0.45), BLUE_GLOW, LIGHT, segs=12)


def mine_build():
    """Стройка буровой: площадка, нижняя часть фермы в лесах, башенный кран со стрелой-фермой и противовесом,
    груз на тросе, штабеля балок, ящики, бытовка, ограждение, прожектор."""
    pad(1.1)
    z0 = 0.12
    lattice_tower(0.36, 0.26, z0, 0.8, 3, 0.016, METAL)
    for lv in (0.27, 0.54):
        box((0.86, 0.12, 0.015), (0, -0.42, z0 + lv), WOOD, shine=0.05, bevel=0)
        box((0.12, 0.86, 0.015), (0.42, 0, z0 + lv), WOOD, shine=0.05, bevel=0)
    # Кран.
    mx, my = 0.72, -0.55
    box((0.3, 0.3, 0.06), (mx, my, z0 + 0.03), CONCRETE, shine=0.05)
    lattice_tower(0.07, 0.07, z0 + 0.06, 2.1, 12, 0.009, HAZARD, center=(mx, my))
    top = z0 + 2.16
    cyl(0.1, 0.05, (mx, my, top), DARK, segs=16)
    box((0.12, 0.14, 0.12), (mx + 0.1, my - 0.12, top + 0.06), HAZARD, shine=0.5, bevel=0.01)
    box((0.01, 0.1, 0.06), (mx + 0.16, my - 0.12, top + 0.07), GLASS, shine=0.9, bevel=0)
    jib_end = (mx - 1.55, my + 0.35, top + 0.05)
    tail_end = (mx + 0.5, my - 0.11, top + 0.05)
    truss((mx, my, top + 0.05), jib_end, 0.09, 0.007, HAZARD, panels=14)
    truss((mx, my, top + 0.05), tail_end, 0.09, 0.007, HAZARD, panels=4)
    for k in range(3):
        box((0.06, 0.16, 0.12), (mx + 0.32 + k * 0.065, my - 0.07 - k * 0.015, top - 0.02), CONCRETE, shine=0.05, rot=(0, 0, -0.22), bevel=0.006)
    lattice_tower(0.03, 0.006, top + 0.05, 0.3, 2, 0.006, HAZARD, center=(mx, my))
    for end in (jib_end, tail_end):
        rod((mx, my, top + 0.35), (end[0], end[1], top + 0.14), 0.004, DARK, segs=3)
    sphere(0.03, (mx, my, top + 0.37), RED, LIGHT, u=8, v=6)
    sphere(0.02, (jib_end[0], jib_end[1], top + 0.15), RED, LIGHT, u=6, v=4)
    tx, ty = mx - 1.05, my + 0.24
    box((0.06, 0.08, 0.03), (tx, ty, top + 0.035), DARK)
    rod((tx, ty, top + 0.02), (tx, ty, z0 + 0.95), 0.004, DARK, segs=3)
    box((0.04, 0.04, 0.05), (tx, ty, z0 + 0.92), HAZARD)
    rod((tx, ty, z0 + 0.9), (tx - 0.2, ty - 0.14, z0 + 0.86), 0.003, DARK, segs=3)
    rod((tx, ty, z0 + 0.9), (tx + 0.2, ty + 0.14, z0 + 0.86), 0.003, DARK, segs=3)
    box((0.5, 0.06, 0.06), (tx, ty, z0 + 0.83), WHITE, ACCENT, shine=0.4, rot=(0, 0, 0.6))
    # Штабеля балок и ящики.
    for row in range(3):
        for k in range(4 - row):
            box((0.55, 0.05, 0.05), (-0.35, -0.62 + k * 0.06 + row * 0.03, z0 + 0.025 + row * 0.05), STEEL, shine=0.5, bevel=0.005)
    for (x, y, z, s, c, role) in [(-0.65, 0.45, 0, 0.16, HAZARD, NORMAL), (-0.47, 0.56, 0, 0.13, WOOD, NORMAL),
                                  (-0.63, 0.63, 0, 0.12, WOOD, NORMAL), (-0.65, 0.45, 0.16, 0.12, WHITE, ACCENT)]:
        box((s, s, s), (x, y, z0 + z + s / 2), c, role, shine=0.1, bevel=0.01)
    # Бытовка.
    bx, by = 0.15, 0.75
    box((0.45, 0.22, 0.22), (bx, by, z0 + 0.13), WHITE, ACCENT, shine=0.4, bevel=0.015)
    for k in range(6):
        box((0.006, 0.226, 0.2), (bx - 0.2 + k * 0.08, by, z0 + 0.13), DARK, shine=0.3, bevel=0)
    box((0.12, 0.01, 0.08), (bx - 0.08, by - 0.115, z0 + 0.16), WARM, LIGHT, bevel=0)
    box((0.07, 0.01, 0.15), (bx + 0.12, by - 0.115, z0 + 0.1), DARK, bevel=0)
    # Ограждение и прожектор.
    for i in range(9):
        a = math.radians(120 + i * 15)
        rod((math.cos(a), math.sin(a), z0), (math.cos(a), math.sin(a), z0 + 0.15), 0.01, HAZARD if i % 2 else WHITE, segs=4)
    for i in range(8):
        a1, a2 = math.radians(120 + i * 15), math.radians(135 + i * 15)
        rod((math.cos(a1), math.sin(a1), z0 + 0.12), (math.cos(a2), math.sin(a2), z0 + 0.12), 0.006, HAZARD, segs=4)
    lamp_post(0.75, 0.55, z0, h=0.7)


def extractor():
    """Экстрактор: бак цвета ресурса с поясами, перилами и лестницей; станок-качалка с балансиром,
    головкой, кривошипом и противовесами; трубы, щит управления, фонарь."""
    cyl(0.78, 0.1, (0, 0, 0), CONCRETE, shine=0.05, segs=10, bevel=0.02, smooth=False)
    z0 = 0.1
    tx, ty = -0.3, 0.1
    cyl(0.32, 0.7, (tx, ty, z0), WHITE, ACCENT, shine=0.5, segs=28)
    for bz in (0.12, 0.35, 0.58):
        cyl(0.325, 0.025, (tx, ty, z0 + bz), DARK, segs=28)
    dome(0.32, (tx, ty, z0 + 0.7), METAL, shine=0.6, u=28, v=10, height=0.4)
    railing_circle(0.3, z0 + 0.72, 0.08, STEEL, posts=12, center=(tx, ty))
    ladder((tx + 0.33, ty, z0), (tx + 0.33, ty, z0 + 0.72), 0.07, STEEL, side=(0, 1, 0))
    cyl(0.05, 0.08, (tx, ty, z0 + 0.82), DARK, segs=12)
    sphere(0.03, (tx, ty, z0 + 0.93), WARM, LIGHT, u=8, v=6)
    # Качалка.
    px, py = 0.35, -0.2
    box((0.62, 0.16, 0.05), (px, py, z0 + 0.025), DARK, shine=0.4)
    apex = (px, py, z0 + 0.5)
    for dx, dy in ((-0.08, -0.07), (-0.08, 0.07), (0.08, -0.07), (0.08, 0.07)):
        rod((px + dx, py + dy, z0 + 0.05), apex, 0.012, STEEL, segs=5)
    beam_rot = (0, math.radians(-10), 0)
    box((0.62, 0.05, 0.05), (px, py, z0 + 0.53), HAZARD, shine=0.5, rot=beam_rot, bevel=0.008)
    hx, hz = px + 0.33, z0 + 0.475
    box((0.07, 0.07, 0.16), (hx, py, hz), HAZARD, shine=0.5, rot=beam_rot, bevel=0.01)
    cyl(0.08, 0.07, (hx - 0.01, py + 0.035, hz), HAZARD, shine=0.5, segs=12, rot=(math.pi / 2, 0, 0))
    rod((hx + 0.05, py, hz - 0.08), (hx + 0.05, py, z0 + 0.1), 0.006, STEEL, segs=4)
    cyl(0.04, 0.06, (hx + 0.05, py, z0 + 0.05), DARK, segs=12)
    ring(0.035, z0 + 0.13, 0.006, RED, segs=12, center=(hx + 0.05, py))
    for sy in (-0.05, 0.05):
        box((0.04, 0.02, 0.18), (px - 0.25, py + sy, z0 + 0.2), STEEL, rot=(0, 0.4, 0), bevel=0.004)
        box((0.12, 0.03, 0.1), (px - 0.31, py + sy, z0 + 0.13), DARK, rot=(0, 0.4, 0), bevel=0.01)
    box((0.12, 0.1, 0.1), (px - 0.2, py, z0 + 0.1), DARK, shine=0.4, bevel=0.01)
    box((0.1, 0.08, 0.08), (px - 0.12, py + 0.12, z0 + 0.08), WHITE, ACCENT, shine=0.4, bevel=0.01)
    polyline([(hx + 0.05, py, z0 + 0.04), (hx + 0.05, py + 0.35, z0 + 0.04), (tx + 0.25, ty + 0.25, z0 + 0.04)], 0.02, STEEL)
    # Щит управления и фонарь.
    box((0.14, 0.06, 0.2), (0.3, 0.45, z0 + 0.1), STEEL, shine=0.4, bevel=0.01)
    box((0.04, 0.01, 0.03), (0.27, 0.419, z0 + 0.15), GREEN_LIGHT, LIGHT, bevel=0)
    box((0.04, 0.01, 0.03), (0.33, 0.419, z0 + 0.15), RED, LIGHT, bevel=0)
    lamp_post(-0.65, -0.4, z0)


def settlement():
    """Поселение: купола с каркасом из рёбер и поясом окон, шлюзы, переходы, оранжерея с грядками,
    солнечные панели, водонапорная башня, антенна, флаг, вездеход."""
    cyl(1.5, 0.06, (0, 0, 0), CONCRETE, shine=0.05, segs=16, bevel=0.015, smooth=False)
    z0 = 0.06
    domes = [((0, 0), 0.58), ((0.95, 0.35), 0.38), ((-0.75, 0.55), 0.36), ((0.15, -0.9), 0.32)]
    for (x, y), _ in domes[1:]:
        length = math.hypot(x, y)
        d = Vector((x, y, 0)) / length
        box((length, 0.18, 0.01), (x / 2, y / 2, z0 + 0.005), ASPHALT, shine=0.1, rot=(0, 0, math.atan2(y, x)), bevel=0)
        rod((d.x * 0.5, d.y * 0.5, z0 + 0.12), (x - d.x * 0.3, y - d.y * 0.3, z0 + 0.12), 0.07, METAL, shine=0.5, segs=12)
        for t in (0.25, 0.5, 0.75):
            p = Vector((d.x * 0.5, d.y * 0.5, 0)).lerp(Vector((x - d.x * 0.3, y - d.y * 0.3, 0)), t)
            box((0.02, 0.17, 0.17), (p.x, p.y, z0 + 0.12), DARK, shine=0.4, rot=(0, 0, math.atan2(y, x)), bevel=0.004)
    for (x, y), r in domes:
        cyl(r * 1.04, 0.1, (x, y, z0), METAL, shine=0.4, segs=28, bevel=0.01)
        dome(r, (x, y, z0 + 0.1), GLASS, shine=0.9, u=32, v=14)
        for k in range(8):
            a = 2 * math.pi * k / 8
            pts = []
            for j in range(9):
                phi = math.pi / 2 * j / 8
                pts.append((x + r * 1.01 * math.cos(phi) * math.cos(a), y + r * 1.01 * math.cos(phi) * math.sin(a),
                            z0 + 0.1 + r * 1.01 * math.sin(phi)))
            polyline(pts, 0.008, STEEL, joints=False, segs=4)
        ring(r * 0.875, z0 + 0.1 + r * 0.5, 0.008, STEEL, segs=24, center=(x, y), tube=4)
        n = max(6, int(r * 22))
        for k in range(n):
            a = 2 * math.pi * k / n
            box((0.05, 0.01, 0.04), (x + r * 1.045 * math.cos(a), y + r * 1.045 * math.sin(a), z0 + 0.06), WARM, LIGHT,
                rot=(0, 0, a + math.pi / 2), bevel=0)
        a = math.radians(250)
        ax, ay = x + r * 1.05 * math.cos(a), y + r * 1.05 * math.sin(a)
        box((0.12, 0.12, 0.12), (ax, ay, z0 + 0.06), METAL, shine=0.4, rot=(0, 0, a), bevel=0.01)
        box((0.07, 0.01, 0.09), (ax + 0.061 * math.cos(a), ay + 0.061 * math.sin(a), z0 + 0.055), BLUE_GLOW, LIGHT, rot=(0, 0, a + math.pi / 2), bevel=0)
    # Оранжерея: стеклянный свод на арках, внутри грядки.
    gx, gy, ga = -0.6, -0.62, 0.5
    box((0.56, 0.36, 0.03), (gx, gy, z0 + 0.015), METAL, shine=0.4, rot=(0, 0, ga), bevel=0.005)
    box((0.5, 0.3, 0.012), (gx, gy, z0 + 0.035), SOIL, shine=0.05, rot=(0, 0, ga), bevel=0)
    rnd = random.Random(5)
    for k in range(12):
        t = (k % 6 - 2.5) * 0.08
        s = 0.07 if k < 6 else -0.07
        sphere(0.03 + rnd.random() * 0.012, (gx + t * math.cos(ga) - s * math.sin(ga), gy + t * math.sin(ga) + s * math.cos(ga), z0 + 0.06),
               LEAF if k % 2 else LEAF_DARK, shine=0.1, u=8, v=5, smooth=False)
    vault(0.17, 0.54, (gx, gy, z0 + 0.03), ga, GLASS)
    for t in (-0.27, -0.09, 0.09, 0.27):
        cx, cy = gx + t * math.cos(ga), gy + t * math.sin(ga)
        pts = []
        for j in range(9):
            phi = math.pi * j / 8
            off = 0.172 * math.cos(phi)
            pts.append((cx - off * math.sin(ga), cy + off * math.cos(ga), z0 + 0.03 + 0.172 * math.sin(phi)))
        polyline(pts, 0.007, STEEL, joints=False, segs=4)
    # Солнечные панели.
    for row in range(2):
        for k in range(3):
            sx, sy = 0.55 + k * 0.2, -0.45 - row * 0.22
            rod((sx, sy, z0), (sx, sy, z0 + 0.1), 0.01, STEEL, segs=4)
            box((0.18, 0.14, 0.012), (sx, sy, z0 + 0.12), SOLAR, shine=0.85, rot=(0.5, 0, 0), bevel=0.003)
            for g in (-0.045, 0.0, 0.045):
                box((0.003, 0.13, 0.014), (sx + g, sy, z0 + 0.12), STEEL, shine=0.6, rot=(0.5, 0, 0), bevel=0)
    # Водонапорная башня, антенна, флаг, вездеход.
    wx, wy = -1.05, -0.1
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.6
        rod((wx + 0.12 * math.cos(a), wy + 0.12 * math.sin(a), z0), (wx + 0.08 * math.cos(a), wy + 0.08 * math.sin(a), z0 + 0.4), 0.01, STEEL, segs=4)
    cyl(0.12, 0.18, (wx, wy, z0 + 0.4), WHITE, ACCENT, shine=0.5, segs=20)
    dome(0.12, (wx, wy, z0 + 0.58), METAL, shine=0.6, u=20, v=6, height=0.4)
    ladder((wx + 0.13, wy, z0), (wx + 0.13, wy, z0 + 0.58), 0.05, STEEL, side=(0, 1, 0))
    dish(0.55, 0.78, z0, 0.13, tilt=0.6, yaw=2.2)
    rod((0, 0, z0 + 0.6), (0, 0, z0 + 0.68), 0.02, STEEL, segs=6)
    flag(0.0, 0.0, z0 + 0.66, h=0.35)
    truck(0.98, -0.1, z0, math.radians(100), color=WHITE)


def school():
    """Школа: двухэтажное здание с карнизами и рядами окон, портик с колоннами и фронтоном, вальмовая крыша,
    часовая башня с открытой звонницей, колоколом и шпилем, обсерватория на крыше,
    двор с газоном, забором, деревьями, скамейкой и флагом."""
    cyl(1.25, 0.05, (0, 0, 0), CONCRETE, shine=0.05, segs=16, bevel=0.01, smooth=False)
    z0 = 0.05
    box((1.5, 1.0, 0.008), (0, 0.05, z0 + 0.004), LAWN, shine=0.05, bevel=0)
    W, D, H = 1.1, 0.6, 0.62
    yc = 0.15
    zb = z0 + 0.06
    box((W + 0.06, D + 0.06, 0.06), (0, yc, z0 + 0.03), STONE_DARK, shine=0.05)
    box((W, D, H), (0, yc, zb + H / 2), PLASTER, shine=0.15, bevel=0.01)
    box((W + 0.04, D + 0.04, 0.03), (0, yc, zb + H * 0.5), STONE, shine=0.05, bevel=0.005)
    box((W + 0.07, D + 0.07, 0.04), (0, yc, zb + H + 0.02), STONE, shine=0.05, bevel=0.008)
    for floor_z in (0.18, 0.47):
        for side in (-1, 1):
            y = yc + side * (D / 2 + 0.003)
            for i in range(7):
                x = -0.45 + i * 0.15
                if side == -1 and abs(x) < 0.12:
                    continue
                box((0.07, 0.008, 0.11), (x, y, zb + floor_z), WARM, LIGHT, bevel=0)
                box((0.09, 0.014, 0.015), (x, y, zb + floor_z - 0.065), STONE, shine=0.05, bevel=0)
                box((0.004, 0.012, 0.11), (x, y, zb + floor_z), PLASTER, shine=0.1, bevel=0)
        for side in (-1, 1):
            for j in range(3):
                box((0.008, 0.07, 0.11), (side * (W / 2 + 0.003), yc - 0.17 + j * 0.17, zb + floor_z), WARM, LIGHT, bevel=0)
    # Вальмовая крыша.
    cyl(0.707, 0.34, (0, yc, zb + H + 0.04), ROOF, shine=0.2, segs=4, r2=0.2, spin=math.pi / 4, scale=(1.2, 0.72, 1), smooth=False)
    for k in range(5):
        box((0.04, 0.04, 0.12), (-0.42 + k * 0.21, yc + 0.08, zb + H + 0.16), STONE_DARK if k % 2 else PLASTER, shine=0.05, bevel=0.004)
    # Портик с колоннами и фронтоном.
    front = yc - D / 2
    py = front - 0.12
    for k in range(3):
        box((0.56 + k * 0.08, 0.06, 0.02), (0, py - 0.06 - k * 0.05, zb - 0.01 - k * 0.018), STONE, shine=0.05, bevel=0.004)
    box((0.5, 0.26, 0.02), (0, py + 0.03, zb - 0.01), STONE, shine=0.05, bevel=0.004)
    for i in range(4):
        x = -0.18 + i * 0.12
        cyl(0.03, 0.03, (x, py, zb), WHITE, shine=0.2, segs=12)
        cyl(0.024, 0.46, (x, py, zb + 0.03), WHITE, shine=0.2, segs=12)
        box((0.065, 0.065, 0.025), (x, py, zb + 0.5), WHITE, shine=0.2, bevel=0.004)
    box((0.5, 0.26, 0.045), (0, py + 0.03, zb + 0.535), WHITE, shine=0.2, bevel=0.006)
    prism_tri(0.5, 0.14, 0.24, (0, py + 0.03, zb + 0.557), WHITE)
    cyl(0.03, 0.006, (0, py - 0.092, zb + 0.61), GOLD, shine=0.8, segs=12, rot=(math.pi / 2, 0, 0))
    box((0.14, 0.01, 0.2), (0, front - 0.004, zb + 0.1), WOOD, shine=0.2, bevel=0)
    box((0.06, 0.006, 0.05), (0, front - 0.008, zb + 0.24), WARM, LIGHT, bevel=0)
    # Часовая башня.
    cx, cy = 0.0, 0.3
    zt = zb + H
    box((0.26, 0.26, 0.5), (cx, cy, zt + 0.25), PLASTER, shine=0.15, bevel=0.01)
    for a in range(4):
        ang = a * math.pi / 2
        nx, ny = math.cos(ang), math.sin(ang)
        cyl(0.075, 0.01, (cx + nx * 0.131, cy + ny * 0.131, zt + 0.36), WHITE, LIGHT, segs=20, rot=(math.pi / 2, 0, ang + math.pi / 2))
        rod((cx + nx * 0.142, cy + ny * 0.142, zt + 0.36), (cx + nx * 0.142, cy + ny * 0.142, zt + 0.41), 0.005, DARK, segs=3)
        rod((cx + nx * 0.142, cy + ny * 0.142, zt + 0.36), (cx + nx * 0.142 - ny * 0.035, cy + ny * 0.142 + nx * 0.035, zt + 0.36), 0.004, DARK, segs=3)
    zbel = zt + 0.5
    box((0.3, 0.3, 0.03), (cx, cy, zbel + 0.015), STONE, shine=0.05, bevel=0.005)
    for sx in (-1, 1):
        for sy in (-1, 1):
            rod((cx + sx * 0.11, cy + sy * 0.11, zbel + 0.03), (cx + sx * 0.11, cy + sy * 0.11, zbel + 0.22), 0.018, PLASTER, segs=6)
    cyl(0.06, 0.09, (cx, cy, zbel + 0.06), GOLD, shine=0.8, segs=14, r2=0.035)
    box((0.3, 0.3, 0.03), (cx, cy, zbel + 0.235), STONE, shine=0.05, bevel=0.005)
    cyl(0.2, 0.42, (cx, cy, zbel + 0.25), ROOF, shine=0.2, segs=4, r2=0.0, spin=math.pi / 4, smooth=False)
    rod((cx, cy, zbel + 0.66), (cx, cy, zbel + 0.8), 0.006, GOLD, shine=0.8, segs=4)
    sphere(0.018, (cx, cy, zbel + 0.8), GOLD, shine=0.8, u=8, v=6)
    # Обсерватория на крыше.
    ox, oy = 0.4, 0.15
    cyl(0.11, 0.05, (ox, oy, zt + 0.12), STEEL, segs=20)
    dome(0.11, (ox, oy, zt + 0.17), METAL, shine=0.7, u=20, v=8)
    box((0.03, 0.1, 0.1), (ox, oy - 0.07, zt + 0.24), DARK, rot=(0.6, 0, 0), bevel=0)
    rod((ox, oy, zt + 0.24), (ox, oy - 0.13, zt + 0.33), 0.012, DARK, segs=6)
    # Двор.
    for side in (-1, 1):
        railing_line((side * 0.75, -0.45, z0), (side * 0.75, 0.55, z0), 0.08, WHITE, posts=8, r=0.008)
    railing_line((-0.75, 0.55, z0), (0.75, 0.55, z0), 0.08, WHITE, posts=11, r=0.008)
    railing_line((-0.75, -0.45, z0), (-0.3, -0.45, z0), 0.08, WHITE, posts=4, r=0.008)
    railing_line((0.3, -0.45, z0), (0.75, -0.45, z0), 0.08, WHITE, posts=4, r=0.008)
    box((0.5, 0.5, 0.006), (0, -0.7, z0 + 0.003), STONE, shine=0.05, bevel=0)
    rnd = random.Random(9)
    for (x, y) in ((-0.95, -0.25), (-0.9, 0.35), (0.95, 0.4), (0.62, -0.75), (-0.6, -0.8)):
        tree(x, y, z0, h=0.42 + rnd.random() * 0.12, rnd=rnd)
    box((0.18, 0.05, 0.015), (0.5, -0.32, z0 + 0.06), WOOD, shine=0.1, bevel=0.003)
    box((0.18, 0.012, 0.05), (0.5, -0.30, z0 + 0.09), WOOD, shine=0.1, bevel=0.003)
    for dx in (-0.07, 0.07):
        rod((0.5 + dx, -0.32, z0), (0.5 + dx, -0.32, z0 + 0.055), 0.008, DARK, segs=4)
    flag(-0.45, -0.32, z0, h=0.55)
    lamp_post(0.3, -0.62, z0, h=0.4)


def guard_tower():
    """Охрана: шестигранный бункер с бойницами, ствол с поясами и лестницей, площадка с перилами,
    застеклённая кабина, прожектор на вилке, радар, антенны, забор из столбиков с проволокой."""
    cyl(0.95, 0.06, (0, 0, 0), CONCRETE, shine=0.05, segs=12, bevel=0.01, smooth=False)
    z0 = 0.06
    cyl(0.5, 0.3, (0, 0, z0), CONCRETE, shine=0.05, segs=6, r2=0.46, bevel=0.02, smooth=False)
    for k in range(6):
        a = 2 * math.pi * (k + 0.5) / 6
        box((0.14, 0.02, 0.03), (0.46 * math.cos(a), 0.46 * math.sin(a), z0 + 0.2), DARK, rot=(0, 0, a + math.pi / 2), bevel=0)
    box((0.02, 0.12, 0.18), (0.475, 0, z0 + 0.09), STEEL, bevel=0.004)
    box((0.006, 0.03, 0.03), (0.487, 0, z0 + 0.21), RED, LIGHT, bevel=0)
    cyl(0.47, 0.03, (0, 0, z0 + 0.3), DARK, segs=6, smooth=False)
    zs = z0 + 0.33
    cyl(0.15, 1.15, (0, 0, zs), METAL, shine=0.5, segs=16, r2=0.11)
    for k in range(4):
        cyl(0.16 - k * 0.01, 0.03, (0, 0, zs + 0.15 + k * 0.27), WHITE if k == 1 else DARK, ACCENT if k == 1 else NORMAL, shine=0.4, segs=16)
    ladder((0.17, 0, zs), (0.13, 0, zs + 1.15), 0.07, STEEL, side=(0, 1, 0))
    rod((-0.13, 0.05, zs), (-0.1, 0.05, zs + 1.15), 0.01, DARK, segs=4)
    zd = zs + 1.15
    cyl(0.42, 0.05, (0, 0, zd), DARK, shine=0.4, segs=24, bevel=0.01)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rod((0.1 * math.cos(a), 0.1 * math.sin(a), zd - 0.2), (0.38 * math.cos(a), 0.38 * math.sin(a), zd), 0.01, STEEL, segs=4)
    railing_circle(0.4, zd + 0.05, 0.1, STEEL, posts=20)
    cyl(0.27, 0.06, (0, 0, zd + 0.05), METAL, segs=16)
    cyl(0.27, 0.16, (0, 0, zd + 0.11), GLASS, shine=0.95, segs=16)
    for k in range(8):
        a = 2 * math.pi * k / 8
        rod((0.272 * math.cos(a), 0.272 * math.sin(a), zd + 0.11), (0.272 * math.cos(a), 0.272 * math.sin(a), zd + 0.27), 0.008, DARK, segs=4)
    box((0.2, 0.01, 0.05), (0, -0.276, zd + 0.18), RED, LIGHT, bevel=0)
    cyl(0.31, 0.12, (0, 0, zd + 0.27), DARK, shine=0.4, segs=16, r2=0.12)
    sx, sy = 0.3, -0.18
    rod((sx, sy, zd + 0.05), (sx, sy, zd + 0.13), 0.012, DARK, segs=5)
    for dy in (-0.035, 0.035):
        rod((sx, sy + dy, zd + 0.13), (sx, sy + dy, zd + 0.2), 0.006, DARK, segs=4)
    lamp = mat((sx, sy, zd + 0.18), (0, math.radians(100), -0.5))
    cyl(0.04, 0.09, tuple(lamp @ Vector((0, 0, -0.045))), STEEL, shine=0.6, segs=14, rot=(0, math.radians(100), -0.5))
    cyl(0.036, 0.004, tuple(lamp @ Vector((0, 0, 0.046))), WARM, LIGHT, segs=14, rot=(0, math.radians(100), -0.5))
    zr = zd + 0.39
    rod((0, 0, zr), (0, 0, zr + 0.1), 0.015, STEEL, segs=6)
    box((0.32, 0.02, 0.08), (0, 0, zr + 0.13), METAL, shine=0.6, rot=(0.25, 0, 0.5), bevel=0.004)
    rod((-0.1, 0.06, zr - 0.05), (-0.1, 0.06, zr + 0.35), 0.006, DARK, segs=4)
    sphere(0.025, (-0.1, 0.06, zr + 0.36), RED, LIGHT, u=8, v=6)
    rod((0.1, 0.08, zr - 0.05), (0.1, 0.08, zr + 0.2), 0.005, DARK, segs=4)
    flag(-0.15, -0.1, zr - 0.08, h=0.2)
    n = 18
    for k in range(2, n):
        a = 2 * math.pi * k / n
        x, y = 0.88 * math.cos(a), 0.88 * math.sin(a)
        rod((x, y, z0), (x, y, z0 + 0.2), 0.012, STEEL, segs=5)
        rod((x, y, z0 + 0.2), (x * 1.04, y * 1.04, z0 + 0.25), 0.006, STEEL, segs=3)
        if k < n - 1:
            a2 = 2 * math.pi * (k + 1) / n
            for hz in (0.07, 0.13, 0.19):
                rod((x, y, z0 + hz), (0.88 * math.cos(a2), 0.88 * math.sin(a2), z0 + hz), 0.003, DARK, segs=3)
    lamp_post(0.7, 0.35, z0)


def ruins():
    """Руины: ступенчатое основание, каннелированные колонны с базами и капителями (часть обломана),
    арка с замковым камнем, упавшая колонна из барабанов, обелиск со светящимися знаками, кристаллы, камни."""
    rnd = random.Random(21)
    for k, r in enumerate((1.2, 1.0, 0.8)):
        cyl(r, 0.055, (0, 0, k * 0.05), STONE_DARK if k == 0 else STONE, shine=0.05, segs=12, bevel=0.012, smooth=False, spin=k * 0.2)
    z0 = 0.16
    heights = [1.1, 1.1, 0.75, 0.22, 0.55, 0.32, 0.9, 0.4]
    spots = []
    for i, h in enumerate(heights):
        a = math.radians(45 * i + 10)
        x, y = 0.66 * math.cos(a), 0.66 * math.sin(a)
        spots.append((x, y))
        box((0.2, 0.2, 0.05), (x, y, z0 + 0.025), STONE_DARK, shine=0.05, rot=(0, 0, a), bevel=0.01)
        cyl(0.075, h, (x, y, z0 + 0.05), STONE, shine=0.05, segs=12, smooth=False)
        for f in range(6):
            fa = 2 * math.pi * f / 6 + a
            box((0.012, 0.012, h * 0.95), (x + 0.075 * math.cos(fa), y + 0.075 * math.sin(fa), z0 + 0.05 + h / 2), STONE_DARK, shine=0.05,
                rot=(0, 0, fa), bevel=0)
        if h > 0.8 and i > 1:
            box((0.2, 0.2, 0.05), (x, y, z0 + 0.05 + h + 0.025), STONE, shine=0.05, rot=(0, 0, a), bevel=0.01)
        elif h <= 0.8:
            cyl(0.075, 0.08, (x + 0.02, y, z0 + 0.03 + h), STONE, shine=0.05, segs=12, rot=(0.35, 0.2, 0), smooth=False)
            rock(0.04, (x + 0.15, y + 0.05, z0 + 0.02), STONE, rnd)
    (x1, y1), (x2, y2) = spots[0], spots[1]
    for (x, y) in spots[:2]:
        box((0.2, 0.2, 0.05), (x, y, z0 + 1.175), STONE, shine=0.05, bevel=0.01)
    span = math.hypot(x2 - x1, y2 - y1)
    ang = math.atan2(y2 - y1, x2 - x1)
    box((span + 0.24, 0.17, 0.12), ((x1 + x2) / 2, (y1 + y2) / 2, z0 + 1.26), STONE, shine=0.05, rot=(0, 0, ang), bevel=0.012)
    box((0.1, 0.18, 0.16), ((x1 + x2) / 2, (y1 + y2) / 2, z0 + 1.3), STONE_DARK, shine=0.05, rot=(0, 0, ang), bevel=0.01)
    for k in range(3):
        cyl(0.07, 0.2, (-0.45 + k * 0.21, -0.85 + k * 0.03, 0.12), STONE, shine=0.05, segs=12,
            rot=(0, math.pi / 2, 0.15 + k * 0.08), smooth=False)
    # Обелиск со знаками.
    cyl(0.12, 0.7, (0, 0, z0), OBSIDIAN, shine=0.6, segs=4, r2=0.07, spin=0.3, smooth=False)
    cyl(0.07, 0.1, (0, 0, z0 + 0.7), OBSIDIAN, shine=0.6, segs=4, r2=0.0, spin=0.3, smooth=False)
    for k in range(4):
        a = k * math.pi / 2 + 0.3 + math.pi / 4
        for j in range(4):
            zz = z0 + 0.15 + j * 0.13
            rr = 0.12 - 0.05 * (zz - z0) / 0.7
            box((0.05 - j * 0.006, 0.006, 0.025), (rr * 0.71 * math.cos(a), rr * 0.71 * math.sin(a), zz), CYAN, LIGHT,
                rot=(0, 0, a + math.pi / 2), bevel=0)
    for k in range(6):
        a = rnd.random() * 2 * math.pi
        d = 0.25 + rnd.random() * 0.15
        cyl(0.04 + rnd.random() * 0.03, 0.18 + rnd.random() * 0.2, (d * math.cos(a), d * math.sin(a), z0 - 0.02), CYAN, LIGHT, segs=6, r2=0.0,
            rot=((rnd.random() - 0.5) * 0.8, (rnd.random() - 0.5) * 0.8, 0), smooth=False)
    for k in range(30):
        a = rnd.random() * 2 * math.pi
        d = 0.85 + rnd.random() * 0.3
        rock(0.03 + rnd.random() * 0.05, (d * math.cos(a), d * math.sin(a), 0.06), STONE if k % 2 else STONE_DARK, rnd)


# ----------------------------------------------------------------------
# Сборка и выгрузка
# ----------------------------------------------------------------------

def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    for i, name in enumerate(MODELS):
        globals()[name]()
        ob = join(name)
        # В файле модели стоят в ряд, чтобы их было удобно смотреть; выгружаются из начала координат.
        ob.location = ((i % 4) * 3.2, -(i // 4) * 3.5, 0)
        print(name, len(ob.data.vertices), "вершин,", len(ob.data.polygons), "граней")
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
            mesh_smooth_type="OFF",
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            bake_anim=False,
        )
        ob.location = saved
        print("Выгружено:", path)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--export-only" not in args:
        build_all()
    export_all()
