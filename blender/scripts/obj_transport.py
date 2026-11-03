"""OePNV-Bauwerke (Models/Transport): Busdepot, Taxizentrale, Bahnhof, U-Bahn-Eingang."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def city_bus(C, x, y, rot=0, color="yellow"):
    P = [box(C, (2.5, 12.0, 2.6), (0, 0, 0.45), color, bevel=0.15),
         box(C, (2.54, 10.6, 1.1), (0, 0.3, 1.55), "glass_dark"),
         box(C, (2.3, 0.08, 1.4), (0, -6.02, 1.4), "glass_dark"),
         box(C, (2.0, 3.0, 0.35), (0, 2.5, 3.05), "metal"),
         box(C, (1.2, 0.1, 0.3), (0, -6.04, 2.75), "black")]
    for wy in (-4.0, 3.6):
        for wx in (-1.2, 0.95):
            P.append(cyl(C, 0.5, 0.25, (wx, wy, 0.5), "black", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def taxi(C, x, y, rot=0):
    P = parked_car(C, 0, 0, 0, "yellow")
    P.append(box(C, (0.6, 0.3, 0.2), (0, 0.2, 1.65), "white"))
    return place_at(P, x, y, rot)


def rails(C, x0, x1, y, z):
    """Gleis entlang X: Schotterbett, Schwellen, zwei Schienen."""
    L = x1 - x0
    P = [box(C, (L, 3.0, 0.3), ((x0 + x1) / 2, y, z), "gravel")]
    n = int(L / 0.9)
    for k in range(n):
        P.append(box(C, (0.25, 2.5, 0.12), (x0 + 0.45 + k * 0.9, y, z + 0.3), "wood_dark"))
    for s in (-0.72, 0.72):
        P.append(box(C, (L, 0.1, 0.15), ((x0 + x1) / 2, y + s, z + 0.42), "metal"))
    return P


def bus_depot(C):
    """Busdepot (Vorschlag 32 x 24 m): Halle mit vier Toren, Tankstelle, Busse."""
    P = [box(C, (32, 24, Z0), (0, 0, 0), "asphalt")]
    hx, hy, hw, hd = -3.0, 4.5, 24.0, 14.0
    S = [box(C, (hw, hd, 7.0), (hx, hy, 0), "pastel_yellow"),
         box(C, (hw + 0.1, hd + 0.1, 1.0), (hx, hy, 6.0), "red")]
    S += flat_roof(C, hw, hd, 7.0, (hx, hy), ph=0.5)
    for i in range(4):
        x = hx - 8.4 + i * 5.6
        S.append(box(C, (4.2, 0.15, 4.8), (x, hy - hd / 2 - 0.07, 0), "metal_dark"))
        for k in range(8):
            S.append(box(C, (4.2, 0.2, 0.06), (x, hy - hd / 2 - 0.1, 0.4 + k * 0.55), "metal"))
    for i in range(6):
        S.append(box(C, (2.4, 1.6, 0.4), (hx - 9.0 + i * 3.6, hy, 7.0), "glass_light"))
    S += sign(C, hx, hy - hd / 2 - 0.1, 5.3, 8.0, 0.7, "red", "white")
    # Verwaltung rechts
    p, top = block(C, 13.0, 6.0, 5.0, 11.0, 2, 3.2, wall="white", z0=0.0, spacing=2.4, win_size=(1.2, 1.3),
                   sides="flr", skip=lambda s, r, c: s == "f" and r == 0 and c == 0)
    S += p
    S += flat_roof(C, 5.0, 11.0, top, (13.0, 6.0), ph=0.4)
    S += door(C, 12.0, 0.5, 1.2, 2.3, "glass_dark", z=0.0, canopy="red")
    # Tankstelle vorne rechts
    S.append(box(C, (8.0, 5.0, 0.3), (10.0, -8.0, 4.6), "white"))
    S.append(box(C, (8.1, 5.1, 0.5), (10.0, -8.0, 4.4), "red"))
    for x in (7.0, 13.0):
        S.append(box(C, (0.4, 0.4, 4.4), (x, -8.0, 0), "metal"))
        S.append(box(C, (0.8, 0.5, 1.6), (x, -8.0 + 1.0, 0), "white"))
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    for k in range(5):   # Markierungen
        P.append(box(C, (0.15, 13.0, 0.05), (-14.0 + k * 3.6, -5.0, Z0), "white"))
    P += city_bus(C, -12.2, -4.8, 0, "yellow")
    P += city_bus(C, -8.6, -4.8, 0, "yellow")
    P += city_bus(C, -1.4, -5.6, 0, "red")
    P += city_bus(C, 10.0, -6.0, 0, "yellow")
    return P


def taxi_depot(C):
    """Taxizentrale (Vorschlag 16 x 16 m): Funkzentrale, Stellplaetze mit Ueberdachung, Taxis."""
    P = [box(C, (16, 16, Z0), (0, 0, 0), "asphalt")]
    S = []
    p, top = block(C, -3.0, 4.5, 9.0, 6.0, 1, 3.4, wall="offwhite", z0=0.3, spacing=2.4, win_size=(1.5, 1.4),
                   skip=lambda s, r, c: s == "f" and c == 1)
    S += p
    S.append(box(C, (9.1, 6.1, 0.6), (-3.0, 4.5, 3.1), "yellow"))
    S += flat_roof(C, 9.0, 6.0, top, (-3.0, 4.5), ph=0.3)
    S += door(C, -3.0 + (-1.0 * 2.4 + 2.4) * 0, 1.5, 1.2, 2.3, "glass_dark", z=0.3, canopy="yellow")
    S += lattice_mast(C, -6.0, 6.0, 8.0, 0.4, 0.2, top, colors=("metal",), t=0.08)
    S.append(box(C, (2.6, 0.3, 1.0), (-3.0, 1.35, top + 0.1), "black"))
    S.append(box(C, (2.0, 0.32, 0.4), (-3.0, 1.33, top + 0.45), "yellow"))
    # Carport fuer 4 Taxis
    S.append(box(C, (12.0, 5.6, 0.25), (1.0, -4.5, 3.0), "metal"))
    for x in (-4.8, 1.0, 6.8):
        S.append(box(C, (0.25, 0.25, 3.0), (x, -2.0, 0), "metal_dark"))
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    for k in range(5):
        P.append(box(C, (0.12, 5.0, 0.05), (-4.6 + k * 2.9, -4.5, Z0), "white"))
    for k in range(3):
        P += taxi(C, -3.1 + k * 2.9, -4.5, 180)
    P += taxi(C, 5.0, 4.2, 90)
    return P


def train_station(C):
    """Bahnhof (Vorschlag 40 x 24 m): Empfangsgebaeude mit Uhrturm, zwei Bahnsteige, Gleise hinten."""
    P = [box(C, (40, 24, Z0), (0, 0, 0), "concrete")]
    S = []
    # Empfangsgebaeude
    cx, cy, w, d = 0, -6.5, 22.0, 9.0
    p, top = block(C, cx, cy, w, d, 2, 4.0, wall="brick", z0=0.35, spacing=2.8, win_size=(1.4, 2.4), frame="cream",
                   skip=lambda s, r, c: s == "f" and r == 0 and c in (3, 4))
    S += p
    S.append(box(C, (w + 0.15, d + 0.15, 0.4), (cx, cy, 4.15), "cream"))
    S += gable(C, w, d, 3.2, (cx, cy, top), "roof_dark", overhang=0.4, gable_color="brick")
    S.append(box(C, (6.0, 0.2, 3.4), (cx, cy - d / 2 - 0.08, 0.35), "glass_dark"))
    S.append(box(C, (8.0, 2.4, 0.3), (cx, cy - d / 2 - 1.2, 3.8), "metal_dark"))
    S += sign(C, cx, cy - d / 2 - 0.1, 4.6, 7.0, 0.8, "steel_blue", "white")
    # Uhrturm
    tx = cx + w / 2 - 2.0
    S.append(box(C, (4.0, 4.0, 15.0), (tx, cy - 1.5, 0), "brick"))
    S.append(box(C, (4.3, 4.3, 0.4), (tx, cy - 1.5, 15.0), "cream"))
    S.append(hip(C, 4.3, 4.3, 3.2, (tx, cy - 1.5, 15.4), "roof_green", overhang=0.2))
    for (nx, ny) in ((0, -1), (1, 0)):
        S.append(cyl(C, 1.1, 0.12, (tx + nx * 2.02, cy - 1.5 + ny * 2.02, 12.6), "white", segs=16,
                     rot=(90 * abs(ny), 90 * abs(nx), 0)))
    # Bahnsteige + Daecher
    for py in (1.5, 9.0):
        S.append(box(C, (36.0, 3.6, 0.9), (0, py, 0), "concrete"))
        S.append(box(C, (36.0, 0.3, 0.05), (0, py - 1.6, 0.9), "white"))
        S.append(box(C, (24.0, 4.4, 0.25), (0, py, 4.2), "metal"))
        for x in range(-10, 11, 5):
            S.append(box(C, (0.25, 0.25, 3.3), (x, py, 0.9), "metal_dark"))
        S += bench(C, -6.0, py + 0.6, 180)
        S.append(box(C, (1.6, 0.12, 0.6), (4.0, py, 3.2), "steel_blue"))   # Zugzielanzeiger
    S += rails(C, -20.0, 20.0, 5.25, 0.0)          # Gleis zwischen den Bahnsteigen
    for o in S:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += S
    # Fahrradstaender + Taxi vorne
    for k in range(4):
        P.append(lathe(C, [(0.36, 0), (0.42, 0), (0.42, 0.05), (0.36, 0.05)], (-14.0 + k * 0.7, -11.0, Z0 + 0.42),
                       "metal", segs=8, rot=(90, 0, 0)))
    P += taxi(C, 9.0, -10.5, 90)
    P += lamp(C, -11.0, -11.3)
    P += lamp(C, 11.0, -11.3)
    return P


def metro_entrance(C):
    """U-Bahn-Eingang (6 x 4 m): Treppenabgang mit Glasdach und U-Schild."""
    P = [box(C, (6.0, 4.0, 0.15), (0, 0, 0), "concrete")]
    # Treppe angedeutet: Stufen werden nach hinten dunkler (das Gelaende darunter ist nicht ausgehoben)
    shades = ["concrete", "concrete", "concrete_dark", "concrete_dark", "slate", "asphalt", "black"]
    for k, c in enumerate(shades):
        P.append(box(C, (2.6, 0.46, 0.05), (0, -1.2 + k * 0.46, 0.15), c))
    for s in (-1, 1):
        P.append(box(C, (0.3, 3.4, 1.1), (s * 1.45, 0.2, 0.15), "concrete"))
        P.append(beam(C, (s * 1.45, -1.5, 1.2), (s * 1.45, 1.9, 1.2), 0.08, "metal"))
    P.append(box(C, (0.3, 0.3, 1.1), (0, 1.75, 0.15), "concrete"))
    P.append(box(C, (3.4, 3.8, 0.12), (0, 0.2, 2.6), "glass_light", rot=(-6, 0, 0)))
    for (x, y) in ((-1.6, -1.6), (1.6, -1.6), (-1.6, 1.9), (1.6, 1.9)):
        P.append(box(C, (0.12, 0.12, 2.6 if y < 0 else 2.2), (x, y, 0.15), "metal_dark"))
    # U-Schild auf Mast
    P.append(box(C, (0.1, 0.1, 3.4), (2.4, -1.6, 0.15), "metal_dark"))
    P.append(box(C, (0.9, 0.12, 0.9), (2.4, -1.6, 3.4), "blue"))
    P.append(box(C, (0.14, 0.14, 0.55), (2.25, -1.68, 3.55), "white"))
    P.append(box(C, (0.14, 0.14, 0.55), (2.55, -1.68, 3.55), "white"))
    P.append(box(C, (0.44, 0.14, 0.14), (2.4, -1.68, 3.5), "white"))
    return P


run_family({"BusDepot": bus_depot, "TaxiDepot": taxi_depot, "TrainStation": train_station,
            "MetroEntrance": metro_entrance}, "Transport")
