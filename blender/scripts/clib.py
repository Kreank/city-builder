"""Helfer fuer die City-Builder-Low-Poly-Assets (Blender 5.2, Hintergrundmodus).

Konventionen (Blender-Koordinaten, Z oben, 1 BU = 1 m):
  * Ursprung = Mitte der Grundflaeche auf Bodenhoehe (wie UtilityView in Unity).
  * Vorderseite (Unity +Z, f.Front) = Blender -Y.  Breite (Unity X) = Blender X.
  * FBX-Export: axis_forward=-Z, axis_up=Y, bake_space_transform  ->  Unity (x,y,z) = (-bx, bz, -by),
    also eine reine 180-Grad-Drehung um die Hochachse, keine Spiegelung.
  * Alle Teile nutzen EIN Material "CB_Palette" mit Paletten-Textur; jede Flaeche bekommt
    die UV-Koordinate der Mitte ihres Farbfelds (flat shading, keine Texturdetails).
"""
import bpy, bmesh, math, os, random, struct, zlib
from mathutils import Vector, Matrix, Euler

ROOT = r"D:\Privat\Games\city-builder"
BLENDER_DIR = os.path.join(ROOT, "blender")
MODELS = os.path.join(ROOT, "Assets", "_Project", "Models")
PALETTE_PNG = os.path.join(MODELS, "Palette", "CB_Palette.png")
RENDERS = os.path.join(BLENDER_DIR, "renders")
BLENDS = os.path.join(BLENDER_DIR, "blend")
RENDER_COLL = "_render_setup"

# ---------------------------------------------------------------- Palette
# Reihenfolge = Feld-Index (8x8 Felder a 16 px -> 128x128 PNG). Neue Farben NUR hinten anhaengen,
# sonst verrutschen die UVs bereits exportierter Modelle.
PALETTE = [
    ("concrete",      "#B3AEA4"), ("concrete_dark", "#8C877E"), ("asphalt",     "#4B4D52"), ("gravel",      "#A39A8C"),
    ("brick",         "#A85B45"), ("brick_dark",    "#7B4335"), ("plaster",     "#E4DCCB"), ("plaster_warm","#D8C3A0"),
    ("roof_dark",     "#4F545C"), ("roof_red",      "#A9483B"), ("roof_green",  "#5C7A63"), ("roof_light",  "#8E949C"),
    ("metal",         "#BCC2C8"), ("metal_dark",    "#6F757C"), ("steel_blue",  "#5E7C99"), ("rust",        "#9A5B3A"),
    ("white",         "#EEEEEA"), ("offwhite",      "#D9D9D3"), ("red",         "#CC3B30"), ("orange",      "#E0802F"),
    ("yellow",        "#E8BC3C"), ("green",         "#6FA552"), ("green_dark",  "#3F6C46"), ("grass",       "#7FB25C"),
    ("blue",          "#3F7BBD"), ("blue_light",    "#86B6DE"), ("water",       "#3E86B0"), ("glass",       "#3E5D7C"),
    ("glass_dark",    "#283A4D"), ("solar",         "#1F3456"), ("solar_line",  "#5C79A6"), ("coal",        "#2C2C2E"),
    ("wood",          "#8D6A48"), ("wood_dark",     "#5E4630"), ("black",       "#1C1D20"), ("light_on",    "#FFE7A3"),
    ("teal",          "#3E9A97"), ("sand",          "#D9C69A"), ("cream",       "#F2EAD3"), ("pipe_green",  "#5E8C5A"),
    # --- ab hier Gebaeude/Props (nur hinten anhaengen!)
    ("pastel_yellow", "#EBD58C"), ("pastel_blue",   "#A9C6DE"), ("pastel_green","#B5D1A3"), ("pastel_pink", "#E3B3A8"),
    ("terracotta",    "#C4704F"), ("slate",         "#5A6470"), ("beige",       "#CDBB98"), ("gold",        "#C9A13B"),
    ("flower_pink",   "#E06F9C"), ("flower_violet", "#8E6CC4"), ("court_orange","#D9773F"), ("glass_light", "#7FA3C2"),
    # --- Landwirtschaft / Kueste (nur hinten anhaengen!)
    ("wheat",         "#D9BC5C"), ("soil",          "#7B5B3E"), ("soil_light",  "#9C7A55"), ("corn",        "#6E9A3E"),
    ("rapeseed",      "#EAD53A"), ("sea",           "#2F6F99"), ("foam",        "#E9F2F4"), ("grass_dry",   "#A8B064"),
    # --- Gelaende (nur hinten anhaengen!)
    ("rock",          "#8A847B"), ("rock_dark",     "#625D57"),
]
GRID = 8
CELL = 16
_IDX = {n: i for i, (n, _) in enumerate(PALETTE)}


def _hex(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def cell_uv(color):
    i = _IDX[color]
    cx, cy = i % GRID, i // GRID
    # PNG-Zeile 0 = oben, UV v=0 = unten
    return ((cx + 0.5) / GRID, 1.0 - (cy + 0.5) / GRID)


def write_palette_png(path=PALETTE_PNG):
    """Schreibt die Palette als PNG (reines Python, Werte = sRGB-Hex 1:1)."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    size = GRID * CELL
    rows = []
    for y in range(size):
        row = bytearray([0])
        cy = y // CELL
        for x in range(size):
            i = cy * GRID + x // CELL
            r, g, b = _hex(PALETTE[i][1]) if i < len(PALETTE) else (255, 0, 255)
            row += bytes((r, g, b))
        rows.append(bytes(row))
    raw = zlib.compress(b"".join(rows), 9)

    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)) \
        + chunk(b"IDAT", raw) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)
    return path


def palette_material():
    m = bpy.data.materials.get("CB_Palette")
    if m:
        return m
    write_palette_png()
    img = bpy.data.images.load(PALETTE_PNG, check_existing=True)
    m = bpy.data.materials.new("CB_Palette")
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.interpolation = 'Closest'
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.8
    bsdf.inputs['Metallic'].default_value = 0.0
    return m


# ---------------------------------------------------------------- Szene
def fresh_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m)
    sc = bpy.context.scene
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0


def collection(name):
    coll = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    return coll


def paint(bm, color, faces=None):
    uv = bm.loops.layers.uv.verify()
    u, v = cell_uv(color)
    for f in (faces if faces is not None else bm.faces):
        for l in f.loops:
            l[uv].uv = (u, v)


def mesh_obj(name, bm, coll, color=None, painted=False):
    if color and not painted:
        paint(bm, color)
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    me.materials.append(palette_material())
    return obj


def _tf(bm, loc=(0, 0, 0), rot=(0, 0, 0)):
    m = Matrix.Translation(Vector(loc)) @ Euler([math.radians(r) for r in rot]).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


# ---------------------------------------------------------------- Grundkoerper
def box(coll, size, loc=(0, 0, 0), color="concrete", rot=(0, 0, 0), bevel=0.0, name="box", center=False):
    """Quader; loc = Mitte der Unterseite (center=True: Mittelpunkt)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if not center:
        bmesh.ops.translate(bm, vec=Vector((0, 0, size[2] / 2)), verts=bm.verts)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, profile=0.5,
                        affect='EDGES', clamp_overlap=True)
    _tf(bm, loc, rot)
    return mesh_obj(name, bm, coll, color)


def cyl(coll, r, h, loc=(0, 0, 0), color="metal", segs=12, r_top=None, rot=(0, 0, 0), name="cyl", caps=True):
    """Zylinder/Kegelstumpf entlang lokal Z, loc = Mitte der Unterseite (vor Drehung)."""
    rt = r if r_top is None else r_top
    prof = [(0, 0), (r, 0), (rt, h), (0, h)] if caps else [(r, 0), (rt, h)]
    return lathe(coll, prof, loc, color, segs, rot=rot, name=name)


def lathe(coll, profile, loc=(0, 0, 0), color="metal", segs=12, rot=(0, 0, 0), name="lathe",
          colors=None, phase=None):
    """(r, z)-Profil um Z drehen. colors: Liste je Profilabschnitt (len(profile)-1) oder None."""
    bm = bmesh.new()
    ph = math.pi / segs if phase is None else phase
    rings = []
    for r, z in profile:
        if r < 1e-7:
            rings.append(('pole', bm.verts.new((0, 0, z))))
        else:
            rings.append(('ring', [bm.verts.new((r * math.cos(2 * math.pi * i / segs + ph),
                                                 r * math.sin(2 * math.pi * i / segs + ph), z))
                                   for i in range(segs)]))
    uv = bm.loops.layers.uv.verify()
    for k, (a, b) in enumerate(zip(rings[:-1], rings[1:])):
        if a[0] == 'pole' and b[0] == 'pole':
            continue
        col = colors[k] if colors else color
        new = []
        for i in range(segs):
            j = (i + 1) % segs
            if a[0] == 'ring' and b[0] == 'ring':
                vs = [a[1][i], a[1][j], b[1][j], b[1][i]]
            elif a[0] == 'pole':
                vs = [a[1], b[1][j], b[1][i]]
            else:
                vs = [a[1][i], a[1][j], b[1]]
            try:
                new.append(bm.faces.new(vs))
            except ValueError:
                pass
        paint(bm, col, new)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _tf(bm, loc, rot)
    return mesh_obj(name, bm, coll, painted=True)


def prism(coll, pts2d, h, loc=(0, 0, 0), color="concrete", rot=(0, 0, 0), name="prism"):
    """2D-Umriss (x, y) in Z um h extrudieren; loc verschiebt."""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, 0)) for x, y in pts2d]
    top = [bm.verts.new((x, y, h)) for x, y in pts2d]
    n = len(pts2d)
    bm.faces.new(bot[::-1])
    bm.faces.new(top)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([bot[i], bot[j], top[j], top[i]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _tf(bm, loc, rot)
    return mesh_obj(name, bm, coll, color)


def gable(coll, w, d, h, loc=(0, 0, 0), color="roof_red", overhang=0.3, rot=(0, 0, 0), name="roof",
          gable_color=None):
    """Satteldach, First entlang X; loc = Mitte der Traufe (Unterkante)."""
    bm = bmesh.new()
    hw, hd = w / 2 + overhang, d / 2 + overhang
    t = 0.2
    s = h / hd
    # Dach als duenne geknickte Platte (Querschnitt in YZ, entlang X extrudiert). Die Unterseite trifft die
    # Wandoberkante (z = 0 bei |y| = d/2), so bleibt das Giebeldreieck darunter sichtbar.
    sec = [(-hd, -s * overhang), (0, s * d / 2), (hd, -s * overhang), (hd, -s * overhang + t), (0, s * d / 2 + t),
           (-hd, -s * overhang + t)]
    left = [bm.verts.new((-hw, y, z)) for y, z in sec]
    right = [bm.verts.new((hw, y, z)) for y, z in sec]
    caps = [bm.faces.new(left[::-1]), bm.faces.new(right)]
    n = len(sec)
    sides = [bm.faces.new([left[i], left[(i + 1) % n], right[(i + 1) % n], right[i]]) for i in range(n)]
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    paint(bm, color)
    _tf(bm, loc, rot)
    obj = mesh_obj(name, bm, coll, painted=True)
    objs = [obj]
    if gable_color:
        # Giebeldreiecke (Wandfuellung) unter dem Dach
        g = bmesh.new()
        gw = w / 2 - 0.02
        tri = [(-d / 2, 0), (d / 2, 0), (0, s * d / 2 + 0.01)]
        for sx in (-gw, gw):
            vs = [g.verts.new((sx, y, z)) for y, z in tri]
            f = g.faces.new(vs if sx > 0 else vs[::-1])
        bmesh.ops.recalc_face_normals(g, faces=g.faces)
        _tf(g, loc, rot)
        objs.append(mesh_obj(name + "_gable", g, coll, gable_color))
    return objs


def beam(coll, a, b, t=0.3, color="metal_dark", name="beam", t2=None):
    """Vierkantstab von a nach b (Querschnitt t x t2)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    L = d.length
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((t, t2 or t, L)), verts=bm.verts)
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    m = Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return mesh_obj(name, bm, coll, color)


def pipe(coll, a, b, r=0.3, color="metal", segs=8, name="pipe"):
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    prof = [(0, 0), (r, 0), (r, d.length), (0, d.length)]
    obj = lathe(coll, prof, (0, 0, 0), color, segs, name=name)
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    obj.data.transform(Matrix.Translation(a) @ q.to_matrix().to_4x4())
    bm.free()
    return obj


def polyline_pipe(coll, pts, r=0.3, color="metal", segs=8, name="pipe", joints=True):
    out = []
    for a, b in zip(pts[:-1], pts[1:]):
        out.append(pipe(coll, a, b, r, color, segs, name))
    if joints:
        for p in pts[1:-1]:
            out.append(sphere(coll, r * 1.15, p, color, subdiv=1, name=name + "_j"))
    return out


def sphere(coll, r, loc=(0, 0, 0), color="white", subdiv=2, scale=(1, 1, 1), name="sphere"):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return mesh_obj(name, bm, coll, color)


def mound(coll, rx, ry, h, loc=(0, 0, 0), color="coal", seed=1, subdiv=2, name="mound"):
    """Unregelmaessiger Haufen (Kohle, Kies, Erde), flache Unterseite."""
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    for v in bm.verts:
        n = 1.0 + rnd.uniform(-0.12, 0.12)
        z = max(v.co.z, 0.0)
        v.co = Vector((v.co.x * rx * n, v.co.y * ry * n, z * h * n))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.05)
    # Boden schliessen: alle z~0 Flaechen entfernen und mit einem n-Eck ersetzen
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if all(v.co.z < 1e-4 for v in f.verts)], context='FACES')
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return mesh_obj(name, bm, coll, color)


def window_row(coll, start, direction, count, spacing, size, color="glass", normal=(0, -1, 0), name="win",
               frame=None):
    """Reihe flacher Fenster auf einer Fassade. start = Mitte des ersten Fensters (auf der Wand),
    direction = Einheitsvektor entlang der Wand, normal = Aussenseite der Wand, size = (breite, hoehe)."""
    out = []
    s, dvec, nvec = Vector(start), Vector(direction).normalized(), Vector(normal).normalized()
    for i in range(count):
        c = s + dvec * (spacing * i) + nvec * 0.04
        sx = abs(dvec.x) * size[0] + abs(nvec.x) * 0.08
        sy = abs(dvec.y) * size[0] + abs(nvec.y) * 0.08
        out.append(box(coll, (sx, sy, size[1]), (c.x, c.y, c.z), color, center=True, name=name))
        if frame:
            out.append(box(coll, (sx + abs(dvec.x) * 0.2, sy + abs(dvec.y) * 0.2, 0.12),
                           (c.x + nvec.x * 0.03, c.y + nvec.y * 0.03, c.z - size[1] / 2 - 0.06), frame,
                           center=True, name=name + "_sill"))
    return out


def fence(coll, pts, h=1.8, spacing=3.0, post="metal_dark", rail="metal", closed=True, gaps=(), name="fence"):
    """Einfacher Zaun: Pfosten + 2 Querlatten. gaps = Indizes der Abschnitte ohne Zaun (Tore)."""
    out = []
    P = [Vector((p[0], p[1], 0)) for p in pts]
    segs = list(zip(P, P[1:] + ([P[0]] if closed else [])))
    for k, (a, b) in enumerate(segs):
        if k in gaps:
            continue
        L = (b - a).length
        n = max(1, int(round(L / spacing)))
        for i in range(n + 1):
            p = a.lerp(b, i / n)
            out.append(box(coll, (0.12, 0.12, h), (p.x, p.y, 0), post, name=name))
        for z in (h * 0.45, h * 0.92):
            out.append(beam(coll, a + Vector((0, 0, z)), b + Vector((0, 0, z)), 0.06, rail, name=name))
    return out


# ---------------------------------------------------------------- Zusammenfuegen
def join(objs, name, origin=(0, 0, 0)):
    """Fuegt Objekte zu einem zusammen; Objektursprung = origin (Welt), Mesh relativ dazu."""
    objs = [o for o in objs if o]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    res = bpy.context.view_layer.objects.active
    res.name = name
    res.data.name = name
    me = res.data
    me.transform(res.matrix_world)
    res.matrix_world = Matrix.Identity(4)
    me.transform(Matrix.Translation(-Vector(origin)))
    res.location = Vector(origin)
    # doppelte Punkte innerhalb gleicher Teile zusammenfassen (verringert Vertex-Anzahl)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # n-Ecke (> 4 Punkte, z. B. konkave Umrisse aus prism) selbst triangulieren, damit kein Importer raten muss
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    me.update()
    return res


def flatten(lst):
    out = []
    for x in lst:
        if isinstance(x, (list, tuple)):
            out += flatten(x)
        elif x is not None:
            out.append(x)
    return out


def tri_count(obj):
    me = obj.data
    me.calc_loop_triangles()
    return len(me.loop_triangles)


def bbox_world(objs):
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    return mn, mx


# ---------------------------------------------------------------- Export / Render
def export_fbx(objs, name, subdir="Utilities"):
    out_dir = os.path.join(MODELS, subdir)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, name + ".fbx")
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False)
    return path


def render(objs, name, view=(1.0, -1.15, 0.75), lens=50, frame=1.05, res=900, ground=True, suffix=""):
    scene = bpy.context.scene
    rc = bpy.data.collections.get(RENDER_COLL)
    if rc is None:
        rc = bpy.data.collections.new(RENDER_COLL)
        scene.collection.children.link(rc)
    for o in list(rc.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()
    mn, mx = bbox_world(objs)
    ctr = (mn + mx) / 2
    radius = (mx - mn).length / 2

    if ground:
        g = bmesh.new()
        bmesh.ops.create_grid(g, x_segments=1, y_segments=1, size=radius * 6)
        gm = bpy.data.meshes.new("_ground")
        g.to_mesh(gm); g.free()
        gmat = bpy.data.materials.get("_ground_mat") or bpy.data.materials.new("_ground_mat")
        gmat.use_nodes = True
        b = next(n for n in gmat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        b.inputs['Base Color'].default_value = (0.32, 0.42, 0.26, 1)
        b.inputs['Roughness'].default_value = 1.0
        gm.materials.append(gmat)
        go = bpy.data.objects.new("_ground", gm)
        go.location = (ctr.x, ctr.y, -0.02)
        rc.objects.link(go)

    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = lens
    cam = bpy.data.objects.new("_cam", cam_data)
    rc.objects.link(cam)
    fov = 2 * math.atan(18 / lens)
    dist = radius / math.sin(fov / 2) * frame
    d = Vector(view).normalized()
    cam.location = ctr + d * dist
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cam_data.clip_start = 0.1
    cam_data.clip_end = dist * 10
    scene.camera = cam

    sun_d = bpy.data.lights.new("_sun", 'SUN')
    sun_d.energy = 4.2
    sun_d.angle = math.radians(3)
    sun = bpy.data.objects.new("_sun", sun_d)
    sun.rotation_euler = Euler((math.radians(50), 0, math.radians(35)))
    rc.objects.link(sun)

    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputWorld')
    bg = nt.nodes.new('ShaderNodeBackground')
    bg.inputs[0].default_value = (0.62, 0.72, 0.85, 1)
    bg.inputs[1].default_value = 0.6
    nt.links.new(bg.outputs[0], out.inputs[0])

    for eng in ('BLENDER_EEVEE', 'BLENDER_EEVEE_NEXT'):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            pass
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    try:
        scene.view_settings.view_transform = 'Standard'
    except TypeError:
        pass
    try:
        scene.eevee.taa_render_samples = 32
    except Exception:
        pass
    scene.render.image_settings.file_format = 'PNG'
    os.makedirs(RENDERS, exist_ok=True)
    scene.render.filepath = os.path.join(RENDERS, name + suffix + ".png")
    bpy.ops.render.render(write_still=True)
    return scene.render.filepath


def build(name, subdir, fn, views=None):
    """Baut ein Asset in frischer Szene.
    fn(coll) liefert eine Teile-Liste (-> ein Objekt <name>) oder ein Dict {objektname: (teile, ursprung)}
    fuer mehrere Objekte (z. B. animierte Teile); ursprung = Drehpunkt in Weltkoordinaten."""
    fresh_scene()
    coll = collection(name)
    res = fn(coll)
    if isinstance(res, dict):
        objs = [join(flatten(parts), key, origin=origin) for key, (parts, origin) in res.items()]
    else:
        objs = [join(flatten(res), name)]
    return finish(objs, name, subdir, views)


def run_family(variants, subdir, views=None):
    """Baut mehrere Varianten {name: fn} oder {name: (fn, unterordner)}; Kommandozeile nach '--' schraenkt auf
    Namen ein."""
    import sys
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name, fn in variants.items():
        if only and name not in only:
            continue
        sd = subdir
        if isinstance(fn, tuple):
            fn, sd = fn
        build(name, sd, fn, views)


def finish(objs, name, subdir="Utilities", views=None):
    """Export + Renders + .blend speichern + Kennzahlen ausgeben."""
    path = export_fbx(objs, name, subdir)
    views = views or {"": (1.0, -1.15, 0.75), "_back": (-1.0, 1.1, 0.7)}
    renders = [render(objs, name, view=v, suffix=s) for s, v in views.items()]
    os.makedirs(BLENDS, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLENDS, name + ".blend"))
    mn, mx = bbox_world(objs)
    tris = sum(tri_count(o) for o in objs)
    print("CB_RESULT", name, path, os.path.getsize(path), "tris=", tris,
          "size=(%.2f x %.2f x %.2f)" % tuple(mx - mn), "min=(%.2f, %.2f, %.2f)" % tuple(mn))
    for o in objs:
        print("CB_PART", o.name, "loc=(%.2f, %.2f, %.2f)" % tuple(o.location), "tris=", tri_count(o))
    for r in renders:
        print("CB_RENDER", r)
    return tris
