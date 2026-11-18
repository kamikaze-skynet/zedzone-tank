"""Tank sounds made in code (no recordings): engine idle loop, engine drive loop, track loop.
Writes loops to src/sounds/<style>_{idle,drive,tracks}.wav and a listen-to preview per style
(idle, rev up, drive with tracks, slow down), built from the loops the way the game plays them.
Usage: python tank_sounds.py OUTDIR"""
import os, sys
import numpy as np
from scipy import signal

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "out"

def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="band", fs=SR, output="sos"); return signal.sosfilt(sos, x)
def lp(x, f, order=2):
    sos = signal.butter(order, f, btype="low", fs=SR, output="sos"); return signal.sosfilt(sos, x)
def hp(x, f, order=2):
    sos = signal.butter(order, f, btype="high", fs=SR, output="sos"); return signal.sosfilt(sos, x)
def reson(x, f, q):
    b, a = signal.iirpeak(f, q, fs=SR); return signal.lfilter(b, a, x)

def looped(fn, secs):
    """Render 2 loop lengths and keep the 2nd one, crossfading the 1st's tail in, so filters are
    settled and the loop joins without a click."""
    n = int(secs * SR)
    y = fn(2 * n)
    a, b = y[:n], y[n:]
    fade = int(0.08 * SR)
    w = np.linspace(0, 1, fade)
    out = b.copy()
    out[:fade] = b[:fade] * w + a[n - fade:] * (1 - w)   # head blends from a's end -> seamless wrap
    return out

def norm(x, peak=0.9):
    return x / (np.max(np.abs(x)) + 1e-9) * peak

def diesel(n, rpm, rng, load, cyl=12, rough=0.18):
    """Firing pulses per cylinder (uneven strength), through exhaust resonances."""
    fire = rpm / 60 * cyl / 2                      # firings per second
    x = np.zeros(n)
    t = 0.0
    k = 0
    strength = 1 + rough * rng.standard_normal(cyl)  # each cylinder a bit different -> chug
    while True:
        i = int(t * SR)
        if i >= n: break
        amp = strength[k % cyl] * (1 + 0.08 * rng.standard_normal())
        L = int(0.012 * SR)
        seg = rng.standard_normal(L) * np.exp(-np.arange(L) / (0.0025 * SR)) * amp
        x[i:i + L] += seg[: max(0, min(L, n - i))]
        t += 1 / fire * (1 + 0.03 * rng.standard_normal())
        k += 1
    y = (reson(x, 70 + rpm * 0.02, 3) * 1.4 + reson(x, 140 + rpm * 0.04, 4) + reson(x, 300 + rpm * 0.06, 5) * 0.6)
    y = lp(y, 900 + 400 * load, 3)
    body = norm(y)
    # deep sub rumble following the chug, and a little mechanical clatter
    sub = lp(np.abs(lp(x, 60)), 40) ; sub = norm(sub - sub.mean()) * 0.5
    clat = hp(x, 2500) ; clat = norm(clat) * (0.05 + 0.08 * load)
    return body + sub + clat

def turbine(n, spool, rng):
    t = np.arange(n) / SR
    f = 1800 + 2200 * spool
    whine = sum(np.sin(2 * np.pi * f * h * t + rng.uniform(0, 6)) / h**1.5 for h in (1, 2, 3))
    whine *= 0.25 * (1 + 0.05 * np.sin(2 * np.pi * 0.7 * t))
    hiss = bp(rng.standard_normal(n), 900, 7000) * (0.5 + 0.6 * spool)
    roar = lp(rng.standard_normal(n), 220, 4) * (2.5 + 2 * spool)
    return norm(whine * 0.5 + norm(hiss) * 0.55 + norm(roar) * 0.9)

def tracks(n, rng, rate=12.0, squeal=0.35):
    """Link clanks at `rate` per second + road-wheel rumble + intermittent squeal."""
    x = np.zeros(n)
    modes = np.array([380, 910, 1620, 2480, 3730])
    t = 0.0
    while True:
        i = int(t * SR)
        if i >= n: break
        L = int(0.09 * SR)
        tt = np.arange(L) / SR
        tune = 1 + 0.06 * rng.standard_normal()
        decays = np.array([0.045, 0.03, 0.02, 0.014, 0.009])
        hit = sum(np.sin(2 * np.pi * m * tune * tt + rng.uniform(0, 6)) * np.exp(-tt / d) / (j + 1) ** 0.6
                  for j, (m, d) in enumerate(zip(modes, decays)))
        click = rng.standard_normal(L) * np.exp(-tt / 0.002)
        amp = 0.6 + 0.4 * rng.random()
        seg = (hit * 0.7 + click * 0.5) * amp
        x[i:i + L] += seg[: max(0, min(L, n - i))]
        t += 1 / rate * (1 + 0.07 * rng.standard_normal())
    rumble = lp(rng.standard_normal(n), 120, 4)
    rumble = norm(rumble) * 0.35
    tq = np.arange(n) / SR
    sq = bp(rng.standard_normal(n), 1700, 2300, 4)
    sq = norm(sq) * (0.5 + 0.5 * np.sin(2 * np.pi * 0.37 * tq) ** 8) * squeal
    grit = norm(bp(rng.standard_normal(n), 300, 1500)) * 0.12
    return norm(norm(x) * 1.0 + rumble + sq + grit)

STYLES = {
    # name: (idle fn, drive fn, tracks fn)
    "diesel": (lambda n, r: diesel(n, 650, r, 0.1), lambda n, r: diesel(n, 1500, r, 0.9),
               lambda n, r: tracks(n, r, 12, 0.25)),
    "heavy_diesel": (lambda n, r: diesel(n, 520, r, 0.15, cyl=8, rough=0.3), lambda n, r: diesel(n, 1200, r, 1.0, cyl=8, rough=0.25),
                     lambda n, r: tracks(n, r, 10, 0.45)),
    "turbine": (lambda n, r: turbine(n, 0.0, r), lambda n, r: turbine(n, 0.7, r),
                lambda n, r: tracks(n, r, 12, 0.3)),
}

def wav(path, x):
    from scipy.io import wavfile
    wavfile.write(path, SR, (np.clip(x, -1, 1) * 32767).astype(np.int16))

def resample_pitch(loop, pitch, n, phase):
    """Play `loop` at `pitch` (array per output sample) like an AudioSource; returns samples, phase."""
    pos = phase + np.cumsum(pitch)
    idx = pos % len(loop)
    i0 = idx.astype(int); fr = idx - i0
    y = loop[i0] * (1 - fr) + loop[(i0 + 1) % len(loop)] * fr
    return y, pos[-1]

def preview(idle, drive, trk):
    # 2 s idle, 2 s rev up, 4 s drive, 2 s slow down -> idle 1 s.  Game: idle source fades out as the
    # drive loop fades in and rises in pitch; tracks follow speed.
    seg = [(2, 0, 0), (2, 0, 1), (4, 1, 1), (2, 1, 0), (1, 0, 0)]
    thr, spd = [], []
    for secs, a, b in seg:
        m = int(secs * SR); thr.append(np.linspace(a if a == b else a, b if a != b else a, m)); spd.append(np.linspace(a if a == b else a, b, m))
    thr = np.concatenate(thr); spd = lp(np.concatenate(spd), 0.6, 1)
    n = len(thr)
    yi, _ = resample_pitch(idle, np.full(n, 1.0), n, 0)
    yd, _ = resample_pitch(drive, 0.75 + 0.35 * thr, n, 0)
    yt, _ = resample_pitch(trk, 0.55 + 0.9 * np.clip(spd, 0, 1), n, 0)
    g = np.clip(thr, 0, 1)
    tv = np.clip(spd / 0.2, 0, 1)
    mix = yi * (1 - g) * 0.8 + yd * (0.3 + 0.7 * g) * g + yt * tv * 0.55
    fade = np.ones(n); fade[:2000] = np.linspace(0, 1, 2000); fade[-6000:] = np.linspace(1, 0, 6000)
    return norm(mix * fade, 0.85)

def main():
    os.makedirs(OUT, exist_ok=True)
    for name, (fi, fd, ft) in STYLES.items():
        rng = np.random.default_rng(7)
        idle = norm(looped(lambda n: fi(n, rng), 2.0))
        drive = norm(looped(lambda n: fd(n, rng), 2.0))
        trk = norm(looped(lambda n: ft(n, rng), 2.0))
        for k, x in (("idle", idle), ("drive", drive), ("tracks", trk)):
            wav(os.path.join(OUT, f"{name}_{k}.wav"), x * 0.9)
        wav(os.path.join(OUT, f"preview_{name}.wav"), preview(idle, drive, trk))
        print(name, "ok")

if __name__ == "__main__":
    main()
