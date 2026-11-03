"""Park (G4-Dienst, Vorschlag 24 x 24 m): Wegekreuz, Brunnen, Beete, Baeume, Baenke, Laternen."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.15


def make(C):
    P = [box(C, (24, 24, Z0), (0, 0, 0), "grass")]
    P.append(box(C, (24, 2.4, 0.04), (0, 0, Z0), "gravel"))
    P.append(box(C, (2.4, 24, 0.04), (0, 0, Z0), "gravel"))
    P.append(cyl(C, 5.0, 0.05, (0, 0, Z0), "gravel", segs=20))
    # Brunnen
    P.append(lathe(C, [(0, 0), (3.0, 0), (3.0, 0.6), (2.6, 0.6), (2.6, 0.3), (0, 0.3)], (0, 0, Z0), "concrete", segs=16))
    P.append(cyl(C, 2.6, 0.5, (0, 0, Z0 + 0.05), "water", segs=16))
    P.append(cyl(C, 0.35, 1.6, (0, 0, Z0 + 0.3), "concrete", segs=8))
    P.append(lathe(C, [(0, 0), (0.4, 0), (1.2, 0.35), (1.25, 0.45), (0, 0.45)], (0, 0, Z0 + 1.9), "concrete", segs=12))
    P.append(cyl(C, 1.05, 0.05, (0, 0, Z0 + 2.3), "water", segs=12))
    P.append(cyl(C, 0.12, 0.7, (0, 0, Z0 + 2.3), "blue_light", segs=6, r_top=0.03))
    # Beete in den Quadranten (am Platz)
    for i, (sx, sy) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
        P += flower_bed(C, sx * 4.6, sy * 4.6, 2.4, 2.4, seed=i)
    # Baeume
    spots = [(-8.5, 8.5, "round"), (-5.0, 10.0, "pine"), (-10.0, 4.0, "round"), (8.5, 8.5, "pine"),
             (10.0, 4.5, "round"), (5.5, 10.0, "round"), (-8.5, -8.5, "round"), (-10, -4.0, "pine"),
             (8.5, -8.5, "round"), (10.0, -4.5, "round"), (5.0, -10.0, "pine"), (-5.0, -10.0, "round")]
    for k, (x, y, kind) in enumerate(spots):
        P += tree(C, x, y, 6.0 + (k % 3) * 0.8, 2.0 + (k % 2) * 0.4, kind, seed=k)
    # Baenke, Laternen, Muelleimer entlang der Wege
    for (x, y, r) in ((3.5, 1.7, 180), (-3.5, -1.7, 0), (1.7, -7.5, 90), (-1.7, 7.5, 270)):
        P += bench(C, x, y, r)
    for (x, y) in ((1.7, 9.5), (-1.7, -9.5), (9.5, -1.7), (-9.5, 1.7)):
        P += lamp(C, x, y)
    for (x, y) in ((1.7, -5.8), (-1.7, 5.8)):
        P += trash_bin(C, x, y)
    # niedrige Hecke als Einfassung (Luecken an den Wegen)
    for s in (-1, 1):
        P.append(hedge(C, (-11.8, s * 11.8), (-1.6, s * 11.8), 0.8, 0.6))
        P.append(hedge(C, (1.6, s * 11.8), (11.8, s * 11.8), 0.8, 0.6))
        P.append(hedge(C, (s * 11.8, -11.8), (s * 11.8, -1.6), 0.8, 0.6))
        P.append(hedge(C, (s * 11.8, 1.6), (s * 11.8, 11.8), 0.8, 0.6))
    return P


build("Park", "Services", make, views={"": (0.9, -1.1, 1.0), "_back": (-1.0, 1.0, 0.9)})
