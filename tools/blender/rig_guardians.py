"""
Rigs the generated Guardian models (see prepare_generated.py) and exports them as skinned FBX for Unity.

Each Guardian gets an armature built from the joint positions below (metres, Blender space: the model faces -Y, so
the Guardian's left is +X; suffix _L / _R is the Guardian's own side). Weights come from the distance to each bone
segment (w = 1 / d^sharpness, the nearest three bones kept), with per-bone regions so a leg never takes the body and
the lich's sceptre rides on her right hand. The golem uses a sharp falloff so its stone plates stay rigid like a
machine's; the lion and the lich bend smoothly. PC/Combat/GuardianMotion.cs moves the bones by name.

    rig("alma_mater"); rig("columbia_lion"); rig("book_keeper")   # in Blender, after prepare()
    export_all(repo)                                               # writes Art/Models/Guardians/<id>.fbx
"""

import os

import bpy
import numpy as np
from mathutils import Vector

GAME = "game/Assets/_Project/Art/Models/Guardians"


def _mirror(bones):
    """Adds the _L twin (x mirrored) of every _R bone."""
    out = []
    for b in bones:
        out.append(b)
        name, parent, head, tail, region = b
        if name.endswith("_R"):
            twin_parent = parent[:-2] + "_L" if parent and parent.endswith("_R") else parent
            m = lambda p: (-p[0], p[1], p[2])
            twin_region = dict(region)
            if "side" in twin_region:
                twin_region["side"] = -twin_region["side"]
            out.append((name[:-2] + "_L", twin_parent, m(head), m(tail), twin_region))
    return out


def _chain(prefix, parent, points, count, region):
    """Splits a polyline into `count` equal bones named prefix1..prefixN."""
    pts = np.array(points, dtype=float)
    seg = np.linalg.norm(np.diff(pts, axis=0), axis=1)
    cum = np.concatenate([[0.0], np.cumsum(seg)])
    def at(dist):
        i = min(np.searchsorted(cum, dist, side="right") - 1, len(seg) - 1)
        t = (dist - cum[i]) / seg[i]
        return tuple(float(v) for v in pts[i] + t * (pts[i + 1] - pts[i]))
    ends = [at(cum[-1] * k / count) for k in range(count + 1)]
    return [(f"{prefix}{k + 1}", parent if k == 0 else f"{prefix}{k}", ends[k], ends[k + 1], dict(region))
            for k in range(count)]


# (bone, parent, head, tail, region). Region keys: side (+1 left / -1 right, with |x| beyond "min_x" measured from
# "cx"), zmin/zmax, ymin/ymax, radius (distance from the bone): the vertices the bone may move.
RIGS = {
    # The lich: a hovering scholar. The right hand holds the sceptre; the left hand casts.
    "alma_mater": {
        "sharpness": 4.0,
        "bones": [
            ("Hips", None, (0.08, -0.05, 2.0), (0.08, -0.05, 2.45), {}),
            ("Spine", "Hips", (0.08, -0.05, 2.45), (0.08, -0.06, 2.75), {}),
            ("Spine2", "Spine", (0.08, -0.06, 2.75), (0.08, -0.08, 3.05), {}),
            ("Chest", "Spine2", (0.08, -0.08, 3.05), (0.1, -0.08, 3.35), {}),
            ("Neck", "Chest", (0.1, -0.08, 3.35), (0.11, -0.06, 3.5), {"zmin": 3.1}),
            ("Head", "Neck", (0.11, -0.06, 3.5), (0.11, -0.06, 4.05), {"zmin": 3.3}),
            # Collarbones carry the shoulders; each moves only the mantle near it.
            ("Shoulder_R", "Chest", (0.04, -0.06, 3.25), (-0.22, -0.05, 3.15), {"side": -1, "cx": 0.07, "min_x": 0.05, "zmin": 2.8, "radius": 0.25}),
            ("Shoulder_L", "Chest", (0.12, -0.06, 3.25), (0.45, -0.05, 3.15), {"side": 1, "cx": 0.07, "min_x": 0.1, "zmin": 2.8, "radius": 0.25}),
            # The arms are not mirror images: the right is raised on the sceptre, the left hangs. Each moves only
            # the cloth close to it (radius), so the mantle does not lift like a board.
            ("UpperArm_R", "Shoulder_R", (-0.22, -0.05, 3.15), (-0.5, -0.4, 2.72), {"side": -1, "cx": 0.07, "min_x": 0.2, "zmin": 2.4, "radius": 0.3}),
            ("LowerArm_R", "UpperArm_R", (-0.5, -0.4, 2.72), (-0.66, -0.69, 3.0), {"side": -1, "cx": 0.07, "min_x": 0.4, "zmin": 2.5, "radius": 0.2}),
            ("Hand_R", "LowerArm_R", (-0.66, -0.69, 3.0), (-0.68, -0.72, 3.15), {"side": -1, "cx": 0.07, "min_x": 0.5, "zmin": 2.8, "radius": 0.2}),
            ("UpperArm_L", "Shoulder_L", (0.45, -0.05, 3.15), (0.65, -0.15, 2.4), {"side": 1, "cx": 0.07, "min_x": 0.25, "zmin": 2.2, "radius": 0.25}),
            ("LowerArm_L", "UpperArm_L", (0.65, -0.15, 2.4), (0.76, -0.4, 1.98), {"side": 1, "cx": 0.07, "min_x": 0.45, "zmin": 1.6, "zmax": 2.7, "radius": 0.17}),
            ("Hand_L", "LowerArm_L", (0.76, -0.4, 1.98), (0.78, -0.45, 1.72), {"side": 1, "cx": 0.07, "min_x": 0.55, "zmin": 1.5, "zmax": 2.1, "radius": 0.17}),
        ] + [
            # No legs to walk on: the robe below the hips is a three-bone pendulum that trails as she floats.
            (name, parent, head, tail, {"zmax": head[2] + 0.2})
            for name, parent, head, tail, _ in _chain("Robe", "Hips", [(0.07, -0.08, 2.0), (0.07, -0.2, 0.0)], 3, {})
        ],
        # The sceptre (a vertical rod at x, y, radius) goes wholly with the right hand; its foot, fused to the hem,
        # stays with the robe.
        "rigid": [("Hand_R", (-0.64, -0.69), 0.1, 0.15, 4.1)],
    },
    # The lion. Lateral-walk legs; the +X hind leg is forward in the rest pose, the -X one back.
    "columbia_lion": {
        "sharpness": 5.0,
        "bones": [
            ("Hips", None, (0.0, 1.7, 2.1), (0.0, 1.2, 2.05), {}),
            ("Spine", "Hips", (0.0, 1.2, 2.05), (0.0, 0.55, 2.0), {}),
            ("Spine2", "Spine", (0.0, 0.55, 2.0), (0.0, -0.1, 1.95), {}),
            ("Chest", "Spine2", (0.0, -0.1, 1.95), (0.0, -0.9, 1.95), {}),
            ("Neck", "Chest", (0.0, -0.9, 1.95), (0.0, -1.3, 2.2), {}),
            ("Neck2", "Neck", (0.0, -1.3, 2.2), (0.0, -1.7, 2.45), {}),
            ("Head", "Neck2", (0.0, -1.75, 2.55), (0.0, -2.5, 2.55), {"ymax": -1.2}),
            ("Jaw", "Head", (0.0, -1.95, 2.4), (0.0, -2.4, 2.25), {"ymax": -1.9, "zmax": 2.47, "zmin": 1.9}),
        ] + _chain("Tail", "Hips", [(0.0, 1.75, 2.15), (0.0, 2.2, 1.8), (0.0, 2.55, 1.35), (0.0, 2.8, 0.85), (0.0, 2.95, 0.25)],
                   6, {"ymin": 1.6}) + _mirror([
            # Shoulder blades slide over the chest, so the front legs swing from high on the body.
            ("Scapula_R", "Chest", (-0.25, -0.45, 2.25), (-0.45, -0.8, 1.85), {"side": -1, "min_x": 0.12, "zmin": 1.4, "ymax": -0.2, "radius": 0.5}),
            ("FrontUpper_R", "Scapula_R", (-0.45, -0.8, 1.85), (-0.47, -0.88, 1.05), {"side": -1, "min_x": 0.12, "zmax": 1.9, "ymax": -0.2}),
            ("FrontLower_R", "FrontUpper_R", (-0.47, -0.88, 1.05), (-0.5, -1.0, 0.35), {"side": -1, "min_x": 0.12, "zmax": 1.3, "ymax": -0.3}),
            ("FrontPaw_R", "FrontLower_R", (-0.5, -1.0, 0.35), (-0.5, -1.12, 0.1), {"side": -1, "min_x": 0.12, "zmax": 0.5, "ymax": -0.4}),
            ("FrontToe_R", "FrontPaw_R", (-0.5, -1.12, 0.1), (-0.5, -1.45, 0.05), {"side": -1, "min_x": 0.12, "zmax": 0.25, "ymax": -0.9}),
        ]) + [
            ("RearUpper_L", "Hips", (0.45, 1.25, 1.95), (0.48, 0.9, 1.15), {"side": 1, "min_x": 0.12, "zmax": 2.0, "ymin": 0.4, "ymax": 2.5}),
            ("RearLower_L", "RearUpper_L", (0.48, 0.9, 1.15), (0.5, 1.35, 0.55), {"side": 1, "min_x": 0.12, "zmax": 1.3, "ymin": 0.4, "ymax": 2.5}),
            ("RearFoot_L", "RearLower_L", (0.5, 1.35, 0.55), (0.5, 1.12, 0.12), {"side": 1, "min_x": 0.12, "zmax": 0.6, "ymin": 0.4, "ymax": 2.5}),
            ("RearToe_L", "RearFoot_L", (0.5, 1.12, 0.12), (0.5, 0.7, 0.05), {"side": 1, "min_x": 0.12, "zmax": 0.25, "ymin": 0.4, "ymax": 1.2}),
            ("RearUpper_R", "Hips", (-0.45, 1.3, 1.95), (-0.48, 1.55, 1.15), {"side": -1, "min_x": 0.12, "zmax": 2.0, "ymin": 0.6, "ymax": 2.5}),
            ("RearLower_R", "RearUpper_R", (-0.48, 1.55, 1.15), (-0.5, 2.15, 0.6), {"side": -1, "min_x": 0.12, "zmax": 1.3, "ymin": 0.6, "ymax": 2.5}),
            ("RearFoot_R", "RearLower_R", (-0.5, 2.15, 0.6), (-0.5, 2.02, 0.12), {"side": -1, "min_x": 0.12, "zmax": 0.65, "ymin": 0.6, "ymax": 2.5}),
            ("RearToe_R", "RearFoot_R", (-0.5, 2.02, 0.12), (-0.5, 1.75, 0.05), {"side": -1, "min_x": 0.12, "zmax": 0.25, "ymin": 0.6, "ymax": 2.1}),
        ],
    },
    # The golem: a stone machine. Sharp weights keep each plate on one bone.
    "book_keeper": {
        "sharpness": 10.0,
        "bones": [
            ("Hips", None, (0.0, 0.1, 1.45), (0.0, 0.1, 1.95), {}),
            ("Spine", "Hips", (0.0, 0.1, 1.95), (0.0, 0.1, 2.6), {}),
            ("Chest", "Spine", (0.0, 0.1, 2.6), (0.0, 0.1, 3.5), {}),
            ("Neck", "Chest", (0.0, -0.05, 3.5), (0.0, -0.1, 3.7), {"zmin": 3.35, "ymax": 0.4}),
            ("Head", "Neck", (0.0, -0.1, 3.7), (0.0, -0.1, 4.2), {"zmin": 3.55, "ymax": 0.4}),
        ] + _mirror([
            ("Shoulder_R", "Chest", (-0.45, 0.1, 3.35), (-1.05, 0.1, 3.3), {"side": -1, "min_x": 0.6, "zmin": 2.8}),
            ("UpperArm_R", "Shoulder_R", (-1.05, 0.1, 3.3), (-1.67, 0.0, 2.39), {"side": -1, "min_x": 0.9}),
            ("LowerArm_R", "UpperArm_R", (-1.67, 0.0, 2.39), (-1.78, -0.39, 1.75), {"side": -1, "min_x": 1.2, "zmax": 2.7}),
            ("Hand_R", "LowerArm_R", (-1.78, -0.39, 1.75), (-1.75, -0.46, 1.5), {"side": -1, "min_x": 1.2, "zmax": 1.9}),
            ("Fingers_R", "Hand_R", (-1.75, -0.46, 1.5), (-1.7, -0.56, 1.2), {"side": -1, "min_x": 1.2, "zmax": 1.6}),
            ("UpperLeg_R", "Hips", (-0.44, 0.1, 1.56), (-0.72, -0.1, 1.05), {"side": -1, "min_x": 0.1, "zmax": 1.6}),
            ("LowerLeg_R", "UpperLeg_R", (-0.72, -0.1, 1.05), (-0.8, -0.17, 0.38), {"side": -1, "min_x": 0.1, "zmax": 1.25}),
            ("Foot_R", "LowerLeg_R", (-0.8, -0.17, 0.38), (-0.82, -0.4, 0.18), {"side": -1, "min_x": 0.1, "zmax": 0.45}),
            ("Toe_R", "Foot_R", (-0.82, -0.4, 0.18), (-0.83, -0.75, 0.1), {"side": -1, "min_x": 0.1, "zmax": 0.3, "ymax": -0.3}),
        ]),
    },
}


def _allowed(co, region):
    ok = np.ones(len(co), dtype=bool)
    if "side" in region:
        ok &= (co[:, 0] - region.get("cx", 0.0)) * region["side"] > region.get("min_x", 0.0)
    for key, axis, sign in (("zmin", 2, 1), ("zmax", 2, -1), ("ymin", 1, 1), ("ymax", 1, -1)):
        if key in region:
            ok &= (co[:, axis] - region[key]) * sign >= 0
    return ok


def _segment_distance(co, a, b):
    a, b = np.array(a), np.array(b)
    ab = b - a
    t = np.clip(((co - a) @ ab) / (ab @ ab), 0.0, 1.0)
    return np.linalg.norm(co - (a + t[:, None] * ab), axis=1)


def rig(guardian_id):
    spec = RIGS[guardian_id]
    mesh = bpy.data.objects[guardian_id]
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    # The armature.
    name = f"{guardian_id}_rig"
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    arm = bpy.data.objects.new(name, bpy.data.armatures.new(name))
    bpy.context.scene.collection.objects.link(arm)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    edit = arm.data.edit_bones
    for bone, parent, head, tail, _ in spec["bones"]:
        eb = edit.new(bone)
        eb.head, eb.tail = Vector(head), Vector(tail)
        if parent:
            eb.parent = edit[parent]
    bpy.ops.object.mode_set(mode="OBJECT")

    # Weights: nearest bones by distance, within each bone's region.
    me = mesh.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [b[0] for b in spec["bones"]]
    w = np.zeros((len(co), len(names)))
    for j, (bone, _, head, tail, region) in enumerate(spec["bones"]):
        d = np.maximum(_segment_distance(co, head, tail), 0.02)
        ok = _allowed(co, region) & (d <= region.get("radius", np.inf))
        w[:, j] = np.where(ok, d ** -spec["sharpness"], 0.0)
    for bone, (x, y), radius, z0, z1 in spec.get("rigid", []):
        rod = (np.hypot(co[:, 0] - x, co[:, 1] - y) < radius) & (co[:, 2] >= z0) & (co[:, 2] <= z1)
        w[rod] = 0.0
        w[rod, names.index(bone)] = 1.0
    # Keep the three strongest, normalised.
    keep = np.argsort(-w, axis=1)[:, :3]
    top = np.take_along_axis(w, keep, axis=1)
    top = top / np.maximum(top.sum(axis=1, keepdims=True), 1e-12)

    mesh.vertex_groups.clear()
    groups = [mesh.vertex_groups.new(name=n) for n in names]
    for i in range(len(co)):
        for k in range(3):
            if top[i, k] > 0.01:
                groups[keep[i, k]].add([i], float(top[i, k]), "REPLACE")

    for m in [m for m in mesh.modifiers if m.type == "ARMATURE"]:
        mesh.modifiers.remove(m)
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
    return arm


def export(guardian_id, repo):
    mesh = bpy.data.objects[guardian_id]
    arm = mesh.parent
    bpy.ops.object.select_all(action="DESELECT")
    for o in (mesh, arm):
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(repo, GAME, f"{guardian_id}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y",
        secondary_bone_axis="X", use_armature_deform_only=True, bake_anim=False, path_mode="STRIP",
        embed_textures=False,
    )
    return path


def export_all(repo):
    return [export(g, repo) for g in RIGS]
