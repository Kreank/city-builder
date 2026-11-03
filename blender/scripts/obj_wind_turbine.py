"""Windrad (UtilityCatalog "power_wind", Grundflaeche 12 x 12 m).
Zwei Objekte: "WindTurbine" (Turm, Gondel) und "Rotor" (Ursprung = Nabe, dreht um Unity-Z = Blender -Y)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "WindTurbine"
fresh_scene()
C = collection(NAME)
parts = []

# Fundament + Trafostation
oct_pts = [(4.6 * math.cos(math.radians(22.5 + 45 * i)), 4.6 * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)]
parts.append(prism(C, oct_pts, 0.35, color="concrete"))
parts.append(cyl(C, 2.2, 0.35, (0, 0, 0.35), "concrete_dark", segs=12))
parts.append(box(C, (2.0, 1.4, 1.9), (3.6, -3.4, 0.0), "green_dark", bevel=0.05))
parts.append(box(C, (2.2, 1.6, 0.12), (3.6, -3.4, 1.9), "metal_dark"))
parts.append(box(C, (0.9, 0.06, 1.3), (3.6, -4.12, 0.3), "metal"))

# Turm (verjuengt) mit Tuer und Absatz
H = 39.6
prof = [(0, 0.7), (1.55, 0.7), (1.5, 3.0), (1.3, 18.0), (1.0, H), (0, H)]
parts.append(lathe(C, prof, segs=16, colors=["white", "offwhite", "white", "white", "white"]))
parts.append(box(C, (0.9, 0.3, 2.0), (0, -1.43, 0.7), "metal_dark"))
parts.append(box(C, (1.6, 1.0, 0.12), (0, -1.9, 0.7), "metal"))
for sx in (-0.75, 0.75):
    parts.append(box(C, (0.06, 0.06, 1.0), (sx, -2.35, 0.82), "metal"))
parts.append(beam(C, (-0.75, -2.35, 1.82), (0.75, -2.35, 1.82), 0.06, "metal"))

# Gondel (zeigt mit der Nabe nach vorne = -Y)
ZH = H + 1.5
parts.append(cyl(C, 1.05, 0.6, (0, 0, H - 0.1), "offwhite", segs=12))
parts.append(box(C, (2.6, 7.0, 2.8), (0, 2.2, ZH - 1.4), "white", bevel=0.25))
parts.append(box(C, (2.2, 1.4, 0.5), (0, 4.5, ZH + 1.35), "offwhite", bevel=0.1))   # Kuehler
parts.append(cyl(C, 0.09, 1.0, (0.6, 5.2, ZH + 1.4), "metal_dark", segs=6))      # Windmesser-Mast
parts.append(sphere(C, 0.22, (-0.6, 5.0, ZH + 1.6), "red", subdiv=1))             # Befeuerung
parts.append(box(C, (2.66, 4.6, 0.5), (0, 3.0, ZH - 0.3), "blue", center=True))   # Zierstreifen
body = join(flatten(parts), NAME)

# ---------------------------------------------------------------- Rotor
HUB = Vector((0, -1.95, ZH))
rparts = []
rparts.append(lathe(C, [(0, 0), (1.15, 0), (1.15, 0.5), (0.85, 1.6), (0.3, 2.3), (0, 2.4)],
                    (HUB.x, HUB.y + 0.75, HUB.z), "white", segs=12, rot=(90, 0, 0), name="spinner"))
blade = [(0.9, -0.55), (3.2, -0.95), (10.0, -0.55), (18.2, -0.12), (18.4, 0.08), (10.0, 0.35), (3.2, 0.7), (0.9, 0.55)]
for k in range(3):
    b = prism(C, blade, 0.32, (0, 0, -0.16), "white", name="blade")
    # Blattflaeche: radial -> Z, Profiltiefe -> X, Dicke -> Y; dann um die Rotorachse verteilen
    b.data.transform(Matrix.Translation(HUB) @ Matrix.Rotation(math.radians(120 * k), 4, 'Y')
                     @ Matrix.Rotation(math.radians(-90), 4, 'Y') @ Matrix.Rotation(math.radians(90), 4, 'X'))
    rparts.append(b)
    # roter Spitzenstreifen (Flugsicherung)
    tip = box(C, (0.55, 0.36, 1.6), (0, 0, 15.6), "red", center=True, name="tip")
    tip.data.transform(Matrix.Translation(HUB) @ Matrix.Rotation(math.radians(120 * k), 4, 'Y')
                       @ Matrix.Translation((0.08, 0, 0)))
    rparts.append(tip)
rotor = join(rparts, "Rotor", origin=HUB)

finish([body, rotor], NAME, views={"": (1.0, -1.3, 0.55), "_back": (-1.1, 0.9, 0.5)})
