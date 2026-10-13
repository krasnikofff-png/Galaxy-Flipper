"""Процедурная планета в стиле «между мультяшным и реалистичным». Типы: jungle, ice, rocky, gas, crystal, anomaly."""
import numpy as np, sys, time
from PIL import Image

def sat(x): return np.clip(x, 0, 1)
def mix(a, b, t): return a + (b - a) * (t[:, None] if np.ndim(t) else t)
def C(*rgb): return np.array(rgb, float) / 255.0

class Noise:
    G = np.array([[1,1,0],[-1,1,0],[1,-1,0],[-1,-1,0],[1,0,1],[-1,0,1],[1,0,-1],[-1,0,-1],[0,1,1],[0,-1,1],[0,1,-1],[0,-1,-1]], float)
    def __init__(self, seed):
        r = np.random.default_rng(seed)
        self.p = np.concatenate([r.permutation(256)] * 2)
    def perlin(self, q):
        pi = np.floor(q).astype(np.int64); f = q - pi; pi &= 255
        u = f*f*f*(f*(f*6-15)+10)
        p = self.p
        def g(ix, iy, iz, dx, dy, dz):
            gr = self.G[p[p[p[ix] + iy] + iz] % 12]
            return gr[:,0]*dx + gr[:,1]*dy + gr[:,2]*dz
        x, y, z = pi.T; fx, fy, fz = f.T; x1, y1, z1 = (x+1)&255, (y+1)&255, (z+1)&255
        a = g(x,y,z,fx,fy,fz);  b = g(x1,y,z,fx-1,fy,fz)
        c = g(x,y1,z,fx,fy-1,fz); d = g(x1,y1,z,fx-1,fy-1,fz)
        e = g(x,y,z1,fx,fy,fz-1); f_ = g(x1,y,z1,fx-1,fy,fz-1)
        h = g(x,y1,z1,fx,fy-1,fz-1); i = g(x1,y1,z1,fx-1,fy-1,fz-1)
        ux, uy, uz = u.T
        l0 = a+ux*(b-a); l1 = c+ux*(d-c); l2 = e+ux*(f_-e); l3 = h+ux*(i-h)
        m0 = l0+uy*(l1-l0); m1 = l2+uy*(l3-l2)
        return m0+uz*(m1-m0)
    def fbm(self, q, oct=6, off=0.0, gain=0.5, lac=2.0):
        s = 0; a = 1; f = 1; n = 0
        for o in range(oct):
            s = s + a*self.perlin(q*f + off + o*17.13); n += a; a *= gain; f *= lac
        return s/n
    def ridged(self, q, oct=5, off=31.7):
        s = 0; a = 1; f = 1; n = 0
        for o in range(oct):
            r = 1 - np.abs(self.perlin(q*f + off + o*9.1)); s = s + a*r*r; n += a; a *= 0.5; f *= 2.1
        return s/n
    def cells(self, q, scale, off=0):
        """Ячейки Вороного на сфере (через 3D-сетку): расстояние до ближайшей и второй точки, id ячейки."""
        qq = q*scale + off
        base = np.floor(qq).astype(np.int64)
        best = np.full(len(q), 9.0); second = np.full(len(q), 9.0); ids = np.zeros(len(q), np.int64)
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                for dz in (-1,0,1):
                    c = base + np.array([dx,dy,dz])
                    h = self.p[(self.p[(self.p[c[:,0]&255] + c[:,1])&511] + c[:,2])&511]
                    jit = np.stack([self.p[(h+1)&511], self.p[(h+2)&511], self.p[(h+3)&511]], -1)/255.0
                    d = np.linalg.norm(c + jit - qq, axis=1)
                    closer = d < best
                    second = np.where(closer, best, np.minimum(second, d))
                    ids = np.where(closer, h, ids); best = np.where(closer, d, best)
        return best, second, ids

# ---------------------------------------------------------------- типы планет
# Каждый тип возвращает: цвет, высоту (для рельефа), блеск (вода/лёд/металл), свечение, облака.

def jungle(n, Q):
    w = np.stack([n.fbm(Q*1.4,4,5.1), n.fbm(Q*1.4,4,11.7), n.fbm(Q*1.4,4,23.3)], -1)
    Qw = Q + 0.38*w
    elev = n.fbm(Qw*1.15, 7)*1.3 + 0.03
    land = elev > 0
    lat = np.abs(Q[:,1])
    moist = n.fbm(Q*2.0, 5, 101)*0.9 + 0.25
    mount = sat((n.ridged(Qw*3.0) - 0.70)*5) * sat(elev*5)
    depth = sat(-elev*5)
    col = mix(mix(C(48,190,190), C(20,120,160), sat(depth/0.25)), C(12,58,110), sat((depth-0.25)/0.75))
    # суша: тропическая зелень, сухие красноватые плато, пляжи
    grove = n.fbm(Q*10, 3, 77)
    tree = n.perlin(Q*110+3.3)*0.6 + n.perlin(Q*230+7.7)*0.4
    dry = sat((0.1 - moist)*3)
    base = mix(C(80,140,45), C(170,120,70), dry)                  # трава / саванна
    base = mix(base, C(190,95,60), sat((-0.2-moist)*4))           # красная земля
    beach = sat(1 - elev*30)
    forest = sat((grove*1.4 + moist*0.9)*2.5) * (1-beach) * (1-mount)
    treeness = sat((tree+0.1)*3.2) * forest
    leaf = mix(C(28,92,30), C(55,130,40), sat(tree*1.5+0.5))
    base = mix(base, leaf, treeness)
    base = mix(base, C(225,205,150), beach*0.9)
    base = mix(base, C(120,110,90), mount*0.85)
    base = mix(base, C(240,245,248), sat((mount-0.6)*3)*sat(elev*3))
    base = mix(base, C(235,240,245), sat((lat-0.86)*8))
    col[land] = base[land]
    height = np.where(land, elev*0.12 + mount*0.22 + treeness*0.012, -0.01)
    spec = np.where(land, 0.0, 1.0)
    cloud_amt = 0.58
    return dict(col=col, height=height, spec=spec, glow=None, cloud=cloud_amt, cloud_tint=C(255,255,255),
                atm=C(90,190,255), shallow=(~land)*sat(1-depth*4))

def ice(n, Q):
    w = np.stack([n.fbm(Q*1.6,4,5.1), n.fbm(Q*1.6,4,11.7), n.fbm(Q*1.6,4,23.3)], -1)
    Qw = Q + 0.3*w
    elev = n.fbm(Qw*1.3, 7)*1.2 - 0.05
    land = elev > 0
    cracks = n.ridged(Qw*6, 4, 13)
    crack = sat((cracks - 0.82)*7)
    floe_d, floe_d2, _ = n.cells(Qw + 0.08*w, 4.5, 3.3)
    floe_edge = sat(1 - (floe_d2 - floe_d)*14) * sat((n.fbm(Q*3,3,71)+0.1)*3)
    snowdrift = n.fbm(Q*14, 4, 44)
    # ледники: белый/голубой снег, торосы
    base = mix(C(225,238,248), C(190,215,240), sat(snowdrift*1.5+0.3))
    base = mix(base, C(120,170,215), crack*0.7)
    ridges = sat((n.ridged(Qw*2.5, 5, 61)-0.72)*5)*sat(elev*4)
    base = mix(base, C(160,190,215), ridges*0.5)
    # замёрзшее море: тёмный синий лёд в трещинах-льдинах
    sea = mix(C(150,190,220), C(95,145,190), sat(-elev*4))
    sea = mix(sea, C(150,195,225), floe_edge*0.45)
    sea = mix(sea, C(215,232,246), sat((n.fbm(Q*5,4,88)+0.1)*2.5)*0.75)
    sea = mix(sea, C(18,50,95), sat((floe_d2-floe_d)*2 - 0.1)*0.0)
    col = np.where(land[:,None], base, sea)
    height = np.where(land, elev*0.10 + ridges*0.16 - crack*0.02, -0.005 + floe_edge*0.01)
    spec = np.where(land, 0.35, 0.8)
    return dict(col=col, height=height, spec=spec, glow=None, cloud=0.25, cloud_tint=C(240,248,255),
                atm=C(160,215,255), shallow=None)

def rocky(n, Q):
    w = np.stack([n.fbm(Q*1.5,4,5.1), n.fbm(Q*1.5,4,11.7), n.fbm(Q*1.5,4,23.3)], -1)
    Qw = Q + 0.45*w
    elev = n.fbm(Qw*1.3, 7)
    canyon = sat((n.ridged(Qw*2.2, 5, 71) - 0.8)*6)
    mesa = np.round(sat(elev*1.6+0.5)*6)/6        # террасы-плато
    strata = np.sin(elev*60 + n.fbm(Q*8,2,9)*3)*0.5+0.5
    base = mix(C(150,95,60), C(190,140,95), mesa)
    base = mix(base, C(110,70,50), strata*0.25)
    base = mix(base, C(80,55,45), canyon*0.85)
    # кратеры
    cd, cd2, cid = n.cells(Q, 5, 8.8)
    rim = sat(1 - np.abs(cd - 0.32)*14) * (cid % 3 == 0)
    bowl = sat(1 - cd/0.3) * (cid % 3 == 0)
    base = mix(base, C(120,80,60), bowl*0.5)
    base = mix(base, C(215,170,120), rim*0.6)
    # металлические жилы — блестят
    vein = sat((n.ridged(Q*9, 3, 91) - 0.9)*12)
    base = mix(base, C(205,210,215), vein*0.8)
    height = mesa*0.10 - canyon*0.08 - bowl*0.05 + rim*0.04
    spec = vein*0.9
    return dict(col=base, height=height, spec=spec, glow=None, cloud=0.12, cloud_tint=C(235,205,170),
                atm=C(240,170,110), shallow=None)

def gas(n, Q):
    lat = Q[:,1]
    turb = n.fbm(Q*np.array([1.5,6,1.5]), 6, 5)
    swirl = n.fbm(Q*np.array([3,12,3]) + turb[:,None]*0.8, 5, 19)
    b = lat*7 + turb*1.8 + swirl*0.8
    band = np.sin(b)*0.5+0.5
    band2 = np.sin(b*2.3+1.1)*0.5+0.5
    base = mix(C(235,215,175), C(205,140,85), band)
    base = mix(base, C(150,90,60), sat(band2-0.6)*1.6)
    base = mix(base, C(250,240,220), sat(swirl*2)*0.3)
    # большое пятно-шторм
    spot_c = np.array([0.55,-0.3,0.78]); spot_c /= np.linalg.norm(spot_c)
    d = np.linalg.norm((Q - spot_c)*np.array([1,1.9,1]), axis=1)
    spot = sat(1 - d/0.2)
    ring = np.sin(d*60 + n.fbm(Q*10,2,3)*4)*0.5+0.5
    base = mix(base, mix(C(200,95,60), C(240,170,120), ring), spot**0.6)
    height = band*0.01
    return dict(col=base, height=height, spec=np.zeros(len(Q)), glow=None, cloud=0.0, cloud_tint=C(255,255,255),
                atm=C(255,210,150), shallow=None, soft=True)

def crystal(n, Q):
    w = np.stack([n.fbm(Q*1.4,4,5.1), n.fbm(Q*1.4,4,11.7), n.fbm(Q*1.4,4,23.3)], -1)
    Qw = Q + 0.3*w
    elev = n.fbm(Qw*1.3, 6)
    d1, d2, cid = n.cells(Qw, 14, 4.4)
    facet = (cid % 7)/7.0
    field = sat((n.fbm(Q*2.5, 4, 66) + 0.05)*4)          # кристальные поля
    edge = sat(1 - (d2-d1)*10)
    ground = mix(C(70,60,95), C(110,95,130), sat(elev+0.5))
    crys = mix(C(120,90,220), C(150,230,255), facet)
    crys = mix(crys, C(235,215,255), edge*0.7)
    base = mix(ground, crys, field)
    height = elev*0.08 + field*(0.06 - d1*0.08)
    spec = field*0.9
    glow = (field*edge*0.25)[:,None]*C(170,130,255)[None,:]
    return dict(col=base, height=height, spec=spec, glow=glow, cloud=0.15, cloud_tint=C(220,210,255),
                atm=C(190,140,255), shallow=None)

def anomaly(n, Q):
    w = np.stack([n.fbm(Q*2,4,5.1), n.fbm(Q*2,4,11.7), n.fbm(Q*2,4,23.3)], -1)
    Qw = Q + 0.6*w
    elev = n.fbm(Qw*1.5, 6)
    fiss = sat((n.ridged(Qw*3.0, 5, 13) - 0.74)*6)
    base = mix(C(45,35,70), C(95,75,125), sat(elev+0.5))
    base = mix(base, C(30,22,50), sat(n.fbm(Q*6,3,8)*2)*0.6)
    hue = sat(n.fbm(Q*1.2, 3, 2)*2+0.5)
    gc = mix(C(60,240,230), C(255,80,220), hue)
    glow = (fiss**1.2)[:,None]*gc*1.6
    height = elev*0.1 - fiss*0.05
    return dict(col=base, height=height, spec=np.zeros(len(Q))+0.2, glow=glow, cloud=0.0, cloud_tint=C(255,255,255),
                atm=C(200,90,255), shallow=None)

TYPES = dict(jungle=jungle, ice=ice, rocky=rocky, gas=gas, crystal=crystal, anomaly=anomaly)

def rot(axis, ang):
    axis = np.array(axis, float); axis /= np.linalg.norm(axis)
    K = np.array([[0,-axis[2],axis[1]],[axis[2],0,-axis[0]],[-axis[1],axis[0],0]])
    return np.eye(3) + np.sin(ang)*K + (1-np.cos(ang))*K@K

def render(kind, seed, size, out, ss=2, spin=0.9):
    t0 = time.time()
    n = Noise(seed)
    R = size*ss
    ys, xs = np.mgrid[0:R, 0:R]
    scale = 1.22
    u = ((xs+0.5)/R*2-1)*scale; v = (1-(ys+0.5)/R*2)*scale
    r2 = u*u+v*v; hit = r2 < 1
    z = np.sqrt(np.clip(1-r2,0,1))
    P = np.stack([u[hit], v[hit], z[hit]], -1)
    M = rot([0,1,0], spin) @ rot([1,0,0], -0.35)
    Q = P @ M
    surf = TYPES[kind](n, Q)
    col, height = surf['col'], surf['height']
    # рельеф: градиент высоты
    eps = 0.0025
    t1 = np.cross(Q, [0,1,1e-4]); t1 /= np.linalg.norm(t1,axis=1,keepdims=True); t2 = np.cross(Q, t1)
    hx = TYPES[kind](n, Q+t1*eps)['height']; hy = TYPES[kind](n, Q+t2*eps)['height']
    bump = 0.0 if surf.get('soft') else 1.0
    nrm = Q - bump*(((hx-height)/eps)[:,None]*t1 + ((hy-height)/eps)[:,None]*t2)
    nrm /= np.linalg.norm(nrm,axis=1,keepdims=True)
    Ncam = nrm @ M.T
    light = np.array([-0.5,0.4,0.78]); light /= np.linalg.norm(light)
    ndl = Ncam@light
    sun = sat((P@light)*1.8+0.25)                       # мягкий терминатор
    diff = sat(ndl*0.8+0.2)
    shade = 0.08 + 0.92*diff*sun
    c = col*shade[:,None]
    H = light+np.array([0,0,1.0]); H /= np.linalg.norm(H)
    c += (sat(Ncam@H)**70*surf['spec']*sun)[:,None]*0.6
    if surf.get('shallow') is not None:
        c += (surf['shallow']*0.10*sun)[:,None]*C(120,255,240)
    if surf['glow'] is not None:
        c += surf['glow']*(0.6+0.4*(1-sun))[:,None]
    # облака
    if surf['cloud'] > 0:
        cl = n.fbm(Q*2.4, 6, 200) + n.fbm(Q*8, 3, 300)*0.3
        thr = 0.55 - surf['cloud']*0.6
        cloud = sat((cl - thr + 0.15)*3.5)*0.9
        cl_s = n.fbm((Q - (light@M)*0.025)*2.4, 6, 200) + n.fbm((Q-(light@M)*0.025)*8, 3, 300)*0.3
        csh = sat((cl_s - thr + 0.15)*3.5)*0.35
        c *= (1 - (csh*sun)[:,None])
        cc = surf['cloud_tint']*(0.2+0.9*sat((P@light)*0.9+0.35))[:,None]
        c = c*(1-cloud[:,None]) + cc*cloud[:,None]
    # атмосфера по краю диска
    fres = (1-z[hit])**2.4
    c += surf['atm']*(fres*(0.7*sun+0.12))[:,None]
    # фон
    img = np.zeros((R,R,3))
    pts = np.stack([u.ravel()*0.7, v.ravel()*0.7, np.full(u.size,0.3)], -1)
    neb = n.fbm(pts*2.2, 5, 400).reshape(R,R)
    img += sat(neb*0.10+0.025)[:,:,None]*np.array([0.20,0.28,0.50])
    rs = np.random.default_rng(seed+1)
    st = rs.random((R,R)) > 0.9988
    img[st] += rs.random((st.sum(),1))*0.7+0.2
    img[hit] = c
    rr = np.sqrt(r2)
    halo = np.exp(-np.clip(rr-1,0,None)/0.05)*(~hit)
    side = np.clip((u*light[0]+v*light[1])/np.maximum(rr,1e-6)*0.6+0.55, 0.2, 1)
    img += (halo*side)[:,:,None]*surf['atm']*0.85
    img = np.clip(img,0,1)
    g = img.mean(-1,keepdims=True); img = np.clip(g+(img-g)*1.15,0,1)**0.95
    Image.fromarray((img*255).astype(np.uint8)).resize((size,size), Image.LANCZOS).save(out)
    print(kind, out, round(time.time()-t0,1), "s")

if __name__ == "__main__":
    kind = sys.argv[1]; seed = int(sys.argv[2]); size = int(sys.argv[3])
    render(kind, seed, size, f"{kind}_{seed}.png")
