"""Polizeiwache (G4-Dienst, Vorschlag 24 x 16 m)."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def make(C):
    P = [box(C, (24, 16, Z0), (0, 0, 0), "concrete")]
    # Parkplatz links vorne mit Markierung + zwei Streifenwagen
    P.append(box(C, (9, 7.4, 0.04), (-7.5, -4.3, Z0), "asphalt"))
    for x in (-10.5, -7.5, -4.5):
        P.append(box(C, (0.12, 4.8, 0.05), (x, -4.3, Z0), "white"))
    for x, c in ((-9.0, "white"), (-6.0, "white")):
        car = parked_car(C, x, -4.6, 0, c)
        P += car
        P.append(box(C, (1.82, 1.0, 0.3), (x, -4.6 - 0.2, 0.62 + Z0), "blue", center=True))   # Streifen
        P.append(box(C, (0.9, 0.25, 0.15), (x, -4.4, 1.65 + Z0), "blue_light", center=True))  # Blaulicht
    for o in P[-1:]:
        pass
    # Garage hinten links
    P.append(box(C, (7, 6, 3.8), (-7.5, 4.6, Z0), "offwhite"))
    P += flat_roof(C, 7, 6, Z0 + 3.8, (-7.5, 4.6), ph=0.4)
    for x in (-9.2, -5.8):
        P.append(box(C, (2.8, 0.1, 2.8), (x, 1.56, Z0), "steel_blue"))
    P.append(box(C, (7.05, 6.05, 0.5), (-7.5, 4.6, Z0 + 3.0), "blue"))
    # Hauptgebaeude
    cx, cy, w, d = 4.5, 1.5, 14, 11
    parts, top = block(C, cx, cy, w, d, 2, fh=3.6, wall="offwhite", z0=Z0 + 0.35, spacing=2.8,
                       win_size=(1.5, 1.6), frame="concrete",
                       skip=lambda s, r, c: s == "f" and r == 0 and c in (2,))
    P += parts
    P.append(box(C, (w + 0.1, d + 0.1, 0.6), (cx, cy, Z0 + 0.35 + 3.3), "blue"))
    P += flat_roof(C, w, d, top, (cx, cy), ph=0.5)
    # Eingang mit Vordach, Schild, Blaulicht-Lampe
    P += door(C, cx, cy - d / 2, 1.8, 2.4, "glass_dark", z=Z0 + 0.35, canopy="steel_blue")
    P += sign(C, cx, cy - d / 2 - 0.6, Z0 + 3.3, 4.2, 0.7, "blue", "white")
    P.append(box(C, (0.5, 0.3, 0.4), (cx + 1.6, cy - d / 2 - 0.15, Z0 + 2.8), "blue_light"))
    P.append(box(C, (3.2, 1.8, 0.35), (cx, cy - d / 2 - 1.0, Z0), "concrete"))   # Stufe
    # Funkmast auf dem Dach
    mx, my = cx + 4.5, cy + 3
    for k in range(3):
        P.append(cyl(C, 0.18 - k * 0.04, 3.0, (mx, my, top + k * 3.0), "metal", segs=6))
    P.append(box(C, (0.6, 0.6, 0.6), (mx, my, top), "metal_dark"))
    P.append(box(C, (0.08, 1.2, 0.6), (mx + 0.2, my, top + 6.5), "white"))
    P += ac_units(C, cx - 3, cy + 2, top, 2, seed=2)
    # Fahnenmast + Hecke
    P += flagpole(C, -1.2, -6.8, 8, "green_dark", "white")
    P.append(hedge(C, (-1.2, -7.6), (11.5, -7.6), 0.9, 0.7))
    return P


build("PoliceStation", "Services", make)
