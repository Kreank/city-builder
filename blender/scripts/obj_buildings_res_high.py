"""Wohnen (hoch): Wohntuerme und Hochhausscheiben. Masse = Grundstueck x 92 % (lot()), Strasse = -Y."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z = PLINTH


def rooftop(C, cx, cy, z, w=4, d=3, h=3.0, color="offwhite"):
    return [box(C, (w, d, h), (cx, cy, z), color), box(C, (w + 0.3, d + 0.3, 0.2), (cx, cy, z + h), "concrete_dark"),
            cyl(C, 0.9, 1.4, (cx + w / 2 + 1.4, cy, z), "metal", segs=10)]


def tower_h1(C):
    """3x3: Sockelgeschoss + 14-geschossiger Wohnturm mit Eckbalkonen."""
    P = [box(C, (21.0, 21.0, Z), (0, 0, 0), "concrete_dark")]
    P.append(box(C, (20.0, 20.0, 4.2), (0, 0, Z), "slate"))
    P += ribbon(C, 0, 0, 20.0, 20.0, Z + 2.0, 2.6, "glass_dark")
    P += flat_roof(C, 20.0, 20.0, Z + 4.2, (0, 0), "roof_green", "slate", ph=0.9)
    P += door(C, 0, -7.2 + 0, 2.2, 2.6, "glass_light", z=Z, canopy=None)
    P.append(box(C, (6.0, 2.0, 0.3), (0, -10.6, Z + 3.3), "slate"))
    fh, n = 3.0, 14
    z0 = Z + 4.2
    p, top = block(C, 0, 1.0, 14.0, 14.0, n, fh, wall="offwhite", z0=z0, plinth="slate", spacing=2.5,
                   win_size=(1.3, 1.6), margin=1.6)
    P += p
    for x in (-7.05, 7.05):   # vertikale Lisenen
        for y in (-6.0, 8.0):
            P.append(box(C, (0.5, 0.5, n * fh), (x, y, z0), "slate"))
    for i in range(1, n):
        P += balcony(C, -5.2, 1.0 - 7.0, z0 + i * fh, 3.0, 1.3, "white", "glass_light")
        P += balcony(C, 5.2, 1.0 - 7.0, z0 + i * fh, 3.0, 1.3, "white", "glass_light")
    P += flat_roof(C, 14.0, 14.0, top, (0, 1.0), ph=0.8, parapet="slate")
    P += rooftop(C, -2.0, 2.0, top)
    P += tree(C, -8.0, -8.0, 4.0, 1.2, seed=1)
    P += tree(C, 8.0, -8.0, 4.0, 1.2, seed=2)
    return P


def slab_h2(C):
    """2x3: zehngeschossige Hochhausscheibe mit bunten Loggien."""
    fh, n = 2.9, 10
    w, d = 12.6, 18.0
    P, top = block(C, 0, 1.0, w, d, n, fh, wall="white", z0=Z, spacing=2.4, win_size=(1.0, 1.4), sides="fbr",
                   margin=1.0, skip=lambda s, r, c: s == "f" and r == 0 and c in (2,))
    P += facade(C, 0, 1.0, w, d, [Z + fh * (i + 0.55) for i in range(n)], 2.4, (1.0, 1.4), "l")
    cols = ["pastel_blue", "pastel_yellow", "pastel_green", "pastel_pink"]
    for i in range(1, n):
        for k, x in enumerate((-4.2, 4.2)):
            P += balcony(C, x, 1.0 - d / 2, Z + i * fh, 3.2, 1.1, cols[(i + k) % 4], cols[(i + k) % 4])
    P += flat_roof(C, w, d, top, (0, 1.0), ph=0.6)
    P += rooftop(C, 0, 4.0, top, 3.6, 3.0, 2.6)
    P += door(C, 0, 1.0 - d / 2, 1.8, 2.4, "glass_dark", canopy="concrete_dark")
    P.append(box(C, (12.0, 3.0, 0.05), (0, -9.6, 0), "concrete"))
    P += bench(C, -3.5, -9.8, 0)
    P += trash_bin(C, -5.2, -9.8)
    return P


def tower_h3(C):
    """4x4: Sockel mit Dachgarten + 18-geschossiger Turm mit Krone."""
    S = lot(4) - 0.4
    P = [box(C, (S, S, Z), (0, 0, 0), "concrete_dark")]
    p, ptop = block(C, 0, 0, S - 1.0, S - 1.0, 2, 3.6, wall="sand", z0=Z, ribbon_windows=True, win_size=(0, 2.2),
                    win="glass_dark", plinth="concrete_dark")
    P += p
    P += flat_roof(C, S - 1.0, S - 1.0, ptop, (0, 0), "grass", "sand", ph=1.0)
    for (x, y) in ((-10, -10), (10, -10), (-10, 10), (10, 10), (-10, 0), (10, 0)):
        P += [o for o in tree(C, x, y, 4.0, 1.4, seed=x + y)]
        for o in P[-3:]:
            o.data.transform(Matrix.Translation((0, 0, ptop)))
    fh, n = 3.0, 18
    tw = 15.0
    p, top = block(C, 0, 2.0, tw, tw, n, fh, wall="cream", z0=ptop, plinth="sand", spacing=2.4,
                   win_size=(1.4, 1.8), margin=1.4, frame=None)
    P += p
    for i in range(0, n + 1, 3):
        P.append(box(C, (tw + 0.3, tw + 0.3, 0.35), (0, 2.0, ptop + i * fh - 0.17), "gold"))
    # Krone
    P.append(box(C, (tw - 3, tw - 3, 3.0), (0, 2.0, top), "sand"))
    P.append(box(C, (tw + 0.6, tw + 0.6, 0.5), (0, 2.0, top), "gold"))
    P.append(hip(C, tw - 3, tw - 3, 4.0, (0, 2.0, top + 3.0), "roof_green", overhang=0.3))
    P.append(cyl(C, 0.15, 5.0, (0, 2.0, top + 7.0), "metal", segs=6))
    P += door(C, 0, -(S - 1.0) / 2, 3.0, 2.8, "glass_light", canopy="gold")
    return P


def round_h4(C):
    """3x3: runder Wohnturm, 16 Geschosse, umlaufende Balkone, auf eckigem Sockel."""
    S = lot(3) - 0.4
    P = [box(C, (S, S, Z), (0, 0, 0), "concrete_dark")]
    p, ptop = block(C, 0, 0, S - 1.0, S - 1.0, 1, 4.5, wall="terracotta", z0=Z, ribbon_windows=True,
                    win_size=(0, 2.6), win="glass_dark")
    P += p
    P += flat_roof(C, S - 1.0, S - 1.0, ptop, (0, 0), "grass", "terracotta", ph=0.9)
    P += round_tower(C, 0, 0.6, 7.6, 16, 3.0, ptop, wall="white", glass="glass", slab="white", balcony="glass_light")
    top = ptop + 16 * 3.0
    P.append(cyl(C, 4.0, 2.4, (0, 0.6, top), "terracotta", segs=12))
    P.append(cyl(C, 7.7, 0.4, (0, 0.6, top), "terracotta", segs=16))
    P += door(C, 0, -(S - 1.0) / 2, 3.0, 2.8, "glass_light", canopy="white")
    for (x, y) in ((-8.0, -8.0), (8.0, -8.0), (-8.0, 8.0), (8.0, 8.0)):
        P += tree(C, x, y, 3.2, 1.0, seed=x * 3 + y)
        for o in P[-3:]:
            o.data.transform(Matrix.Translation((0, 0, ptop)))
    return P


def twin_h5(C):
    """4x4: zwei Wohntuerme (12 und 16 Geschosse) auf gemeinsamem Sockel mit Laeden."""
    S = lot(4) - 0.4
    P = [box(C, (S, S, Z), (0, 0, 0), "concrete_dark")]
    p, ptop = block(C, 0, 0, S - 1.0, S - 1.0, 1, 4.8, wall="slate", z0=Z, ribbon_windows=True,
                    win_size=(0, 3.0), win="glass_light")
    P += p
    P += flat_roof(C, S - 1.0, S - 1.0, ptop, (0, 0), "roof_light", "slate", ph=0.8)
    for x in (-7.0, -1.0, 6.0):
        P += sign(C, x, -(S - 1.0) / 2 - 0.05, Z + 3.6, 4.0, 0.7, ("red", "teal", "orange")[int(x) % 3], "white")
    for (tx, ty, n, wall) in ((-6.5, 4.5, 16, "pastel_blue"), (6.5, -2.5, 12, "cream")):
        p, top = block(C, tx, ty, 11.0, 11.0, n, 3.0, wall=wall, z0=ptop, plinth="slate", spacing=2.3,
                       win_size=(1.3, 1.6), margin=1.2)
        P += p
        for i in range(1, n):
            P += balcony(C, tx + 3.0, ty - 5.5, ptop + i * 3.0, 3.0, 1.2, "white", "glass_light")
        P += flat_roof(C, 11.0, 11.0, top, (tx, ty), ph=0.6)
        P.append(box(C, (3.0, 3.0, 2.4), (tx - 2.0, ty + 1.5, top), "slate"))
    return P


run_family({"Tower_H1": tower_h1, "Slab_H2": slab_h2, "Tower_H3": tower_h3, "Round_H4": round_h4,
            "Twin_H5": twin_h5}, "Buildings/ResidentialHigh",
           views={"": (1.0, -1.2, 0.6), "_back": (-1.0, 1.0, 0.6)})
