"""Wohnen (gering): Einfamilienhaeuser. Masse = Grundstueck x 92 % (lot()), Strasse = -Y."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z = PLINTH


def chimney(C, x, y, z, h=1.6):
    return [box(C, (0.6, 0.6, h), (x, y, z), "brick_dark"), box(C, (0.75, 0.75, 0.15), (x, y, z + h), "concrete_dark")]


def house_l1(C):
    """1x1: kleines Haus mit Satteldach."""
    W = lot(1)
    cx, cy, w, d = 0, 0.6, 6.0, 5.4
    P, top = block(C, cx, cy, w, d, 1, fh=2.9, wall="pastel_yellow", z0=Z, spacing=2.2, win_size=(1.0, 1.2),
                   frame="white", skip=lambda s, r, c: s == "f" and c == 1)
    P += gable(C, w, d, 2.6, (cx, cy, top), "roof_red", overhang=0.4, gable_color="pastel_yellow")
    P += door(C, cx, cy - d / 2, 1.0, 2.1, "wood_dark", canopy="roof_red")
    P.append(box(C, (1.4, 0.8, Z), (cx, cy - d / 2 - 0.4, 0), "concrete"))
    P += window_row(C, (cx - w / 2, cy, top + 0.8), (0, 1, 0), 1, 1, (0.8, 0.8), normal=(-1, 0, 0))   # Giebelfenster
    P += window_row(C, (cx + w / 2, cy, top + 0.8), (0, 1, 0), 1, 1, (0.8, 0.8), normal=(1, 0, 0))
    P += chimney(C, 1.6, cy + 1.2, top + 1.0)
    P.append(hedge(C, (-W / 2 + 0.3, -W / 2 + 0.3), (-1.0, -W / 2 + 0.3), 0.8, 0.5))
    P.append(hedge(C, (1.0, -W / 2 + 0.3), (W / 2 - 0.3, -W / 2 + 0.3), 0.8, 0.5))
    return P


def house_l2(C):
    """1x2: schmales Giebelhaus (Giebel zur Strasse), Garten hinten."""
    W, D = lot(1), lot(2)
    cx, cy, w, d = 0, -2.0, 6.2, 8.4
    P, top = block(C, cx, cy, w, d, 2, fh=2.9, wall="white", z0=Z, spacing=2.0, win_size=(0.9, 1.3),
                   frame="slate", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    P += gable(C, d, w, 3.2, (cx, cy, top), "roof_dark", overhang=0.4, rot=(0, 0, 90), gable_color="white")
    P += window_row(C, (cx, cy - d / 2, top + 1.0), (1, 0, 0), 1, 1, (0.9, 1.0), normal=(0, -1, 0), frame="slate")
    P += door(C, cx, cy - d / 2, 1.0, 2.2, "slate", canopy="concrete_dark")
    P.append(box(C, (6.8, 0.25, 0.25), (cx, cy - d / 2 - 0.1, Z + 2.9), "slate"))
    # Garten
    P += tree(C, -1.6, 4.6, 5.5, 1.8, seed=11)
    P += tree(C, 2.0, 5.8, 4.5, 1.4, "pine")
    P += fence(C, [(-W / 2 + 0.1, 1.8), (-W / 2 + 0.1, D / 2 - 0.1), (W / 2 - 0.1, D / 2 - 0.1), (W / 2 - 0.1, 1.8)],
               h=1.0, spacing=1.8, post="wood_dark", rail="wood", closed=False)
    P.append(box(C, (1.6, 1.2, 0.5), (1.8, 2.6, 0), "wood", bevel=0.05))   # Gartenhuette (klein)
    P.append(gable(C, 1.6, 1.2, 0.5, (1.8, 2.6, 0.5), "roof_green", overhang=0.15)[0])
    return P


def house_l3(C):
    """2x2: Bungalow mit Walmdach, Garage, Einfahrt, Auto."""
    W = lot(2)
    P = [box(C, (3.2, 7.2, 0.05), (4.6, -3.7, 0), "asphalt")]
    cx, cy, w, d = -2.0, 1.0, 9.6, 8.0
    p, top = block(C, cx, cy, w, d, 1, fh=3.0, wall="cream", z0=Z, spacing=2.4, win_size=(1.5, 1.3),
                   frame="wood_dark", skip=lambda s, r, c: s == "f" and c == 2)
    P += p
    P.append(hip(C, w, d, 2.4, (cx, cy, top), "terracotta", overhang=0.6))
    P += door(C, cx + 0.3, cy - d / 2, 1.1, 2.2, "wood", canopy=None)
    P.append(box(C, (2.6, 1.6, Z), (cx + 0.3, cy - d / 2 - 0.8, 0), "concrete"))
    # Garage
    P.append(box(C, (3.6, 6.2, 2.8), (4.6, 1.6, Z), "cream"))
    P += flat_roof(C, 3.6, 6.2, Z + 2.8, (4.6, 1.6), "roof_dark", "wood_dark", ph=0.3, t=0.2)
    P.append(box(C, (2.8, 0.1, 2.3), (4.6, 1.6 - 3.12, Z), "white"))
    P += parked_car(C, 4.6, -4.4, 0, "steel_blue")
    P += tree(C, -5.4, -4.6, 5.0, 1.8, seed=5)
    P.append(hedge(C, (-W / 2 + 0.3, -W / 2 + 0.3), (2.2, -W / 2 + 0.3), 0.9, 0.6))
    P.append(hedge(C, (-W / 2 + 0.3, -W / 2 + 0.3), (-W / 2 + 0.3, W / 2 - 0.3), 0.9, 0.6))
    return P


def house_l4(C):
    """2x2: zweigeschossiges Familienhaus mit Gaube, Carport, Garten."""
    W = lot(2)
    cx, cy, w, d = -1.6, 0.8, 9.0, 7.2
    P, top = block(C, cx, cy, w, d, 2, fh=2.9, wall="pastel_blue", z0=Z, spacing=2.2, win_size=(1.1, 1.3),
                   frame="white", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    P += gable(C, w, d, 3.0, (cx, cy, top), "roof_dark", overhang=0.45, gable_color="pastel_blue")
    # Gaube
    P.append(box(C, (2.2, 1.6, 1.3), (cx + 1.5, cy - 1.8, top + 0.4), "pastel_blue"))
    P.append(gable(C, 1.6, 2.2, 0.6, (cx + 1.5, cy - 1.8, top + 1.7), "roof_dark", overhang=0.15, rot=(0, 0, 90))[0])
    P += window_row(C, (cx + 1.5, cy - 2.6, top + 1.05), (1, 0, 0), 1, 1, (1.0, 0.8), normal=(0, -1, 0))
    P += door(C, cx - 2.2 + 2.2 * 0, cy - d / 2, 1.0, 2.2, "red", canopy="white")
    P.append(box(C, (1.6, 1.0, Z), (cx - 2.2, cy - d / 2 - 0.5, 0), "concrete"))
    P += chimney(C, cx - 2.6, cy + 1.4, top + 1.2)
    # Carport
    for x in (3.9, 6.5):
        for y in (-3.4, 1.6):
            P.append(box(C, (0.15, 0.15, 2.5), (x, y, 0), "wood_dark"))
    P.append(box(C, (3.2, 5.8, 0.18), (5.2, -0.9, 2.5), "wood"))
    P.append(box(C, (2.8, 6.4, 0.05), (5.2, -2.8, 0), "gravel"))
    P += parked_car(C, 5.2, -1.4, 0, "red")
    P += tree(C, -5.2, 5.6, 5.5, 1.9, seed=8)
    P += tree(C, 0.8, 6.0, 4.5, 1.3, "pine")
    P += fence(C, [(-W / 2 + 0.1, -W / 2 + 0.1), (-W / 2 + 0.1, W / 2 - 0.1), (W / 2 - 0.1, W / 2 - 0.1),
                   (W / 2 - 0.1, 2.4)], h=1.0, spacing=2.0, post="white", rail="white", closed=False)
    return P


def house_l5(C):
    """1x1: moderner Kubus mit Flachdach, Holzverkleidung und grossen Fenstern."""
    W = lot(1)
    P = [box(C, (6.4, 5.6, Z), (0, 0.5, 0), "concrete_dark"),
         box(C, (6.2, 5.4, 2.9), (0, 0.5, Z), "white"),
         box(C, (4.2, 5.0, 2.8), (-0.8, 0.9, Z + 2.9), "wood")]
    P.append(box(C, (3.6, 0.1, 2.2), (-1.1, 0.5 - 2.7 - 0.05, Z + 0.2), "glass_dark"))
    P.append(box(C, (0.1, 3.0, 2.2), (3.15, 0.5, Z + 0.2), "glass_dark"))
    P.append(box(C, (3.0, 0.1, 1.4), (-0.8, 0.9 - 2.5 - 0.05, Z + 3.6), "glass_dark"))
    P += door(C, 2.1, 0.5 - 2.7, 1.0, 2.2, "slate")
    P += flat_roof(C, 6.2, 5.4, Z + 2.9, (0, 0.5), "roof_green", "white", ph=0.3, t=0.15)
    P += flat_roof(C, 4.2, 5.0, Z + 5.7, (-0.8, 0.9), "roof_light", "slate", ph=0.25, t=0.15)
    P.append(box(C, (1.6, 1.0, 0.05), (2.1, -2.8, 0), "concrete"))
    P += bush_small(C, -3.0, -3.0)
    return P


def bush_small(C, x, y, color="green_dark"):
    return [sphere(C, 0.55, (x, y, 0.45), color, subdiv=1, scale=(1, 1, 0.8))]


def house_l6(C):
    """1x2: Fachwerkhaus (Giebel zur Strasse) mit Bauerngarten."""
    D = lot(2)
    cx, cy, w, d = 0, -2.2, 6.4, 8.2
    fh = 2.8
    P = [box(C, (w + 0.1, d + 0.1, 0.6), (cx, cy, 0), "concrete_dark"),
         box(C, (w, d, 2 * fh), (cx, cy, 0.6), "cream")]
    P += half_timber(C, cx, cy, w, d, 0.6, fh, 2, "flr", bay=1.6)
    for z in (0.6 + 1.3, 0.6 + fh + 1.3):
        P += window_row(C, (cx - 1.6, cy - d / 2, z), (1, 0, 0), 2, 3.2, (0.8, 1.0), normal=(0, -1, 0))
        P += window_row(C, (cx - w / 2, cy - 2.4, z), (0, 1, 0), 3, 2.4, (0.8, 1.0), normal=(-1, 0, 0))
    P.append(box(C, (1.0, 0.15, 2.1), (cx, cy - d / 2 - 0.08, 0.6), "wood_dark"))
    P.append(box(C, (1.4, 0.9, 0.6), (cx, cy - d / 2 - 0.45, 0), "concrete"))
    P += gable(C, d, w, 3.6, (cx, cy, 0.6 + 2 * fh), "roof_red", overhang=0.35, rot=(0, 0, 90), gable_color="cream")
    P += window_row(C, (cx, cy - d / 2, 0.6 + 2 * fh + 1.2), (1, 0, 0), 1, 1, (0.7, 0.9), normal=(0, -1, 0))
    # Bauerngarten
    P.append(box(C, (6.8, 5.6, 0.06), (0, 4.6, 0), "grass"))
    for k in range(3):
        P.append(box(C, (1.2, 4.0, 0.25), (-2.2 + k * 2.2, 4.6, 0), "wood_dark"))
        for j in range(4):
            P.append(sphere(C, 0.3, (-2.2 + k * 2.2, 3.1 + j * 1.0, 0.4), ("green", "flower_pink", "yellow")[(j + k) % 3],
                            subdiv=0))
    P += fence(C, [(-3.6, 1.9), (-3.6, D / 2 - 0.1), (3.6, D / 2 - 0.1), (3.6, 1.9)], h=0.9, spacing=1.5,
               post="wood", rail="wood", closed=False)
    return P


def house_l7(C):
    """2x2: Villa mit Walmdach, Terrasse und Pool."""
    cx, cy, w, d = -1.0, 1.0, 10.0, 7.0
    P, top = block(C, cx, cy, w, d, 2, 3.1, wall="white", z0=Z, spacing=2.4, win_size=(1.2, 1.6), frame="slate",
                   skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    P.append(hip(C, w, d, 2.6, (cx, cy, top), "roof_dark", overhang=0.6))
    P += door(C, cx - 1.2, cy - d / 2, 1.4, 2.4, "wood_dark", canopy="slate")
    for x in (cx - 2.4, cx):
        P.append(cyl(C, 0.15, 2.6, (x, cy - d / 2 - 1.1, Z), "white", segs=8))
    P.append(box(C, (3.4, 1.6, Z), (cx - 1.2, cy - d / 2 - 0.8, 0), "cream"))
    P.append(box(C, (10.5, 2.2, 0.15), (cx, cy + d / 2 + 1.1, 0), "wood"))    # Terrasse hinten
    P += pool(C, 3.8, -3.6, 5.0, 3.0)
    P.append(box(C, (0.8, 1.8, 0.35), (0.4, -3.6, 0.3), "white"))            # Liege
    P += tree(C, -5.8, -5.2, 4.5, 1.4, "pine")
    P += tree(C, 5.8, 5.6, 5.5, 1.8, seed=17)
    P.append(hedge(C, (-7.0, -7.0), (7.0, -7.0), 1.2, 0.6))
    return P


def house_l8(C):
    """1x1: kleines Cottage mit hohem Dach, Schornstein und Rosenbusch."""
    cx, cy, w, d = 0.3, 0.8, 5.0, 4.6
    P, top = block(C, cx, cy, w, d, 1, 2.6, wall="beige", z0=Z, spacing=1.9, win_size=(0.8, 1.0), frame="white",
                   skip=lambda s, r, c: s == "f" and c == 1)
    P += gable(C, w, d, 3.2, (cx, cy, top), "roof_green", overhang=0.4, gable_color="beige")
    P += door(C, cx, cy - d / 2, 0.9, 2.0, "red")
    P += chimney(C, cx - 1.6, cy + 1.0, top + 1.2, 1.8)
    P.append(box(C, (0.9, 2.0, 0.05), (cx, cy - d / 2 - 1.0, 0), "gravel"))
    P += bush_small(C, -2.6, -2.6, "green")
    for k in range(4):
        P.append(sphere(C, 0.13, (-2.6 + 0.3 * math.cos(k * 1.6), -2.6 + 0.3 * math.sin(k * 1.6), 0.85), "flower_pink",
                        subdiv=0))
    P.append(box(C, (0.8, 0.8, 1.2), (2.7, 2.8, 0), "wood_dark"))           # Regentonne/Schuppen
    return P


import math
run_family({"House_L1": house_l1, "House_L2": house_l2, "House_L3": house_l3, "House_L4": house_l4,
            "House_L5": house_l5, "House_L6": house_l6, "House_L7": house_l7, "House_L8": house_l8},
           "Buildings/ResidentialLow")
