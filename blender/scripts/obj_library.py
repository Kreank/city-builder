"""Bibliothek (G4-Dienst, Vorschlag 16 x 16 m): klassizistischer Bau mit Saeulenportikus."""
import sys, os
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def make(C):
    P = [box(C, (16, 16, Z0), (0, 0, 0), "concrete")]
    # Sockel + Baukoerper
    cx, cy, w, d = 0, 2.2, 12, 9.6
    P.append(box(C, (w + 0.6, d + 0.6, 1.0), (cx, cy, Z0), "beige"))
    P.append(box(C, (w, d, 6.4), (cx, cy, Z0 + 1.0), "sand"))
    P.append(box(C, (w + 0.4, d + 0.4, 0.6), (cx, cy, Z0 + 7.4), "cream"))
    for s in "lrb":
        P += facade(C, cx, cy, w, d, [Z0 + 4.2], 2.4, (1.1, 3.4), s, "glass", frame="cream", margin=1.2)
    P.append(hip(C, w + 0.4, d + 0.4, 2.2, (cx, cy, Z0 + 8.0), "roof_green", overhang=0.1))
    # Freitreppe
    for k in range(4):
        P.append(box(C, (9.6 - k * 0.2, 3.6 - k * 0.6, 0.25), (cx, cy - d / 2 - 1.8 + k * 0.3, Z0 + k * 0.25), "cream"))
    # Portikus: Saeulen, Gebaelk, Giebel
    py = cy - d / 2 - 1.6
    for i in range(6):
        x = -3.8 + i * 1.52
        P.append(box(C, (0.8, 0.8, 0.3), (x, py, Z0 + 1.0), "cream"))
        P.append(cyl(C, 0.32, 5.1, (x, py, Z0 + 1.3), "white", segs=10, r_top=0.27))
        P.append(box(C, (0.8, 0.8, 0.3), (x, py, Z0 + 6.4), "cream"))
    P.append(box(C, (9.2, 3.4, 0.9), (cx, py + 0.9, Z0 + 6.7), "cream"))
    P += gable(C, 3.4, 9.2, 1.6, (cx, py + 0.9, Z0 + 7.6), "roof_green", overhang=0.15, rot=(0, 0, 90),
               gable_color="cream")
    P.append(box(C, (1.8, 0.12, 3.6), (cx, cy - d / 2 - 0.04, Z0 + 1.0), "wood_dark"))
    P.append(box(C, (2.3, 0.15, 0.3), (cx, cy - d / 2 - 0.08, Z0 + 4.6), "gold"))
    for x in (-2.2, 2.2):
        P += window_row(C, (x, cy - d / 2, Z0 + 3.3), (1, 0, 0), 1, 1, (1.0, 2.6), normal=(0, -1, 0), frame="cream")
    # Vorplatz: Laternen, Baenke, Baeume
    P += lamp(C, -5.6, -6.0)
    P += lamp(C, 5.6, -6.0)
    P += bench(C, -4.2, -7.0, 0)
    P += bench(C, 4.2, -7.0, 0)
    P += tree(C, -6.6, 4.0, 5.5, 1.6, "pine")
    P += tree(C, 6.6, 4.0, 5.5, 1.6, "pine")
    return P


build("Library", "Services", make)
