"""Weitere G4-Dienste (Models/Services) mit den Dateinamen und Grundflaechen, die das Spiel erwartet
(OwnModelCatalogBuilder.ServiceFiles / ServiceCatalog): Clinic 16 x 16, PrimarySchool 32 x 24, HighSchool 40 x 32,
ParkSmall 16 x 16, ParkLarge 48 x 40. Vorderseite (Strasse) = -Y."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.2, 0.8)}


def court_lines(C, fx, fy, w, d, z, color="white"):
    out = []
    for (sx, sy, px, py) in ((w, 0.1, 0, -d / 2 + 0.05), (w, 0.1, 0, d / 2 - 0.05), (0.1, d, -w / 2 + 0.05, 0),
                             (0.1, d, w / 2 - 0.05, 0), (0.1, d, 0, 0)):
        out.append(box(C, (sx, sy, 0.05), (fx + px, fy + py, z), color))
    return out


# ------------------------------------------------------------------ Klinik / Aerztehaus
def clinic(C):
    """Aerztehaus (16 x 16 m): zweigeschossig, Fensterbaender, Eingang mit Vordach und Rotkreuz-Schild,
    zwei Stellplaetze, Krankenwagen-Bucht."""
    P = [box(C, (16.0, 16.0, 0.12), (0, 0, 0), "grass"),
         box(C, (16.0, 5.5, 0.06), (0, -5.25, 0.12), "asphalt")]
    for x in (-5.0, -1.8, 1.4):
        P.append(box(C, (0.1, 4.6, 0.02), (x, -5.4, 0.18), "white"))
    cx, cy, w, d = 0.5, 3.0, 13.0, 9.0
    P.append(box(C, (w + 0.1, d + 0.1, PLINTH), (cx, cy, 0), "concrete_dark"))
    P.append(box(C, (w, d, 7.0), (cx, cy, PLINTH), "white"))
    P += ribbon(C, cx, cy, w, d, PLINTH + 1.9, 1.3, "glass", "lrb", 0.6)
    P += ribbon(C, cx, cy, w, d, PLINTH + 5.2, 1.5, "glass", "flrb", 0.6)
    P.append(box(C, (w + 0.12, d + 0.12, 0.5), (cx, cy, PLINTH + 3.4), "teal"))
    P += flat_roof(C, w, d, PLINTH + 7.0, (cx, cy))
    P += ac_units(C, cx + 2.0, cy + 1.5, PLINTH + 7.08, 2, seed=3)
    # Erdgeschoss vorne: Glaseingang + Vordach
    fy = cy - d / 2
    P.append(box(C, (5.0, 0.1, 2.6), (cx - 2.5, fy - 0.05, PLINTH), "glass_dark"))
    P.append(box(C, (6.0, 2.4, 0.25), (cx - 2.5, fy - 1.2, PLINTH + 2.9), "teal"))
    for x in (cx - 5.2, cx + 0.2):
        P.append(box(C, (0.15, 0.15, 2.9), (x, fy - 2.2, PLINTH), "metal"))
    P += window_row(C, (cx + 3.2, fy, PLINTH + 1.9), (1, 0, 0), 2, 2.4, (1.6, 1.3), "glass", normal=(0, -1, 0))
    # Rotkreuz-Schild
    sx, sz = cx + 4.5, PLINTH + 5.4
    P.append(box(C, (1.8, 0.15, 1.8), (sx, fy - 0.08, sz - 0.9), "white"))
    P.append(box(C, (1.2, 0.2, 0.4), (sx, fy - 0.12, sz - 0.2), "red"))
    P.append(box(C, (0.4, 0.2, 1.2), (sx, fy - 0.12, sz - 0.6), "red"))
    P += parked_car(C, -3.4, -5.4, 0, "steel_blue")
    P += parked_car(C, -0.2, -5.4, 0, "white")
    # Krankenwagen
    amb = [box(C, (2.1, 5.6, 2.4), (0, 0.4, 0.4), "white", bevel=0.1),
           box(C, (2.0, 1.6, 1.3), (0, -2.9, 0.4), "white", bevel=0.1),
           box(C, (1.9, 0.08, 0.6), (0, -3.68, 1.2), "glass_dark"),
           box(C, (2.14, 5.0, 0.35), (0, 0.6, 1.3), "red", center=False),
           box(C, (0.8, 0.3, 0.18), (0, -2.0, 2.8), "blue")]
    for wx in (-1.05, 0.8):
        for wy in (-2.6, 2.0):
            amb.append(cyl(C, 0.42, 0.25, (wx, wy, 0.42), "black", segs=8, rot=(0, 90, 0)))
    P += place_at(amb, 5.6, -4.2, 0)
    P += tree(C, -6.5, 3.0, 6.0, 1.6, "round", seed=2)
    P += tree(C, -6.5, 6.5, 5.0, 1.4, "pine", seed=3)
    P += bench(C, -5.3, -1.8, 0)
    return P


# ------------------------------------------------------------------ Grundschule
def primary_school(C):
    """Grundschule (32 x 24 m): eingeschossiger Pavillonbau mit bunten Fassadenfeldern, Pultdaechern,
    Pausenhof mit Spielgeraeten, Klettergeruest, Sandkasten."""
    Z0 = 0.15
    P = [box(C, (32.0, 24.0, Z0), (0, 0, 0), "grass"),
         box(C, (18.0, 10.0, 0.04), (6.0, -5.0, Z0), "concrete"),
         box(C, (3.0, 3.0, 0.04), (-6.0, -10.5, Z0), "concrete")]
    colors = ("pastel_yellow", "pastel_blue", "pastel_green", "pastel_pink")
    # zwei Klassentrakte: hinten quer, links laengs
    for (cx, cy, w, d, rot) in ((2.0, 7.5, 24.0, 8.0, 0), (-10.5, -2.0, 8.0, 12.0, 1)):
        P.append(box(C, (w + 0.1, d + 0.1, PLINTH), (cx, cy, Z0), "concrete_dark"))
        P.append(box(C, (w, d, 3.8), (cx, cy, Z0 + PLINTH), "white"))
        # bunte Felder um die Fenster
        if rot == 0:
            n = int(w // 4)
            for k in range(n):
                x = cx - w / 2 + 2.0 + k * 4.0
                P.append(box(C, (3.0, 0.1, 2.6), (x, cy - d / 2 - 0.05, Z0 + PLINTH + 0.6), colors[k % 4]))
                P.append(box(C, (2.0, 0.14, 1.6), (x, cy - d / 2 - 0.08, Z0 + PLINTH + 1.1), "glass"))
            P.append(prism(C, [(-w / 2 - 0.4, -d / 2 - 0.6), (w / 2 + 0.4, -d / 2 - 0.6), (w / 2 + 0.4, d / 2 + 0.6),
                               (-w / 2 - 0.4, d / 2 + 0.6)], 0.3, (cx, cy, Z0 + PLINTH + 3.8), "roof_light"))
            P.append(box(C, (w + 0.8, 0.4, 1.2), (cx, cy + d / 2 + 0.4, Z0 + PLINTH + 3.8), "roof_light"))
        else:
            n = int(d // 4)
            for k in range(n):
                y = cy - d / 2 + 2.0 + k * 4.0
                P.append(box(C, (0.1, 3.0, 2.6), (cx + w / 2 + 0.05, y, Z0 + PLINTH + 0.6), colors[(k + 2) % 4]))
                P.append(box(C, (0.14, 2.0, 1.6), (cx + w / 2 + 0.08, y, Z0 + PLINTH + 1.1), "glass"))
            P += flat_roof(C, w, d, Z0 + PLINTH + 3.8, (cx, cy), "roof_light")
    # Eingang (Verbindung beider Trakte) mit Vordach
    P.append(box(C, (5.0, 5.0, 4.6), (-8.5, 6.0, Z0), "orange"))
    P.append(box(C, (3.0, 0.1, 2.6), (-8.5, 3.45, Z0 + PLINTH), "glass_dark"))
    P.append(box(C, (4.0, 2.0, 0.2), (-8.5, 2.5, Z0 + 3.2), "white"))
    P += sign(C, -8.5, 3.5, Z0 + 3.6, 3.4, 0.6, "white", "orange")
    # Spielgeraete
    P.append(box(C, (4.0, 4.0, 0.3), (12.0, -6.5, Z0), "sand"))                          # Sandkasten
    for (sx, sy, px, py) in ((4.3, 0.2, 0, -2.05), (4.3, 0.2, 0, 2.05), (0.2, 4.3, -2.05, 0), (0.2, 4.3, 2.05, 0)):
        P.append(box(C, (sx, sy, 0.4), (12.0 + px, -6.5 + py, Z0), "wood"))
    for k in range(4):                                                                     # Klettergeruest
        P.append(box(C, (0.12, 0.12, 2.4), (2.0 + (k % 2) * 2.4, -7.5 + (k // 2) * 2.4, Z0), "red"))
    for z in (0.8, 1.6):
        P.append(box(C, (2.6, 0.08, 0.08), (3.2, -7.5, Z0 + z), "yellow"))
    P.append(box(C, (2.6, 2.6, 0.08), (3.2, -6.3, Z0 + 2.4), "yellow"))
    P.append(beam(C, (4.4, -5.1, Z0 + 2.4), (6.6, -3.2, Z0 + 0.1), 0.6, "blue", t2=0.1))   # Rutsche
    for s in (-1, 1):                                                                      # Schaukel
        P.append(beam(C, (8.0 + s * 0.0, -1.0 + s * 1.2, Z0), (8.0, -1.0 + s * 0.6, Z0 + 2.6), 0.1, "metal_dark"))
        P.append(beam(C, (10.6, -1.0 + s * 1.2, Z0), (10.6, -1.0 + s * 0.6, Z0 + 2.6), 0.1, "metal_dark"))
    P.append(box(C, (2.8, 0.12, 0.12), (9.3, -1.0, Z0 + 2.6), "metal_dark"))
    for x in (8.8, 9.8):
        P.append(box(C, (0.03, 0.03, 1.8), (x, -1.0, Z0 + 0.8), "metal_dark"))
        P.append(box(C, (0.5, 0.25, 0.05), (x, -1.0, Z0 + 0.75), "red"))
    # Hof-Markierung (Hinkelkasten), Baeume, Fahnen, Hecke
    for k in range(5):
        P.append(box(C, (0.8, 0.8, 0.02), (0.0, -2.5 + k * 0.9, Z0 + 0.04), colors[k % 4]))
    P += tree(C, 14.0, 2.0, 7.0, 2.4, "round", seed=1)
    P += tree(C, -1.5, -9.5, 6.0, 2.0, "round", seed=3)
    P += tree(C, 14.0, 9.0, 6.0, 1.8, "pine", seed=5)
    for x in (-2.0, 2.4):
        P += bench(C, x, 2.2, 180)
    P.append(hedge(C, (-15.6, -11.6), (-7.8, -11.6), 0.9, 0.6))
    P.append(hedge(C, (-4.2, -11.6), (15.6, -11.6), 0.9, 0.6))
    return P


# ------------------------------------------------------------------ Gymnasium / Oberschule
def high_school(C):
    """Gymnasium / Oberschule (40 x 32 m): dreigeschossiger Klinkerbau (L-Form), Aula mit Glasfront,
    Sporthalle, Kleinspielfeld mit Laufbahn-Geraden, Fahrradstaender."""
    Z0 = 0.15
    P = [box(C, (40.0, 32.0, Z0), (0, 0, 0), "grass"),
         box(C, (14.0, 7.0, 0.04), (-8.0, -12.5, Z0), "concrete")]
    # Haupttrakt (hinten, 3 Geschosse)
    parts, top = block(C, -4.0, 10.0, 30.0, 10.0, 3, fh=3.6, wall="brick", plinth="concrete_dark", z0=Z0 + 0.3,
                       spacing=2.7, win_size=(1.8, 2.0), frame="white", bands="offwhite")
    P += parts
    P += flat_roof(C, 30.0, 10.0, top, (-4.0, 10.0))
    P += solar_rows(C, -4.0, 10.0, top + 0.1, 24.0, 6.0)
    # Fluegel links vorne
    parts, top2 = block(C, -15.0, -2.0, 10.0, 14.0, 3, fh=3.6, wall="brick", plinth="concrete_dark", z0=Z0 + 0.3,
                        spacing=2.7, win_size=(1.8, 2.0), frame="white", bands="offwhite", sides="frl")
    P += parts
    P += flat_roof(C, 10.0, 14.0, top2, (-15.0, -2.0))
    # Aula / Eingang: Glaskubus im Winkel
    P.append(box(C, (9.0, 7.0, 6.5), (-5.5, 1.5, Z0 + 0.3), "glass_light"))
    for k in range(5):
        P.append(box(C, (0.15, 7.05, 6.5), (-10.0 + k * 2.25, 1.5, Z0 + 0.3), "metal"))
    P.append(box(C, (9.6, 7.6, 0.5), (-5.5, 1.5, Z0 + 6.8), "offwhite"))
    P.append(box(C, (4.0, 0.12, 2.6), (-5.5, -2.06, Z0 + 0.3), "glass_dark"))
    P += sign(C, -5.5, -2.0, Z0 + 5.2, 6.0, 0.7, "steel_blue", "white")
    # Sporthalle rechts hinten (Tonnendach)
    hx, hy, hw, hd = 13.5, 9.0, 12.0, 13.0
    P.append(box(C, (hw, hd, 6.0), (hx, hy, Z0), "concrete"))
    P += ribbon(C, hx, hy, hw, hd, Z0 + 5.0, 0.9, "glass_light", "flr", 0.5)
    arch = [(hw / 2 * math.cos(math.pi * k / 8), 2.2 * math.sin(math.pi * k / 8)) for k in range(9)]
    roof = prism(C, arch, hd + 0.6, (0, 0, 0), "roof_light", name="barrel")
    roof.data.transform(Matrix.Translation((hx, hy + (hd + 0.6) / 2, Z0 + 6.0)) @
                        Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(roof)
    # Kleinspielfeld mit Laufbahn
    fx, fy = 7.8, -8.5
    P.append(box(C, (24.0, 13.0, 0.04), (fx, fy, Z0), "court_orange"))
    P.append(box(C, (20.0, 9.0, 0.05), (fx, fy, Z0 + 0.01), "green"))
    P += court_lines(C, fx, fy, 19.0, 8.4, Z0 + 0.02)
    for k in range(3):
        P.append(box(C, (24.0, 0.06, 0.02), (fx, fy - 6.5 + 0.6 + k * 0.6, Z0 + 0.04), "white"))
    for s in (-1, 1):
        P.append(box(C, (0.1, 3.0, 2.0), (fx + s * 9.6, fy, Z0), "white"))
        P.append(box(C, (0.8, 3.0, 0.1), (fx + s * 10.0, fy, Z0 + 1.95), "white"))
    # Fahrradstaender + Baeume + Bank
    for k in range(8):
        P.append(lathe(C, [(0.35, 0), (0.4, 0), (0.4, 0.05), (0.35, 0.05)], (-13.0 + k * 0.7, -14.0, Z0 + 0.4),
                       "metal_dark", segs=8, rot=(90, 0, 0)))
    P.append(box(C, (6.0, 2.0, 0.12), (-10.6, -14.0, Z0 + 2.2), "roof_light"))
    for x in (-13.6, -7.6):
        P.append(box(C, (0.12, 0.12, 2.2), (x, -13.2, Z0), "metal_dark"))
    for (x, y) in ((-17.6, -13.6), (1.0, -13.6), (17.6, 3.0), (-1.0, -6.0)):
        P += tree(C, x, y, 7.0, 2.2, "round", seed=int(x * 3 + y))
    P += flagpole(C, 0.0, -2.5, 9.0, "steel_blue", "white")
    return P


def solar_rows(C, cx, cy, z, w, d, rows=3):
    out = []
    for k in range(rows):
        y = cy - d / 2 + d * (k + 0.5) / rows
        out.append(box(C, (w, 1.4, 0.08), (cx, y, z + 0.4), "solar", rot=(20, 0, 0), center=True))
        out.append(box(C, (w, 0.15, 0.4), (cx, y + 0.5, z), "metal_dark"))
    return out


# ------------------------------------------------------------------ Parks
def park_small(C):
    """Pocket-Park (16 x 16 m): diagonaler Weg, Rundplatz mit Baum, Baenke, Beete, Wippe."""
    Z0 = 0.12
    P = [box(C, (16.0, 16.0, Z0), (0, 0, 0), "grass")]
    c = 1.56                                        # Diagonalweg (2,2 m breit), an der Grundflaeche abgeschnitten
    P.append(prism(C, [(-8.0, 8.0 - 2 * c), (8.0 - 2 * c, -8.0), (8.0, -8.0), (8.0, -8.0 + 2 * c),
                       (-8.0 + 2 * c, 8.0), (-8.0, 8.0)], 0.04, (0, 0, Z0), "gravel", name="path"))
    P.append(box(C, (2.2, 9.0, 0.04), (0, -3.5, Z0), "gravel"))
    P.append(cyl(C, 3.4, 0.05, (0, 0, Z0), "gravel", segs=16))
    P.append(cyl(C, 1.4, 0.45, (0, 0, Z0), "concrete", segs=10))
    P.append(cyl(C, 1.2, 0.05, (0, 0, Z0 + 0.45), "soil", segs=10))
    P += [o for o in tree(C, 0, 0, 8.0, 2.6, "round", seed=5)]
    for o in P[-3:]:
        o.data.transform(Matrix.Translation((0, 0, 0.45)))
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        x, y = 2.9 * math.cos(r), 2.9 * math.sin(r)
        P += bench(C, x, y, math.degrees(math.atan2(x, -y)) + 180)
    P += flower_bed(C, -5.0, 5.0, 3.0, 1.4, seed=1)
    P += flower_bed(C, -4.8, -2.4, 3.0, 1.4, seed=2)
    P += tree(C, 5.5, 5.0, 6.0, 1.9, "round", seed=3)
    P += tree(C, -5.5, -5.5, 5.5, 1.6, "pine", seed=4)
    P += tree(C, 6.5, 1.0, 5.0, 1.5, "round", seed=6)
    P.append(box(C, (0.3, 0.3, 0.5), (2.8, 5.6, Z0), "red"))                           # Wippe
    P.append(box(C, (3.2, 0.3, 0.1), (2.8, 5.6, Z0 + 0.5), "yellow", rot=(0, 10, 0)))
    P += lamp(C, 2.2, -6.5, 0, h=4.0)
    P += trash_bin(C, -1.8, -6.5)
    # Hecke mit Luecken an den Wegenden (Ecken links oben / rechts unten, vorne Mitte)
    P.append(hedge(C, (-7.7, -7.7), (-7.7, 4.6), 0.8, 0.5))
    P.append(hedge(C, (-4.6, 7.7), (7.7, 7.7), 0.8, 0.5))
    P.append(hedge(C, (7.7, -4.6), (7.7, 7.7), 0.8, 0.5))
    P.append(hedge(C, (-7.7, -7.7), (-1.6, -7.7), 0.8, 0.5))
    P.append(hedge(C, (1.6, -7.7), (4.6, -7.7), 0.8, 0.5))
    return P


def park_large(C):
    """Stadtpark (48 x 40 m): Teich mit Holzbruecke und Insel, Rundweg, Pavillon, Spielplatz, Liegewiese,
    viele Baeume, Baenke und Laternen."""
    rnd = random.Random(42)
    Z0 = 0.12
    P = [box(C, (48.0, 40.0, Z0), (0, 0, 0), "grass")]
    # Wege: Rechteck-Rundweg + Diagonalen zu den Ecken
    ring = [(-16.0, -12.0), (16.0, -12.0), (16.0, 12.0), (-16.0, 12.0)]
    for i in range(4):
        a, b = ring[i], ring[(i + 1) % 4]
        L = math.hypot(b[0] - a[0], b[1] - a[1]) + 2.4
        ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
        P.append(box(C, (L, 2.4, 0.04), ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, Z0), "gravel", rot=(0, 0, ang)))
    for (a, b) in (((-16.0, -12.0), (-23.0, -19.0)), ((16.0, -12.0), (23.0, -19.0)), ((16.0, 12.0), (23.0, 19.0)),
                   ((-16.0, 12.0), (-23.0, 19.0)), ((0.0, -12.0), (0.0, -19.4))):
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        ang = math.degrees(math.atan2(b[1] - a[1], b[0] - a[0]))
        P.append(box(C, (L, 2.4, 0.04), ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, Z0), "gravel", rot=(0, 0, ang)))
    # Teich (unregelmaessiges Achteck) mit Ufersteinen und Insel
    pond = [(-12.0 + 7.5 * math.cos(2 * math.pi * k / 14) * (1 + 0.12 * math.sin(3 * k)),
             2.5 + 5.5 * math.sin(2 * math.pi * k / 14) * (1 + 0.1 * math.cos(2 * k))) for k in range(14)]
    P.append(prism(C, [(x * 1.0 + (x + 12.0) * 0.08, (y - 2.5) * 1.1 + 2.5) for x, y in pond], 0.08,
                   (0, 0, Z0), "sand", name="shore"))
    P.append(prism(C, pond, 0.1, (0, 0, Z0), "water", name="pond"))
    for k in range(0, 14, 2):
        x, y = pond[k]
        P.append(mound(C, 0.6, 0.5, 0.4, (x * 1.05 + 0.6, (y - 2.5) * 1.08 + 2.5, Z0), "rock", seed=k, subdiv=1))
    P.append(cyl(C, 1.6, 0.25, (-14.0, 4.0, Z0), "grass", segs=8))
    P += tree(C, -14.0, 4.0, 5.0, 1.5, "round", seed=9)
    for o in P[-3:]:
        o.data.transform(Matrix.Translation((0, 0, 0.3)))
    # Holzbruecke ueber den Teich (laengs Y)
    P.append(box(C, (2.0, 11.0, 0.2), (-9.0, 2.5, Z0 + 0.5), "wood"))
    for s in (-1, 1):
        P.append(box(C, (0.08, 11.0, 0.08), (-9.0 + s * 0.95, 2.5, Z0 + 1.5), "wood_dark"))
        for k in range(6):
            P.append(box(C, (0.1, 0.1, 1.0), (-9.0 + s * 0.95, -2.5 + k * 2.0, Z0 + 0.6), "wood_dark"))
    for k in range(4):
        P.append(box(C, (0.25, 0.25, 0.6), (-9.0 + (k % 2) * 1.6 - 0.8, -0.5 + (k // 2) * 6.0, Z0), "wood_dark"))
    # Pavillon
    px, py = 8.0, 3.0
    P.append(cyl(C, 3.2, 0.4, (px, py, Z0), "offwhite", segs=8))
    for k in range(8):
        a = 2 * math.pi * k / 8 + math.pi / 8
        P.append(cyl(C, 0.12, 2.6, (px + 2.8 * math.cos(a), py + 2.8 * math.sin(a), Z0 + 0.4), "white", segs=6))
    P.append(cyl(C, 3.6, 1.6, (px, py, Z0 + 3.0), "roof_green", segs=8, r_top=0.2, rot=(0, 0, 22.5)))
    P.append(sphere(C, 0.2, (px, py, Z0 + 4.7), "gold", subdiv=1))
    # Spielplatz (rechts vorne)
    sx, sy = 9.0, -6.5
    P.append(box(C, (8.0, 6.0, 0.05), (sx, sy, Z0), "sand"))
    for k in range(4):
        P.append(box(C, (0.14, 0.14, 2.2), (sx - 2.5 + (k % 2) * 1.8, sy - 1.0 + (k // 2) * 1.8, Z0), "red"))
    P.append(box(C, (2.0, 2.0, 0.1), (sx - 1.6, sy - 0.1, Z0 + 1.4), "wood"))
    P.append(prism(C, [(-1.1, -1.1), (1.1, -1.1), (1.1, 1.1), (-1.1, 1.1)], 0.1, (sx - 1.6, sy - 0.1, Z0 + 2.2), "blue"))
    P.append(beam(C, (sx - 0.5, sy - 0.1, Z0 + 1.45), (sx + 2.2, sy - 0.1, Z0 + 0.1), 0.7, "yellow", t2=0.08))
    P.append(cyl(C, 1.0, 0.3, (sx + 2.5, sy + 2.0, Z0), "orange", segs=10))           # Karussell
    P.append(cyl(C, 0.08, 1.0, (sx + 2.5, sy + 2.0, Z0 + 0.3), "metal_dark", segs=6))
    # Liegewiese hinten rechts mit Decken
    for (x, y, c) in ((14.0, 16.0, "red"), (8.0, 17.0, "blue"), (19.0, 15.0, "yellow")):
        P.append(box(C, (1.6, 2.0, 0.03), (x, y, Z0), c))
    # Baeume (Raender), Baenke, Laternen
    spots = []
    for x in range(-20, 21, 5):
        spots += [(x + rnd.uniform(-1, 1), -16.8 + rnd.uniform(-0.5, 0.5)), (x + rnd.uniform(-1, 1), 16.8 + rnd.uniform(-0.5, 0.5))]
    for y in (-8, -2, 4, 9):
        spots += [(-20.8 + rnd.uniform(-0.5, 0.5), y), (20.8 + rnd.uniform(-0.5, 0.5), y)]
    spots += [(-3.0, 7.0), (3.0, -6.0), (20.0, -5.0), (-4.0, -7.0)]
    for k, (x, y) in enumerate(spots):
        if abs(x) < 3.0 and y < -14.0:              # Eingang vorne frei lassen
            continue
        if 10.0 < x < 22.0 and 13.5 < y < 18.5 and k % 2:
            continue
        kind = "pine" if k % 4 == 0 else "round"
        P += tree(C, x, y, rnd.uniform(5.5, 8.5), rnd.uniform(1.6, 2.6), kind, seed=k,
                  leaf=rnd.choice(("green", "green", "green_dark")))
    for (x, y, r) in ((-6.0, -13.6, 0), (6.0, -13.6, 0), (17.6, 0.0, 90), (-17.6, -4.0, 270), (4.0, 13.6, 180),
                      (-4.0, 13.6, 180), (12.0, 1.0, 90)):
        P += bench(C, x, y, r)
    for (x, y) in ((-16.0, -12.0), (16.0, -12.0), (16.0, 12.0), (-16.0, 12.0), (0.0, -13.8), (0.0, 13.8)):
        P += lamp(C, x + 1.6, y - 1.6 if y > 0 else y + 1.6, 0, h=4.2)
    for (x, y) in ((1.8, -15.5), (17.8, 6.0)):
        P += trash_bin(C, x, y)
    for x in (-3.0, 3.0):
        P.append(box(C, (0.6, 0.6, 2.2), (x, -19.0, 0), "rock"))                       # Torpfeiler
    P.append(box(C, (7.0, 0.4, 0.5), (0.0, -19.0, 2.2), "wood_dark"))
    P += sign(C, 0.0, -19.2, 2.7, 3.0, 0.5, "green_dark", "white")
    return P


SV = {"Clinic": clinic, "PrimarySchool": primary_school, "HighSchool": high_school, "ParkSmall": park_small,
      "ParkLarge": park_large}
run_family(SV, "Services", views=V)
