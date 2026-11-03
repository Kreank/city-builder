"""Wohnen (mittel): Mehrfamilienhaeuser. Masse = Grundstueck x 92 % (lot()), Strasse = -Y."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z = PLINTH


def entrance(C, x, y, color="glass_dark", canopy="concrete_dark"):
    P = door(C, x, y, 1.6, 2.4, color, canopy=canopy)
    P.append(box(C, (2.2, 1.2, Z), (x, y - 0.6, 0), "concrete"))
    P.append(box(C, (0.4, 0.06, 0.25), (x + 1.2, y - 0.05, Z + 1.4), "metal"))   # Klingel/Briefkasten
    return P


def apartment_m1(C):
    """2x2: viergeschossiger Wohnblock mit Balkonen und Treppenhaus-Glasband."""
    cx, cy, w, d = 0, 0.6, 13.0, 11.0
    fh = 3.0
    P, top = block(C, cx, cy, w, d, 4, fh, wall="terracotta", z0=Z, spacing=2.6, win_size=(1.2, 1.4),
                   frame="cream", bands="cream", skip=lambda s, r, c: s == "f" and c == 2)
    P.append(box(C, (1.8, 0.1, 4 * fh - 1.2), (cx, cy - d / 2 - 0.04, Z + 2.8), "glass_light"))   # Treppenhaus
    for i in range(1, 4):
        for x in (-4.4, 4.4):
            P += balcony(C, x, cy - d / 2, Z + i * fh, 3.0, 1.2, "cream", "metal")
    P += flat_roof(C, w, d, top, (cx, cy), ph=0.5, parapet="cream")
    P.append(box(C, (2.6, 2.6, 2.2), (cx, cy + 2, top), "cream"))
    P += entrance(C, cx, cy - d / 2)
    P += tree(C, -5.6, -5.8, 4.5, 1.4, seed=21)
    P += bench(C, 4.4, -6.2, 0)
    return P


def apartment_m2(C):
    """3x3: L-foermige Wohnanlage, 5 Geschosse, Innenhof mit Baum."""
    W = lot(3)
    fh = 3.0
    FY = -5.5   # Vorderhaus-Mitte (Balkone duerfen nicht ueber das Grundstueck ragen)
    P, top = block(C, -0.5, FY, 19.0, 8.6, 5, fh, wall="beige", z0=Z, spacing=2.7, win_size=(1.3, 1.5),
                   frame="white", skip=lambda s, r, c: s == "f" and c in (3,))
    BY = 4.2    # Hinterhaus-Mitte
    p2, _ = block(C, -5.8, BY, 8.4, 13.6, 5, fh, wall="beige", z0=Z, spacing=2.7, win_size=(1.3, 1.5),
                  frame="white", sides="lbr")
    P += p2
    for i in range(5 - 1):
        P.append(box(C, (19.1, 8.7, 0.25), (-0.5, FY, Z + (i + 1) * fh - 0.12), "white"))
        P.append(box(C, (8.5, 13.7, 0.25), (-5.8, BY, Z + (i + 1) * fh - 0.12), "white"))
    for i in range(1, 5):
        for x in (-7.0, 6.0):
            P += balcony(C, x, FY - 4.3, Z + i * fh, 3.2, 1.2, "white", "steel_blue")
    P += flat_roof(C, 19.0, 8.6, top, (-0.5, FY), ph=0.5)
    P += flat_roof(C, 8.4, 13.6, top, (-5.8, BY), ph=0.5)
    P.append(box(C, (2.6, 2.6, 2.0), (3.0, FY + 0.2, top), "beige"))
    # Eingang in der Fensterluecke (Spalte 3 von 6, Raster 2,7 m)
    P += entrance(C, -0.5 + (-2.5 * 2.7 + 3 * 2.7), FY - 4.3)
    # Innenhof
    P.append(box(C, (9.6, 12.6, 0.06), (4.4, 3.8, 0), "grass"))
    P += tree(C, 5.0, 4.0, 7.0, 2.6, seed=3)
    P += tree(C, 8.0, 8.4, 5.0, 1.6, "pine")
    P += bench(C, 2.6, 7.8, 180)
    P += flower_bed(C, 7.8, 0.6, 2.2, 1.4, seed=2)
    return P


def townhouses_m3(C):
    """2x3: Reihe aus drei Stadthaeusern (Giebel quer), Gaerten hinten."""
    D = lot(3)
    P = []
    cols = [("pastel_pink", "roof_dark"), ("pastel_green", "roof_red"), ("pastel_yellow", "roof_dark")]
    for i, (wall, roof) in enumerate(cols):
        x = -4.6 + i * 4.6
        p, top = block(C, x, -4.6, 4.6, 10.0, 3, 2.9, wall=wall, z0=Z, spacing=1.7, win_size=(1.0, 1.4),
                       frame="white", sides="fb", margin=0.6, skip=lambda s, r, c: s == "f" and r == 0 and c == 0)
        P += p
        P += gable(C, 4.6, 10.0, 2.8, (x, -4.6, top), roof, overhang=0.25)
        P += door(C, x - 0.85, -9.6, 1.0, 2.2, "wood_dark", canopy="white")
        P.append(box(C, (1.4, 1.0, Z), (x - 0.85, -10.1, 0), "concrete"))
        P.append(box(C, (1.6, 1.2, 1.2), (x + 0.4, -4.6 - 2.2, top + 0.6), wall))   # Gaube
        P.append(gable(C, 1.2, 1.6, 0.5, (x + 0.4, -4.6 - 2.2, top + 1.8), roof, overhang=0.12, rot=(0, 0, 90))[0])
        P += window_row(C, (x + 0.4, -7.6, top + 1.2), (1, 0, 0), 1, 1, (0.8, 0.7), normal=(0, -1, 0))
        P += chimney_small(C, x + 1.4, -2.6, top + 1.3)
        # Gaerten hinten
        P.append(box(C, (4.4, 9.6, 0.06), (x, 5.6, 0), "grass"))
        P += tree(C, x + 0.6, 7.0 + (i % 2) * 1.6, 4.2 + i * 0.4, 1.3, seed=30 + i)
    P += fence(C, [(-6.9, 0.6), (-6.9, D / 2 - 0.1), (6.9, D / 2 - 0.1), (6.9, 0.6)], h=1.1, spacing=1.9,
               post="wood_dark", rail="wood", closed=False)
    for x in (-2.3, 2.3):
        P.append(hedge(C, (x, 0.8), (x, D / 2 - 0.4), 1.0, 0.4))
    # Gehwegtrenner vorne
    P.append(hedge(C, (-6.9, -10.9), (-6.0, -10.9), 0.7, 0.4))
    return P


def chimney_small(C, x, y, z):
    return [box(C, (0.5, 0.5, 1.2), (x, y, z), "brick_dark"), box(C, (0.62, 0.62, 0.12), (x, y, z + 1.2), "concrete_dark")]


def altbau_m4(C):
    """2x2: Gruenderzeit-Altbau, 4 Geschosse, Stuckbaender, Mansarddach mit Gauben."""
    cx, cy, w, d = 0, -1.0, 14.0, 11.0
    fh = 3.4
    P, top = block(C, cx, cy, w, d, 4, fh, wall="pastel_yellow", z0=Z, spacing=2.4, win_size=(1.1, 2.0),
                   frame="cream", bands="cream", skip=lambda s, r, c: s == "f" and r == 0 and c == 2)
    P.append(box(C, (w + 0.1, d + 0.1, 1.4), (cx, cy, Z), "cream"))           # Sockelgeschoss hell
    P.append(box(C, (w + 0.5, d + 0.5, 0.35), (cx, cy, top - 0.35), "cream"))  # Kranzgesims
    P.append(hip(C, w + 0.4, d + 0.4, 3.0, (cx, cy, top), "slate", overhang=-0.2))
    for x in (-4.8, -2.4, 2.4, 4.8):
        P.append(box(C, (1.4, 1.4, 1.4), (x, cy - d / 2 + 0.9, top + 0.1), "pastel_yellow"))
        P.append(gable(C, 1.4, 1.4, 0.6, (x, cy - d / 2 + 0.9, top + 1.5), "slate", overhang=0.1, rot=(0, 0, 90))[0])
        P += window_row(C, (x, cy - d / 2 + 0.2, top + 0.85), (1, 0, 0), 1, 1, (0.8, 0.9), normal=(0, -1, 0))
    for i in (1, 2, 3):
        P += balcony(C, cx, cy - d / 2, Z + i * fh, 2.4, 0.9, "cream", "black")
    P += door(C, cx, cy - d / 2, 1.6, 2.8, "wood_dark")
    P.append(box(C, (2.6, 0.4, 0.4), (cx, cy - d / 2 - 0.2, Z + 2.9), "cream"))
    P += tree(C, -5.5, -6.6, 5.0, 1.6, seed=41)
    return P


def courtyard_m5(C):
    """3x3: moderner U-Block (offen nach hinten) mit gruenem Innenhof, 4 Geschosse."""
    fh = 3.0
    P = []
    for (cx, cy, w, d, sides) in ((0, -7.5, 20.0, 6.5, "flr"), (-7.0, 3.0, 6.0, 15.0, "lb"), (7.0, 3.0, 6.0, 15.0, "rb")):
        p, top = block(C, cx, cy, w, d, 4, fh, wall="offwhite", z0=Z, spacing=2.5, win_size=(1.6, 1.7),
                       sides=sides, frame="slate", skip=(lambda s, r, c: s == "f" and r == 0 and c == 3))
        P += p
        P += flat_roof(C, w, d, top, (cx, cy), "roof_green", "slate", ph=0.4)
    for i in range(1, 4):   # Laubengang-Balkone zum Hof
        P.append(box(C, (8.0, 1.4, 0.2), (0, -4.25 + 0.7, Z + i * fh), "slate"))
        P.append(box(C, (8.0, 0.06, 1.0), (0, -4.25 + 1.37, Z + i * fh + 0.2), "glass_light"))
    for x in (-3.4, 3.4):
        P.append(box(C, (0.5, 0.5, 4 * fh), (x, -7.5 - 3.3, Z), "terracotta"))   # Akzent-Lisenen vorne
    P.append(box(C, (8.0, 11.0, 0.06), (0, 4.0, 0), "grass"))
    P += tree(C, -1.5, 3.0, 6.0, 2.0, seed=51)
    P += tree(C, 2.0, 7.5, 5.0, 1.6, "pine")
    P += bench(C, 1.8, 1.0, 180)
    P += flower_bed(C, -2.2, 8.4, 2.4, 1.4, seed=5)
    P += door(C, 0.0, -10.75, 1.8, 2.4, "glass_dark")
    return P


def terraced_m6(C):
    """2x3: Terrassenhaus, nach hinten ansteigend (3/4/5 Geschosse), Dachgaerten zur Strasse."""
    fh = 3.0
    P = []
    steps = [(-7.0, 3, 6.6), (0.0, 4, 7.2), (7.2, 5, 7.0)]   # (cy, Geschosse, Tiefe)
    for i, (cy, n, d) in enumerate(steps):
        p, top = block(C, 0, cy, 13.0, d, n, fh, wall=("pastel_green", "pastel_green", "cream")[i], z0=Z,
                       spacing=2.6, win_size=(1.5, 1.6), frame="white", sides="flrb" if i == 0 else "lr",
                       skip=lambda s, r, c: s == "f" and r == 0 and c == 2)
        P += p
        P += flat_roof(C, 13.0, d, top, (0, cy), "grass", "white", ph=1.0)
        for x in (-4.0, 0.0, 4.0):
            P.append(sphere(C, 0.6, (x, cy - d / 2 + 0.9, top + 0.5), "green_dark", subdiv=1))
    # Glasfronten der hoeheren Stufe ueber dem Dachgarten der niedrigeren (je ein Geschoss)
    P.append(box(C, (12.0, 0.1, 2.0), (0, 0.0 - 3.6 - 0.05, Z + 3 * fh + 0.5), "glass"))
    P.append(box(C, (12.0, 0.1, 2.0), (0, 7.2 - 3.5 - 0.05, Z + 4 * fh + 0.5), "glass"))
    P += door(C, 0, -10.3, 1.8, 2.4, "glass_dark", canopy="white")
    return P


run_family({"Apartment_M1": apartment_m1, "Apartment_M2": apartment_m2, "Townhouses_M3": townhouses_m3,
            "Altbau_M4": altbau_m4, "Courtyard_M5": courtyard_m5, "Terraced_M6": terraced_m6},
           "Buildings/ResidentialMedium")
