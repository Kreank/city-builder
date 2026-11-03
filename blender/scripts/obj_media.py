"""Medien (G4): Zeitungsverlag, Radiosender, Fernsehturm. Je eine FBX unter Models/Services."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def newspaper(C):
    """Zeitungsverlag mit Druckerei (Vorschlag 24 x 16 m)."""
    P = [box(C, (24, 16, Z0), (0, 0, 0), "concrete")]
    S = []
    # Druckhalle hinten (Sheddach)
    S.append(box(C, (14, 9, 6.0), (-4.0, 3.0, 0), "beige"))
    S += sawtooth(C, -4.0, 3.0, 14, 9, 6.0, n=3, h=2.0)
    S += ribbon(C, -4.0, 3.0, 14, 9, 4.2, 1.2, "glass", "lb")
    S.append(box(C, (4.0, 0.15, 4.0), (-8.0, -1.5 - 0.07, 0), "metal_dark"))
    # Verlagshaus vorne rechts (4 Geschosse, Glas)
    p, top = block(C, 6.5, -1.0, 9.0, 9.0, 4, 3.3, wall="slate", z0=0.35, ribbon_windows=True, win_size=(0, 1.9),
                   win="glass_light", sides="flr")
    S += p
    S += flat_roof(C, 9.0, 9.0, top, (6.5, -1.0), ph=0.6)
    S += door(C, 6.5, -5.5, 2.4, 2.6, "glass_dark", z=0.35, canopy="red")
    S.append(box(C, (8.0, 0.3, 1.4), (6.5, -5.65, top + 0.4), "white"))       # Zeitungs-Logo auf dem Dach
    S.append(box(C, (6.4, 0.32, 0.5), (6.5, -5.7, top + 0.85), "black"))
    S.append(box(C, (0.3, 0.3, 0.8), (3.5, -5.3, top), "metal_dark"))
    S.append(box(C, (0.3, 0.3, 0.8), (9.5, -5.3, top), "metal_dark"))
    # Papierrollen im Hof
    for k in range(6):
        S.append(cyl(C, 0.6, 1.4, (-10.0 + (k % 3) * 1.5, -5.5 - (k // 3) * 1.6, 0.6), "white", segs=10, rot=(0, 90, 0)))
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    P += parked_car(C, -3.0, -5.6, 90, "white")
    P += parked_car(C, 1.4, -5.6, 90, "red")
    return P


def radio_station(C):
    """Radiosender mit abgespanntem Gittermast und Schuesseln (Vorschlag 16 x 16 m)."""
    P = [box(C, (16, 16, Z0), (0, 0, 0), "grass")]
    S = []
    p, top = block(C, -3.5, -3.8, 8.0, 6.0, 1, 3.6, wall="white", z0=0.3, spacing=2.4, win_size=(1.4, 1.4),
                   skip=lambda s, r, c: s == "f" and c == 1)
    S += p
    S.append(box(C, (8.1, 6.1, 0.6), (-3.5, -3.8, 3.3), "red"))
    S += flat_roof(C, 8.0, 6.0, top, (-3.5, -3.8), ph=0.3)
    S += door(C, -3.5, -6.8, 1.2, 2.3, "glass_dark", z=0.3, canopy="red")
    S += dish(C, -5.5, -3.0, top, 1.0, 30, 0)
    S += dish(C, -1.8, -2.6, top, 0.7, 40, 20)
    S += lattice_mast(C, 3.5, 3.5, 42.0, 1.3, 0.45, 0.0, guys=None)
    for frac in (0.45, 0.85):
        for (gx, gy) in ((-6.8, 6.8), (7.2, -6.2), (7.2, 7.2)):
            S.append(beam(C, (3.5, 3.5, 42 * frac), (gx, gy, 0.1), 0.04, "metal_dark"))
    for (gx, gy) in ((-6.8, 6.8), (7.2, -6.2), (7.2, 7.2)):
        S.append(box(C, (0.8, 0.8, 0.4), (gx, gy, 0), "concrete"))
    S.append(box(C, (2.2, 1.6, 2.0), (3.5, -1.0, 0), "metal_dark"))   # Sender-Huette am Mast
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    P += fence(C, [(-7.7, -7.7), (7.7, -7.7), (7.7, 7.7), (-7.7, 7.7)], h=1.8, spacing=2.6, gaps=(0,))
    return P


def tv_tower(C):
    """Fernsehturm mit Aussichtskorb und Antennenspitze (Vorschlag 16 x 16 m Fuss, ca. 150 m hoch)."""
    P = [box(C, (16, 16, Z0), (0, 0, 0), "concrete")]
    prof = [(0, 0), (6.0, 0), (5.6, 1.0), (4.0, 8.0), (3.0, 24.0), (2.6, 60.0), (2.4, 92.0),
            (3.4, 92.5), (8.5, 95.5), (8.8, 97.0), (8.8, 100.0), (8.0, 101.5), (8.6, 102.0), (8.6, 104.0),
            (6.0, 106.5), (2.6, 107.5), (2.4, 112.0), (1.3, 114.0), (1.3, 122.0), (0.7, 122.5), (0.7, 132.0),
            (0.35, 132.5), (0.35, 148.0), (0, 148.5)]
    cols = ["concrete", "concrete", "concrete", "offwhite", "offwhite", "offwhite",
            "offwhite", "white", "glass", "white", "metal", "glass_dark", "white", "white",
            "offwhite", "offwhite", "red", "white", "red", "white", "red", "white", "red"]
    P.append(lathe(C, prof, (0, 0, Z0), segs=20, colors=cols))
    # Eingangspavillon (Ring) mit Glas
    P.append(lathe(C, [(5.8, 0), (7.6, 0), (7.6, 0.2), (7.4, 0.2), (7.4, 3.4), (7.8, 3.4), (7.8, 3.8), (5.8, 3.8)],
                   (0, 0, Z0), segs=20, colors=["concrete", "concrete", "glass_light", "glass_light", "white", "white",
                                                "roof_light"]))
    P.append(box(C, (3.0, 2.0, 0.3), (0, -8.2, Z0 + 3.0), "white"))
    for x in (-1.3, 1.3):
        P.append(cyl(C, 0.12, 3.0, (x, -9.0, Z0), "metal", segs=6))
    # Rote Warnlichter
    for z in (60.0, 112.0):
        for i in range(4):
            a = math.radians(45 + 90 * i)
            r = 2.65 if z < 100 else 2.45
            P.append(sphere(C, 0.3, (r * math.cos(a), r * math.sin(a), Z0 + z), "red", subdiv=1))
    for (x, y) in ((-6.0, -6.0), (6.0, -6.0)):
        P += tree(C, x, y, 4.0, 1.2, seed=int(x))
    return P


run_family({"Newspaper": newspaper, "RadioStation": radio_station, "TVTower": tv_tower}, "Services",
           views={"": (1.0, -1.2, 0.65), "_back": (-1.0, 1.0, 0.6)})
