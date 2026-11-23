"""
Blockout models for the three prototype Guardians (GDD 9, visual direction): stand-ins until the paid models arrive.

Run inside Blender (Text Editor → Run Script, or through MCP for Blender). It rebuilds the "Guardians" collection and,
when EXPORT_DIR is set, writes one FBX per Guardian for Unity:

    alma_mater.fbx     lich in a flowing robe, staff and book; the hidden owl is a child object named "Owl"
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


def torus(col, name, loc, major, minor, tilt_deg=0.0):
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
    bmesh.ops.rotate(bm, matrix=Euler((math.radians(tilt_deg), 0, 0)).to_matrix(), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(loc), verts=bm.verts)
    return _mesh_object(name, bm, col)


def roughen(obj, amount, seed):
    """Push vertices in and out a little, so a shape reads as hewn or cast rather than machined."""
    rng = random.Random(seed)
    for v in obj.data.vertices:
        v.co += v.normal * rng.uniform(-amount, amount)


def finish(parts, name, scale=1.0):
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
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
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
    """Alma Mater: a lich in a flowing robe, a staff in its right hand and a book in its left."""
    p = []
    robe = cone(col, "robe", (0, 0, 0), 1.25, 0.5, 2.7, segments=48)
    # Folds: ripple the lower robe in and out around its circumference.
    for v in robe.data.vertices:
        drop = max(0.0, 1.0 - v.co.z / 2.7)
        angle = math.atan2(v.co.y, v.co.x)
        v.co.x += math.cos(angle) * math.sin(angle * 9) * 0.09 * drop
        v.co.y += math.sin(angle) * math.sin(angle * 9) * 0.09 * drop
    p.append(robe)
    p.append(sphere(col, "torso", (0, 0.02, 2.95), (0.6, 0.42, 0.72)))
    p.append(sphere(col, "mantle", (0, 0.02, 3.35), (0.88, 0.55, 0.32)))
    p.append(sphere(col, "hood", (0, 0.12, 3.92), (0.5, 0.5, 0.56)))
    # The skull juts out of the hood, so the silhouette reads as a lich, not a monk.
    p.append(sphere(col, "skull", (0, -0.3, 3.8), (0.24, 0.22, 0.28)))
    p.append(sphere(col, "eye_l", (0.09, -0.5, 3.86), (0.06, 0.03, 0.05)))
    p.append(sphere(col, "eye_r", (-0.09, -0.5, 3.86), (0.06, 0.03, 0.05)))
    p.append(box(col, "jaw", (0, -0.42, 3.6), (0.14, 0.1, 0.06), bevel=0.03))
    p.append(torus(col, "wreath", (0, 0.04, 4.22), 0.36, 0.055, tilt_deg=-8))

    # Right arm (-X when facing -Y) holds the staff upright.
    p.append(limb(col, "arm_r", (-0.72, 0.0, 3.3), (-0.98, -0.42, 2.55), 0.26, 0.2))
    p.append(sphere(col, "hand_r", (-1.0, -0.5, 2.5), (0.15, 0.15, 0.16)))
    p.append(limb(col, "staff", (-1.0, -0.5, 0.0), (-1.0, -0.5, 4.55), 0.06, 0.06, segments=10))
    p.append(ico(col, "lamp", (-1.0, -0.5, 4.72), (0.2, 0.2, 0.24)))
    p.append(torus(col, "lamp_ring", (-1.0, -0.5, 4.58), 0.18, 0.035))

    # Left arm cradles the open book.
    p.append(limb(col, "arm_l", (0.72, 0.0, 3.3), (0.55, -0.5, 2.62), 0.26, 0.2))
    p.append(sphere(col, "hand_l", (0.5, -0.58, 2.58), (0.15, 0.15, 0.15)))
    p.append(box(col, "book_l", (0.32, -0.7, 2.6), (0.2, 0.15, 0.03), rot_deg=(-25, 8, 12), bevel=0.01))
    p.append(box(col, "book_r", (0.66, -0.7, 2.62), (0.2, 0.15, 0.03), rot_deg=(-25, -8, -12), bevel=0.01))
    body = finish(p, "alma_mater")

    # The owl peeks out of a fold low on the robe's front-left (GDD 6.7). Separate so Unity can show and hide it.
    owl = [
        sphere(col, "owl_body", (0.46, -0.72, 1.66), (0.15, 0.13, 0.2)),
        sphere(col, "owl_head", (0.46, -0.74, 1.9), (0.12, 0.11, 0.1)),
        cone(col, "owl_ear_l", (0.53, -0.74, 1.96), 0.035, 0.0, 0.08, segments=8),
        cone(col, "owl_ear_r", (0.39, -0.74, 1.96), 0.035, 0.0, 0.08, segments=8),
    ]
    owl_obj = finish(owl, "Owl")
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
        obj.location.x = (i - 1) * 5.0
    if export_dir:
        os.makedirs(export_dir, exist_ok=True)
        for obj in models:
            export(obj, os.path.join(export_dir, obj.name + ".fbx"))
    return models


if __name__ == "__main__":
    build()
