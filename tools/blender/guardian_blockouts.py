"""
Blockout models for the three prototype Guardians (GDD 9, visual direction): stand-ins until the paid models arrive.

Run inside Blender (Text Editor → Run Script, or through MCP for Blender). It rebuilds the "Guardians" collection and,
when EXPORT_DIR is set, writes one FBX per Guardian for Unity:

    alma_mater.fbx     skeletal scholar in a torn robe and mantle, crowned sceptre, book on the belt; children "Owl"
                       (the weak point) and "Eyes" (glow)
    columbia_lion.fbx  bronze lion guardian: metaball muscle and a sculpted mane of flowing locks, a laurel collar
                       with a temple medallion, fangs, long claws; child "Eyes" (glow)
    book_keeper.fbx    marble golem: a closed book for a chest, Ionic-column legs, temple pauldrons, a belt of books;
                       child "Eyes"

Conventions: metres; origin at the feet; the model faces -Y (Blender's front view), which is +Z (forward) in Unity
with the export settings below. One mesh per Guardian plus its moving parts, UVs cube-projected at TEXTURE_METRES per
tile so the CC0 stone and bronze materials tile evenly. Materials are left to Unity.
"""

import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

COLLECTION = "Guardians"
TEXTURE_METRES = 2.0
EXPORT_DIR = None  # set to ".../game/Assets/_Project/Art/Models/Guardians" to export
# Guardians whose FBX now comes from a generated model (tools/blender/prepare_generated.py): built here for reference,
# never exported over it.
GENERATED = {"alma_mater", "columbia_lion", "book_keeper"}


# ---------------------------------------------------------------- helpers

def _collection():
    col = bpy.data.collections.get(COLLECTION)
    if col is None:
        col = bpy.data.collections.new(COLLECTION)
        bpy.context.scene.collection.children.link(col)
    for obj in list(col.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    return col


def _link(obj, col):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    col.objects.link(obj)
    return obj


def _mesh_object(name, bm, col):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    return _link(bpy.data.objects.new(name, mesh), col)


def sphere(col, name, loc, scale, segments=24, rings=12):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def ico(col, name, loc, scale, subdivisions=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def box(col, name, loc, half, rot_deg=(0, 0, 0), bevel=0.05):
    """A box with these half-extents, bevelled so stone edges catch the light."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    if bevel > 0:
        bmesh.ops.scale(bm, vec=Vector(half), verts=bm.verts)
        bmesh.ops.bevel(bm, geom=bm.edges[:] + bm.verts[:], offset=bevel, segments=2, affect="EDGES")
    else:
        bmesh.ops.scale(bm, vec=Vector(half), verts=bm.verts)
    bmesh.ops.rotate(bm, matrix=Euler([math.radians(a) for a in rot_deg]).to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def limb(col, name, a, b, r_a, r_b, segments=16):
    """A tapered cylinder from point a (radius r_a) to point b (radius r_b), capped with spheres."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r_a, radius2=r_b, depth=axis.length)
    # create_cone is centred on the origin along +Z; move its base to the origin, then aim it.
    bmesh.ops.translate(bm, vec=Vector((0, 0, axis.length / 2)), verts=bm.verts)
    rot = Vector((0, 0, 1)).rotation_difference(axis.normalized()).to_matrix()
    bmesh.ops.rotate(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=a, verts=bm.verts)
    return _mesh_object(name, bm, col)


def cone(col, name, loc, r_bottom, r_top, height, segments=24):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=r_bottom, radius2=r_top, depth=height)
    bmesh.ops.translate(bm, vec=Vector(loc) + Vector((0, 0, height / 2)), verts=bm.verts)
    return _mesh_object(name, bm, col)


def torus(col, name, loc, major, minor, tilt_deg=0.0, squash_y=1.0):
    bm = bmesh.new()
    seg_major, seg_minor = 32, 10
    verts = []
    for i in range(seg_major):
        u = 2 * math.pi * i / seg_major
        ring = []
        for j in range(seg_minor):
            v = 2 * math.pi * j / seg_minor
            r = major + minor * math.cos(v)
            ring.append(bm.verts.new((r * math.cos(u), r * math.sin(u), minor * math.sin(v))))
        verts.append(ring)
    for i in range(seg_major):
        for j in range(seg_minor):
            a, b = verts[i][j], verts[(i + 1) % seg_major][j]
            c, d = verts[(i + 1) % seg_major][(j + 1) % seg_minor], verts[i][(j + 1) % seg_minor]
            bm.faces.new((a, b, c, d))
    bmesh.ops.scale(bm, vec=Vector((1.0, squash_y, 1.0)), verts=bm.verts)
    bmesh.ops.rotate(bm, matrix=Euler((math.radians(tilt_deg), 0, 0)).to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def _apply(obj, mod):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def drape(col, name, z_top, z_bottom, r_top, r_bottom, gap_top_deg, gap_bottom_deg, seed,
          rows=28, segments=96, folds=9, fold_depth=0.05, hem_jag=0.3, thickness=0.025, holes=0, hem_lift=0.0,
          trim=0.0, shoulder=None, flare=1.4):
    """
    Cloth hanging around the body: an elliptical shell open at the front (-Y), wider at the hem, rippled into folds
    that deepen toward the hem, with a ragged hem of torn strips. r_top and r_bottom are (x, y) radii; the front gap is
    in degrees and may change with height. holes: torn holes in the lower half. hem_lift raises the hem at the front
    edges, so an open mantle cascades longer at the sides. trim pushes the cloth along the front edges out into a
    raised border. shoulder=(z, (rx, ry)) bends the profile there, so cloth can fall from the neck over the shoulders.
    """
    rng = random.Random(seed)
    # Ragged hem: mostly at full length, torn up in V-shaped notches of different depths and widths.
    hem = [rng.uniform(0, 0.02) for _ in range(segments + 1)]
    for _ in range(segments // 4):
        c, depth, width = rng.randrange(segments + 1), hem_jag * rng.random() ** 1.5, rng.uniform(1.5, 5.0)
        for i in range(max(0, int(c - width)), min(segments, int(c + width)) + 1):
            hem[i] = max(hem[i], depth * (1 - abs(i - c) / width))
    waves = [(folds * k, 1.0 / k ** 1.3, rng.uniform(0, 2 * math.pi)) for k in (1.0, 1.7, 2.9, 4.3)]
    tears = [(rng.uniform(0, 1), rng.uniform(0.45, 0.93), rng.uniform(0.6, 1.4), rng.uniform(0, 6.3)) for _ in range(holes)]
    open_front = gap_top_deg > 0 or gap_bottom_deg > 0
    bm = bmesh.new()
    grid = []
    for r in range(rows + 1):
        t = r / rows  # 0 at the top, 1 at the hem
        z = z_top + (z_bottom - z_top) * t
        if shoulder and z >= shoulder[0]:
            u = math.sin((z_top - z) / (z_top - shoulder[0]) * math.pi / 2)  # rounds over the shoulder
            rx = r_top[0] + (shoulder[1][0] - r_top[0]) * u
            ry = r_top[1] + (shoulder[1][1] - r_top[1]) * u
        else:
            z0, (bx, by) = (shoulder[0], shoulder[1]) if shoulder else (z_top, r_top)
            u = ((z0 - z) / (z0 - z_bottom)) ** flare
            rx = bx + (r_bottom[0] - bx) * u
            ry = by + (r_bottom[1] - by) * u
        gap = math.radians(gap_top_deg + (gap_bottom_deg - gap_top_deg) * t)
        row = []
        for i in range(segments + 1):
            a = gap / 2 + (2 * math.pi - gap) * i / segments  # angle from the front (-Y)
            ripple = sum(amp * math.sin(a * n + ph) for n, amp, ph in waves)
            k = 1.0 + ripple * fold_depth * (0.25 + t)
            edge = min(i, segments - i)
            if open_front and trim and edge <= 2:
                k += trim  # the raised border along the front edges
            # Tear the hem up by squeezing the cloth above it, so the rows never fold over each other.
            zone = hem[i] + 0.15
            zz = z
            if z - z_bottom < zone:
                zz = z_bottom + hem[i] + (z - z_bottom) * (zone - hem[i]) / zone
            zz += max(0.0, math.cos(a)) ** 2 * hem_lift * t
            row.append(bm.verts.new((rx * k * math.sin(a), -ry * k * math.cos(a), zz)))
        grid.append(row)
    for r in range(rows):
        t = (r + 0.5) / rows
        for i in range(segments):
            f = (i + 0.5) / segments
            if any(_torn(f, t, c, h, sz, ph) for c, h, sz, ph in tears):
                continue
            bm.faces.new((grid[r][i], grid[r][i + 1], grid[r + 1][i + 1], grid[r + 1][i]))
    if not open_front:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)  # close the seam at the front
    bm.normal_update()
    obj = _mesh_object(name, bm, col)
    mod = obj.modifiers.new("thickness", "SOLIDIFY")
    mod.thickness = thickness
    return _apply(obj, mod)


def _torn(f, t, c, h, size, phase):
    """Whether a face at (f, t) falls in a torn hole: an ellipse whose edge wobbles, so it reads as ripped."""
    du, dv = (f - c) / (0.03 * size), (t - h) / (0.05 * size)
    wobble = 1.0 + 0.35 * math.sin(math.atan2(dv, du) * 3 + phase) + 0.2 * math.sin(math.atan2(dv, du) * 7 + phase * 2)
    return du * du + dv * dv < wobble * wobble


def hang(obj, a, b):
    """Re-aims cloth built hanging down the Z axis (top at z = length) so it hangs from a toward b: a sleeve."""
    a, b = Vector(a), Vector(b)
    axis = b - a
    rot = Vector((0, 0, -1)).rotation_difference(axis.normalized()).to_matrix().to_4x4()
    obj.data.transform(Matrix.Translation(a) @ rot @ Matrix.Translation((0, 0, -axis.length)))
    return obj


def carve(obj, cutters):
    """Cuts the cutters out of obj (eye sockets, a neckline) and deletes them."""
    for c in cutters:
        mod = obj.modifiers.new("cut", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.object = c
        try:
            mod.solver = "EXACT"
        except TypeError:
            pass
        _apply(obj, mod)
        bpy.data.objects.remove(c, do_unlink=True)
    return obj


# Material slots on the lich, so Unity can colour the robe, mantle, bone, and bronze apart (concept: blue-grey gown,
# verdigris mantle, bone, gilded bronze). Viewport colours only; Unity assigns its own materials by these names.
SLOT_COLOURS = {
    "Robe": (0.36, 0.43, 0.55, 1.0),
    "Mantle": (0.22, 0.40, 0.34, 1.0),
    "Bone": (0.80, 0.76, 0.66, 1.0),
    "Metal": (0.62, 0.50, 0.28, 1.0),
    "Hair": (0.30, 0.34, 0.31, 1.0),
    "Marble": (0.86, 0.81, 0.70, 1.0),
    "Leather": (0.45, 0.28, 0.16, 1.0),
    "Pages": (0.92, 0.86, 0.72, 1.0),
    "Ribbon": (0.62, 0.76, 0.80, 1.0),
}


def paint(obj, slot):
    mat = bpy.data.materials.get(slot) or bpy.data.materials.new(slot)
    mat.diffuse_color = SLOT_COLOURS[slot]
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    return obj


def roughen(obj, amount, seed):
    """Push vertices in and out a little, so a shape reads as hewn or cast rather than machined."""
    rng = random.Random(seed)
    for v in obj.data.vertices:
        v.co += v.normal * rng.uniform(-amount, amount)


def finish(parts, name, scale=1.0, smooth_deg=35):
    """Join the parts into one mesh, scale it, smooth it by angle, and cube-project UVs."""
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = obj.data.name = name
    obj.scale = (scale, scale, scale)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_deg))
    except (AttributeError, RuntimeError):
        bpy.ops.object.shade_smooth()
    cube_uvs(obj)
    return obj


def part(parts, name, pivot, parent, scale=1.0, smooth_deg=35):
    """
    A moving part: joined like finish, with its origin moved to the joint it turns around (pivot, before scaling),
    and parented without moving it. Unity animates these by name (GuardianMotion).
    """
    obj = finish(parts, name, scale, smooth_deg)
    joint = Vector(pivot) * scale
    obj.data.transform(Matrix.Translation(-joint))
    obj.location = joint
    obj.parent = parent
    # Both are pure translations here, so the inverse of the parent's offset keeps the part where it is.
    obj.matrix_parent_inverse = Matrix.Translation(-parent.matrix_world.to_translation())
    return obj


def cube_uvs(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=TEXTURE_METRES, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---------------------------------------------------------------- Guardians

def _join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    obj.name = name
    return obj


def _turned(obj, matrix):
    obj.data.transform(matrix)
    return obj


def _chain(col, out, points, r_a, r_b, joints=True):
    """Bones or cords through a list of points, tapering from r_a to r_b, with a knuckle at each joint."""
    n = len(points) - 1
    for k in range(n):
        ra = r_a + (r_b - r_a) * k / n
        rb = r_a + (r_b - r_a) * (k + 1) / n
        out.append(limb(col, "bone", points[k], points[k + 1], ra, rb, segments=8))
        if joints and k > 0:
            out.append(sphere(col, "knuckle", points[k], (ra * 1.25,) * 3, segments=8, rings=5))


def lich(col):
    """
    Alma Mater as a lich (concept: "Verdigris Lich Scholar", 2026-11-23 turnaround): a tall skeletal scholar with a
    Gibson-style updo, a laurel wreath and a small tower finial; a blue-grey chiton in layered, torn tiers under a
    verdigris mantle with a raised border, clasped at both shoulders; a crowned sceptre gripped in the bony right hand;
    a bronze-cornered book with a lamp emblem hanging from the belt; sandalled bone feet; the owl on the sash.
    Material slots (Robe, Mantle, Bone, Metal, Hair) let Unity colour them apart. The eyes are their own object
    ("Eyes") so Unity can make them glow; Head, ArmR (with the sceptre), ArmL, and Owl are separate parts.
    """
    p, head, arm_r, arm_l = [], [], [], []

    def add(out, obj, slot):
        out.append(paint(obj, slot))
        return obj

    # ---- Robe: the gown to the floor, two torn tiers over it, the bodice with a V neckline, the wrapped sash.
    add(p, drape(col, "gown", 3.02, 0.02, (0.3, 0.22), (0.6, 0.46), 0, 0, seed=1, folds=12, fold_depth=0.07,
                 hem_jag=0.12, thickness=0.03), "Robe")
    add(p, drape(col, "tier_mid", 2.95, 1.2, (0.33, 0.25), (0.62, 0.47), 0, 0, seed=6, folds=11, fold_depth=0.09,
                 hem_jag=0.42), "Robe")
    add(p, drape(col, "tier_low", 1.6, 0.3, (0.44, 0.34), (0.68, 0.52), 0, 0, seed=5, folds=10, fold_depth=0.09,
                 hem_jag=0.38), "Robe")
    bodice = sphere(col, "bodice", (0, 0.0, 3.22), (0.3, 0.22, 0.4), segments=32, rings=16)
    carve(bodice, [box(col, "v_neck", (0, -0.25, 3.63), (0.1, 0.2, 0.1), rot_deg=(0, 45, 0), bevel=0.0)])
    add(p, bodice, "Robe")
    sash = drape(col, "sash", 3.13, 2.9, (0.325, 0.245), (0.345, 0.26), 0, 0, seed=8, rows=6, segments=96, folds=14,
                 fold_depth=0.07, hem_jag=0.0, thickness=0.03)
    add(p, _turned(sash, Matrix.Translation((0, 0, 3.0)) @ Matrix.Rotation(math.radians(-9), 4, "Y")
                   @ Matrix.Translation((0, 0, -3.0))), "Robe")
    tail = drape(col, "sash_tail", 0.6, 0.0, (0.05, 0.03), (0.09, 0.045), 0, 0, seed=9, rows=10, segments=24, folds=3,
                 fold_depth=0.12, hem_jag=0.1, thickness=0.02)
    add(p, hang(tail, (0.27, -0.2, 3.0), (0.33, -0.27, 2.4)), "Robe")

    # ---- Mantle: open at the front with a raised border, cascading longer at the sides, torn; a capelet over it.
    add(p, drape(col, "mantle", 3.74, 0.04, (0.2, 0.16), (0.95, 0.72), 130, 100, seed=3, folds=8, fold_depth=0.11,
                 hem_jag=0.5, hem_lift=0.9, trim=0.045, shoulder=(3.5, (0.52, 0.36))), "Mantle")
    add(p, drape(col, "cape", 3.78, 2.85, (0.21, 0.17), (0.74, 0.56), 70, 80, seed=4, folds=10, fold_depth=0.09,
                 hem_jag=0.22, trim=0.04, shoulder=(3.48, (0.56, 0.39))), "Mantle")
    add(p, torus(col, "neckline", (0, -0.02, 3.64), 0.2, 0.022, tilt_deg=10, squash_y=0.8), "Mantle")
    for x in (0.19, -0.19):  # the round clasps at both shoulders
        add(p, limb(col, "clasp", (x, -0.19, 3.6), (x, -0.235, 3.6), 0.058, 0.055, segments=20), "Metal")
        add(p, torus(col, "clasp_rim", (x, -0.235, 3.6), 0.05, 0.009, tilt_deg=90), "Metal")
        add(p, sphere(col, "clasp_boss", (x, -0.24, 3.6), (0.022, 0.014, 0.022), segments=12, rings=6), "Metal")

    # ---- Belt, buckle, and the tassel at the front.
    add(p, torus(col, "belt", (0, 0.0, 2.86), 0.375, 0.03, tilt_deg=4, squash_y=0.75), "Metal")
    add(p, box(col, "buckle", (0, -0.29, 2.86), (0.05, 0.015, 0.04), bevel=0.008), "Metal")
    add(p, limb(col, "tassel_cord", (0.07, -0.29, 2.84), (0.08, -0.33, 2.5), 0.012, 0.012, segments=6), "Metal")
    add(p, sphere(col, "tassel_knot", (0.08, -0.33, 2.49), (0.03, 0.03, 0.03), segments=12, rings=6), "Metal")
    add(p, cone(col, "tassel_cap", (0.08, -0.33, 2.38), 0.045, 0.02, 0.1, segments=12), "Metal")
    for k in range(8):
        a = 2 * math.pi * k / 8
        dx, dy = 0.032 * math.cos(a), 0.032 * math.sin(a)
        add(p, limb(col, "tassel_strand", (0.08 + dx, -0.33 + dy, 2.39), (0.08 + dx * 1.3, -0.33 + dy * 1.3, 2.2),
                    0.01, 0.006, segments=5), "Metal")

    # ---- The book on the right hip (-X): parchment block, verdigris covers, bronze corners, lamp-and-laurel emblem.
    bx, by, bz = -0.36, -0.37, 2.38
    add(p, box(col, "pages", (bx, by, bz), (0.14, 0.05, 0.19), bevel=0.006), "Bone")
    for dy in (-0.058, 0.058):
        add(p, box(col, "cover", (bx, by + dy, bz), (0.155, 0.011, 0.205), bevel=0.006), "Mantle")
    add(p, box(col, "spine", (bx + 0.155, by, bz), (0.014, 0.07, 0.205), bevel=0.008), "Mantle")
    for cx in (-1, 1):
        for cz in (-1, 1):
            add(p, box(col, "corner", (bx + cx * 0.135, by - 0.07, bz + cz * 0.185), (0.028, 0.006, 0.028),
                       bevel=0.004), "Metal")
    add(p, torus(col, "emblem_laurel", (bx, by - 0.072, bz), 0.08, 0.007, tilt_deg=90), "Metal")
    add(p, sphere(col, "lamp", (bx, by - 0.074, bz - 0.01), (0.045, 0.012, 0.022), segments=16, rings=8), "Metal")
    add(p, box(col, "lamp_foot", (bx, by - 0.074, bz - 0.04), (0.025, 0.008, 0.01), bevel=0.0), "Metal")
    add(p, limb(col, "lamp_spout", (bx - 0.04, by - 0.074, bz - 0.005), (bx - 0.07, by - 0.074, bz + 0.01),
                0.01, 0.006, segments=6), "Metal")
    add(p, cone(col, "lamp_flame", (bx - 0.07, by - 0.074, bz + 0.012), 0.012, 0.0, 0.04, segments=8), "Metal")
    for sx in (-0.09, 0.09):
        add(p, limb(col, "strap", (bx + sx * 0.6, -0.27, 2.86), (bx + sx, by, bz + 0.2), 0.012, 0.012, segments=6),
            "Metal")
        add(p, torus(col, "strap_ring", (bx + sx, by, bz + 0.215), 0.022, 0.006, tilt_deg=90), "Metal")

    # ---- Sandalled bone feet under the gown.
    for x in (0.12, -0.12):
        add(p, box(col, "sole", (x, -0.48, 0.015), (0.06, 0.125, 0.015), bevel=0.008), "Metal")
        add(p, sphere(col, "instep", (x, -0.43, 0.06), (0.045, 0.08, 0.035), segments=12, rings=6), "Bone")
        for k in range(5):
            dx = (k - 2) * 0.021
            add(p, limb(col, "toe", (x + dx, -0.47, 0.05), (x + dx * 1.15, -0.585, 0.036), 0.012, 0.009,
                        segments=6), "Bone")
        for y in (-0.44, -0.5):
            add(p, torus(col, "strap", (x, y, 0.05), 0.056, 0.008, tilt_deg=90, squash_y=0.7), "Metal")

    # ---- Bare bones above the neckline: vertebrae, collarbones, sternum, upper ribs.
    for k in range(5):
        z = 3.63 + 0.045 * k
        add(p, limb(col, "vertebra", (0, 0.02, z), (0, 0.02, z + 0.028), 0.036, 0.034, segments=10), "Bone")
    add(p, limb(col, "spine", (0, 0.02, 3.6), (0, 0.0, 3.86), 0.02, 0.02, segments=8), "Bone")
    for x in (1, -1):
        add(p, limb(col, "collarbone", (x * 0.03, -0.13, 3.63), (x * 0.21, -0.07, 3.68), 0.014, 0.012,
                    segments=8), "Bone")
    add(p, box(col, "sternum", (0, -0.155, 3.52), (0.025, 0.01, 0.075), bevel=0.008), "Bone")
    for z, r in ((3.57, 0.13), (3.51, 0.145), (3.45, 0.155)):
        add(p, torus(col, "rib", (0, -0.02, z), r, 0.009, tilt_deg=-12, squash_y=0.85), "Bone")

    # ---- Head: skull with sockets, cheekbones and teeth; the updo, wreath and finial.
    def socket_cutters():
        cut = [sphere(col, "socket", (x, -0.2, 3.99), (0.04, 0.075, 0.036), segments=16, rings=8)
               for x in (0.052, -0.052)]
        cut.append(sphere(col, "nasal", (0, -0.2, 3.925), (0.018, 0.04, 0.03), segments=12, rings=6))
        return cut

    cranium = carve(sphere(col, "cranium", (0, 0.0, 4.04), (0.155, 0.185, 0.17), segments=32, rings=16),
                    socket_cutters())
    face = carve(sphere(col, "face", (0, -0.09, 3.94), (0.1, 0.11, 0.11), segments=24, rings=12), socket_cutters())
    brow = carve(sphere(col, "brow", (0, -0.165, 4.025), (0.1, 0.035, 0.026), segments=20, rings=8), socket_cutters())
    add(head, cranium, "Bone")
    add(head, face, "Bone")
    add(head, brow, "Bone")
    for x in (1, -1):
        add(head, sphere(col, "cheekbone", (x * 0.085, -0.14, 3.955), (0.03, 0.045, 0.02), segments=12, rings=6),
            "Bone")
        add(head, limb(col, "jaw", (x * 0.045, -0.15, 3.82), (x * 0.088, -0.07, 3.85), 0.022, 0.02, segments=8),
            "Bone")
        add(head, limb(col, "ramus", (x * 0.088, -0.07, 3.85), (x * 0.092, -0.06, 3.92), 0.02, 0.016, segments=8),
            "Bone")
    add(head, box(col, "chin", (0, -0.152, 3.817), (0.045, 0.03, 0.02), bevel=0.014), "Bone")
    for k in range(8):
        a = (k - 3.5) * 0.18
        x, y = 0.07 * math.sin(a), -0.11 - 0.07 * math.cos(a)
        add(head, box(col, "tooth", (x, y, 3.873), (0.0085, 0.008, 0.016), bevel=0.003), "Bone")
        add(head, box(col, "tooth", (x * 0.95, y + 0.004, 3.842), (0.008, 0.008, 0.014), bevel=0.003), "Bone")

    hair = sphere(col, "hair", (0, 0.02, 4.08), (0.2, 0.218, 0.18), segments=48, rings=24)
    for v in hair.data.vertices:  # strands swept up and back
        a = math.atan2(v.co.x, -(v.co.y - 0.02))
        v.co += v.normal * 0.008 * math.sin(a * 22 + v.co.z * 12)
    carve(hair, [sphere(col, "face_opening", (0, -0.25, 3.92), (0.15, 0.17, 0.155), segments=24, rings=12)])
    add(head, hair, "Hair")
    add(head, torus(col, "pompadour", (0, -0.04, 4.13), 0.125, 0.05, tilt_deg=-22, squash_y=0.95), "Hair")
    for x in (1, -1):
        add(head, sphere(col, "side_roll", (x * 0.17, 0.0, 4.1), (0.06, 0.12, 0.07), segments=16, rings=8), "Hair")
    add(head, torus(col, "bun_coil", (0, 0.2, 4.13), 0.065, 0.036, tilt_deg=75), "Hair")
    add(head, torus(col, "bun_coil", (0, 0.225, 4.135), 0.032, 0.026, tilt_deg=75), "Hair")
    add(head, sphere(col, "bun", (0, 0.21, 4.13), (0.05, 0.05, 0.05), segments=16, rings=8), "Hair")

    tilt = Matrix.Rotation(math.radians(-12), 4, "X")
    add(head, torus(col, "wreath_band", (0, 0.01, 4.12), 0.2, 0.009, tilt_deg=-12), "Metal")
    for k in range(26):
        a = 2 * math.pi * (k + 0.5) / 26
        if abs(math.sin(a / 2)) < 0.22:
            continue  # the two branches meet at the front
        for row, (lift, out) in enumerate(((0.012, 0.0), (-0.004, 0.012))):
            leaf = ico(col, "leaf", (0, 0, 0), (0.062, 0.022, 0.007), subdivisions=1)
            ring = Vector((0.2 * math.sin(a), -0.2 * math.cos(a), lift))
            ring += Vector((math.sin(a), -math.cos(a), 0)) * out
            pitch = math.radians(50 if row == 0 else -5)
            m = (Matrix.Translation(Vector((0, 0.01, 4.12)) + (tilt @ ring)) @ tilt
                 @ Matrix.Rotation(a + (0.35 if a < math.pi else -0.35), 4, "Z") @ Matrix.Rotation(pitch, 4, "Y"))
            add(head, _turned(leaf, m), "Mantle")
    add(head, limb(col, "finial_stem", (0, -0.02, 4.2), (0, -0.02, 4.33), 0.03, 0.024, segments=12), "Metal")
    add(head, torus(col, "finial_ring", (0, -0.02, 4.27), 0.034, 0.008), "Metal")
    add(head, sphere(col, "finial_knob", (0, -0.02, 4.35), (0.045, 0.045, 0.04), segments=16, rings=8), "Metal")
    add(head, cone(col, "finial_spire", (0, -0.02, 4.38), 0.028, 0.0, 0.1, segments=10), "Metal")

    # ---- Right arm (-X): bones rising to the sceptre, the hand wrapped round it, a wide torn sleeve hanging.
    sx, sy = -0.72, -0.22
    add(arm_r, limb(col, "humerus", (-0.4, 0.0, 3.5), (-0.5, -0.02, 3.0), 0.035, 0.03, segments=10), "Bone")
    for off in (0.014, -0.014):
        add(arm_r, limb(col, "forearm_r", (-0.5, -0.02 + off, 3.0), (-0.65, -0.2 + off, 3.29), 0.018, 0.014,
                        segments=8), "Bone")
    add(arm_r, sphere(col, "wrist_r", (-0.655, -0.21, 3.3), (0.03, 0.03, 0.035), segments=12, rings=6), "Bone")
    for j in range(4):  # fingers curl round the shaft from the palm side (+X) through the front
        z = 3.35 - j * 0.032
        pts = [(sx + 0.065 * math.cos(t), sy + 0.065 * math.sin(t), z) for t in (0.2, -0.9, -1.9, -2.7, -3.3)]
        pts[0] = (-0.66, -0.21 - 0.015 * j, 3.33 - j * 0.02)
        _chain(col, arm_r, pts, 0.011, 0.008)
    thumb = [(-0.66, -0.2, 3.31), (sx + 0.05, sy + 0.05, 3.36), (sx - 0.01, sy + 0.068, 3.38), (sx - 0.05, sy + 0.05, 3.38)]
    _chain(col, arm_r, thumb, 0.011, 0.009)
    for o in arm_r:
        if not o.data.materials:
            paint(o, "Bone")
    add(arm_r, hang(drape(col, "sleeve_r_upper", 0.55, 0.0, (0.11, 0.11), (0.16, 0.15), 0, 0, seed=21, rows=12,
                          segments=40, folds=5, fold_depth=0.1, hem_jag=0.05), (-0.41, 0.0, 3.52), (-0.5, -0.02, 2.98)),
        "Mantle")
    add(arm_r, hang(drape(col, "sleeve_r", 1.0, 0.0, (0.1, 0.1), (0.3, 0.24), 0, 0, seed=22, rows=18, segments=56,
                          folds=6, fold_depth=0.12, hem_jag=0.3), (-0.52, -0.06, 3.15), (-0.62, -0.12, 2.15)),
        "Mantle")
    # The crowned sceptre: bronze shaft with bands, a knop below the crown, a ferrule at the foot.
    add(arm_r, limb(col, "shaft", (sx, sy, 0.03), (sx, sy, 4.3), 0.03, 0.03, segments=12), "Metal")
    add(arm_r, cone(col, "ferrule", (sx, sy, 0.0), 0.022, 0.036, 0.12, segments=12), "Metal")
    for z in (0.15, 0.9, 1.6, 2.3, 3.0, 3.6, 3.95):
        add(arm_r, torus(col, "band", (sx, sy, z), 0.036, 0.011), "Metal")
    add(arm_r, sphere(col, "knop", (sx, sy, 4.18), (0.055, 0.055, 0.06), segments=16, rings=8), "Metal")
    add(arm_r, cone(col, "collar", (sx, sy, 4.24), 0.035, 0.06, 0.1, segments=16), "Metal")
    add(arm_r, cone(col, "crown_cup", (sx, sy, 4.34), 0.06, 0.1, 0.1, segments=24), "Metal")
    add(arm_r, torus(col, "crown_rim", (sx, sy, 4.44), 0.1, 0.016), "Metal")
    for k in range(8):
        a = 2 * math.pi * k / 8
        cx, cy = sx + 0.1 * math.cos(a), sy + 0.1 * math.sin(a)
        add(arm_r, sphere(col, "jewel", (cx * 1.0, cy, 4.44), (0.016, 0.016, 0.016), segments=8, rings=4), "Metal")
        add(arm_r, cone(col, "point", (cx, cy, 4.45), 0.016, 0.0, 0.05, segments=6), "Metal")
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        arch = [(sx + 0.1 * math.cos(a) * math.cos(t), sy + 0.1 * math.sin(a) * math.cos(t), 4.45 + 0.14 * math.sin(t))
                for t in (0.0, 0.4, 0.8, 1.2, math.pi / 2)]
        _chain(col, arm_r, arch, 0.01, 0.01)
        for q in arch[1:-1]:
            arm_r.append(sphere(col, "pearl", q, (0.014,) * 3, segments=8, rings=4))
    for o in arm_r:
        if not o.data.materials:
            paint(o, "Metal")
    add(arm_r, sphere(col, "orb", (sx, sy, 4.62), (0.034, 0.034, 0.034), segments=12, rings=6), "Metal")
    add(arm_r, box(col, "cross_v", (sx, sy, 4.7), (0.009, 0.009, 0.055), bevel=0.0), "Metal")
    add(arm_r, box(col, "cross_h", (sx, sy, 4.72), (0.03, 0.009, 0.009), bevel=0.0), "Metal")

    # ---- Left arm (+X): hangs at the side in its sleeve, the bony hand open below it.
    add(arm_l, limb(col, "humerus", (0.4, 0.0, 3.5), (0.47, 0.0, 2.95), 0.035, 0.03, segments=10), "Bone")
    for off in (0.014, -0.014):
        add(arm_l, limb(col, "forearm_l", (0.47, off, 2.95), (0.505, -0.06 + off, 2.42), 0.018, 0.014, segments=8),
            "Bone")
    add(arm_l, sphere(col, "wrist_l", (0.508, -0.065, 2.4), (0.028, 0.035, 0.03), segments=12, rings=6), "Bone")
    for j, y in enumerate((-0.095, -0.072, -0.05, -0.03)):
        length = (0.95, 1.0, 0.95, 0.8)[j]
        pts = [(0.51, y + 0.01, 2.38), (0.515, y, 2.38 - 0.09 * length), (0.505, y - 0.004, 2.38 - 0.15 * length),
               (0.49, y - 0.004, 2.38 - 0.19 * length)]
        _chain(col, arm_l, pts, 0.01, 0.007)
    _chain(col, arm_l, [(0.505, -0.09, 2.39), (0.49, -0.125, 2.33), (0.475, -0.14, 2.28)], 0.011, 0.008)
    for o in arm_l:
        if not o.data.materials:
            paint(o, "Bone")
    add(arm_l, hang(drape(col, "sleeve_l", 0.9, 0.0, (0.11, 0.11), (0.25, 0.2), 0, 0, seed=23, rows=16, segments=48,
                          folds=5, fold_depth=0.12, hem_jag=0.28), (0.42, 0.0, 3.52), (0.5, -0.03, 2.6)),
        "Mantle")

    s = 1.05
    body = finish(p, "alma_mater", scale=s, smooth_deg=60)
    # Moving parts, each pivoting at its joint (GuardianMotion; off for now, but the split costs nothing).
    head_obj = part(head, "Head", (0, -0.02, 3.8), body, scale=s, smooth_deg=60)
    part(arm_r, "ArmR", (-0.42, 0.0, 3.5), body, scale=s, smooth_deg=60)
    part(arm_l, "ArmL", (0.42, 0.0, 3.5), body, scale=s, smooth_deg=60)
    part([sphere(col, "eye_l", (0.052, -0.162, 3.99), (0.017, 0.012, 0.016)),
          sphere(col, "eye_r", (-0.052, -0.162, 3.99), (0.017, 0.012, 0.016))], "Eyes", (0, -0.02, 3.8), head_obj,
         scale=s)

    # The owl perches on the sash at the left of the chest (GDD 6.7). Separate so Unity can show it only while it is
    # out (its material comes from Unity); while hidden, it is tucked into the robe.
    ox, oy, oz = 0.22, -0.34, 3.16
    owl = [
        sphere(col, "owl_body", (ox, oy, oz), (0.075, 0.065, 0.1), segments=20, rings=10),
        sphere(col, "owl_wing", (ox + 0.058, oy + 0.01, oz - 0.005), (0.026, 0.055, 0.085), segments=12, rings=8),
        sphere(col, "owl_wing", (ox - 0.058, oy + 0.01, oz - 0.005), (0.026, 0.055, 0.085), segments=12, rings=8),
        sphere(col, "owl_head", (ox, oy - 0.01, oz + 0.12), (0.062, 0.056, 0.052), segments=20, rings=10),
        cone(col, "owl_tail", (ox, oy + 0.04, oz - 0.15), 0.01, 0.04, 0.07, segments=8),
    ]
    for x in (0.024, -0.024):
        owl.append(sphere(col, "owl_disc", (ox + x, oy - 0.058, oz + 0.125), (0.027, 0.01, 0.027), segments=12, rings=6))
        owl.append(sphere(col, "owl_eye", (ox + x, oy - 0.066, oz + 0.128), (0.012, 0.008, 0.012), segments=8, rings=4))
        owl.append(cone(col, "owl_tuft", (ox + x * 1.6, oy - 0.01, oz + 0.155), 0.016, 0.0, 0.045, segments=6))
        for t in (-0.012, 0.0, 0.012):
            owl.append(limb(col, "owl_talon", (ox + x + t, oy - 0.03, oz - 0.09), (ox + x + t * 1.3, oy - 0.07, oz - 0.12),
                            0.006, 0.003, segments=5))
    owl.append(limb(col, "owl_beak", (ox, oy - 0.062, oz + 0.112), (ox, oy - 0.078, oz + 0.095), 0.009, 0.002,
                    segments=6))
    for o in owl:
        paint(o, "Metal")
    part(owl, "Owl", (ox, oy + 0.01, 3.05), body, scale=s)
    return body


def blob(col, name, elements, resolution=0.06):
    """
    An organic mass from metaball ellipsoids that melt into each other (muscle, fur masses), converted to a mesh.
    elements: (centre, half_extents, rot_deg). Each part uses its own name, so separate parts do not merge.
    """
    mb = bpy.data.metaballs.new(name)
    mb.resolution = resolution
    mb.render_resolution = resolution
    mb.threshold = 0.6
    obj = _link(bpy.data.objects.new(name, mb), col)
    for centre, half, rot in elements:
        el = mb.elements.new(type="ELLIPSOID")
        el.co = centre
        el.radius = 1.0
        # With stiffness 2 and threshold 0.6 a lone element's surface sits at about 0.67 of its size.
        el.size_x, el.size_y, el.size_z = (h * 1.5 for h in half)
        el.rotation = rot if isinstance(rot, Quaternion) else Euler([math.radians(a) for a in rot]).to_quaternion()
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.convert(target="MESH")
    mesh_obj = bpy.context.view_layer.objects.active
    mesh_obj.name = name
    bpy.data.metaballs.remove(mb)
    return mesh_obj


def lock(base, flow, length, width, rng, gravity=0.35, curl=0.25):
    """
    Metaball elements for one flowing lock of mane: from the root along flow, sagging under gravity, curling at the
    tip, flattened like a sculpted ribbon. Locks in one blob melt into each other like cast bronze.
    """
    base, flow = Vector(base), Vector(flow).normalized()
    side = flow.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
    twist = rng.uniform(-0.15, 0.15)
    pts = []
    for k in range(6):
        t = k / 5
        q = base + flow * (length * t) + Vector((0, 0, -gravity * (length * t) ** 2)) + side * (twist * length * t * t)
        if k >= 4:
            q += (Vector((0, 0, 1)) * 0.35 - flow * 0.2) * (curl * length * (t - 0.6))
        pts.append(q)
    elements = []
    for k in range(5):
        a, b = pts[k], pts[k + 1]
        w = width * (1.0 - 0.45 * k / 5)
        d = (b - a)
        quat = Vector((0, 1, 0)).rotation_difference(d.normalized())
        elements.append(((a + b) / 2, (w, d.length * 0.75, w * 0.42), quat))
    return elements


def lion(col):
    """
    Columbia Lion (concept: "Ancient bronze lion guardian" sheet, 2026-11-23): a tall, lean, muscular lion standing
    square, head high and roaring, with a deep chest and tucked waist, long legs, big paws with long curved claws, a
    great mane of long flowing locks over the head, chest, and shoulders, a laurel collar with a temple medallion on
    the chest, glowing eyes, and a long S-curved tail ending in a flame-like tuft. Head (with the mane), the four
    legs, and the tail are separate parts; "Eyes" glows.
    """
    rng = random.Random(17)
    p, head, tail = [], [], []
    legs = {"LegFL": [], "LegFR": [], "LegBL": [], "LegBR": []}

    p.append(blob(col, "lion_body", [
        ((0, -0.85, 1.98), (0.7, 0.62, 0.82), (0, 0, 0)),       # deep chest
        ((0, -0.2, 2.05), (0.54, 0.8, 0.6), (0, 0, 0)),         # ribcage
        ((0, 0.6, 2.25), (0.42, 0.6, 0.36), (0, 0, 0)),         # waist: the back stays level, the belly tucks up
        ((0, 1.35, 2.15), (0.44, 0.5, 0.42), (0, 0, 0)),        # haunch
        ((0.4, -0.85, 2.35), (0.24, 0.42, 0.5), (-15, 0, 0)),   # shoulder blades
        ((-0.4, -0.85, 2.35), (0.24, 0.42, 0.5), (-15, 0, 0)),
        ((0, -1.4, 2.7), (0.42, 0.5, 0.58), (-40, 0, 0)),       # neck, rising
    ] + [e for x in (0.42, -0.42) for e in (
        ((x, -0.95, 1.65), (0.3, 0.36, 0.6), (6, 0, 0)),        # upper forelegs and triceps
        ((x * 0.85, 1.42, 1.82), (0.2, 0.42, 0.44), (-15, 0, 0)),  # thighs, inside the haunch
    )], resolution=0.05))

    # Legs: long, muscled, ending in big paws with long curved claws.
    for x, side in ((0.42, "L"), (-0.42, "R")):
        fore = blob(col, "lion_fore" + side, [
            ((x * 1.05, -1.0, 1.25), (0.23, 0.29, 0.26), (0, 0, 0)),  # elbow
            ((x, -1.02, 0.85), (0.26, 0.28, 0.48), (3, 0, 0)),      # forearm
            ((x, -1.06, 0.33), (0.19, 0.2, 0.16), (0, 0, 0)),       # wrist
            ((x, -1.2, 0.14), (0.25, 0.33, 0.14), (0, 0, 0)),       # paw
        ], resolution=0.04)
        hind = blob(col, "lion_hind" + side, [
            ((x, 1.74, 1.2), (0.23, 0.3, 0.5), (25, 0, 0)),         # gaskin to the hock
            ((x, 1.66, 0.52), (0.17, 0.18, 0.34), (-10, 0, 0)),     # cannon
            ((x, 1.5, 0.14), (0.23, 0.32, 0.14), (0, 0, 0)),        # paw
        ], resolution=0.04)
        legs["LegF" + side].append(fore)
        legs["LegB" + side].append(hind)
        for out, py in ((legs["LegF" + side], -1.2), (legs["LegB" + side], 1.5)):
            for k in range(4):  # toes, each with a long claw curving down to the floor
                tx = x + (k - 1.5) * 0.105
                out.append(sphere(col, "toe", (tx, py - 0.27, 0.12), (0.065, 0.09, 0.08), segments=12, rings=6))
                _chain(col, out, [(tx, py - 0.33, 0.15), (tx, py - 0.43, 0.13), (tx, py - 0.49, 0.06),
                                  (tx, py - 0.5, 0.01)], 0.03, 0.006, joints=False)

    # Head: high and angular, a heavy brow, a long muzzle, the jaw dropped open in a roar.
    head.append(blob(col, "lion_head", [
        ((0, -1.95, 3.32), (0.36, 0.4, 0.34), (0, 0, 0)),          # skull
        ((0, -2.15, 3.46), (0.28, 0.22, 0.11), (25, 0, 0)),        # sloping forehead
        ((0.15, -2.3, 3.4), (0.17, 0.12, 0.075), (0, -25, 0)),     # heavy brow in a frowning V
        ((-0.15, -2.3, 3.4), (0.17, 0.12, 0.075), (0, 25, 0)),
        ((0, -2.42, 3.26), (0.16, 0.32, 0.12), (18, 0, 0)),        # long, broad bridge of the nose
        ((0.13, -2.52, 3.03), (0.16, 0.18, 0.13), (0, 0, 0)),      # whisker pads
        ((-0.13, -2.52, 3.03), (0.16, 0.18, 0.13), (0, 0, 0)),
        ((0.26, -2.05, 3.1), (0.13, 0.22, 0.15), (0, 0, 0)),       # cheeks
        ((-0.26, -2.05, 3.1), (0.13, 0.22, 0.15), (0, 0, 0)),
    ], resolution=0.028))
    head.append(blob(col, "lion_jaw", [
        ((0, -2.3, 2.79), (0.19, 0.27, 0.085), (-25, 0, 0)),       # lower jaw, dropped open in a roar
        ((0, -2.1, 2.9), (0.2, 0.2, 0.1), (0, 0, 0)),
    ], resolution=0.028))
    head.append(sphere(col, "nose", (0, -2.7, 3.15), (0.13, 0.08, 0.075), segments=16, rings=8))
    for x in (0.1, -0.1):
        head.append(limb(col, "fang", (x * 1.2, -2.6, 2.98), (x * 1.1, -2.62, 2.78), 0.035, 0.004, segments=8))
        head.append(limb(col, "fang", (x, -2.48, 2.72), (x * 0.95, -2.52, 2.88), 0.03, 0.004, segments=8))
        head.append(cone(col, "ear", (x * 2.8, -1.82, 3.58), 0.11, 0.025, 0.2, segments=10))

    # The mane: a mass round the head and neck, then long locks: a crown round the face sweeping back, locks down
    # the neck and over the shoulders, and a bib down the chest to the knees.
    # The mane: a great volume over the head, neck, shoulders and chest (concept side view: it reaches the elbows
    # and back to mid-back), its surface sculpted by locks lying along it, flowing back and down, curling at the ends.
    mane = [
        ((0, -1.68, 3.2), (0.74, 0.5, 0.82), (0, 0, 0)),         # halo round the face
        ((0, -1.2, 3.0), (0.64, 0.6, 0.66), (0, 0, 0)),          # back of the head
        ((0, -0.85, 2.5), (0.68, 0.6, 0.62), (0, 0, 0)),         # over the neck and shoulders
        ((0, -1.55, 2.05), (0.55, 0.42, 0.72), (0, 0, 0)),       # chest bib
        ((0.45, -1.15, 1.95), (0.3, 0.42, 0.6), (0, 0, 0)),      # down to the elbows
        ((-0.45, -1.15, 1.95), (0.3, 0.42, 0.6), (0, 0, 0)),
    ]
    for k in range(44):  # round the face, sweeping out and back
        u = 2 * math.pi * k / 44 + rng.uniform(-0.05, 0.05)
        r = 0.56 + rng.uniform(-0.03, 0.04)
        base = Vector((math.sin(u) * r, -2.0 + rng.uniform(0, 0.08), 3.15 + math.cos(u) * r * 1.1))
        flow = Vector((math.sin(u) * 0.9, 0.7, math.cos(u) * 0.8 - 0.3))
        mane += lock(base, flow, rng.uniform(0.4, 0.62), rng.uniform(0.17, 0.22), rng, gravity=0.3, curl=0.35)
    for k in range(95):  # lying along the head, neck and shoulders, flowing back and down
        u = rng.uniform(-2.1, 2.1)
        y = rng.uniform(-1.85, -0.55)
        r = 0.58 + 0.08 * math.sin((y + 1.85) / 1.3 * math.pi)
        zc = 3.05 - 0.55 * (y + 1.85)
        base = Vector((math.sin(u) * r, y, zc + math.cos(u) * r * 0.7))
        flow = Vector((math.sin(u) * 0.6, 0.6, -0.7 + 0.3 * math.cos(u)))
        mane += lock(base, flow, rng.uniform(0.45, 0.7), rng.uniform(0.18, 0.24), rng, gravity=0.3, curl=0.5)
    for k in range(32):  # the bib, falling down the chest
        x = rng.uniform(-0.5, 0.5)
        z = rng.uniform(1.7, 2.75)
        base = Vector((x, -1.9 - 0.08 * (1 - abs(x) / 0.5), z))
        flow = Vector((x * 0.5, -0.1, -1.0))
        mane += lock(base, flow, rng.uniform(0.4, 0.65), rng.uniform(0.17, 0.22), rng, gravity=0.05, curl=0.5)
    head.append(blob(col, "lion_mane", mane, resolution=0.045))

    # The laurel collar across the chest and its temple medallion.
    cc, major, squash = Vector((0, -1.6, 2.4)), 0.8, 0.95
    head.append(torus(col, "collar", cc, major, 0.06, tilt_deg=50, squash_y=squash))
    tilt = Matrix.Rotation(math.radians(50), 4, "X")
    for k in range(22):
        a = math.pi * (0.2 + 0.6 * k / 21) + math.pi / 2  # the front half of the collar
        on = tilt @ Vector((major * math.cos(a), major * squash * math.sin(a), 0.0)) + cc
        leaf = ico(col, "collar_leaf", (0, 0, 0), (0.06, 0.022, 0.008), subdivisions=1)
        head.append(_turned(leaf, Matrix.Translation(on + Vector((0, -0.06, 0))) @ tilt
                            @ Matrix.Rotation(a + math.pi / 2 + (0.4 if k % 2 else -0.4), 4, "Z")))
    front = tilt @ Vector((0, -major * squash, 0)) + cc
    face = Matrix.Translation(front + Vector((0, -0.08, 0))) @ Matrix.Rotation(math.radians(-40), 4, "X") \
        @ Matrix.Diagonal((1.4, 1.4, 1.4, 1))
    medallion = [limb(col, "medallion", (0, 0.03, 0), (0, -0.02, 0), 0.15, 0.15, segments=28),
                 torus(col, "medallion_rim", (0, -0.025, 0), 0.14, 0.018, tilt_deg=90),
                 box(col, "temple_base", (0, -0.035, -0.07), (0.085, 0.012, 0.012), bevel=0.0),
                 box(col, "temple_roof", (0, -0.035, 0.065), (0.09, 0.012, 0.012), bevel=0.0),
                 box(col, "temple_pediment", (0, -0.035, 0.09), (0.05, 0.012, 0.05), rot_deg=(0, 45, 0), bevel=0.0)]
    for cx in (-0.06, -0.02, 0.02, 0.06):
        medallion.append(limb(col, "temple_column", (cx, -0.035, -0.06), (cx, -0.035, 0.055), 0.011, 0.011,
                              segments=6))
    for o in medallion:
        head.append(_turned(o, face))

    # Tail: long, rising then falling in an S, ending in a flame-like tuft of locks.
    pts = [(0, 2.0, 2.25), (0, 2.35, 2.32), (0, 2.8, 2.05), (0.05, 3.08, 1.5), (0.1, 3.15, 0.95), (0.16, 3.02, 0.6)]
    elements = []
    for k, (a, b) in enumerate(zip(pts, pts[1:])):
        a, b = Vector(a), Vector(b)
        w = 0.11 - 0.008 * k
        elements.append(((a + b) / 2, (w, (b - a).length * 0.7, w), Vector((0, 1, 0)).rotation_difference((b - a).normalized())))
    for k in range(10):
        a = 2 * math.pi * k / 10
        flow = Vector((math.cos(a) * 0.35, -0.3 + math.sin(a) * 0.35, -1.0))
        elements += lock((0.16, 3.02, 0.66), flow, rng.uniform(0.35, 0.55), 0.1, rng, gravity=0.1, curl=0.6)
    tail.append(blob(col, "lion_tail", elements, resolution=0.035))

    s = 0.9
    body = finish(p, "columbia_lion", scale=s, smooth_deg=70)
    head_obj = part(head, "Head", (0, -1.45, 2.75), body, scale=s, smooth_deg=70)
    for name, parts in legs.items():
        x = 0.42 if name.endswith("L") else -0.42
        part(parts, name, (x, -1.0, 2.0) if "F" in name else (x, 1.5, 2.1), body, scale=s, smooth_deg=70)
    part(tail, "Tail", (0, 2.0, 2.25), body, scale=s, smooth_deg=70)
    part([sphere(col, "eye", (x, -2.33, 3.32), (0.05, 0.02, 0.022)) for x in (0.15, -0.15)], "Eyes",
         (0, -1.45, 2.75), head_obj, scale=s)
    return body


def stone(col, name, loc, half, rot_deg=(0, 0, 0), rng=None, chips=2, bevel=0.05):
    """A hewn stone block: bevelled, its faces gently uneven, with chipped corners."""
    rng = rng or random.Random(0)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.scale(bm, vec=Vector(half), verts=bm.verts)
    bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=2, affect="EDGES")
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if e.calc_length() > 0.25], cuts=2, use_grid_fill=True)
    for v in bm.verts:
        v.co += Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1))) * 0.012
    obj = _mesh_object(name, bm, col)
    cutters = []
    for _ in range(chips):
        corner = Vector([h * rng.choice((-1, 1)) for h in half])
        size = min(half) * rng.uniform(0.35, 0.6)
        cutters.append(box(col, "chip", corner, (size, size, size),
                           rot_deg=(rng.uniform(20, 70), rng.uniform(20, 70), rng.uniform(0, 90)), bevel=0.0))
    carve(obj, cutters)
    obj.data.transform(Matrix.Translation(loc) @ Euler([math.radians(a) for a in rot_deg]).to_matrix().to_4x4())
    return obj


def tri_prism(col, name, loc, half_w, height, depth):
    """A triangular prism standing on its base (a pediment): base 2*half_w wide, apex height up, depth along Y."""
    bm = bmesh.new()
    vs = []
    for y in (-depth, depth):
        vs.append([bm.verts.new((-half_w, y, 0)), bm.verts.new((half_w, y, 0)), bm.verts.new((0, y, height))])
    bm.faces.new(vs[0][::-1])
    bm.faces.new(vs[1])
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new((vs[0][i], vs[0][j], vs[1][j], vs[1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def temple(col, out, m, slot="Marble"):
    """A small temple facade relief (steps, four columns, architrave, pediment), 0.3 m wide, facing -Y; placed by m."""
    parts = [box(col, "step", (0, 0, -0.1), (0.15, 0.02, 0.018), bevel=0.0),
             box(col, "architrave", (0, 0, 0.075), (0.15, 0.02, 0.018), bevel=0.0),
             tri_prism(col, "pediment", (0, 0, 0.093), 0.16, 0.07, 0.02)]
    for cx in (-0.105, -0.035, 0.035, 0.105):
        parts.append(limb(col, "column", (cx, 0, -0.082), (cx, 0, 0.057), 0.016, 0.016, segments=8))
    for o in parts:
        out.append(paint(_turned(o, m), slot))


def laurel(col, out, m, radius, a0, a1, count, size=1.0, slot="Marble"):
    """A laurel branch relief along an arc (angles in radians, 0 at the top, clockwise seen from the front) in the
    XZ plane facing -Y, leaves alternating out and in; placed by m."""
    count *= 2  # leaves in pairs along the stem
    prev = None
    for k in range(count):
        a = a0 + (a1 - a0) * k / max(1, count - 1)
        pos = Vector((radius * math.sin(a), 0, radius * math.cos(a)))
        if prev is not None:
            out.append(paint(_turned(limb(col, "laurel_stem", prev, pos, 0.007 * size, 0.007 * size, segments=6), m), slot))
        prev = pos
        # Each leaf points along the branch, angled out to one side, set a little off the stem.
        tangent = Vector((math.cos(a), 0, -math.sin(a))) * (1 if a1 > a0 else -1)
        normal = Vector((math.sin(a), 0, math.cos(a))) * (1 if k % 2 else -1)
        centre = pos + (tangent * 0.6 + normal * 0.55).normalized() * 0.03 * size
        leaf = ico(col, "laurel_leaf", (0, 0, 0), (0.042 * size, 0.008 * size, 0.016 * size), subdivisions=1)
        d = (tangent * 0.6 + normal * 0.55)
        turn = math.atan2(-d.z, d.x)
        out.append(paint(_turned(leaf, m @ Matrix.Translation(centre) @ Matrix.Rotation(turn, 4, "Y")), slot))


def stone_leaf(col, out, top, out_dir, length, width, slot="Marble"):
    """A stone leaf hanging from top, tipped outward (out_dir), for the leaf skirts and arm tassets."""
    leaf = ico(col, "stone_leaf", (0, 0, 0), (width, 0.03, length / 2), subdivisions=2)
    out_dir = Vector(out_dir).normalized()
    yaw = math.atan2(out_dir.x, -out_dir.y)
    m = Matrix.Translation(Vector(top) + out_dir * 0.03 - Vector((0, 0, length / 2))) @ Matrix.Rotation(yaw, 4, "Z") \
        @ Matrix.Rotation(math.radians(-14), 4, "X")
    out.append(paint(_turned(leaf, m), slot))


def golem(col):
    """
    Book Keeper (concept: "Ancient marble book-keeper golem" sheet, 2026-11-23): a broad marble golem whose chest is a
    great closed book (laurel reliefs, a central band, a round keyhole lock; page edges on the side), a small stern
    face under a laurel crest with an open book rising behind the head, temple-emblem pauldrons over stone-leaf
    tassets, fluted bracers with laurel bands, big jointed hands, a belt of small leather books with a temple buckle
    and ribbon bookmarks over a stone-leaf skirt, and legs like Ionic columns on toed feet. Material slots: Marble,
    Leather, Pages, Ribbon. Head, both arms, and both legs are separate parts; "Eyes" glows.
    """
    rng = random.Random(11)
    p = []
    groups = {"Head": [], "ArmL": [], "ArmR": [], "LegL": [], "LegR": []}

    def add(out, obj, slot="Marble"):
        out.append(paint(obj, slot))
        return obj

    front = Matrix.Rotation(0, 4, "X")  # reliefs are built facing -Y

    # ---- Chest: a great closed book.
    cz, cy = 2.8, -0.12
    add(p, box(col, "pages", (0.03, cy, cz), (0.7, 0.33, 0.6), bevel=0.01), "Pages")
    for z in [cz - 0.56 + 0.04 * k for k in range(29)]:  # page edges on the side
        add(p, box(col, "page_edge", (0.735, cy, z), (0.006, 0.31, 0.006), bevel=0.0), "Pages")
    for y in (cy - 0.37, cy + 0.37):
        add(p, box(col, "cover", (0, y, cz), (0.78, 0.045, 0.67), bevel=0.02))
    add(p, box(col, "spine", (-0.76, cy, cz), (0.06, 0.4, 0.67), bevel=0.03))
    for z in (cz - 0.45, cz, cz + 0.45):
        add(p, box(col, "spine_band", (-0.82, cy, z), (0.015, 0.38, 0.03), bevel=0.0))
    fy = cy - 0.42  # the front face of the cover
    for (x, z, hx, hz) in ((0, cz + 0.6, 0.72, 0.025), (0, cz - 0.6, 0.72, 0.025), (0.7, cz, 0.025, 0.6),
                           (-0.7, cz, 0.025, 0.6)):
        add(p, box(col, "frame", (x, fy, z), (hx, 0.02, hz), bevel=0.005))
    add(p, box(col, "band", (0, fy - 0.01, cz), (0.13, 0.03, 0.6), bevel=0.01))
    for z in (cz + 0.42, cz + 0.22, cz - 0.22, cz - 0.42):
        add(p, sphere(col, "boss", (0, fy - 0.05, z), (0.04, 0.02, 0.04), segments=12, rings=6))
    for x in (-0.4, 0.4):
        laurel(col, p, Matrix.Translation((x, fy - 0.03, cz - 0.03)), 0.24, 2.7, 0.45, 10, size=1.8)
        laurel(col, p, Matrix.Translation((x, fy - 0.03, cz - 0.03)), 0.24, -2.7, -0.45, 10, size=1.8)
    for x in (-0.6, 0.6):
        for z in (cz - 0.5, cz + 0.5):
            add(p, sphere(col, "corner", (x, fy - 0.02, z), (0.05, 0.02, 0.05), segments=12, rings=6))
    add(p, box(col, "lock_plate", (0, fy - 0.04, cz), (0.24, 0.025, 0.08), bevel=0.01))
    lock = limb(col, "lock", (0, fy - 0.03, cz), (0, fy - 0.1, cz), 0.13, 0.13, segments=28)
    carve(lock, [limb(col, "keyhole", (0, fy - 0.2, cz + 0.025), (0, fy, cz + 0.025), 0.03, 0.03, segments=12),
                 box(col, "keyhole_slot", (0, fy - 0.1, cz - 0.03), (0.015, 0.1, 0.05), bevel=0.0)])
    add(p, lock)
    add(p, torus(col, "lock_rim", (0, fy - 0.1, cz), 0.12, 0.015, tilt_deg=90))
    add(p, box(col, "chest_core", (0, 0.05, cz), (0.6, 0.3, 0.55), bevel=0.05))

    # ---- The open book behind the head, its pages fanned up like a collar.
    for side in (1, -1):
        m = Matrix.Translation((side * 0.04, 0.12, 3.42)) @ Matrix.Rotation(math.radians(side * -38), 4, "Y") \
            @ Matrix.Rotation(math.radians(-18), 4, "X")
        add(p, _turned(box(col, "open_pages", (side * 0.32, 0, 0.22), (0.3, 0.06, 0.22), bevel=0.01), m), "Pages")
        add(p, _turned(box(col, "open_cover", (side * 0.34, 0.07, 0.22), (0.34, 0.02, 0.25), bevel=0.01), m))
        for k in range(6):
            add(p, _turned(box(col, "open_page_line", (side * 0.32, -0.062, 0.05 + 0.065 * k), (0.28, 0.004, 0.004),
                               bevel=0.0), m), "Pages")

    # ---- Waist: a core, a belt of small leather books with a temple buckle and ribbons, a skirt of stone leaves.
    add(p, box(col, "waist", (0, -0.05, 2.0), (0.68, 0.38, 0.22), bevel=0.05))
    add(p, box(col, "pelvis", (0, -0.02, 1.65), (0.62, 0.38, 0.2), bevel=0.05))
    add(p, box(col, "belt", (0, -0.45, 1.86), (0.74, 0.03, 0.03), bevel=0.0))
    add(p, box(col, "belt_top", (0, -0.45, 2.16), (0.74, 0.03, 0.025), bevel=0.0))
    x = -0.7
    while x < 0.68:
        if abs(x) < 0.17:
            x = 0.17
            continue
        w, h = rng.uniform(0.045, 0.06), rng.uniform(0.11, 0.135)
        bx = x + w
        add(p, box(col, "belt_book", (bx, -0.47, 1.9 + h), (w, 0.06, h), bevel=0.006), "Leather")
        for bz in (0.65, -0.65):
            add(p, box(col, "belt_book_band", (bx, -0.532, 1.9 + h + bz * h), (w * 1.02, 0.004, 0.008), bevel=0.0),
                "Pages")
        if rng.random() < 0.22:
            add(p, box(col, "ribbon", (bx, -0.5, 1.78), (0.025, 0.004, 0.13), rot_deg=(0, rng.uniform(-6, 6), 0),
                       bevel=0.0), "Ribbon")
        x += 2 * w + 0.008
    temple(col, p, Matrix.Translation((0, -0.53, 2.0)) @ Matrix.Diagonal((1.0, 1.0, 1.2, 1.0)))
    add(p, box(col, "buckle_back", (0, -0.5, 2.0), (0.17, 0.02, 0.17), bevel=0.01))
    for k in range(26):
        a = 2 * math.pi * k / 26
        if abs(math.sin(a / 2)) < 0.18:
            continue  # the front under the buckle stays clear
        for row, (z, r) in enumerate(((1.8, 1.0), (1.62, 1.06))):
            pos = Vector((0.74 * r * math.sin(a), -0.45 * r * math.cos(a), z))
            stone_leaf(col, p, pos, (math.sin(a), -math.cos(a), 0), 0.32 - 0.05 * row, 0.1)

    # ---- Head: a small stern face under a laurel crest.
    head = groups["Head"]
    add(head, blob(col, "golem_head", [
        ((0, -0.15, 3.72), (0.2, 0.22, 0.24), (0, 0, 0)),          # cranium
        ((0, -0.27, 3.64), (0.16, 0.13, 0.2), (0, 0, 0)),          # face
        ((0, -0.37, 3.75), (0.15, 0.05, 0.035), (0, 0, 0)),        # stern brow
        ((0, -0.41, 3.64), (0.035, 0.06, 0.075), (15, 0, 0)),      # nose
        ((0, -0.31, 3.5), (0.13, 0.12, 0.07), (0, 0, 0)),          # jaw
        ((0.12, -0.3, 3.62), (0.06, 0.08, 0.07), (0, 0, 0)),       # cheekbones
        ((-0.12, -0.3, 3.62), (0.06, 0.08, 0.07), (0, 0, 0)),
    ], resolution=0.022))
    for k in range(9):  # the crest: laurel leaves fanned up and back from the forehead
        a = (k - 4) * 0.22
        m = Matrix.Translation((0.0, -0.22, 3.9)) @ Matrix.Rotation(a, 4, "Y") @ Matrix.Rotation(math.radians(-25), 4, "X")
        add(head, _turned(ico(col, "crest_leaf", (0, 0, 0.13), (0.035, 0.015, 0.12), subdivisions=2), m))

    # ---- Shoulders: temple-emblem pauldrons over the shoulder masses.
    for side in (1, -1):
        add(p, box(col, "shoulder", (side * 0.82, -0.05, 3.2), (0.32, 0.38, 0.3), bevel=0.06))
        tilt = Matrix.Rotation(math.radians(-side * 12), 4, "Y")
        m = Matrix.Translation((side * 1.08, -0.06, 3.42)) @ tilt
        add(p, _turned(box(col, "pauldron", (0, 0, 0), (0.42, 0.46, 0.17), bevel=0.04), m))
        add(p, _turned(box(col, "pauldron_lower", (side * 0.06, 0, -0.26), (0.4, 0.43, 0.09), bevel=0.03), m))
        add(p, _turned(box(col, "pauldron_rim", (0, -0.47, 0), (0.38, 0.015, 0.13), bevel=0.0), m))
        temple(col, p, m @ Matrix.Translation((0, -0.49, 0.0)) @ Matrix.Diagonal((0.85, 1.0, 0.85, 1.0)))
        laurel(col, p, m @ Matrix.Translation((0, -0.48, -0.26)), 0.2, -1.3, 1.3, 9, size=0.8)

    # ---- Arms: stone-leaf tassets over the upper arm, a fluted bracer with laurel bands, a big jointed hand.
    for side, sd in ((1, "L"), (-1, "R")):
        arm = groups["Arm" + sd]
        ax = side * 1.15
        add(arm, limb(col, "upper_arm", (ax, -0.02, 3.15), (ax * 1.03, -0.06, 2.4), 0.26, 0.24, segments=16))
        for row, z in enumerate((3.08, 2.88, 2.68)):
            for k in range(9):
                a = 2 * math.pi * k / 9
                pos = Vector((ax + 0.27 * math.sin(a), -0.04 + 0.27 * math.cos(a), z))
                stone_leaf(col, arm, pos, (math.sin(a), math.cos(a), 0), 0.3, 0.09)
        add(arm, sphere(col, "elbow", (ax * 1.04, -0.08, 2.36), (0.27, 0.27, 0.25), segments=20, rings=10))
        top, bot = Vector((ax * 1.06, -0.1, 2.3)), Vector((ax * 1.08, -0.14, 1.48))
        add(arm, limb(col, "bracer", top, bot, 0.36, 0.38, segments=24))
        for k in range(16):  # flutes
            a = 2 * math.pi * k / 16
            off = Vector((math.cos(a), math.sin(a), 0)) * 0.37
            add(arm, limb(col, "flute", top + off + Vector((0, 0, -0.12)), bot + off * 1.03 + Vector((0, 0, 0.12)),
                          0.025, 0.025, segments=6))
        for z, r in ((2.22, 0.4), (2.1, 0.4), (1.62, 0.42), (1.5, 0.42)):
            add(arm, torus(col, "bracer_ring", (ax * 1.07, -0.12, z), r, 0.035))
        laurel(col, arm, Matrix.Translation((ax * 1.07, -0.53, 1.86)), 0.17, -2.3, 2.3, 10, size=1.4)
        # The hand hangs with the palm toward the body; fingers curl in.
        hx, hy, hz = ax * 1.08, -0.16, 1.2
        add(arm, box(col, "palm", (hx, hy, hz), (0.24, 0.33, 0.28), bevel=0.06))
        add(arm, box(col, "knuckle_guard", (hx + side * 0.02, hy, hz - 0.24), (0.25, 0.34, 0.06), bevel=0.02))
        for k in range(4):
            fy = hy - 0.25 + 0.165 * k
            joints = [Vector((hx, fy, hz - 0.26)), Vector((hx - side * 0.03, fy, hz - 0.5)),
                      Vector((hx - side * 0.13, fy, hz - 0.68)), Vector((hx - side * 0.26, fy, hz - 0.76))]
            for a, b in zip(joints, joints[1:]):
                add(arm, limb(col, "finger", a, b, 0.085, 0.076, segments=8))
                add(arm, sphere(col, "finger_joint", b, (0.088,) * 3, segments=10, rings=5))
        thumb = [Vector((hx - side * 0.18, hy - 0.26, hz - 0.05)), Vector((hx - side * 0.3, hy - 0.38, hz - 0.22)),
                 Vector((hx - side * 0.36, hy - 0.38, hz - 0.42))]
        for a, b in zip(thumb, thumb[1:]):
            add(arm, limb(col, "thumb", a, b, 0.09, 0.08, segments=8))
            add(arm, sphere(col, "thumb_joint", b, (0.09,) * 3, segments=10, rings=5))

    # ---- Legs: Ionic columns, volutes at the knees, fluted shafts with a laurel band, on big toed feet.
    for side, sd in ((1, "L"), (-1, "R")):
        leg = groups["Leg" + sd]
        lx = side * 0.55
        add(leg, limb(col, "thigh", (lx, -0.02, 1.75), (lx, -0.02, 1.28), 0.3, 0.3, segments=16))
        add(leg, box(col, "abacus", (lx, -0.04, 1.25), (0.44, 0.4, 0.06), bevel=0.015))
        add(leg, torus(col, "echinus", (lx, -0.04, 1.16), 0.34, 0.06))
        for vx in (0.4, -0.4):  # the volutes, coiled on the front and back
            add(leg, limb(col, "volute", (lx + vx, -0.42, 1.14), (lx + vx, 0.34, 1.14), 0.1, 0.1, segments=16))
            for vy in (-0.43, 0.35):
                add(leg, torus(col, "volute_coil", (lx + vx, vy, 1.14), 0.075, 0.018, tilt_deg=90))
                add(leg, torus(col, "volute_coil", (lx + vx, vy, 1.14), 0.04, 0.014, tilt_deg=90))
        add(leg, limb(col, "shaft", (lx, -0.04, 1.12), (lx, -0.04, 0.42), 0.3, 0.33, segments=24))
        for k in range(18):
            a = 2 * math.pi * k / 18
            off = Vector((math.cos(a), math.sin(a), 0))
            add(leg, limb(col, "flute", Vector((lx, -0.04, 1.08)) + off * 0.3, Vector((lx, -0.04, 0.46)) + off * 0.33,
                          0.022, 0.022, segments=6))
        add(leg, torus(col, "laurel_band", (lx, -0.04, 0.66), 0.345, 0.03))
        laurel(col, leg, Matrix.Translation((lx, -0.4, 0.66)), 0.5, -0.5, 0.5, 7, size=0.8)
        add(leg, torus(col, "base_torus", (lx, -0.04, 0.38), 0.38, 0.06))
        add(leg, box(col, "plinth", (lx, -0.06, 0.3), (0.44, 0.44, 0.05), bevel=0.015))
        add(leg, box(col, "foot", (lx, -0.14, 0.13), (0.44, 0.52, 0.13), bevel=0.05))
        for k in range(4):
            tx = lx + (k - 1.5) * 0.2
            add(leg, box(col, "toe", (tx, -0.72, 0.1), (0.085, 0.13, 0.1), bevel=0.035))
            add(leg, box(col, "toenail", (tx, -0.84, 0.13), (0.06, 0.02, 0.05), bevel=0.015))

    obj = finish(p, "book_keeper", smooth_deg=30)
    pivots = {"Head": (0, -0.15, 3.5), "ArmL": (1.15, 0, 3.15), "ArmR": (-1.15, 0, 3.15),
              "LegL": (0.55, 0, 1.7), "LegR": (-0.55, 0, 1.7)}
    head_obj = None
    for name, parts in groups.items():
        made = part(parts, name, pivots[name], obj, smooth_deg=30)
        head_obj = made if name == "Head" else head_obj
    part([sphere(col, "eye", (x, -0.355, 3.71), (0.035, 0.012, 0.016)) for x in (0.07, -0.07)], "Eyes",
         (0, -0.15, 3.5), head_obj)
    return obj


# ---------------------------------------------------------------- build and export

def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for child in obj.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = obj
    saved = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
    )
    obj.location = saved


def build(export_dir=EXPORT_DIR):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    col = _collection()
    models = [lich(col), lion(col), golem(col)]
    # Side by side for viewing; the export puts each back at the origin.
    for i, obj in enumerate(models):
        obj.location.x = (i - 1) * 7.0
    if export_dir:
        os.makedirs(export_dir, exist_ok=True)
        for guardian_id, obj in zip(("alma_mater", "columbia_lion", "book_keeper"), models):
            if guardian_id not in GENERATED:
                export(obj, os.path.join(export_dir, guardian_id + ".fbx"))
    return models


if __name__ == "__main__":
    build()
