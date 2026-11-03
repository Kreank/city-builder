"""Feuerwache (G4-Dienst, Vorschlag 24 x 24 m). Drei Tore nach vorne, Schlauchturm, Loeschfahrzeug."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def fire_truck(C, x, y, rot=0):
    P = [box(C, (2.5, 8.4, 0.5), (0, 0.4, 0.55), "metal_dark"),
         box(C, (2.5, 2.2, 2.4), (0, -3.0, 0.9), "red", bevel=0.12),            # Kabine
         box(C, (2.3, 0.08, 1.0), (0, -4.12, 2.1), "glass_dark"),
         box(C, (2.5, 6.0, 2.5), (0, 1.5, 0.9), "red", bevel=0.06),             # Aufbau
         box(C, (2.52, 6.0, 0.18), (0, 1.5, 1.6), "white"),
         box(C, (1.2, 0.5, 0.18), (0, -3.0, 3.3), "blue_light")]
    for k in range(3):
        P.append(box(C, (0.08, 1.6, 1.6), (1.27, -0.6 + k * 2.0, 1.2), "metal"))   # Rollaeden
    for sx in (-0.45, 0.45):                                                   # Leiter
        P.append(box(C, (0.1, 7.4, 0.12), (sx, 1.0, 3.5), "metal"))
    for k in range(12):
        P.append(box(C, (0.9, 0.06, 0.06), (0, -2.4 + k * 0.62, 3.55), "metal"))
    for wy in (-2.9, 0.6, 2.4):
        for wx in (-1.25, 1.0):
            P.append(cyl(C, 0.55, 0.28, (wx, wy, 0.55), "black", segs=10, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def make(C):
    P = [box(C, (24, 24, Z0), (0, 0, 0), "concrete")]
    P.append(box(C, (17, 8, 0.04), (-3.5, -8, Z0), "asphalt"))
    # Fahrzeughalle (Ziegel) mit drei roten Toren
    cx, cy, w, d = -3.5, 3.0, 16, 14
    P.append(box(C, (w + 0.1, d + 0.1, 0.35), (cx, cy, Z0), "concrete_dark"))
    P.append(box(C, (w, d, 7.6), (cx, cy, Z0 + 0.35), "brick"))
    P += flat_roof(C, w, d, Z0 + 0.35 + 7.6, (cx, cy), ph=0.6)
    for i in range(3):
        x = cx - 5.0 + i * 5.0
        P.append(box(C, (4.2, 0.12, 4.6), (x, cy - d / 2 - 0.04, Z0 + 0.35), "red"))
        for k in range(5):
            P.append(box(C, (4.2, 0.16, 0.08), (x, cy - d / 2 - 0.06, Z0 + 0.35 + 0.8 + k * 0.85), "rust"))
        P.append(box(C, (4.6, 0.3, 0.3), (x, cy - d / 2 - 0.12, Z0 + 5.0), "concrete"))
    P += window_row(C, (cx - 6.0, cy - d / 2, Z0 + 6.6), (1, 0, 0), 5, 3.0, (1.6, 1.3), normal=(0, -1, 0))
    P += sign(C, cx, cy - d / 2 - 0.1, Z0 + 5.45, 6.0, 0.6, "red", "white")
    P += facade(C, cx, cy, w, d, [Z0 + 2.8, Z0 + 6.4], 3.2, (1.3, 1.4), "lb", frame="concrete")
    # Sirene
    P.append(box(C, (0.3, 0.3, 1.6), (cx + 5, cy + 3, Z0 + 8.0), "metal_dark"))
    P.append(box(C, (1.4, 0.6, 0.6), (cx + 5, cy + 3, Z0 + 9.6), "metal"))
    # Verwaltungsfluegel rechts (2 Geschosse)
    parts, top = block(C, 8.5, -0.5, 6, 7, 2, fh=3.3, wall="plaster", z0=Z0 + 0.35, spacing=2.6,
                       win_size=(1.2, 1.4), sides="fr", frame="concrete",
                       skip=lambda s, r, c: s == "f" and r == 0 and c == 0)
    P += parts
    P += flat_roof(C, 6, 7, top, (8.5, -0.5), ph=0.4)
    P += door(C, 7.2, -4.0, 1.1, 2.2, "red", z=Z0 + 0.35, canopy="concrete_dark")
    # Schlauchturm hinten rechts
    tx, ty = 8.5, 7.5
    P.append(box(C, (4.2, 4.2, 15.5), (tx, ty, Z0), "brick"))
    for z in (5, 9, 13):
        P += window_row(C, (tx, ty - 2.1, z), (1, 0, 0), 1, 1, (0.9, 1.3), normal=(0, -1, 0), frame="concrete")
    P.append(box(C, (4.4, 4.4, 0.3), (tx, ty, Z0 + 15.5), "concrete"))
    P.append(hip(C, 4.4, 4.4, 2.6, (tx, ty, Z0 + 15.8), "roof_red", overhang=0.25))
    P.append(box(C, (2.2, 0.08, 2.0), (tx, ty - 2.2, Z0 + 13.3), "black"))  # Luke (Schlauchtrocknung)
    # Loeschfahrzeug auf dem Hof
    P += fire_truck(C, -8.5, -7.0, 0)
    P += hydrant_simple(C, 10.5, -9.0)
    P += flagpole(C, 11, -11, 7, "red", "white")
    return P


def hydrant_simple(C, x, y):
    return [cyl(C, 0.18, 0.75, (x, y, Z0), "red", segs=8),
            sphere(C, 0.2, (x, y, Z0 + 0.78), "red", subdiv=1),
            cyl(C, 0.07, 0.42, (x - 0.21, y, Z0 + 0.5), "metal", segs=6, rot=(0, 90, 0))]


build("FireStation", "Services", make)
