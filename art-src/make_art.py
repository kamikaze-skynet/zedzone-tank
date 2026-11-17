# Draws the tank's top-down pixel art (hull, turret, roof machine gun). Front faces +x (right).
# Original art: an M1-style main battle tank in green/tan camo, seen from straight above.
import math, random, sys
from PIL import Image, ImageDraw
OUT = sys.argv[1] if len(sys.argv) > 1 else "."
random.seed(7)

OLIVE = (96, 104, 60); TAN = (148, 126, 82); DARK = (66, 76, 44)
OUTL = (34, 38, 24); TRACK = (52, 50, 46); TRACK_HI = (78, 74, 66); TRACK_LO = (34, 32, 30)
STEEL = (58, 60, 62); STEEL_HI = (92, 94, 96); STEEL_LO = (36, 37, 38)

def noise_field(w, h, scale, seed):
    rnd = random.Random(seed)
    gw, gh = w // scale + 3, h // scale + 3
    g = [[rnd.random() for _ in range(gw)] for _ in range(gh)]
    def smooth(t): return t * t * (3 - 2 * t)
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            fx, fy = x / scale, y / scale
            ix, iy = int(fx), int(fy); tx, ty = smooth(fx - ix), smooth(fy - iy)
            a = g[iy][ix] * (1 - tx) + g[iy][ix + 1] * tx
            b = g[iy + 1][ix] * (1 - tx) + g[iy + 1][ix + 1] * tx
            out[y][x] = a * (1 - ty) + b * ty
    return out

def camo(w, h, seed):
    n1 = noise_field(w, h, 11, seed); n2 = noise_field(w, h, 7, seed + 1)
    def col(x, y):
        v = n1[y][x] * 0.7 + n2[y][x] * 0.3
        if v > 0.62: return TAN
        if v < 0.36: return DARK
        return OLIVE
    return col

def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c) + (255,)

def fill_mask(img, mask, colf, light=True):
    w, h = img.size; px = img.load()
    for y in range(h):
        for x in range(w):
            if mask[y][x]:
                c = colf(x, y)
                k = 1.0
                if light:
                    up = y > 0 and mask[y - 1][x]; dn = y < h - 1 and mask[y + 1][x]
                    lf = x > 0 and mask[y][x - 1]; rt = x < w - 1 and mask[y][x + 1]
                    if not up or not lf: k = 1.18
                    elif not dn or not rt: k = 0.78
                px[x, y] = shade(c, k)

def outline(img, mask, color=OUTL):
    w, h = img.size; px = img.load()
    for y in range(h):
        for x in range(w):
            if mask[y][x]: continue
            for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                xx, yy = x + dx, y + dy
                if 0 <= xx < w and 0 <= yy < h and mask[yy][xx]:
                    px[x, y] = color + (255,); break

def poly_mask(w, h, polys):
    m = Image.new("L", (w, h), 0); d = ImageDraw.Draw(m)
    for p in polys: d.polygon(p, fill=255)
    return [[m.getpixel((x, y)) > 0 for x in range(w)] for y in range(h)]

def rect(px, x0, y0, x1, y1, c):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1): px[x, y] = c + (255,) if len(c) == 3 else c

# ---------------- hull ----------------
def hull():
    W, H = 184, 100
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); px = img.load()
    # tracks
    for (y0, y1) in ((2, 21), (78, 97)):
        rect(px, 4, y0, 178, y1, TRACK)
        for x in range(4, 179):
            if (x // 3) % 2 == 0:
                for y in range(y0, y1 + 1): px[x, y] = TRACK_HI + (255,) if (x % 6 == 0) else TRACK + (255,)
            else:
                for y in range(y0, y1 + 1): px[x, y] = TRACK_LO + (255,) if x % 6 == 3 else TRACK + (255,)
        for x in range(4, 179):
            px[x, y0] = OUTL + (255,); px[x, y1] = OUTL + (255,)
        for y in range(y0, y1 + 1):
            px[4, y] = OUTL + (255,); px[178, y] = OUTL + (255,)
    # hull body: rear deck + sloped glacis to the front (right)
    body = [(8, 14), (150, 14), (176, 22), (180, 30), (180, 70), (176, 78), (150, 86), (8, 86), (6, 80), (6, 20)]
    m = poly_mask(W, H, [body])
    fill_mask(img, m, camo(W, H, 11))
    outline(img, m)
    # side skirts with armour blocks (above the tracks)
    cam = camo(W, H, 21)
    for (y0, y1) in ((10, 19), (81, 90)):
        mm = poly_mask(W, H, [[(14, y0), (148, y0), (148, y1), (14, y1)]])
        fill_mask(img, mm, cam); outline(img, mm)
        for bx in range(52, 140, 11):
            for y in range(y0 + 1, y1):
                px[bx, y] = OUTL + (255,)
    # glacis plate line + front lip
    for y in range(24, 77): px[150, y] = shade(OLIVE, 0.7)
    for y in range(28, 73): px[178, y] = shade(DARK, 0.8)
    # headlight / tow hooks
    for (y) in (26, 72):
        rect(px, 174, y - 1, 176, y + 1, (40, 40, 36))
    # rear engine deck grilles
    for gx0 in (14, 40):
        rect(px, gx0, 24, gx0 + 20, 44, (58, 64, 40))
        rect(px, gx0, 56, gx0 + 20, 76, (58, 64, 40))
        for y in list(range(25, 44, 3)) + list(range(57, 76, 3)):
            for x in range(gx0 + 1, gx0 + 20): px[x, y] = (40, 44, 28, 255)
        for x in range(gx0, gx0 + 21):
            px[x, 24] = OUTL + (255,); px[x, 44] = OUTL + (255,); px[x, 56] = OUTL + (255,); px[x, 76] = OUTL + (255,)
    # rear lights
    rect(px, 8, 22, 9, 25, (150, 40, 30)); rect(px, 8, 75, 9, 78, (150, 40, 30))
    # turret ring (dark well, mostly covered by the turret)
    cx, cy, r = 100, 50, 25
    for y in range(H):
        for x in range(W):
            d = math.hypot(x - cx, y - cy)
            if r - 3 <= d <= r: px[x, y] = shade(OLIVE, 0.62)
            elif d < r - 3: px[x, y] = (24, 24, 22, 255)
    # driver hatch near the front
    rect(px, 156, 44, 166, 56, shade(OLIVE, 0.85)); rect(px, 158, 46, 164, 54, (34, 38, 26))
    return img

# ---------------- turret ----------------
def turret(with_mg_mount=True):
    W, H = 224, 76
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); px = img.load()
    PX, PY = 64, 38   # pivot (turret ring centre)
    # barrel first (under the turret front)
    by = PY
    for x in range(110, 220):
        hw = 3
        if 150 <= x <= 160: hw = 5          # bore evacuator
        if x >= 214: hw = 4                 # muzzle
        for y in range(by - hw, by + hw + 1):
            k = 1.15 if y < by - 1 else (0.75 if y > by + 1 else 1.0)
            base = (78, 86, 52)
            if 150 <= x <= 160: base = (70, 78, 46)
            px[x, y] = shade(base, k)
        px[x, by - hw - 1] = OUTL + (255,); px[x, by + hw + 1] = OUTL + (255,)
        if x in (128, 129, 170, 171, 196): 
            for y in range(by - hw, by + hw + 1): px[x, y] = shade((78, 86, 52), 0.7)
    for y in range(by - 5, by + 6): px[220, y] = OUTL + (255,)
    px[219, by] = (16, 16, 14, 255); px[218, by] = (22, 22, 20, 255)
    # angular turret body, flat-faced wedge at the front
    body = [(20, 10), (88, 8), (114, 18), (118, 30), (118, 46), (114, 58), (88, 68), (20, 66), (14, 58), (14, 18)]
    m = poly_mask(W, H, [body])
    fill_mask(img, m, camo(W, H, 31))
    # bustle rack at the back
    rack = [(2, 14), (16, 14), (16, 62), (2, 62)]
    rm = poly_mask(W, H, [rack])
    for y in range(H):
        for x in range(W):
            if rm[y][x] and not m[y][x]:
                px[x, y] = (60, 62, 40, 255) if (x % 4 == 0 or y % 4 == 0) else (40, 42, 28, 255)
    outline(img, [[m[y][x] or rm[y][x] for x in range(W)] for y in range(H)])
    # mantlet
    rect(px, 112, by - 7, 119, by + 7, shade(OLIVE, 0.8))
    for y in range(by - 7, by + 8): px[120, y] = OUTL + (255,)
    # front cheek lines
    for t in range(0, 24):
        x = 88 + t; 
        px[x, 9 + t * 10 // 26] = shade(OLIVE, 0.7)
        px[x, 67 - t * 10 // 26] = shade(OLIVE, 0.7)
    # commander cupola (left of centre-line = top in image)
    def disc(cx, cy, r, c, ring=True):
        for y in range(cy - r - 1, cy + r + 2):
            for x in range(cx - r - 1, cx + r + 2):
                d = math.hypot(x - cx, y - cy)
                if d <= r: px[x, y] = shade(c, 1.15 if (x - cx) + (y - cy) < -r * 0.6 else (0.8 if (x - cx) + (y - cy) > r * 0.6 else 1.0))
                elif ring and d <= r + 1: px[x, y] = OUTL + (255,)
    disc(52, 24, 9, (88, 96, 56)); disc(52, 24, 5, (70, 78, 46))
    for a in range(0, 360, 45):
        x = 52 + round(7 * math.cos(math.radians(a))); y = 24 + round(7 * math.sin(math.radians(a)))
        px[x, y] = (110, 160, 150, 255)   # vision blocks
    # loader hatch (machine gun goes here)
    disc(50, 52, 8, (86, 94, 54)); disc(50, 52, 3, (60, 66, 40))
    # gunner's sight box
    rect(px, 80, 18, 92, 28, (74, 82, 48)); rect(px, 90, 20, 92, 26, (90, 150, 160))
    for x in range(80, 93): px[x, 17] = OUTL + (255,); px[x, 29] = OUTL + (255,)
    for y in range(17, 30): px[79, y] = OUTL + (255,); px[93, y] = OUTL + (255,)
    # antennas (dots)
    px[22, 16] = (20, 20, 20, 255); px[22, 60] = (20, 20, 20, 255)
    # smoke grenade launchers
    for yy in (12, 64):
        rect(px, 96, yy - 1, 102, yy + 1, (54, 60, 36))
    return img, (PX / W, PY / H)

# ---------------- roof machine gun ----------------
def mg():
    W, H = 56, 20
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0)); px = img.load()
    PX, PY = 12, 10
    # barrel
    for x in range(24, 54):
        for y in range(PY - 1, PY + 2): px[x, y] = shade(STEEL, 1.2 if y == PY - 1 else (0.8 if y == PY + 1 else 1))
        px[x, PY - 2] = OUTL + (255,); px[x, PY + 2] = OUTL + (255,)
    for x in range(24, 34):   # perforated jacket
        for y in range(PY - 2, PY + 3): px[x, y] = shade(STEEL, 0.9)
        if x % 2 == 0: px[x, PY] = (20, 20, 20, 255)
    rect(px, 52, PY - 2, 54, PY + 2, STEEL_LO)
    # receiver
    rect(px, 6, PY - 4, 24, PY + 4, STEEL)
    for x in range(6, 25): px[x, PY - 4] = STEEL_HI + (255,); px[x, PY + 4] = STEEL_LO + (255,)
    # spade grips
    rect(px, 2, PY - 3, 5, PY + 3, (96, 62, 34))
    # camo ammo can on the side
    rect(px, 12, PY + 5, 24, PY + 9, TAN); rect(px, 12, PY + 5, 24, PY + 5, OLIVE)
    for x in range(12, 25): px[x, PY + 9] = OUTL + (255,)
    # brass feed
    for x in range(14, 22, 2): px[x, PY + 4] = (200, 160, 70, 255)
    return img, (PX / W, PY / H)

h = hull(); h.save(f"{OUT}/tank_hull.png")
t, tp = turret(); t.save(f"{OUT}/tank_turret.png")
g, gp = mg(); g.save(f"{OUT}/tank_mg.png")
print("turret pivot", tp, "mg pivot", gp)
# preview composite
S = 4
prev = Image.new("RGBA", (320, 140), (190, 190, 186, 255))
prev.alpha_composite(h, (40, 20))
prev.alpha_composite(t, (40 + 100 - 64, 20 + 50 - 38))
prev.alpha_composite(g, (40 + 100 - 64 + 50 - 12, 20 + 50 - 38 + 52 - 10))
prev.resize((320 * S, 140 * S), Image.NEAREST).save(f"{OUT}/preview.png")
