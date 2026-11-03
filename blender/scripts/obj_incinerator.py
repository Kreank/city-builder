"""Muellverbrennungsanlage (Vorschlag 32 x 24 m): Bunkerhalle, Kesselhaus, Rauchgasreinigung, Schornstein."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def make(C):
    P = [box(C, (32, 24, Z0), (0, 0, 0), "concrete")]
    P.append(box(C, (16, 6, 0.04), (-6, -9, Z0), "asphalt"))
    S = []
    # Bunkerhalle (bunte Fassadenstreifen)
    bx, by, bw, bd, bh = -8.0, 3.0, 12.0, 14.0, 20.0
    S.append(box(C, (bw, bd, bh), (bx, by, 0), "offwhite"))
    for k, c in enumerate(("teal", "pastel_green", "teal", "pastel_blue", "teal")):
        S.append(box(C, (1.4, 0.2, bh - 2), (bx - 4.8 + k * 2.4, by - bd / 2 - 0.1, 1.0), c))
    S += flat_roof(C, bw, bd, bh, (bx, by), ph=0.6)
    # Anlieferung: drei Tore + Vordach
    for i in range(3):
        S.append(box(C, (3.0, 0.15, 4.2), (bx - 3.8 + i * 3.8, by - bd / 2 - 0.12, 0), "metal_dark"))
    S.append(box(C, (bw + 2, 4.0, 0.35), (bx, by - bd / 2 - 2.0, 5.0), "metal"))
    for x in (bx - 6.5, bx + 6.5):
        S.append(cyl(C, 0.2, 5.0, (x, by - bd / 2 - 3.6, 0), "metal_dark", segs=8))
    # Kesselhaus (hoeher, metallisch)
    kx, ky, kw, kd, kh = 3.0, 4.0, 10.0, 12.0, 26.0
    S.append(box(C, (kw, kd, kh), (kx, ky, 0), "metal"))
    for k in range(6):
        S.append(box(C, (0.3, 0.3, kh), (kx - kw / 2 + 0.5 + k * 1.8, ky - kd / 2 - 0.1, 0), "metal_dark"))
    S += ribbon(C, kx, ky, kw, kd, 18.0, 1.4, "glass_light", "flr")
    S += flat_roof(C, kw, kd, kh, (kx, ky), ph=0.6, parapet="metal_dark")
    # Rauchgasreinigung + Kanal zum Schornstein
    S.append(box(C, (6.0, 9.0, 12.0), (11.5, 4.5, 0), "steel_blue"))
    S += flat_roof(C, 6.0, 9.0, 12.0, (11.5, 4.5), ph=0.4)
    for y in (2.0, 7.0):
        S.append(cyl(C, 1.5, 6.0, (11.5, y, 12.0), "metal", segs=10, r_top=1.2))
    S.append(box(C, (2.4, 6.0, 2.4), (11.5, -1.5, 9.0), "metal_dark"))
    # Schornstein
    S.append(lathe(C, [(0, 0), (1.9, 0), (1.7, 52), (1.85, 52.2), (1.85, 54), (1.4, 54), (1.4, 53.5), (0, 53.5)],
                   (11.5, -5.5, 0), segs=12, colors=["metal", "metal", "teal", "teal", "black", "black", "black"]))
    S.append(lathe(C, [(1.74, 44), (1.8, 44), (1.8, 46), (1.74, 46)], (11.5, -5.5, 0), "teal", segs=12))
    # Verwaltung vorne rechts
    p, top = block(C, 4.5, -8.5, 7.0, 5.0, 2, 3.2, wall="white", z0=0.35, spacing=2.2, win_size=(1.2, 1.3),
                   sides="fl", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    S += p
    S += flat_roof(C, 7.0, 5.0, top, (4.5, -8.5), ph=0.4)
    S += door(C, 4.5, -11.0, 1.4, 2.3, "glass_dark", z=0.35, canopy="teal")
    S += sign(C, 4.5, -11.05, top - 1.0, 5.0, 0.7, "teal", "white")
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    P += garbage_truck(C, -11.0, -8.5, 0, "orange")
    P += garbage_truck(C, -4.0, -9.0, 15, "green")
    return P


build("Incinerator", "Services", make, views={"": (1.0, -1.15, 0.65), "_back": (-1.0, 1.0, 0.6)})
