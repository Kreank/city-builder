"""Krankenhaus (G4-Dienst, Vorschlag 32 x 32 m): Bettenhaus mit Hubschrauberlandeplatz, Flachbau mit Notaufnahme."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

Z0 = 0.3


def ambulance(C, x, y, rot=0):
    P = [box(C, (2.1, 5.6, 2.4), (0, 0.4, 0.45), "white", bevel=0.1),
         box(C, (2.0, 1.5, 1.6), (0, -2.9, 0.45), "white", bevel=0.12),
         box(C, (1.9, 0.08, 0.7), (0, -3.66, 1.3), "glass_dark"),
         box(C, (2.14, 5.6, 0.35), (0, 0.4, 1.3), "red"),
         box(C, (1.1, 0.4, 0.15), (0, -1.7, 2.85), "blue_light")]
    for wy in (-2.6, 2.0):
        for wx in (-1.05, 0.8):
            P.append(cyl(C, 0.4, 0.25, (wx, wy, 0.42), "black", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def make(C):
    P = [box(C, (32, 32, Z0), (0, 0, 0), "concrete")]
    P.append(box(C, (12, 10, 0.04), (-9.5, -10.5, Z0), "asphalt"))
    for k in range(5):
        P.append(box(C, (0.12, 4.6, 0.05), (-14.5 + k * 2.6, -12.5, Z0), "white"))
    for k, c in enumerate(("blue", "metal", "red")):
        P += parked_car(C, -13.2 + k * 2.6, -12.6, 0, c)
    # Flachbau (Notaufnahme, Ambulanz)
    lx, ly, lw, ld = 0, -3.5, 30, 9
    parts, top_l = block(C, lx, ly, lw, ld, 1, fh=4.4, wall="white", z0=Z0 + 0.35, ribbon_windows=True,
                         win_size=(0, 1.6), win="glass_light", sides="flr")
    P += parts
    P += flat_roof(C, lw, ld, top_l, (lx, ly), ph=0.5)
    P.append(box(C, (lw + 0.1, ld + 0.1, 0.4), (lx, ly, Z0 + 0.35 + 3.6), "teal"))
    # Haupteingang (Glas, Vordach) + Notaufnahme rechts mit grossem Vordach
    P.append(box(C, (5, 0.12, 2.8), (-2, ly - ld / 2 - 0.05, Z0 + 0.35), "glass_dark"))
    P.append(box(C, (7, 3, 0.3), (-2, ly - ld / 2 - 1.5, Z0 + 3.4), "white"))
    for x in (-5, 1):
        P.append(cyl(C, 0.15, 3.1, (x, ly - ld / 2 - 2.7, Z0), "metal", segs=8))
    ex = 10.5
    P.append(box(C, (6, 6, 0.4), (ex, ly - ld / 2 - 3.0, Z0 + 4.0), "white"))
    for x in (ex - 2.8, ex + 2.8):
        P.append(cyl(C, 0.2, 4.0, (x, ly - ld / 2 - 5.6, Z0), "metal", segs=8))
    P.append(box(C, (6.1, 6.1, 0.3), (ex, ly - ld / 2 - 3.0, Z0 + 3.7), "red"))
    P.append(box(C, (3.6, 0.12, 2.8), (ex, ly - ld / 2 - 0.05, Z0 + 0.35), "metal_dark"))
    P += sign(C, ex, ly - ld / 2 - 6.0, Z0 + 4.45, 4.4, 0.6, "red", "white")
    P += ambulance(C, ex, -14.0, 0)
    # Bettenhaus (5 Geschosse)
    tx, ty, tw, td = 0, 7.0, 22, 12
    parts, top = block(C, tx, ty, tw, td, 5, fh=3.4, wall="white", z0=Z0 + 0.35 + 4.4 - 3.4 * 0 - 0.0,
                       ribbon_windows=True, win_size=(0, 1.5), win="glass_light", plinth="white")
    P += parts
    P += ribbon(C, tx, ty, tw, td, Z0 + 2.5, 1.6, "glass_light", "blr")   # Erdgeschoss hinter dem Flachbau
    for i in range(5):
        P.append(box(C, (tw + 0.12, td + 0.12, 0.3), (tx, ty, Z0 + 0.35 + 4.4 + i * 3.4 - 0.15), "teal"))
    # Treppenhauskern
    P.append(box(C, (4, 4, top - Z0 + 2.4), (tx - tw / 2 + 2.0, ty + td / 2 - 1.0, Z0), "offwhite"))
    P += flat_roof(C, tw, td, top, (tx, ty), ph=0.6)
    # Rotes Kreuz an der Front
    zc = top - 2.6
    P.append(box(C, (4.0, 0.2, 4.0), (tx + 6, ty - td / 2 - 0.1, zc - 2.0), "white"))
    P.append(box(C, (3.0, 0.3, 0.9), (tx + 6, ty - td / 2 - 0.15, zc - 0.45), "red"))
    P.append(box(C, (0.9, 0.3, 3.0), (tx + 6, ty - td / 2 - 0.15, zc - 1.5), "red"))
    # Hubschrauberlandeplatz
    hx, hy = tx + 2.5, ty
    P.append(cyl(C, 5.2, 0.4, (hx, hy, top), "concrete_dark", segs=20))
    P.append(lathe(C, [(4.3, 0.42), (4.75, 0.42), (4.75, 0.47), (4.3, 0.47)], (hx, hy, top), "yellow", segs=20))
    P.append(box(C, (0.6, 3.2, 0.06), (hx - 1.0, hy, top + 0.4), "white"))
    P.append(box(C, (0.6, 3.2, 0.06), (hx + 1.0, hy, top + 0.4), "white"))
    P.append(box(C, (1.6, 0.6, 0.06), (hx, hy, top + 0.4), "white"))
    P += ac_units(C, tx - 6, ty + 2, top, 3, seed=5)
    # Gruen
    for (x, y) in ((-13, 12), (-13, 6), (13, 12), (13.5, 5.5)):
        P += tree(C, x, y, 6.5, 2.2, seed=int(x * y))
    P += bench(C, -13, 2.0, 90)
    return P


build("Hospital", "Services", make)
