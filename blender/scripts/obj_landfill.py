"""Muellhalde / Deponie (Vorschlag 40 x 40 m): Schuettberg mit Muellschichten, Kompaktor, Waage, Zaun.
Objekt "Fill" (der Muellberg) separat, damit Unity ihn je nach Fuellstand in der Hoehe skalieren kann."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.2
COLS = ("red", "blue", "yellow", "white", "green", "orange", "pastel_blue", "metal", "black", "wood")


def compactor(C, x, y, rot=0):
    P = [box(C, (2.6, 4.2, 1.2), (0, 0, 0.9), "yellow", bevel=0.1),
         box(C, (1.8, 1.8, 1.6), (0, 0.6, 2.1), "yellow", bevel=0.1),
         box(C, (1.6, 1.82, 1.0), (0, 0.6, 2.4), "glass_dark"),
         box(C, (3.0, 0.4, 1.0), (0, -2.7, 0.2), "metal_dark")]
    for wy in (-1.4, 1.4):
        P.append(cyl(C, 0.9, 0.7, (-1.5, wy, 0.9), "metal_dark", segs=8, rot=(0, 90, 0)))
        P.append(cyl(C, 0.9, 0.7, (0.8, wy, 0.9), "metal_dark", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def make(C):
    rnd = random.Random(4)
    P = [box(C, (40, 40, Z0), (0, 0, 0), "grass")]
    P.append(box(C, (34, 30, 0.04), (0, 3, Z0), "wood_dark"))                   # Erdreich (abgedichtete Flaeche)
    P.append(box(C, (4.0, 14.0, 0.05), (-14.0, -12.0, Z0), "asphalt"))          # Zufahrt
    # Waage + Haeuschen am Eingang
    P.append(box(C, (3.4, 8.0, 0.12), (-14.0, -14.5, Z0), "metal_dark"))
    P.append(box(C, (3.0, 2.6, 2.6), (-10.4, -16.0, Z0), "offwhite"))
    P += flat_roof(C, 3.0, 2.6, Z0 + 2.6, (-10.4, -16.0), ph=0.2, t=0.15)
    P += window_row(C, (-11.9, -16.0, Z0 + 1.6), (0, 1, 0), 1, 1, (1.4, 0.9), normal=(-1, 0, 0))
    P.append(box(C, (0.15, 0.15, 1.1), (-12.2, -11.6, Z0), "red"))
    P.append(beam(C, (-12.2, -11.6, Z0 + 1.0), (-15.8, -11.6, Z0 + 1.0), 0.1, "red"))     # Schranke
    # Kompaktor + Muellwagen
    P += compactor(C, 6.0, -5.0, 20)
    P += garbage_truck(C, -14.0, -6.0, 0, "orange")
    P += garbage_truck(C, -2.0, -12.5, 90, "green")
    # Zaun mit Tor
    P += fence(C, [(-16.2, -19.7), (-19.7, -19.7), (-19.7, 19.7), (19.7, 19.7), (19.7, -19.7), (-11.8, -19.7)],
               h=2.2, spacing=3.4, closed=False, post="metal_dark", rail="metal")
    # Baeume als Sichtschutz
    for k in range(6):
        P += tree(C, 18.0, -14.0 + k * 6.0, 6.0 + (k % 2), 1.6, "pine")
        P += tree(C, -18.0, -2.0 + k * 4.0, 6.0, 1.6, "pine" if k % 2 else "round", seed=k)

    # ---- Muellberg (eigenes Objekt)
    F = []
    F.append(mound(C, 13.0, 11.0, 7.5, (2.0, 6.0, Z0), "wood_dark", seed=11, subdiv=3))
    F.append(mound(C, 10.0, 8.5, 6.6, (2.5, 6.5, Z0 + 0.6), "gravel", seed=12, subdiv=3))
    F.append(mound(C, 6.0, 5.0, 4.0, (-6.0, 1.0, Z0), "metal_dark", seed=13, subdiv=2))
    # bunte Muellstuecke auf der Oberflaeche
    for i in range(140):
        a = rnd.uniform(0, 2 * math.pi)
        rr = math.sqrt(rnd.uniform(0, 1))
        x = 2.5 + math.cos(a) * rr * 9.0
        y = 6.5 + math.sin(a) * rr * 7.5
        z = Z0 + 0.6 + 6.6 * max(0.0, 1 - rr * rr) * 0.92
        s = rnd.uniform(0.3, 0.8)
        F.append(box(C, (s, s * rnd.uniform(0.5, 1.2), s * 0.6), (x, y, z - 0.1), rnd.choice(COLS),
                     rot=(rnd.uniform(-25, 25), rnd.uniform(-25, 25), rnd.uniform(0, 180))))
    return {"Landfill": (P, (0, 0, 0)), "Fill": (F, (0, 0, 0))}


build("Landfill", "Services", make, views={"": (0.9, -1.1, 0.9), "_back": (-1.0, 1.0, 0.8)})
