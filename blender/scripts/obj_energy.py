"""Weitere Energie (Models/Energy): Wasserkraftwerk mit Bogenstaumauer, Biogasanlage, Offshore-Windrad
("Rotor" separat, Ursprung = Nabe), Umspannwerk, Batteriespeicher.
Ursprung = Mitte Grundflaeche, Vorderseite = -Y. Offshore: Wasserspiegel z = 0."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.25, 0.65)}


def arc_pts(cx, cy, R, a0, a1, n):
    """Punkte auf einem Kreisbogen, Winkel in Grad ab +Y im Uhrzeigersinn (x = R sin a, y = R cos a)."""
    return [(cx + R * math.sin(math.radians(a0 + (a1 - a0) * k / n)),
             cy + R * math.cos(math.radians(a0 + (a1 - a0) * k / n))) for k in range(n + 1)]


# ------------------------------------------------------------------ Wasserkraftwerk
def hydro_dam(C):
    """Bogenstaumauer (Diorama 64 x 56 m): Mauer 22 m hoch zwischen Felsflanken, Stausee hinten (Spiegel z = 19),
    Krafthaus am Mauerfuss, Druckrohre, Unterwasser-Fluss nach vorne (z = 0)."""
    P = [box(C, (64.0, 56.0, 0.1), (0, 0, -0.1), "grass")]
    # Unterwasser (Fluss) nach vorne
    P.append(box(C, (14.0, 30.0, 0.12), (0, -13.0, -0.08), "water"))
    for s in (-1, 1):
        P.append(box(C, (1.5, 30.0, 0.4), (s * 7.7, -13.0, -0.1), "rock"))
    # Stausee-Plateau hinten + Wasser
    # Plateau beginnt hinter der Mauer (Mitte der Wasserseite y = 12,5); seitlich bis an die Mauerenden aufgefuellt
    P.append(box(C, (64.0, 15.0, 19.0), (0, 20.5, 0), "rock"))
    P.append(box(C, (64.0, 15.0, 0.5), (0, 20.5, 19.0), "grass"))
    for s in (-1, 1):
        P.append(box(C, (12.0, 11.0, 19.0), (s * 26.0, 7.5, 0), "rock"))
        P.append(box(C, (12.0, 11.0, 0.5), (s * 26.0, 7.5, 19.0), "grass"))
    # Mauer: Kreisringsektor (Luftseite R1, Wasserseite R2), Krone etwas breiter
    cx, cy, A = 0.0, -22.0, 38.0
    R1, R2 = 31.0, 34.5
    # Stausee: Umriss folgt der Wasserseite der Mauer (Unterbau Fels, Spiegel z = 19,4)
    lake = arc_pts(cx, cy, R2 - 0.3, -A, A, 16) + [(24.0, 27.0), (-24.0, 27.0)]
    P.append(prism(C, lake, 19.2, (0, 0, 0), "rock", name="lake_base"))
    P.append(prism(C, lake, 0.2, (0, 0, 19.2), "water", name="lake"))
    outer = arc_pts(cx, cy, R1, -A, A, 16)
    inner = arc_pts(cx, cy, R2, A, -A, 16)
    P.append(prism(C, outer + inner, 21.0, (0, 0, 0), "concrete", name="dam"))
    crest_o = arc_pts(cx, cy, R1 - 0.6, -A - 1, A + 1, 16)
    crest_i = arc_pts(cx, cy, R2 + 0.3, A + 1, -A - 1, 16)
    P.append(prism(C, crest_o + crest_i, 1.0, (0, 0, 21.0), "concrete_dark", name="crest"))
    road_o = arc_pts(cx, cy, R1 + 0.4, -A - 1, A + 1, 16)
    road_i = arc_pts(cx, cy, R2 - 0.6, A + 1, -A - 1, 16)
    P.append(prism(C, road_o + road_i, 0.06, (0, 0, 22.0), "asphalt", name="road"))
    for R in (R1 - 0.4, R2 + 0.1):                          # Gelaenderpfosten
        for (x, y) in arc_pts(cx, cy, R, -A, A, 24):
            P.append(box(C, (0.12, 0.12, 1.0), (x, y, 22.0), "metal"))
    # Horizontale Fugenlinien auf der Luftseite
    for z in (6.0, 12.0, 17.0):
        band_o = arc_pts(cx, cy, R1 - 0.08, -A, A, 16)
        band_i = arc_pts(cx, cy, R1 + 0.2, A, -A, 16)
        P.append(prism(C, band_o + band_i, 0.3, (0, 0, z), "concrete_dark", name="joint"))
    # Hochwasser-Ueberlauf in der Mitte (Schussrinne auf der Luftseite)
    for x in (-3.0, 0.0, 3.0):
        P.append(box(C, (0.8, 4.2, 2.2), (x, cy + R1 + 1.4, 21.5), "concrete_dark"))
    P.append(box(C, (7.0, 0.3, 21.0), (0, cy + R1 - 0.1, 0.0), "foam"))
    # Felsflanken
    for s in (-1, 1):
        P.append(mound(C, 6.5, 16.0, 24.0, (s * 26.5, 8.0, 0), "rock", seed=3 + s, subdiv=2))
        P.append(mound(C, 4.5, 6.0, 9.0, (s * 26.0, -6.0, 0), "rock_dark", seed=7 + s, subdiv=2))
        P.append(mound(C, 3.0, 3.0, 2.0, (s * 13.0, -22.0, 0), "rock", seed=11 + s, subdiv=1))
        P += tree(C, s * 24.0, -20.0, 7.0, 2.0, "pine", seed=2 + s)
        P += tree(C, s * 28.0, -24.0, 6.0, 1.7, "pine", seed=4 + s)
    # Krafthaus am Mauerfuss
    hy = cy + R1 - 7.0
    P.append(box(C, (16.0, 9.0, 8.0), (0, hy, 0), "concrete"))
    P += ribbon(C, 0, hy, 16.0, 9.0, 5.6, 1.6, "glass", "flr", 0.8)
    P.append(box(C, (16.6, 9.6, 0.5), (0, hy, 8.0), "concrete_dark"))
    P.append(box(C, (6.0, 0.1, 4.0), (-4.0, hy - 4.55, 0.0), "metal_dark"))
    for x in (-5.0, 0.0, 5.0):                                   # Druckrohre von der Mauer zum Krafthaus
        P.append(pipe(C, (x, cy + R1 + 0.5, 15.0), (x, hy + 4.0, 7.0), 0.9, "steel_blue", segs=10))
        P.append(box(C, (2.4, 1.4, 1.4), (x, hy - 4.6, 0.0), "concrete_dark"))      # Auslass
        P.append(box(C, (2.2, 3.0, 0.08), (x, hy - 6.4, 0.04), "foam"))
    # Schaltanlage / Trafos neben dem Krafthaus
    for x in (12.0, 15.5):
        P.append(box(C, (2.6, 2.0, 2.6), (x, hy, 0.0), "metal_dark"))
        for k in range(3):
            P.append(cyl(C, 0.12, 1.4, (x - 0.8 + k * 0.8, hy, 2.6), "white", segs=6))
    P += fence(C, [(10.0, hy - 2.5), (17.5, hy - 2.5), (17.5, hy + 2.5), (10.0, hy + 2.5)], 2.0, 2.5)
    return P


# ------------------------------------------------------------------ Biogas
def dome_tank(C, x, y, r, h, dome="pipe_green"):
    return [cyl(C, r, h, (x, y, 0), "concrete", segs=16),
            cyl(C, r + 0.15, 0.4, (x, y, h - 0.4), "concrete_dark", segs=16),
            lathe(C, [(r, h), (r * 0.92, h + r * 0.28), (r * 0.7, h + r * 0.5), (r * 0.38, h + r * 0.62), (0, h + r * 0.66)],
                  (x, y, 0), dome, segs=16)]


def biogas(C):
    """Biogasanlage (40 x 32 m): zwei Fermenter mit Gasdach, Gaerrestlager, BHKW-Container, Fahrsilo,
    Gasfackel, Rohrbruecke."""
    P = [box(C, (40.0, 32.0, 0.1), (0, 0, 0), "gravel")]
    P += dome_tank(C, -11.0, 6.0, 7.0, 6.0, "pipe_green")
    P += dome_tank(C, 5.0, 8.0, 6.0, 6.0, "pipe_green")
    P += dome_tank(C, 13.5, -4.0, 5.0, 4.0, "roof_light")
    # Fahrsilo mit Silage (Mais) und Abdeckfolie
    P.append(box(C, (16.0, 0.6, 3.0), (-11.0, -6.4, 0.1), "concrete"))
    P.append(box(C, (16.0, 0.6, 3.0), (-11.0, -13.4, 0.1), "concrete"))
    P.append(box(C, (0.6, 7.6, 3.0), (-18.7, -9.9, 0.1), "concrete"))
    P.append(prism(C, [(-18.4, -6.7), (-5.0, -6.7), (-2.6, -9.9), (-5.0, -13.1), (-18.4, -13.1)], 2.6,
                   (0, 0, 0.1), "corn"))
    P.append(box(C, (8.0, 6.5, 0.15), (-14.0, -9.9, 2.7), "black"))
    for k in range(6):
        P.append(cyl(C, 0.35, 0.3, (-17.0 + k * 1.2, -9.9 + (k % 2) * 1.6 - 0.8, 2.85), "black", segs=8,
                     rot=(0, 90, 0)))       # Reifen als Folienbeschwerung
    # BHKW-Container + Kamin
    P.append(box(C, (3.0, 9.0, 3.0), (4.0, -10.0, 0.1), "green_dark", bevel=0.05))
    P.append(box(C, (2.6, 1.2, 0.8), (4.0, -12.0, 3.1), "metal"))
    P.append(cyl(C, 0.3, 4.5, (4.0, -7.5, 3.1), "metal", segs=8))
    P.append(box(C, (3.2, 2.4, 2.5), (8.5, -13.0, 0.1), "white"))
    # Fackel
    P.append(cyl(C, 0.35, 7.0, (17.0, 12.0, 0.1), "metal", segs=8))
    P.append(cyl(C, 0.6, 0.6, (17.0, 12.0, 7.1), "metal_dark", segs=8))
    P.append(box(C, (1.6, 1.6, 0.3), (17.0, 12.0, 0.1), "concrete"))
    # Rohrbruecke zwischen den Tanks
    pts = [(-4.0, 6.5, 3.4), (-1.0, 6.5, 3.4), (-1.0, -6.0, 3.4), (4.0, -6.0, 3.4)]
    P += polyline_pipe(C, pts, r=0.22, color="yellow", segs=6)
    P += polyline_pipe(C, [(11.0, 8.0, 2.0), (13.0, 4.0, 2.0), (13.0, 1.0, 2.0)], r=0.25, color="metal", segs=6)
    for (x, y) in ((-1.0, 3.0), (-1.0, -2.0), (1.5, -6.0)):
        P.append(box(C, (0.25, 0.25, 3.3), (x, y, 0.1), "metal_dark"))
    P += fence(C, [(19.8, -15.8), (19.8, 15.8), (-19.8, 15.8), (-19.8, -15.8)], 1.8, 3.0, closed=False)
    P += tractor_trailer(C, 0.0, -2.5)
    return P


def tractor_trailer(C, x, y):
    """Kleiner Traktor mit Haenger (laengs X)."""
    Q = [box(C, (2.4, 1.8, 1.2), (0, 0, 0.6), "green", bevel=0.08),
         box(C, (1.4, 1.7, 1.6), (-0.9, 0, 1.6), "glass_dark", bevel=0.05),
         box(C, (1.6, 1.9, 0.12), (-0.9, 0, 3.2), "green")]
    Q.append(cyl(C, 0.85, 0.5, (-0.9, -1.2, 0.85), "black", segs=10, rot=(90, 0, 0)))
    Q.append(cyl(C, 0.85, 0.5, (-0.9, 1.2, 0.85), "black", segs=10, rot=(-90, 0, 0)))
    Q.append(cyl(C, 0.5, 0.35, (1.0, -1.0, 0.5), "black", segs=8, rot=(90, 0, 0)))
    Q.append(cyl(C, 0.5, 0.35, (1.0, 1.0, 0.5), "black", segs=8, rot=(-90, 0, 0)))
    Q.append(box(C, (5.0, 2.3, 1.4), (4.6, 0, 0.9), "red"))
    Q.append(mound(C, 2.2, 1.0, 0.8, (4.6, 0, 2.3), "corn", seed=2, subdiv=1))
    for k in (3.6, 5.6):
        for s in (-1, 1):
            Q.append(cyl(C, 0.45, 0.3, (k, s * 1.15, 0.45), "black", segs=8, rot=(-s * 90, 0, 0)))
    return place_at(Q, x, y, 0)


# ------------------------------------------------------------------ Offshore-Windrad
def offshore_wind(C):
    """Offshore-Windrad (Monopile, Nabe 72 m). Wasserspiegel z = 0, Pfahl bis -20 m.
    Objekte: OffshoreWind (Fundament, Turm, Gondel) + Rotor (Ursprung = Nabe, dreht um Unity-Z)."""
    P = [cyl(C, 3.2, 22.0, (0, 0, -20.0), "metal_dark", segs=16),
         cyl(C, 3.4, 14.0, (0, 0, -2.0), "yellow", segs=16),
         cyl(C, 5.5, 0.05, (0, 0, -0.02), "foam", segs=16)]
    # Plattform mit Gelaender, Anlegestelle, Kran
    P.append(cyl(C, 7.0, 0.5, (0, 0, 12.0), "metal", segs=16))
    for k in range(24):
        a = 2 * math.pi * k / 24
        P.append(box(C, (0.08, 0.08, 1.1), (6.9 * math.cos(a), 6.9 * math.sin(a), 12.5), "yellow"))
    P.append(lathe(C, [(6.9, 13.55), (7.0, 13.55), (7.0, 13.65), (6.9, 13.65)], (0, 0, 0), "yellow", segs=24))
    for s in (-1, 1):
        P.append(cyl(C, 0.25, 13.0, (s * 1.4, -3.5, -1.0), "yellow", segs=6))
    for k in range(12):
        P.append(box(C, (2.8, 0.1, 0.08), (0, -3.6, -0.5 + k * 1.05), "yellow"))
    P.append(box(C, (2.2, 2.2, 2.4), (3.6, 3.6, 12.5), "white"))
    P.append(cyl(C, 0.2, 4.0, (-4.5, 3.0, 12.5), "yellow", segs=6))
    P.append(beam(C, (-4.5, 3.0, 16.3), (-4.5, -1.5, 15.0), 0.2, "yellow"))
    # Turm
    H = 68.0
    P.append(lathe(C, [(0, 12.5), (2.6, 12.5), (2.4, 30.0), (1.8, H), (0, H)], (0, 0, 0), "white", segs=16))
    ZH = H + 2.0
    P.append(cyl(C, 1.85, 0.6, (0, 0, H - 0.1), "offwhite", segs=12))
    P.append(box(C, (4.0, 11.0, 4.0), (0, 3.0, ZH - 2.0), "white", bevel=0.35))
    P.append(box(C, (4.06, 6.0, 0.6), (0, 4.0, ZH - 0.6), "blue", center=True))
    P.append(box(C, (3.0, 3.0, 0.3), (0, 6.5, ZH + 2.0), "metal"))                    # Hubschrauber-Plattform
    P.append(sphere(C, 0.3, (-1.2, 7.5, ZH + 2.4), "red", subdiv=1))
    HUB = Vector((0, -3.0, ZH))
    R = [lathe(C, [(0, 0), (1.8, 0), (1.8, 0.8), (1.3, 2.6), (0.5, 3.6), (0, 3.8)], (HUB.x, HUB.y + 1.2, HUB.z),
               "white", segs=12, rot=(90, 0, 0), name="spinner")]
    blade = [(1.4, -0.9), (5.0, -1.6), (16.0, -0.9), (29.0, -0.2), (29.3, 0.12), (16.0, 0.55), (5.0, 1.15),
             (1.4, 0.9)]
    for k in range(3):
        b = prism(C, blade, 0.5, (0, 0, -0.25), "white", name="blade")
        b.data.transform(Matrix.Translation(HUB) @ Matrix.Rotation(math.radians(120 * k), 4, 'Y')
                         @ Matrix.Rotation(math.radians(-90), 4, 'Y') @ Matrix.Rotation(math.radians(90), 4, 'X'))
        R.append(b)
        tip = box(C, (0.6, 0.56, 2.4), (0, 0, 25.5), "red", center=True, name="tip")
        tip.data.transform(Matrix.Translation(HUB) @ Matrix.Rotation(math.radians(120 * k), 4, 'Y')
                           @ Matrix.Translation((0.1, 0, 0)))
        R.append(tip)
    return {"OffshoreWind": (P, (0, 0, 0)), "Rotor": (R, tuple(HUB))}


# ------------------------------------------------------------------ Umspannwerk
def transformer(C, x, y, rot=0):
    Q = [box(C, (3.4, 2.4, 0.4), (0, 0, 0), "concrete"),
         box(C, (3.0, 2.0, 2.6), (0, 0, 0.4), "metal_dark")]
    for s in (-1, 1):
        for k in range(5):
            Q.append(box(C, (0.08, 0.7, 2.0), (-1.2 + k * 0.6, s * 1.35, 0.7), "metal"))
    for k in range(3):
        Q.append(cyl(C, 0.13, 1.6, (-0.9 + k * 0.9, -0.5, 3.0), "white", segs=6))
        Q.append(cyl(C, 0.08, 1.2, (-0.9 + k * 0.9, 0.6, 3.0), "white", segs=6))
    Q.append(cyl(C, 0.45, 2.0, (0, 0.6, 3.0), "metal", segs=10, rot=(0, 90, 0)))
    Q[-1].data.transform(Matrix.Translation((-1.0, 0, 0.4)))
    return place_at(Q, x, y, rot)


def substation(C):
    """Umspannwerk (UtilityCatalog "power_substation", 24 x 20 m, Strasse vorne): Kiesflaeche, 2 Trafos,
    Portale mit Sammelschienen, Schaltgeraete, Betriebsgebaeude, Zaun mit Tor."""
    P = [box(C, (24.0, 20.0, 0.1), (0, 0, 0), "gravel"),
         box(C, (3.6, 4.0, 0.12), (7.5, -8.0, 0), "asphalt")]
    for x in (-7.0, -1.0):
        P += transformer(C, x, -4.5)
    # Portale (Stahlrahmen) mit Leitungen
    for y in (2.5, 7.5):
        for x in (-10.5, 0.0, 10.5):
            P.append(box(C, (0.45, 0.45, 8.5), (x, y, 0.1), "metal"))
        P.append(box(C, (21.5, 0.45, 0.55), (0, y, 8.5), "metal"))
        for x in (-7.5, 3.0):
            for k in range(3):
                P.append(cyl(C, 0.1, 1.0, (x + k * 1.5, y, 7.5), "white", segs=6))
    for k in range(3):                                         # Sammelschienen
        P.append(pipe(C, (-10.5, 4.0 + k * 1.0, 6.2), (10.5, 4.0 + k * 1.0, 6.2), 0.08, "metal_dark", segs=6))
    for x in (-8.0, -4.0, 0.0, 4.0, 8.0):                       # Schalter/Wandler auf Stuetzen
        for k in range(3):
            y = 4.0 + k * 1.0
            P.append(box(C, (0.4, 0.4, 2.4), (x, y, 0.1), "concrete_dark"))
            P.append(cyl(C, 0.15, 1.5, (x, y, 2.5), "white", segs=6))
            P.append(beam(C, (x, y, 4.0), (x, y, 6.2), 0.06, "metal_dark"))
    # Betriebsgebaeude
    bx, by = 7.0, -3.5
    P.append(box(C, (6.5, 4.5, 3.4), (bx, by, 0.1), "plaster"))
    P.append(box(C, (6.9, 4.9, 0.3), (bx, by, 3.5), "concrete_dark"))
    P += window_row(C, (bx - 2.2, by - 2.25, 2.0), (1, 0, 0), 2, 2.0, (1.2, 1.0), "glass", normal=(0, -1, 0))
    P += door(C, bx + 2.0, by - 2.25, 1.0, 2.2, "metal_dark", 0.1)
    P += sign(C, bx - 1.2, by - 2.25, 2.85, 2.6, 0.45, "yellow", "black")
    # Zaun mit Tor (vorne bei x = 5,7 .. 9,3)
    P += fence(C, [(9.3, -9.8), (11.8, -9.8), (11.8, 9.8), (-11.8, 9.8), (-11.8, -9.8), (5.7, -9.8)], 2.2, 3.0,
               closed=False)
    for x in (5.7, 9.3):
        P.append(box(C, (0.3, 0.3, 2.4), (x, -9.8, 0.1), "metal_dark"))
    P.append(box(C, (0.6, 0.06, 0.4), (4.0, -9.85, 1.4), "yellow"))                         # Warnschild
    return P


# ------------------------------------------------------------------ Batteriespeicher
def battery(C):
    """Batteriespeicher (32 x 24 m): 2 Reihen Batteriecontainer mit Klimageraeten, Wechselrichter, Trafo, Zaun."""
    P = [box(C, (32.0, 24.0, 0.1), (0, 0, 0), "gravel")]
    for row, y in enumerate((-4.5, 4.5)):
        for k in range(4):
            x = -11.0 + k * 6.4
            P.append(box(C, (0.8, 2.8, 0.3), (x - 2.0, y, 0.1), "concrete"))
            P.append(box(C, (0.8, 2.8, 0.3), (x + 2.0, y, 0.1), "concrete"))
            P.append(box(C, (6.0, 2.44, 2.6), (x, y, 0.4), "white", bevel=0.04))
            for j in range(4):
                P.append(box(C, (0.9, 0.06, 2.1), (x - 2.1 + j * 1.4, y - 1.24, 0.6), "offwhite"))
            P.append(box(C, (1.2, 1.0, 0.7), (x + 1.8, y, 3.0), "metal"))
            P.append(cyl(C, 0.35, 0.06, (x + 1.8, y, 3.7), "metal_dark", segs=8))
            P.append(box(C, (1.0, 0.07, 0.25), (x - 2.0, y - 1.25, 2.6), "green"))
    for x in (-6.0, 6.0):
        P.append(box(C, (2.6, 2.0, 2.4), (x, 0.0, 0.1), "metal_dark"))
        P.append(box(C, (2.0, 0.06, 0.4), (x, -1.03, 1.8), "yellow"))
    P += transformer(C, 13.5, 0.0, 90)
    P += polyline_pipe(C, [(-6.0, 1.0, 2.2), (-6.0, 1.0, 2.6), (6.0, 1.0, 2.6), (6.0, 1.0, 2.2)], r=0.15,
                       color="black", segs=6)
    P += fence(C, [(-15.8, -11.8), (15.8, -11.8), (15.8, 11.8), (-15.8, 11.8)], 2.2, 3.5, gaps=(0,))
    P += fence(C, [(-15.8, -11.8), (-2.0, -11.8)], 2.2, 3.5, closed=False)
    P += fence(C, [(2.0, -11.8), (15.8, -11.8)], 2.2, 3.5, closed=False)
    return P


# Substation liegt unter Models/Utilities: OwnModelCatalogBuilder ordnet "Substation" -> power_substation zu
EN = {"HydroDam": hydro_dam, "BiogasPlant": biogas, "OffshoreWind": offshore_wind,
      "Substation": (substation, "Utilities"), "BatteryStorage": battery}
run_family(EN, "Energy", views=V)
