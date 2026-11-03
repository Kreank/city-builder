"""Kohlekraftwerk (UtilityCatalog "power_coal", Grundflaeche 48 x 36 m).
Objekte: "CoalPlant" (alles Feste) und "Steam" (Dampfwolke ueber dem Kuehlturm, separat ein-/ausblendbar)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "CoalPlant"
fresh_scene()
C = collection(NAME)
P = []
Z0 = 0.3

# Gelaende
P.append(box(C, (48, 36, Z0), (0, 0, 0), "concrete"))
P.append(box(C, (14, 7, 0.04), (-4, -14.5, Z0), "asphalt"))                       # Hof vor dem Tor
P.append(box(C, (16, 10, 0.04), (14, -11, Z0), "gravel"))                         # Kohlelager

# ---------------- Kesselhaus (Ziegel, hoch)
bx, by, bw, bd, bh = -10, 8, 14, 12, 20
P.append(box(C, (bw, bd, bh), (bx, by, Z0), "brick"))
P.append(box(C, (bw + 0.4, bd + 0.4, 0.6), (bx, by, Z0 + bh), "concrete_dark"))
for z in (13.5, 17.0):
    P += window_row(C, (bx - 4.5, by - bd / 2, z), (1, 0, 0), 4, 3.0, (1.6, 2.2), normal=(0, -1, 0))
    P += window_row(C, (bx - bw / 2, by - 4.5, z), (0, 1, 0), 4, 3.0, (1.6, 2.2), normal=(-1, 0, 0))
    P += window_row(C, (bx - 4.5, by + bd / 2, z), (1, 0, 0), 4, 3.0, (1.6, 2.2), normal=(0, 1, 0))
for i in range(5):   # Lisenen
    P.append(box(C, (0.5, 0.3, bh), (bx - bw / 2 + 1.25 + i * 2.875, by - bd / 2 - 0.1, Z0), "brick_dark"))
P.append(box(C, (3, 3, 2.5), (bx + 3, by + 2, Z0 + bh + 0.6), "metal_dark"))       # Dachaufbau

# ---------------- Maschinenhaus (Turbinenhalle)
hx, hy, hw, hd, hh = -10, -5, 22, 12, 10
P.append(box(C, (hw, hd, hh), (hx, hy, Z0), "offwhite"))
P.append(box(C, (hw + 0.1, hd + 0.1, 0.7), (hx, hy, Z0 + hh - 1.2), "blue"))
P += gable(C, hw, hd, 2.4, (hx, hy, Z0 + hh), "roof_dark", overhang=0.35)
P += window_row(C, (hx - 9, hy - hd / 2, 6.0), (1, 0, 0), 7, 3.0, (2.2, 3.0), normal=(0, -1, 0))
P += window_row(C, (hx - hw / 2, hy - 3.5, 6.0), (0, 1, 0), 3, 3.5, (2.2, 3.0), normal=(-1, 0, 0))
P.append(box(C, (4.2, 0.12, 4.6), (hx + 6.5, hy - hd / 2 - 0.05, Z0), "metal_dark"))   # Rolltor
P.append(box(C, (4.6, 0.6, 0.2), (hx + 6.5, hy - hd / 2 - 0.3, Z0 + 4.6), "concrete_dark"))
P.append(box(C, (1.0, 0.12, 2.1), (hx - 2, hy - hd / 2 - 0.05, Z0), "wood_dark"))

# ---------------- Schornstein + Rauchgaskanal
cx, cy = 0.5, 13.0
prof = [(0, Z0), (2.3, Z0), (2.1, 1.5), (1.65, 40), (1.6, 43), (1.55, 46), (1.5, 49), (1.65, 49.2), (1.65, 50), (0, 50)]
P.append(lathe(C, prof, (cx, cy, 0), segs=12,
               colors=["concrete", "concrete_dark", "offwhite", "red", "white", "red", "metal_dark", "metal_dark", "black"]))
P.append(box(C, (4.2, 2.6, 2.6), (bx + bw / 2 + 1.6, cy - 0.5, 15), "metal_dark"))
for z in (20, 30, 40):   # Steigleiter-Podeste
    P.append(lathe(C, [(1.6, z), (2.4, z), (2.4, z + 0.15), (1.6, z + 0.15)], (cx, cy, 0), "metal_dark", segs=12))

# ---------------- Kuehlturm (Hyperboloid, hohl)
tx, ty = 13.5, 7.0
outer = [(8.6, Z0), (8.2, 2.5), (7.0, 9), (6.2, 15), (6.0, 18), (6.2, 21.5), (6.6, 24.5)]
inner = [(6.25, 24.5), (5.85, 21.5), (5.65, 18), (5.85, 15), (6.6, 9), (7.8, 2.5), (0, 2.5)]
prof = outer + inner
cols = ["concrete_dark", "offwhite", "offwhite", "offwhite", "offwhite", "concrete", "concrete",
        "concrete_dark", "concrete_dark", "concrete_dark", "concrete_dark", "concrete_dark", "water"]
P.append(lathe(C, prof, (tx, ty, 0), segs=20, colors=cols))
# Stuetzenkranz am Fuss (dunkle Luftoeffnungen)
for i in range(20):
    a = 2 * math.pi * i / 20
    P.append(box(C, (1.6, 0.2, 1.9), (tx + 8.45 * math.cos(a), ty + 8.45 * math.sin(a), Z0 + 0.1), "black",
                 rot=(0, 0, math.degrees(a) + 90)))

# Kuehlwasser-Leitungen Kuehlturm -> Maschinenhaus
for z, r in ((1.2, 0.55), (2.6, 0.55)):
    P += polyline_pipe(C, [(6.2, 2.0, z), (3.0, 2.0, z), (3.0, -2.5, z), (hx + hw / 2 - 0.1, -2.5, z)], r, "pipe_green")

# ---------------- Kohlelager + Bandanlage
P.append(mound(C, 5.8, 3.4, 4.2, (14.5, -11.5, Z0 + 0.04), "coal", seed=3))
P.append(mound(C, 2.6, 1.8, 2.4, (18.8, -8.6, Z0 + 0.04), "coal", seed=7, subdiv=1))
for a, b in [((7, -16), (21.5, -16)), ((21.5, -16), (21.5, -6.4)), ((7, -16), (7, -10.5))]:
    v = Vector((b[0] - a[0], b[1] - a[1], 0))
    m = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, Z0)
    P.append(box(C, (abs(v.x) + 0.5, abs(v.y) + 0.5, 1.4), m, "concrete_dark"))
hop = Vector((6.0, -6.0, Z0))
P.append(box(C, (1.6, 1.6, 1.5), (hop.x, hop.y, Z0), "metal_dark"))
P.append(cyl(C, 1.2, 1.7, (hop.x, hop.y, Z0 + 1.5), "rust", segs=8, r_top=2.2))        # Trichter
P.append(cyl(C, 2.3, 0.2, (hop.x, hop.y, Z0 + 3.2), "metal_dark", segs=8))
a = Vector((hop.x, hop.y, Z0 + 3.6))
b = Vector((bx + bw / 2 + 0.1, by - 1.5, 16.0))
P.append(beam(C, a, b, 1.5, "metal", t2=1.1))
P.append(beam(C, a + Vector((0, 0, 0.7)), b + Vector((0, 0, 0.7)), 1.8, "roof_dark", t2=0.2))
for f in (0.35, 0.68):
    p = a.lerp(b, f)
    P.append(box(C, (0.6, 0.6, p.z - Z0 - 0.5), (p.x, p.y, Z0), "metal_dark"))
    P.append(box(C, (2.2, 0.5, 0.4), (p.x, p.y, p.z - 0.9), "metal_dark", rot=(0, 0, 55)))
P.append(beam(C, (13.0, -8.0, Z0 + 1.0), (hop.x + 1.2, hop.y - 0.6, Z0 + 3.2), 1.0, "metal", t2=0.8))

# ---------------- Schaltanlage vorne links
P.append(box(C, (11, 5.6, 0.06), (-17.5, -14.6, Z0), "gravel"))
for i in range(3):
    x = -21.0 + i * 3.4
    P.append(box(C, (2.2, 1.8, 2.2), (x, -14.4, Z0), "metal_dark", bevel=0.06))
    P.append(box(C, (2.4, 0.3, 1.6), (x, -15.45, Z0 + 0.3), "metal"))       # Kuehlrippen
    for sx in (-0.6, 0, 0.6):
        P.append(cyl(C, 0.13, 1.1, (x + sx, -14.4, Z0 + 2.2), "white", segs=6))
for x in (-22.6, -12.6):
    P.append(box(C, (0.3, 0.3, 6.5), (x, -12.6, Z0), "metal_dark"))
P.append(beam(C, (-22.6, -12.6, Z0 + 6.4), (-12.6, -12.6, Z0 + 6.4), 0.3, "metal_dark"))
P += fence(C, [(-23.4, -17.6), (-11.8, -17.6), (-11.8, -11.6), (-23.4, -11.6)], h=1.6, spacing=2.9)

# ---------------- Verwaltung vorne Mitte
ox, oy = 4.0, -14.8
P.append(box(C, (7, 4.4, 3.6), (ox, oy, Z0), "plaster"))
P.append(box(C, (7.3, 4.7, 0.35), (ox, oy, Z0 + 3.6), "concrete_dark"))
P += window_row(C, (ox - 2.4, oy - 2.2, 2.0), (1, 0, 0), 4, 1.6, (1.1, 1.2), normal=(0, -1, 0))
P.append(box(C, (1.2, 0.1, 2.2), (ox + 2.8, oy - 2.25, Z0), "glass_dark"))
P.append(box(C, (2.4, 0.15, 0.7), (ox - 1.0, oy - 2.3, Z0 + 3.0), "blue"))  # Schild

body = join(flatten(P), NAME)

# ---------------- Dampf
S = []
puffs = [(0, 0, 25.6, 5.6), (-2.8, 1.0, 27.4, 4.2), (2.9, -0.6, 27.8, 4.4), (0.6, 2.4, 29.6, 4.8),
         (-1.8, 3.8, 32.4, 3.9), (2.0, 4.6, 33.6, 3.4), (0.0, 6.4, 36.2, 2.8), (1.4, 8.0, 38.4, 2.0)]
for (dx, dy, dz, r) in puffs:
    S.append(sphere(C, r, (tx + dx, ty + dy, dz), "white", subdiv=2, scale=(1, 1, 0.72)))
steam = join(S, "Steam", origin=(tx, ty, 26.5))

finish([body, steam], NAME, views={"": (1.0, -1.1, 0.8), "_back": (-1.0, 1.0, 0.75)})
