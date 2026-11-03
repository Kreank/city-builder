"""Strassen-Props (je eine FBX unter Models/Props). Ursprung = Mitte Fuss, Vorderseite (zur Strasse) = -Y."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.3, 0.7)}
ONLY = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def street_lamp(C):
    P = [cyl(C, 0.16, 0.6, (0, 0, 0), "metal_dark", segs=8),
         cyl(C, 0.09, 6.0, (0, 0, 0), "metal_dark", segs=8, r_top=0.07)]
    P.append(beam(C, (0, 0, 5.9), (0, -1.5, 6.2), 0.08, "metal_dark"))
    P.append(box(C, (0.4, 0.75, 0.18), (0, -1.75, 6.05), "metal_dark", bevel=0.04))
    P.append(box(C, (0.3, 0.6, 0.04), (0, -1.75, 6.03), "light_on"))
    return P


def hydrant(C):
    return [cyl(C, 0.22, 0.12, (0, 0, 0), "metal_dark", segs=10),
            cyl(C, 0.15, 0.75, (0, 0, 0.12), "red", segs=10),
            lathe(C, [(0, 0), (0.18, 0), (0.18, 0.06), (0.1, 0.18), (0, 0.22)], (0, 0, 0.87), "red", segs=10),
            cyl(C, 0.06, 0.08, (0, 0, 1.08), "metal", segs=6),
            cyl(C, 0.07, 0.2, (0.13, 0, 0.6), "metal", segs=8, rot=(0, 90, 0)),
            cyl(C, 0.07, 0.2, (-0.13, 0, 0.6), "metal", segs=8, rot=(0, -90, 0)),
            cyl(C, 0.09, 0.18, (0, -0.13, 0.42), "metal", segs=8, rot=(90, 0, 0)),
            cyl(C, 0.17, 0.05, (0, 0, 0.55), "white", segs=10)]


def mailbox(C):
    P = [box(C, (0.08, 0.08, 0.95), (0, 0.05, 0), "metal_dark"),
         box(C, (0.48, 0.32, 0.5), (0, 0, 0.9), "yellow", bevel=0.03),
         cyl(C, 0.16, 0.48, (-0.24, 0, 1.4), "yellow", segs=10, rot=(0, 90, 0)),
         box(C, (0.26, 0.04, 0.03), (0, -0.17, 1.28), "black"),
         box(C, (0.12, 0.03, 0.1), (0, -0.165, 1.05), "black")]
    return P


def bus_stop(C):
    P = [box(C, (4.2, 1.8, 0.12), (0, 0, 0), "concrete")]
    for x in (-1.95, 1.95):
        for y in (-0.7, 0.75):
            P.append(box(C, (0.1, 0.1, 2.5), (x, y, 0.12), "metal_dark"))
    P.append(box(C, (4.1, 0.05, 2.0), (0, 0.78, 0.4), "glass_light"))                     # Rueckwand Glas
    P.append(box(C, (0.05, 1.3, 2.0), (-1.95, 0.05, 0.4), "glass_light"))
    P.append(box(C, (0.05, 1.3, 2.0), (1.95, 0.05, 0.4), "advert" if False else "pastel_blue"))  # Werbetafel
    P.append(box(C, (4.5, 2.1, 0.14), (0, 0.0, 2.62), "metal", rot=(-3, 0, 0)))            # Dach
    P.append(box(C, (4.5, 0.06, 0.25), (0, -1.05, 2.5), "steel_blue"))
    for k in range(3):
        P.append(box(C, (2.6, 0.12, 0.04), (0, 0.38 + k * 0.13, 0.5), "wood"))             # Sitzbank
    P.append(box(C, (0.08, 0.3, 0.5), (-1.2, 0.5, 0.12), "metal_dark"))
    P.append(box(C, (0.08, 0.3, 0.5), (1.2, 0.5, 0.12), "metal_dark"))
    # Haltestellenschild (gruener Kreis, gelbes H)
    P.append(cyl(C, 0.05, 2.9, (2.5, -0.7, 0), "metal", segs=6))
    P.append(cyl(C, 0.32, 0.05, (2.5, -0.67, 2.65), "yellow", segs=16, rot=(90, 0, 0)))
    P.append(cyl(C, 0.27, 0.07, (2.5, -0.66, 2.65), "green_dark", segs=16, rot=(90, 0, 0)))
    P.append(box(C, (0.06, 0.03, 0.3), (2.43, -0.73, 2.5), "yellow"))
    P.append(box(C, (0.06, 0.03, 0.3), (2.57, -0.73, 2.5), "yellow"))
    P.append(box(C, (0.12, 0.03, 0.05), (2.5, -0.73, 2.63), "yellow"))
    P.append(box(C, (0.4, 0.04, 0.5), (2.5, -0.74, 1.3), "white"))                         # Fahrplan
    return P


def kiosk(C):
    P = [box(C, (3.4, 2.8, 0.15), (0, 0, 0), "concrete")]
    P.append(box(C, (3.0, 2.4, 2.6), (0, 0.1, 0.15), "pastel_green"))
    P.append(box(C, (3.4, 2.8, 0.2), (0, 0.1, 2.75), "green_dark"))
    P.append(box(C, (2.2, 0.08, 1.1), (0, -1.12, 1.2), "glass_light"))
    P.append(box(C, (2.4, 0.4, 0.08), (0, -1.25, 1.1), "wood"))                              # Tresen
    P += awning(C, 0, -1.12, 2.8, 0.9, 2.55, "green", "white", 0.35)
    P += sign(C, 0, -1.2, 2.95, 2.4, 0.5, "red", "white")
    # Zeitungsstaender
    P.append(box(C, (0.6, 0.35, 1.0), (1.35, -1.55, 0.15), "metal_dark"))
    for k, c in enumerate(("white", "red", "pastel_yellow")):
        P.append(box(C, (0.5, 0.05, 0.25), (1.35, -1.74, 0.45 + k * 0.28), c, rot=(-10, 0, 0)))
    P.append(box(C, (0.08, 0.08, 0.06), (0, 0, 0), "metal"))
    return P


def bike_rack(C):
    P = [box(C, (3.2, 0.5, 0.06), (0, 0, 0), "concrete")]
    for k in range(5):
        P.append(lathe(C, [(0.36, 0), (0.42, 0), (0.42, 0.05), (0.36, 0.05)], (-1.2 + k * 0.6, 0.025, 0.42),
                       "metal", segs=10, rot=(90, 0, 0)))
        P.append(box(C, (0.08, 0.06, 0.06), (-1.2 + k * 0.6 - 0.39, 0, 0.0), "metal_dark"))
        P.append(box(C, (0.08, 0.06, 0.06), (-1.2 + k * 0.6 + 0.39, 0, 0.0), "metal_dark"))
    return P


def bollard(C):
    return [cyl(C, 0.1, 0.9, (0, 0, 0), "metal_dark", segs=8),
            cyl(C, 0.105, 0.12, (0, 0, 0.62), "red", segs=8),
            cyl(C, 0.105, 0.08, (0, 0, 0.74), "white", segs=8),
            sphere(C, 0.1, (0, 0, 0.9), "metal_dark", subdiv=1, scale=(1, 1, 0.6))]


def planter(C):
    P = [box(C, (1.3, 1.3, 0.55), (0, 0, 0), "concrete", bevel=0.04),
         box(C, (1.1, 1.1, 0.04), (0, 0, 0.55), "wood_dark")]
    P += tree(C, 0, 0, 3.0, 0.9, seed=3)
    for o in P[-3:]:
        o.data.transform(Matrix.Translation((0, 0, 0.55)))
    return P


def ad_pillar(C):
    cols = ["metal_dark", "pastel_yellow", "flower_pink", "pastel_blue", "orange", "white", "green_dark", "green_dark"]
    prof = [(0, 0), (0.62, 0), (0.62, 0.2), (0.58, 0.25), (0.58, 0.9), (0.58, 1.5), (0.58, 2.1), (0.58, 2.7),
            (0.7, 2.75), (0.7, 2.9), (0.3, 3.2), (0, 3.35)]
    cols = ["metal_dark", "metal_dark", "metal_dark", "pastel_yellow", "flower_pink", "pastel_blue", "white",
            "green_dark", "green_dark", "green_dark", "green_dark"]
    P = [lathe(C, prof, (0, 0, 0), segs=14, colors=cols)]
    P.append(sphere(C, 0.1, (0, 0, 3.4), "gold", subdiv=1))
    return P


def picnic_table(C):
    P = [box(C, (1.9, 0.8, 0.06), (0, 0, 0.74), "wood")]
    for sy in (-0.7, 0.7):
        P.append(box(C, (1.9, 0.3, 0.05), (0, sy, 0.44), "wood"))
    for sx in (-0.75, 0.75):
        P.append(beam(C, (sx, -0.72, 0.04), (sx, 0.0, 0.76), 0.08, "wood_dark"))
        P.append(beam(C, (sx, 0.72, 0.04), (sx, 0.0, 0.76), 0.08, "wood_dark"))
        P.append(box(C, (0.08, 1.7, 0.08), (sx, 0, 0.36), "wood_dark"))
    return P


PROPS = {
    "Bench": lambda C: bench(C),
    "TrashBin": lambda C: trash_bin(C),
    "StreetLamp": street_lamp,
    "Hydrant": hydrant,
    "Mailbox": mailbox,
    "BusStop": bus_stop,
    "Kiosk": kiosk,
    "BikeRack": bike_rack,
    "Bollard": bollard,
    "Planter": planter,
    "AdPillar": ad_pillar,
    "PicnicTable": picnic_table,
}
for name, fn in PROPS.items():
    if ONLY and name not in ONLY:
        continue
    build(name, "Props", fn, views=V)
