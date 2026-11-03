"""Infrastruktur (Models/Infrastructure): Klappbruecke ("LeafA"/"LeafB" klappen um X), Tunnelportal, Brueckenpfeiler,
Laermschutzwand, Schilderbruecke, Hochspannungsmast, Baustellen-Set.
Strassen laufen entlang Y (Fahrtrichtung), Breite entlang X."""
import sys, os, math, random
sys.path.insert(0, os.path.dirname(__file__))
import importlib, clib, bparts
importlib.reload(clib); importlib.reload(bparts)
from bparts import *

V = {"": (1.0, -1.2, 0.6)}


def deck(C, w, L, y0, z, rail=True):
    P = [box(C, (w, L, 0.8), (0, y0, z - 0.8), "concrete"), box(C, (w - 2.0, L, 0.06), (0, y0, z), "asphalt")]
    P.append(box(C, (0.15, L, 0.07), (0, y0, z), "white"))
    for s in (-1, 1):
        P.append(box(C, (1.0, L, 0.25), (s * (w / 2 - 0.5), y0, z), "concrete"))
        if rail:
            P.append(box(C, (0.1, L, 0.1), (s * (w / 2 - 0.1), y0, z + 1.1), "metal"))
            n = max(2, int(L / 2.0))
            for k in range(n + 1):
                P.append(box(C, (0.08, 0.08, 1.1), (s * (w / 2 - 0.1), y0 - L / 2 + k * L / n, z + 0.05), "metal"))
    return P


def drawbridge(C):
    """Klappbruecke (Spannweite 24 m, Breite 12 m) mit zwei Klappen und Bruckenhaeuschen.
    Fahrbahnhoehe 3 m ueber Wasser. Pfeiler reichen bis -4 m. LeafA (vorne, -Y) / LeafB (hinten, +Y)
    drehen um ihre Achse am Pfeiler (Unity: um lokale X-Achse)."""
    w, Z = 12.0, 3.0
    P = []
    for s in (-1, 1):
        y = s * 16.0
        P.append(box(C, (w + 2.0, 8.0, Z + 4.0), (0, y, -4.0), "concrete_dark"))       # Pfeiler/Widerlager
        P += deck(C, w, 8.0, y, Z)
        for sx in (-1, 1):                                                       # Waagebalken-Tuerme
            P.append(box(C, (1.2, 2.0, 7.0), (sx * (w / 2 + 0.2), y - s * 2.6, Z), "steel_blue"))
            P.append(box(C, (1.4, 2.4, 0.3), (sx * (w / 2 + 0.2), y - s * 2.6, Z + 7.0), "metal_dark"))
            P.append(box(C, (0.3, 0.3, 1.5), (sx * (w / 2 + 0.2), y - s * 3.8, Z + 0.25), "red"))  # Schranke
        P.append(beam(C, (-(w / 2 + 0.2), y - s * 2.6, Z + 6.6), (w / 2 + 0.2, y - s * 2.6, Z + 6.6), 0.5, "steel_blue"))
    # Brueckenwaerterhaus
    P.append(box(C, (3.0, 3.0, 3.0), (w / 2 + 2.6, 18.0, Z), "white"))
    P += ribbon(C, w / 2 + 2.6, 18.0, 3.0, 3.0, Z + 2.0, 0.9, "glass_dark", "flrb", 0.3)
    P.append(hip(C, 3.0, 3.0, 1.2, (w / 2 + 2.6, 18.0, Z + 3.0), "roof_red", overhang=0.3))
    leaves = {}
    for name, s in (("LeafA", -1), ("LeafB", 1)):
        hinge = Vector((0, s * 12.0, Z))
        L = deck(C, w - 0.4, 11.9, s * 6.05, Z)
        L.append(box(C, (w - 0.4, 0.3, 0.6), (0, s * 0.3, Z - 0.6), "yellow"))
        leaves[name] = (L, tuple(hinge))
    out = {"Drawbridge": (P, (0, 0, 0))}
    out.update(leaves)
    return out


def tunnel_portal(C):
    """Tunnelportal fuer 2-spurige Strasse (Oeffnung 11 x 6,5 m), Stirnwand mit Fluegelmauern, Hang darueber."""
    P = []
    # Stirnwand mit Oeffnung: zwei Wangen + Sturz mit Bogenausschnitt
    P.append(box(C, (3.5, 1.5, 10.0), (-7.25, 0, 0), "concrete"))
    P.append(box(C, (3.5, 1.5, 10.0), (7.25, 0, 0), "concrete"))
    arc = [(5.5 * math.cos(math.pi * k / 8), 4.5 + 2.0 * math.sin(math.pi * k / 8)) for k in range(9)]
    lintel = prism(C, [(5.5, 10.0)] + arc + [(-5.5, 10.0)], 1.5, (0, 0, 0), "concrete")
    lintel.data.transform(Matrix.Translation((0, 0.75, 0)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(lintel)
    P.append(box(C, (19.0, 1.8, 0.6), (0, 0, 10.0), "concrete_dark"))
    # dunkler Tunnelschlund
    inner = prism(C, [(5.4, 0)] + [(x * 0.98, z) for x, z in arc] + [(-5.4, 0)], 12.0, (0, 0, 0), "black")
    inner.data.transform(Matrix.Translation((0, 12.75, 0)) @ Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    P.append(inner)
    P.append(box(C, (11.0, 12.0, 0.06), (0, 6.75, 0.01), "asphalt"))
    # Fluegelmauern: Dreieck im YZ-Profil (faellt nach vorne -Y ab), 1 m dick entlang X
    YZ = Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # (a, b, c) -> (x=c, y=a, z=b)
    for s in (-1, 1):
        wing = prism(C, [(0.0, 0.0), (0.0, 9.5), (-7.0, 0.0)], 1.0, (0, 0, 0), "concrete", name="wing")
        wing.data.transform(Matrix.Translation((s * 9.0 - 0.5, -0.75, 0)) @ YZ)
        P.append(wing)
    # Hang/Bewuchs ueber dem Portal
    P.append(mound(C, 14.0, 8.0, 6.0, (0, 6.5, 9.0), "grass", seed=4, subdiv=2))
    for (x, y) in ((-6.0, 7.0), (5.0, 9.0), (0.0, 11.0)):
        P += tree(C, x, y, 5.0, 1.6, "pine")
        for o in P[-4:]:
            o.data.transform(Matrix.Translation((0, 0, 12.0)))
    P += sign(C, 0, -0.8, 8.2, 6.0, 0.8, "blue", "white")
    for x in (-4.0, 4.0):
        P.append(box(C, (0.6, 0.2, 0.3), (x, -0.8, 6.0), "light_on"))
    return P


def bridge_pillar(C):
    """Brueckenpfeiler (fuer Strassenbruecken, 12 m hoch, Kopf 12 m breit)."""
    return [box(C, (4.0, 4.0, 1.0), (0, 0, 0), "concrete_dark"),
            box(C, (2.4, 2.2, 10.5), (0, 0, 1.0), "concrete", bevel=0.2),
            box(C, (12.0, 2.6, 1.0), (0, 0, 11.5), "concrete"),
            box(C, (1.0, 1.0, 0.3), (-4.0, 0, 12.5), "metal_dark"),
            box(C, (1.0, 1.0, 0.3), (4.0, 0, 12.5), "metal_dark")]


def noise_barrier(C):
    """Laermschutzwand 16 m (kachelbar entlang Y), 4 m hoch: Stahlstuetzen, Paneele, Glasband."""
    P = []
    for k in range(5):
        y = -8.0 + k * 4.0
        P.append(box(C, (0.3, 0.3, 4.2), (0, y, 0), "metal_dark"))
        P.append(box(C, (1.0, 1.0, 0.4), (0, y, 0), "concrete"))
    for k in range(4):
        y = -6.0 + k * 4.0
        P.append(box(C, (0.2, 3.8, 2.6), (0, y, 0.2), ("green_dark", "pipe_green", "green_dark", "pipe_green")[k]))
        P.append(box(C, (0.12, 3.8, 1.2), (0, y, 2.8), "glass_light"))
    return P


def sign_gantry(C):
    """Schilderbruecke ueber 3 Spuren (Spannweite 16 m)."""
    P = []
    for s in (-1, 1):
        P.append(box(C, (1.4, 1.4, 0.5), (s * 8.5, 0, 0), "concrete"))
        P.append(box(C, (0.5, 0.5, 7.0), (s * 8.5, 0, 0.5), "metal"))
    P.append(box(C, (17.4, 0.6, 0.6), (0, 0, 6.6), "metal"))
    P.append(box(C, (17.4, 0.4, 0.4), (0, 0, 7.4), "metal"))
    for x, wdt in ((-4.5, 6.0), (3.5, 7.0)):
        P.append(box(C, (wdt, 0.15, 2.4), (x, -0.4, 5.6), "blue"))
        P.append(box(C, (wdt - 0.3, 0.17, 2.1), (x, -0.41, 5.75), "white"))
        P.append(box(C, (wdt - 0.6, 0.19, 1.8), (x, -0.42, 5.9), "blue"))
        for k in range(2):
            P.append(box(C, (wdt * 0.6, 0.2, 0.25), (x - wdt * 0.1, -0.44, 6.2 + k * 0.7), "white"))
        P.append(box(C, (0.4, 0.2, 0.7), (x + wdt * 0.35, -0.44, 6.3), "white"))
    return P


def power_pylon(C):
    """Hochspannungsmast (Donaumast), 42 m, Traversen fuer 2 x 3 Leiterseile."""
    P = []
    hb, ht, H = 4.0, 1.2, 36.0
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    n = 9
    for k in range(n):
        za, zb = H * k / n, H * (k + 1) / n
        ra = hb + (ht - hb) * k / n
        rb = hb + (ht - hb) * (k + 1) / n
        for i in range(4):
            a, b = corners[i], corners[(i + 1) % 4]
            P.append(beam(C, (a[0] * ra, a[1] * ra, za), (a[0] * rb, a[1] * rb, zb), 0.22, "metal"))
            P.append(beam(C, (a[0] * rb, a[1] * rb, zb), (b[0] * rb, b[1] * rb, zb), 0.1, "metal"))
            P.append(beam(C, (a[0] * ra, a[1] * ra, za), (b[0] * rb, b[1] * rb, zb), 0.08, "metal"))
    P.append(lattice_mast(C, 0, 0, 6.0, 1.0, 0.3, H, colors=("metal",), t=0.12)[0])
    P.append(cyl(C, 0.3, 6.0, (0, 0, H), "metal", segs=6, r_top=0.05))
    for z, span in ((24.0, 9.0), (31.0, 6.5)):
        for s in (-1, 1):
            P.append(beam(C, (0, 0, z), (s * span, 0, z), 0.5, "metal"))
            P.append(beam(C, (s * ht, 0, z - 2.0), (s * span, 0, z), 0.12, "metal"))
            for f in ((0.55, 1.0) if span > 7 else (1.0,)):
                x = s * span * f
                P.append(cyl(C, 0.18, 2.2, (x, 0, z - 2.2), "glass_light", segs=6))
                P.append(sphere(C, 0.12, (x, 0, z - 2.3), "metal_dark", subdiv=0))
    for (cx, cy) in corners:
        P.append(box(C, (1.4, 1.4, 0.6), (cx * hb, cy * hb, 0), "concrete"))
    return P


def roadworks(C):
    """Baustellen-Set (8 x 6 m): Absperrbaken, Pylonen, Bauzaun, Bauwagen, Schuttmulde, Warnleuchten."""
    rnd = random.Random(3)
    P = [box(C, (8.0, 6.0, 0.03), (0, 0, 0), "asphalt")]
    for k in range(6):
        x = -3.5 + k * 1.4
        P.append(box(C, (1.2, 0.12, 0.25), (x, -2.6, 0.6), "red"))
        P.append(box(C, (1.2, 0.13, 0.12), (x, -2.6, 0.72), "white"))
        for s in (-0.5, 0.5):
            P.append(box(C, (0.06, 0.06, 0.9), (x + s, -2.6, 0), "metal_dark"))
        P.append(sphere(C, 0.1, (x, -2.6, 0.95), "yellow", subdiv=0))
    for k in range(5):
        P.append(cyl(C, 0.2, 0.7, (-3.4 + k * 1.7, -1.8, 0), "orange", segs=8, r_top=0.03))
        P.append(cyl(C, 0.21, 0.12, (-3.4 + k * 1.7, -1.8, 0.35), "white", segs=8))
    P.append(box(C, (3.4, 2.0, 2.4), (2.0, 1.6, 0.4), "yellow", bevel=0.1))       # Bauwagen
    P.append(box(C, (3.5, 2.1, 0.12), (2.0, 1.6, 2.8), "white"))
    P.append(box(C, (0.8, 0.06, 0.6), (1.6, 0.58, 1.6), "glass"))
    for x in (1.0, 3.0):
        P.append(cyl(C, 0.4, 0.2, (x, 0.55, 0.4), "black", segs=8, rot=(90, 0, 0)))
    P.append(prism(C, [(-1.0, -0.6), (1.0, -0.6), (1.0, 0.6), (-1.0, 0.6)], 1.0, (-2.5, 1.8, 0.0), "orange"))
    P.append(mound(C, 0.9, 0.5, 0.5, (-2.5, 1.8, 1.0), "gravel", seed=3, subdiv=1))
    for k in range(3):
        P.append(box(C, (0.04, 0.04, 1.8), (-3.9, -1.0 + k * 1.2, 0), "metal"))
    P.append(box(C, (0.03, 2.4, 1.6), (-3.9, -0.4, 0.15), "metal_dark"))
    return P


INF = {"Drawbridge": drawbridge, "TunnelPortal": tunnel_portal, "BridgePillar": bridge_pillar,
       "NoiseBarrier": noise_barrier, "SignGantry": sign_gantry, "PowerPylon": power_pylon, "Roadworks": roadworks}
run_family(INF, "Infrastructure", views=V)
