"""
Blockout models for the three prototype Guardians (GDD 9, visual direction): stand-ins until the paid models arrive.

Run inside Blender (Text Editor → Run Script, or through MCP for Blender). It rebuilds the "Guardians" collection and,
when EXPORT_DIR is set, writes one FBX per Guardian for Unity:

    alma_mater.fbx     skeletal scholar in a torn robe and mantle, crowned sceptre, book on the belt; children "Owl"
                       (the weak point) and "Eyes" (glow)
    columbia_lion.fbx  lion
    book_keeper.fbx    plain stone golem with a bookshelf in its chest

Conventions: metres; origin at the feet; the model faces -Y (Blender's front view), which is +Z (forward) in Unity
with the export settings below. One mesh per Guardian (plus the owl), UVs cube-projected at TEXTURE_METRES per
tile so the CC0 stone and bronze materials tile evenly. Materials are left to Unity.
"""

import math
import os
import random

import bmesh
import bpy
from mathutils import Euler, Vector

COLLECTION = "Guardians"
TEXTURE_METRES = 2.0
EXPORT_DIR = None  # set to ".../game/Assets/_Project/Art/Models/Guardians" to export


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


def drape(col, name, z_top, z_bottom, r_top, r_bottom, gap_top_deg, gap_bottom_deg, seed,
          rows=18, segments=72, folds=9, fold_depth=0.05, hem_jag=0.35, thickness=0.03, torn=0.25, shoulder=None):
    """
    Cloth hanging around the body: an elliptical shell open at the front (-Y), wider at the hem, rippled into folds,
    with a ragged, torn hem. r_top and r_bottom are (x, y) radii; the front gap is in degrees and may change with
    height. Torn holes: that share of the lowest faces is removed. shoulder=(z, (rx, ry)) bends the profile there, so
    cloth can fall from the neck over the shoulders and then hang.
    """
    rng = random.Random(seed)
    hem = [rng.uniform(0.0, hem_jag) * (1.0 if i % 2 else 0.45) for i in range(segments + 1)]
    bm = bmesh.new()
    grid = []
    for r in range(rows + 1):
        t = r / rows  # 0 at the top, 1 at the hem
        z = z_top + (z_bottom - z_top) * t
        if shoulder and z >= shoulder[0]:
            u = (z_top - z) / (z_top - shoulder[0])
            u = math.sin(u * math.pi / 2)  # rounds over the shoulder
            rx = r_top[0] + (shoulder[1][0] - r_top[0]) * u
            ry = r_top[1] + (shoulder[1][1] - r_top[1]) * u
        else:
            z0, (bx, by) = (shoulder[0], shoulder[1]) if shoulder else (z_top, r_top)
            u = ((z0 - z) / (z0 - z_bottom)) ** 1.4
            rx = bx + (r_bottom[0] - bx) * u
            ry = by + (r_bottom[1] - by) * u
        gap = math.radians(gap_top_deg + (gap_bottom_deg - gap_top_deg) * t)
        row = []
        for i in range(segments + 1):
            a = gap / 2 + (2 * math.pi - gap) * i / segments  # angle from the front (-Y)
            ripple = 1.0 + math.sin(a * folds + seed) * fold_depth * (0.3 + t)
            zz = z + (hem[i] if r == rows else 0.0) + (hem[i] * 0.4 * t ** 3 if r == rows - 1 else 0.0)
            row.append(bm.verts.new((rx * ripple * math.sin(a), -ry * ripple * math.cos(a), zz)))
        grid.append(row)
    for r in range(rows):
        for i in range(segments):
            if r >= rows - 2 and rng.random() < torn:
                continue
            bm.faces.new((grid[r][i], grid[r][i + 1], grid[r + 1][i + 1], grid[r + 1][i]))
    if gap_top_deg == 0 and gap_bottom_deg == 0:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)  # close the seam at the front
    bm.normal_update()
    obj = _mesh_object(name, bm, col)
    mod = obj.modifiers.new("thickness", "SOLIDIFY")
    mod.thickness = thickness
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
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


def cube_uvs(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=TEXTURE_METRES, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")


# ---------------------------------------------------------------- Guardians

def lich(col):
    """
    Alma Mater as a lich (concept: "Verdigris Lich Scholar"): a tall skeletal scholar in a layered, torn robe and
    mantle, a laurel wreath with a small finial, a crowned sceptre in the right hand, a book hanging from the belt,
    and the owl perched on the sash. The eyes are their own object ("Eyes") so Unity can make them glow.
    """
    p = []
    # Inner gown to the floor, and the bodice above it.
    p.append(drape(col, "gown", 3.0, 0.02, (0.34, 0.26), (0.72, 0.52), 0, 0, seed=1,
                   folds=11, fold_depth=0.06, hem_jag=0.08, torn=0.1, thickness=0.04))
    p.append(drape(col, "gown_overlay", 2.9, 0.55, (0.38, 0.3), (0.74, 0.55), 70, 40, seed=2,
                   folds=7, fold_depth=0.07, hem_jag=0.3, torn=0.3))
    p.append(sphere(col, "bodice", (0, 0.0, 3.22), (0.33, 0.24, 0.42)))
    p.append(sphere(col, "ribs", (0, -0.12, 3.5), (0.2, 0.12, 0.14)))

    # Mantle over both shoulders, open at the front, hanging almost to the floor with a ragged hem;
    # a short cape over the shoulders and arms.
    p.append(drape(col, "mantle", 3.74, 0.25, (0.2, 0.16), (0.95, 0.72), 120, 95, seed=3,
                   folds=8, fold_depth=0.08, hem_jag=0.45, torn=0.3, shoulder=(3.5, (0.5, 0.34))))
    p.append(drape(col, "cape", 3.78, 2.55, (0.21, 0.17), (0.8, 0.6), 75, 85, seed=4,
                   folds=10, fold_depth=0.07, hem_jag=0.3, torn=0.35, shoulder=(3.48, (0.55, 0.38))))
    for x in (0.3, -0.3):
        p.append(limb(col, "brooch", (x, -0.26, 3.55), (x, -0.31, 3.55), 0.065, 0.06, segments=16))

    # Sash at the waist, the belt below it, a tassel at the front.
    p.append(torus(col, "sash", (0, 0.0, 3.0), 0.36, 0.06, tilt_deg=-6, squash_y=0.75))
    p.append(torus(col, "belt", (0, 0.0, 2.86), 0.38, 0.035, tilt_deg=4, squash_y=0.75))
    p.append(limb(col, "tassel_cord", (0.08, -0.29, 2.84), (0.09, -0.33, 2.45), 0.015, 0.015, segments=6))
    p.append(cone(col, "tassel", (0.09, -0.33, 2.28), 0.05, 0.015, 0.18, segments=10))

    # Book hanging from the belt on the right hip (-X), on two straps.
    p.append(limb(col, "strap_a", (-0.22, -0.27, 2.86), (-0.28, -0.36, 2.62), 0.015, 0.015, segments=6))
    p.append(limb(col, "strap_b", (-0.38, -0.2, 2.86), (-0.44, -0.33, 2.62), 0.015, 0.015, segments=6))
    p.append(box(col, "book", (-0.36, -0.37, 2.4), (0.16, 0.06, 0.21), rot_deg=(0, 0, 8), bevel=0.015))
    p.append(box(col, "book_clasp", (-0.36, -0.44, 2.4), (0.05, 0.015, 0.05), rot_deg=(0, 0, 8), bevel=0.0))

    # Neck, skull, hair gathered in a bun, laurel wreath, and the small finial on top.
    p.append(limb(col, "neck", (0, 0.0, 3.6), (0, -0.02, 3.85), 0.06, 0.055, segments=10))
    p.append(sphere(col, "skull", (0, -0.03, 4.0), (0.17, 0.19, 0.21)))
    p.append(box(col, "jaw", (0, -0.13, 3.83), (0.1, 0.08, 0.045), bevel=0.025))
    p.append(sphere(col, "hair", (0, 0.04, 4.06), (0.18, 0.17, 0.17)))
    p.append(sphere(col, "bun", (0, 0.2, 4.1), (0.11, 0.09, 0.1)))
    p.append(torus(col, "wreath", (0, 0.01, 4.12), 0.17, 0.022, tilt_deg=-12))
    for k in range(14):
        a = 2 * math.pi * k / 14
        if abs(math.sin(a / 2)) < 0.25:
            continue  # the leaves meet at the front
        x, y = 0.18 * math.sin(a), -0.18 * math.cos(a) * 0.98
        p.append(ico(col, "leaf", (x, y + 0.01, 4.13 + 0.04 * math.cos(a)), (0.045, 0.02, 0.022), subdivisions=1))
    p.append(limb(col, "finial_stem", (0, -0.02, 4.2), (0, -0.02, 4.38), 0.03, 0.022, segments=8))
    p.append(sphere(col, "finial_knob", (0, -0.02, 4.34), (0.05, 0.05, 0.045)))
    p.append(cone(col, "finial_tip", (0, -0.02, 4.38), 0.03, 0.0, 0.08, segments=8))

    # Right arm (-X) raised, the bony hand gripping the sceptre; a wide sleeve hangs from it.
    p.append(limb(col, "upper_arm_r", (-0.4, 0.0, 3.5), (-0.5, -0.02, 3.0), 0.07, 0.055, segments=10))
    p.append(limb(col, "forearm_r", (-0.5, -0.02, 3.0), (-0.66, -0.2, 3.3), 0.05, 0.04, segments=10))
    p.append(limb(col, "sleeve_r", (-0.42, 0.0, 3.45), (-0.56, -0.04, 2.6), 0.13, 0.26, segments=16))
    p.append(sphere(col, "hand_r", (-0.7, -0.22, 3.32), (0.06, 0.05, 0.08)))
    # Crowned sceptre, floor to above the head.
    p.append(limb(col, "sceptre", (-0.72, -0.22, 0.02), (-0.72, -0.22, 4.4), 0.035, 0.035, segments=10))
    for z in (0.15, 1.6, 3.05, 3.6, 4.3):
        p.append(torus(col, "sceptre_band", (-0.72, -0.22, z), 0.045, 0.014))
    p.append(cone(col, "crown_base", (-0.72, -0.22, 4.4), 0.05, 0.11, 0.1, segments=16))
    p.append(torus(col, "crown_rim", (-0.72, -0.22, 4.5), 0.1, 0.018))
    for k in range(4):
        a = math.pi / 2 * k
        p.append(limb(col, "crown_arch", (-0.72 + 0.1 * math.cos(a), -0.22 + 0.1 * math.sin(a), 4.5),
                      (-0.72, -0.22, 4.66), 0.012, 0.012, segments=6))
    p.append(sphere(col, "crown_orb", (-0.72, -0.22, 4.68), (0.035, 0.035, 0.035)))
    p.append(box(col, "crown_cross_v", (-0.72, -0.22, 4.77), (0.01, 0.01, 0.06), bevel=0.0))
    p.append(box(col, "crown_cross_h", (-0.72, -0.22, 4.79), (0.035, 0.01, 0.01), bevel=0.0))

    # Left arm (+X) hangs at the side, skeletal hand open; its sleeve falls from the shoulder.
    p.append(limb(col, "upper_arm_l", (0.4, 0.0, 3.5), (0.47, 0.0, 2.95), 0.07, 0.055, segments=10))
    p.append(limb(col, "forearm_l", (0.47, 0.0, 2.95), (0.5, -0.06, 2.45), 0.05, 0.035, segments=10))
    p.append(limb(col, "sleeve_l", (0.42, 0.0, 3.45), (0.52, 0.0, 2.75), 0.13, 0.25, segments=16))
    p.append(sphere(col, "hand_l", (0.51, -0.07, 2.35), (0.05, 0.04, 0.09)))

    # Sandalled feet under the gown.
    for x in (0.12, -0.12):
        p.append(box(col, "foot", (x, -0.48, 0.04), (0.06, 0.13, 0.04), bevel=0.02))
    body = finish(p, "alma_mater", scale=1.05, smooth_deg=60)

    eyes = finish([sphere(col, "eye_l", (0.062, -0.19, 4.02), (0.04, 0.022, 0.034)),
                   sphere(col, "eye_r", (-0.062, -0.19, 4.02), (0.04, 0.022, 0.034))], "Eyes", scale=1.05)
    eyes.parent = body

    # The owl perches on the sash at the left of the chest (GDD 6.7). Separate so Unity can show it only while it is
    # out; while hidden, it is tucked into the robe.
    owl = [
        sphere(col, "owl_body", (0.22, -0.33, 3.16), (0.08, 0.07, 0.11)),
        sphere(col, "owl_head", (0.22, -0.34, 3.29), (0.065, 0.06, 0.055)),
        cone(col, "owl_ear_l", (0.26, -0.34, 3.32), 0.02, 0.0, 0.05, segments=6),
        cone(col, "owl_ear_r", (0.18, -0.34, 3.32), 0.02, 0.0, 0.05, segments=6),
    ]
    owl_obj = finish(owl, "Owl", scale=1.05)
    owl_obj.parent = body
    return body


def lion(col):
    """Columbia Lion: a lion with a heavy mane."""
    p = []
    p.append(sphere(col, "barrel", (0, 0.3, 1.75), (0.82, 1.6, 0.78)))
    p.append(sphere(col, "chest", (0, -0.9, 1.9), (0.92, 0.85, 0.95)))
    p.append(sphere(col, "haunch", (0, 1.4, 1.75), (0.78, 0.8, 0.82)))
    p.append(limb(col, "neck", (0, -1.2, 2.2), (0, -1.75, 2.65), 0.62, 0.52))
    mane = ico(col, "mane", (0, -1.72, 2.68), (1.12, 0.85, 1.15), subdivisions=3)
    roughen(mane, 0.12, seed=7)
    p.append(mane)
    p.append(sphere(col, "head", (0, -2.12, 2.75), (0.52, 0.58, 0.52)))
    p.append(box(col, "muzzle", (0, -2.62, 2.58), (0.3, 0.32, 0.22), bevel=0.1))
    p.append(sphere(col, "nose", (0, -2.92, 2.7), (0.12, 0.08, 0.08)))
    p.append(box(col, "jaw", (0, -2.55, 2.32), (0.26, 0.28, 0.1), bevel=0.06))
    p.append(cone(col, "ear_l", (0.36, -1.98, 3.15), 0.13, 0.04, 0.2, segments=10))
    p.append(cone(col, "ear_r", (-0.36, -1.98, 3.15), 0.13, 0.04, 0.2, segments=10))
    for x in (0.48, -0.48):
        p.append(limb(col, "foreleg", (x, -1.0, 1.7), (x, -1.1, 0.2), 0.38, 0.26))
        p.append(sphere(col, "forepaw", (x, -1.22, 0.15), (0.3, 0.38, 0.17)))
        p.append(limb(col, "thigh", (x, 1.45, 1.75), (x, 1.75, 0.95), 0.46, 0.3))
        p.append(limb(col, "hindleg", (x, 1.75, 0.95), (x, 1.55, 0.2), 0.3, 0.24))
        p.append(sphere(col, "hindpaw", (x, 1.45, 0.15), (0.28, 0.36, 0.16)))
    p.append(limb(col, "tail", (0, 2.15, 1.95), (0, 3.1, 1.0), 0.11, 0.07, segments=10))
    tuft = ico(col, "tuft", (0, 3.2, 0.9), (0.2, 0.2, 0.25))
    roughen(tuft, 0.04, seed=3)
    p.append(tuft)
    return finish(p, "columbia_lion", scale=0.9)


def golem(col):
    """Book Keeper: a plain stone golem of stacked blocks, with a shelf of stone books in its chest."""
    rng = random.Random(11)
    p = []

    def block(name, loc, half, bevel=0.06):
        rot = (rng.uniform(-3, 3), rng.uniform(-3, 3), rng.uniform(-4, 4))
        p.append(box(col, name, loc, half, rot_deg=rot, bevel=bevel))

    block("torso", (0, 0, 2.8), (0.95, 0.6, 0.8))
    block("pelvis", (0, 0, 1.72), (0.7, 0.48, 0.3))
    block("head", (0, -0.12, 3.88), (0.34, 0.32, 0.28))
    p.append(box(col, "brow", (0, -0.42, 3.98), (0.3, 0.06, 0.06), bevel=0.02))
    for side in (1, -1):
        block("shoulder", (side * 1.22, 0, 3.3), (0.42, 0.45, 0.38))
        block("upper_arm", (side * 1.32, 0, 2.5), (0.3, 0.3, 0.5))
        block("forearm", (side * 1.38, -0.12, 1.62), (0.37, 0.37, 0.45))
        block("fist", (side * 1.4, -0.16, 0.96), (0.42, 0.42, 0.36))
        block("thigh", (side * 0.48, 0, 1.15), (0.36, 0.36, 0.38))
        block("shin", (side * 0.5, 0, 0.52), (0.33, 0.33, 0.3))
        block("foot", (side * 0.5, -0.12, 0.12), (0.42, 0.55, 0.13))

    # The shelf: a frame on the chest with two rows of stone books.
    p.append(box(col, "shelf_frame", (0, -0.62, 2.8), (0.62, 0.06, 0.55), bevel=0.02))
    for row_z in (2.52, 2.98):
        p.append(box(col, "shelf_board", (0, -0.72, row_z - 0.17), (0.56, 0.08, 0.025), bevel=0.0))
        x = -0.5
        while x < 0.45:
            w = rng.uniform(0.035, 0.06)
            h = rng.uniform(0.11, 0.16)
            lean = rng.uniform(-6, 6)
            p.append(box(col, "book", (x + w, -0.74, row_z - 0.145 + h), (w, 0.07, h), rot_deg=(0, lean, 0), bevel=0.0))
            x += 2 * w + 0.01
    obj = finish(p, "book_keeper")
    roughen(obj, 0.01, seed=5)
    return obj


# ---------------------------------------------------------------- build and export

def export(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for child in obj.children:
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
        for obj in models:
            export(obj, os.path.join(export_dir, obj.name + ".fbx"))
    return models


if __name__ == "__main__":
    build()
