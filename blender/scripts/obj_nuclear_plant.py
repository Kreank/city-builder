"""Atomkraftwerk (Vorschlag 64 x 48 m): zwei Naturzug-Kuehltuerme, Reaktorkuppel, Maschinenhaus, Schaltanlage.
Objekte: "NuclearPlant" und "Steam" (Dampf ueber beiden Kuehltuermen, separat ein-/ausblendbar)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def cooling_tower(C, x, y, s=1.0):
    outer = [(13.0, 0), (12.4, 4), (10.4, 16), (9.0, 28), (8.8, 33), (9.1, 38), (9.6, 42)]
    inner = [(9.2, 42), (8.7, 38), (8.4, 33), (8.6, 28), (10.0, 16), (12.0, 4), (0, 4)]
    prof = [(r * s, z * s) for r, z in outer + inner]
    cols = ["concrete_dark", "offwhite", "offwhite", "offwhite", "offwhite", "concrete", "concrete",
            "concrete_dark", "concrete_dark", "concrete_dark", "concrete_dark", "concrete_dark", "water"]
    P = [lathe(C, prof, (x, y, Z0), segs=24, colors=cols)]
    for i in range(24):
        a = 2 * math.pi * (i + 0.5) / 24
        P.append(box(C, (2.6 * s, 0.25, 2.8 * s), (x + 12.8 * s * math.cos(a), y + 12.8 * s * math.sin(a), Z0 + 0.1),
                     "black", rot=(0, 0, math.degrees(a) + 90)))
    return P


def make(C):
    P = [box(C, (64, 48, Z0), (0, 0, 0), "concrete")]
    P.append(box(C, (22, 8, 0.04), (-18, -19.5, Z0), "asphalt"))
    # Kuehltuerme hinten
    P += cooling_tower(C, -17.0, 9.0, 0.95)
    P += cooling_tower(C, 13.0, 9.5, 0.95)
    # Reaktorgebaeude (Zylinder + Kuppel)
    rx, ry = -2.0, -6.0
    P.append(cyl(C, 9.0, 1.0, (rx, ry, Z0), "concrete_dark", segs=24))
    P.append(lathe(C, [(0, 0), (8.4, 0), (8.4, 16.0), (8.0, 19.0), (6.6, 21.8), (4.4, 23.6), (0, 24.6)],
                   (rx, ry, Z0 + 1.0), segs=24, colors=["concrete", "offwhite", "white", "white", "white", "white"]))
    P.append(lathe(C, [(8.42, 10.0), (8.45, 10.0), (8.45, 11.0), (8.42, 11.0)], (rx, ry, Z0 + 1.0), "red", segs=24))
    # Hilfsanlagengebaeude zwischen Reaktor und Maschinenhaus
    P.append(box(C, (10.0, 12.0, 12.0), (rx + 11.0, ry, Z0), "offwhite"))
    P += flat_roof(C, 10.0, 12.0, Z0 + 12.0, (rx + 11.0, ry), ph=0.5)
    # Maschinenhaus rechts
    mx, my, mw, md = 23.0, -7.0, 16.0, 16.0
    P.append(box(C, (mw, md, 16.0), (mx, my, Z0), "pastel_blue"))
    P.append(box(C, (mw + 0.1, md + 0.1, 1.2), (mx, my, Z0 + 14.0), "steel_blue"))
    P += ribbon(C, mx, my, mw, md, Z0 + 10.0, 2.0, "glass", "fr")
    P += gable(C, mw, md, 2.4, (mx, my, Z0 + 16.0), "roof_dark", overhang=0.3, rot=(0, 0, 90), gable_color="pastel_blue")
    # Abluftkamin (schlank, rot-weiss)
    P.append(lathe(C, [(0, 0), (1.2, 0), (1.0, 50), (1.1, 50.3), (1.1, 51), (0, 51)], (rx + 11.0, ry + 4.0, Z0 + 12.0),
                   segs=10, colors=["offwhite", "offwhite", "metal_dark", "black", "black"]))
    for z in (36, 40, 44, 48):
        P.append(lathe(C, [(1.03, z), (1.09, z), (1.09, z + 2), (1.03, z + 2)], (rx + 11.0, ry + 4.0, Z0 + 12.0), "red", segs=10))
    # Verwaltung + Pforte vorne links
    p, top = block(C, -22.0, -14.0, 14.0, 7.0, 3, 3.2, wall="white", z0=Z0 + 0.35, spacing=2.6, win_size=(1.4, 1.4),
                   sides="flr", skip=lambda s, r, c: s == "f" and r == 0 and c == 2)
    P += p
    P += flat_roof(C, 14.0, 7.0, top, (-22.0, -14.0), ph=0.5)
    P += door(C, -22.0 + (-2 * 2.6 + 2 * 2.6), -17.5, 1.8, 2.4, "glass_dark", z=Z0 + 0.35, canopy="steel_blue")
    P += sign(C, -22.0, -17.6, top - 1.2, 8.0, 0.8, "steel_blue", "white")
    # Schaltanlage vorne rechts
    for i in range(3):
        x = 14.0 + i * 5.0
        P.append(box(C, (3.0, 2.6, 3.0), (x, -20.0, Z0), "metal_dark", bevel=0.06))
        for dx in (-0.8, 0, 0.8):
            P.append(cyl(C, 0.16, 1.6, (x + dx, -20.0, Z0 + 3.0), "white", segs=6))
    for x in (11.5, 27.5):
        P.append(box(C, (0.4, 0.4, 9.0), (x, -17.5, Z0), "metal_dark"))
    P.append(beam(C, (11.5, -17.5, Z0 + 8.8), (27.5, -17.5, Z0 + 8.8), 0.4, "metal_dark"))
    # Zaun (Sicherheitsbereich) um den Kern
    P += fence(C, [(-10.0, -18.5), (-31.5, -18.5)], h=2.4, spacing=3.0, closed=False)

    # Dampf
    S = []
    for (tx, ty) in ((-17.0, 9.0), (13.0, 9.5)):
        for (dx, dy, dz, r) in [(0, 0, 41, 8.0), (-3.5, 2, 44, 6.5), (3.8, 1, 45, 6.8), (0.5, 4.5, 49, 6.0),
                                (-2.0, 7.0, 53, 5.0), (2.0, 9.0, 56, 4.0)]:
            S.append(sphere(C, r, (tx + dx, ty + dy, dz), "white", subdiv=2, scale=(1, 1, 0.72)))
    return {"NuclearPlant": (P, (0, 0, 0)), "Steam": (S, (-2.0, 9.25, 41.0))}


build("NuclearPlant", "Utilities", make, views={"": (1.0, -1.1, 0.7), "_back": (-1.0, 1.0, 0.7)})
