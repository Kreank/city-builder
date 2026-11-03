"""Solarpark (UtilityCatalog "power_solar", Grundflaeche 40 x 32 m). Module neigen sich nach vorne (-Y)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "SolarPark"
fresh_scene()
C = collection(NAME)
P = []
Z0 = 0.12

P.append(box(C, (40, 32, Z0), (0, 0, 0), "grass"))
P.append(box(C, (5, 4.5, 0.04), (0, -13.7, Z0), "gravel"))
P.append(box(C, (40, 1.6, 0.04), (0, -11.4, Z0), "gravel"))   # Wartungsweg vor den Reihen

TILT = 25.0
MOD = 1.62          # Modulraster entlang X
COLS = 20
DEPTH = 1.62        # Moduletiefe (zwei Reihen hochkant)
rows_y = [-8.6 + i * 4.55 for i in range(6)]
up = Vector((0, math.cos(math.radians(TILT)), math.sin(math.radians(TILT))))
x0 = -(COLS - 1) * MOD / 2
for ry in rows_y:
    base = Vector((0, ry, 1.55))
    # Unterkonstruktion
    for k in range(6):
        x = -16.0 + k * 6.4
        lo = base - up * 1.45
        hi = base + up * 1.45
        P.append(box(C, (0.12, 0.12, lo.z - 0.15 - Z0), (x, lo.y, Z0), "metal_dark"))
        P.append(box(C, (0.12, 0.12, hi.z - 0.15 - Z0), (x, hi.y, Z0), "metal_dark"))
        P.append(beam(C, (x, lo.y, lo.z - 0.12), (x, hi.y, hi.z - 0.12), 0.1, "metal_dark"))
    P.append(box(C, (COLS * MOD + 0.2, 3.3, 0.05), (0, ry, 1.55 - 0.08), "metal", center=True, rot=(TILT, 0, 0)))
    # Module (2 Reihen)
    for s in (-0.5, 0.5):
        c = base + up * (s * (DEPTH + 0.04))
        for i in range(COLS):
            P.append(box(C, (MOD - 0.07, DEPTH - 0.05, 0.06), (x0 + i * MOD, c.y, c.z), "solar", center=True,
                         rot=(TILT, 0, 0)))
        # Zellraster-Andeutung: eine helle Linie je Modulreihe
        cl = c + Vector((0, 0, 0.035)).normalized() * 0.0
        P.append(box(C, (COLS * MOD - 0.1, 0.05, 0.065), (0, c.y, c.z + 0.004), "solar_line", center=True,
                     rot=(TILT, 0, 0)))

# Wechselrichter / Trafostation vorne
P.append(box(C, (4.0, 2.4, 2.5), (-1.2, -13.9, Z0), "offwhite", bevel=0.04))
P.append(box(C, (4.2, 2.6, 0.15), (-1.2, -13.9, Z0 + 2.5), "metal_dark"))
P.append(box(C, (0.9, 0.08, 2.0), (-2.2, -15.13, Z0), "metal_dark"))
for k in range(3):
    P.append(box(C, (0.8, 0.08, 0.5), (-0.2 + k * 0.0, -15.13, Z0 + 0.6 + k * 0.6), "metal"))
P.append(box(C, (1.8, 1.6, 1.8), (2.6, -13.9, Z0), "green_dark", bevel=0.04))
P.append(box(C, (0.6, 0.6, 0.1), (2.6, -13.9, Z0 + 1.8), "yellow"))   # Warnschild obendrauf
# Wetter-/Messmast
P.append(box(C, (0.1, 0.1, 4.0), (5.0, -13.0, Z0), "metal"))
P.append(box(C, (0.9, 0.5, 0.05), (5.0, -13.0, Z0 + 3.4), "solar", rot=(30, 0, 0)))

# Zaun mit Tor-Luecke vorne
fp = [(-19.7, -15.7), (-2.6, -15.7), (2.6, -15.7), (19.7, -15.7), (19.7, 15.7), (-19.7, 15.7)]
P += fence(C, fp, h=2.0, spacing=3.2, post="metal_dark", rail="metal", gaps=(1,))
for x in (-2.6, 2.6):
    P.append(box(C, (0.22, 0.22, 2.3), (x, -15.7, Z0), "metal_dark"))

body = join(flatten(P), NAME)
finish([body], NAME, views={"": (0.9, -1.2, 0.9), "_back": (-1.0, 1.0, 0.8)})
