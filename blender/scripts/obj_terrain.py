"""Gelaende (Models/Terrain): Klippen-Kacheln 16 x 16 m (gerade, Aussenecke, mit Wasserfall), Huegel, Felsgruppe,
Teich, Fluss gerade / Kurve (16 x 16 m). Ursprung = Mitte Grundflaeche auf Bodenhoehe, Vorderseite = -Y.

Kachel-Regeln (nahtlos aneinanderlegbar):
- Klippe: Plateau hinten (+Y) auf z = 10 m, Abbruch vorne. Die Felskante liegt an den Kachelraendern in jeder Hoehe
  exakt bei y = -8 + SETBACK(z) (Zufallsversatz nur innen), daher passen gerade Stuecke und Ecken lueckenlos.
- Aussenecke: Plateau im hinteren rechten Viertel; Raender x = +8 und y = +8 passen an ein gerades Stueck
  (bei y = +8 ein um 90 Grad gedrehtes).
- Fluss: 10 m breit, Wasser auf z = 0,05, Ufer bis z = 0,4; gerade = entlang Y, Kurve verbindet Rand -Y mit Rand -X."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.3, 0.7)}
H = 10.0
LEVELS = [(0.0, 0.0), (2.5, 0.7), (5.0, 1.2), (7.5, 2.1), (H, 2.8)]       # (z, Ruecksprung der Felskante)
BANDS = ["rock_dark", "rock", "rock", "rock_dark"]


def jitter(rnd, n, amp):
    """n Zufallswerte, Raender 0 (nahtlos)."""
    return [0.0 if i in (0, n - 1) else rnd.uniform(-amp, amp) for i in range(n)]


def cliff_loft(C, front_fn, back, n, seed):
    """front_fn(t, setback) -> (x, y) fuer t in [0, 1]; back = Liste der hinteren Eckpunkte (Ringschluss)."""
    rnd = random.Random(seed)
    js = [jitter(rnd, n + 1, 0.9 if k else 0.0) for k in range(len(LEVELS))]
    secs = []
    for k, (z, sb) in enumerate(LEVELS):
        pts = []
        for i in range(n + 1):
            t = i / n
            x, y = front_fn(t, sb + js[k][i])
            pts.append((x, y))
        secs.append((pts + back, z))
    return loft(C, secs, BANDS, top="grass", bottom=None, name="cliff")


def top_decor(C, spots, seed):
    P = []
    for k, (x, y) in enumerate(spots):
        T = tree(C, x, y, 6.0 + (k % 3), 1.8, "pine" if k % 2 == 0 else "round", seed=seed + k)
        for o in T:
            o.data.transform(Matrix.Translation((0, 0, H)))
        P += T
    return P


def base_rocks(C, pts, seed):
    rnd = random.Random(seed)
    return [mound(C, rnd.uniform(0.8, 1.6), rnd.uniform(0.6, 1.2), rnd.uniform(0.6, 1.3), (x, y, 0),
                  rnd.choice(("rock", "rock_dark")), seed=seed + i, subdiv=1) for i, (x, y) in enumerate(pts)]


def cliff_straight(C):
    """Klippe gerade (16 x 16 m, 10 m hoch), Kante entlang X."""
    P = [box(C, (16.0, 16.0, 0.05), (0, 0, 0), "grass")]
    P.append(cliff_loft(C, lambda t, sb: (-8.0 + 16.0 * t, -8.0 + max(sb, 0.0) + 2.0), [(8.0, 8.0), (-8.0, 8.0)],
                        8, seed=3))
    P += base_rocks(C, [(-4.5, -6.6), (3.0, -6.8), (5.5, -6.2)], 10)
    P += top_decor(C, [(-4.0, 4.0), (3.5, 5.5), (6.0, 1.5)], 1)
    return P


def cliff_corner(C):
    """Klippe Aussenecke (16 x 16 m): Plateau hinten rechts, Kante als Viertelbogen um (8, 8)."""
    P = [box(C, (16.0, 16.0, 0.05), (0, 0, 0), "grass")]

    def front(t, sb):
        R = 14.0 - max(sb, 0.0)                      # passt zu gerade: Kante bei y = -8 + 2 + sb (R = 16 - 2 - sb)
        a = math.radians(180.0 + 90.0 * t)
        return 8.0 + R * math.cos(a), 8.0 + R * math.sin(a)

    P.append(cliff_loft(C, front, [(8.0, 8.0)], 10, seed=5))
    P += base_rocks(C, [(-5.0, -2.0), (-1.0, -6.5), (2.5, -7.0)], 20)
    P += top_decor(C, [(5.0, 5.0), (1.0, 6.0)], 4)
    return P


def cliff_waterfall(C):
    """Klippe gerade mit Wasserfall (16 x 16 m): Bach auf dem Plateau, Fall ueber die Kante, Becken unten."""
    P = cliff_straight(C)
    P.append(box(C, (3.2, 11.0, 0.12), (0.0, 2.5, H), "water"))
    P.append(box(C, (4.0, 11.0, 0.08), (0.0, 2.5, H - 0.02), "sand"))
    # fallendes Wasser: Streifen vor der Felskante (folgt grob dem Ruecksprung)
    # Profil in (y, z), entlang X 2,8 m extrudiert; liegt 1 m vor der Felskante (Zufallsversatz max. 0,9 m)
    YZ = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))       # (a, b, c) -> (x=c, y=a, z=b)
    for k in range(len(LEVELS) - 1):
        (z0, s0), (z1, s1) = LEVELS[k], LEVELS[k + 1]
        y0, y1 = -6.0 + s0 - 1.0, -6.0 + s1 - 1.0
        sheet = prism(C, [(y0 - 0.25, z0), (y0, z0), (y1, z1 + 0.05), (y1 - 0.25, z1 + 0.05)], 2.8, (0, 0, 0),
                      "foam" if k % 2 else "blue_light", name="fall")
        sheet.data.transform(Matrix.Translation((-1.4, 0, 0)) @ YZ)
        P.append(sheet)
    P.append(box(C, (2.8, 1.6, 0.12), (0, -6.0 + LEVELS[-1][1] - 0.6, H - 0.02), "water"))
    P.append(cyl(C, 2.2, 0.1, (0, -5.8, 0.0), "water", segs=12))      # Becken (bleibt in der Kachel)
    P.append(cyl(C, 1.4, 0.35, (0, -6.2, 0.0), "foam", segs=10, r_top=1.0))
    return P


def hill(C):
    """Huegel (32 x 32 m, 9 m hoch) mit Felsnase, Baeumen und Pfad."""
    from mathutils.bvhtree import BVHTree
    m = mound(C, 14.0, 14.0, 9.0, (0, 0, 0), "grass", seed=8, subdiv=3)
    bm = bmesh.new()
    bm.from_mesh(m.data)
    bvh = BVHTree.FromBMesh(bm)

    def ground(x, y):
        hit = bvh.ray_cast(Vector((x, y, 50.0)), Vector((0, 0, -1)))
        return hit[0].z if hit[0] else 0.0

    P = [m]
    P.append(mound(C, 3.5, 2.5, 4.5, (5.0, -4.0, ground(5.0, -4.0) - 1.5), "rock", seed=2, subdiv=1))
    cz = ground(0.3, 0.2)
    P.append(box(C, (0.15, 0.15, 1.8), (0.3, 0.2, cz - 0.2), "wood_dark"))                # Gipfelkreuz
    P.append(box(C, (0.9, 0.15, 0.15), (0.3, 0.2, cz + 1.0), "wood_dark"))
    for k, (x, y) in enumerate(((-6.0, 6.0), (-8.0, 1.0), (7.0, 7.0), (-3.0, 9.0), (10.0, -2.0))):
        z = ground(x, y) - 0.3
        T = tree(C, x, y, 6.0, 1.9, "pine" if k % 2 else "round", seed=k)
        for o in T:
            o.data.transform(Matrix.Translation((0, 0, z)))
        P += T
    bm.free()
    return P


def rocks(C):
    """Felsgruppe (10 x 8 m)."""
    P = [mound(C, 3.0, 2.4, 3.6, (-1.0, 0.5, 0), "rock", seed=1, subdiv=1),
         mound(C, 2.0, 1.8, 2.4, (2.4, -1.2, 0), "rock_dark", seed=2, subdiv=1),
         mound(C, 1.2, 1.0, 1.2, (-3.6, -2.2, 0), "rock", seed=3, subdiv=1),
         mound(C, 0.8, 0.7, 0.7, (3.8, 2.0, 0), "rock_dark", seed=4, subdiv=1),
         mound(C, 0.6, 0.5, 0.5, (0.6, -3.0, 0), "rock", seed=5, subdiv=1)]
    P.append(sphere(C, 0.9, (-3.0, 2.2, 0.4), "green_dark", subdiv=1, scale=(1, 1, 0.7)))
    P.append(sphere(C, 0.6, (1.5, 2.8, 0.3), "green", subdiv=1, scale=(1, 1, 0.7)))
    return P


def pond(C):
    """Teich (16 x 16 m): Wasser, Sandufer, Schilf, Seerosen, Steg."""
    rnd = random.Random(12)
    P = [box(C, (16.0, 16.0, 0.05), (0, 0, 0), "grass")]
    out = [(6.6 * math.cos(2 * math.pi * k / 16) * (1 + 0.1 * math.sin(3 * k)),
            5.6 * math.sin(2 * math.pi * k / 16) * (1 + 0.08 * math.cos(2 * k))) for k in range(16)]
    P.append(prism(C, [(x * 1.07, y * 1.12) for x, y in out], 0.08, (0, 0, 0.0), "sand", name="shore"))
    P.append(prism(C, out, 0.1, (0, 0, 0.0), "water", name="water"))
    for k in range(22):
        a = rnd.uniform(0, 2 * math.pi)
        if -1.9 < a < -1.2:
            continue
        r = rnd.uniform(0.95, 1.03)
        x, y = 6.6 * math.cos(a) * r, 5.6 * math.sin(a) * r
        for j in range(3):
            P.append(cyl(C, 0.05, rnd.uniform(1.0, 1.7), (x + rnd.uniform(-0.3, 0.3), y + rnd.uniform(-0.3, 0.3), 0),
                         rnd.choice(("green", "green_dark", "grass_dry")), segs=4, r_top=0.01))
    for k in range(6):
        x, y = rnd.uniform(-3.5, 3.5), rnd.uniform(-2.5, 3.0)
        P.append(cyl(C, rnd.uniform(0.35, 0.55), 0.04, (x, y, 0.1), "green", segs=7))
        if k % 2 == 0:
            P.append(sphere(C, 0.12, (x + 0.1, y, 0.16), "flower_pink", subdiv=0))
    P.append(box(C, (1.4, 4.0, 0.15), (-0.5, -5.0, 0.4), "wood"))                            # Steg
    for x in (-1.1, 0.1):
        for y in (-6.5, -3.5):
            P.append(box(C, (0.15, 0.15, 0.55), (x, y, 0), "wood_dark"))
    P += base_rocks(C, [(5.5, 4.5), (-6.0, -3.5)], 30)
    P += tree(C, -5.4, 5.4, 7.0, 2.2, "round", seed=3)
    return P


def river_straight(C):
    """Fluss gerade (16 x 16 m, entlang Y): 10 m Wasser, Sand- und Grasufer mit Steinen und Schilf."""
    P = [box(C, (16.0, 16.0, 0.05), (0, 0, -0.3), "sand"),
         box(C, (10.0, 16.0, 0.1), (0, 0, -0.05), "water")]
    for s in (-1, 1):
        P.append(prism(C, [(s * 5.0, -8.0), (s * 5.0, 8.0), (s * 6.0, 8.0), (s * 6.0, -8.0)], 0.1, (0, 0, -0.25),
                       "sand", name="beach"))
        P.append(box(C, (2.0, 16.0, 0.65), (s * 7.0, 0, -0.25), "grass"))
    rnd = random.Random(4)
    for k in range(10):
        s = 1 if k % 2 else -1
        x, y = s * rnd.uniform(5.0, 5.8), rnd.uniform(-7.0, 7.0)
        if k < 4:
            P.append(mound(C, 0.5, 0.4, 0.35, (x, y, -0.05), "rock", seed=k, subdiv=1))
        else:
            for j in range(3):
                P.append(cyl(C, 0.05, rnd.uniform(0.9, 1.4), (x + rnd.uniform(-0.3, 0.3), y + rnd.uniform(-0.3, 0.3),
                                                               -0.05), "green_dark", segs=4, r_top=0.01))
    return P


def river_bend(C):
    """Fluss-Kurve (16 x 16 m): verbindet Rand -Y (x = -5 .. 5) mit Rand -X (y = -5 .. 5), Bogen um (-8, -8)."""
    P = [box(C, (16.0, 16.0, 0.05), (0, 0, -0.3), "sand")]

    def ring(r0, r1, z, h, color, name, n=10):
        pts = [(-8.0 + r1 * math.cos(math.pi / 2 * k / n), -8.0 + r1 * math.sin(math.pi / 2 * k / n)) for k in range(n + 1)]
        pts += [(-8.0 + r0 * math.cos(math.pi / 2 * k / n), -8.0 + r0 * math.sin(math.pi / 2 * k / n))
                for k in range(n, -1, -1)]
        return prism(C, pts, h, (0, 0, z), color, name=name)

    P.append(ring(3.0, 13.0, -0.05, 0.1, "water", "water"))
    P.append(ring(2.0, 3.0, -0.25, 0.1, "sand", "beach_in"))
    P.append(ring(13.0, 14.0, -0.25, 0.1, "sand", "beach_out"))
    P.append(ring(0.01, 2.0, -0.25, 0.65, "grass", "bank_in", n=4))
    outer = [(-8.0 + 14.0 * math.cos(math.pi / 2 * k / 10), -8.0 + 14.0 * math.sin(math.pi / 2 * k / 10))
             for k in range(10, -1, -1)]
    P.append(prism(C, [(6.0, -8.0), (8.0, -8.0), (8.0, 8.0), (-8.0, 8.0), (-8.0, 6.0)] + outer[1:-1], 0.65,
                   (0, 0, -0.25), "grass", name="bank_out"))
    P += tree(C, 5.0, 5.0, 7.0, 2.2, "round", seed=2)
    P.append(mound(C, 0.6, 0.5, 0.4, (2.5, 4.5, 0.4), "rock", seed=3, subdiv=1))
    return P


TER = {"Cliff_Straight": cliff_straight, "Cliff_Corner": cliff_corner, "Cliff_Waterfall": cliff_waterfall,
       "Hill": hill, "Rocks": rocks, "Pond": pond, "River_Straight": river_straight, "River_Bend": river_bend}
run_family(TER, "Terrain", views=V)
