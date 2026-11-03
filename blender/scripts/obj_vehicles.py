"""Fahrzeuge und Bahn (Models/Vehicles, Models/Rail).
Fahrzeuge: Fahrtrichtung = Vorderseite = -Y (Unity +Z), Ursprung = Boden unter der Fahrzeugmitte, Raeder auf z = 0.
Bahn: Spurweite 1435 mm, Schienenoberkante z = 0,62 (Gleis-Kachel), Schienenfahrzeuge stehen mit Radunterkante
auf z = 0,62 (direkt aufs Gleis setzbar). Gleis-Kachel 16 m entlang Y.
Bahnuebergang: Schranken "BarrierA" (links) / "BarrierB" (rechts) als eigene Objekte, Drehpunkt am Schrankenbock,
oeffnen um die lokale Y-Achse (Blender) = Unity -Z."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.1, 0.6)}
RAIL_Z = 0.62
GAUGE = 1.435


def wheels(C, ys, half, r=0.45, w=0.3, z=0.0, color="black"):
    """Radpaare bei y in ys; half = halbe Spurweite (Radmitte)."""
    out = []
    for y in ys:
        for s in (-1, 1):
            out.append(cyl(C, r, w, (s * half - w / 2, y, z + r), color, segs=10, rot=(0, 90, 0)))
            out.append(cyl(C, r * 0.5, w + 0.04, (s * half - w / 2 - 0.02, y, z + r), "metal", segs=8, rot=(0, 90, 0)))
    return out


def side_windows(C, W, y0, y1, z, h, color="glass_dark", n=None, gap=0.25):
    out = []
    if n is None:
        out += [box(C, (W + 0.04, y1 - y0, h), (0, (y0 + y1) / 2, z), color)]
        return out
    L = (y1 - y0 - gap * (n - 1)) / n
    for k in range(n):
        y = y0 + L / 2 + k * (L + gap)
        out.append(box(C, (W + 0.04, L, h), (0, y, z), color))
    return out


def lightbar(C, y, z, w=1.2, colors=("blue", "blue")):
    return [box(C, (w / 2, 0.3, 0.15), (-w / 4, y, z), colors[0]), box(C, (w / 2, 0.3, 0.15), (w / 4, y, z), colors[1])]


# ------------------------------------------------------------------ Strasse
def bus(C, color="yellow"):
    """Stadtbus 12 m (Niederflur), Tueren rechts (+X)."""
    W, L = 2.55, 12.0
    P = [box(C, (W, L, 2.6), (0, 0, 0.35), color, bevel=0.15),
         box(C, (W - 0.1, L - 0.2, 0.25), (0, 0, 2.95), "offwhite", bevel=0.08),
         box(C, (1.8, 3.5, 0.35), (0, 1.5, 3.2), "metal")]
    P += side_windows(C, W, -4.6, 5.6, 1.45, 1.15, n=6, gap=0.2)
    P.append(box(C, (W - 0.25, 0.08, 1.5), (0, -L / 2 - 0.02, 1.2), "glass_dark"))
    P.append(box(C, (1.6, 0.1, 0.3), (0, -L / 2 - 0.03, 2.75), "light_on"))
    P.append(box(C, (W - 0.4, 0.08, 0.9), (0, L / 2 + 0.02, 1.8), "glass_dark"))
    for y in (-4.9, 0.2):                                                   # Tueren rechts
        P.append(box(C, (0.06, 1.25, 2.3), (W / 2 + 0.02, y, 0.4), "glass"))
    for s in (-1, 1):
        P.append(box(C, (0.3, 0.06, 0.15), (s * 0.95, -L / 2 - 0.03, 0.6), "light_on"))
        P.append(box(C, (0.3, 0.06, 0.15), (s * 0.95, L / 2 + 0.03, 0.8), "red"))
    P.append(box(C, (W + 0.02, L - 0.4, 0.25), (0, 0, 0.55), "offwhite"))
    P += wheels(C, (-3.8, 2.9), W / 2 - 0.15, r=0.5, w=0.3)
    return P


def tram(C):
    """Niederflur-Strassenbahn (2 Teile, 30 m), Stromabnehmer auf dem Dach; Raeder auf Schienenhoehe z = 0."""
    W = 2.4
    P = []
    for k, (y0, y1) in enumerate(((-15.0, -0.3), (0.3, 15.0))):
        c = (y0 + y1) / 2
        L = y1 - y0
        P.append(box(C, (W, L, 2.7), (0, c, 0.35), "white", bevel=0.12))
        P.append(box(C, (W + 0.02, L - 0.3, 0.55), (0, c, 0.4), "red"))
        P.append(box(C, (W - 0.1, L - 0.3, 0.2), (0, c, 3.05), "offwhite"))
        P += side_windows(C, W, y0 + 1.6, y1 - 1.6, 1.25, 1.35, n=5, gap=0.25)
        for y in (c - 3.0, c + 3.0):
            P.append(box(C, (0.06, 1.3, 2.25), (W / 2 + 0.02, y, 0.38), "glass"))
            P.append(box(C, (0.06, 1.3, 2.25), (-W / 2 - 0.02, y, 0.38), "glass"))
        P += wheels(C, (y0 + 3.0, y1 - 3.0), GAUGE / 2, r=0.33, w=0.12, color="metal_dark")
    P.append(box(C, (W - 0.3, 0.7, 2.5), (0, 0, 0.4), "metal_dark"))           # Gelenk
    for y, s in ((-15.0, -1), (15.0, 1)):
        P.append(box(C, (W - 0.2, 0.08, 1.6), (0, y + s * 0.03, 1.2), "glass_dark"))
        P.append(box(C, (1.4, 0.1, 0.3), (0, y + s * 0.05, 2.75), "light_on"))
        for x in (-0.85, 0.85):
            P.append(box(C, (0.3, 0.06, 0.15), (x, y + s * 0.05, 0.75), "light_on" if s < 0 else "red"))
    # Stromabnehmer (Rautenform) auf Teil 1
    P.append(box(C, (1.2, 1.6, 0.3), (0, -7.0, 3.25), "metal_dark"))
    P.append(beam(C, (0, -7.6, 3.55), (0, -6.6, 4.6), 0.08, "metal_dark"))
    P.append(beam(C, (0, -6.6, 4.6), (0, -7.4, 5.4), 0.08, "metal_dark"))
    P.append(box(C, (1.6, 0.12, 0.08), (0, -7.4, 5.4), "metal"))
    return P


def fire_truck(C):
    """Loeschfahrzeug (Drehleiter, 10 m)."""
    W = 2.5
    P = [box(C, (W, 2.4, 2.6), (0, -3.8, 0.55), "red", bevel=0.12),
         box(C, (W - 0.2, 0.08, 1.0), (0, -5.0, 1.9), "glass_dark"),
         box(C, (W, 7.4, 2.4), (0, 1.2, 0.55), "red", bevel=0.08),
         box(C, (W + 0.02, 9.8, 0.3), (0, -1.1, 0.55), "white")]
    for y in (-1.6, 0.4, 2.4, 4.0):                                         # Rolllaeden
        for s in (-1, 1):
            P.append(box(C, (0.04, 1.6, 1.6), (s * (W / 2 + 0.01), y, 1.0), "offwhite"))
    P += lightbar(C, -4.2, 3.15)
    # Drehleiter
    P.append(cyl(C, 0.7, 0.4, (0, 3.2, 2.95), "metal_dark", segs=10))
    for s in (-1, 1):
        P.append(beam(C, (s * 0.45, 3.4, 3.4), (s * 0.45, -4.6, 3.6), 0.14, "metal"))
    for k in range(11):
        P.append(box(C, (0.9, 0.06, 0.06), (0, 3.2 - k * 0.75, 3.42 + k * 0.018), "metal"))
    P.append(box(C, (1.0, 0.8, 0.6), (0, 4.0, 3.35), "metal_dark"))
    P += wheels(C, (-3.6, 2.0, 3.4), W / 2 - 0.15, r=0.55, w=0.35)
    return P


def ambulance(C):
    """Rettungswagen (Kastenwagen, 7 m)."""
    P = [box(C, (2.1, 5.0, 2.5), (0, 0.8, 0.4), "white", bevel=0.1),
         box(C, (2.0, 1.8, 1.4), (0, -2.6, 0.4), "white", bevel=0.12),
         box(C, (1.9, 0.6, 0.9), (0, -1.7, 1.75), "white", bevel=0.05),
         box(C, (1.9, 0.08, 0.7), (0, -3.5, 1.15), "glass_dark"),
         box(C, (2.14, 6.6, 0.35), (0, -0.2, 1.1), "red"),
         box(C, (2.14, 6.6, 0.12), (0, -0.2, 1.55), "yellow")]
    P += side_windows(C, 2.0, -2.4, -1.4, 1.5, 0.6)
    P += lightbar(C, -1.5, 2.9, 1.4)
    P.append(box(C, (0.8, 0.12, 0.8), (0, 3.32, 1.7), "white"))
    P.append(box(C, (0.5, 0.14, 0.16), (0, 3.33, 2.05), "red"))
    P.append(box(C, (0.16, 0.14, 0.5), (0, 3.33, 1.88), "red"))
    P += wheels(C, (-2.3, 2.2), 0.95, r=0.42, w=0.25)
    return P


def sedan(C, color, extra=None):
    W = 1.8
    P = [box(C, (W, 4.5, 0.75), (0, 0, 0.3), color, bevel=0.14),
         box(C, (W - 0.2, 2.3, 0.6), (0, 0.25, 1.02), color, bevel=0.12),
         box(C, (W - 0.16, 2.34, 0.42), (0, 0.25, 1.08), "glass_dark")]
    for s in (-1, 1):
        P.append(box(C, (0.35, 0.06, 0.12), (s * 0.6, -2.26, 0.75), "light_on"))
        P.append(box(C, (0.35, 0.06, 0.12), (s * 0.6, 2.26, 0.8), "red"))
    P += wheels(C, (-1.4, 1.4), 0.78, r=0.34, w=0.24)
    if extra:
        P += extra
    return P


def police_car(C):
    """Streifenwagen (Limousine weiss/blau mit Lichtbalken)."""
    ex = [box(C, (1.84, 4.3, 0.22), (0, 0, 0.62), "blue")] + lightbar(C, 0.3, 1.62, 1.1)
    return sedan(C, "white", ex)


def taxi(C):
    """Taxi (Limousine hellelfenbein/gelb mit Dachschild)."""
    ex = [box(C, (0.7, 0.3, 0.25), (0, 0.3, 1.62), "yellow"), box(C, (0.5, 0.32, 0.1), (0, 0.3, 1.7), "black")]
    return sedan(C, "cream", ex)


def delivery_van(C):
    """Lieferwagen (Kastenwagen, 6 m)."""
    P = [box(C, (2.0, 4.2, 2.3), (0, 0.6, 0.4), "white", bevel=0.08),
         box(C, (1.95, 1.6, 1.3), (0, -2.3, 0.4), "white", bevel=0.12),
         box(C, (1.85, 0.6, 0.8), (0, -1.5, 1.6), "white", bevel=0.05),
         box(C, (1.85, 0.08, 0.65), (0, -3.15, 1.1), "glass_dark"),
         box(C, (2.04, 3.2, 0.9), (0, 0.8, 1.2), "orange")]
    P += side_windows(C, 1.95, -2.1, -1.3, 1.4, 0.6)
    P += wheels(C, (-2.0, 1.9), 0.9, r=0.38, w=0.24)
    return P


def garbage(C):
    """Muellwagen (aus den Gebaeudeteilen, Vorderseite -Y)."""
    return garbage_truck(C, 0, 0, 0)


# ------------------------------------------------------------------ Schiene
def bogie(C, y, z0=RAIL_Z):
    out = [box(C, (2.2, 2.8, 0.5), (0, y, z0 + 0.35), "metal_dark")]
    out += wheels(C, (y - 0.9, y + 0.9), GAUGE / 2 + 0.06, r=0.46, w=0.12, z=z0, color="metal_dark")
    return out


def buffers(C, y, s, z):
    return [cyl(C, 0.2, 0.5, (x, y, z), "metal_dark", segs=8, rot=(s * -90, 0, 0)) for x in (-0.9, 0.9)]


def locomotive(C):
    """E-Lok (19 m) mit zwei Fuehrerstaenden, Stromabnehmern, Lueftergittern."""
    W, L, z0 = 3.0, 19.0, RAIL_Z + 0.9
    P = [box(C, (W, L, 3.1), (0, 0, z0), "red", bevel=0.15),
         box(C, (W + 0.02, L - 1.0, 0.5), (0, 0, z0 + 0.2), "offwhite"),
         box(C, (W - 0.6, L - 4.0, 0.35), (0, 0, z0 + 3.1), "metal")]
    for s in (-1, 1):
        y = s * L / 2
        P.append(box(C, (W - 0.3, 0.08, 1.0), (0, y + s * 0.03, z0 + 1.7), "glass_dark"))
        for x in (-1.0, 1.0):
            P.append(box(C, (0.3, 0.06, 0.2), (x, y + s * 0.04, z0 + 0.9), "light_on" if s < 0 else "red"))
        P += side_windows(C, W, y - s * 1.6 - 0.4, y - s * 1.6 + 0.4, z0 + 1.8, 0.8)
        P += buffers(C, y, s, z0 + 0.1)
        P.append(box(C, (1.4, 1.2, 0.3), (0, s * 5.0, z0 + 3.45), "metal_dark"))
        P.append(beam(C, (0, s * 5.5, z0 + 3.75), (0, s * 4.5, z0 + 4.6), 0.07, "metal_dark"))
        P.append(beam(C, (0, s * 4.5, z0 + 4.6), (0, s * 5.3, z0 + 5.3), 0.07, "metal_dark"))
        P.append(box(C, (1.6, 0.12, 0.08), (0, s * 5.3, z0 + 5.3), "metal"))
    for k in range(6):
        y = -5.0 + k * 2.0
        P.append(box(C, (W + 0.04, 1.4, 0.9), (0, y, z0 + 1.5), "metal_dark"))
    P += bogie(C, -6.0) + bogie(C, 6.0)
    return P


def passenger_coach(C):
    """Reisezugwagen (26 m), Fensterband, Tueren an den Enden."""
    W, L, z0 = 2.9, 26.0, RAIL_Z + 0.95
    P = [box(C, (W, L, 3.0), (0, 0, z0), "offwhite", bevel=0.12),
         box(C, (W + 0.02, L - 0.4, 0.45), (0, 0, z0 + 0.6), "blue"),
         box(C, (W - 0.4, L - 1.0, 0.25), (0, 0, z0 + 3.0), "roof_light", bevel=0.08)]
    P += side_windows(C, W, -10.5, 10.5, z0 + 1.75, 1.0, n=12, gap=0.35)
    for s in (-1, 1):
        for x in (-1, 1):
            P.append(box(C, (0.06, 0.9, 2.1), (x * (W / 2 + 0.02), s * 12.0, z0 + 0.2), "metal_dark"))
        P += buffers(C, s * L / 2, s, z0 + 0.1)
        P.append(box(C, (1.2, 0.3, 2.2), (0, s * (L / 2 + 0.1), z0 + 0.2), "black"))
    P += bogie(C, -9.0) + bogie(C, 9.0)
    return P


def flat_wagon(C, L=19.6):
    z0 = RAIL_Z + 0.95
    P = [box(C, (2.7, L, 0.35), (0, 0, z0), "metal_dark")]
    for s in (-1, 1):
        P += buffers(C, s * L / 2, s, z0 - 0.15)
    P += bogie(C, -L / 2 + 3.0) + bogie(C, L / 2 - 3.0)
    return P, z0 + 0.35


def container_wagon(C):
    """Containertragwagen (19,6 m) mit drei 20-Fuss-Containern."""
    P, top = flat_wagon(C)
    for y, col in ((-6.3, "blue"), (0.0, "orange"), (6.3, "green_dark")):
        P += container(C, 0, y, top, 0, col)
    return P


def tank_wagon(C):
    """Kesselwagen (16 m)."""
    P, top = flat_wagon(C, 16.0)
    P.append(cyl(C, 1.45, 13.0, (0, -6.5, top + 1.45), "white", segs=14, rot=(-90, 0, 0)))
    for s in (-1, 1):
        P.append(sphere(C, 1.45, (0, s * 6.5, top + 1.45), "white", subdiv=2, scale=(1, 0.35, 1)))
        P.append(box(C, (2.6, 0.4, 0.9), (0, s * 4.0, top - 0.1), "metal_dark"))
    P.append(cyl(C, 0.45, 0.4, (0, 0, top + 2.85), "metal_dark", segs=10))
    P.append(cyl(C, 1.47, 0.6, (0, -0.3, top + 1.45), "orange", segs=14, rot=(-90, 0, 0)))
    P.append(box(C, (2.0, 0.1, 0.1), (0, 0, top + 3.0), "yellow"))
    return P


def track(C, L=16.0, catenary=True):
    """Gleis gerade (Kachel 16 m entlang Y): Schotterbett, Betonschwellen, Schienen, optional Oberleitungsmast."""
    P = [prism(C, [(-2.2, 0.0), (2.2, 0.0), (1.5, 0.35), (-1.5, 0.35)], L, (0, 0, 0), "gravel", name="ballast")]
    P[-1].data.transform(Matrix.Translation((0, L / 2, 0)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0),
                                                                     (0, 0, 0, 1))))
    n = int(L / 0.6)
    for k in range(n):
        P.append(box(C, (2.6, 0.26, 0.14), (0, -L / 2 + 0.3 + k * L / n, 0.33), "concrete"))
    for s in (-1, 1):
        P.append(box(C, (0.08, L, 0.15), (s * GAUGE / 2, 0, 0.47), "metal"))
        P.append(box(C, (0.16, L, 0.04), (s * GAUGE / 2, 0, 0.45), "metal_dark"))
    if catenary:
        P.append(box(C, (0.6, 0.6, 0.5), (3.0, 0, 0), "concrete"))
        P.append(box(C, (0.25, 0.25, 6.5), (3.0, 0, 0.5), "metal"))
        P.append(beam(C, (3.0, 0, 6.6), (-0.6, 0, 6.6), 0.12, "metal"))
        P.append(beam(C, (3.0, 0, 5.6), (-0.4, 0, 6.6), 0.06, "metal"))
        P.append(box(C, (0.04, L, 0.04), (0, 0, 5.6), "metal_dark"))
        P.append(box(C, (0.04, L, 0.04), (0, 0, 6.4), "metal_dark"))
        P.append(cyl(C, 0.08, 0.5, (-0.3, 0, 6.0), "glass_light", segs=6))
    return P


def track_straight(C):
    """Gleis gerade 16 m mit Oberleitung."""
    return track(C)


def level_crossing(C):
    """Bahnuebergang (16 x 16 m): Gleis entlang Y, Strasse (8 m) entlang X, Gummiplatten, Andreaskreuze,
    Blinklichter, zwei Halbschranken (BarrierA links vorne, BarrierB rechts hinten)."""
    P = track(C, 16.0, catenary=False)
    P.append(box(C, (16.0, 8.0, 0.4), (0, 0, 0.12), "asphalt"))
    P.append(box(C, (3.2, 8.0, 0.08), (0, 0, 0.52), "black"))
    for s in (-1, 1):
        P.append(box(C, (0.08, 8.0, 0.06), (s * GAUGE / 2, 0, 0.56), "metal"))
        for x in (-6.0, -3.5, 3.5, 6.0):
            P.append(box(C, (1.2, 0.15, 0.02), (x, s * 3.85, 0.52), "white"))
    out = {}
    for name, sx, sy in (("BarrierA", -1, -1), ("BarrierB", 1, 1)):
        bx, by = sx * 3.6, sy * 4.6
        P.append(box(C, (0.6, 0.6, 1.2), (bx, by, 0.0), "white"))
        P.append(box(C, (0.4, 0.4, 0.8), (bx, by, 1.2), "red"))
        # Andreaskreuz + Blinklicht
        P.append(box(C, (0.12, 0.12, 3.2), (bx, by - sy * 0.8, 0.0), "metal"))
        for a in (35, -35):
            P.append(box(C, (1.6, 0.06, 0.25), (bx, by - sy * 0.85, 2.8), "red", rot=(0, a, 0), center=True))
            P.append(box(C, (1.5, 0.07, 0.12), (bx, by - sy * 0.86, 2.8), "white", rot=(0, a, 0), center=True))
        P.append(box(C, (0.5, 0.2, 0.5), (bx, by - sy * 0.85, 2.0), "black"))
        P.append(cyl(C, 0.15, 0.05, (bx, by - sy * 0.95, 2.25), "red", segs=8, rot=(90 * sy, 0, 0)))
        # Schrankenbaum (geschlossen quer ueber die halbe Strasse, entlang -sx), Drehpunkt am Bock
        pivot = (bx, by, 1.7)
        arm = []
        for k in range(8):
            arm.append(box(C, (0.5, 0.14, 0.14), (bx - sx * (0.45 + k * 0.5), by, 1.63),
                           "red" if k % 2 == 0 else "white"))
        arm.append(box(C, (0.6, 0.3, 0.3), (bx + sx * 0.4, by, 1.55), "metal_dark"))       # Gegengewicht
        out[name] = (arm, pivot)
    res = {"LevelCrossing": (P, (0, 0, 0))}
    res.update(out)
    return res


VEH = {"Bus": bus, "Tram": (tram, "Rail"), "FireTruck": fire_truck, "Ambulance": ambulance, "PoliceCar": police_car,
       "Taxi": taxi, "DeliveryVan": delivery_van, "GarbageTruck": garbage,
       "Locomotive": (locomotive, "Rail"), "PassengerCoach": (passenger_coach, "Rail"),
       "ContainerWagon": (container_wagon, "Rail"), "TankWagon": (tank_wagon, "Rail"),
       "Track_Straight": (track_straight, "Rail"), "LevelCrossing": (level_crossing, "Rail")}
run_family(VEH, "Vehicles", views=V)
