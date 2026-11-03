"""Brunnen + Wasserturm (UtilityCatalog "water_tower", Grundflaeche 12 x 12 m)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib
importlib.reload(clib)
from clib import *

NAME = "WaterTower"
fresh_scene()
C = collection(NAME)
parts = []

# Fundament (Achteck) + Fuesse
oct_pts = [(5.4 * math.cos(math.radians(22.5 + 45 * i)), 5.4 * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)]
parts.append(prism(C, oct_pts, 0.3, color="concrete"))

# Stuetzen: 6 Beine, leicht nach innen geneigt
N = 6
Z_TANK = 15.4
legs_bot, legs_top = [], []
for i in range(N):
    a = math.radians(30 + 60 * i)
    b = Vector((4.3 * math.cos(a), 4.3 * math.sin(a), 0.3))
    t = Vector((3.3 * math.cos(a), 3.3 * math.sin(a), Z_TANK + 0.6))
    legs_bot.append(b); legs_top.append(t)
    parts.append(pipe(C, b, t, 0.28, "steel_blue", segs=6))
    parts.append(box(C, (1.0, 1.0, 0.5), (b.x, b.y, 0.3), "concrete_dark", rot=(0, 0, 30 + 60 * i)))


def leg_at(i, z):
    b, t = legs_bot[i % N], legs_top[i % N]
    f = (z - b.z) / (t.z - b.z)
    return b.lerp(t, f)


levels = [0.8, 5.6, 10.4, 15.2]
for i in range(N):
    for z in levels[1:]:
        parts.append(beam(C, leg_at(i, z), leg_at(i + 1, z), 0.18, "steel_blue"))
    for z0, z1 in zip(levels[:-1], levels[1:]):
        parts.append(beam(C, leg_at(i, z0), leg_at(i + 1, z1), 0.09, "metal_dark"))
        parts.append(beam(C, leg_at(i + 1, z0), leg_at(i, z1), 0.09, "metal_dark"))

# Steigrohr in der Mitte
parts.append(cyl(C, 0.55, Z_TANK + 0.5, (0, 0, 0.3), "metal", segs=8))

# Tank
prof = [(0, Z_TANK), (2.6, Z_TANK + 0.6), (4.4, Z_TANK + 1.8), (4.4, Z_TANK + 3.4), (4.4, Z_TANK + 4.6),
        (4.4, Z_TANK + 6.0), (4.65, Z_TANK + 6.0), (4.65, Z_TANK + 6.35), (1.0, Z_TANK + 8.0), (0, Z_TANK + 8.15)]
cols = ["metal_dark", "steel_blue", "blue", "white", "blue", "white", "white", "roof_dark", "roof_dark"]
parts.append(lathe(C, prof, color="blue", segs=16, colors=cols))
# Entlueftung + Warnlicht oben
parts.append(cyl(C, 0.35, 0.6, (0, 0, Z_TANK + 8.0), "metal_dark", segs=6))
parts.append(sphere(C, 0.3, (0, 0, Z_TANK + 8.75), "red", subdiv=1))

# Umlaufender Steg mit Gelaender
ZC = Z_TANK + 1.8
parts.append(lathe(C, [(4.3, ZC - 0.15), (5.3, ZC - 0.15), (5.3, ZC), (4.3, ZC)], color="metal_dark", segs=16))
for i in range(16):
    a = 2 * math.pi * i / 16 + math.pi / 16
    parts.append(box(C, (0.08, 0.08, 1.05), (5.2 * math.cos(a), 5.2 * math.sin(a), ZC), "metal_dark"))
parts.append(lathe(C, [(5.12, ZC + 1.0), (5.28, ZC + 1.0), (5.28, ZC + 1.1), (5.12, ZC + 1.1)], color="metal", segs=16))
parts.append(lathe(C, [(5.14, ZC + 0.5), (5.26, ZC + 0.5), (5.26, ZC + 0.57), (5.14, ZC + 0.57)], color="metal", segs=16))

# Leiter an Bein 4 (Richtung -Y/+X vorne rechts -> a = 270 Grad ist Bein i=4)
lb, lt = legs_bot[4], Vector((legs_top[4].x, legs_top[4].y, ZC))
out = Vector((lb.x, lb.y, 0)).normalized() * 0.45
side = Vector((-out.y, out.x, 0)).normalized() * 0.3
r0, r1 = lb + out, lt + out + Vector((0, 0, 0)) * 0
r1 = Vector((leg_at(4, ZC).x, leg_at(4, ZC).y, ZC)) + out * 1.4
for s in (side, -side):
    parts.append(beam(C, r0 + s, r1 + s, 0.07, "metal"))
steps = 30
for k in range(1, steps):
    p = r0.lerp(r1, k / steps)
    parts.append(beam(C, p - side, p + side, 0.05, "metal"))

# Brunnenhaus vorne rechts (Tuer nach vorne = -Y)
hx, hy = 3.4, -3.6
parts.append(box(C, (3.2, 2.8, 2.4), (hx, hy, 0.3), "plaster"))
parts += gable(C, 3.2, 2.8, 1.1, (hx, hy, 2.7), "roof_red", overhang=0.25, rot=(0, 0, 90), gable_color="plaster")
parts.append(box(C, (0.9, 0.08, 1.9), (hx - 0.6, hy - 1.42, 0.3), "wood_dark"))
parts.append(box(C, (0.7, 0.08, 0.6), (hx + 0.75, hy - 1.42, 1.5), "glass"))
parts.append(cyl(C, 0.16, 0.5, (hx + 0.9, hy + 0.6, 3.3), "metal_dark", segs=6))
# Leitung Brunnenhaus -> Steigrohr (auf Fundamenthoehe)
parts += polyline_pipe(C, [(hx - 1.6, hy + 0.6, 0.6), (0.8, hy + 0.6, 0.6), (0.8, -0.6, 0.6), (0.4, -0.3, 0.6)],
                       0.18, "pipe_green", segs=6)

body = join(flatten(parts), NAME)
finish([body], NAME)
