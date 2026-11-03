"""Landwirtschaft (Models/Agriculture): Felder (32 x 32 m Kacheln, kachelbar), Hof, Gewaechshaus, Weide,
Obstplantage, Weinberg, Windmuehle (Fluegel separat: "Sails"), Traktor, Heuballen.
Felder: Reihen laufen entlang X, Rand 0,6 m Grasstreifen -> nebeneinander gelegt entsteht ein Feldweg-Raster."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

F = 32.0          # Feldkachel
IN = F - 1.2      # bepflanzte Flaeche
VF = {"": (0.9, -1.1, 0.75)}


def field_base(C, soil="soil"):
    return [box(C, (F, F, 0.1), (0, 0, 0), "grass"), box(C, (IN, IN, 0.06), (0, 0, 0.1), soil)]


def rows(C, pitch, w, h, color, z=0.16, jitter=0.0, seed=0, bevel=0.0):
    rnd = random.Random(seed)
    P = []
    n = int(IN / pitch)
    y0 = -(n - 1) * pitch / 2
    for i in range(n):
        hh = h * (1 + rnd.uniform(-jitter, jitter))
        P.append(box(C, (IN - 0.4, w, hh), (0, y0 + i * pitch, z), color, bevel=bevel))
    return P


def field_wheat(C):
    P = field_base(C, "soil_light")
    P += rows(C, 0.75, 0.68, 0.9, "wheat", jitter=0.06, seed=1)
    # Ahren-Andeutung: hellere Kappe auf jeder zweiten Reihe
    n = int(IN / 0.75)
    for i in range(0, n, 2):
        P.append(box(C, (IN - 0.6, 0.4, 0.08), (0, -(n - 1) * 0.375 + i * 0.75, 1.06), "sand"))
    return P


def field_wheat_harvested(C):
    P = field_base(C, "soil_light")
    P += rows(C, 0.5, 0.3, 0.12, "grass_dry")
    for (x, y) in ((-8, -6), (-2, 4), (6, -3), (10, 8), (-11, 9), (3, -11)):
        P.append(cyl(C, 0.75, 1.2, (x - 0.6, y, 0.85), "wheat", segs=10, rot=(0, 90, 0)))
    return P


def field_corn(C):
    P = field_base(C)
    P += rows(C, 1.0, 0.55, 2.1, "corn", jitter=0.08, seed=2)
    n = int(IN / 1.0)
    for i in range(n):
        P.append(box(C, (IN - 0.6, 0.2, 0.25), (0, -(n - 1) * 0.5 + i * 1.0, 2.25), "wheat"))
    return P


def field_rapeseed(C):
    P = field_base(C)
    P += rows(C, 0.7, 0.66, 1.1, "corn", seed=3)
    n = int(IN / 0.7)
    for i in range(n):
        P.append(box(C, (IN - 0.5, 0.62, 0.22), (0, -(n - 1) * 0.35 + i * 0.7, 1.22), "rapeseed"))
    return P


def field_potato(C):
    P = field_base(C)
    n = int(IN / 0.9)
    for i in range(n):
        y = -(n - 1) * 0.45 + i * 0.9
        P.append(prism(C, [(-0.4, 0), (0.4, 0), (0, 0.35)], IN - 0.4, (0, 0, 0), "soil"))
        P[-1].data.transform(Matrix.Translation((-(IN - 0.4) / 2, y, 0.16)) @
                             Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
        P.append(box(C, (IN - 0.8, 0.45, 0.35), (0, y, 0.4), "green_dark", bevel=0.1))
    return P


def field_vegetables(C):
    rnd = random.Random(5)
    P = field_base(C)
    n = 14
    for i in range(n):
        y = -IN / 2 + 1.0 + i * (IN - 2.0) / (n - 1)
        kind = i % 3
        if kind == 0:      # Kohl
            for k in range(14):
                P.append(sphere(C, 0.45, (-IN / 2 + 1.2 + k * 2.1, y, 0.45), "green", subdiv=1, scale=(1, 1, 0.8)))
        elif kind == 1:    # Salat (hellgruen, flach)
            for k in range(18):
                P.append(sphere(C, 0.35, (-IN / 2 + 1.0 + k * 1.65, y, 0.3), "pastel_green", subdiv=0, scale=(1, 1, 0.6)))
        else:              # Kuerbis
            for k in range(10):
                if rnd.random() < 0.8:
                    P.append(sphere(C, 0.4, (-IN / 2 + 1.5 + k * 3.0, y, 0.45), "orange", subdiv=1, scale=(1, 1, 0.75)))
            P.append(box(C, (IN - 1.0, 0.3, 0.12), (0, y, 0.16), "green_dark"))
    return P


def field_plowed(C):
    P = field_base(C)
    n = int(IN / 0.8)
    for i in range(n):
        y = -(n - 1) * 0.4 + i * 0.8
        P.append(prism(C, [(-0.4, 0), (0.4, 0), (0, 0.3)], IN - 0.4, (0, 0, 0), "soil"))
        P[-1].data.transform(Matrix.Translation((-(IN - 0.4) / 2, y, 0.16)) @
                             Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    return P


def field_meadow(C):
    """Wiese mit Wildblumen (Brache)."""
    rnd = random.Random(7)
    P = [box(C, (F, F, 0.12), (0, 0, 0), "grass")]
    for i in range(220):
        x, y = rnd.uniform(-15, 15), rnd.uniform(-15, 15)
        c = rnd.choice(("grass", "green", "grass_dry", "flower_pink", "yellow", "white", "flower_violet", "grass"))
        P.append(sphere(C, rnd.uniform(0.15, 0.35), (x, y, 0.15), c, subdiv=0, scale=(1, 1, 0.7)))
    for (x, y) in ((-10, 9), (11, -8)):
        P += tree(C, x, y, 6.0, 2.2, seed=x)
    return P


# ------------------------------------------------------------------ Hof & Gebaeude
def cow(C, x, y, rot=0, spots=True):
    P = [box(C, (0.8, 1.9, 0.9), (0, 0, 0.75), "white", bevel=0.12),
         box(C, (0.5, 0.6, 0.55), (0, -1.15, 1.25), "white", bevel=0.08),
         box(C, (0.42, 0.25, 0.3), (0, -1.5, 1.15), "pastel_pink"),
         box(C, (0.7, 0.08, 0.08), (0, -1.05, 1.62), "cream")]
    for (lx, ly) in ((-0.28, -0.7), (0.28, -0.7), (-0.28, 0.7), (0.28, 0.7)):
        P.append(box(C, (0.18, 0.18, 0.78), (lx, ly, 0), "white"))
    if spots:
        P.append(box(C, (0.82, 0.6, 0.5), (0, 0.3, 1.1), "black"))
        P.append(box(C, (0.4, 0.5, 0.4), (0.22, -0.5, 0.85), "black"))
    P.append(beam(C, (0, 0.95, 1.5), (0, 1.15, 0.8), 0.06, "white"))
    return place_at(P, x, y, rot)


def tractor(C, x=0, y=0, rot=0, color="green"):
    P = [box(C, (1.6, 2.6, 0.8), (0, -0.3, 0.7), color, bevel=0.08),
         box(C, (1.4, 1.4, 1.4), (0, 0.6, 1.4), "glass_dark", bevel=0.05),
         box(C, (1.6, 1.6, 0.12), (0, 0.6, 2.8), color),
         cyl(C, 0.08, 0.8, (0.45, -1.1, 1.5), "black", segs=6)]
    for s in (-1, 1):
        P.append(cyl(C, 0.75, 0.45, (s * 0.8 + (0 if s > 0 else -0.45), 0.8, 0.75), "black", segs=12, rot=(0, 90, 0)))
        P.append(cyl(C, 0.45, 0.3, (s * 0.75 + (0 if s > 0 else -0.3), -1.1, 0.45), "black", segs=10, rot=(0, 90, 0)))
        P.append(cyl(C, 0.35, 0.47, (s * 0.8 + (0.01 if s > 0 else -0.46), 0.8, 0.75), "yellow", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def haybales(C):
    P = []
    for (x, y, z) in ((0, 0, 0), (1.3, 0.1, 0), (0.65, 0.05, 1.1)):
        P.append(cyl(C, 0.6, 1.2, (x - 0.6, y, z + 0.6), "wheat", segs=10, rot=(0, 90, 0)))
    return P


def barn(C, cx, cy, w, d, h, wall="red", rot=0):
    P = [box(C, (w, d, h), (0, 0, 0), wall)]
    # First entlang Y -> Giebel (mit Tor) zeigt nach vorne
    P += gable(C, d, w, w * 0.38, (0, 0, h), "roof_dark", overhang=0.4, rot=(0, 0, 90), gable_color=wall)
    P.append(box(C, (3.6, 0.12, 3.6), (0, -d / 2 - 0.06, 0), "wood_dark"))
    for s in (-1, 1):   # weisses Andreaskreuz-Tor
        P.append(beam(C, (s * 1.7, -d / 2 - 0.13, 0.1), (-s * 1.7, -d / 2 - 0.13, 3.5), 0.15, "white"))
    P.append(box(C, (3.9, 0.16, 0.18), (0, -d / 2 - 0.1, 3.6), "white"))
    P.append(box(C, (1.0, 0.12, 1.0), (0, -d / 2 - 0.06, h + 0.6), "white"))  # Heuluke
    return place_at(P, cx, cy, rot)


def farm(C):
    """Bauernhof (Vorschlag 32 x 24 m): Wohnhaus, Scheune, Silos, Hof mit Traktor, Heu, Huehnerstall."""
    P = [box(C, (32, 24, 0.1), (0, 0, 0), "grass"), box(C, (16, 12, 0.05), (2, -2, 0.1), "gravel")]
    # Wohnhaus links
    p, top = block(C, -10.5, -3.0, 8.0, 7.0, 2, 2.9, wall="cream", z0=PLINTH, spacing=2.2, win_size=(1.0, 1.2),
                   frame="wood_dark", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    P += p
    P += half_timber(C, -10.5, -3.0, 8.0, 7.0, PLINTH + 2.9, 2.9, 1, "fl", bay=2.0)
    P += gable(C, 8.0, 7.0, 3.2, (-10.5, -3.0, top), "roof_red", overhang=0.45, gable_color="cream")
    P += door(C, -10.5, -6.5, 1.1, 2.2, "wood_dark", canopy="roof_red")
    P.append(box(C, (0.6, 0.6, 1.4), (-8.5, -2.0, top + 1.4), "brick_dark"))
    # Scheune hinten rechts
    P += barn(C, 6.0, 6.0, 14.0, 10.0, 6.0, "red")
    # Silos
    for k, x in enumerate((-3.0, 0.4)):
        P.append(cyl(C, 1.5, 10.0, (x, 8.6, 0), "metal", segs=12))
        P.append(lathe(C, [(1.6, 0), (0, 1.4)], (x, 8.6, 10.0), "steel_blue", segs=12))
        for z in (3.0, 6.5):
            P.append(cyl(C, 1.53, 0.2, (x, 8.6, z), "metal_dark", segs=12))
    # Huehnerstall + Auslauf
    P.append(box(C, (3.0, 2.2, 1.8), (-11.5, 8.0, 0), "wood"))
    P.append(gable(C, 3.0, 2.2, 0.8, (-11.5, 8.0, 1.8), "roof_dark", overhang=0.15)[0])
    P += fence(C, [(-14.5, 5.0), (-8.5, 5.0), (-8.5, 10.8), (-14.5, 10.8)], h=1.0, spacing=1.5, post="wood",
               rail="wood")
    rnd = random.Random(3)
    for i in range(5):
        P.append(sphere(C, 0.18, (rnd.uniform(-13.8, -9.2), rnd.uniform(5.5, 7.0), 0.25), "white", subdiv=0))
    # Hofausstattung
    P += tractor(C, 4.0, -5.0, 30, "green")
    for o in haybales(C):
        o.data.transform(Matrix.Translation((10.5, -7.0, 0.1)))
        P.append(o)
    P.append(box(C, (2.0, 1.0, 0.8), (-4.0, -9.0, 0.1), "metal"))       # Milchkannen-Bank
    for k in range(3):
        P.append(cyl(C, 0.22, 0.6, (-4.6 + k * 0.6, -9.0, 0.9), "metal", segs=8))
    P += tree(C, -14.0, -9.5, 7.0, 2.6, seed=9)
    P += tree(C, 14.0, -10.0, 6.0, 2.0, seed=10)
    P += fence(C, [(-15.8, -11.8), (-3.0, -11.8)], h=1.1, spacing=2.2, post="wood", rail="wood", closed=False)
    P += fence(C, [(3.0, -11.8), (15.8, -11.8)], h=1.1, spacing=2.2, post="wood", rail="wood", closed=False)
    return P


def greenhouse(C):
    """Gewaechshaus (16 x 24 m): Glas-Satteldach auf Stahlrahmen, Pflanzreihen innen."""
    P = [box(C, (16, 24, 0.1), (0, 0, 0), "gravel"), box(C, (14.6, 22.6, 0.4), (0, 0, 0.1), "concrete")]
    for k in range(4):
        P.append(box(C, (1.6, 20.0, 0.5), (-5.0 + k * 3.3, 0.5, 0.5), "soil"))
        for j in range(16):
            P.append(sphere(C, 0.42, (-5.0 + k * 3.3, -8.6 + j * 1.22, 1.15), ("green", "red", "green")[(j + k) % 3],
                            subdiv=0))
    P.append(box(C, (14.4, 22.4, 3.0), (0, 0, 0.5), "glass_light"))
    P += gable(C, 22.4, 14.4, 2.4, (0, 0, 3.5), "glass_light", overhang=0.05, rot=(0, 0, 90), gable_color="glass_light")
    for k in range(9):
        y = -11.2 + k * 2.8
        P.append(box(C, (14.5, 0.12, 3.0), (0, y, 0.5), "white"))
        P.append(beam(C, (-7.25, y, 3.5), (0, y, 5.9), 0.12, "white"))
        P.append(beam(C, (7.25, y, 3.5), (0, y, 5.9), 0.12, "white"))
    for s in (-1, 1):
        P.append(box(C, (0.12, 22.5, 0.15), (s * 7.25, 0, 3.45), "white"))
    P.append(box(C, (1.8, 0.14, 2.4), (0, -11.25, 0.5), "white"))
    P.append(box(C, (2.0, 1.4, 2.0), (-6.0, 11.8, 0.1), "metal"))      # Technikkasten hinten
    return P


def pasture(C):
    """Weide (32 x 32 m) mit Holzzaun, Kuehen, Tranke und Baum."""
    P = [box(C, (F, F, 0.12), (0, 0, 0), "grass")]
    rnd = random.Random(11)
    for i in range(60):
        P.append(sphere(C, rnd.uniform(0.2, 0.4), (rnd.uniform(-15, 15), rnd.uniform(-15, 15), 0.12), "green",
                        subdiv=0, scale=(1, 1, 0.5)))
    P += fence(C, [(-15.7, -15.7), (15.7, -15.7), (15.7, 15.7), (-15.7, 15.7)], h=1.3, spacing=3.0, post="wood_dark",
               rail="wood", gaps=(0,))
    P += fence(C, [(-15.7, -15.7), (-2.0, -15.7)], h=1.3, spacing=3.0, post="wood_dark", rail="wood", closed=False)
    P += fence(C, [(2.0, -15.7), (15.7, -15.7)], h=1.3, spacing=3.0, post="wood_dark", rail="wood", closed=False)
    for i, (x, y, r) in enumerate(((-6, -4, 20), (-2, 3, 160), (5, -6, 75), (8, 5, 250), (-9, 8, 300), (2, 10, 40))):
        P += cow(C, x, y, r, spots=(i % 3 != 2))
    P.append(box(C, (3.0, 0.8, 0.6), (10.0, -11.0, 0.12), "metal"))
    P.append(box(C, (2.8, 0.6, 0.05), (10.0, -11.0, 0.66), "water"))
    P += tree(C, -10.0, -10.0, 7.0, 2.8, seed=12)
    return P


def orchard(C):
    """Obstplantage (32 x 32 m): 6 x 6 niedrige Apfelbaeume in Reihen, Grasweg."""
    P = [box(C, (F, F, 0.12), (0, 0, 0), "grass")]
    rnd = random.Random(13)
    for i in range(6):
        for j in range(6):
            x, y = -12.5 + i * 5.0, -12.5 + j * 5.0
            P.append(cyl(C, 0.15, 1.6, (x, y, 0.1), "wood_dark", segs=6))
            P.append(sphere(C, 1.6, (x, y, 2.6), "green", subdiv=1, scale=(1, 1, 0.85)))
            for k in range(4):
                a = rnd.uniform(0, 2 * math.pi)
                P.append(sphere(C, 0.18, (x + math.cos(a) * 1.4, y + math.sin(a) * 1.4, 2.4 + rnd.uniform(-0.4, 0.6)),
                                "red", subdiv=0))
    for k in range(3):
        P.append(box(C, (1.0, 0.7, 0.5), (14.0, -3.0 + k * 1.1, 0.12), "wood"))
    return P


def vineyard(C):
    """Weinberg (32 x 32 m): Rebzeilen an Drahtrahmen, Trauben, Weg."""
    P = [box(C, (F, F, 0.12), (0, 0, 0), "soil_light")]
    n = 14
    for i in range(n):
        y = -14.0 + i * 2.15
        for k in range(8):
            P.append(box(C, (0.1, 0.1, 1.9), (-14.0 + k * 4.0, y, 0.1), "wood_dark"))
        P.append(box(C, (28.0, 0.6, 1.0), (0, y, 0.8), "green_dark", bevel=0.15))
        for k in range(10):
            P.append(sphere(C, 0.16, (-13.0 + k * 2.9, y - 0.32, 0.9), "flower_violet", subdiv=0, scale=(1, 1, 1.4)))
    return P


def windmill(C):
    """Historische Windmuehle (12 x 12 m): Turm aus Ziegel, Kappe, Fluegel als eigenes Objekt "Sails"."""
    P = [box(C, (12, 12, 0.1), (0, 0, 0), "grass"), cyl(C, 4.6, 0.5, (0, 0, 0.1), "concrete", segs=8)]
    P.append(lathe(C, [(0, 0), (4.0, 0), (2.9, 12.0), (0, 12.0)], (0, 0, 0.6), "white", segs=8))
    P.append(cyl(C, 4.25, 0.3, (0, 0, 4.0), "wood_dark", segs=8))      # Galerie
    for i in range(8):
        a = 2 * math.pi * (i + 0.5) / 8
        P.append(box(C, (0.08, 0.08, 1.0), (4.15 * math.cos(a), 4.15 * math.sin(a), 4.3), "wood_dark"))
    P.append(lathe(C, [(0, 0), (3.2, 0), (3.0, 1.2), (1.6, 3.2), (0, 3.6)], (0, 0, 12.6), "roof_dark", segs=8))
    P.append(box(C, (1.2, 0.12, 2.0), (0, -3.85, 0.6), "wood_dark"))
    for z in (6.0, 9.5):   # Fenster auf der konischen Wand (Radius nimmt nach oben ab)
        r = 4.0 - (z - 0.6) / 12.0 * 1.1
        P.append(box(C, (0.9, 0.12, 1.0), (0, -r - 0.02, z), "glass"))
    hub = Vector((0, -3.4, 14.0))
    S = [cyl(C, 0.45, 1.2, (hub.x, hub.y + 0.6, hub.z), "wood_dark", segs=8, rot=(90, 0, 0))]
    for k in range(4):
        parts = [box(C, (0.25, 0.25, 9.0), (0, 0, 0.5), "wood_dark"),
                 box(C, (1.8, 0.08, 7.2), (0.95, 0, 2.2), "cream")]
        for j in range(6):
            parts.append(box(C, (1.9, 0.1, 0.06), (0.95, -0.05, 2.4 + j * 1.2), "wood_dark"))
        for o in parts:
            o.data.transform(Matrix.Translation(hub) @ Matrix.Rotation(math.radians(90 * k + 20), 4, 'Y'))
        S += parts
    return {"Windmill": (P, (0, 0, 0)), "Sails": (S, tuple(hub))}


AG = {
    "Field_Wheat": field_wheat, "Field_Wheat_Harvested": field_wheat_harvested, "Field_Corn": field_corn,
    "Field_Rapeseed": field_rapeseed, "Field_Potato": field_potato, "Field_Vegetables": field_vegetables,
    "Field_Plowed": field_plowed, "Field_Meadow": field_meadow,
    "Farm": farm, "Greenhouse": greenhouse, "Pasture": pasture, "Orchard": orchard, "Vineyard": vineyard,
    "Windmill": windmill,
    "Tractor": lambda C: tractor(C), "HayBales": haybales, "Cow": lambda C: cow(C, 0, 0),
}
run_family(AG, "Agriculture", views=VF)
