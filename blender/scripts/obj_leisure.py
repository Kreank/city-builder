"""Sport & Freizeit (Models/Leisure): Stadion, Sportplatz, Freibad, Tennis, Spielplatz, Riesenrad ("Wheel" separat)."""
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.2, 0.8)}


def superellipse(a, b, n=32, p=4.0):
    pts = []
    for i in range(n):
        t = 2 * math.pi * i / n
        c, s = math.cos(t), math.sin(t)
        pts.append((a * math.copysign(abs(c) ** (2 / p), c), b * math.copysign(abs(s) ** (2 / p), s)))
    return pts


def pitch(C, w, d, z=0.0):
    P = [box(C, (w, d, 0.1), (0, 0, z), "green")]
    for k in range(0, 8, 2):
        P.append(box(C, (w / 8, d, 0.11), (-w / 2 + w / 16 + k * w / 8, 0, z), "grass"))
    lw = 0.15
    for (sx, sy, px, py) in ((w - 2, lw, 0, d / 2 - 1), (w - 2, lw, 0, -d / 2 + 1), (lw, d - 2, w / 2 - 1, 0),
                             (lw, d - 2, -w / 2 + 1, 0), (lw, d - 2, 0, 0)):
        P.append(box(C, (sx, sy, 0.12), (px, py, z), "white"))
    P.append(lathe(C, [(3.6, 0), (3.8, 0), (3.8, 0.12), (3.6, 0.12)], (0, 0, z), "white", segs=20))
    for s in (-1, 1):
        x = s * (w / 2 - 1)
        P.append(box(C, (0.15, 2.4, 2.4), (x, 0, z), "white"))
        P.append(box(C, (1.2, 0.12, 2.4), (x - s * 0.6, -1.2, z), "white"))
        P.append(box(C, (1.2, 0.12, 2.4), (x - s * 0.6, 1.2, z), "white"))
        P.append(box(C, (1.4, 2.6, 0.12), (x - s * 0.6, 0, z + 2.4), "white"))
        for (bw, bd) in ((6.0, 0.15), (0.15, 12.0)):
            pass
        P.append(box(C, (0.15, 14.0, 0.12), (x - s * 6.0, 0, z), "white"))
        P.append(box(C, (6.0, 0.15, 0.12), (x - s * 3.0, 7.0, z), "white"))
        P.append(box(C, (6.0, 0.15, 0.12), (x - s * 3.0, -7.0, z), "white"))
    return P


def floodlight(C, x, y, h=28.0):
    return [box(C, (0.8, 0.8, h), (x, y, 0), "metal"),
            box(C, (4.0, 0.6, 2.4), (x, y, h), "metal_dark", rot=(0, 0, math.degrees(math.atan2(-y, -x)) + 90)),
            box(C, (3.6, 0.1, 2.0), (x - 0.0, y, h + 0.2), "light_on", rot=(0, 0, math.degrees(math.atan2(-y, -x)) + 90))]


def stadium(C):
    """Stadion 80 x 64 m: Spielfeld, umlaufende Tribuene (gerundetes Rechteck), Dachring, vier Flutlichtmasten."""
    P = [box(C, (80.0, 64.0, 0.2), (0, 0, 0), "concrete")]
    P += pitch(C, 50.0, 32.0, 0.2)
    a0, b0 = 28.0, 19.0
    rings = [(superellipse(a0, b0), 0.2), (superellipse(a0, b0), 1.2), (superellipse(a0 + 10.0, b0 + 10.0), 13.0),
             (superellipse(a0 + 11.0, b0 + 11.0), 13.0), (superellipse(a0 + 11.0, b0 + 11.0), 0.2)]
    P.append(loft(C, rings, ["concrete", "steel_blue", "concrete", "concrete_dark"], name="stands"))
    # Sitzreihen-Stufen als hellere Baender
    for k in range(1, 4):
        f = k / 4
        P.append(loft(C, [(superellipse(a0 + 10 * f, b0 + 10 * f), 1.2 + 11.8 * f + 0.02),
                          (superellipse(a0 + 10 * f + 0.6, b0 + 10 * f + 0.6), 1.2 + 11.8 * f + 0.02 + 0.7)],
                      ["red"], name="row"))
    # Dachring (schwebend auf Stuetzen)
    P.append(loft(C, [(superellipse(a0 + 3.0, b0 + 3.0), 18.0), (superellipse(a0 + 12.0, b0 + 12.0), 19.0),
                      (superellipse(a0 + 12.0, b0 + 12.0), 19.6), (superellipse(a0 + 3.0, b0 + 3.0), 18.6)],
                  ["white", "white", "white"], top=None, name="roof"))
    for (x, y) in superellipse(a0 + 11.5, b0 + 11.5, 12):
        P.append(box(C, (0.8, 0.8, 19.0), (x, y, 0.2), "metal"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            P += floodlight(C, sx * 37.0, sy * 29.0, 30.0)
    # Eingang
    P.append(box(C, (12.0, 3.0, 6.0), (0, -30.5, 0.2), "steel_blue"))
    P += sign(C, 0, -32.05, 4.6, 10.0, 1.2, "white", "steel_blue")
    return P


def sports_field(C):
    """Sportplatz 48 x 32 m: Fussballfeld, kleine Tribuene, Zaun, Vereinsheim."""
    P = [box(C, (48.0, 32.0, 0.1), (0, 0, 0), "grass")]
    P += pitch(C, 40.0, 24.0, 0.1)
    for k in range(4):
        P.append(box(C, (16.0, 1.0, 0.5 + k * 0.5), (0, 13.0 + k * 0.6 - 0.6, 0.1), ("concrete", "red", "red", "red")[k]))
    P.append(box(C, (17.0, 3.2, 0.2), (0, 13.6, 3.4), "metal"))
    for x in (-8.0, 8.0):
        P.append(box(C, (0.2, 0.2, 3.3), (x, 14.9, 0.1), "metal_dark"))
    P.append(box(C, (8.0, 5.0, 3.2), (-18.0, 12.0, 0.1), "cream"))
    P.append(gable(C, 8.0, 5.0, 1.6, (-18.0, 12.0, 3.3), "roof_red", overhang=0.3)[0])
    P += fence(C, [(-23.8, -15.8), (23.8, -15.8), (23.8, 15.8), (-23.8, 15.8)], h=2.6, spacing=4.0, post="metal_dark",
               rail="metal", gaps=(2,))
    for (x, y) in ((-21.0, -14.0), (21.0, -14.0), (-21.0, 14.0), (21.0, 14.0)):
        P += floodlight(C, x, y, 16.0)
    return P


def outdoor_pool(C):
    """Freibad 40 x 32 m: Schwimmerbecken, Nichtschwimmer, Rutsche, Sprungturm, Liegewiese, Kassenhaus."""
    P = [box(C, (40.0, 32.0, 0.1), (0, 0, 0), "grass"), box(C, (26.0, 18.0, 0.15), (-4.0, -3.0, 0.1), "cream")]
    P.append(box(C, (20.0, 10.0, 0.08), (-6.0, -5.0, 0.22), "water"))
    for k in range(1, 5):
        P.append(box(C, (20.0, 0.1, 0.09), (-6.0, -10.0 + k * 2.0, 0.22), "white"))
    P.append(box(C, (8.0, 5.0, 0.08), (-6.0, 4.5, 0.22), "blue_light"))
    # Sprungturm
    for x in (-15.0, -14.0):
        P.append(box(C, (0.3, 0.3, 5.0), (x, -5.0, 0.25), "white"))
    P.append(box(C, (2.0, 3.0, 0.2), (-14.5, -6.0, 5.0), "blue"))
    P.append(box(C, (0.6, 2.6, 0.08), (-14.5, -8.0, 5.0), "white"))
    P.append(box(C, (0.6, 2.4, 0.08), (-14.5, -7.0, 3.0), "white"))
    # Rutsche (Kurve aus Segmenten)
    pts = [(6.0, 6.0, 6.0), (8.0, 3.0, 5.0), (8.0, -1.0, 3.5), (5.5, -3.0, 2.0), (3.5, -4.0, 0.6)]
    for a, b in zip(pts[:-1], pts[1:]):
        P.append(beam(C, a, b, 1.0, "yellow", t2=0.3))
    P.append(box(C, (1.6, 1.6, 6.0), (6.0, 6.0, 0.1), "metal"))
    # Liegewiese mit Schirmen und Handtuechern
    for k in range(6):
        x, y = 12.0 + (k % 2) * 4.0, -10.0 + (k // 2) * 6.0
        P.append(cyl(C, 0.05, 2.2, (x, y, 0.1), "white", segs=6))
        P.append(cyl(C, 1.2, 0.4, (x, y, 2.0), ("red", "blue", "yellow")[k % 3], segs=8, r_top=0.05))
        P.append(box(C, (0.9, 1.8, 0.03), (x + 1.0, y, 0.1), ("teal", "orange", "flower_pink")[k % 3]))
    # Kassenhaus / Umkleiden
    P.append(box(C, (12.0, 4.0, 3.2), (-6.0, 13.5, 0.1), "pastel_blue"))
    P += flat_roof(C, 12.0, 4.0, 3.3, (-6.0, 13.5), ph=0.3)
    P += sign(C, -6.0, 11.4, 2.5, 6.0, 0.6, "blue", "white")
    P += tree(C, 16.0, 12.0, 7.0, 2.4, seed=3)
    P += tree(C, 10.0, 13.0, 6.0, 2.0, seed=4)
    P += fence(C, [(-19.8, -15.8), (19.8, -15.8), (19.8, 15.8), (-19.8, 15.8)], h=1.8, spacing=3.0)
    return P


def tennis(C):
    """Tennisanlage 32 x 24 m: zwei Sandplaetze mit Netz und Zaun."""
    P = [box(C, (32.0, 24.0, 0.1), (0, 0, 0), "grass")]
    for cx in (-7.5, 7.5):
        P.append(box(C, (13.0, 22.0, 0.08), (cx, 0, 0.1), "court_orange"))
        for (sx, sy, px, py) in ((10.9, 0.08, 0, 11.9), (10.9, 0.08, 0, -11.9), (0.08, 23.8, 5.45, 0), (0.08, 23.8, -5.45, 0),
                                 (0.08, 23.8, 4.1, 0), (0.08, 23.8, -4.1, 0), (8.2, 0.08, 0, 6.4), (8.2, 0.08, 0, -6.4),
                                 (0.08, 12.8, 0, 0)):
            P.append(box(C, (sx, min(sy, 21.0), 0.09), (cx + px * 0.95, py * 0.88, 0.1), "white"))
        P.append(box(C, (11.6, 0.05, 0.95), (cx, 0, 0.1), "white"))
        for s in (-1, 1):
            P.append(box(C, (0.1, 0.1, 1.1), (cx + s * 5.9, 0, 0.1), "metal_dark"))
    P += fence(C, [(-15.8, -11.8), (15.8, -11.8), (15.8, 11.8), (-15.8, 11.8)], h=3.2, spacing=3.2,
               post="green_dark", rail="green_dark")
    P += bench(C, 0, -10.6, 0)
    return P


def playground(C):
    """Spielplatz 16 x 16 m: Schaukel, Rutsche, Sandkasten, Wippe, Klettergeruest."""
    P = [box(C, (16.0, 16.0, 0.1), (0, 0, 0), "grass"), box(C, (12.0, 12.0, 0.05), (0, 0, 0.1), "sand")]
    # Schaukel
    for x in (-5.5, -1.5):
        P.append(beam(C, (x, 2.2, 0.1), (x, 3.5, 2.6), 0.12, "red"))
        P.append(beam(C, (x, 4.8, 0.1), (x, 3.5, 2.6), 0.12, "red"))
    P.append(box(C, (4.3, 0.15, 0.15), (-3.5, 3.5, 2.55), "red"))
    for x in (-4.5, -2.5):
        for s in (-0.2, 0.2):
            P.append(box(C, (0.03, 0.03, 1.9), (x + s, 3.5, 0.65), "metal"))
        P.append(box(C, (0.5, 0.25, 0.06), (x, 3.5, 0.6), "black"))
    # Rutsche mit Turm
    P.append(box(C, (1.6, 1.6, 0.15), (3.5, 3.5, 1.6), "wood"))
    for (x, y) in ((2.8, 2.8), (4.2, 2.8), (2.8, 4.2), (4.2, 4.2)):
        P.append(box(C, (0.15, 0.15, 2.8), (x, y, 0.1), "wood_dark"))
    P.append(hip(C, 1.8, 1.8, 1.0, (3.5, 3.5, 2.9), "blue", overhang=0.1))
    P.append(beam(C, (3.5, 2.7, 1.75), (3.5, -0.6, 0.3), 0.7, "yellow", t2=0.1))
    P.append(beam(C, (4.3, 4.2, 0.1), (4.3, 5.4, 0.1), 0.1, "wood_dark"))
    # Sandkasten, Wippe, Klettergeruest
    P.append(box(C, (3.0, 3.0, 0.35), (-4.0, -3.5, 0.1), "wood"))
    P.append(box(C, (2.6, 2.6, 0.1), (-4.0, -3.5, 0.4), "sand"))
    P.append(box(C, (0.4, 0.3, 0.5), (1.0, -4.0, 0.1), "metal_dark"))
    P.append(box(C, (3.2, 0.3, 0.1), (1.0, -4.0, 0.62), "green", rot=(0, 10, 0)))
    for k in range(4):
        for j in range(3):
            P.append(beam(C, (4.0 + k * 0.6 - 0.9, -3.0, 0.1 + j * 0.6), (4.0 + k * 0.6 - 0.9, -4.8, 0.1 + j * 0.6), 0.06,
                          ("red", "blue", "yellow")[j]))
    for k in range(4):
        P.append(box(C, (0.08, 0.08, 1.9), (3.1 + k * 0.6, -3.0, 0.1), "metal"))
        P.append(box(C, (0.08, 0.08, 1.9), (3.1 + k * 0.6, -4.8, 0.1), "metal"))
    P += bench(C, -6.6, 0.0, 90)
    P += bench(C, 6.6, 0.0, 270)
    P += tree(C, -6.8, 6.8, 5.5, 1.8, seed=2)
    P += tree(C, 6.8, -6.8, 5.5, 1.8, seed=5)
    P += fence(C, [(-7.9, -7.9), (7.9, -7.9), (7.9, 7.9), (-7.9, 7.9)], h=1.0, spacing=2.0, post="wood", rail="wood",
               gaps=(0,))
    return P


def ferris_wheel(C):
    """Riesenrad 24 x 24 m, 32 m hoch. "Wheel" dreht um Unity-Z (Blender -Y), Gondeln sind fest am Rad."""
    P = [box(C, (24.0, 24.0, 0.2), (0, 0, 0), "concrete")]
    hub = Vector((0, 0, 17.8))   # unterste Gondel bleibt ueber dem Boden
    for sy in (-1.5, 1.5):
        for sx in (-1, 1):
            P.append(beam(C, (sx * 8.0, sy * 2.2, 0.2), (0, sy, hub.z), 0.6, "white"))
    P.append(cyl(C, 0.6, 3.4, (0, -1.7, hub.z), "metal_dark", segs=8, rot=(-90, 0, 0)))
    P.append(box(C, (6.0, 4.0, 3.0), (0, 6.0, 0.2), "red"))                  # Kasse
    P.append(hip(C, 6.0, 4.0, 1.5, (0, 6.0, 3.2), "white", overhang=0.3))
    W = []
    R = 15.0
    for side in (-0.9, 0.9):
        ring = lathe(C, [(R - 0.25, 0), (R + 0.25, 0), (R + 0.25, 0.3), (R - 0.25, 0.3)], (0, 0, 0), "white", segs=24)
        ring.data.transform(Matrix.Translation((0, side, hub.z)) @ Matrix.Rotation(math.radians(-90), 4, 'X'))
        W.append(ring)
    cols = ("red", "yellow", "blue", "green", "orange", "teal")
    for k in range(12):
        a = 2 * math.pi * k / 12
        p = Vector((R * math.cos(a), 0, hub.z + R * math.sin(a)))
        for side in (-0.9, 0.9):
            W.append(beam(C, (0, side, hub.z), (p.x, side, p.z), 0.12, "white"))
        W.append(beam(C, (p.x, -1.0, p.z), (p.x, 1.0, p.z), 0.15, "white"))
        W.append(box(C, (1.6, 1.6, 1.8), (p.x, 0, p.z - 2.2), cols[k % len(cols)], bevel=0.15))
        W.append(box(C, (1.62, 1.0, 0.7), (p.x, 0, p.z - 1.4), "glass_light"))
        W.append(box(C, (0.08, 0.08, 0.5), (p.x, 0, p.z - 0.45), "metal"))
    return {"FerrisWheel": (P, (0, 0, 0)), "Wheel": (W, tuple(hub))}


LEI = {"Stadium": stadium, "SportsField": sports_field, "OutdoorPool": outdoor_pool, "TennisCourts": tennis,
       "Playground": playground, "FerrisWheel": ferris_wheel}
run_family(LEI, "Leisure", views=V)
