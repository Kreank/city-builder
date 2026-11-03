"""Wasserpumpe am Fluss (UtilityCatalog "water_pump", Grundflaeche 16 x 16 m).
Vorderseite (-Y) zeigt zum Wasser: Kaimauer mit Einlaufbauwerk, Saugrohre reichen bis 3 m unter Gelaende."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "WaterPump"
fresh_scene()
C = collection(NAME)
P = []
Z0 = 0.3

P.append(box(C, (16, 16, Z0), (0, 0, 0), "concrete"))
P.append(box(C, (16, 1.2, 3.3), (0, -7.4, -3.0), "concrete_dark"))      # Kaimauer (reicht unter Gelaende)
P.append(box(C, (16.2, 1.4, 0.25), (0, -7.4, Z0), "concrete"))          # Kaimauerkrone

# ---------------- Pumpenhaus (Ziegel, Satteldach)
hx, hy, hw, hd, hh = 0, 2.5, 10, 7, 5.2
P.append(box(C, (hw, hd, 0.5), (hx, hy, Z0), "concrete_dark"))       # Sockel
P.append(box(C, (hw, hd, hh), (hx, hy, Z0 + 0.5), "brick"))
P += gable(C, hw, hd, 2.6, (hx, hy, Z0 + 0.5 + hh), "roof_dark", overhang=0.4, gable_color="brick")
for sx in (-1, 1):   # Gesims
    pass
P.append(box(C, (hw + 0.2, hd + 0.2, 0.3), (hx, hy, Z0 + 0.5 + hh - 0.3), "brick_dark"))
# Fenster (hoch, mit Sohlbank) vorne + seitlich
P += window_row(C, (hx - 3.6, hy - hd / 2, 3.4), (1, 0, 0), 2, 1.8, (1.0, 2.0), normal=(0, -1, 0), frame="plaster")
P += window_row(C, (hx + 1.8, hy - hd / 2, 3.4), (1, 0, 0), 2, 1.8, (1.0, 2.0), normal=(0, -1, 0), frame="plaster")
P += window_row(C, (hx - hw / 2, hy - 2.0, 3.4), (0, 1, 0), 3, 2.0, (1.0, 2.0), normal=(-1, 0, 0), frame="plaster")
P += window_row(C, (hx + hw / 2, hy - 2.0, 3.4), (0, 1, 0), 3, 2.0, (1.0, 2.0), normal=(1, 0, 0), frame="plaster")
# Tor + Schild
P.append(box(C, (2.0, 0.12, 3.0), (hx, hy - hd / 2 - 0.05, Z0 + 0.5), "wood_dark"))
P.append(box(C, (2.4, 0.2, 0.25), (hx, hy - hd / 2 - 0.1, Z0 + 3.5), "plaster"))
P.append(box(C, (1.6, 0.12, 0.8), (hx, hy - hd / 2 - 0.07, Z0 + 4.2), "blue"))
P.append(sphere(C, 0.22, (hx, hy - hd / 2 - 0.18, Z0 + 4.2), "blue_light", subdiv=1, scale=(1, 0.4, 1.3)))
# Dachreiter / Lueftung
P.append(box(C, (1.4, 1.4, 1.2), (hx + 2.5, hy, Z0 + 0.5 + hh + 2.0), "roof_light"))
P += gable(C, 1.4, 1.4, 0.6, (hx + 2.5, hy, Z0 + 0.5 + hh + 3.2), "roof_dark", overhang=0.15)

# ---------------- Saugleitungen zum Wasser + Einlaufbauwerk
for sx in (-2.2, 2.2):
    P += polyline_pipe(C, [(sx, hy - hd / 2 - 0.05, 1.6), (sx, -6.2, 1.6), (sx, -7.0, 1.0), (sx, -8.4, 0.2),
                           (sx, -8.6, -3.0)], 0.42, "blue", segs=10)
    P.append(cyl(C, 0.62, 0.25, (sx, -4.0, 1.6 - 0.62), "metal_dark", segs=10, rot=(90, 0, 0)))   # Flansch
    P.append(cyl(C, 0.62, 0.25, (sx, -4.6, 1.6 - 0.62), "metal_dark", segs=10, rot=(90, 0, 0)))
    P.append(box(C, (0.5, 0.5, 1.1), (sx, -5.5, Z0), "concrete_dark"))                            # Rohrbock
    # Schieber-Handrad
    P.append(cyl(C, 0.1, 0.7, (sx, -3.0, 2.0), "metal_dark", segs=6))
    P.append(lathe(C, [(0.35, 2.7), (0.45, 2.7), (0.45, 2.78), (0.35, 2.78)], (sx, -3.0, 0), "red", segs=10))
# Rechen vor der Kaimauer (Schutzgitter am Einlauf, nur bei Wasser sichtbar)
for k in range(9):
    P.append(box(C, (0.08, 0.1, 3.2), (-3.2 + k * 0.8, -8.15, -3.0), "metal_dark"))
P.append(box(C, (7.0, 0.16, 0.16), (0, -8.15, 0.05), "metal_dark"))

# Gelaender an der Kaimauer (gelb), Luecken fuer die Saugrohre
y = -6.9
P += fence(C, [(-7.8, y), (-3.0, y), (-1.4, y), (1.4, y), (3.0, y), (7.8, y)], h=1.1, spacing=1.6,
           post="yellow", rail="yellow", closed=False, gaps=(1, 3))

# ---------------- hinten: Druckleitung ins Netz, Trafo, Filterbehaelter
P += polyline_pipe(C, [(0, hy + hd / 2 + 0.05, 1.4), (0, 7.2, 1.4), (0, 7.6, 0.5), (0, 8.0, -0.6)], 0.38, "blue",
                   segs=10)
P.append(cyl(C, 1.35, 3.2, (-5.8, 6.4, Z0), "blue_light", segs=12))
P.append(cyl(C, 1.45, 0.25, (-5.8, 6.4, Z0 + 3.2), "metal", segs=12, r_top=0.3))
P += polyline_pipe(C, [(-4.45, 6.4, 1.2), (-hw / 2 + 0.1, 5.4, 1.2)], 0.2, "blue", segs=6, joints=False)
P.append(box(C, (1.8, 1.4, 1.8), (5.8, 6.6, Z0), "green_dark", bevel=0.04))
P.append(box(C, (0.6, 0.06, 0.6), (5.8, 5.87, Z0 + 1.0), "yellow"))
# Lampe am Tor
P.append(box(C, (0.25, 0.4, 0.25), (hx + 1.4, hy - hd / 2 - 0.2, Z0 + 3.1), "metal_dark"))
P.append(box(C, (0.2, 0.2, 0.1), (hx + 1.4, hy - hd / 2 - 0.3, Z0 + 3.0), "light_on"))

body = join(flatten(P), NAME)
finish([body], NAME, views={"": (1.0, -1.2, 0.85), "_back": (-1.0, 1.1, 0.8)})
