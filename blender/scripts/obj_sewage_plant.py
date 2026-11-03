"""Klaeranlage (Vorschlag 32 x 32 m): Belebungsbecken, Nachklaerbecken mit Raeumerbruecke, Faultuerme (Eier)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def basin(C, cx, cy, w, d, water="teal"):
    P = [box(C, (w, d, 1.4), (cx, cy, 0), "concrete")]
    P.append(box(C, (w - 0.8, d - 0.8, 0.05), (cx, cy, 1.25), water))
    # Belueftungsblasen (helle Flecken)
    for i in range(int(w / 2.2)):
        for j in range(2):
            P.append(cyl(C, 0.35, 0.04, (cx - w / 2 + 1.4 + i * 2.2, cy - d / 4 + j * d / 2, 1.3), "blue_light", segs=6))
    # Gelaender laengs
    for s in (-1, 1):
        P.append(beam(C, (cx - w / 2, cy + s * (d / 2 - 0.1), 2.4), (cx + w / 2, cy + s * (d / 2 - 0.1), 2.4), 0.05,
                      "yellow"))
    for i in range(int(w / 2.5) + 1):
        for s in (-1, 1):
            P.append(box(C, (0.06, 0.06, 1.0), (cx - w / 2 + 0.1 + i * 2.5, cy + s * (d / 2 - 0.1), 1.4), "yellow"))
    return P


def clarifier(C, cx, cy, r):
    P = [lathe(C, [(0, 0), (r, 0), (r, 1.6), (r - 0.4, 1.6), (r - 0.4, 1.35), (0, 1.35)], (cx, cy, 0), segs=20,
               colors=["concrete", "concrete", "concrete", "concrete", "water"])]
    P.append(cyl(C, 0.7, 2.0, (cx, cy, 1.3), "concrete_dark", segs=8))
    P.append(box(C, (r * 2, 1.0, 0.3), (cx, cy, 2.2), "metal_dark"))           # Raeumerbruecke
    for s in (-1, 1):
        P.append(beam(C, (cx - r, cy + s * 0.5, 3.1), (cx + r, cy + s * 0.5, 3.1), 0.05, "yellow"))
    P.append(box(C, (0.6, 1.4, 1.4), (cx + r - 0.3, cy, 1.6), "orange"))     # Antrieb am Rand
    return P


def egg(C, x, y, r=3.4, h=15.0):
    prof = [(0, 0)] + [(r * math.sin(math.pi * k / 10) ** 0.85, h * (1 - math.cos(math.pi * k / 10)) / 2) for k in range(1, 10)] + [(0, h)]
    P = [lathe(C, prof, (x, y, 0.8), "metal", segs=14)]
    P.append(cyl(C, r * 0.75, 0.8, (x, y, 0), "concrete_dark", segs=10))
    P.append(cyl(C, 0.9, 0.6, (x, y, 0.8 + h - 0.1), "metal_dark", segs=8))
    return P


def make(C):
    P = [box(C, (32, 32, Z0), (0, 0, 0), "grass")]
    P.append(box(C, (30, 4, 0.04), (0, 0, Z0), "asphalt"))     # Fahrweg quer
    sub = []
    sub += basin(C, -5.0, -11.0, 20.0, 5.0)
    sub += basin(C, -5.0, -5.5, 20.0, 5.0)
    sub += clarifier(C, -9.5, 8.5, 5.6)
    sub += clarifier(C, 2.6, 8.5, 5.0)
    sub += egg(C, 12.0, 10.0)
    sub += egg(C, 12.0, 2.8, 3.0, 13.0)
    # Gasspeicher + Leitungen
    sub.append(sphere(C, 1.6, (12.5, -4.5, 1.6), "white", subdiv=2))
    sub += polyline_pipe(C, [(12.0, 6.2, 3.0), (12.0, 6.6, 3.0)], 0.25, "yellow")
    sub += polyline_pipe(C, [(5.5, -8.0, 1.0), (8.0, -8.0, 1.0), (8.0, 1.0, 1.0), (8.0, 4.0, 1.0)], 0.3, "pipe_green")
    # Betriebsgebaeude
    p, top = block(C, 11.0, -12.0, 8.0, 6.0, 2, 3.0, wall="offwhite", z0=0.35, spacing=2.4, win_size=(1.2, 1.3),
                   sides="flr", skip=lambda s, r, c: s == "f" and r == 0 and c == 1)
    sub += p
    sub.append(box(C, (8.1, 6.1, 0.5), (11.0, -12.0, 3.2), "teal"))
    sub += flat_roof(C, 8.0, 6.0, top, (11.0, -12.0), ph=0.4)
    sub += door(C, 11.0, -15.0, 1.4, 2.3, "glass_dark", z=0.35, canopy="teal")
    for o in sub:
        o.data.transform(Matrix.Translation((0, 0, Z0)))
    P += sub
    P += fence(C, [(5.5, -15.7), (-15.7, -15.7), (-15.7, 15.7), (15.7, 15.7), (15.7, -8.0)], h=1.8, spacing=3.2,
               closed=False)
    return P


build("SewagePlant", "Services", make)
