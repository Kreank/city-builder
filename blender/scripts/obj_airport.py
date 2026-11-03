"""Flughafen (Models/Airport). Terminal: Landseite (Vorfahrt) = -Y, Vorfeld/Fluggastbruecken = +Y.
Flugzeuge: Nase nach -Y, Ursprung = Boden unter der Rumpfmitte. Bahnkacheln 64 x 48 m (Bahn entlang X)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.2, 0.7)}
RX = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # Profil (y,z) -> extrudiert entlang X


def airliner(C, L=38.0, span=34.0, livery="blue"):
    P = []
    s = L / 38.0
    prof = [(0, 0), (1.0 * s, 0.8 * s), (1.8 * s, 3.0 * s), (2.0 * s, 6.0 * s), (2.0 * s, 27.0 * s),
            (1.6 * s, 32.0 * s), (0.9 * s, 36.0 * s), (0.35 * s, 37.6 * s), (0, 38.0 * s)]
    f = lathe(C, prof, (0, 0, 0), segs=12, colors=["glass_dark", "white", "white", "white", "white", "white", "white",
                                                     livery])
    zc = 3.6 * s
    f.data.transform(Matrix.Translation((0, -L / 2, zc)) @ Matrix.Rotation(math.radians(-90), 4, 'X'))
    P.append(f)
    for sx in (-1, 1):   # Fensterband + Zierlinie
        P.append(box(C, (0.06, 20.0 * s, 0.35 * s), (sx * 1.98 * s, -L / 2 + 17.0 * s, zc + 0.5 * s), "glass_dark",
                     center=True))
        P.append(box(C, (0.06, 26.0 * s, 0.3 * s), (sx * 1.97 * s, -L / 2 + 18.0 * s, zc - 0.4 * s), livery, center=True))
    # Fluegel (gepfeilt), Triebwerke
    for sx in (-1, 1):
        wing = [(0.0, -1.5), (span / 2, 7.0), (span / 2, 8.6), (0.0, 5.5)]
        pts = [(sx * x * s, (y - 1.0) * s) for x, y in wing]
        if sx < 0:
            pts = pts[::-1]
        P.append(prism(C, pts, 0.4 * s, (0, 0, zc - 1.2 * s), "white"))
        P.append(box(C, (0.3 * s, 1.6 * s, 1.2 * s), (sx * span / 2 * s, 7.2 * s, zc - 1.0 * s), livery))   # Winglet
        e = cyl(C, 0.95 * s, 3.6 * s, (0, 0, 0), "metal", segs=10, r_top=0.8 * s)
        e.data.transform(Matrix.Translation((sx * 5.8 * s, -2.6 * s, zc - 2.3 * s)) @ Matrix.Rotation(math.radians(-90), 4, 'X'))
        P.append(e)
        P.append(cyl(C, 0.7 * s, 0.06, (sx * 5.8 * s, -2.62 * s, zc - 2.3 * s), "black", segs=10, rot=(90, 0, 0)))
        P.append(box(C, (0.3, 1.4 * s, 1.2 * s), (sx * 5.8 * s, -1.0 * s, zc - 1.5 * s), "white"))
    # Leitwerk
    fin = prism(C, [(13.0, 0), (17.5, 0), (18.5, 7.0), (16.6, 7.0)], 0.35 * s, (0, 0, 0), livery)
    fin.data.transform(Matrix.Translation((-0.17 * s, 0, zc + 1.4 * s)) @ RX @ Matrix.Scale(s, 4))
    P.append(fin)
    for sx in (-1, 1):
        st = [(0.0, 15.0), (6.0, 17.6), (6.0, 18.6), (0.0, 18.4)]
        pts = [(sx * x * s, y * s) for x, y in st]
        if sx < 0:
            pts = pts[::-1]
        P.append(prism(C, pts, 0.25 * s, (0, 0, zc + 0.6 * s), "white"))
    # Fahrwerk
    P.append(cyl(C, 0.12, zc - 1.6 * s, (0, -L / 2 + 4.0 * s, 0.5), "metal_dark", segs=6))
    P.append(cyl(C, 0.45, 0.4, (-0.2, -L / 2 + 4.0 * s, 0.45), "black", segs=8, rot=(0, 90, 0)))
    for sx in (-1, 1):
        P.append(cyl(C, 0.15, zc - 1.8 * s, (sx * 3.0 * s, 1.5 * s, 0.6), "metal_dark", segs=6))
        P.append(cyl(C, 0.6, 0.8, (sx * 3.0 * s - 0.4, 1.5 * s, 0.6), "black", segs=8, rot=(0, 90, 0)))
    return P


def small_plane(C):
    """Einmotoriges Kleinflugzeug (Hochdecker), 8 m lang, 11 m Spannweite."""
    P = [box(C, (1.2, 6.5, 1.4), (0, -0.6, 0.8), "white", bevel=0.25),
         box(C, (1.1, 1.8, 0.8), (0, -1.8, 2.15), "glass_dark", bevel=0.15),
         lathe(C, [(0, 0), (0.55, 0.0), (0.5, 0.6), (0, 0.9)], (0, -3.85, 1.5), "red", segs=8, rot=(90, 0, 0)),
         box(C, (11.0, 1.6, 0.18), (0, -1.6, 2.6), "white"),
         box(C, (11.05, 0.4, 0.2), (0, -2.3, 2.6), "red"),
         box(C, (0.6, 3.0, 0.5), (0, 3.4, 1.4), "white"),
         box(C, (3.4, 1.0, 0.12), (0, 4.4, 1.5), "white")]
    fin = prism(C, [(3.6, 0), (5.0, 0), (5.0, 1.8), (4.4, 1.8)], 0.12, (0, 0, 0), "red")
    fin.data.transform(Matrix.Translation((-0.06, 0, 1.6)) @ RX)
    P.append(fin)
    P.append(box(C, (0.08, 0.2, 2.6), (0, -4.0, 0.25), "black"))   # Propeller
    for sx in (-1, 1):
        P.append(beam(C, (sx * 0.6, -1.2, 1.0), (sx * 1.8, -1.6, 2.55), 0.08, "metal"))
        P.append(cyl(C, 0.3, 0.2, (sx * 1.0 - 0.1, -1.0, 0.3), "black", segs=8, rot=(0, 90, 0)))
    P.append(cyl(C, 0.22, 0.15, (-0.07, -3.2, 0.22), "black", segs=8, rot=(0, 90, 0)))
    return P


def terminal(C):
    """Terminal 64 x 32 m: Glasfassade, Wellendach, Vorfahrt mit Vordach, drei Fluggastbruecken nach +Y."""
    P = [box(C, (64.0, 32.0, 0.2), (0, 0, 0), "concrete"),
         box(C, (64.0, 8.0, 0.05), (0, -12.0, 0.2), "asphalt")]
    w, d, h = 60.0, 16.0, 12.0
    P.append(box(C, (w, d, h), (0, 0, 0.2), "glass_light"))
    for k in range(13):
        P.append(box(C, (0.5, d + 0.2, h), (-w / 2 + 0.25 + k * (w - 0.5) / 12, 0, 0.2), "white"))
    P.append(box(C, (w + 0.2, d + 0.2, 0.6), (0, 0, 6.2), "white"))
    # Wellendach: geneigte Paneele abwechselnd
    for k in range(6):
        x = -w / 2 + 5.0 + k * 10.0
        P.append(box(C, (10.2, d + 4.0, 0.4), (x, 0, h + 1.4), "metal", rot=(0, 7 if k % 2 else -7, 0), center=True))
    P.append(box(C, (w - 4, d - 4, 1.0), (0, 0, h + 0.2), "metal_dark"))
    # Vorfahrt
    P.append(box(C, (44.0, 6.0, 0.4), (0, -10.5, 5.0), "white"))
    for k in range(6):
        P.append(cyl(C, 0.25, 4.8, (-20.0 + k * 8.0, -13.0, 0.2), "metal", segs=8))
    P += sign(C, 0, -8.1, 9.5, 18.0, 1.4, "steel_blue", "white")
    for k in range(4):
        P += taxi_car(C, -12.0 + k * 6.0, -14.0)
    # Fluggastbruecken
    for x in (-18.0, 0.0, 18.0):
        P.append(box(C, (3.0, 12.0, 3.0), (x, 14.0, 4.5), "metal"))
        P.append(box(C, (3.4, 3.4, 3.4), (x, 20.5, 4.3), "metal_dark"))
        P.append(box(C, (0.8, 0.8, 4.5), (x, 19.0, 0.2), "metal_dark"))
        P.append(box(C, (2.4, 1.0, 0.8), (x, 19.0, 0.2), "black"))
    P.append(box(C, (64.0, 0.4, 0.06), (0, 12.0, 0.2), "yellow"))
    return P


def taxi_car(C, x, y):
    return parked_car(C, x, y, 90, "yellow")


def control_tower(C):
    """Tower 12 x 12 m, 42 m: Schaft, Kanzel mit Rundumverglasung, Radar."""
    P = [box(C, (12.0, 12.0, 0.3), (0, 0, 0), "concrete"),
         box(C, (10.0, 8.0, 4.0), (0, 1.0, 0.3), "white")]
    P += ribbon(C, 0, 1.0, 10.0, 8.0, 2.4, 1.6, "glass_dark", "flr")
    P.append(lathe(C, [(0, 0), (3.0, 0), (2.4, 4.0), (2.2, 34.0), (0, 34.0)], (0, 0, 0.3), segs=12,
                   colors=["concrete", "offwhite", "offwhite", "offwhite"]))
    P.append(lathe(C, [(0, 0), (2.4, 0), (4.6, 1.6), (5.0, 1.8), (5.0, 2.1), (5.6, 4.8), (5.2, 5.2), (4.8, 5.4), (0, 5.4)],
                   (0, 0, 34.3), segs=12, colors=["offwhite", "white", "white", "white", "glass_dark", "white", "white",
                                                   "roof_dark"]))
    P.append(cyl(C, 0.2, 3.0, (0, 0, 39.7), "metal", segs=6))
    P.append(box(C, (3.0, 0.4, 0.8), (0, 0, 42.7), "white"))
    P.append(sphere(C, 0.25, (0, 0, 43.6), "red", subdiv=1))
    return P


def hangar(C):
    """Bogenhangar 40 x 32 m mit grossem Schiebetor nach -Y, Kleinflugzeug davor."""
    P = [box(C, (40.0, 32.0, 0.2), (0, 0, 0), "concrete")]
    arc = [(14.0 * math.cos(math.pi * k / 10), 2.0 + 12.0 * math.sin(math.pi * k / 10)) for k in range(11)]
    sec = [(-14.0, 0.2)] + arc[::-1] + [(14.0, 0.2)]
    # Querschnitt im XZ-Profil -> entlang Y extrudiert
    shell = prism(C, sec, 26.0, (0, 0, 0), "metal")
    shell.data.transform(Matrix.Translation((0, 13.0 - 2.0, 0)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(shell)
    P.append(box(C, (24.0, 0.3, 12.0), (0, -15.05, 0.2), "steel_blue"))
    for k in range(6):
        P.append(box(C, (0.2, 0.32, 12.0), (-10.0 + k * 4.0, -15.1, 0.2), "metal_dark"))
    P += sign(C, 0, -15.2, 12.6, 10.0, 1.0, "white", "steel_blue")
    for o in small_plane(C):
        o.data.transform(Matrix.Translation((8.0, -12.0, 0.2)) @ Matrix.Rotation(math.radians(200), 4, 'Z'))
        P.append(o)
    return P


def runway(C, threshold=False):
    """Bahnkachel 64 x 48 m (Bahn 45 m breit entlang X), Mittellinie, Randlinien, Randfeuer."""
    P = [box(C, (64.0, 48.0, 0.1), (0, 0, 0), "grass"), box(C, (64.0, 45.0, 0.08), (0, 0, 0.1), "asphalt")]
    for s in (-1, 1):
        P.append(box(C, (64.0, 0.9, 0.02), (0, s * 21.5, 0.18), "white"))
        for k in range(4):
            P.append(cyl(C, 0.2, 0.4, (-24.0 + k * 16.0, s * 23.2, 0.1), "yellow", segs=6))
    if threshold:
        for k in range(12):
            y = -18.0 + k * (36.0 / 11)
            P.append(box(C, (30.0, 1.8, 0.02), (-14.0, y, 0.18), "white"))
        P.append(box(C, (1.2, 45.0, 0.02), (-31.0, 0, 0.18), "white"))
        for s in (-1, 1):
            P.append(box(C, (12.0, 4.0, 0.02), (14.0, s * 8.0, 0.18), "white"))   # Aufsetzzone
    else:
        for k in range(3):
            P.append(box(C, (12.0, 0.9, 0.02), (-24.0 + k * 22.0, 0, 0.18), "white"))
        for s in (-1, 1):
            for k in range(2):
                P.append(box(C, (10.0, 2.0, 0.02), (-15.0 + k * 30.0, s * 9.0, 0.18), "white"))
    return P


def taxiway(C):
    """Rollweg-Kachel 32 x 32 m (entlang X), gelbe Mittellinie, blaue Randfeuer."""
    P = [box(C, (32.0, 32.0, 0.1), (0, 0, 0), "grass"), box(C, (32.0, 23.0, 0.08), (0, 0, 0.1), "asphalt"),
         box(C, (32.0, 0.3, 0.02), (0, 0, 0.18), "yellow")]
    for s in (-1, 1):
        P.append(box(C, (32.0, 0.3, 0.02), (0, s * 10.8, 0.18), "yellow"))
        for k in range(3):
            P.append(cyl(C, 0.18, 0.4, (-10.0 + k * 10.0, s * 12.0, 0.1), "blue", segs=6))
    return P


def windsock(C):
    P = [box(C, (1.2, 1.2, 0.2), (0, 0, 0), "concrete"), cyl(C, 0.08, 6.0, (0, 0, 0.2), "metal", segs=6)]
    # durchgehender Schlauch (5 Baender), entlang +X, leicht haengend
    prof = [(0.5 - k * 0.07, k * 0.7) for k in range(6)]
    sock = lathe(C, prof, (0, 0, 0), segs=8, colors=["orange", "white", "orange", "white", "orange"])
    sock.data.transform(Matrix.Translation((0.1, 0, 5.9)) @ Matrix.Rotation(math.radians(82), 4, 'Y'))
    P.append(sock)
    P.append(cyl(C, 0.52, 0.06, (0, 0, 0), "metal", segs=8))   # Haltering
    P[-1].data.transform(Matrix.Translation((0.1, 0, 5.9)) @ Matrix.Rotation(math.radians(82), 4, 'Y'))
    return P


def baggage_train(C):
    P = [box(C, (1.6, 2.6, 1.0), (0, -2.6, 0.4), "yellow", bevel=0.08), box(C, (1.4, 0.8, 0.9), (0, -2.0, 1.4), "glass_dark")]
    for k in range(3):
        y = 0.6 + k * 2.6
        P.append(box(C, (1.6, 2.2, 0.15), (0, y, 0.5), "metal_dark"))
        P.append(box(C, (1.5, 2.0, 1.4), (0, y, 0.65), ("blue", "red", "teal")[k], bevel=0.05))
    for y in (-3.4, -1.8, 0.0, 1.2, 2.6, 3.8, 5.2, 6.4):
        P.append(cyl(C, 0.25, 1.7, (-0.85, y, 0.25), "black", segs=8, rot=(0, 90, 0)))
    return P


AIR = {"Terminal": terminal, "ControlTower": control_tower, "Hangar": hangar,
       "Airliner": lambda C: airliner(C), "Airliner_Red": lambda C: airliner(C, livery="red"),
       "SmallPlane": small_plane, "Runway": lambda C: runway(C), "Runway_End": lambda C: runway(C, True),
       "Taxiway": taxiway, "Windsock": windsock, "BaggageTrain": baggage_train}
run_family(AIR, "Airport", views=V)
