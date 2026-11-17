# Builds the tank as a small 3D block (voxel) model and renders it from the game's tilted
# overhead view at 32 headings, like the game's own cars (which are 3D models filmed from
# above). Writes two sprite sheets: hull (32 frames) and turret with roof gun (32 frames).
#
#   python art-src/render_angles.py <out_dir>
#
# Model units are decimetres (x forward, y left, z up). The turret ring sits at the hull's
# centre (0,0), so both sheets share the same ground origin at the centre of each frame.
import math, sys
import numpy as np
from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
TILT = math.radians(45)   # camera tilt from straight down (the game's car camera: 45)
PX = 2.3                  # pixels per decimetre
FRAMES = 32
LIGHT = np.array([-0.45, 0.55, 0.70]); LIGHT /= np.linalg.norm(LIGHT)

OLIVE = (96, 104, 60); TAN = (150, 128, 84); DARK = (64, 74, 42)
TRACK = (46, 44, 40); WHEEL = (70, 74, 52); STEEL = (56, 58, 60); BLACK = (20, 20, 18); MGSTEEL = (78, 80, 82)
GRILLE = (44, 48, 30); GLASS = (90, 150, 160); RED = (150, 40, 30); BRASS = (190, 150, 70)

rng = np.random.default_rng(5)

def noise2(nx, ny, scale, seed):
    r = np.random.default_rng(seed)
    gx, gy = nx // scale + 3, ny // scale + 3
    g = r.random((gy, gx))
    ys, xs = np.mgrid[0:ny, 0:nx]
    fx, fy = xs / scale, ys / scale
    ix, iy = fx.astype(int), fy.astype(int)
    tx, ty = fx - ix, fy - iy
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a = g[iy, ix] * (1 - tx) + g[iy, ix + 1] * tx
    b = g[iy + 1, ix] * (1 - tx) + g[iy + 1, ix + 1] * tx
    return a * (1 - ty) + b * ty

class Model:
    def __init__(self, x0, x1, y0, y1, z0, z1):
        self.o = np.array([x0, y0, z0])
        self.occ = np.zeros((x1 - x0, y1 - y0, z1 - z0), bool)
        self.col = np.zeros(self.occ.shape + (3,), np.uint8)
        xs, ys, zs = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5, np.arange(z0, z1) + 0.5, indexing="ij")
        self.X, self.Y, self.Z = xs, ys, zs
        n1 = noise2(x1 - x0, y1 - y0, 9, 11).T
        n2 = noise2(x1 - x0, y1 - y0, 5, 12).T
        v = n1 * 0.7 + n2 * 0.3
        camo = np.zeros((x1 - x0, y1 - y0, 3), np.uint8)
        camo[:] = OLIVE
        camo[v > 0.62] = TAN
        camo[v < 0.36] = DARK
        self.camo = np.repeat(camo[:, :, None, :], z1 - z0, axis=2)

    def add(self, mask, color=None):
        self.occ |= mask
        if color is None: self.col[mask] = self.camo[mask]
        else: self.col[mask] = color

    def paint(self, mask, color):
        m = mask & self.occ
        self.col[m] = color

# ----------------------------------------------------------------- hull
def build_hull():
    m = Model(-44, 45, -20, 21, 0, 16)
    X, Y, Z = m.X, m.Y, m.Z
    ay = np.abs(Y)
    # tracks with rounded ends
    track = (ay >= 11) & (ay <= 18) & (Z <= 9) & (np.abs(X) <= 39)
    end = np.abs(X) - 33
    track &= ~((end > 0) & (np.hypot(end, Z - 4.5) > 6.2))
    m.add(track, TRACK)
    # tread pattern on top of the tracks
    m.paint(track & (Z > 8) & (np.floor(X) % 3 == 0), (66, 63, 56))
    # road wheels on the outer faces
    for wx in range(-30, 34, 9):
        wheel = (ay >= 17.5) & (np.hypot(X - wx, Z - 4) <= 3.4)
        m.paint(wheel, WHEEL)
        m.paint(wheel & (np.hypot(X - wx, Z - 4) <= 1.2), STEEL)
    # hull body between and over the tracks
    body = (ay <= 11) & (Z <= 13) & (X >= -42) & (X <= 34)
    m.add(body)
    # sloped glacis at the front
    glacis = (ay <= 18) & (X > 30) & (X <= 44) & (Z <= 13 - (X - 30) * 0.75) & (Z >= 3)
    m.add(glacis)
    # fenders / side skirts over the tracks
    skirt = (ay > 11) & (ay <= 19) & (Z >= 7) & (Z <= 13) & (X >= -40) & (X <= 31)
    m.add(skirt)
    # armour block lines on the skirts
    m.paint(skirt & (ay > 18) & (np.floor(X) % 8 == 0) & (X > -10), (52, 58, 34))
    m.paint(skirt & (ay > 18) & (np.floor(Z) == 10) & (X > -10), (52, 58, 34))
    # rear engine deck grilles
    for gx in (-38, -26):
        for sy in (-1, 1):
            g = (Z >= 12.5) & (X >= gx) & (X <= gx + 9) & (sy * Y >= 2) & (sy * Y <= 10)
            m.paint(g, GRILLE)
            m.paint(g & (np.floor(Y) % 2 == 0), (34, 37, 22))
    # rear lights, driver hatch, front lights
    m.paint((X < -41) & (ay >= 14) & (ay <= 16) & (Z >= 9) & (Z <= 11), RED)
    m.paint((Z >= 12.5) & (X >= 24) & (X <= 29) & (ay <= 3), (40, 44, 28))
    m.paint((X > 40) & (ay >= 13) & (ay <= 15), (40, 40, 36))
    # turret ring (mostly hidden by the turret)
    ring = (Z >= 12.5) & (np.hypot(X, Y) <= 12) & (np.hypot(X, Y) >= 10.5)
    m.paint(ring, (60, 66, 40))
    return m

# ----------------------------------------------------------------- turret (+ barrel + roof gun)
def build_turret():
    m = Model(-34, 76, -18, 19, 13, 28)
    X, Y, Z = m.X, m.Y, m.Z
    ay = np.abs(Y)
    # angular turret: wide at the back, wedged at the front
    half = np.where(X < 10, 15.0, 15.0 - (X - 10) * 0.55)
    body = (X >= -24) & (X <= 26) & (ay <= half) & (Z >= 14) & (Z <= 21)
    body &= ~((X > 18) & (Z > 21 - (X - 18) * 0.6))
    m.add(body)
    # bustle rack at the back
    rack = (X >= -31) & (X < -24) & (ay <= 14) & (Z >= 15) & (Z <= 19)
    rack &= (np.floor(X) % 3 == 0) | (np.floor(Y) % 3 == 0) | (Z < 16)
    m.add(rack, (52, 56, 34))
    # mantlet and barrel
    mant = (X >= 24) & (X <= 29) & (ay <= 4.5) & (Z >= 15) & (Z <= 21)
    m.add(mant, (82, 90, 54))
    r = np.hypot(Y, Z - 18)
    barrel = (X >= 28) & (X <= 74) & (r <= 1.7)
    barrel |= (X >= 46) & (X <= 51) & (r <= 2.6)     # bore evacuator
    barrel |= (X >= 72) & (X <= 75) & (r <= 2.2)     # muzzle
    m.add(barrel, (82, 90, 54))
    m.paint(barrel & (X >= 46) & (X <= 51), (72, 80, 46))
    m.paint(barrel & (X >= 74.5) & (r <= 1.0), BLACK)
    # commander's cupola (right side) with vision blocks
    cup = (np.hypot(X + 8, Y + 7) <= 4) & (Z >= 21) & (Z <= 24)
    m.add(cup, (86, 94, 54))
    m.paint(cup & (Z <= 22) & (np.hypot(X + 8, Y + 7) >= 3.4), GLASS)
    # loader's hatch (left side) with the roof machine gun
    hatch = (np.hypot(X + 8, Y - 7) <= 3.5) & (Z >= 21) & (Z <= 22)
    m.add(hatch, (80, 88, 50))
    mg = (X >= -9) & (X <= 1) & (np.abs(Y - 7) <= 1) & (Z >= 22) & (Z <= 24)
    mg |= (X >= 1) & (X <= 12) & (np.abs(Y - 7) <= 0.5) & (Z >= 22.5) & (Z <= 23.5)
    m.add(mg, MGSTEEL)
    m.add((X >= -7) & (X <= -2) & (Y >= 8) & (Y <= 10) & (Z >= 22) & (Z <= 24), TAN)   # ammo can
    m.paint(mg & (X >= -9) & (X <= -8), (96, 62, 34))
    # gunner's sight
    sight = (X >= 10) & (X <= 16) & (Y >= -11) & (Y <= -5) & (Z >= 21) & (Z <= 23)
    m.add(sight, (74, 82, 48))
    m.paint(sight & (X >= 15.5), GLASS)
    # smoke launchers
    for sy in (-1, 1):
        m.add((X >= 16) & (X <= 20) & (sy * Y >= 12) & (sy * Y <= 15) & (Z >= 18) & (Z <= 20), (54, 60, 36))
    # antennas
    for sy in (-1, 1):
        m.add((np.abs(X + 22) <= 0.5) & (np.abs(Y - sy * 12) <= 0.5) & (Z >= 21) & (Z <= 24), (40, 40, 36))
    return m

# ----------------------------------------------------------------- renderer
def normals(m):
    occ = m.occ.astype(np.float32)
    p = np.pad(occ, 1)
    gx = p[2:, 1:-1, 1:-1] - p[:-2, 1:-1, 1:-1]
    gy = p[1:-1, 2:, 1:-1] - p[1:-1, :-2, 1:-1]
    gz = p[1:-1, 1:-1, 2:] - p[1:-1, 1:-1, :-2]
    n = -np.stack([gx, gy, gz], -1)
    # surface voxels only
    surf = m.occ & ((np.abs(n).sum(-1) > 0) | (p[1:-1, 1:-1, 2:] == 0))
    n[..., 2] += 0.35  # bias up so flat tops read as tops
    ln = np.linalg.norm(n, axis=-1, keepdims=True)
    n = n / np.maximum(ln, 1e-6)
    return surf, n

def render(m, deg, size, sub=3):
    surf, n = normals(m)
    idx = np.nonzero(surf)
    pts = np.stack([m.X[idx], m.Y[idx], m.Z[idx]], -1)
    cols = m.col[idx].astype(np.float32)
    nn = n[idx]
    a = math.radians(deg)
    ca, sa = math.cos(a), math.sin(a)
    rx = pts[:, 0] * ca - pts[:, 1] * sa
    ry = pts[:, 0] * sa + pts[:, 1] * ca
    rz = pts[:, 2]
    nx = nn[:, 0] * ca - nn[:, 1] * sa
    ny = nn[:, 0] * sa + nn[:, 1] * ca
    nz = nn[:, 2]
    shade = 0.55 + 0.6 * np.clip(nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2], 0, 1)
    cols = np.clip(cols * shade[:, None], 0, 255)
    ct, st = math.cos(TILT), math.sin(TILT)
    sx = rx
    sy = ry * ct + rz * st
    depth = rz * ct - ry * st
    S = size * sub
    scale = PX * sub
    px = np.round(S / 2 + sx * scale).astype(int)
    py = np.round(S / 2 - sy * scale).astype(int)
    img = np.zeros((S, S, 4), np.float32)
    zb = np.full((S, S), -1e9, np.float32)
    order = np.argsort(depth)
    rad = int(math.ceil(scale * 0.75))
    for dx in range(-rad, rad + 1):
        for dy in range(-rad, rad + 1):
            X = px[order] + dx; Y = py[order] + dy
            ok = (X >= 0) & (X < S) & (Y >= 0) & (Y < S)
            X, Y, d, c = X[ok], Y[ok], depth[order][ok], cols[order][ok]
            # later (nearer) points overwrite earlier ones: sorted ascending depth
            better = d >= zb[Y, X]
            X, Y, d, c = X[better], Y[better], d[better], c[better]
            zb[Y, X] = d
            img[Y, X, :3] = c
            img[Y, X, 3] = 255
    im = Image.fromarray(img.astype(np.uint8), "RGBA").resize((size, size), Image.NEAREST)
    return outline(im)

def outline(im):
    a = np.array(im)
    alpha = a[..., 3] > 0
    p = np.pad(alpha, 1)
    edge = ~alpha & (p[2:, 1:-1] | p[:-2, 1:-1] | p[1:-1, 2:] | p[1:-1, :-2])
    a[edge] = (30, 34, 22, 255)
    return Image.fromarray(a, "RGBA")

def sheet(model, size, name):
    cols = 8
    rows = FRAMES // cols
    out = Image.new("RGBA", (size * cols, size * rows), (0, 0, 0, 0))
    for i in range(FRAMES):
        fr = render(model, i * 360 / FRAMES, size)
        out.alpha_composite(fr, ((i % cols) * size, (i // cols) * size))
    out.save(f"{OUT}/{name}")
    return out

hull = build_hull()
tur = build_turret()
hs = sheet(hull, 216, "art.tank_hull32.png")
ts = sheet(tur, 360, "art.tank_turret32.png")

# preview: a few headings with the turret aimed differently
prev = Image.new("RGBA", (360 * 4, 360), (120, 116, 100, 255))
for k, (h, t) in enumerate(((0, 0), (4, 6), (10, 20), (24, 28))):
    hx, hy = (h % 8) * 216, (h // 8) * 216
    tx, ty = (t % 8) * 360, (t // 8) * 360
    base = k * 360
    prev.alpha_composite(hs.crop((hx, hy, hx + 216, hy + 216)), (base + 72, 72))
    prev.alpha_composite(ts.crop((tx, ty, tx + 360, ty + 360)), (base, 0))
prev.save(f"{OUT}/preview32.png")
print("done")
