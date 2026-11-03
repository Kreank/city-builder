"""Sehenswuerdigkeiten (Models/Landmarks): Kirche, Rathaus, Museum, Burg, Ruine, Brunnen, Obelisk, Denkmal,
Musikpavillon. Ursprung = Mitte Grundflaeche, Vorderseite = -Y (Unity +Z)."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.25, 0.65)}


def face_center(x, y):
    """Drehwinkel (Grad), damit die Vorderseite (-Y) eines Teils bei (x, y) zum Ursprung zeigt."""
    return math.degrees(math.atan2(-x, y))


def merlons(C, a, b, z, t=0.6, h=1.0, w=0.9, gap=0.8, color="rock", side=0.0):
    """Zinnen entlang der Linie a-b (2D) auf Hoehe z; side verschiebt quer zur Linie."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    v = b - a
    L = v.length
    d = v.normalized()
    n_ = Vector((-d.y, d.x, 0))
    ang = math.degrees(math.atan2(d.y, d.x))
    k = max(1, int((L + gap) // (w + gap)))
    span = k * w + (k - 1) * gap
    out = []
    for i in range(k):
        p = a + d * ((L - span) / 2 + w / 2 + i * (w + gap)) + n_ * side
        out.append(box(C, (w, t, h), (p.x, p.y, z), color, rot=(0, 0, ang)))
    return out


def ring_merlons(C, x, y, r, z, n=10, h=1.0, color="rock"):
    out = []
    for i in range(n):
        a = 2 * math.pi * i / n
        out.append(box(C, (0.9, 0.6, h), (x + r * math.cos(a), y + r * math.sin(a), z), color,
                       rot=(0, 0, math.degrees(a) + 90)))
    return out


def clock(C, x, y, z, side, r=1.1):
    """Zifferblatt auf einer Turmseite. side: 'f' (-Y), 'b' (+Y), 'l' (-X), 'r' (+X); (x, y) = Wandpunkt."""
    rot = {"f": (90, 0, 0), "b": (-90, 0, 0), "l": (0, -90, 0), "r": (0, 90, 0)}[side]
    n = {"f": (0, -1), "b": (0, 1), "l": (-1, 0), "r": (1, 0)}[side]
    P = [cyl(C, r, 0.08, (x, y, z), "white", segs=12, rot=rot),
         cyl(C, r + 0.12, 0.05, (x, y, z), "gold", segs=12, rot=rot)]
    hx, hy = x + n[0] * 0.1, y + n[1] * 0.1
    along = (abs(n[1]) * 0.12 + abs(n[0]) * 0.06, abs(n[0]) * 0.12 + abs(n[1]) * 0.06)
    P.append(box(C, (along[0], along[1], r * 0.75), (hx, hy, z - 0.05), "black"))
    P.append(box(C, (abs(n[1]) * r * 0.55 + abs(n[0]) * 0.06, abs(n[0]) * r * 0.55 + abs(n[1]) * 0.06, 0.1),
                 (hx + abs(n[1]) * r * 0.27, hy + abs(n[0]) * r * 0.27, z - 0.05), "black"))
    return P


# ------------------------------------------------------------------ Kirche
def church(C):
    """Dorfkirche (Grundstueck 24 x 36 m): Schiff 12 x 21 m, Westturm mit Spitzhelm (37 m), Apsis, Friedhof."""
    rnd = random.Random(7)
    P = [box(C, (24.0, 36.0, 0.1), (0, 0, 0), "grass"),
         box(C, (2.6, 2.5, 0.12), (0, -16.75, 0), "gravel"),
         box(C, (14.0, 33.0, PLINTH), (0, 0, 0), "concrete_dark")]
    z0 = PLINTH
    P.append(box(C, (12.0, 21.0, 9.0), (0, 0, z0), "plaster"))
    P += gable(C, 21.0, 12.0, 6.5, (0, 0, z0 + 9.0), "roof_red", overhang=0.5, rot=(0, 0, 90), gable_color="plaster")
    for s in (-1, 1):
        P += window_row(C, (s * 6.0, -7.2, z0 + 4.8), (0, 1, 0), 5, 3.6, (1.3, 3.8), "glass", normal=(s, 0, 0),
                        frame="plaster_warm")
        for k in range(6):
            P.append(box(C, (0.8, 0.9, 6.5), (s * 6.35, -9.0 + k * 3.6, z0), "plaster_warm"))
        P += door(C, s * 6.0, 7.6, 1.4, 2.6, "wood_dark", z0, normal=(s, 0, 0))
    # Apsis (Halbkreis hinten)
    P.append(cyl(C, 5.0, 8.0, (0, 10.5, z0), "plaster", segs=14))
    P.append(cyl(C, 5.4, 4.0, (0, 10.5, z0 + 8.0), "roof_red", segs=14, r_top=0.3))
    for a in (-50, 0, 50):
        ar = math.radians(a)
        P.append(box(C, (1.0, 0.1, 3.4), (5.03 * math.sin(ar), 10.5 + 5.03 * math.cos(ar), z0 + 3.0), "glass",
                     rot=(0, 0, -a)))
    # Turm
    ty, tw = -13.0, 5.6
    P.append(box(C, (tw, tw, 24.0), (0, ty, z0), "plaster"))
    for z in (z0 + 9.0, z0 + 15.6, z0 + 23.5):
        P.append(box(C, (tw + 0.4, tw + 0.4, 0.45), (0, ty, z), "plaster_warm"))
    P += facade(C, 0, ty, tw, tw, [z0 + 20.6], spacing=2.0, win=(1.0, 2.8), color="black", margin=0.5)
    P += facade(C, 0, ty, tw, tw, [z0 + 12.0], spacing=2.0, win=(0.5, 1.4), color="glass", margin=1.5)
    for sd, (x, y) in (("f", (0, ty - tw / 2)), ("l", (-tw / 2, ty)), ("r", (tw / 2, ty)), ("b", (0, ty + tw / 2))):
        P += clock(C, x, y, z0 + 17.8, sd)
    P.append(hip(C, tw, tw, 12.5, (0, ty, z0 + 24.0), "roof_green", overhang=0.3))
    P.append(sphere(C, 0.3, (0, ty, z0 + 36.6), "gold", subdiv=1))
    P.append(box(C, (0.16, 0.16, 1.8), (0, ty, z0 + 36.8), "gold"))
    P.append(box(C, (1.0, 0.16, 0.16), (0, ty, z0 + 37.9), "gold"))
    P += door(C, 0, ty - tw / 2, 1.8, 3.4, "wood_dark", z0, canopy=None)
    P.append(box(C, (2.6, 0.2, 0.5), (0, ty - tw / 2 - 0.05, z0 + 3.4), "plaster_warm"))
    # Friedhof: Grabsteine + Hecke + Baeume
    for s in (-1, 1):
        for row in range(4):
            for col in range(2):
                x = s * (8.2 + col * 1.9)
                y = -4.0 + row * 3.4
                P.append(box(C, (0.9, 0.25, 0.9 + rnd.uniform(0, 0.4)), (x, y, 0.1), rnd.choice(("rock", "rock_dark",
                                                                                                 "concrete"))))
                P.append(box(C, (0.9, 1.6, 0.18), (x, y + 0.95, 0.1), "green_dark"))
        P += tree(C, s * 9.5, -12.0, 8.0, 2.8, "round", seed=3 + s)
        P += tree(C, s * 9.0, 12.5, 9.0, 1.8, "pine", seed=5 + s)
        P.append(hedge(C, (s * 11.6, -17.6), (s * 11.6, 17.6), 1.0, 0.7))
    P.append(hedge(C, (-11.6, 17.6), (11.6, 17.6), 1.0, 0.7))
    for s in (-1, 1):
        P.append(hedge(C, (s * 1.6, -17.6), (s * 11.6, -17.6), 1.0, 0.7))
    return P


# ------------------------------------------------------------------ Rathaus
def town_hall(C):
    """Rathaus (Grundstueck 36 x 28 m): dreigeschossig mit Mittelrisalit, Saeulenvorhalle, Dachreiter mit Uhr,
    Platz mit Fahnen."""
    W, D, fh, z0 = 32.0, 16.0, 3.6, 0.9
    P = [box(C, (36.0, 28.0, 0.1), (0, 0, 0), "concrete")]
    cy = 3.0
    parts, top = block(C, 0, cy, W, D, 3, fh=fh, wall="plaster_warm", plinth="concrete_dark", z0=z0, win="glass",
                       spacing=3.3, win_size=(1.3, 2.0), frame="cream", bands="cream",
                       skip=lambda s, r, c: s == "f" and 3 <= c <= 5)
    P += parts
    fy = cy - D / 2
    # Risalit
    RW = 10.0
    P.append(box(C, (RW, 1.6, top - z0 + 1.2), (0, fy - 0.8, z0), "cream"))
    P += facade(C, 0, fy - 0.8, RW, 1.6, [z0 + fh * 1.55, z0 + fh * 2.55], spacing=3.0, win=(1.4, 2.4),
                color="glass", frame="white", sides="f")
    P += gable(C, 1.6, RW + 0.4, 2.4, (0, fy - 0.8, top + 1.2), "roof_dark", overhang=0.2, rot=(0, 0, 90),
               gable_color="cream")
    # Vorhalle: Podest, Saeulen, Balkon
    P.append(box(C, (RW, 3.4, z0), (0, fy - 3.3, 0), "concrete_dark"))
    for k in range(3):
        P.append(box(C, (RW - 2.0, 0.5, z0 - k * 0.3), (0, fy - 5.25 - k * 0.5, 0), "concrete"))
    for x in (-3.9, -1.3, 1.3, 3.9):
        P.append(cyl(C, 0.4, fh, (x, fy - 4.4, z0), "white", segs=10))
        P.append(box(C, (0.95, 0.95, 0.3), (x, fy - 4.4, z0 + fh - 0.3), "white"))
    P.append(box(C, (RW, 4.0, 0.4), (0, fy - 3.0, z0 + fh), "white"))
    P.append(box(C, (RW, 0.1, 0.9), (0, fy - 5.0, z0 + fh + 0.4), "metal_dark"))
    P += door(C, 0, fy - 1.6, 2.4, 2.8, "wood_dark", z0)
    P += sign(C, 0, fy - 1.6, z0 + fh * 2 + 2.6, 4.0, 0.5, "gold", "black")
    # Dach + Dachreiter
    P.append(hip(C, W, D, 5.0, (0, cy, top), "roof_dark", overhang=0.4))
    tw = 4.4
    P.append(box(C, (tw, tw, 9.5), (0, cy, top + 1.0), "cream"))
    P.append(box(C, (tw + 0.4, tw + 0.4, 0.4), (0, cy, top + 10.5), "white"))
    P += facade(C, 0, cy, tw, tw, [top + 9.0], spacing=1.6, win=(0.8, 1.6), color="black", margin=0.6)
    for sd, (x, y) in (("f", (0, cy - tw / 2)), ("b", (0, cy + tw / 2)), ("l", (-tw / 2, cy)), ("r", (tw / 2, cy))):
        P += clock(C, x, y, top + 6.6, sd, r=1.2)
    P.append(hip(C, tw, tw, 4.5, (0, cy, top + 10.9), "roof_green", overhang=0.3))
    P.append(cyl(C, 0.08, 2.0, (0, cy, top + 15.3), "gold", segs=6))
    P.append(sphere(C, 0.3, (0, cy, top + 16.0), "gold", subdiv=1))
    # Platz
    for x, fl in ((-7.0, "red"), (-5.0, "yellow"), (5.0, "blue"), (7.0, "white")):
        P += flagpole(C, x, -12.0, 9.0, fl)
    for x in (-12.0, 12.0):
        P += bench(C, x, -9.5, 0)
        P += lamp(C, x + 2.5, -11.5, 0)
        P += tree(C, x + (2.0 if x > 0 else -2.0), -6.0, 7.0, 2.2, "round", seed=int(x))
    P += flower_bed(C, -15.0, 3.0, 2.4, 10.0, seed=2)
    P += flower_bed(C, 15.0, 3.0, 2.4, 10.0, seed=3)
    return P


# ------------------------------------------------------------------ Museum
def museum(C):
    """Museum im klassizistischen Stil (34 x 30 m): Stufensockel, 8 Saeulen, Dreiecksgiebel, Banner."""
    P = [box(C, (34.0, 30.0, 0.1), (0, 0, 0), "concrete")]
    for k, (w, d) in enumerate(((30.0, 25.0), (28.8, 23.8), (27.6, 22.6))):
        P.append(box(C, (w, d, 0.5), (0, 0, 0.1 + k * 0.5), ("concrete_dark", "concrete", "offwhite")[k]))
    zb = 1.6
    P.append(box(C, (24.0, 15.0, 9.0), (0, 3.0, zb), "cream"))
    H = 9.0
    for i in range(8):
        x = -12.0 + i * 24.0 / 7
        P.append(box(C, (1.4, 1.4, 0.35), (x, -9.4, zb), "white"))
        P.append(cyl(C, 0.6, H - 0.7, (x, -9.4, zb + 0.35), "white", segs=12, r_top=0.52))
        P.append(box(C, (1.5, 1.5, 0.35), (x, -9.4, zb + H - 0.35), "white"))
    for y in (-5.6, -1.6, 2.4, 6.4):            # Seitensaeulen (Halbsaeulen an der Wand)
        for s in (-1, 1):
            P.append(cyl(C, 0.45, H, (s * 12.05, y, zb), "white", segs=10))
    P.append(box(C, (27.0, 22.0, 1.3), (0, 0.6, zb + H), "white"))
    P.append(box(C, (27.4, 22.4, 0.3), (0, 0.6, zb + H + 1.3), "offwhite"))
    P += gable(C, 22.4, 27.4, 3.4, (0, 0.6, zb + H + 1.6), "roof_light", overhang=0.3, rot=(0, 0, 90),
               gable_color="white")
    P.append(box(C, (6.0, 0.15, 0.9), (0, -10.42, zb + H + 2.0), "gold"))        # Relief im Giebel
    # Eingang, Banner, Fenster
    fy = 3.0 - 7.5
    P.append(box(C, (3.2, 0.12, 5.2), (0, fy - 0.05, zb), "wood_dark"))
    P.append(box(C, (4.0, 0.2, 0.4), (0, fy - 0.1, zb + 5.2), "gold"))
    for x, col in ((-6.0, "red"), (6.0, "steel_blue")):
        P.append(box(C, (1.6, 0.08, 4.5), (x, fy - 0.08, zb + 3.2), col))
        P.append(box(C, (1.1, 0.1, 0.5), (x, fy - 0.1, zb + 6.3), "white"))
    P += facade(C, 0, 3.0, 24.0, 15.0, [zb + 5.0], spacing=4.0, win=(1.2, 3.0), color="glass_dark", sides="lr",
                frame="white")
    for x in (-15.0, 15.0):
        P += lamp(C, x, -13.0, 0)
        P.append(box(C, (1.8, 1.8, 1.0), (x * 0.62, -13.2, 0.1), "offwhite"))     # Sockel fuer Figuren
        P.append(sphere(C, 0.75, (x * 0.62, -13.2, 1.8), "teal", subdiv=1, scale=(1, 1, 1.1)))
    return P


# ------------------------------------------------------------------ Burg
def castle(C):
    """Burg (44 x 44 m): Ringmauer mit Zinnen, vier Rundtuerme, Torhaus mit Zugbruecke, Bergfried, Palas."""
    P = [box(C, (44.0, 44.0, 0.2), (0, 0, 0), "grass"),
         box(C, (28.0, 28.0, 0.08), (0, 0, 0.2), "gravel")]
    z0, R, WH, WT = 0.2, 14.0, 9.0, 2.0
    # Mauern
    # (a, b, sg): sg = Vorzeichen der Aussenseite relativ zur Linksnormalen von a->b (Zinnen aussen)
    segs = [((-R, R), (R, R), 1), ((-R, -R), (-R, R), 1), ((R, -R), (R, R), -1), ((-R, -R), (-4.0, -R), -1),
            ((4.0, -R), (R, -R), -1)]
    for a, b, sg in segs:
        cx, cy = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        horiz = abs(b[1] - a[1]) < 1e-6
        P.append(box(C, ((L, WT, WH) if horiz else (WT, L, WH)), (cx, cy, z0), "rock"))
        P.append(box(C, ((L, WT + 0.3, 0.4) if horiz else (WT + 0.3, L, 0.4)), (cx, cy, z0 + WH - 0.4), "rock_dark"))
        P += merlons(C, a, b, z0 + WH, 0.6, 1.1, side=sg * (WT / 2 - 0.2), color="rock")
    # Torhaus
    P.append(box(C, (9.0, 5.0, 13.0), (0, -R, z0), "rock"))
    P.append(box(C, (9.6, 5.6, 0.5), (0, -R, z0 + 12.5), "rock_dark"))
    P += merlons(C, (-4.8, -R - 2.5), (4.8, -R - 2.5), z0 + 13.0, color="rock")
    P += merlons(C, (-4.8, -R + 2.5), (4.8, -R + 2.5), z0 + 13.0, color="rock")
    P.append(box(C, (3.8, 0.1, 5.2), (0, -R - 2.55, z0), "black"))
    for k in range(4):
        P.append(box(C, (0.12, 0.14, 5.0), (-1.4 + k * 0.93, -R - 2.62, z0 + 0.2), "metal_dark"))
    for k in range(3):
        P.append(box(C, (3.8, 0.14, 0.12), (0, -R - 2.62, z0 + 1.4 + k * 1.4), "metal_dark"))
    P.append(box(C, (4.4, 0.3, 0.8), (0, -R - 2.6, z0 + 5.2), "rock_dark"))
    P.append(box(C, (3.8, 5.0, 0.3), (0, -R - 5.0, z0), "wood"))                      # Zugbruecke
    for s in (-1, 1):
        P.append(beam(C, (s * 1.8, -R - 2.6, z0 + 5.6), (s * 1.8, -R - 7.4, z0 + 0.3), 0.06, "metal_dark"))
    P += facade(C, 0, -R, 9.0, 5.0, [z0 + 9.0], spacing=2.5, win=(0.35, 1.2), color="black", sides="fb",
                margin=1.5)
    # Ecktuerme
    for (sx, sy) in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        x, y = sx * R, sy * R
        P.append(cyl(C, 3.4, 13.0, (x, y, z0), "rock", segs=12))
        P.append(cyl(C, 3.8, 0.8, (x, y, z0 + 12.6), "rock_dark", segs=12))
        for k in range(3):
            a = math.atan2(sy, sx) + (k - 1) * 0.6
            P.append(box(C, (0.4, 0.1, 1.1), (x + 3.42 * math.cos(a), y + 3.42 * math.sin(a), z0 + 6.0 + k * 1.5),
                         "black", rot=(0, 0, math.degrees(a) + 90)))
        if sy > 0:
            P.append(cyl(C, 3.9, 6.5, (x, y, z0 + 13.4), "roof_red", segs=12, r_top=0.15))
            P.append(cyl(C, 0.05, 1.8, (x, y, z0 + 19.8), "metal_dark", segs=4))
            P.append(box(C, (0.9, 0.03, 0.55), (x + 0.47, y, z0 + 21.0), "red"))
        else:
            P += ring_merlons(C, x, y, 3.45, z0 + 13.4, n=10, h=1.1)
    # Bergfried
    kx, ky, kw = 6.0, 5.0, 8.0
    P.append(box(C, (kw, kw, 22.0), (kx, ky, z0), "rock"))
    P.append(box(C, (kw + 0.6, kw + 0.6, 0.6), (kx, ky, z0 + 21.4), "rock_dark"))
    for a, b in (((kx - kw / 2, ky - kw / 2), (kx + kw / 2, ky - kw / 2)),
                 ((kx + kw / 2, ky + kw / 2), (kx - kw / 2, ky + kw / 2)),
                 ((kx - kw / 2, ky + kw / 2), (kx - kw / 2, ky - kw / 2)),
                 ((kx + kw / 2, ky - kw / 2), (kx + kw / 2, ky + kw / 2))):
        P += merlons(C, a, b, z0 + 22.0, 0.6, 1.2)
    P += facade(C, kx, ky, kw, kw, [z0 + 8.0, z0 + 13.0, z0 + 18.0], spacing=3.0, win=(0.5, 1.5), color="black",
                margin=1.0)
    P += flagpole(C, kx, ky, 26.0, "red", "yellow")
    # Palas (Wohnbau) an der linken Mauer
    px, py = -9.5, 2.0
    P.append(box(C, (7.0, 16.0, 7.5), (px, py, z0), "plaster_warm"))
    P += gable(C, 16.0, 7.0, 4.0, (px, py, z0 + 7.5), "roof_red", overhang=0.3, rot=(0, 0, 90),
               gable_color="plaster_warm")
    P += facade(C, px, py, 7.0, 16.0, [z0 + 2.0, z0 + 5.4], spacing=3.0, win=(0.9, 1.4), color="glass", sides="r",
                margin=1.5, frame="wood_dark")
    P += half_timber(C, px, py, 7.0, 16.0, z0 + 3.8, 3.7, 1, sides="r")
    P += door(C, px + 3.5, py - 4.5, 1.2, 2.2, "wood_dark", z0, normal=(1, 0, 0))
    # Burghof: Brunnen, Karren, Kisten
    P.append(cyl(C, 1.1, 0.9, (3.0, -6.0, z0), "rock", segs=10))
    P.append(cyl(C, 0.85, 0.05, (3.0, -6.0, z0 + 0.88), "water", segs=10))
    for s in (-1, 1):
        P.append(box(C, (0.15, 0.15, 2.2), (3.0 + s * 0.9, -6.0, z0 + 0.9), "wood_dark"))
    P += gable(C, 2.4, 1.4, 0.6, (3.0, -6.0, z0 + 3.1), "roof_red", overhang=0.1)
    P += crates(C, 9.0, -9.0, 3, seed=5)
    for x, y in ((-18.0, -18.0), (18.0, -19.0), (-19.0, 17.0), (19.0, 18.0), (-19.5, 0.0), (19.5, -4.0)):
        P += tree(C, x, y, 7.0, 1.9, "pine" if x * y > 0 else "round", seed=int(x + y))
    return P


# ------------------------------------------------------------------ Ruine
def ruin(C):
    """Burgruine (22 x 18 m): eingefallene Mauern mit gezackter Krone, Bogenrest, Geroell, Bewuchs."""
    rnd = random.Random(21)
    P = [box(C, (22.0, 18.0, 0.1), (0, 0, 0), "grass")]
    z0 = 0.1

    def jag(x0, x1, y, heights, t=1.4, vertical=False):
        n = len(heights)
        step = (x1 - x0) / n
        out = []
        for i, h in enumerate(heights):
            c = x0 + step * (i + 0.5)
            col = "rock" if i % 3 else "rock_dark"
            if vertical:
                out.append(box(C, (t, step + 0.02, h), (y, c, z0), col))
            else:
                out.append(box(C, (step + 0.02, t, h), (c, y, z0), col))
        return out

    P += jag(-8.0, 8.0, 6.0, [5.5, 7.0, 7.8, 7.2, 6.0, 3.0, 1.6, 2.4, 4.5, 6.4, 6.8, 5.2])
    P += jag(-6.0, 6.0, -8.0, [5.0, 6.4, 6.0, 4.2, 2.6, 1.2, 0.8, 1.4, 2.8, 3.6], vertical=True)
    P += jag(-6.0, 4.0, 8.0, [2.2, 3.4, 1.4, 0.8, 1.6, 2.6, 1.0], vertical=True)
    P += jag(-8.0, -2.0, -6.0, [4.0, 3.0, 1.8, 1.0])
    # Fensterhoehlen in der Rueckwand (dunkle Nischen)
    for x in (-4.6, 4.8):
        P.append(box(C, (1.0, 0.1, 1.8), (x, 5.25, z0 + 3.4), "black"))
    # Bogenrest
    for x in (2.0, 6.0):
        P.append(box(C, (1.2, 1.2, 4.2), (x, -4.0, z0), "rock"))
    arc = [(2.0 * math.cos(math.pi * k / 6), 4.2 + 1.8 * math.sin(math.pi * k / 6)) for k in range(7)]
    inner = [(1.4 * math.cos(math.pi * k / 6), 4.2 + 1.2 * math.sin(math.pi * k / 6)) for k in range(6, -1, -1)]
    a = prism(C, arc[:5] + inner[2:], 1.2, (0, 0, 0), "rock")     # Bogen nur zur Haelfte erhalten
    a.data.transform(Matrix.Translation((4.0, -3.4, z0)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0),
                                                                    (0, 0, 0, 1))))
    P.append(a)
    # Geroell + Bewuchs
    for i in range(14):
        x, y = rnd.uniform(-9, 9), rnd.uniform(-7.5, 7.5)
        P.append(mound(C, rnd.uniform(0.4, 1.1), rnd.uniform(0.4, 0.9), rnd.uniform(0.3, 0.7), (x, y, z0),
                       rnd.choice(("rock", "rock_dark")), seed=i, subdiv=1))
    for i in range(6):
        x, y = rnd.uniform(-9, 9), rnd.uniform(-7.5, 7.5)
        r = rnd.uniform(0.6, 1.1)
        P.append(sphere(C, r, (x, y, z0 + r * 0.45), rnd.choice(("green", "green_dark")), subdiv=1,
                        scale=(1, 1, 0.7)))
    for (x, y, z) in ((-6.0, 6.6, 4.5), (6.5, 6.6, 3.6), (-8.6, -1.0, 3.0)):          # Efeu
        P.append(box(C, (2.2, 0.25, 2.6), (x, y, z), "green_dark", bevel=0.1))
    P += tree(C, 0.5, 1.5, 8.0, 2.4, "round", seed=4)
    P += tree(C, -9.0, -7.0, 6.0, 1.7, "pine", seed=9)
    return P


# ------------------------------------------------------------------ Brunnen
def fountain(C):
    """Platzbrunnen (14 x 14 m): achteckiges Becken, zweistufige Schale, Fontaene, Baenke und Laternen."""
    P = [cyl(C, 7.0, 0.1, (0, 0, 0), "concrete", segs=8),
         cyl(C, 5.2, 0.12, (0, 0, 0), "concrete_dark", segs=8)]
    P.append(lathe(C, [(3.6, 0.8), (4.2, 0.8), (4.2, 0.1), (3.6, 0.1), (3.6, 0.8)], (0, 0, 0), "offwhite", segs=8))
    P.append(cyl(C, 3.62, 0.55, (0, 0, 0.1), "water", segs=8))
    P.append(lathe(C, [(0, 0.6), (0.7, 0.6), (0.5, 1.5), (0.4, 1.7), (1.9, 1.8), (2.0, 2.05), (1.85, 2.05), (0.35, 2.2),
                       (0.3, 3.3), (1.05, 3.4), (1.1, 3.6), (0.95, 3.6), (0.2, 3.7), (0.15, 4.3), (0, 4.4)],
                   (0, 0, 0), "offwhite", segs=10))
    P.append(cyl(C, 1.86, 0.05, (0, 0, 1.98), "water", segs=10))
    P.append(cyl(C, 0.96, 0.05, (0, 0, 3.53), "water", segs=10))
    P.append(cyl(C, 0.2, 1.4, (0, 0, 4.3), "foam", segs=8, r_top=0.04))                   # Fontaene
    for k in range(8):                                                                       # Wasserstrahlen
        a = 2 * math.pi * k / 8
        c, s = math.cos(a), math.sin(a)
        P.append(beam(C, (1.95 * c, 1.95 * s, 1.98), (2.35 * c, 2.35 * s, 0.66), 0.09, "foam"))
        P.append(beam(C, (1.0 * c, 1.0 * s, 3.53), (1.3 * c, 1.3 * s, 2.0), 0.07, "foam"))
        P.append(cyl(C, 0.22, 0.03, (2.38 * c, 2.38 * s, 0.65), "foam", segs=6))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        x, y = 5.8 * math.cos(a), 5.8 * math.sin(a)
        P += bench(C, x, y, face_center(x, y))
        a2 = k * math.pi / 2
        P += lamp(C, 6.2 * math.cos(a2), 6.2 * math.sin(a2), 0, h=4.0)
    return P


# ------------------------------------------------------------------ Obelisk
def obelisk(C):
    """Obelisk-Denkmal (10 x 10 m): Stufensockel, 14 m Schaft mit Spitze, Pfosten mit Kette, Blumenbeete."""
    P = [box(C, (10.0, 10.0, 0.1), (0, 0, 0), "gravel")]
    for k, w in enumerate((6.0, 4.6, 3.2)):
        P.append(box(C, (w, w, 0.5 if k < 2 else 1.6), (0, 0, 0.1 + k * 0.5), ("concrete", "concrete_dark",
                                                                              "offwhite")[k]))
    z = 2.7
    P.append(lathe(C, [(0, z), (1.27, z), (0.85, z + 13.0), (0, z + 14.2)], (0, 0, 0), "offwhite", segs=4,
                   phase=math.pi / 4))
    P.append(box(C, (1.6, 0.1, 0.9), (0, -1.65, 1.4), "gold"))
    for i in range(8):
        a = 2 * math.pi * i / 8 + math.pi / 8
        x, y = 4.2 * math.cos(a), 4.2 * math.sin(a)
        P.append(cyl(C, 0.12, 0.9, (x, y, 0.1), "black", segs=6))
        P.append(sphere(C, 0.15, (x, y, 1.0), "black", subdiv=0))
        b = a + 2 * math.pi / 8
        P.append(beam(C, (x, y, 0.85), (4.2 * math.cos(b), 4.2 * math.sin(b), 0.85), 0.04, "black"))
    for x, y in ((-4.0, 0), (4.0, 0), (0, 4.0)):
        P += flower_bed(C, x * 1.08, y * 1.08, 1.4 if x else 3.0, 3.0 if x else 1.4, seed=int(x + y * 3))
    return P


# ------------------------------------------------------------------ Denkmal (Statue)
def statue(C):
    """Standbild (8 x 8 m): Stufensockel mit Inschrift, Figur in Patina-Gruen mit erhobenem Arm."""
    P = [box(C, (8.0, 8.0, 0.1), (0, 0, 0), "concrete")]
    P.append(box(C, (4.2, 4.2, 0.45), (0, 0, 0.1), "concrete_dark"))
    P.append(box(C, (2.6, 2.6, 2.8), (0, 0, 0.55), "offwhite"))
    P.append(box(C, (3.0, 3.0, 0.35), (0, 0, 3.35), "concrete"))
    P.append(box(C, (1.6, 0.08, 0.8), (0, -1.33, 1.6), "gold"))
    z, col = 3.7, "teal"
    for s in (-1, 1):
        P.append(cyl(C, 0.2, 1.5, (s * 0.24, 0.0, z), col, segs=8, r_top=0.22))
    P.append(lathe(C, [(0, z + 0.9), (0.62, z + 0.9), (0.5, z + 1.8), (0.48, z + 2.5), (0.25, z + 2.75),
                       (0, z + 2.8)], (0, 0, 0), col, segs=10))
    P.append(sphere(C, 0.3, (0, 0, z + 3.05), col, subdiv=1, scale=(0.9, 1, 1.1)))
    P += polyline_pipe(C, [(0.48, 0, z + 2.5), (0.75, -0.15, z + 3.1), (0.85, -0.25, z + 3.75)], r=0.13, color=col,
                       segs=6)
    P += polyline_pipe(C, [(-0.48, 0, z + 2.5), (-0.62, -0.25, z + 1.9), (-0.45, -0.5, z + 1.6)], r=0.13, color=col,
                       segs=6)
    P.append(box(C, (0.5, 0.12, 0.65), (-0.45, -0.6, z + 1.25), col))                        # Buch
    P.append(cyl(C, 0.6, 0.06, (0, 0.6, z + 2.0), col, segs=8, rot=(80, 0, 0)))              # Mantelsaum
    for s in (-1, 1):
        P += flower_bed(C, s * 3.0, 0, 1.2, 3.6, seed=s + 4)
        P += bench(C, s * 2.4, -3.2, 0)
    return P


# ------------------------------------------------------------------ Musikpavillon
def bandstand(C):
    """Musikpavillon (12 x 12 m): achteckiges Podest mit Treppe, 8 Saeulen, Gelaender, Zeltdach."""
    P = [cyl(C, 6.4, 0.1, (0, 0, 0), "gravel", segs=16)]
    P.append(cyl(C, 5.0, 1.0, (0, 0, 0.1), "offwhite", segs=8))
    P.append(cyl(C, 5.15, 0.12, (0, 0, 1.1), "wood", segs=8))
    r = 4.6
    pts = [(r * math.cos(2 * math.pi * i / 8 + math.pi / 8), r * math.sin(2 * math.pi * i / 8 + math.pi / 8))
           for i in range(8)]
    for x, y in pts:
        P.append(cyl(C, 0.16, 3.2, (x, y, 1.22), "white", segs=8))
    for i in range(8):
        a, b = pts[i], pts[(i + 1) % 8]
        mx, my = (a[0] + b[0]) / 2, (a[1] + b[1]) / 2
        if my < -3.0:                       # vorne offen (Treppe)
            continue
        P.append(beam(C, (a[0], a[1], 2.1), (b[0], b[1], 2.1), 0.08, "white"))
        n = 6
        for k in range(1, n):
            t = k / n
            P.append(box(C, (0.05, 0.05, 0.9), (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, 1.22), "white"))
        # Zierbogen oben
        P.append(beam(C, (a[0], a[1], 4.1), (b[0], b[1], 4.1), 0.12, "white"))
    P.append(lathe(C, [(5.0, 4.4), (5.6, 4.4), (5.6, 4.0), (5.0, 4.0)], (0, 0, 0), "red", segs=8, phase=math.pi / 8))
    P.append(cyl(C, 5.9, 2.8, (0, 0, 4.4), "roof_green", segs=8, r_top=0.3, rot=(0, 0, 22.5)))
    P.append(cyl(C, 0.3, 0.6, (0, 0, 7.2), "white", segs=8))
    P.append(sphere(C, 0.25, (0, 0, 7.95), "gold", subdiv=1))
    for k in range(4):
        P.append(box(C, (3.0, 0.45, 1.0 - k * 0.25), (0, -4.6 - k * 0.45, 0.1), "offwhite"))
    for k, (x, y) in enumerate(((-1.2, 0.8), (0.8, 1.4), (1.5, -0.5))):
        P.append(box(C, (0.5, 0.5, 0.45), (x, y, 1.22), "wood_dark"))
        P.append(box(C, (0.05, 0.05, 1.1), (x + 0.6, y, 1.22), "metal_dark"))
    for i in range(6):
        a = math.pi * 0.25 + i * math.pi * 0.3
        x, y = 6.9 * math.cos(a), 6.9 * math.sin(a)
        if y < -3:
            continue
        P += bench(C, x, y, face_center(x, y))
    return P


LM = {"Church": church, "TownHall": town_hall, "Museum": museum, "Castle": castle, "Ruin": ruin,
      "Fountain": fountain, "Obelisk": obelisk, "Statue": statue, "Bandstand": bandstand}
run_family(LM, "Landmarks", views=V)
