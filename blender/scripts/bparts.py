"""Gebaeude-Bauteile fuer die City-Builder-Assets (setzt auf clib auf).
Konvention wie clib: Ursprung = Mitte Grundflaeche, Vorderseite (Strasse) = -Y, Sockel ab z = 0
(die Zonenplatte in Unity liegt 0,22 m hoch, deshalb Sockel >= 0,3 m)."""
import math, random
from clib import *

PLINTH = 0.35


def hip(coll, w, d, h, loc=(0, 0, 0), color="roof_red", overhang=0.4, name="hip"):
    """Walmdach (bei w == d Zeltdach); loc = Mitte der Traufe."""
    hw, hd = w / 2 + overhang, d / 2 + overhang
    bm = bmesh.new()
    c = [bm.verts.new(p) for p in ((-hw, -hd, 0), (hw, -hd, 0), (hw, hd, 0), (-hw, hd, 0))]
    if abs(hw - hd) < 1e-3:
        a = bm.verts.new((0, 0, h))
        for i in range(4):
            bm.faces.new([c[i], c[(i + 1) % 4], a])
    elif hw > hd:
        a, b = bm.verts.new((-(hw - hd), 0, h)), bm.verts.new((hw - hd, 0, h))
        bm.faces.new([c[0], c[1], b, a]); bm.faces.new([c[1], c[2], b])
        bm.faces.new([c[2], c[3], a, b]); bm.faces.new([c[3], c[0], a])
    else:
        a, b = bm.verts.new((0, -(hd - hw), h)), bm.verts.new((0, hd - hw, h))
        bm.faces.new([c[0], c[1], a]); bm.faces.new([c[1], c[2], b, a])
        bm.faces.new([c[2], c[3], b]); bm.faces.new([c[3], c[0], a, b])
    bm.faces.new([c[3], c[2], c[1], c[0]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return mesh_obj(name, bm, coll, color)


def flat_roof(coll, w, d, z, loc=(0, 0), color="roof_light", parapet="concrete_dark", ph=0.6, t=0.3):
    """Flachdach mit Attika."""
    x, y = loc
    out = [box(coll, (w - 2 * t + 0.02, d - 2 * t + 0.02, 0.08), (x, y, z), color)]
    for (sx, sy, px, py) in ((w + 0.1, t, 0, -(d / 2 - t / 2)), (w + 0.1, t, 0, d / 2 - t / 2),
                             (t, d + 0.1, -(w / 2 - t / 2), 0), (t, d + 0.1, w / 2 - t / 2, 0)):
        out.append(box(coll, (sx, sy, ph), (x + px, y + py, z), parapet))
    return out


def facade(coll, cx, cy, w, d, zs, spacing=3.2, win=(1.3, 1.5), sides="fblr", color="glass", frame=None,
           margin=1.0, skip=None):
    """Fensterraster auf einem Quader (Mitte cx, cy, Breite w (X), Tiefe d (Y)).
    zs = Mittelhoehen der Fensterreihen. sides: f=vorne(-Y) b=hinten l=links(-X) r=rechts.
    skip(side, row, col) -> True laesst ein Fenster weg (z. B. Tuer)."""
    out = []
    spec = {"f": ((cx, cy - d / 2), (1, 0, 0), (0, -1, 0), w),
            "b": ((cx, cy + d / 2), (-1, 0, 0), (0, 1, 0), w),
            "l": ((cx - w / 2, cy), (0, 1, 0), (-1, 0, 0), d),
            "r": ((cx + w / 2, cy), (0, -1, 0), (1, 0, 0), d)}
    for s in sides:
        (mx, my), dvec, nvec, L = spec[s]
        n = max(1, int((L - 2 * margin + spacing - win[0]) // spacing))
        span = (n - 1) * spacing
        for row, z in enumerate(zs):
            for col in range(n):
                if skip and skip(s, row, col):
                    continue
                off = -span / 2 + col * spacing
                p = (mx + dvec[0] * off, my + dvec[1] * off, z)
                out += window_row(coll, p, dvec, 1, spacing, win, color=color, normal=nvec, frame=frame)
    return out


def door(coll, x, y, w=1.1, h=2.2, color="wood_dark", z=PLINTH, normal=(0, -1, 0), canopy=None):
    nx, ny = normal[0], normal[1]
    sx = w if abs(ny) > 0.5 else 0.12
    sy = 0.12 if abs(ny) > 0.5 else w
    out = [box(coll, (sx, sy, h), (x + nx * 0.05, y + ny * 0.05, z), color)]
    if canopy:
        cx_ = (w + 0.8) if abs(ny) > 0.5 else 1.2
        cy_ = 1.2 if abs(ny) > 0.5 else (w + 0.8)
        out.append(box(coll, (cx_, cy_, 0.15), (x + nx * 0.6, y + ny * 0.6, z + h + 0.2), canopy))
    return out


def tree(coll, x, y, h=6.0, r=2.2, kind="round", seed=0, trunk="wood_dark", leaf="green"):
    rnd = random.Random(seed)
    out = [cyl(coll, 0.18 * h / 6, h * 0.45, (x, y, 0), trunk, segs=6, r_top=0.12 * h / 6)]
    if kind == "round":
        out.append(sphere(coll, r, (x, y, h * 0.62), leaf, subdiv=1, scale=(1, 1, 1.05)))
        out.append(sphere(coll, r * 0.65, (x + rnd.uniform(-0.6, 0.6) * r, y + rnd.uniform(-0.5, 0.5) * r,
                                            h * 0.62 + r * 0.55), leaf, subdiv=1))
    else:  # Nadelbaum
        for k, (rr, zz) in enumerate(((r, 0.3), (r * 0.75, 0.52), (r * 0.48, 0.72))):
            out.append(cyl(coll, rr, h * 0.32, (x, y, h * zz), "green_dark", segs=7, r_top=0.05))
    return out


def hedge(coll, a, b, h=1.1, t=0.8, color="green_dark"):
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    v = b - a
    ang = math.degrees(math.atan2(v.y, v.x))
    m = (a + b) / 2
    return box(coll, (v.length, t, h), (m.x, m.y, 0), color, rot=(0, 0, ang), bevel=0.15)


def awning(coll, x, y, w, depth=1.4, z=3.0, color="red", stripe="white", drop=0.5):
    """Schraege Markise vor der Fassade (Fassade bei y, nach -Y)."""
    out = []
    n = max(2, int(w / 0.9))
    sw = w / n
    for i in range(n):
        c = color if i % 2 == 0 else stripe
        px = x - w / 2 + sw * (i + 0.5)
        out.append(box(coll, (sw, depth + 0.1, 0.08), (px, y - depth / 2, z - drop / 2), c, center=True,
                       rot=(math.degrees(math.atan2(drop, depth)), 0, 0)))
        out.append(box(coll, (sw, 0.06, 0.35), (px, y - depth - 0.02, z - drop - 0.17), c, center=True))
    return out


def ac_units(coll, x, y, z, n=2, seed=0):
    rnd = random.Random(seed)
    out = []
    for i in range(n):
        px, py = x + rnd.uniform(-1.5, 1.5) + i * 1.8, y + rnd.uniform(-1, 1)
        out.append(box(coll, (1.4, 1.0, 0.9), (px, py, z), "metal"))
        out.append(cyl(coll, 0.35, 0.05, (px, py, z + 0.9), "metal_dark", segs=8))
    return out


def sign(coll, x, y, z, w, h=0.8, color="blue", text_color="white", normal=(0, -1, 0)):
    """Schild mit angedeuteter Schriftzeile."""
    ny = normal[1]
    out = [box(coll, (w, 0.15, h), (x, y + ny * 0.1, z), color)]
    out.append(box(coll, (w * 0.7, 0.04, h * 0.25), (x, y + ny * 0.2, z + h * 0.38), text_color))
    return out


def balcony(coll, x, y, z, w=2.6, depth=1.2, slab="concrete", rail="metal", normal=(0, -1, 0)):
    ny = normal[1]
    out = [box(coll, (w, depth, 0.18), (x, y + ny * depth / 2, z), slab)]
    out.append(box(coll, (w, 0.06, 0.95), (x, y + ny * (depth - 0.03), z + 0.18), rail))
    for sx in (-1, 1):
        out.append(box(coll, (0.06, depth, 0.95), (x + sx * (w / 2 - 0.03), y + ny * depth / 2, z + 0.18), rail))
    return out


def ribbon(coll, cx, cy, w, d, z, h=1.5, color="glass", sides="fblr", inset=0.6):
    """Fensterband (durchgehende Verglasung) um einen Quader auf Hoehe z (Mitte)."""
    out = []
    for s in sides:
        if s in "fb":
            y = cy - d / 2 - 0.04 if s == "f" else cy + d / 2 + 0.04
            out.append(box(coll, (w - 2 * inset, 0.08, h), (cx, y, z), color, center=True))
        else:
            x = cx - w / 2 - 0.04 if s == "l" else cx + w / 2 + 0.04
            out.append(box(coll, (0.08, d - 2 * inset, h), (x, cy, z), color, center=True))
    return out


def block(coll, cx, cy, w, d, floors, fh=3.0, wall="plaster", plinth="concrete_dark", z0=PLINTH,
          win="glass", spacing=3.0, win_size=(1.3, 1.5), sides="fblr", frame=None, skip_ground=False,
          ribbon_windows=False, bands=None, margin=1.0, skip=None):
    """Mehrgeschossiger Baukoerper mit Sockel und Fenstern. Liefert (teile, oberkante)."""
    P = []
    if z0 > 0:
        P.append(box(coll, (w + 0.1, d + 0.1, z0), (cx, cy, 0), plinth))
    h = floors * fh
    P.append(box(coll, (w, d, h), (cx, cy, z0), wall))
    first = 1 if skip_ground else 0
    zs = [z0 + fh * (i + 0.55) for i in range(first, floors)]
    if ribbon_windows:
        for z in zs:
            P += ribbon(coll, cx, cy, w, d, z, win_size[1], win, sides)
    else:
        P += facade(coll, cx, cy, w, d, zs, spacing, win_size, sides, win, frame, margin, skip)
    if bands:
        for i in range(1, floors):
            P.append(box(coll, (w + 0.12, d + 0.12, 0.22), (cx, cy, z0 + i * fh - 0.11), bands))
    return P, z0 + h


def bench(coll, x=0, y=0, rot=0, wood="wood", metal="metal_dark"):
    P = []
    for sx in (-0.7, 0.7):
        P.append(box(coll, (0.08, 0.5, 0.42), (sx, 0, 0), metal))
        P.append(box(coll, (0.08, 0.08, 0.5), (sx, 0.24, 0.42), metal, rot=(-12, 0, 0)))
    for k in range(3):
        P.append(box(coll, (1.8, 0.15, 0.05), (0, -0.18 + k * 0.17, 0.42), wood))
    for k in range(2):
        P.append(box(coll, (1.8, 0.05, 0.13), (0, 0.27 + 0.02 * k, 0.6 + k * 0.2), wood, rot=(-12, 0, 0)))
    return place_at(P, x, y, rot)


def lamp(coll, x=0, y=0, rot=0, h=4.5, pole="metal_dark"):
    P = [cyl(coll, 0.14, 0.5, (0, 0, 0), pole, segs=8),
         cyl(coll, 0.07, h, (0, 0, 0), pole, segs=6),
         cyl(coll, 0.3, 0.3, (0, 0, h - 0.05), pole, segs=8, r_top=0.12),
         cyl(coll, 0.22, 0.28, (0, 0, h - 0.33), "light_on", segs=8)]
    return place_at(P, x, y, rot)


def trash_bin(coll, x=0, y=0, rot=0, color="green_dark"):
    P = [cyl(coll, 0.28, 0.85, (0, 0, 0), color, segs=8),
         cyl(coll, 0.31, 0.08, (0, 0, 0.85), "metal_dark", segs=8),
         box(coll, (0.05, 0.05, 0.85), (0, -0.3, 0), "metal_dark")]
    return place_at(P, x, y, rot)


def flower_bed(coll, x, y, w, d, seed=0, colors=("flower_pink", "yellow", "red", "flower_violet")):
    rnd = random.Random(seed)
    P = [box(coll, (w, d, 0.3), (x, y, 0), "concrete"), box(coll, (w - 0.3, d - 0.3, 0.05), (x, y, 0.3), "wood_dark")]
    for i in range(int(w * d * 1.2)):
        P.append(sphere(coll, rnd.uniform(0.18, 0.3), (x + rnd.uniform(-w / 2 + 0.4, w / 2 - 0.4),
                                                       y + rnd.uniform(-d / 2 + 0.4, d / 2 - 0.4), 0.42),
                        rnd.choice(("green", "green_dark") + tuple(colors)), subdiv=1))
    return P


def flagpole(coll, x, y, h=8.0, flag="blue", flag2=None):
    P = [cyl(coll, 0.08, h, (x, y, 0), "metal", segs=6), sphere(coll, 0.13, (x, y, h + 0.05), "gold", subdiv=1),
         box(coll, (1.8, 0.04, 1.1), (x + 0.95, y, h - 1.3), flag)]
    if flag2:
        P.append(box(coll, (1.8, 0.05, 0.36), (x + 0.95, y, h - 1.3 + 0.37), flag2))
    return P


def lot(n):
    """Zielmass fuer n Zonenzellen (8 m), wie BuildingView einpasst (92 %)."""
    return n * 8.0 * 0.92


def sawtooth(coll, cx, cy, w, d, z, n=4, h=2.4, roof="roof_light", glass="glass_light"):
    """Sheddach: n Zaehne entlang Y, steile Seite (Glas) nach hinten (+Y)."""
    P = []
    tooth = d / n
    for i in range(n):
        y0 = cy - d / 2 + i * tooth
        P.append(prism(coll, [(y0, 0), (y0 + tooth, h), (y0 + tooth, 0)], w, (0, 0, 0), roof, name="saw"))
        P[-1].data.transform(Matrix.Translation((cx - w / 2, 0, z)) @ Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0),
                                                                             (0, 0, 0, 1))))
        P.append(box(coll, (w - 0.4, 0.06, h * 0.8), (cx, y0 + tooth + 0.03, z + h * 0.08), glass))
    return P


def container(coll, x, y, z=0, rot=0, color="red"):
    P = [box(coll, (2.44, 6.06, 2.6), (0, 0, 0), color)]
    for k in range(9):
        P.append(box(coll, (2.5, 0.12, 2.4), (0, -2.8 + k * 0.7, 0.1), color))
    P.append(box(coll, (2.46, 0.06, 2.5), (0, -3.05, 0.05), "metal_dark"))
    for o in P:
        o.data.transform(Matrix.Translation((0, 0, z)))
    return place_at(P, x, y, rot)


def truck(coll, x, y, rot=0, cab="blue", trailer="white"):
    P = [box(coll, (2.5, 2.4, 2.6), (0, -5.3, 0.6), cab, bevel=0.1),
         box(coll, (2.3, 0.08, 1.0), (0, -6.5, 2.0), "glass_dark"),
         box(coll, (2.0, 10.5, 0.4), (0, 0.6, 0.6), "metal_dark"),
         box(coll, (2.55, 9.0, 2.9), (0, 1.3, 1.0), trailer)]
    for wy in (-5.2, 3.6, 4.9):
        for wx in (-1.25, 1.0):
            P.append(cyl(coll, 0.5, 0.25, (wx, wy, 0.5), "black", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def crates(coll, x, y, n=3, seed=0):
    rnd = random.Random(seed)
    P = []
    for i in range(n):
        s = rnd.uniform(0.8, 1.2)
        dx, dy = (i % 2) * 1.3, (i // 2) * 1.3
        P.append(box(coll, (1.2, 1.0, 0.15), (x + dx, y + dy, 0), "wood_dark"))   # Palette
        P.append(box(coll, (s, s * 0.9, s * 0.8), (x + dx, y + dy, 0.15), rnd.choice(("wood", "sand", "beige"))))
    return P


def tank(coll, x, y, r=3.0, h=8.0, color="white", band="steel_blue"):
    return [cyl(coll, r, h, (x, y, 0), color, segs=16),
            cyl(coll, r + 0.02, 0.5, (x, y, h * 0.6), band, segs=16),
            cyl(coll, r, r * 0.25, (x, y, h), color, segs=16, r_top=0.4),
            beam(coll, (x + r, y, 0.1), (x + r * 0.2, y - r * 0.98, h + 0.1), 0.12, "metal_dark")]


def lattice_mast(coll, x, y, h, r0=1.4, r1=0.5, z0=0.0, colors=("red", "white"), t=0.14, band=2, guys=None):
    """Dreibeiniger Gittermast; Farbwechsel alle <band> Felder. guys = Abspannradius (oder None)."""
    P = []
    n = max(2, int(h / 3.5))

    def leg(i, z):
        f = (z - z0) / h
        r = r0 + (r1 - r0) * f
        a = math.radians(90 + 120 * i)
        return Vector((x + r * math.cos(a), y + r * math.sin(a), z))
    for k in range(n):
        za, zb = z0 + h * k / n, z0 + h * (k + 1) / n
        col = colors[(k // band) % len(colors)]
        for i in range(3):
            P.append(beam(coll, leg(i, za), leg(i, zb), t, col))
            P.append(beam(coll, leg(i, zb), leg(i + 1, zb), t * 0.6, col))
            P.append(beam(coll, leg(i, za), leg(i + 1, zb), t * 0.45, col))
    if guys:
        for frac in (0.45, 0.85):
            z = z0 + h * frac
            for i in range(3):
                a = math.radians(90 + 120 * i)
                g = Vector((x + guys * math.cos(a), y + guys * math.sin(a), 0.1))
                P.append(beam(coll, leg(i, z), g, 0.04, "metal_dark"))
        for i in range(3):
            a = math.radians(90 + 120 * i)
            P.append(box(coll, (0.8, 0.8, 0.4), (x + guys * math.cos(a), y + guys * math.sin(a), 0), "concrete"))
    P.append(sphere(coll, 0.3, (x, y, z0 + h + 0.2), "red", subdiv=1))
    return P


def garbage_truck(coll, x, y, rot=0, body="orange"):
    P = [box(coll, (2.4, 2.0, 2.4), (0, -3.2, 0.5), "white", bevel=0.1),
         box(coll, (2.2, 0.08, 0.9), (0, -4.22, 1.8), "glass_dark"),
         box(coll, (2.0, 7.6, 0.4), (0, 0.2, 0.5), "metal_dark"),
         box(coll, (2.5, 4.6, 2.8), (0, 0.4, 0.9), body, bevel=0.15),
         box(coll, (2.5, 1.4, 2.4), (0, 3.4, 1.0), body, bevel=0.2),
         box(coll, (2.55, 0.4, 0.3), (0, -1.3, 2.4), "metal_dark")]
    for wy in (-3.0, 1.6, 2.8):
        for wx in (-1.2, 0.95):
            P.append(cyl(coll, 0.5, 0.25, (wx, wy, 0.5), "black", segs=8, rot=(0, 90, 0)))
    return place_at(P, x, y, rot)


def dish(coll, x, y, z, r=1.2, tilt=35, rot=0, color="white"):
    """Satellitenschuessel (Schale + Fuss), schaut nach -Y."""
    s = lathe(coll, [(0, 0), (r * 0.5, 0.06), (r, r * 0.3), (r * 0.94, r * 0.32), (0, 0.06)], (0, 0, 0), color, segs=12)
    s.data.transform(Matrix.Translation((x, y, z + r)) @ Matrix.Rotation(math.radians(rot), 4, 'Z') @
                     Matrix.Rotation(math.radians(90 - tilt), 4, 'X'))   # Oeffnung nach vorne-oben
    return [s, box(coll, (0.3, 0.3, r + 0.2), (x, y, z), "metal_dark"),
            beam(coll, (x, y, z + r), (x, y - r * 0.9, z + r * 0.5 + r * 0.4), 0.05, "metal_dark")]


def half_timber(coll, cx, cy, w, d, z0, fh, floors, sides="fblr", color="wood_dark", t=0.22, bay=1.6):
    """Fachwerk auf die Fassaden eines Quaders legen: Staender, Riegel, Streben (Andreaskreuz je zweites Feld)."""
    P = []
    spec = {"f": ((cx, cy - d / 2), (1, 0), (0, -1), w), "b": ((cx, cy + d / 2), (-1, 0), (0, 1), w),
            "l": ((cx - w / 2, cy), (0, 1), (-1, 0), d), "r": ((cx + w / 2, cy), (0, -1), (1, 0), d)}
    for s in sides:
        (mx, my), (dx, dy), (nx, ny), L = spec[s]
        n = max(2, int(round(L / bay)))
        o = 0.05

        def pt(u, z):
            return (mx + dx * (u - L / 2) + nx * o, my + dy * (u - L / 2) + ny * o, z)
        for f in range(floors):
            za, zb = z0 + f * fh, z0 + (f + 1) * fh
            P.append(beam(coll, pt(0, za + t / 2), pt(L, za + t / 2), t, color))
            P.append(beam(coll, pt(0, zb - t / 2), pt(L, zb - t / 2), t, color))
            for i in range(n + 1):
                u = L * i / n
                P.append(beam(coll, pt(u, za), pt(u, zb), t, color))
            for i in range(0, n, 2):
                u0, u1 = L * i / n, L * (i + 1) / n
                P.append(beam(coll, pt(u0, za), pt(u1, zb), t * 0.8, color))
                if (i // 2) % 2 == 0:
                    P.append(beam(coll, pt(u1, za), pt(u0, zb), t * 0.8, color))
    return P


def round_tower(coll, x, y, r, floors, fh, z0, wall="white", glass="glass", slab="offwhite", balcony=None, segs=16):
    """Rundturm aus Geschossringen (Brüstung, Glasband, Sturz) + optionale Balkonringe."""
    prof = [(0, 0)]
    cols = []
    for f in range(floors):
        z = f * fh
        prof += [(r, z), (r, z + fh * 0.32), (r - 0.15, z + fh * 0.32), (r - 0.15, z + fh * 0.9), (r, z + fh * 0.9)]
        cols += [slab if f == 0 else wall, wall, glass, glass, wall]
    prof += [(r, floors * fh), (0, floors * fh)]
    cols += [wall, "roof_light"]
    P = [lathe(coll, prof, (x, y, z0), segs=segs, colors=cols)]
    if balcony:
        for f in range(1, floors):
            z = z0 + f * fh
            P.append(lathe(coll, [(r - 0.1, z - 0.1), (r + 1.1, z - 0.1), (r + 1.1, z + 0.85), (r + 1.04, z + 0.85),
                                   (r + 1.04, z + 0.08), (r - 0.1, z + 0.08)], (x, y, 0), segs=segs,
                           colors=[slab, balcony, balcony, balcony, slab]))
    return P


def pool(coll, x, y, w, d):
    return [box(coll, (w + 0.8, d + 0.8, 0.3), (x, y, 0), "cream"),
            box(coll, (w, d, 0.05), (x, y, 0.28), "water"),
            box(coll, (0.6, 0.1, 0.9), (x + w / 2 - 0.5, y - d / 2 - 0.3, 0.3), "metal")]


def loft(coll, sections, colors, top=None, bottom=None, name="loft"):
    """Verbindet Umrisse gleicher Punktzahl: sections = [(pts2d, z), ...] von unten nach oben.
    colors[i] faerbt das Band zwischen Abschnitt i und i+1; top/bottom = Deckel-Farben (None = offen)."""
    bm = bmesh.new()
    rings = [[bm.verts.new((x, y, z)) for (x, y) in pts] for pts, z in sections]
    n = len(rings[0])
    for k in range(len(rings) - 1):
        faces = []
        for i in range(n):
            j = (i + 1) % n
            faces.append(bm.faces.new([rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]]))
        paint(bm, colors[k], faces)
    if bottom:
        paint(bm, bottom, [bm.faces.new(rings[0][::-1])])
    if top:
        paint(bm, top, [bm.faces.new(rings[-1])])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return mesh_obj(name, bm, coll, painted=True)


def ship_outline(L, B, bow=0.25, stern_round=0.0, n_bow=4):
    """Draufsicht eines Rumpfes, Bug nach -Y. Gegen den Uhrzeigersinn, Punktzahl fest (2*n_bow + 4)."""
    pts = [(B / 2, L / 2 - stern_round), (B / 2 * (1 - stern_round * 0.3), L / 2), (-B / 2 * (1 - stern_round * 0.3), L / 2),
           (-B / 2, L / 2 - stern_round)]
    yb = -L / 2 + L * bow
    for k in range(n_bow + 1):
        t = k / n_bow
        pts.append((-B / 2 * math.cos(t * math.pi / 2) ** 0.8, yb - (yb + L / 2) * math.sin(t * math.pi / 2)))
    for k in range(n_bow - 1, -1, -1):
        t = k / n_bow
        pts.append((B / 2 * math.cos(t * math.pi / 2) ** 0.8, yb - (yb + L / 2) * math.sin(t * math.pi / 2)))
    # doppelten Bugpunkt (t=1 links/rechts identisch) vermeiden
    out = []
    for p in pts:
        if not out or (abs(out[-1][0] - p[0]) > 1e-6 or abs(out[-1][1] - p[1]) > 1e-6):
            out.append(p)
    return out


def hull(coll, L, B, draft, freeboard, hull_col="steel_blue", deck="wood", bottom_col="red", bow=0.25, sheer=0.0):
    """Schiffsrumpf: Unterwasserteil (rot) bis -draft, Bordwand bis freeboard, Deck als Deckel."""
    o = ship_outline(L, B, bow)
    lo = [(x * 0.72, y * 0.92) for x, y in o]
    mid = [(x * 0.97, y * 0.99) for x, y in o]
    return loft(coll, [(lo, -draft), (mid, 0.0), (o, 0.25), (o, freeboard)], [bottom_col, "white", hull_col],
                top=deck, bottom=bottom_col, name="hull")


def place_at(parts, x, y, rot):
    m = Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(rot), 4, 'Z')
    for o in parts:
        o.data.transform(m)
    return parts


def parked_car(coll, x, y, rot=0, color="red"):
    out = [box(coll, (1.8, 4.2, 0.75), (0, 0, 0.3), color, bevel=0.12),
           box(coll, (1.6, 2.2, 0.6), (0, 0.2, 1.05), "glass_dark", bevel=0.1)]
    for wx in (-0.85, 0.85):
        for wy in (-1.3, 1.3):
            out.append(cyl(coll, 0.34, 0.25, (wx - 0.125, wy, 0.34), "black", segs=8, rot=(0, 90, 0)))
    m = Matrix.Translation((x, y, 0)) @ Matrix.Rotation(math.radians(rot), 4, 'Z')
    for o in out:
        o.data.transform(m)
    return out
