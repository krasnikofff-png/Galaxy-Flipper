"""
Магнаты: 3D-бюсты для портретов во вкладке «Магнаты». Полумультяшные, в стиле планет и жителей.

Запуск (создаёт модели, сохраняет magnates.blend рядом и выгружает FBX в Unity):
    /Applications/Blender.app/Contents/MacOS/Blender --background --python tools/magnates/make_magnates.py

Свойства детали — в вершинах, как у построек (tools/buildings/make_buildings.py):
    цвет вершины RGB — цвет; альфа — роль: 1 — обычная, 0.5 — акцент (в Unity — цвет магната), 0 — светится;
    UV.x — блеск: 0 — кожа и ткань, 0.5 — лак и кожа обуви, 0.9 — стекло, золото, жемчуг.
Ось Z — вверх, лицо смотрит в −Y. Бюст от груди: низ на z = 0, макушка около z = 2.
Голова — подвижная часть «модель__head» с точкой вращения в шее: Unity покачивает её, грудь «дышит».

Голова лепится из сферы: оси растягиваются (ширина, глубина, высота, челюсть), а выпуклости и впадины
(щёки, надбровья, подбородок, брыли) — гауссовы бугры по направлению из центра. Нос, уши, глаза,
веки, брови, губы и всё остальное — отдельные гладкие детали.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(REPO, "tools", "buildings"))
import make_buildings as mb  # noqa: E402  (детали, роли, сборка моделей)

OUT_DIR = os.path.join(REPO, "unity", "Assets", "Resources", "Magnates")
BLEND = os.path.join(HERE, "magnates.blend")

NORMAL, ACCENT, LIGHT = mb.NORMAL, mb.ACCENT, mb.LIGHT
WHITE = (1.0, 1.0, 1.0)
EYE_WHITE = (0.97, 0.97, 0.94)
PUPIL = (0.04, 0.04, 0.05)
GOLD = (0.95, 0.72, 0.25)
GOLD_DARK = (0.7, 0.5, 0.16)
BLACK = (0.08, 0.08, 0.1)
PEARL = (0.96, 0.94, 0.9)
GLASS = (0.7, 0.85, 0.95)

HEAD_C = Vector((0, 0, 1.42))
NECK = Vector((0, 0, 0.98))

MODELS = ["magnate_hugo", "magnate_zizi", "magnate_matilda", "magnate_nova", "magnate_orpheus", "magnate_xenon", "magnate_shadow"]


# ----------------------------------------------------------------------
# Лепка
# ----------------------------------------------------------------------

def finish(bm, smooth=True, matrix=None):
    """Готовая сетка с раскраской в слоях bmesh → объект-деталь модели."""
    me = bpy.data.meshes.new("деталь")
    bm.to_mesh(me)
    bm.free()
    if matrix is not None:
        me.transform(matrix)
    for poly in me.polygons:
        poly.use_smooth = smooth
    ob = bpy.data.objects.new("деталь", me)
    bpy.context.scene.collection.objects.link(ob)
    mb.PARTS.append(ob)
    return ob


def paint_loops(bm, fn):
    """Раскраска по вершинам: fn(вершина) → (цвет, роль, блеск); пишется в слои цвета и UV, как у построек."""
    col = bm.loops.layers.color.new("Col")
    uv = bm.loops.layers.uv.new("UVMap")
    cache = {}
    for face in bm.faces:
        for loop in face.loops:
            v = loop.vert
            if v not in cache:
                cache[v] = fn(v)
            color, role, shine = cache[v]
            loop[col] = (color[0], color[1], color[2], role)
            loop[uv].uv = (shine, 0.5)


def painted(bm, paint, smooth=True, matrix=None):
    """Деталь с раскраской по вершинам: paint(точка, нормаль) → (цвет, роль, блеск)."""
    bm.normal_update()
    paint_loops(bm, lambda v: paint(v.co.copy(), v.normal.copy()))
    return finish(bm, smooth, matrix)


def bump(n, direction, amp, sigma):
    """Гауссов бугор на сфере: n и direction — единичные направления, sigma — ширина в радианах."""
    ang = n.angle(Vector(direction).normalized())
    return amp * math.exp(-(ang / sigma) ** 2)


def sculpt(center, r, size=(1, 1, 1), jaw=1.0, jaw_depth=1.0, bumps=(), paint=None, u=96, v=64, keep=None):
    """Голова, шапка волос, платок из сферы радиуса r: size — растяжение по осям, jaw — ширина низа
    (брыли > 1, острый подбородок < 1), bumps — [(направление, высота, ширина)], paint(направление) → (цвет, роль, блеск),
    keep(направление) — какие части оставить (шапке волос лицо не нужно)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    dirs = {}
    for vert in bm.verts:
        n = vert.co.normalized()
        dirs[vert] = n
        q = Vector((n.x * size[0], n.y * size[1], n.z * size[2]))
        if n.z < 0:
            q.x *= 1 + (jaw - 1) * (-n.z)
            q.y *= 1 + (jaw_depth - 1) * (-n.z)
        d = sum(bump(n, b[0], b[1], b[2]) for b in bumps)
        vert.co = Vector(center) + (q + n * d) * r
    # Раскраска — по исходному направлению вершины, а не по смятой точке: так проще рисовать лицо.
    paint_loops(bm, lambda vert: paint(dirs[vert]))
    if keep is not None:
        bmesh.ops.delete(bm, geom=[vert for vert in bm.verts if not keep(dirs[vert])], context="VERTS")
    bm.normal_update()
    return finish(bm, True)


def ellipsoid(c, radii, color, role=NORMAL, shine=0.2, rot=(0, 0, 0), u=32, v=20):
    return mb.sphere(1.0, tuple(c), color, role, shine, u=u, v=v, scale=radii, rot=rot)


def torus(c, R, r, color, role=NORMAL, shine=0.5, rot=(0, 0, 0), segs=48, tube=12, scale=(1, 1, 1)):
    """Кольцо: монокль, цепь, обруч. Ось кольца — Z до поворота."""
    bm = bmesh.new()
    rings = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ca, sa = math.cos(a), math.sin(a)
        ring = []
        for j in range(tube):
            b = 2 * math.pi * j / tube
            rr = R + r * math.cos(b)
            ring.append(bm.verts.new((rr * ca * scale[0], rr * sa * scale[1], r * math.sin(b) * scale[2])))
        rings.append(ring)
    for i in range(segs):
        a, b2 = rings[i], rings[(i + 1) % segs]
        for j in range(tube):
            j2 = (j + 1) % tube
            bm.faces.new((a[j], b2[j], b2[j2], a[j2]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mb.emit(bm, color, role, shine, True, mb.mat(tuple(c), rot))


def tube_path(points, r, color, role=NORMAL, shine=0.3, r2=None, segs=12):
    """Гладкая трубка по точкам (брови, усы, пряди): радиус от r к r2."""
    r2 = r if r2 is None else r2
    n = len(points) - 1
    for i, (a, b) in enumerate(zip(points, points[1:])):
        ra = r + (r2 - r) * i / n
        rb = r + (r2 - r) * (i + 1) / n
        mb.rod(a, b, ra, color, role, shine, segs, r2=rb)
        mb.sphere(rb, b, color, role, shine, u=segs, v=max(6, segs // 2))
    mb.sphere(r, points[0], color, role, shine, u=segs, v=max(6, segs // 2))


def curve(p0, p1, p2, n=8):
    return mb.bezier(p0, p1, p2, n)


def on_head(direction, r_out=1.0, size=(1, 1, 1), r=0.44):
    """Точка на поверхности головы по направлению (до бугров)."""
    d = Vector(direction).normalized()
    return HEAD_C + Vector((d.x * size[0], d.y * size[1], d.z * size[2])) * r * r_out


# ----------------------------------------------------------------------
# Лицо
# ----------------------------------------------------------------------

def eye(c, r, iris, look=(0, -1, 0), lid=0.25, lid_color=None, lid_tilt=0.0, lower=0.0, lash=False):
    """Глаз: белок, радужка с зрачком, два блика; верхнее веко опущено на lid (0 — открыт, 1 — закрыт),
    lid_tilt — наклон века (сердитый, хитрый); lower — нижнее веко (прищур)."""
    c = Vector(c)
    d = Vector(look).normalized()
    mb.sphere(r, tuple(c), EYE_WHITE, shine=0.7, u=24, v=16)
    # Радужка и зрачок — линзы на поверхности глаза: не выше века, иначе веко их не прикроет.
    rot = Vector((0, -1, 0)).rotation_difference(d).to_euler()
    ellipsoid(c + d * r * 0.86, (r * 0.62, r * 0.17, r * 0.62), iris, shine=0.8, rot=tuple(rot), u=24, v=12)
    ellipsoid(c + d * r * 0.93, (r * 0.32, r * 0.12, r * 0.32), PUPIL, shine=0.95, rot=tuple(rot), u=16, v=8)
    mb.sphere(r * 0.11, tuple(c + d * r * 0.95 + Vector((-r * 0.26, 0, r * 0.26))), WHITE, LIGHT, u=8, v=6)
    mb.sphere(r * 0.05, tuple(c + d * r * 0.96 + Vector((r * 0.2, 0, -r * 0.18))), WHITE, LIGHT, u=6, v=4)
    if lid_color is not None:
        lid_shell(c, r * 1.08, d, lid, lid_tilt, lid_color, top=True)
        if lower > 0:
            lid_shell(c, r * 1.07, d, lower, -lid_tilt * 0.5, lid_color, top=False)
    if lash:
        for k in range(5):
            a = -0.9 + k * 0.45
            base = c + Vector((math.sin(a) * r * 1.0, -math.cos(a) * r * 0.9, r * (0.55 - lid * 0.9)))
            tube_path([tuple(base), tuple(base + Vector((math.sin(a) * r * 0.35, -r * 0.2, r * 0.35)))], r * 0.05, BLACK, r2=r * 0.02, segs=6)


def lid_shell(c, R, d, cover, tilt, color, top=True):
    """Веко: кусок сферы поверх глаза. cover 0 — веко у края, 1 — закрыто наполовину и больше."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=16, radius=R)
    up = Vector((0, 0, 1 if top else -1))
    cut = 0.62 - cover * 0.95
    rot = Matrix.Rotation(tilt, 3, Vector(d))
    kill = [v for v in bm.verts if (rot @ v.co).dot(up) < cut * R or v.co.dot(d) < -0.25 * R]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    painted(bm, lambda co, n: (color, NORMAL, 0.25), True, Matrix.Translation(c))


def brow(c, side, width, lift=0.0, angle=0.0, color=(0.2, 0.14, 0.1), thick=0.022):
    """Бровь дугой над глазом: angle > 0 — сердитая (внутренний край ниже), lift — поднять."""
    c = Vector(c)
    inner = c + Vector((-side * width * 0.5, 0, -angle * width * 0.5 + lift))
    outer = c + Vector((side * width * 0.5, 0.03, angle * width * 0.5 + lift))
    mid = (inner + outer) / 2 + Vector((0, -0.02, width * 0.18))
    tube_path(curve(tuple(inner), tuple(mid), tuple(outer), 6), thick, color, r2=thick * 0.55, segs=10)


def lips(c, width, color, smile=0.0, open_=0.0, teeth=False, gap=False):
    """Губы: верхняя и нижняя трубками по дуге; smile — уголки вверх; open_ — рот приоткрыт."""
    c = Vector(c)
    def arc(dz, bulge, thick):
        a = c + Vector((-width / 2, 0.02, smile * width * 0.4 + dz))
        b = c + Vector((width / 2, 0.02, smile * width * 0.4 + dz))
        m = c + Vector((0, -0.02, dz + bulge))
        tube_path(curve(tuple(a), tuple(m), tuple(b), 8), thick, color, shine=0.35, r2=thick * 0.6, segs=10)
    if open_ > 0:
        ellipsoid(c + Vector((0, 0.01, -open_ * 0.5 - smile * width * 0.1)), (width * 0.42, 0.03, open_ * 0.75 + 0.01), (0.25, 0.04, 0.06), shine=0.3)
        if teeth:
            for k in range(6):
                if gap and k == 3:
                    continue
                x = -width * 0.3 + k * width * 0.12
                mb.box((width * 0.1, 0.02, open_ * 0.5), (x, c.y - 0.012, c.z - open_ * 0.08), (0.97, 0.96, 0.9), shine=0.6, bevel=0.004)
    arc(open_ * 0.15, -0.012 if smile > 0 else 0.006, width * 0.075)
    arc(-open_ * 0.95, -0.045 - smile * 0.03, width * 0.09)


def ear(side, color, size=1.0, pointy=False):
    c = HEAD_C + Vector((side * 0.42, 0.04, 0.02))
    ellipsoid(c, (0.04 * size, 0.09 * size, 0.12 * size), color, shine=0.15, rot=(0, side * 0.25, 0))
    ellipsoid(c + Vector((side * 0.02, -0.02, 0)), (0.02 * size, 0.055 * size, 0.08 * size), tuple(x * 0.82 for x in color), shine=0.1)


def skin_paint(skin, blush=None, lips_color=None, freckles=0, stubble=None, beard=None, rnd=None):
    """Раскраска головы по направлению: румянец на щеках, веснушки, щетина, борода."""
    spots = []
    if freckles and rnd is not None:
        for _ in range(freckles):
            a = rnd.uniform(-0.9, 0.9)
            spots.append(Vector((math.sin(a) * 0.9, -math.cos(a), rnd.uniform(-0.25, 0.05))).normalized())

    def paint(n):
        col = Vector(skin)
        if blush is not None:
            for sx in (-1, 1):
                w = bump(n, (sx * 0.62, -0.75, -0.18), 1.0, 0.22)
                col = col.lerp(Vector(blush), w * 0.55)
        for s in spots:
            if n.angle(s) < 0.022:
                col = col.lerp(Vector((0.85, 0.45, 0.6)), 0.55)
        if stubble is not None and n.z < -0.1 and n.y < 0.3:
            col = col.lerp(Vector(stubble), min(1, (-0.1 - n.z) * 3) * 0.45)
        if beard is not None and n.z < -0.05 and n.y < 0.5:
            mouth_zone = bump(n, (0, -0.85, -0.42), 1, 0.15)
            col = col.lerp(Vector(beard), min(1, (-0.05 - n.z) * 6) * (1 - mouth_zone * 0.3))
        return tuple(col), NORMAL, 0.12
    return paint


# ----------------------------------------------------------------------
# Тело
# ----------------------------------------------------------------------

def bust(cloth, width=0.66, fat=1.0, paint=None, cut_front=0.0, scale=1.0, trim=None):
    """Грудь и плечи: сечения-суперэллипсы снизу вверх — грудь, линия плеч, скаты к шее. Низ на z = 0."""
    def default(n):
        return cloth, NORMAL, 0.15
    # (высота, полуширина, полуглубина): fat раздувает низ — живот у Гуго.
    rows = [(0.0, 0.6 * (1 + 0.25 * (fat - 1)), 0.3 * fat), (0.3, 0.64 * (1 + 0.15 * (fat - 1)), 0.33 * fat),
            (0.52, 0.68, 0.32), (0.62, 0.66, 0.3), (0.7, 0.58, 0.27), (0.77, 0.42, 0.23), (0.82, 0.24, 0.17), (0.86, 0.15, 0.14)]
    segs = 64
    bm = bmesh.new()
    rings = []
    for z, w, d in rows:
        ring = []
        for k in range(segs):
            a = 2 * math.pi * k / segs
            c, s_ = math.cos(a), math.sin(a)
            # Суперэллипс: грудь и спина площе, бока круглее.
            e = 2.6
            x = w * width / 0.66 * math.copysign(abs(c) ** (2 / e), c)
            y = d * math.copysign(abs(s_) ** (2 / e), s_)
            ring.append(bm.verts.new((x, y, z)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            k2 = (k + 1) % segs
            bm.faces.new((a[k], a[k2], b[k2], b[k]))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # Сгладить ступеньки между сечениями.
    for _ in range(2):
        bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-6],
                                  cuts=1, use_grid_fill=True, smooth=1.0)
    if scale != 1.0:
        for v in bm.verts:
            v.co = Vector((v.co.x * scale, v.co.y * scale, v.co.z))
    if cut_front > 0:
        # Накидка открыта спереди: край — золотая кайма.
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < 0 and abs(v.co.x) < cut_front], context="VERTS")
    bm.normal_update()

    def paint_vertex(co, nr):
        if trim is not None and co.y < 0 and abs(co.x) < cut_front + 0.04:
            return trim, NORMAL, 0.9
        return (paint or default)(co, nr) if paint else default(nr)
    return painted(bm, paint_vertex, True)


def neck(color, r=0.16, fat=1.0):
    mb.cyl(r * fat, 0.3, (0, 0.02, 0.8), color, shine=0.12, segs=32, smooth=True, r2=r * fat * 0.92)


# ----------------------------------------------------------------------
# Магнаты
# ----------------------------------------------------------------------

def magnate_hugo():
    """Гуго Жаднер: грузный, лысый с бакенбардами, монокль, цилиндр, сигара, усы, бабочка, золотые пуговицы."""
    skin = (0.96, 0.7, 0.6)
    suit = (0.12, 0.12, 0.16)
    bust(suit, width=0.9, fat=1.25)
    # Рубашка, лацканы, жилет с цепочкой часов.
    ellipsoid((0, -0.36, 0.6), (0.2, 0.08, 0.3), (0.95, 0.94, 0.9), shine=0.1)
    for sx in (-1, 1):
        mb.box((0.12, 0.04, 0.42), (sx * 0.2, -0.4, 0.56), (0.18, 0.17, 0.22), shine=0.35, rot=(0.2, 0, sx * 0.35), bevel=0.02)
    ellipsoid((0, -0.26, 0.3), (0.34, 0.12, 0.3), (0.5, 0.12, 0.1), shine=0.3)
    for z in (0.22, 0.36):
        mb.sphere(0.022, (0, -0.52, z), GOLD, shine=0.9, u=12, v=8)
    tube_path(curve((-0.2, -0.5, 0.3), (-0.05, -0.56, 0.2), (0.16, -0.51, 0.29)), 0.008, GOLD, shine=0.9)
    neck(skin, fat=1.3)
    # Бабочка — акцент (цвет магната).
    for sx in (-1, 1):
        mb.cyl(0.06, 0.12, (sx * 0.0, -0.4, 0.88), (1, 1, 1), ACCENT, shine=0.4, r2=0.015, rot=(0, sx * math.pi / 2, 0), segs=16)
    mb.sphere(0.03, (0, -0.42, 0.88), (1, 1, 1), ACCENT, shine=0.4)

    with mb.moving("head", NECK):
        sculpt(HEAD_C, 0.44, size=(1.05, 0.95, 0.98), jaw=1.35, jaw_depth=1.1,
               bumps=[((0, -1, -0.55), 0.12, 0.3), ((0.55, -0.7, -0.25), 0.1, 0.25), ((-0.55, -0.7, -0.25), 0.1, 0.25),
                      ((0, -0.6, 0.45), 0.06, 0.35)],
               paint=skin_paint(skin, blush=(1, 0.45, 0.45), stubble=(0.55, 0.4, 0.38)))
        ear(1, skin)
        ear(-1, skin)
        # Нос картошкой, красноватый.
        ellipsoid(on_head((0, -1, -0.12), 1.02, (1.05, 0.95, 0.98)), (0.075, 0.07, 0.065), (0.98, 0.58, 0.52), shine=0.25)
        for sx in (-1, 1):
            e = on_head((sx * 0.36, -0.88, 0.14), 0.97, (1.05, 0.95, 0.98))
            eye(e, 0.075, (0.35, 0.55, 0.3), look=(sx * -0.05, -1, -0.05), lid=0.45, lid_color=skin, lid_tilt=sx * -0.2)
            brow(e + Vector((0, -0.02, 0.085)), sx, 0.13, angle=0.35, color=(0.62, 0.6, 0.6), thick=0.028)
            # Бакенбарды седые, пушистые.
            for k in range(6):
                ellipsoid(HEAD_C + Vector((sx * 0.43, 0.05 - k * 0.012, 0.05 - k * 0.07)), (0.05, 0.07, 0.06), (0.78, 0.76, 0.74), shine=0.05)
            # Усы: подкрученные.
            tube_path(curve(tuple(on_head((sx * 0.02, -1, -0.32), 1.05, (1.05, 0.95, 0.98))),
                            tuple(on_head((sx * 0.25, -0.95, -0.38), 1.08, (1.05, 0.95, 0.98))),
                            tuple(on_head((sx * 0.42, -0.85, -0.2), 1.1, (1.05, 0.95, 0.98)))), 0.04, (0.8, 0.78, 0.76), r2=0.012, shine=0.1)
        lips(on_head((0, -1, -0.45), 0.98, (1.05, 0.95, 0.98)), 0.15, (0.75, 0.38, 0.36), smile=0.25)
        # Монокль на правом глазу и цепочка.
        m = on_head((-0.36, -0.88, 0.14), 1.0, (1.05, 0.95, 0.98)) + Vector((0, -0.04, 0))
        torus(m, 0.075, 0.009, GOLD, shine=0.9, rot=(math.pi / 2, 0, 0))
        ellipsoid(m, (0.07, 0.005, 0.07), GLASS, shine=0.95)
        tube_path(curve(tuple(m + Vector((-0.07, 0, -0.02))), (-0.5, -0.4, 1.0), (-0.32, -0.5, 0.6), 10), 0.005, GOLD, shine=0.9)
        # Сигара с тлеющим кончиком.
        c0 = on_head((0.2, -1, -0.45), 1.02, (1.05, 0.95, 0.98))
        mb.rod(tuple(c0), tuple(c0 + Vector((0.26, -0.12, -0.06))), 0.022, (0.45, 0.28, 0.16), shine=0.15, segs=12)
        mb.sphere(0.023, tuple(c0 + Vector((0.26, -0.12, -0.06))), (1.0, 0.45, 0.12), LIGHT, u=10, v=6)
        mb.cyl(0.024, 0.03, tuple(c0 + Vector((0.06, -0.028, -0.014))), GOLD, shine=0.9, rot=(0, math.pi / 2 - 0.2, -0.43), segs=12)
        # Цилиндр с лентой в цвет магната.
        top = HEAD_C + Vector((0, 0.02, 0.36))
        mb.cyl(0.36, 0.03, tuple(top), BLACK, shine=0.45, segs=48, bevel=0.01, rot=(0.08, 0, 0))
        mb.cyl(0.25, 0.5, tuple(top + Vector((0, 0, 0.02))), BLACK, shine=0.45, segs=48, r2=0.27, rot=(0.08, 0, 0))
        mb.cyl(0.255, 0.08, tuple(top + Vector((0, -0.002, 0.04))), (1, 1, 1), ACCENT, shine=0.4, segs=48, rot=(0.08, 0, 0))


def magnate_zizi():
    """Зизи Квазар: лавандовая кожа, торчащие разноцветные пряди, гогглы на лбу, серьги-планеты, веснушки, щербатая улыбка."""
    rnd = random.Random(7)
    skin = (0.78, 0.7, 0.95)
    jacket = (0.36, 0.2, 0.55)
    bust(jacket, width=0.72, fat=0.95)
    # Нашивки на куртке.
    for (x, z, col) in [(-0.3, 0.45, (1, 0.8, 0.2)), (0.28, 0.32, (0.3, 0.9, 0.85)), (-0.1, 0.22, (1, 0.4, 0.6))]:
        mb.box((0.1, 0.02, 0.08), (x, -0.42, z), col, shine=0.1, rot=(0.25, 0, rnd.uniform(-0.4, 0.4)), bevel=0.01)
    mb.sphere(0.035, (0.2, -0.43, 0.55), (1, 1, 1), ACCENT, shine=0.6, u=10, v=6)
    ellipsoid((0, -0.35, 0.7), (0.22, 0.08, 0.12), (0.2, 0.9, 0.8), shine=0.2)
    neck(skin, r=0.17)

    with mb.moving("head", NECK):
        size = (0.98, 0.95, 1.02)
        sculpt(HEAD_C, 0.43, size=size, jaw=0.85, jaw_depth=0.95,
               bumps=[((0.5, -0.75, -0.2), 0.06, 0.25), ((-0.5, -0.75, -0.2), 0.06, 0.25), ((0, -0.7, -0.75), 0.04, 0.25)],
               paint=skin_paint(skin, blush=(1, 0.55, 0.75), freckles=14, rnd=rnd))
        ear(1, skin, 1.1, pointy=True)
        ear(-1, skin, 1.1, pointy=True)
        ellipsoid(on_head((0, -1, -0.1), 1.0, size), (0.05, 0.05, 0.05), (0.85, 0.65, 0.95), shine=0.2)
        for sx in (-1, 1):
            e = on_head((sx * 0.36, -0.86, 0.12), 0.96, size)
            eye(e, 0.085, (0.95, 0.4, 0.7), look=(sx * 0.12, -1, 0.05), lid=0.0, lid_color=skin)
            brow(e + Vector((0, -0.02, 0.11)), sx, 0.12, lift=0.02, angle=-0.25, color=(0.95, 0.35, 0.6), thick=0.02)
            # Серьги-планеты с колечком.
            ep = HEAD_C + Vector((sx * 0.45, 0.02, -0.18))
            tube_path([tuple(ep), tuple(ep + Vector((0, 0, -0.06)))], 0.006, GOLD, shine=0.9)
            mb.sphere(0.045, tuple(ep + Vector((0, 0, -0.1))), (0.3, 0.9, 0.85) if sx > 0 else (1, 0.7, 0.2), shine=0.5, u=16, v=10)
            torus(ep + Vector((0, 0, -0.1)), 0.068, 0.006, GOLD, shine=0.9, rot=(0.5, 0.3 * sx, 0))
        lips(on_head((0, -1, -0.42), 0.97, size), 0.19, (0.6, 0.25, 0.5), smile=0.6, open_=0.045, teeth=True, gap=True)
        # Гогглы на лбу.
        band = HEAD_C + Vector((0, 0, 0.2))
        torus(band, 0.445, 0.02, (0.3, 0.2, 0.15), shine=0.3, scale=(1, 0.98, 1))
        for sx in (-1, 1):
            g = on_head((sx * 0.3, -0.85, 0.5), 1.05, size)
            mb.cyl(0.09, 0.07, tuple(g), (0.6, 0.45, 0.25), shine=0.8, segs=24, rot=(math.pi / 2 + 0.5, 0, sx * 0.3))
            ellipsoid(g + Vector((0, -0.06, -0.03)), (0.075, 0.015, 0.075), (0.4, 0.9, 1.0), shine=0.95, rot=(0.5, 0, sx * 0.3))
        # Волосы: торчащие во все стороны пряди трёх цветов.
        colors = [(1.0, 0.35, 0.6), (0.3, 0.9, 0.85), (1.0, 0.85, 0.25)]
        for k in range(46):
            a = rnd.uniform(0, 2 * math.pi)
            up = rnd.uniform(0.25, 1.0)
            d = Vector((math.cos(a) * math.sqrt(1 - up * up), math.sin(a) * math.sqrt(1 - up * up) + 0.25, up)).normalized()
            if d.y < -0.5 and d.z < 0.6:
                continue
            base = HEAD_C + d * 0.4
            tip = base + (d + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), 0.4))).normalized() * rnd.uniform(0.22, 0.42)
            mb.cyl(0.07, (tip - base).length, tuple(base), colors[k % 3], shine=0.25, r2=0.0, segs=10,
                   rot=(Vector((0, 0, 1)).rotation_difference(tip - base)).to_euler())
        sculpt(HEAD_C + Vector((0, 0.02, 0.04)), 0.445, size=(1.0, 0.98, 1.03), paint=lambda n: (colors[0], NORMAL, 0.25), u=64, v=40,
               keep=lambda n: n.z > 0.62 or (n.y > 0.05 and n.z > -0.2))


def magnate_matilda():
    """Матильда Ласкова: серебряные волосы с пучком, очки на цепочке, жемчуг, брошь, высокий воротник."""
    skin = (0.96, 0.84, 0.78)
    dress = (0.18, 0.42, 0.45)
    bust(dress, width=0.68, fat=0.95)
    # Высокий кружевной воротник.
    mb.cyl(0.17, 0.16, (0, 0.0, 0.8), (0.95, 0.94, 0.92), shine=0.1, segs=32, r2=0.2)
    for k in range(18):
        a = math.pi * 2 * k / 18
        mb.sphere(0.018, (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.96), (0.95, 0.94, 0.92), shine=0.1, u=8, v=5)
    # Жемчужное ожерелье и брошь-камея в цвет магната.
    for k in range(22):
        a = math.pi * (0.1 + 0.8 * k / 21)
        mb.sphere(0.022, (math.cos(a) * 0.3, -0.12 - math.sin(a) * 0.24, 0.72 - math.sin(a) * 0.08), PEARL, shine=0.9, u=12, v=8)
    ellipsoid((0, -0.4, 0.55), (0.06, 0.02, 0.075), (1, 1, 1), ACCENT, shine=0.85)
    torus((0, -0.405, 0.55), 0.065, 0.008, GOLD, shine=0.9, rot=(math.pi / 2, 0, 0), scale=(1, 1.25, 1))
    neck(skin, r=0.17)

    with mb.moving("head", NECK):
        size = (0.92, 0.95, 1.02)
        sculpt(HEAD_C, 0.42, size=size, jaw=0.9, jaw_depth=0.95,
               bumps=[((0.5, -0.78, -0.15), 0.05, 0.25), ((-0.5, -0.78, -0.15), 0.05, 0.25)],
               paint=skin_paint(skin, blush=(1, 0.6, 0.6)))
        ear(1, skin, 0.9)
        ear(-1, skin, 0.9)
        ellipsoid(on_head((0, -1, -0.08), 1.0, size), (0.04, 0.06, 0.07), skin, shine=0.2)
        for sx in (-1, 1):
            e = on_head((sx * 0.34, -0.88, 0.12), 0.97, size)
            eye(e, 0.07, (0.35, 0.5, 0.75), look=(0, -1, 0), lid=0.3, lid_color=skin, lid_tilt=sx * 0.15, lower=0.15)
            brow(e + Vector((0, -0.02, 0.09)), sx, 0.11, angle=-0.15, color=(0.7, 0.7, 0.72), thick=0.014)
            # Очки: оправа и стёкла; дужки к ушам.
            torus(e + Vector((0, -0.06, -0.005)), 0.07, 0.007, GOLD, shine=0.9, rot=(math.pi / 2, 0, 0))
            ellipsoid(e + Vector((0, -0.06, -0.005)), (0.066, 0.004, 0.066), GLASS, shine=0.95)
            tube_path([tuple(e + Vector((sx * 0.07, -0.06, 0))), tuple(HEAD_C + Vector((sx * 0.41, 0.0, 0.08)))], 0.006, GOLD, shine=0.9)
            mb.sphere(0.022, tuple(HEAD_C + Vector((sx * 0.44, -0.02, -0.12))), PEARL, shine=0.9, u=10, v=6)
        tube_path([tuple(on_head((-0.34, -0.88, 0.12), 0.93, size) + Vector((0.07, -0.06, 0))),
                   tuple(on_head((0.34, -0.88, 0.12), 0.93, size) + Vector((-0.07, -0.06, 0)))], 0.006, GOLD, shine=0.9)
        # Цепочка очков вниз к плечам.
        for sx in (-1, 1):
            tube_path(curve(tuple(HEAD_C + Vector((sx * 0.41, -0.02, 0.06))), (sx * 0.45, -0.2, 0.9), (sx * 0.12, -0.3, 0.85), 10), 0.004, GOLD, shine=0.9)
        lips(on_head((0, -1, -0.42), 0.97, size), 0.15, (0.75, 0.38, 0.45), smile=0.35)
        # Причёска: гладкая шапка волос и пучок на затылке, с волнами.
        hair = (0.82, 0.83, 0.86)
        sculpt(HEAD_C + Vector((0, 0.03, 0.05)), 0.445, size=(0.95, 0.98, 1.0),
               paint=lambda n: (hair, NORMAL, 0.35), u=64, v=40,
               keep=lambda n: n.z > 0.35 or (n.y > 0.1 and n.z > -0.35) or (abs(n.x) > 0.8 and n.z > 0.0))
        # Лоб обрамляет валик волос.
        tube_path(curve(tuple(on_head((-0.6, -0.7, 0.45), 1.02, size)), tuple(on_head((0, -0.95, 0.62), 1.05, size)),
                        tuple(on_head((0.6, -0.7, 0.45), 1.02, size)), 10), 0.06, hair, shine=0.35)
        ellipsoid(HEAD_C + Vector((0, 0.3, 0.38)), (0.2, 0.18, 0.18), hair, shine=0.35)
        torus(HEAD_C + Vector((0, 0.3, 0.38)), 0.17, 0.02, (1, 1, 1), ACCENT, shine=0.6, rot=(1.1, 0, 0))


def magnate_nova():
    """Нова Рики: оранжевый загар, огромная блондинистая укладка, золотые цепи, кольца в ушах, жвачка пузырём, леопардовый мех."""
    rnd = random.Random(11)
    skin = (0.98, 0.66, 0.42)
    top = (0.95, 0.75, 0.35)

    # Леопард: розетки — тёмное кольцо с рыжей серединой, по точкам на груди и спине (в плоскости x–z).
    spots = [(rnd.uniform(-0.75, 0.75), rnd.uniform(0.05, 0.8)) for _ in range(45)]

    def leopard(co, n):
        for x, z in spots:
            d = math.hypot(co.x - x, co.z - z)
            if d < 0.045:
                return ((0.22, 0.13, 0.07) if d > 0.025 else (0.8, 0.52, 0.22)), NORMAL, 0.1
        return top, NORMAL, 0.1

    bust(top, width=0.72, fat=1.05, paint=leopard)
    # Меховой воротник: белые пушистые шарики.
    for k in range(26):
        a = math.pi * 2 * k / 26
        ellipsoid((math.cos(a) * 0.3, math.sin(a) * 0.24 - 0.04, 0.82 + rnd.uniform(-0.03, 0.03)), (0.09, 0.09, 0.08), (0.98, 0.95, 0.98), shine=0.02, u=14, v=10)
    # Золотые цепи: три кольца звеньями.
    for j, (rx, z, sag) in enumerate([(0.22, 0.78, 0.1), (0.27, 0.74, 0.2), (0.32, 0.7, 0.3)]):
        pts = []
        for k in range(17):
            a = math.pi * (0.0 + 1.0 * k / 16)
            pts.append((math.cos(a) * rx, -0.05 - math.sin(a) * (0.2 + j * 0.05), z - math.sin(a) * sag))
        tube_path(pts, 0.018 + j * 0.004, GOLD, shine=0.95, segs=10)
    mb.cyl(0.07, 0.02, (0, -0.41, 0.38), GOLD, shine=0.9, rot=(math.pi / 2, 0, 0), segs=24)
    mb.box((0.06, 0.01, 0.06), (0, -0.425, 0.38), (1, 1, 1), ACCENT, shine=0.8, rot=(0, math.pi / 4, 0), bevel=0)
    neck(skin, r=0.17)

    with mb.moving("head", NECK):
        size = (0.95, 0.95, 1.0)
        sculpt(HEAD_C, 0.42, size=size, jaw=0.95, jaw_depth=1.0,
               bumps=[((0.5, -0.78, -0.2), 0.08, 0.22), ((-0.5, -0.78, -0.2), 0.08, 0.22)],
               paint=skin_paint(skin, blush=(1, 0.42, 0.42)))
        ear(1, skin)
        ear(-1, skin)
        ellipsoid(on_head((0, -1, -0.1), 1.0, size), (0.04, 0.05, 0.05), skin, shine=0.2)
        for sx in (-1, 1):
            e = on_head((sx * 0.35, -0.87, 0.12), 0.97, size)
            eye(e, 0.078, (0.35, 0.6, 0.85), look=(0, -1, 0), lid=0.4, lid_color=(0.4, 0.75, 0.95), lid_tilt=sx * 0.1, lash=True)
            brow(e + Vector((0, -0.02, 0.1)), sx, 0.12, lift=0.02, angle=-0.4, color=(0.35, 0.22, 0.12), thick=0.014)
            # Огромные золотые кольца в ушах.
            torus(HEAD_C + Vector((sx * 0.45, 0.0, -0.22)), 0.1, 0.012, GOLD, shine=0.95, rot=(0, math.pi / 2, 0))
        lips(on_head((0, -1, -0.43), 0.97, size), 0.17, (0.95, 0.12, 0.3), smile=0.2, open_=0.03)
        # Пузырь жвачки.
        mb.sphere(0.11, tuple(on_head((0.05, -1, -0.45), 1.18, size)), (1.0, 0.55, 0.8), shine=0.75, u=28, v=18)
        # Причёска: облако локонов — шаровая укладка.
        hair = (0.98, 0.86, 0.45)
        for k in range(110):
            a = rnd.uniform(0, 2 * math.pi)
            up = rnd.uniform(-0.3, 1.0)
            d = Vector((math.cos(a) * math.sqrt(1 - up * up), math.sin(a) * math.sqrt(1 - up * up), up)).normalized()
            if d.y < -0.35 and d.z < 0.55:
                continue
            shade = rnd.uniform(0.85, 1.05)
            ellipsoid(HEAD_C + Vector((0, 0.08, 0.22)) + Vector((d.x * 0.52, d.y * 0.46, d.z * 0.5)),
                      (0.125, 0.125, 0.12), tuple(min(1, c * shade) for c in hair), shine=0.3, u=16, v=10)
        # Розовые очки в волосах.
        for sx in (-1, 1):
            g = on_head((sx * 0.25, -0.75, 0.75), 1.1, size)
            torus(g, 0.07, 0.01, (1, 0.4, 0.7), shine=0.8, rot=(math.pi / 2 - 0.8, 0, 0))
            ellipsoid(g, (0.065, 0.005, 0.065), (1, 0.5, 0.75), shine=0.95, rot=(-0.8, 0, 0))


def magnate_orpheus():
    """Орфей Младший: зализанный кок, надменный прищур, поднятая бровь, белый китель с эполетами, шейный платок."""
    skin = (0.98, 0.87, 0.8)
    coat = (0.95, 0.94, 0.9)
    bust(coat, width=0.74, fat=0.95)
    for sx in (-1, 1):
        # Эполеты с бахромой.
        ellipsoid((sx * 0.55, -0.02, 0.62), (0.16, 0.12, 0.05), GOLD, shine=0.9, rot=(0, sx * -0.3, 0))
        for k in range(7):
            mb.cyl(0.012, 0.09, (sx * (0.45 + k * 0.03), -0.08, 0.5), GOLD, shine=0.9, segs=8)
        # Пуговицы двумя рядами.
        for z in (0.2, 0.33, 0.46):
            mb.sphere(0.022, (sx * 0.12, -0.44, z), GOLD, shine=0.9, u=12, v=8)
    # Воротник-стойка и шейный платок (аскот) в цвет магната.
    mb.cyl(0.17, 0.14, (0, 0.0, 0.8), coat, shine=0.15, segs=32, r2=0.18)
    ellipsoid((0, -0.2, 0.78), (0.13, 0.1, 0.14), (1, 1, 1), ACCENT, shine=0.45)
    ellipsoid((0, -0.26, 0.64), (0.07, 0.05, 0.1), (1, 1, 1), ACCENT, shine=0.45)
    mb.sphere(0.022, (0, -0.31, 0.72), GOLD, shine=0.95, u=12, v=8)
    neck(skin, r=0.17)

    with mb.moving("head", NECK):
        size = (0.9, 0.95, 1.05)
        # Подбородок вздёрнут: голова чуть откинута назад.
        sculpt(HEAD_C, 0.42, size=size, jaw=0.88, jaw_depth=1.05,
               bumps=[((0, -0.75, -0.7), 0.07, 0.22), ((0.5, -0.8, -0.1), 0.04, 0.25), ((-0.5, -0.8, -0.1), 0.04, 0.25)],
               paint=skin_paint(skin, blush=(1, 0.7, 0.7)))
        ear(1, skin, 0.9)
        ear(-1, skin, 0.9)
        # Нос прямой, задран.
        n0 = on_head((0, -1, 0.02), 0.98, size)
        mb.rod(tuple(n0), tuple(n0 + Vector((0, -0.07, -0.1))), 0.03, skin, shine=0.2, segs=16, r2=0.045)
        mb.sphere(0.045, tuple(n0 + Vector((0, -0.07, -0.1))), skin, shine=0.2, u=16, v=10)
        for sx in (-1, 1):
            e = on_head((sx * 0.33, -0.88, 0.14), 0.97, size)
            eye(e, 0.068, (0.3, 0.45, 0.95), look=(sx * 0.1, -1, 0.1), lid=0.75, lid_color=skin, lid_tilt=sx * -0.1, lower=0.2)
            # Правая бровь вскинута.
            brow(e + Vector((0, -0.02, 0.09)), sx, 0.11, lift=0.05 if sx < 0 else 0.0, angle=-0.35 if sx < 0 else 0.1,
                 color=(0.2, 0.14, 0.12), thick=0.016)
        # Ухмылка на одну сторону.
        lips(on_head((0.04, -1, -0.43), 0.97, size), 0.14, (0.8, 0.5, 0.5), smile=0.15)
        mb.sphere(0.012, tuple(on_head((0.2, -0.96, -0.36), 0.99, size)), (0.75, 0.5, 0.48), shine=0.2, u=8, v=6)
        # Зализанные волосы и кок.
        hair = (0.18, 0.12, 0.1)
        sculpt(HEAD_C + Vector((0, 0.04, 0.06)), 0.44, size=(0.93, 0.98, 1.0),
               paint=lambda n: (hair, NORMAL, 0.85), u=64, v=40,
               keep=lambda n: n.z > 0.45 or (n.y > 0.15 and n.z > -0.3) or (abs(n.x) > 0.75 and n.z > 0.1))
        tube_path(curve(tuple(on_head((-0.45, -0.65, 0.55), 1.02, size)), tuple(on_head((0.1, -0.9, 0.95), 1.15, size)),
                        tuple(on_head((0.55, -0.5, 0.6), 1.05, size)), 12), 0.11, hair, shine=0.85, r2=0.05, segs=20)
        tube_path(curve(tuple(on_head((-0.3, -0.5, 0.8), 1.04, size)), tuple(on_head((0.2, -0.7, 1.0), 1.12, size)),
                        tuple(on_head((0.6, -0.3, 0.7), 1.05, size)), 10), 0.08, hair, shine=0.85, r2=0.04, segs=16)


def magnate_xenon():
    """Ксенон: платок с обручем, зеркальный визор вместо глаз, ухоженная борода, белое одеяние с золотой каймой, светящийся камень."""
    skin = (0.78, 0.58, 0.42)
    robe = (0.96, 0.95, 0.93)
    # Белое одеяние спереди, тёмная накидка по плечам и бокам, между ними — золотая кайма.
    cloak = (0.1, 0.09, 0.13)

    def robe_paint(co, n):
        edge = 0.2 + 0.05 * co.z
        if co.y < 0 and abs(co.x) < edge:
            return robe, NORMAL, 0.12
        if co.y < 0 and abs(co.x) < edge + 0.035:
            return GOLD, NORMAL, 0.9
        return cloak, NORMAL, 0.3
    bust(robe, width=0.76, fat=1.0, paint=robe_paint)
    # Камень на груди — светится цветом магната.
    mb.sphere(0.06, (0, -0.44, 0.4), (1, 1, 1), ACCENT, shine=0.95, u=20, v=12)
    mb.sphere(0.03, (0, -0.49, 0.42), (0.9, 1.0, 1.0), LIGHT, u=10, v=6)
    torus((0, -0.44, 0.4), 0.065, 0.01, GOLD, shine=0.9, rot=(math.pi / 2, 0, 0))
    neck(skin, r=0.18)

    with mb.moving("head", NECK):
        size = (0.92, 0.95, 1.04)
        sculpt(HEAD_C, 0.42, size=size, jaw=0.95, jaw_depth=1.0,
               bumps=[((0, -0.75, -0.7), 0.06, 0.25)],
               paint=skin_paint(skin, beard=(0.1, 0.08, 0.08)))
        # Борода: аккуратный объём по челюсти.
        sculpt(HEAD_C + Vector((0, -0.02, -0.06)), 0.43, size=(0.93, 0.95, 1.0), jaw=1.0,
               bumps=[((0, -0.8, -0.6), 0.08, 0.3)],
               paint=lambda n: ((0.1, 0.08, 0.08), NORMAL, 0.15), u=64, v=40,
               keep=lambda n: n.z < -0.12 and n.y < 0.15 and bump(n, (0, -0.85, -0.42), 1, 0.2) < 0.5)
        ellipsoid(on_head((0, -1, -0.05), 1.0, size), (0.045, 0.06, 0.075), skin, shine=0.2)
        lips(on_head((0, -1, -0.42), 1.0, size), 0.12, (0.45, 0.28, 0.24), smile=0.0)
        # Визор: тёмное зеркальное стекло во всю ширину глаз, с отблеском в цвет магната.
        v0 = on_head((0, -0.92, 0.14), 1.0, size)
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=24, radius=1.0)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y > -0.25 or abs(v.co.z) > 0.28], context="VERTS")
        for v in bm.verts:
            v.co = Vector((v.co.x * 0.41, v.co.y * 0.45, v.co.z * 0.42)) + HEAD_C + Vector((0, -0.02, 0.13))
        painted(bm, lambda co, n: ((0.05, 0.06, 0.09), NORMAL, 0.97), True)
        tube_path([tuple(v0 + Vector((-0.36, 0.06, 0.07))), tuple(v0 + Vector((0.36, 0.06, 0.07)))], 0.006, (1, 1, 1), ACCENT)
        # Платок: ткань от макушки до плеч, обруч из двух чёрных колец.
        cloth = (0.97, 0.97, 0.95)
        sculpt(HEAD_C + Vector((0, 0.05, 0.04)), 0.47, size=(0.98, 1.0, 1.02),
               paint=lambda n: (cloth, NORMAL, 0.08), u=64, v=40,
               keep=lambda n: not (n.y < -0.35 and n.z < 0.5) and n.z > -0.6)
        # Края платка ниспадают за ушами до плеч: задняя половина расширяющегося книзу «колокола».
        bm = bmesh.new()
        prof = [(0.84, 0.62), (0.95, 0.56), (1.1, 0.5), (1.3, 0.48), (1.45, 0.47)]
        rings = []
        for z, rr in prof:
            rings.append([bm.verts.new((rr * math.cos(2 * math.pi * k / 48) * 0.95, rr * math.sin(2 * math.pi * k / 48) * 0.85 + 0.06, z)) for k in range(48)])
        for a, b in zip(rings, rings[1:]):
            for k in range(48):
                bm.faces.new((a[k], a[(k + 1) % 48], b[(k + 1) % 48], b[k]))
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.y < -0.12], context="VERTS")
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        painted(bm, lambda co, n: (cloth, NORMAL, 0.08), True)
        for k, z in enumerate((0.25, 0.3)):
            torus(HEAD_C + Vector((0, 0.06, z)), 0.455 - k * 0.012, 0.03, BLACK, shine=0.35, rot=(-0.2, 0, 0), scale=(0.98, 1.0, 1))
        torus(HEAD_C + Vector((0, 0.06, 0.275)), 0.47, 0.007, GOLD, shine=0.9, rot=(-0.2, 0, 0), scale=(0.98, 1.0, 1))


def magnate_shadow():
    """Безымянный магнат: тёмный силуэт в шляпе с горящими глазами — пока не знаем, кто это."""
    shade = (0.13, 0.13, 0.17)
    bust(shade, width=0.74)
    neck(shade, r=0.12)
    with mb.moving("head", NECK):
        sculpt(HEAD_C, 0.42, size=(0.95, 0.95, 1.0), paint=lambda n: (shade, NORMAL, 0.2))
        for sx in (-1, 1):
            ellipsoid(on_head((sx * 0.33, -0.9, 0.1), 1.0), (0.06, 0.02, 0.03), (1, 1, 1), LIGHT, rot=(0, sx * 0.2, 0))
        mb.cyl(0.5, 0.03, (0, 0.02, 1.78), (0.09, 0.09, 0.12), shine=0.3, segs=48)
        mb.cyl(0.3, 0.26, (0, 0.02, 1.8), (0.09, 0.09, 0.12), shine=0.3, segs=48, r2=0.26)
        mb.cyl(0.305, 0.06, (0, 0.02, 1.81), (1, 1, 1), ACCENT, shine=0.3, segs=48)


# ----------------------------------------------------------------------
# Сборка и выгрузка
# ----------------------------------------------------------------------

def build_all():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i, name in enumerate(MODELS):
        globals()[name]()
        ob = mb.join(name)
        ob.location = (i * 2.2, 0, 0)
        verts = len(ob.data.vertices) + sum(len(c.data.vertices) for c in ob.children if c.type == "MESH")
        print(name, verts, "вершин")
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("Сохранено:", BLEND)


def export_all():
    mb.OUT_DIR = OUT_DIR
    mb.MODELS = MODELS
    mb.export_all()


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--export-only" not in args:
        build_all()
    export_all()
