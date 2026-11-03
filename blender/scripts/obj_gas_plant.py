"""Gaskraftwerk / GuD (UtilityCatalog "power_gas", Grundflaeche 36 x 28 m)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "GasPlant"
fresh_scene()
C = collection(NAME)
P = []
Z0 = 0.3

P.append(box(C, (36, 28, Z0), (0, 0, 0), "concrete"))
P.append(box(C, (10, 5, 0.04), (-6, -11.3, Z0), "asphalt"))

# ---------------- Maschinenhaus
hx, hy, hw, hd, hh = -6, -5.0, 20, 11, 9
P.append(box(C, (hw, hd, hh), (hx, hy, Z0), "offwhite"))
P.append(box(C, (hw + 0.1, hd + 0.1, 1.0), (hx, hy, Z0 + 1.0), "steel_blue"))
P.append(box(C, (hw + 0.1, hd + 0.1, 0.5), (hx, hy, Z0 + hh - 1.0), "teal"))
P.append(box(C, (hw + 0.3, hd + 0.3, 0.5), (hx, hy, Z0 + hh), "metal_dark"))
P += window_row(C, (hx - 8, hy - hd / 2, 5.3), (1, 0, 0), 9, 2.0, (1.4, 2.6), normal=(0, -1, 0))
P.append(box(C, (4.0, 0.12, 4.4), (hx + 5.5, hy - hd / 2 - 0.05, Z0), "metal_dark"))
P.append(box(C, (1.0, 0.12, 2.1), (hx - 5, hy - hd / 2 - 0.05, Z0), "glass_dark"))
P.append(box(C, (3.6, 0.15, 1.0), (hx - 5, hy - hd / 2 - 0.08, 7.4), "teal"))  # Logo-Tafel
# Ansaugfilter auf dem Dach (graue Kaesten mit Lamellen)
for x in (hx - 5, hx + 4):
    P.append(box(C, (5.0, 3.6, 3.2), (x, hy - 2.0, Z0 + hh + 0.5), "metal"))
    for k in range(4):
        P.append(box(C, (4.6, 0.1, 0.25), (x, hy - 3.85, Z0 + hh + 1.1 + k * 0.6), "metal_dark"))
    P.append(box(C, (2.2, 3.2, 1.6), (x, hy + 1.2, Z0 + hh + 0.5), "metal_dark"))

# ---------------- Abhitzekessel + Kamine (zwei Bloecke)
for x in (hx - 5, hx + 4):
    P.append(box(C, (5.6, 9.0, 13.0), (x, 5.0, Z0), "metal"))
    for k in range(5):   # Stahlrahmen
        P.append(box(C, (5.8, 0.35, 13.0), (x, 0.7 + k * 2.15, Z0), "metal_dark"))
    P.append(box(C, (6.0, 9.4, 0.5), (x, 5.0, Z0 + 13.0), "metal_dark"))
    P.append(box(C, (3.0, 2.8, 2.8), (x, 10.4, Z0 + 9.6), "metal_dark"))              # Rauchgaskanal
    P.append(lathe(C, [(0, Z0), (1.55, Z0), (1.45, 27.0), (1.6, 27.2), (1.6, 28.4), (1.2, 28.4), (1.2, 28.0), (0, 28.0)],
                   (x, 12.0, 0), segs=12,
                   colors=["metal", "metal", "metal_dark", "metal_dark", "black", "black", "black"]))
    P.append(lathe(C, [(1.45, 24.5), (1.52, 24.5), (1.52, 25.6), (1.45, 25.6)], (x, 12.0, 0), "red", segs=12))
    # Treppenturm/Bühnen
    P.append(box(C, (1.4, 1.4, 13.0), (x + 3.5, 0.9, Z0), "yellow"))

# ---------------- Gas-Kugelspeicher + Leitungen
for sy in (7.0, -1.8):
    sx = 11.5
    P.append(sphere(C, 3.6, (sx, sy, 5.6), "white", subdiv=2))
    P.append(lathe(C, [(3.25, 4.9), (3.62, 4.9), (3.62, 5.4), (3.25, 5.4)], (sx, sy, 0.7), "steel_blue", segs=16))
    for i in range(6):
        a = math.radians(30 + 60 * i)
        P.append(pipe(C, (sx + 3.3 * math.cos(a), sy + 3.3 * math.sin(a), Z0),
                      (sx + 3.3 * math.cos(a), sy + 3.3 * math.sin(a), 5.0), 0.2, "steel_blue", segs=6))
    P.append(cyl(C, 0.22, 0.6, (sx, sy, 9.1), "metal_dark", segs=6))
    P += polyline_pipe(C, [(sx - 2.6, sy, 1.3), (6.5, sy, 1.3), (6.5, -3.0, 1.3), (hx + hw / 2 - 0.1, -3.0, 1.3)],
                       0.32, "yellow")
P.append(box(C, (1.6, 1.2, 1.6), (6.5, 2.6, Z0), "yellow", bevel=0.05))   # Gasdruckregler

# ---------------- Trafos vorne rechts
for i, x in enumerate((7.5, 12.0)):
    P.append(box(C, (3.0, 2.4, 2.6), (x, -10.5, Z0), "metal_dark", bevel=0.06))
    P.append(box(C, (3.2, 0.35, 2.0), (x, -11.9, Z0 + 0.3), "metal"))
    for dx in (-0.8, 0, 0.8):
        P.append(cyl(C, 0.14, 1.2, (x + dx, -10.5, Z0 + 2.6), "white", segs=6))
P += fence(C, [(5.0, -13.6), (14.6, -13.6), (14.6, -7.6), (5.0, -7.6)], h=1.6, spacing=2.4)

body = join(flatten(P), NAME)
finish([body], NAME, views={"": (1.0, -1.1, 0.8), "_back": (-1.0, 1.0, 0.75)})
