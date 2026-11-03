"""Natur-Set (je eine FBX unter Models/Nature): Baeume, Herbstvarianten, Bueshe, Felsen. Ursprung = Stammfuss."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.3, 0.55)}


def crown(C, x, y, z, r, color, seed, n=4, spread=0.55, flat=1.0):
    rnd = random.Random(seed)
    P = [sphere(C, r, (x, y, z), color, subdiv=1, scale=(1, 1, flat))]
    for i in range(n):
        a = rnd.uniform(0, 2 * math.pi)
        d = r * spread
        P.append(sphere(C, r * rnd.uniform(0.5, 0.7), (x + math.cos(a) * d, y + math.sin(a) * d,
                                                       z + rnd.uniform(-0.3, 0.5) * r), color, subdiv=1,
                        scale=(1, 1, flat)))
    return P


def trunk(C, h, r=0.25, color="wood_dark", branches=0, seed=0):
    rnd = random.Random(seed)
    P = [cyl(C, r, h, (0, 0, 0), color, segs=7, r_top=r * 0.6)]
    for i in range(branches):
        a = rnd.uniform(0, 2 * math.pi)
        z = h * rnd.uniform(0.55, 0.85)
        P.append(beam(C, (0, 0, z), (math.cos(a) * h * 0.25, math.sin(a) * h * 0.25, z + h * 0.2), r * 0.6, color))
    return P


def oak(C, leaf="green", seed=1):
    return trunk(C, 4.0, 0.38, branches=3, seed=seed) + crown(C, 0, 0, 5.4, 2.8, leaf, seed, 5, 0.7, 0.85)


def birch(C, leaf="green", seed=2):
    P = [cyl(C, 0.17, 6.5, (0, 0, 0), "white", segs=7, r_top=0.1)]
    rnd = random.Random(seed)
    for k in range(6):
        P.append(box(C, (0.3, 0.08, 0.12), (0, 0, 0.6 + k * 0.9), "black", center=True,
                     rot=(0, 0, rnd.uniform(0, 180))))
    P += crown(C, 0, 0, 6.2, 1.5, leaf, seed, 4, 0.6, 1.35)
    return P


def pine(C, seed=3, h=9.0):
    P = trunk(C, h * 0.35, 0.22)
    for k, (rr, zz) in enumerate(((2.4, 0.18), (1.9, 0.38), (1.4, 0.56), (0.85, 0.73))):
        P.append(cyl(C, rr, h * 0.3, (0, 0, h * zz), "green_dark", segs=8, r_top=0.08, rot=(0, 0, k * 20)))
    return P


def spruce(C, seed=4):
    P = trunk(C, 2.0, 0.25)
    P.append(cyl(C, 2.3, 11.0, (0, 0, 1.2), "green_dark", segs=8, r_top=0.05))
    for k in range(3):
        P.append(cyl(C, 2.0 - k * 0.55, 1.4, (0, 0, 2.5 + k * 3.0), "green_dark", segs=8, r_top=1.3 - k * 0.4,
                     rot=(0, 0, 22.5)))
    return P


def poplar(C, leaf="green", seed=5):
    return trunk(C, 2.0, 0.22) + [sphere(C, 1.3, (0, 0, 5.5), leaf, subdiv=1, scale=(1, 1, 3.0)),
                                  sphere(C, 0.9, (0.3, 0.2, 8.0), leaf, subdiv=1, scale=(1, 1, 2.0))]


def fruit_tree(C, seed=6):
    rnd = random.Random(seed)
    P = trunk(C, 2.2, 0.22, branches=2, seed=seed) + crown(C, 0, 0, 3.4, 1.8, "green", seed, 4, 0.6, 0.85)
    for i in range(18):
        # auf der Oberflaeche der Hauptkugel (r 1.8, in Z auf 0.85 gestaucht)
        a, e = rnd.uniform(0, 2 * math.pi), rnd.uniform(-0.5, 0.5)
        P.append(sphere(C, 0.22, (math.cos(a) * 1.6 * math.cos(e), math.sin(a) * 1.6 * math.cos(e),
                                  3.4 + 1.8 * 0.85 * math.sin(e)), "red", subdiv=0))
    return P


def bush(C, color="green", seed=7, r=0.9):
    return crown(C, 0, 0, r * 0.6, r, color, seed, 3, 0.8, 0.75)


def flower_bush(C, seed=8):
    rnd = random.Random(seed)
    P = bush(C, "green_dark", seed, 0.8)
    for i in range(12):
        a, e = rnd.uniform(0, 2 * math.pi), rnd.uniform(0.0, 1.0)
        P.append(sphere(C, 0.12, (math.cos(a) * 0.8 * math.cos(e), math.sin(a) * 0.8 * math.cos(e),
                                   0.5 + 0.6 * math.sin(e)), rnd.choice(("flower_pink", "white", "flower_violet")),
                        subdiv=0))
    return P


def rock(C, seed=9, s=1.0):
    rnd = random.Random(seed)
    P = [mound(C, 1.4 * s, 1.0 * s, 1.1 * s, (0, 0, 0), "concrete_dark", seed=seed, subdiv=1)]
    P.append(mound(C, 0.7 * s, 0.6 * s, 0.6 * s, (1.1 * s, 0.5 * s, 0), "gravel", seed=seed + 1, subdiv=1))
    return P


NATURE = {
    "Tree_Oak": lambda C: oak(C),
    "Tree_Oak_Autumn": lambda C: oak(C, "orange", 11),
    "Tree_Birch": lambda C: birch(C),
    "Tree_Birch_Autumn": lambda C: birch(C, "yellow", 12),
    "Tree_Pine": lambda C: pine(C),
    "Tree_Spruce": lambda C: spruce(C),
    "Tree_Poplar": lambda C: poplar(C),
    "Tree_Fruit": fruit_tree,
    "Tree_Maple_Red": lambda C: oak(C, "red", 13),
    "Bush_Round": lambda C: bush(C),
    "Bush_Flower": flower_bush,
    "Bush_Dark": lambda C: bush(C, "green_dark", 14, 1.2),
    "Rock_Small": lambda C: rock(C, 21, 0.6),
    "Rock_Large": lambda C: rock(C, 22, 1.6),
}
run_family(NATURE, "Nature", views=V)
