"""Kueste & Hafen (Models/Coast). Wasserspiegel = z 0 (wie im Spiel). Teile, die ins Wasser reichen,
gehen bis -3 ... -5 m hinunter. Uferseite = +Y, Wasserseite = -Y (Vorderseite schaut aufs Wasser).
Schiffe: Bug nach -Y, Ursprung = Wasserlinie mittschiffs."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.2, 0.7)}


# ------------------------------------------------------------------ Strand
def beach_chair(C, x=0, y=0, rot=0, stripe="blue"):
    """Strandkorb."""
    P = [box(C, (1.4, 0.9, 0.5), (0, 0, 0), "wood"),
         box(C, (1.4, 0.15, 1.5), (0, 0.42, 0.5), "wood"),
         box(C, (0.12, 0.9, 1.2), (-0.64, 0.0, 0.5), "wood"),
         box(C, (0.12, 0.9, 1.2), (0.64, 0.0, 0.5), "wood"),
         box(C, (1.5, 1.05, 0.45), (0, 0.0, 1.7), stripe, bevel=0.12),       # Dach (Haube)
         box(C, (1.3, 0.6, 0.12), (0, -0.1, 0.42), "white")]
    for k in range(3):
        P.append(box(C, (0.2, 0.17, 1.4), (-0.45 + k * 0.45, 0.43, 0.55), "white"))
    return place_at(P, x, y, rot)


def umbrella(C, x=0, y=0, color="red"):
    return [cyl(C, 0.04, 2.3, (x, y, 0), "white", segs=6),
            cyl(C, 1.3, 0.45, (x, y, 1.9), color, segs=8, r_top=0.05)]


def towel(C, x, y, rot, color):
    return place_at([box(C, (0.9, 1.8, 0.03), (0, 0, 0), color)], x, y, rot)


def beach(C):
    """Strandkachel 32 x 24 m: Sand faellt von +0,4 m (Land, +Y) auf -2 m (Wasser, -Y), Schaumkante bei z 0."""
    P = []
    # Querschnitt in YZ, entlang X extrudiert
    sec = [(-12.0, -2.0), (12.0, -2.0), (12.0, 0.4), (5.0, 0.35), (-3.0, 0.08), (-12.0, -2.0)]
    pr = prism(C, [(y, z) for y, z in sec[:-1]], 32.0, (0, 0, 0), "sand")
    pr.data.transform(Matrix.Translation((-16.0, 0, 0)) @ Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(pr)
    # nasser Sand / Schaumkante an der Wasserlinie (Sand schneidet z 0 bei y ~ -4)
    P.append(box(C, (32.0, 1.0, 0.04), (0, -4.2, -0.12), "foam", rot=(-12, 0, 0)))
    rnd = random.Random(4)
    cols = ("red", "blue", "yellow", "teal", "orange")
    for k in range(5):
        x = -12.0 + k * 6.0
        P += umbrella(C, x, 2.2 + rnd.uniform(-1, 1), cols[k])
        for o in P[-2:]:
            o.data.transform(Matrix.Translation((0, 0, 0.12 + 0.03 * k)))
        P += towel(C, x + 1.2, 1.2 + rnd.uniform(-0.5, 0.5), rnd.uniform(-20, 20), cols[(k + 2) % 5])
        P[-1].data.transform(Matrix.Translation((0, 0, 0.14)))
    for k in range(4):
        P += beach_chair(C, -13.0 + k * 8.5, 7.5, 180 + rnd.uniform(-15, 15), ("blue", "red", "teal", "green")[k])
        for o in P[-9:]:
            o.data.transform(Matrix.Translation((0, 0, 0.3)))
    # Duenengras am Landrand
    for i in range(30):
        P.append(cyl(C, 0.25, 0.7, (rnd.uniform(-15.5, 15.5), rnd.uniform(9.5, 11.6), 0.38), "grass_dry", segs=5, r_top=0.02))
    return P


def lifeguard_tower(C):
    P = []
    for (x, y) in ((-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9)):
        P.append(beam(C, (x * 1.3, y * 1.3, 0), (x, y, 2.6), 0.15, "white"))
    P.append(box(C, (2.4, 2.4, 0.15), (0, 0, 2.6), "wood"))
    P.append(box(C, (2.0, 2.0, 1.6), (0, 0.2, 2.75), "red"))
    P.append(box(C, (1.6, 0.06, 0.8), (0, -0.82, 3.4), "glass"))
    P.append(hip(C, 2.2, 2.2, 0.9, (0, 0.2, 4.35), "white", overhang=0.2))
    P.append(beam(C, (0.3, -2.4, 0), (0.3, -1.2, 2.6), 0.5, "wood", t2=0.06))
    P.append(box(C, (0.05, 0.05, 1.6), (-1.1, -1.1, 2.75), "white"))
    P.append(box(C, (0.8, 0.03, 0.5), (-0.7, -1.1, 3.9), "yellow"))
    return P


# ------------------------------------------------------------------ Hafenbauten
def pier(C):
    """Holzsteg 4 x 24 m auf Pfaehlen, Land bei +Y. Pfaehle reichen bis -3 m."""
    P = [box(C, (4.0, 24.0, 0.25), (0, 0, 1.0), "wood")]
    for k in range(24):
        P.append(box(C, (4.0, 0.04, 0.02), (0, -11.75 + k * 1.0, 1.25), "wood_dark"))
    for k in range(7):
        y = -11.5 + k * 3.8
        for s in (-1, 1):
            P.append(cyl(C, 0.18, 4.3, (s * 1.8, y, -3.0), "wood_dark", segs=6))
            P.append(box(C, (0.1, 0.1, 1.0), (s * 1.9, y, 1.25), "wood_dark"))
    for s in (-1, 1):
        P.append(box(C, (0.12, 24.0, 0.1), (s * 1.9, 0, 2.15), "wood"))
    P.append(box(C, (4.4, 0.6, 0.3), (0, -12.0, 0.95), "wood_dark"))
    P.append(cyl(C, 0.4, 1.0, (1.5, -11.0, 1.25), "white", segs=10, rot=(90, 0, 0)))   # Rettungsring
    P.append(cyl(C, 0.25, 1.02, (1.5, -11.01, 1.25), "red", segs=10, rot=(90, 0, 0)))
    P += lamp(C, -1.7, -11.6, 0, 3.0)
    for o in P[-4:]:
        o.data.transform(Matrix.Translation((0, 0, 1.25)))
    return P


def lighthouse(C):
    """Leuchtturm auf Felsensockel mit Waerterhaus (12 x 12 m)."""
    P = [mound(C, 6.0, 6.0, 1.6, (0, 0, -2.0), "concrete_dark", seed=2, subdiv=2)]
    P.append(cyl(C, 3.0, 1.6, (0, 0, -0.4), "concrete", segs=12))
    prof = [(0, 0), (2.6, 0), (2.3, 4.5), (2.15, 9.0), (2.0, 13.5), (1.85, 18.0), (1.75, 21.0), (0, 21.0)]
    P.append(lathe(C, prof, (0, 0, 1.2), segs=12, colors=["red", "white", "red", "white", "red", "white", "white"]))
    P.append(cyl(C, 2.5, 0.3, (0, 0, 22.2), "metal_dark", segs=12))
    for i in range(12):
        a = 2 * math.pi * (i + 0.5) / 12
        P.append(box(C, (0.06, 0.06, 0.9), (2.4 * math.cos(a), 2.4 * math.sin(a), 22.5), "metal_dark"))
    P.append(cyl(C, 2.45, 0.06, (0, 0, 23.35), "metal_dark", segs=12))
    P.append(cyl(C, 1.3, 2.2, (0, 0, 22.5), "light_on", segs=8))
    P.append(lathe(C, [(0, 0), (1.6, 0), (0.2, 1.6), (0, 1.8)], (0, 0, 24.7), "red", segs=8))
    P.append(sphere(C, 0.25, (0, 0, 26.6), "metal_dark", subdiv=1))
    P.append(box(C, (0.9, 0.15, 1.9), (0, -2.6, 1.2), "wood_dark"))
    for z in (6.0, 11.0, 16.0):
        P.append(box(C, (0.6, 0.15, 0.9), (0, -2.25 + (z - 6) * 0.025, z), "glass"))
    # Waerterhaus
    P.append(box(C, (4.5, 3.6, 2.8), (3.5, 2.6, 1.2), "white"))
    P += gable(C, 4.5, 3.6, 1.6, (3.5, 2.6, 4.0), "roof_red", overhang=0.3, gable_color="white")
    P += window_row(C, (3.5, 0.8, 2.6), (1, 0, 0), 2, 2.0, (0.8, 0.9), normal=(0, -1, 0))
    return P


def breakwater(C):
    """Wellenbrecher 24 x 6 m aus Steinbloecken (Tetrapoden-Andeutung), Oberkante 1,5 m ueber Wasser."""
    rnd = random.Random(7)
    P = [box(C, (24.0, 4.0, 3.7), (0, 0, -3.5), "concrete_dark")]      # Kern bis +0,2 m
    for i in range(55):
        x, y = rnd.uniform(-11.5, 11.5), rnd.uniform(-2.8, 2.8)
        s = rnd.uniform(0.9, 1.6)
        z = 0.9 - abs(y) * 0.3 + rnd.uniform(-0.2, 0.3)                    # Mitte hoeher, Flanken fallen ab
        P.append(box(C, (s, s, s), (x, y, z), rnd.choice(("concrete", "concrete_dark", "gravel")),
                     rot=(rnd.uniform(0, 45), rnd.uniform(0, 45), rnd.uniform(0, 90)), center=True, bevel=0.15))
    return P


def quay(C):
    """Kaimauer-Segment 16 x 8 m (kachelbar entlang X): Wand bis -5 m, Poller, Fender, Leiter, Kante +1,5 m."""
    P = [box(C, (16.0, 8.0, 6.5), (0, 0.0, -5.0), "concrete")]
    P.append(box(C, (16.0, 0.4, 0.15), (0, -3.8, 1.5), "yellow"))
    for x in (-5.0, 3.0):
        P.append(cyl(C, 0.3, 0.7, (x, -3.2, 1.5), "black", segs=8))
        P.append(cyl(C, 0.42, 0.12, (x, -3.2, 2.2), "black", segs=8))
    for x in (-7.0, -1.0, 5.0):
        P.append(box(C, (1.4, 0.5, 2.6), (x, -4.25, -1.2), "black"))       # Fender
    for s in (-0.3, 0.3):
        P.append(box(C, (0.06, 0.1, 6.0), (1.0 + s, -4.05, -4.5), "metal"))
    for k in range(12):
        P.append(box(C, (0.6, 0.08, 0.05), (1.0, -4.05, -4.2 + k * 0.5), "metal"))
    for k in range(3):
        P.append(box(C, (0.25, 0.25, 0.05), (-7.0 + k * 7.0, 2.0, 1.5), "metal_dark"))  # Gleisschienen-Andeutung
    return P


def harbor_warehouse(C):
    """Speicher (Backstein, 24 x 12 m, 5 Geschosse) mit Ladeluken und Windenerkern zur Wasserseite."""
    cx, cy, w, d = 0, 0, 24.0, 12.0
    P, top = block(C, cx, cy, w, d, 5, 3.4, wall="brick", z0=1.5, plinth="concrete", spacing=2.6, win_size=(1.0, 1.4),
                   frame="cream", sides="lrb")
    P.append(box(C, (w + 0.2, d + 0.2, 1.5), (cx, cy, 0), "concrete"))
    for k in range(3):
        x = -8.0 + k * 8.0
        for f in range(5):
            P.append(box(C, (1.8, 0.15, 2.4), (x, -d / 2 - 0.07, 1.8 + f * 3.4), "wood_dark"))
        P.append(box(C, (2.2, 1.6, 2.4), (x, -d / 2 - 0.6, top), "brick"))       # Windenerker
        P.append(gable(C, 1.6, 2.2, 1.0, (x, -d / 2 - 0.6, top + 2.4), "roof_dark", overhang=0.1, rot=(0, 0, 90))[0])
        P.append(box(C, (0.2, 1.8, 0.2), (x, -d / 2 - 1.3, top + 1.8), "metal_dark"))
        P.append(beam(C, (x, -d / 2 - 2.1, top + 1.8), (x, -d / 2 - 2.1, top - 1.0), 0.05, "black"))
    for x in (-4.0, 4.0):
        P += facade(C, x, cy, 4.0, d, [1.5 + 3.4 * (f + 0.55) for f in range(5)], 2.0, (1.0, 1.4), "f", frame="cream",
                    margin=0.8)
    P += gable(C, w, d, 4.0, (cx, cy, top), "roof_dark", overhang=0.3, gable_color="brick")
    return P


def harbor_crane(C):
    """Klassischer Wippdrehkran auf Portal (Schienen entlang X). Objekt "CraneTop" dreht um Z."""
    P = []
    for (x, y) in ((-3.0, -3.0), (3.0, -3.0), (-3.0, 3.0), (3.0, 3.0)):
        P.append(beam(C, (x, y, 0), (x * 0.6, y * 0.6, 9.0), 0.5, "orange"))
        P.append(box(C, (1.2, 1.0, 0.8), (x, y, 0), "metal_dark"))
    P.append(box(C, (4.6, 4.6, 0.8), (0, 0, 9.0), "orange"))
    T = [cyl(C, 2.0, 0.6, (0, 0, 9.8), "metal_dark", segs=12),
         box(C, (4.0, 5.0, 4.0), (0, 0.8, 10.4), "orange", bevel=0.1),
         box(C, (1.6, 0.1, 1.2), (-1.0, -1.72, 12.4), "glass_dark"),
         box(C, (3.6, 2.0, 2.0), (0, 3.6, 10.6), "metal_dark")]
    T.append(beam(C, (0, -1.2, 11.5), (0, -22.0, 22.0), 0.9, "orange"))
    T.append(beam(C, (0, 0.5, 14.4), (0, 0.5, 18.0), 0.6, "orange"))
    T.append(beam(C, (0, 0.5, 18.0), (0, -22.0, 22.0), 0.08, "black"))
    T.append(beam(C, (0, -22.0, 22.0), (0, -22.0, 9.0), 0.05, "black"))
    T.append(box(C, (0.8, 0.8, 0.6), (0, -22.0, 8.4), "yellow"))
    return {"HarborCrane": (P, (0, 0, 0)), "CraneTop": (T, (0, 0, 9.8))}


def container_crane(C):
    """Containerbruecke (STS-Kran), Ausleger ueber das Wasser (-Y), 30 x 18 m Portal, 48 m hoch."""
    P = []
    for x in (-8.0, 8.0):
        for y in (-7.0, 9.0):
            P.append(box(C, (1.2, 1.2, 30.0), (x, y, 0), "red"))
            P.append(box(C, (2.0, 3.0, 1.2), (x, y, 0), "metal_dark"))
        P.append(box(C, (1.0, 17.0, 1.4), (x, 1.0, 12.0), "red"))
        P.append(beam(C, (x, -7.0, 1.0), (x, 9.0, 12.0), 0.4, "red"))
    P.append(box(C, (17.2, 1.4, 1.6), (0, -7.0, 30.0), "red"))
    P.append(box(C, (17.2, 1.4, 1.6), (0, 9.0, 30.0), "red"))
    for x in (-3.0, 3.0):
        P.append(box(C, (1.4, 70.0, 1.8), (x, -14.0, 30.0), "red"))          # Ausleger
    P.append(box(C, (3.6, 4.0, 3.0), (0, -14.0, 26.8), "white"))            # Katze/Kabine
    P.append(box(C, (3.4, 0.1, 1.2), (0, -16.05, 27.4), "glass_dark"))
    P.append(box(C, (6.4, 0.8, 0.4), (0, 21.0 - 2.0, 31.6), "red"))
    for x in (-6.0, 6.0):
        P.append(beam(C, (x * 0.5, 9.0, 31.8), (x * 0.5, 2.0, 46.0), 0.8, "red"))
        P.append(beam(C, (x * 0.5, 2.0, 46.0), (x * 0.5, -48.0, 31.8), 0.12, "metal_dark"))
        P.append(beam(C, (x * 0.5, 2.0, 46.0), (x * 0.5, 20.0, 31.8), 0.12, "metal_dark"))
    P.append(box(C, (8.0, 6.0, 3.5), (0, 16.5, 30.0), "white"))             # Maschinenhaus
    P.append(box(C, (2.6, 6.2, 2.6), (0, -14.0, 22.0), "blue"))             # haengender Container
    P.append(beam(C, (0, -14.0, 24.6), (0, -14.0, 26.8), 0.05, "black"))
    return P


# ------------------------------------------------------------------ Schiffe
def cargo_ship(C):
    """Containerschiff 80 x 13 m (Bug -Y), Brueckenhaus hinten, Containerreihen."""
    L, B = 80.0, 13.0
    P = [hull(C, L, B, 5.0, 4.0, "teal", "red", "red", bow=0.22)]
    cols = ("red", "blue", "orange", "green_dark", "yellow", "white", "steel_blue", "metal_dark")
    rnd = random.Random(8)
    for bay in range(8):
        y = -24.0 + bay * 6.6
        h = rnd.randint(2, 4)
        for row in range(4):
            x = -4.5 + row * 3.0
            for z in range(h):
                if bay < 1 and row in (0, 3):
                    continue
                P.append(box(C, (2.44, 6.06, 2.55), (x, y, 4.0 + z * 2.6), cols[rnd.randrange(len(cols))]))
    # Brueckenhaus hinten
    P.append(box(C, (11.0, 7.0, 12.0), (0, 32.0, 4.0), "white"))
    P.append(box(C, (13.0, 3.0, 2.6), (0, 30.0, 16.0), "white"))
    P += ribbon(C, 0, 30.0, 13.0, 3.0, 17.4, 1.1, "glass_dark", "f", 0.4)
    for z in (7.5, 11.0):
        P += ribbon(C, 0, 32.0, 11.0, 7.0, z, 0.8, "glass_dark", "flr", 1.0)
    P.append(box(C, (2.5, 2.5, 4.0), (0, 35.0, 16.0), "black"))
    P.append(box(C, (2.6, 2.6, 1.0), (0, 35.0, 18.6), "red"))
    P.append(box(C, (0.2, 0.2, 5.0), (0, 30.0, 18.6), "metal"))
    P.append(box(C, (B - 1.0, 1.2, 1.5), (0, -36.0, 4.0), "teal"))         # Bugschanz
    return P


def fishing_boat(C):
    """Fischkutter 14 x 4,5 m: Steuerhaus, Mast mit Ausleger, Netzrolle."""
    P = [hull(C, 14.0, 4.5, 1.5, 1.6, "blue", "wood", "red", bow=0.35)]
    P.append(box(C, (2.6, 3.0, 2.2), (0, 2.0, 1.6), "white"))
    P += ribbon(C, 0, 2.0, 2.6, 3.0, 3.1, 0.6, "glass_dark", "flr", 0.25)
    P.append(box(C, (2.8, 3.2, 0.15), (0, 2.0, 3.8), "red"))
    P.append(cyl(C, 0.1, 6.0, (0, -1.6, 1.6), "metal", segs=6))
    P.append(beam(C, (0, -1.6, 6.5), (2.6, 4.6, 3.0), 0.08, "metal"))
    P.append(beam(C, (0, -1.6, 6.5), (-2.6, 4.6, 3.0), 0.08, "metal"))
    P.append(cyl(C, 0.6, 1.8, (-0.9, 5.4, 2.2), "orange", segs=10, rot=(0, 90, 0)))
    for k in range(3):
        P.append(box(C, (0.8, 0.8, 0.5), (1.2, -3.0 + k * 0.9, 1.6), ("teal", "white", "teal")[k]))
    return P


def sailboat(C):
    """Segelyacht 9 x 3 m mit Mast (12 m), Grosssegel und Fock."""
    P = [hull(C, 9.0, 3.0, 0.8, 0.9, "white", "wood", "steel_blue", bow=0.4)]
    P.append(box(C, (1.6, 2.4, 0.6), (0, 0.4, 0.9), "white", bevel=0.15))
    P.append(box(C, (1.4, 0.06, 0.3), (0, -0.82, 1.2), "glass_dark"))
    P.append(box(C, (0.4, 1.4, 1.4), (0, 0, -2.2), "steel_blue"))           # Kiel
    P.append(cyl(C, 0.08, 12.0, (0, -0.8, 0.9), "metal", segs=6))
    sail = prism(C, [(0, 0), (3.6, 0), (0, 10.5)], 0.06, (0, 0, 0), "white", name="sail")
    sail.data.transform(Matrix.Translation((0, -0.6, 1.8)) @ Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(sail)
    jib = prism(C, [(0, 0), (-3.4, 0), (0, 10.2)], 0.06, (0, 0, 0), "foam", name="jib")
    jib.data.transform(Matrix.Translation((0, -0.95, 1.2)) @ Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(jib)
    P.append(beam(C, (0, -0.6, 1.9), (0, 3.2, 1.9), 0.1, "metal"))
    return P


def tugboat(C):
    """Schlepper 18 x 7 m: hoher Aufbau, Fender rundum, Schornstein."""
    P = [hull(C, 18.0, 7.0, 2.5, 2.0, "red", "wood_dark", "black", bow=0.35)]
    o = ship_outline(18.0, 7.0, 0.35)
    P.append(loft(C, [([(x * 1.04, y * 1.02) for x, y in o], 1.3), ([(x * 1.04, y * 1.02) for x, y in o], 2.0)],
                  ["black"], name="fender"))
    P.append(box(C, (4.6, 6.0, 2.6), (0, 0.5, 2.0), "white"))
    P.append(box(C, (3.8, 3.2, 2.0), (0, -0.4, 4.6), "white"))
    P += ribbon(C, 0, -0.4, 3.8, 3.2, 5.8, 0.8, "glass_dark", "flr", 0.2)
    P.append(box(C, (4.2, 3.6, 0.2), (0, -0.4, 6.6), "red"))
    P.append(cyl(C, 0.6, 3.0, (0, 3.0, 4.6), "black", segs=8))
    P.append(cyl(C, 0.62, 0.6, (0, 3.0, 6.0), "yellow", segs=8))
    P.append(box(C, (1.2, 1.2, 0.8), (0, 6.0, 2.0), "metal_dark"))           # Schleppwinde
    return P


def ferry(C):
    """Faehre 42 x 12 m: zwei Decks, Autodeck offen, Bruecke vorne."""
    P = [hull(C, 42.0, 12.0, 2.8, 2.2, "white", "asphalt", "steel_blue", bow=0.18)]
    P.append(box(C, (12.0, 28.0, 3.0), (0, 4.0, 2.2), "white"))
    P += ribbon(C, 0, 4.0, 12.0, 28.0, 3.8, 1.4, "glass_dark", "lr", 1.0)
    P.append(box(C, (12.2, 28.2, 0.4), (0, 4.0, 4.4), "steel_blue"))
    P.append(box(C, (9.0, 18.0, 2.6), (0, 2.0, 5.2), "white"))
    P += ribbon(C, 0, 2.0, 9.0, 18.0, 6.6, 1.2, "glass_dark", "flr", 0.8)
    P.append(box(C, (11.0, 3.0, 2.2), (0, -8.0, 7.8), "white"))
    P += ribbon(C, 0, -8.0, 11.0, 3.0, 9.0, 0.9, "glass_dark", "f", 0.4)
    for x in (-2.0, 2.0):
        P.append(cyl(C, 0.9, 3.0, (x, 8.0, 7.8), "steel_blue", segs=8))
        P.append(cyl(C, 0.92, 0.5, (x, 8.0, 10.4), "black", segs=8))
    for k in range(3):
        P += parked_car(C, -3.0 + k * 3.0, 17.0, 0, ("red", "white", "blue")[k])
        for o in P[-7:]:
            o.data.transform(Matrix.Translation((0, 0, 2.2)))
    return P


def buoy(C, color="red"):
    return [lathe(C, [(0, -1.0), (0.6, -0.6), (0.8, 0.0), (0.7, 0.5), (0.25, 0.6), (0.25, 2.2), (0, 2.6)], (0, 0, 0),
                  segs=10, colors=["black", color, color, color, color, color]),
            sphere(C, 0.18, (0, 0, 2.6), "light_on", subdiv=1)]


COAST = {
    "Beach": beach, "BeachChair": lambda C: beach_chair(C), "LifeguardTower": lifeguard_tower,
    "Pier": pier, "Lighthouse": lighthouse, "Breakwater": breakwater, "Quay": quay,
    "HarborWarehouse": harbor_warehouse, "HarborCrane": harbor_crane, "ContainerCrane": container_crane,
    "CargoShip": cargo_ship, "FishingBoat": fishing_boat, "Sailboat": sailboat, "Tugboat": tugboat, "Ferry": ferry,
    "Buoy_Red": lambda C: buoy(C, "red"), "Buoy_Green": lambda C: buoy(C, "green"),
}
run_family(COAST, "Coast", views=V)
