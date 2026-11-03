"""Industrie: Werkstatt, Lager, Fabrik, Tanklager. Masse = Grundstueck x 92 % (lot()), Strasse = -Y."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z = PLINTH


def roller_door(C, x, y, w=4.0, h=4.2, color="metal_dark", z=Z):
    P = [box(C, (w, 0.12, h), (x, y - 0.05, z), color)]
    for k in range(int(h / 0.5)):
        P.append(box(C, (w, 0.16, 0.06), (x, y - 0.07, z + 0.3 + k * 0.5), "metal"))
    P.append(box(C, (w + 0.4, 0.3, 0.3), (x, y - 0.15, z + h), "concrete_dark"))
    return P


def workshop_i1(C):
    """2x2: Werkstatthalle mit Satteldach (Blech), Buero-Anbau, Hof mit Paletten."""
    S = lot(2)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "concrete")]
    cx, cy, w, d = -1.4, 2.0, 10.0, 10.0
    P.append(box(C, (w + 0.1, d + 0.1, Z), (cx, cy, 0), "concrete_dark"))
    P.append(box(C, (w, d, 5.0), (cx, cy, Z), "steel_blue"))
    for k in range(11):
        P.append(box(C, (0.12, 0.12, 5.0), (cx - w / 2 + 0.5 + k * 0.9, cy - d / 2 - 0.05, Z), "metal_dark"))
    P += gable(C, w, d, 2.2, (cx, cy, Z + 5.0), "roof_light", overhang=0.4, gable_color="steel_blue")
    P += roller_door(C, cx - 1.5, cy - d / 2, 4.2, 4.0)
    P += ribbon(C, cx, cy, w, d, Z + 4.0, 0.8, "glass_light", "lr")
    # Buero-Anbau
    P.append(box(C, (3.6, 5.0, 3.2), (5.6, 0.0, Z), "offwhite"))
    P += flat_roof(C, 3.6, 5.0, Z + 3.2, (5.6, 0.0), ph=0.3, t=0.2)
    P += door(C, 5.6, -2.5, 1.0, 2.2, "glass_dark", canopy="steel_blue")
    P += window_row(C, (5.6, -2.5, Z + 1.6), (1, 0, 0), 1, 1, (0.9, 1.0), normal=(0, -1, 0)) if False else []
    P += sign(C, cx, cy - d / 2 - 0.1, Z + 5.4, 5.0, 0.8, "yellow", "black")
    P += crates(C, 4.6, 4.0, 4, seed=3)
    P.append(box(C, (1.2, 2.4, 1.6), (-6.2, -5.2, 0), "metal_dark", bevel=0.05))   # Container/Mulde
    P += parked_car(C, 4.4, -5.0, 90, "white")
    return P


def warehouse_i2(C):
    """3x3: Logistikhalle mit drei Laderampen und Sattelzug."""
    S = lot(3)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "asphalt")]
    cx, cy, w, d = 0, 3.4, 20.0, 14.0
    P.append(box(C, (w + 0.1, d + 0.1, Z), (cx, cy, 0), "concrete_dark"))
    P.append(box(C, (w, d, 7.5), (cx, cy, Z), "offwhite"))
    P.append(box(C, (w + 0.05, d + 0.05, 1.2), (cx, cy, Z + 6.3), "orange"))
    P += flat_roof(C, w, d, Z + 7.5, (cx, cy), ph=0.4)
    for i in range(6):
        P.append(box(C, (2.0, 1.4, 0.4), (cx - 7.0 + i * 3.2, cy + 0.0, Z + 7.5), "glass_light"))   # Lichtkuppeln
    P.append(box(C, (13.0, 2.0, 1.2), (cx - 2.5, cy - d / 2 - 1.0, 0), "concrete"))               # Rampe
    for i in range(3):
        x = cx - 6.5 + i * 4.0
        P += roller_door(C, x, cy - d / 2, 3.0, 3.4, "metal_dark", z=1.2)
        P.append(box(C, (3.6, 0.6, 0.4), (x, cy - d / 2 - 0.3, 4.7), "black"))   # Torabdichtung
    P.append(box(C, (14.0, 2.4, 0.25), (cx - 2.5, cy - d / 2 - 1.2, 4.9), "metal"))
    P += sign(C, 6.5, cy - d / 2 - 0.1, Z + 4.5, 5.0, 0.9, "orange", "white")
    P += door(C, 7.5, cy - d / 2, 1.0, 2.2, "metal_dark", canopy="metal")
    P += truck(C, -3.6, -8.8, 90, "red", "white")       # quer vor der Rampe (passt ins Grundstueck)
    P += container(C, 7.4, -8.8, 0, 90, "blue")
    P += container(C, 7.4, -8.8, 2.6, 90, "green_dark")
    return P


def factory_i3(C):
    """4x4: Fabrik mit Sheddach, Schornstein, Silos und Verwaltung."""
    S = lot(4)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "concrete")]
    hx, hy, hw, hd = -2.0, 3.0, 24.0, 18.0
    P.append(box(C, (hw + 0.1, hd + 0.1, Z), (hx, hy, 0), "concrete_dark"))
    P.append(box(C, (hw, hd, 6.5), (hx, hy, Z), "brick"))
    P += ribbon(C, hx, hy, hw, hd, Z + 4.4, 1.6, "glass", "lr")
    P += sawtooth(C, hx, hy, hw, hd, Z + 6.5, n=5, h=2.6)
    for x in (hx - 6.0, hx + 2.0):
        P += roller_door(C, x, hy - hd / 2, 4.6, 5.0)
    # Verwaltung vorne rechts
    p, top = block(C, 10.5, -8.0, 7.0, 7.0, 3, 3.2, wall="plaster", z0=Z, spacing=2.2, win_size=(1.2, 1.4),
                   sides="flr", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    P += p
    P += flat_roof(C, 7.0, 7.0, top, (10.5, -8.0), ph=0.5)
    P += door(C, 10.5, -11.5, 1.6, 2.4, "glass_dark", canopy="brick")
    P += sign(C, 10.5, -11.6, top - 1.2, 5.0, 0.8, "steel_blue", "white")
    # Schornstein
    P.append(lathe(C, [(0, 0), (1.4, 0), (1.1, 26), (1.2, 26.2), (1.2, 27), (0, 27)], (8.5, 10.5, 0), segs=10,
                   colors=["brick_dark", "brick_dark", "brick_dark", "metal_dark", "black"]))
    # Silos
    for k in range(3):
        x = 12.0 - 0.0
        y = 5.5 - k * 4.6 + 4.6
        P += tank(C, 13.0, y, 1.9, 9.0, "metal", "orange")
    P += polyline_pipe(C, [(11.1, 10.1, 7.0), (hx + hw / 2 + 0.1, 10.1, 7.0)], 0.3, "metal_dark")
    # Hof: Container, Paletten
    P += container(C, -10.0, -9.5, 0, 90, "red")
    P += crates(C, -4.0, -10.5, 4, seed=5)
    P += truck(C, 2.5, -9.5, 90, "white", "teal")
    return P


def tank_farm_i4(C):
    """4x3: Tanklager mit Prozessturm, Rohrbruecke und Leitstand."""
    W, D = lot(4), lot(3)
    P = [box(C, (W - 0.2, D - 0.2, 0.05), (0, 0, 0), "gravel")]
    for (x, y) in ((-9.0, 4.0), (-1.0, 4.0), (-9.0, -4.0)):
        P.append(box(C, (7.6, 7.6, 0.6), (x, y, 0), "concrete_dark"))     # Auffangwanne
        P += tank(C, x, y, 3.2, 7.5, "white", "green_dark")
    # Prozessturm (Stahlgeruest mit Kolonnen)
    tx, ty = 7.0, 4.0
    for (dx, dy) in ((-2, -2), (2, -2), (2, 2), (-2, 2)):
        P.append(box(C, (0.3, 0.3, 16.0), (tx + dx, ty + dy, 0), "steel_blue"))
    for z in (4.0, 8.0, 12.0, 16.0):
        P.append(box(C, (4.6, 4.6, 0.2), (tx, ty, z), "metal_dark"))
    P.append(cyl(C, 1.0, 22.0, (tx - 0.8, ty, 0), "metal", segs=10))
    P.append(cyl(C, 0.7, 18.0, (tx + 1.0, ty + 0.8, 0), "white", segs=10))
    P.append(sphere(C, 1.0, (tx - 0.8, ty, 22.0), "metal", subdiv=1, scale=(1, 1, 0.5)))
    P.append(cyl(C, 0.15, 4.0, (tx + 1.0, ty + 0.8, 18.0), "metal_dark", segs=6))
    P.append(sphere(C, 0.35, (tx + 1.0, ty + 0.8, 22.2), "orange", subdiv=1))   # Fackel
    # Rohrbruecke
    for x in (-5.0, -1.0, 3.0):
        P.append(box(C, (0.3, 0.3, 4.0), (x, -0.2, 0), "metal_dark"))
        P.append(box(C, (0.3, 2.0, 0.3), (x, -0.2, 4.0), "metal_dark"))
    for dy, col in ((-0.6, "yellow"), (0.0, "metal"), (0.6, "pipe_green")):
        P.append(pipe(C, (-9.0, -0.2 + dy, 4.5), (tx - 2.0, -0.2 + dy, 4.5), 0.22, col, segs=6))
    # Leitstand vorne rechts
    P.append(box(C, (6.0, 4.0, 3.2), (8.0, -6.0, 0), "offwhite"))
    P += flat_roof(C, 6.0, 4.0, 3.2, (8.0, -6.0), ph=0.3, t=0.2)
    P += window_row(C, (8.0, -8.0, 1.8), (1, 0, 0), 3, 1.8, (1.4, 1.2), normal=(0, -1, 0))
    P.append(box(C, (1.0, 0.1, 2.2), (5.6, -8.05, 0), "metal_dark"))
    P += fence(C, [(-W / 2 + 0.3, -D / 2 + 0.3), (2.0, -D / 2 + 0.3)], h=2.0, spacing=2.4, closed=False)
    return P


def log_pile(C, x, y, n=6, L=6.0, rot=0):
    P = []
    for i in range(n):
        row = 0 if i < 3 else 1
        k = i if i < 3 else i - 3
        P.append(cyl(C, 0.35, L, (-L / 2, -0.75 + k * 0.72 + row * 0.36, 0.35 + row * 0.62), "wood", segs=7,
                     rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def sawmill_i5(C):
    """2x2: Saegewerk mit offener Halle, Stammholz, Brettstapeln und Kran."""
    S = lot(2)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "gravel")]
    # Offene Halle (Pultdach auf Stuetzen)
    for x in (-6.0, -2.0, 2.0):
        for y in (-1.0, 5.5):
            P.append(box(C, (0.3, 0.3, 5.0 if y > 0 else 4.0), (x, y, 0), "wood_dark"))
    roof = box(C, (9.0, 7.6, 0.25), (-2.0, 2.25, 4.62), "roof_light", rot=(8, 0, 0), center=True)  # hinten hoeher
    P.append(roof)
    P.append(box(C, (6.0, 1.4, 1.0), (-2.0, 2.2, 0), "metal_dark"))     # Saegestrasse
    P.append(cyl(C, 0.8, 0.1, (-2.0, 2.2, 1.0), "metal", segs=12, rot=(0, 90, 0)))
    P.append(box(C, (9.0, 0.2, 2.0), (-2.0, 5.6, 0), "wood"))
    # Stammholz und Bretter
    P += log_pile(C, 3.5, -4.5, 6, 6.0, 0)
    P += log_pile(C, -3.5, -4.8, 6, 5.0, 0)
    for k in range(3):
        for j in range(5):
            P.append(box(C, (2.4, 1.2, 0.12), (5.0, 1.5 + k * 1.6, 0.15 + j * 0.16), "sand"))
        P.append(box(C, (2.4, 1.2, 0.15), (5.0, 1.5 + k * 1.6, 0), "wood_dark"))
    # Buero-Container + Saegemehlsilo
    P.append(box(C, (5.0, 2.4, 2.6), (3.6, 6.0, 0), "green_dark"))
    P.append(cyl(C, 1.3, 5.0, (-6.0, -5.6, 0.0), "metal", segs=10))
    P.append(cyl(C, 1.3, 1.0, (-6.0, -5.6, 5.0), "metal", segs=10, r_top=0.2))
    return P


def brewery_i6(C):
    """3x3: Brauerei mit Sudhaus (Kupferkessel hinter Glas), Gaertanks, Malzsilos und Schornstein."""
    S = lot(3)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "concrete")]
    # Sudhaus (Ziegel, hohe Glasfront)
    cx, cy, w, d = -4.0, -2.5, 12.0, 9.0
    P.append(box(C, (w, d, 9.0), (cx, cy, 0), "brick"))
    P.append(box(C, (8.0, 0.1, 6.0), (cx, cy - d / 2 - 0.05, 1.0), "glass_light"))
    for x in (cx - 2.2, cx + 2.2):
        P.append(cyl(C, 1.4, 2.6, (x, cy - d / 2 + 2.0, 1.0), "orange", segs=12))
        P.append(lathe(C, [(1.4, 0), (0.3, 1.2), (0.25, 2.5), (0, 2.5)], (x, cy - d / 2 + 2.0, 3.6), "orange", segs=12))
    P += gable(C, w, d, 3.0, (cx, cy, 9.0), "roof_red", overhang=0.4, gable_color="brick")
    P += sign(C, cx, cy - d / 2 - 0.1, 7.6, 6.0, 0.7, "gold", "brick_dark")
    # Gaertanks (stehend, Edelstahl)
    for k in range(4):
        P += tank(C, 3.0 + (k % 2) * 3.4, -6.0 + (k // 2) * 3.4, 1.4, 7.0, "metal", "metal_dark")
    # Malzsilos hinten
    for k in range(3):
        P.append(cyl(C, 1.8, 11.0, (-8.0 + k * 3.8, 7.0, 0), "offwhite", segs=12))
        P.append(cyl(C, 1.8, 0.8, (-8.0 + k * 3.8, 7.0, 11.0), "offwhite", segs=12, r_top=0.3))
    P.append(box(C, (12.0, 1.2, 1.2), (-4.2, 7.0, 11.4), "metal_dark"))
    # Schornstein (Ziegel)
    P.append(lathe(C, [(0, 0), (1.0, 0), (0.8, 18), (0.95, 18.2), (0.95, 18.8), (0, 18.8)], (6.5, 7.0, 0),
                   segs=8, colors=["brick_dark", "brick_dark", "brick_dark", "brick_dark", "black"]))
    # Fasslager / Lkw
    for k in range(6):
        P.append(cyl(C, 0.45, 0.9, (7.6 + (k % 3) * 1.0, 1.0 + (k // 3) * 1.0, 0), "wood", segs=8))
    P += truck(C, 0.0, -9.6, 90, "gold", "white")
    return P


def logistics_i7(C):
    """4x4: Container-Umschlagplatz mit Portalkran, Containerstapeln und Lkw."""
    S = lot(4)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "asphalt")]
    cols = ("red", "blue", "green_dark", "orange", "steel_blue", "yellow", "white", "teal")
    k = 0
    for gx in range(3):
        for gy in range(2):
            h = 1 + (gx + gy) % 3
            for z in range(h):
                P += container(C, -10.0 + gx * 3.0, -4.0 + gy * 7.0, z * 2.6, 0, cols[k % len(cols)])
                k += 1
    for gy in range(2):
        for z in range(2):
            P += container(C, 9.0, -4.0 + gy * 7.0, z * 2.6, 0, cols[(k + gy + z) % len(cols)])
    # Portalkran ueber den Stapeln
    for x in (-13.0, 4.0):
        for y in (-8.5, 12.0):
            P.append(box(C, (0.8, 0.8, 12.0), (x, y, 0), "yellow"))
        P.append(box(C, (0.8, 21.3, 1.2), (x, 1.75, 12.0), "yellow"))
    P.append(box(C, (18.0, 1.2, 1.4), (-4.5, 3.0, 11.9), "yellow"))
    P.append(box(C, (2.0, 2.4, 1.6), (-6.0, 3.0, 10.4), "metal_dark"))
    P.append(beam(C, (-6.0, 3.0, 10.4), (-6.0, 3.0, 8.4), 0.1, "black"))
    # Abfertigung + Lkw
    P.append(box(C, (5.0, 3.0, 3.0), (10.5, -11.5, 0), "offwhite"))
    P += flat_roof(C, 5.0, 3.0, 3.0, (10.5, -11.5), ph=0.2, t=0.15)
    P += truck(C, -2.0, -12.0, 90, "blue", "red")
    return P


def recycling_i8(C):
    """2x2: Wertstoffhof mit Containern (Glas, Papier, Metall), Halle und Schrottberg."""
    S = lot(2)
    P = [box(C, (S - 0.2, S - 0.2, 0.05), (0, 0, 0), "asphalt")]
    P.append(box(C, (8.0, 6.0, 5.0), (-3.0, 3.6, 0), "metal"))
    P += gable(C, 8.0, 6.0, 1.6, (-3.0, 3.6, 5.0), "pipe_green", overhang=0.3, gable_color="metal")
    P.append(box(C, (4.0, 0.12, 4.0), (-3.0, 0.55, 0), "black"))
    # Mulden in Reihe mit Farbcodes
    for k, c in enumerate(("green", "blue", "metal_dark", "yellow")):
        x = -5.0 + k * 3.0
        P.append(prism(C, [(-1.2, -1.8), (1.2, -1.8), (1.2, 1.8), (-1.2, 1.8)], 1.6, (x, -4.5, 0.0), c))
        P.append(box(C, (2.2, 3.2, 0.1), (x, -4.5, 1.45), ("glass_light", "white", "rust", "orange")[k]))
    P.append(mound(C, 2.6, 2.0, 2.2, (4.6, 3.6, 0), "rust", seed=6))
    P.append(mound(C, 1.6, 1.4, 1.4, (5.0, 0.2, 0), "metal_dark", seed=8))
    P += fence(C, [(-7.1, -7.1), (-7.1, 7.1), (7.1, 7.1), (7.1, -7.1)], h=2.0, spacing=2.4, closed=False)
    return P


run_family({"Workshop_I1": workshop_i1, "Warehouse_I2": warehouse_i2, "Factory_I3": factory_i3,
            "TankFarm_I4": tank_farm_i4, "Sawmill_I5": sawmill_i5, "Brewery_I6": brewery_i6,
            "Logistics_I7": logistics_i7, "Recycling_I8": recycling_i8}, "Buildings/Industry")
