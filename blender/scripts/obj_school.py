"""Schule (G4-Dienst, Vorschlag 32 x 24 m): L-foermiger Bau, Schulhof mit Basketballfeld."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def make(C):
    P = [box(C, (32, 24, Z0), (0, 0, 0), "grass")]
    P.append(box(C, (22, 13, 0.04), (5, -5.5, Z0), "concrete"))                  # Hof
    P.append(box(C, (3, 4, 0.04), (0, -10, Z0), "concrete"))
    # Haupttrakt (hinten, Satteldach)
    cx, cy, w, d = -4, 6.5, 22, 9
    parts, top = block(C, cx, cy, w, d, 2, fh=3.6, wall="plaster_warm", z0=Z0 + 0.35, spacing=2.6,
                       win_size=(1.8, 1.9), frame="white",
                       skip=lambda s, r, c: s == "f" and r == 0 and c in (5,))
    P += parts
    P.append(box(C, (w + 0.1, d + 0.1, 0.3), (cx, cy, Z0 + 0.35 + 3.5), "terracotta"))
    P += gable(C, w, d, 3.0, (cx, cy, top), "roof_red", overhang=0.5, gable_color="plaster_warm")
    # Eingang mit Vordach + Uhr
    ex = cx + 5 * 2.6 - 2.6 * (int((w - 2 + 2.6 - 1.8) // 2.6) - 1) / 2
    P += door(C, ex, cy - d / 2, 2.2, 2.6, "glass_dark", z=Z0 + 0.35, canopy="terracotta")
    P.append(cyl(C, 0.75, 0.12, (ex, cy - d / 2 - 0.02, 6.2), "white", segs=16, rot=(90, 0, 0)))
    P.append(cyl(C, 0.82, 0.08, (ex, cy - d / 2 + 0.04, 6.2), "black", segs=16, rot=(90, 0, 0)))
    P.append(box(C, (0.08, 0.06, 0.55), (ex, cy - d / 2 - 0.16, 6.2), "black"))
    P.append(box(C, (0.4, 0.06, 0.08), (ex + 0.18, cy - d / 2 - 0.16, 6.2), "black"))
    # Seitenfluegel links vorne (Satteldach quer)
    wx, wy, ww, wd = -11, -3.5, 8, 11
    parts, top2 = block(C, wx, wy, ww, wd, 2, fh=3.6, wall="plaster_warm", z0=Z0 + 0.35, spacing=2.6,
                        win_size=(1.8, 1.9), sides="frl", frame="white")
    P += parts
    P.append(box(C, (ww + 0.1, wd + 0.1, 0.3), (wx, wy, Z0 + 0.35 + 3.5), "terracotta"))
    P += gable(C, wd, ww, 2.6, (wx, wy, top2), "roof_red", overhang=0.5, rot=(0, 0, 90), gable_color="plaster_warm")
    # Basketballfeld
    fx, fy = 8.5, -5.5
    P.append(box(C, (12, 8, 0.05), (fx, fy, Z0 + 0.04), "court_orange"))
    for (sx, sy, px, py) in ((12, 0.1, 0, -3.9), (12, 0.1, 0, 3.9), (0.1, 8, -5.9, 0), (0.1, 8, 5.9, 0), (0.1, 8, 0, 0)):
        P.append(box(C, (sx, sy, 0.06), (fx + px, fy + py, Z0 + 0.04), "white"))
    P.append(cyl(C, 1.2, 0.06, (fx, fy, Z0 + 0.04), "white", segs=16))
    P.append(cyl(C, 1.05, 0.07, (fx, fy, Z0 + 0.04), "court_orange", segs=16))
    for s in (-1, 1):
        x = fx + s * 6.4
        P.append(box(C, (0.15, 0.15, 3.0), (x, fy, Z0), "metal_dark"))
        P.append(box(C, (0.1, 1.6, 1.1), (x - s * 0.2, fy, Z0 + 2.6), "white"))
        P.append(lathe(C, [(0.22, 0), (0.28, 0), (0.28, 0.04), (0.22, 0.04)], (x - s * 0.55, fy, Z0 + 3.0), "orange", segs=8))
    # Baeume, Fahrradstaender, Bank, Fahne
    P += tree(C, 12.5, 7.0, 7, 2.6, seed=1)
    P += tree(C, 9.0, 9.5, 6, 2.2, seed=2)
    P += tree(C, 14.0, 10.0, 7, 2.0, "pine")
    P += tree(C, -2.0, -9.5, 5.5, 2.0, seed=4)
    for k in range(5):
        P.append(lathe(C, [(0.35, 0), (0.4, 0), (0.4, 0.05), (0.35, 0.05)], (3.0 + k * 0.7, 0.4, Z0 + 0.4), "metal_dark",
                       segs=8, rot=(90, 0, 0)))
    P.append(box(C, (3.4, 0.08, 0.08), (4.4, 0.4, Z0 + 0.1), "metal_dark"))
    P += bench(C, 14.0, -1.0, 90)
    P += flagpole(C, 3, -10.5, 8, "blue", "yellow")
    P.append(hedge(C, (-15.5, -11.6), (-2.5, -11.6), 1.0, 0.7))
    P.append(hedge(C, (2.5, -11.6), (15.5, -11.6), 1.0, 0.7))
    return P


build("School", "Services", make)
