"""Mischgebiet, Einzelhandel, Buero. Masse = Grundstueck x 92 % (lot()), Strasse = -Y."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z = PLINTH


def shopfront(C, cx, y, w, h=3.0, glass="glass_light", frame="slate", awn=None, sign_col="red", z=Z):
    """Ladenfront auf der Fassade bei y (Front -Y): Schaufenster, Tuer, Schild, optional Markise."""
    P = [box(C, (w - 0.6, 0.1, h - 0.9), (cx, y - 0.05, z + 0.4), glass),
         box(C, (w - 0.4, 0.2, 0.25), (cx, y - 0.1, z + h - 0.5), frame),
         box(C, (1.4, 0.14, 2.3), (cx + w / 2 - 1.3, y - 0.08, z), "glass_dark")]
    P += sign(C, cx - 0.6, y - 0.12, z + h - 0.25, min(w - 2.0, 6.0), 0.6, sign_col, "white")
    if awn:
        P += awning(C, cx - 0.6, y - 0.1, min(w - 1.6, 7.0), 1.3, z + h - 0.45, awn, "white", 0.45)
    return P


# ------------------------------------------------------------------ Mischgebiet
def mixed_1(C):
    """2x2: Laeden im Erdgeschoss, drei Wohngeschosse, Dachterrasse."""
    w, d = 14.0, 11.0
    cy = 0.8
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, cy, 0), "concrete_dark"),
         box(C, (w, d, 3.6), (0, cy, Z), "slate")]
    P += shopfront(C, -3.4, cy - d / 2, 6.6, 3.6, awn="red", sign_col="red")
    P += shopfront(C, 3.6, cy - d / 2, 6.4, 3.6, awn="teal", sign_col="teal")
    p, top = block(C, 0, cy, w, d, 3, 3.0, wall="pastel_pink", z0=Z + 3.6, plinth="slate", spacing=2.6,
                   win_size=(1.2, 1.5), frame="white", bands="white")
    P += p
    for i in range(3):
        for x in (-4.5, 0.0, 4.5):
            P.append(box(C, (1.6, 0.35, 0.12), (x, cy - d / 2 - 0.15, Z + 3.6 + i * 3.0 + 0.7), "white"))  # Blumenkaesten
            P.append(box(C, (1.5, 0.3, 0.15), (x, cy - d / 2 - 0.15, Z + 3.6 + i * 3.0 + 0.82), "flower_pink"))
    P += flat_roof(C, w, d, top, (0, cy), "wood", "white", ph=1.0)
    P += tree(C, 4.5, cy + 2.5, 2.5, 0.9, seed=4)
    for o in P[-3:]:
        o.data.transform(Matrix.Translation((0, 0, top)))
    P.append(box(C, (3, 2, 2.4), (-4.0, cy + 2.5, top), "pastel_pink"))
    return P


def mixed_2(C):
    """3x3: fuenfgeschossiges Eckhaus, Laeden an zwei Seiten, Erker."""
    S = lot(3)
    w, d = 20.0, 14.0
    cx, cy = 0, -3.4
    P = [box(C, (w + 0.1, d + 0.1, Z), (cx, cy, 0), "concrete_dark"), box(C, (w, d, 3.8), (cx, cy, Z), "brick_dark")]
    P += shopfront(C, -5.5, cy - d / 2, 8.0, 3.8, awn="green_dark", sign_col="green_dark")
    P += shopfront(C, 5.0, cy - d / 2, 8.5, 3.8, awn="orange", sign_col="blue")
    p, top = block(C, cx, cy, w, d, 4, 3.1, wall="sand", z0=Z + 3.8, plinth="brick_dark", spacing=2.7,
                   win_size=(1.2, 1.6), frame="white", bands="cream")
    P += p
    # Erker vorne Mitte
    for i in range(4):
        P.append(box(C, (4.0, 1.0, 3.1), (cx, cy - d / 2 - 0.5, Z + 3.8 + i * 3.1), "cream"))
        P += window_row(C, (cx, cy - d / 2 - 1.0, Z + 3.8 + i * 3.1 + 1.6), (1, 0, 0), 2, 1.6, (1.2, 1.6),
                        normal=(0, -1, 0))
    P.append(hip(C, 4.0, 1.0, 1.0, (cx, cy - d / 2 - 0.5, top), "roof_green", overhang=0.15))
    P += gable(C, w, d, 3.6, (cx, cy, top), "roof_dark", overhang=0.35, gable_color="sand")
    # Hinterhof
    P.append(box(C, (S - 0.4, S / 2 - d / 2 + 6.4 - 6.4, 0.05), (0, 0, 0), "concrete"))
    P.append(box(C, (16.0, 6.0, 0.05), (0, 7.0, 0), "concrete"))
    P += tree(C, 5.0, 7.0, 5.0, 1.8, seed=9)
    P += bike_rack_small(C, -4.0, 6.0)
    P += [box(C, (1.2, 0.8, 1.2), (-7.5 + k * 1.3, 8.8, 0), c) for k, c in enumerate(("green_dark", "blue", "yellow"))]
    return P


def bike_rack_small(C, x, y):
    return [lathe(C, [(0.36, 0), (0.42, 0), (0.42, 0.05), (0.36, 0.05)], (x + k * 0.7, y, 0.42), "metal", segs=8,
                  rot=(90, 0, 0)) for k in range(4)]


# ------------------------------------------------------------------ Einzelhandel
def shop_1(C):
    """1x1: Baeckerei mit Satteldach und Markise."""
    w, d = 6.6, 6.0
    cy = 0.5
    P, top = block(C, 0, cy, w, d, 1, 3.4, wall="cream", z0=Z, sides="lrb", spacing=2.2, win_size=(1.0, 1.2))
    P += shopfront(C, 0, cy - d / 2, w, 3.4, awn="orange", sign_col="wood_dark")
    P += gable(C, w, d, 2.4, (0, cy, top), "roof_red", overhang=0.35, gable_color="cream")
    P.append(cyl(C, 0.45, 0.06, (2.5, cy - d / 2 - 0.5, Z + 2.6), "gold", segs=10, rot=(90, 0, 0)))  # Brezel-Schild
    P.append(box(C, (0.06, 0.6, 0.06), (2.5, cy - d / 2 - 0.25, Z + 2.6), "metal_dark"))
    P += bench(C, -2.0, -3.2, 0)
    return P


def shop_2(C):
    """1x2: zweigeschossige Boutique, Flachdach, Hof hinten."""
    w, d = 6.8, 9.0
    cy = -2.6
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, cy, 0), "concrete_dark"), box(C, (w, d, 3.4), (0, cy, Z), "black")]
    P += shopfront(C, 0, cy - d / 2, w, 3.4, glass="glass_light", frame="gold", sign_col="black")
    p, top = block(C, 0, cy, w, d, 1, 3.2, wall="pastel_blue", z0=Z + 3.4, plinth="black", spacing=2.2,
                   win_size=(1.4, 1.8), frame="white")
    P += p
    P += flat_roof(C, w, d, top, (0, cy), ph=0.6, parapet="white")
    P.append(box(C, (6.6, 5.0, 0.05), (0, 4.4, 0), "gravel"))
    P += crates(C, -1.6, 3.4, 2, seed=1)
    P += trash_bin(C, 2.2, 5.6)
    return P


def shop_3(C):
    """2x2: Supermarkt mit grossem Schild, Parkplaetzen und Einkaufswagen-Box."""
    S = lot(2)
    w, d = 14.0, 8.6
    cy = 2.6
    P = [box(C, (S - 0.2, 5.4, 0.05), (0, -4.3, 0), "asphalt")]
    for k in range(4):
        P.append(box(C, (0.12, 4.6, 0.06), (-6.0 + k * 3.0, -4.4, 0), "white"))
    P += parked_car(C, -4.5, -4.4, 0, "red")
    P += parked_car(C, 1.5, -4.4, 0, "metal")
    P.append(box(C, (w + 0.1, d + 0.1, Z), (0, cy, 0), "concrete_dark"))
    P.append(box(C, (w, d, 5.0), (0, cy, Z), "offwhite"))
    P.append(box(C, (w + 0.05, d + 0.05, 1.0), (0, cy, Z + 4.0), "yellow"))
    P.append(box(C, (8.0, 0.1, 2.6), (-2.0, cy - d / 2 - 0.05, Z), "glass_light"))
    P.append(box(C, (2.4, 0.12, 2.6), (4.0, cy - d / 2 - 0.06, Z), "glass_dark"))
    P.append(box(C, (4.4, 2.0, 0.25), (4.0, cy - d / 2 - 1.0, Z + 2.9), "yellow"))
    P += sign(C, -2.0, cy - d / 2 - 0.05, Z + 3.2, 7.0, 1.0, "red", "white")
    P += flat_roof(C, w, d, Z + 5.0, (0, cy), ph=0.4)
    P += ac_units(C, -3.0, cy + 1.0, Z + 5.0, 3, seed=7)
    # Einkaufswagen-Unterstand
    P.append(box(C, (2.4, 1.4, 0.1), (6.0, -1.0, 2.2), "metal"))
    for x in (5.0, 7.0):
        P.append(box(C, (0.1, 0.1, 2.2), (x, -1.6, 0), "metal_dark"))
    for k in range(3):
        P.append(box(C, (0.6, 1.0, 0.6), (5.4 + k * 0.5, -1.0, 0.3), "metal"))
    return P


def shop_4(C):
    """2x3: dreigeschossiges Kaufhaus mit Glasfront und Vordach."""
    w, d = 14.0, 19.0
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, 0, 0), "concrete_dark"), box(C, (w, d, 12.0), (0, 0, Z), "beige")]
    for i in range(3):
        P += ribbon(C, 0, 0, w, d, Z + 1.8 + i * 4.0, 2.4 if i else 3.0, "glass_light" if i == 0 else "glass", "flr", 0.8)
    for x in (-6.4, -2.2, 2.2, 6.4):
        P.append(box(C, (0.6, 0.3, 12.0), (x, -d / 2 - 0.1, Z), "slate"))
    P.append(box(C, (w + 0.6, 2.0, 0.35), (0, -d / 2 - 0.95, Z + 3.6), "slate"))
    P += sign(C, 0, -d / 2 - 0.15, Z + 10.6, 8.0, 1.2, "flower_violet", "white")
    P += flat_roof(C, w, d, Z + 12.0, (0, 0), ph=0.6, parapet="slate")
    P.append(box(C, (6.0, 5.0, 2.6), (0, 4.0, Z + 12.0), "beige"))
    P += ac_units(C, -3.0, -4.0, Z + 12.0, 3, seed=8)
    return P


# ------------------------------------------------------------------ Buero
def office_1(C):
    """2x2: sechsgeschossiges Buerohaus mit Fensterbaendern."""
    w, d = 13.0, 12.0
    P, top = block(C, 0, 0.6, w, d, 6, 3.4, wall="offwhite", z0=Z, ribbon_windows=True, win_size=(0, 1.9),
                   win="glass")
    P += door(C, 0, 0.6 - d / 2, 3.0, 2.8, "glass_light", canopy="steel_blue")
    P.append(box(C, (2.4, 0.5, 6 * 3.4 + 1.2), (w / 2 - 1.6, 0.6 - d / 2 - 0.15, Z), "steel_blue"))  # Akzentstreifen
    P += flat_roof(C, w, d, top, (0, 0.6), ph=0.6)
    P += rooftop_box(C, 0, 2.0, top)
    P += tree(C, -5.5, -5.8, 4.0, 1.3, seed=12)
    P += tree(C, 5.5, -5.8, 4.0, 1.3, seed=13)
    return P


def rooftop_box(C, x, y, z):
    return [box(C, (4.0, 3.0, 2.4), (x, y, z), "metal"), box(C, (4.2, 3.2, 0.15), (x, y, z + 2.4), "metal_dark")]


def office_2(C):
    """3x3: zwoelfgeschossiger Glasturm mit vertikalen Lamellen."""
    w = 17.0
    fh, n = 3.6, 12
    P = [box(C, (w + 0.2, w + 0.2, Z), (0, 0, 0), "concrete_dark"), box(C, (w, w, n * fh), (0, 0, Z), "glass")]
    for i in range(1, n):
        P.append(box(C, (w + 0.1, w + 0.1, 0.3), (0, 0, Z + i * fh - 0.15), "slate"))
    k = 9
    for j in range(k):
        t = -w / 2 + 0.9 + j * (w - 1.8) / (k - 1)
        for (x, y, sx, sy) in ((t, -w / 2 - 0.2, 0.15, 0.5), (t, w / 2 + 0.2, 0.15, 0.5),
                               (-w / 2 - 0.2, t, 0.5, 0.15), (w / 2 + 0.2, t, 0.5, 0.15)):
            P.append(box(C, (sx, sy, n * fh), (x, y, Z), "metal"))
    P.append(box(C, (6.0, 3.0, 0.3), (0, -w / 2 - 1.5, Z + 3.4), "metal"))
    P += door(C, 0, -w / 2, 3.2, 3.0, "glass_light", canopy=None)
    top = Z + n * fh
    P += flat_roof(C, w, w, top, (0, 0), ph=1.2, parapet="metal")
    P.append(box(C, (7.0, 7.0, 3.0), (0, 0, top), "metal_dark"))
    return P


def office_3(C):
    """4x4: 24-geschossiger Buero-Hochhaus mit Sockel, Ruecksprung und Spitze."""
    S = lot(4) - 0.4
    P = [box(C, (S, S, Z), (0, 0, 0), "concrete")]
    p, ptop = block(C, 0, 0, 26.0, 26.0, 2, 4.5, wall="slate", z0=Z, ribbon_windows=True, win_size=(0, 3.0),
                    win="glass_light")
    P += p
    P += flat_roof(C, 26.0, 26.0, ptop, (0, 0), "roof_light", "slate", ph=0.6)
    fh = 3.6
    p, t1 = block(C, 0, 2.0, 18.0, 18.0, 16, fh, wall="glass_dark", z0=ptop, plinth="slate", ribbon_windows=True,
                  win_size=(0, 1.0), win="glass_light")
    P += p
    for i in range(1, 16):
        P.append(box(C, (18.15, 18.15, 0.35), (0, 2.0, ptop + i * fh - 0.17), "metal"))
    p, t2 = block(C, 0, 2.0, 13.0, 13.0, 8, fh, wall="glass_dark", z0=t1, plinth="metal", ribbon_windows=True,
                  win_size=(0, 1.0), win="glass_light")
    P += p
    for i in range(1, 8):
        P.append(box(C, (13.15, 13.15, 0.35), (0, 2.0, t1 + i * fh - 0.17), "metal"))
    P.append(box(C, (13.4, 13.4, 1.2), (0, 2.0, t2), "metal"))
    P.append(cyl(C, 2.0, 4.0, (0, 2.0, t2 + 1.2), "metal", segs=8, r_top=1.2))
    P.append(cyl(C, 0.3, 12.0, (0, 2.0, t2 + 5.2), "metal", segs=6, r_top=0.08))
    P.append(sphere(C, 0.3, (0, 2.0, t2 + 17.3), "red", subdiv=1))
    P += door(C, 0, -13.0, 4.0, 3.2, "glass_dark", canopy="metal")
    for x in (-10, 10):
        P += tree(C, x, -13.6, 4.5, 1.2, "pine")
    return P


# ------------------------------------------------------------------ weitere Varianten
def mixed_3(C):
    """2x2: Altbau-Eckhaus mit Cafe und Aussenbestuhlung, 3 Wohngeschosse, Satteldach."""
    w, d, cy = 13.0, 10.0, 1.5
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, cy, 0), "concrete_dark"), box(C, (w, d, 3.6), (0, cy, Z), "terracotta")]
    P += shopfront(C, -1.0, cy - d / 2, 10.0, 3.6, awn="green_dark", sign_col="green_dark")
    p, top = block(C, 0, cy, w, d, 3, 3.1, wall="pastel_blue", z0=Z + 3.6, plinth="terracotta", spacing=2.4,
                   win_size=(1.1, 1.7), frame="white", bands="white")
    P += p
    P += gable(C, w, d, 3.4, (0, cy, top), "roof_red", overhang=0.3, gable_color="pastel_blue")
    for x in (-4.0, 0.0):
        P.append(box(C, (1.6, 1.6, 0.6), (x, cy - d / 2 - 0.9, top + 0.4), "pastel_blue"))
        P.append(gable(C, 1.6, 1.6, 0.6, (x, cy - d / 2 - 0.9 + 0.8, top + 1.0), "roof_red", overhang=0.1,
                       rot=(0, 0, 90))[0])
    # Aussenbestuhlung: Tische mit Sonnenschirmen
    for k, x in enumerate((-4.5, -1.5, 1.5)):
        y = cy - d / 2 - 2.4
        P.append(cyl(C, 0.45, 0.75, (x, y, 0), "white", segs=8, r_top=0.45))
        P.append(cyl(C, 0.04, 2.3, (x, y, 0.75), "metal", segs=6))
        P.append(cyl(C, 1.3, 0.4, (x, y, 2.5), ("red", "cream", "red")[k], segs=8, r_top=0.05))
        for s in (-1, 1):
            P.append(box(C, (0.4, 0.4, 0.45), (x + s * 0.75, y, 0), "wood_dark"))
    return P


def mixed_4(C):
    """2x3: moderner Mischbau, Laeden + Arztpraxis-Etage, 4 Wohngeschosse mit Erkern."""
    w, d = 14.0, 19.0
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, 0, 0), "concrete_dark"), box(C, (w, d, 4.0), (0, 0, Z), "slate")]
    P += shopfront(C, -3.4, -d / 2, 6.6, 4.0, sign_col="flower_violet")
    P += shopfront(C, 3.5, -d / 2, 6.4, 4.0, sign_col="orange", awn="orange")
    p, top = block(C, 0, 0, w, d, 5, 3.0, wall="white", z0=Z + 4.0, plinth="slate", spacing=2.5,
                   win_size=(1.4, 1.6), frame=None)
    P += p
    for i in range(5):
        for x in (-4.2, 4.2):
            P.append(box(C, (3.0, 1.0, 2.6), (x, -d / 2 - 0.5, Z + 4.2 + i * 3.0), "wood"))
            P.append(box(C, (2.6, 0.06, 1.6), (x, -d / 2 - 1.02, Z + 4.7 + i * 3.0), "glass"))
    P += flat_roof(C, w, d, top, (0, 0), "roof_green", "white", ph=0.5)
    P.append(box(C, (4.0, 4.0, 2.4), (0, 4.0, top), "slate"))
    return P


def shop_5(C):
    """1x1: Apotheke (gruenes Kreuz), zwei Geschosse."""
    w, d, cy = 6.8, 5.8, 0.7
    P = [box(C, (w + 0.1, d + 0.1, Z), (0, cy, 0), "concrete_dark"), box(C, (w, d, 3.4), (0, cy, Z), "white")]
    P += shopfront(C, 0, cy - d / 2, w, 3.4, sign_col="green")
    p, top = block(C, 0, cy, w, d, 1, 3.0, wall="pastel_green", z0=Z + 3.4, plinth="white", spacing=2.2,
                   win_size=(1.1, 1.4), frame="white")
    P += p
    P += gable(C, w, d, 2.2, (0, cy, top), "roof_dark", overhang=0.3, gable_color="pastel_green")
    # Apothekenkreuz (Ausleger)
    P.append(box(C, (0.08, 0.9, 0.08), (2.9, cy - d / 2 - 0.45, Z + 3.0), "metal_dark"))
    P.append(box(C, (0.2, 0.9, 0.3), (2.9, cy - d / 2 - 0.9, Z + 2.7), "green", center=True))
    P.append(box(C, (0.2, 0.3, 0.9), (2.9, cy - d / 2 - 0.9, Z + 2.7), "green", center=True))
    return P


def shop_6(C):
    """2x2: Tankstelle mit Dach, Zapfsaeulen, Shop und Preismast."""
    S = lot(2)
    P = [box(C, (S - 0.2, S - 0.2, 0.06), (0, 0, 0), "asphalt")]
    P.append(box(C, (9.0, 6.0, 0.6), (-0.5, -2.6, 4.6), "white"))           # Dach
    P.append(box(C, (9.05, 6.05, 0.4), (-0.5, -2.6, 4.7), "red"))
    for x in (-3.0, 2.0):
        P.append(box(C, (0.4, 0.4, 4.6), (x, -2.6, 0), "metal"))
        P.append(box(C, (1.6, 0.8, 0.2), (x, -2.6, 0.06), "concrete"))
        P.append(box(C, (0.7, 0.5, 1.7), (x, -2.6, 0.26), "white"))
        P.append(box(C, (0.72, 0.52, 0.3), (x, -2.6, 1.7), "red"))
    P += parked_car(C, -1.6, -2.6, 0, "steel_blue")
    # Shop hinten
    P.append(box(C, (10.0, 5.0, 3.4), (0, 4.4, 0), "white"))
    P.append(box(C, (10.1, 5.1, 0.6), (0, 4.4, 2.8), "red"))
    P.append(box(C, (6.0, 0.1, 2.2), (-1.0, 1.85, 0.2), "glass_light"))
    P.append(box(C, (1.6, 0.12, 2.3), (3.2, 1.85, 0.1), "glass_dark"))
    P += flat_roof(C, 10.0, 5.0, 3.4, (0, 4.4), ph=0.3)
    # Preismast vorne rechts
    P.append(box(C, (0.4, 0.4, 5.5), (6.0, -6.2, 0), "metal_dark"))
    P.append(box(C, (1.6, 0.4, 3.0), (6.0, -6.2, 4.5), "white"))
    P.append(box(C, (1.62, 0.42, 0.8), (6.0, -6.2, 6.7), "red"))
    for k in range(3):
        P.append(box(C, (1.2, 0.44, 0.35), (6.0, -6.2, 4.8 + k * 0.6), "black"))
    P += [box(C, (0.4, 0.4, 0.8), (5.0 + k * 0.6, 5.8, 0), "metal") for k in range(2)]   # Luft/Wasser
    return P


def shop_7(C):
    """1x1: Cafe mit Glasfront, Markise, Tischen und Pflanzkuebeln."""
    w, d, cy = 6.6, 5.2, 1.0
    P, top = block(C, 0, cy, w, d, 1, 3.6, wall="wood_dark", z0=Z, sides="lrb", spacing=2.2, win_size=(1.0, 1.4))
    P += shopfront(C, 0, cy - d / 2, w, 3.6, glass="glass_light", frame="wood", sign_col="cream", awn="teal")
    P += flat_roof(C, w, d, top, (0, cy), "roof_green", "wood_dark", ph=0.5)
    for x in (-2.0, 1.0):
        y = -2.9
        P.append(cyl(C, 0.35, 0.72, (x, y, 0), "white", segs=8))
        for s in (-1, 1):
            P.append(box(C, (0.35, 0.35, 0.45), (x + s * 0.6, y, 0), "metal"))
    P.append(box(C, (0.6, 0.6, 0.6), (3.0, -3.0, 0), "wood"))
    P.append(sphere(C, 0.45, (3.0, -3.0, 0.9), "green", subdiv=1))
    return P


def shop_8(C):
    """2x3: Baumarkt mit Gartencenter-Zaun und Parkplatz."""
    w, d = 14.0, 13.0
    P = [box(C, (lot(2) - 0.2, 7.6, 0.05), (0, -7.2, 0), "asphalt")]
    for k in range(5):
        P.append(box(C, (0.12, 4.6, 0.06), (-6.0 + k * 3.0, -7.6, 0), "white"))
    P += parked_car(C, -4.5, -7.6, 0, "green_dark")
    P += parked_car(C, 4.5, -7.6, 0, "white")
    P.append(box(C, (w, d, 7.0), (0, 4.0, 0), "orange"))
    P.append(box(C, (w + 0.05, d + 0.05, 1.2), (0, 4.0, 5.8), "white"))
    P += flat_roof(C, w, d, 7.0, (0, 4.0), ph=0.5)
    P.append(box(C, (3.0, 0.12, 3.0), (-3.5, 4.0 - d / 2 - 0.06, 0), "glass_dark"))
    P += sign(C, 2.0, 4.0 - d / 2 - 0.1, 6.0, 7.0, 1.0, "orange", "black")
    P += fence(C, [(-6.9, -2.6), (-6.9, -3.4), (-1.0, -3.4), (-1.0, -2.6)], h=2.0, spacing=1.5, closed=False)
    P += [box(C, (1.2, 0.8, 0.5), (-5.6 + k * 1.4, -2.0, 0), "wood") for k in range(3)]
    P += [sphere(C, 0.4, (-5.6 + k * 1.4, -2.0, 0.85), "green", subdiv=1) for k in range(3)]
    return P


def office_4(C):
    """2x2: Loft-Buero in umgebautem Ziegelbau, Sprossenfenster, Sheddach-Aufsatz."""
    w, d = 13.0, 11.0
    P, top = block(C, 0, 0.6, w, d, 3, 3.8, wall="brick", z0=Z, spacing=2.4, win_size=(1.6, 2.4), frame="cream",
                   skip=lambda s, r, c: s == "f" and r == 0 and c == 2)
    for i in range(1, 3):
        P.append(box(C, (w + 0.1, d + 0.1, 0.3), (0, 0.6, Z + i * 3.8 - 0.15), "brick_dark"))
    P += flat_roof(C, w, d, top, (0, 0.6), ph=0.5, parapet="brick_dark")
    P += sawtooth(C, 0, 1.0, 9.0, 7.0, top, n=2, h=1.8, roof="metal_dark", glass="glass_light")
    P += door(C, 0, 0.6 - d / 2, 2.0, 2.8, "black", canopy="black")
    P.append(box(C, (0.12, 1.0, 1.0), (2.0, 0.6 - d / 2 - 0.5, Z + 3.0), "metal_dark"))
    P += sign(C, 0, 0.6 - d / 2 - 0.1, Z + 3.25, 4.4, 0.5, "black", "cream")
    return P


def office_5(C):
    """3x3: L-foermiger Buerocampus (6 und 9 Geschosse) mit Vorplatz."""
    P = [box(C, (lot(3) - 0.4, 7.0, 0.05), (0, -7.4, 0), "concrete")]
    p, t1 = block(C, -3.0, -1.0, 15.0, 9.0, 6, 3.5, wall="cream", z0=Z, ribbon_windows=True, win_size=(0, 2.0),
                  win="glass_dark")
    P += p
    P += flat_roof(C, 15.0, 9.0, t1, (-3.0, -1.0), "roof_green", "cream", ph=0.6)
    p, t2 = block(C, 6.0, 4.0, 9.0, 13.0, 9, 3.5, wall="slate", z0=Z, ribbon_windows=True, win_size=(0, 2.0),
                  win="glass_light")
    P += p
    P += flat_roof(C, 9.0, 13.0, t2, (6.0, 4.0), ph=0.6)
    P.append(box(C, (4.0, 4.0, 2.6), (6.0, 6.0, t2), "metal"))
    P += door(C, -3.0, -5.5, 3.0, 3.0, "glass_light", canopy="slate")
    P.append(cyl(C, 2.0, 0.5, (-3.0, -8.5, 0), "concrete", segs=12))   # Brunnen/Rondell
    P.append(cyl(C, 1.6, 0.06, (-3.0, -8.5, 0.48), "water", segs=12))
    for x in (-9.5, 3.5):
        P += tree(C, x, -8.5, 4.5, 1.4, seed=int(x))
    return P


def office_6(C):
    """4x4: gestaffelte Glaspyramide (Hochhaus mit Ruecksprung je 4 Geschosse)."""
    S = lot(4) - 0.4
    P = [box(C, (S, S, Z), (0, 0, 0), "concrete")]
    z = Z
    w = 26.0
    for k, n in enumerate((3, 4, 4, 4, 4, 3)):
        p, z2 = block(C, 0, 0.5, w, w, n, 3.6, wall="teal" if k % 2 else "glass", z0=z, plinth="metal", ribbon_windows=True,
                      win_size=(0, 2.2), win="glass_light" if k % 2 else "glass_dark")
        P += p
        P.append(box(C, (w + 0.2, w + 0.2, 0.4), (0, 0.5, z2), "metal"))
        z = z2 + 0.4
        w -= 3.6
    P.append(cyl(C, 0.25, 10.0, (0, 0.5, z), "metal", segs=6, r_top=0.05))
    P += door(C, 0, 0.5 - 13.0, 4.0, 3.2, "glass_dark", canopy="metal")
    return P


run_family({"Mixed_1": mixed_1, "Mixed_2": mixed_2, "Mixed_3": mixed_3, "Mixed_4": mixed_4}, "Buildings/Mixed")
run_family({"Shop_1": shop_1, "Shop_2": shop_2, "Shop_3": shop_3, "Shop_4": shop_4, "Shop_5": shop_5,
            "Shop_6": shop_6, "Shop_7": shop_7, "Shop_8": shop_8}, "Buildings/Retail")
run_family({"Office_1": office_1, "Office_2": office_2, "Office_3": office_3, "Office_4": office_4,
            "Office_5": office_5, "Office_6": office_6}, "Buildings/Office",
           views={"": (1.0, -1.2, 0.6), "_back": (-1.0, 1.0, 0.6)})
