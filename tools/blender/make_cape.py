"""
Builds the Columbia Blue Robe as an open cape for the player: the commencement gown in the design sheet
(art_source/generated/blue_robe_sheet.png, not in git) worn over the shoulders and left open at the front, so it
streams behind her. Run inside Blender with the rigged player (rig_guardians.py) in the scene:

    build(repo)   # mesh, UVs, rig and weights, texture set, FBX

The cape is a pleated sheet hung from a standing collar, around her back from shoulder to shoulder, flaring to
mid-calf; an inner surface 4 mm in carries the ivory lining. Its outer face is textured by projecting the sheet's
back view (collar, laurel yoke, pleats, hem trim and stars) onto it. It has a root bone, "Cape", at the nape and
three chains of four bones (Cape_R1-4, Cape_C1-4, Cape_L1-4) down her right side, the middle of her back and her left
side, which PlayerMotion swings as she runs and turns. Exported in the player's rest space with player.fbx's settings
(Art/Models/Artifacts/columbia_blue_robe.fbx); the sandbox reparents it to the Chest bone.
"""

import math
import os

import bpy
import bmesh
import numpy as np
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")) if "__file__" in globals() else None
GAME = "game/Assets/_Project/Art"
ID = "columbia_blue_robe"
SET = "ColumbiaBlueRobe"
CX = -0.03  # the player's centre line

# Profile down the cape: (z, half-width a, half-depth b, centre y, half-angle in degrees from her back,
# sheet row y and half-width in sheet pixels for the texture). An ellipse at each height: x = CX + a sin(t),
# y = centre + b cos(t), t in [-half-angle, half-angle], 0 straight behind her, positive toward her left (+x).
PROFILE = [
    (1.49, 0.078, 0.078, -0.060, 158, 125, 40),   # top of the standing collar
    (1.44, 0.092, 0.092, -0.060, 155, 148, 45),   # its foot
    (1.425, 0.190, 0.140, -0.070, 140, 165, 80),  # over the tops of the shoulders
    (1.37, 0.222, 0.162, -0.070, 122, 190, 95),
    (1.28, 0.245, 0.175, -0.060, 100, 230, 95),   # the yoke's point
    (1.10, 0.270, 0.195, -0.050, 88, 330, 95),
    (0.80, 0.300, 0.230, -0.040, 84, 560, 140),
    (0.32, 0.380, 0.320, -0.030, 82, 912, 205),   # the hem
]
SHEET_CENTRE_X = 830
SHEET_CROP = (620, 1040, 120, 930)  # x0, x1, y0, y1: the texture is this part of the sheet
LINING = (0.86, 0.82, 0.76)
ROWS, COLUMNS = 34, 41
CHAIN_Z = (1.34, 1.08, 0.82, 0.56, 0.30)

# The hanging sleeves: empty bell sleeves sewn on at the shoulders over the cape, hanging behind her arms (her arms
# are not in them). Centre line from the shoulder to the cuff for her left side (x offsets from CX; her right mirrors),
# the flattened cross-section's half-thickness (across her side) and half-width (front to back) at shoulder and cuff,
# how much lower the cuff hangs at the back, and the sheet's sleeve (x range at the shoulder and at the cuff, rows).
SLEEVE = dict(top=(0.195, 0.005, 1.415), bottom=(0.31, 0.10, 0.94), half_thick=(0.028, 0.07), half_width=(0.05, 0.17),
              slant=0.07, sheet_x_top=(705, 760), sheet_x_cuff=(625, 700), sheet_y=(215, 442))
SLEEVE_RINGS, SLEEVE_AROUND = 14, 24


def _profile(z):
    p = np.array(PROFILE, dtype=float)[::-1]  # ascending z for interp
    return [np.interp(z, p[:, 0], p[:, k]) for k in range(1, 7)]


def _point(z, t_norm, pleat=True):
    a, b, yc, half, _, _ = _profile(z)
    t = math.radians(half) * t_norm
    if pleat:  # pleats deepen toward the hem
        depth = 0.018 * max(0.0, min(1.0, (1.30 - z) / 0.98))
        s = 1.0 + depth / max(a, b) * math.sin(t_norm * 9 * math.pi)
        a, b = a * s, b * s
    return Vector((CX + a * math.sin(t), yc + b * math.cos(t), z))


def _uv(z, t_norm):
    _, _, _, _, sy, hw = _profile(z)
    x0, x1, y0, y1 = SHEET_CROP
    sx = SHEET_CENTRE_X + hw * t_norm
    return ((sx - x0) / (x1 - x0), 1.0 - (sy - y0) / (y1 - y0))


def _rows():
    top, bottom = PROFILE[0][0], PROFILE[-1][0]
    # Denser near the collar and shoulders.
    return [top - (top - bottom) * (i / (ROWS - 1)) ** 1.3 for i in range(ROWS)]


def mesh():
    old = bpy.data.objects.get(ID)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rows = _rows()
    ts = [-1 + 2 * j / (COLUMNS - 1) for j in range(COLUMNS)]
    outer = [[bm.verts.new(_point(z, t)) for t in ts] for z in rows]
    bm.verts.ensure_lookup_table()
    faces = []
    for i in range(ROWS - 1):
        for j in range(COLUMNS - 1):
            # Outward-facing winding (normals away from her).
            f = bm.faces.new((outer[i][j], outer[i][j + 1], outer[i + 1][j + 1], outer[i + 1][j]))
            for loop, (ii, jj) in zip(f.loops, ((i, j), (i, j + 1), (i + 1, j + 1), (i + 1, j))):
                loop[uv].uv = _uv(rows[ii], ts[jj])
            faces.append(f)
    bm.normal_update()
    if sum(f.normal.dot(f.calc_center_median() - Vector((CX, -0.05, f.calc_center_median().z))) for f in faces) < 0:
        for f in faces:
            f.normal_flip()
    bm.normal_update()
    # The lining: the same sheet 4 mm in, facing her, all its UVs on the lining patch in the texture's corner.
    inner = {v: bm.verts.new(v.co - v.normal * 0.004) for row in outer for v in row}
    for f in faces:
        g = bm.faces.new([inner[v] for v in reversed(f.verts)])
        for loop in g.loops:
            loop[uv].uv = (0.03, 0.97)
    part = bm.verts.layers.int.new("part")  # 0 the cape, 1 her right sleeve, 2 her left sleeve
    for v in bm.verts:
        v[part] = 0
    for side in (-1, 1):
        _sleeve(bm, uv, part, side)
    me = bpy.data.meshes.new(ID)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(ID, me)
    bpy.context.scene.collection.objects.link(obj)
    mat = bpy.data.materials.get(SET) or bpy.data.materials.new(SET)
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    return obj


def _sleeve_point(side, s, u):
    """A point on the sleeve: s from shoulder (0) to cuff (1), u around it (0 at its front, 0.5 at its back)."""
    p = SLEEVE
    top, bottom = Vector(p["top"]), Vector(p["bottom"])
    c = top.lerp(bottom, s)
    flare = s ** 1.4
    thick = p["half_thick"][0] + (p["half_thick"][1] - p["half_thick"][0]) * flare
    width = p["half_width"][0] + (p["half_width"][1] - p["half_width"][0]) * flare
    a = 2 * math.pi * u
    # Folds in the cloth, deeper toward the cuff.
    fold = 1.0 + 0.12 * flare * math.sin(a * 6)
    x = c.x + thick * fold * math.sin(a)
    y = c.y - width * fold * math.cos(a)  # u = 0 faces her front (-y)
    z = c.z - p["slant"] * s * (1 - math.cos(a)) / 2  # the back of the cuff hangs lower
    return Vector((CX + side * x, y, z))


def _sleeve(bm, uv, part, side):
    p = SLEEVE
    ss = [i / (SLEEVE_RINGS - 1) for i in range(SLEEVE_RINGS)]
    us = [j / SLEEVE_AROUND for j in range(SLEEVE_AROUND)]
    rings = [[bm.verts.new(_sleeve_point(side, s, u)) for u in us] for s in ss]
    tag = 1 if side < 0 else 2
    x0, x1, y0, y1 = SHEET_CROP

    def sheet_uv(s, u):
        lo = p["sheet_x_top"][0] + (p["sheet_x_cuff"][0] - p["sheet_x_top"][0]) * s
        hi = p["sheet_x_top"][1] + (p["sheet_x_cuff"][1] - p["sheet_x_top"][1]) * s
        sx = lo + (hi - lo) * (1 - abs(2 * u - 1))  # mirrored round the sleeve: no seam
        sy = p["sheet_y"][0] + (p["sheet_y"][1] - p["sheet_y"][0]) * s
        return ((sx - x0) / (x1 - x0), 1.0 - (sy - y0) / (y1 - y0))

    outer = []
    for i in range(SLEEVE_RINGS - 1):
        for j in range(SLEEVE_AROUND):
            k = (j + 1) % SLEEVE_AROUND
            quad = (rings[i][j], rings[i + 1][j], rings[i + 1][k], rings[i][k])
            if side < 0:
                quad = quad[::-1]
            f = bm.faces.new(quad)
            u1 = us[k] if k else 1.0
            coords = {(i, j): (ss[i], us[j]), (i + 1, j): (ss[i + 1], us[j]), (i + 1, k): (ss[i + 1], u1), (i, k): (ss[i], u1)}
            for loop in f.loops:
                ii, jj = next(key for key in ((i, j), (i + 1, j), (i + 1, k), (i, k)) if rings[key[0]][key[1]] is loop.vert)
                loop[uv].uv = sheet_uv(*coords[(ii, jj)])
            outer.append(f)
    bm.normal_update()
    # Outward normals: away from the sleeve's own centre line.
    def axis_point(f):
        m = f.calc_center_median()
        top = Vector((CX + side * p["top"][0], p["top"][1], p["top"][2]))
        bot = Vector((CX + side * p["bottom"][0], p["bottom"][1], p["bottom"][2]))
        d = bot - top
        t = max(0.0, min(1.0, (m - top).dot(d) / d.dot(d)))
        return m - (top + d * t)
    if sum(f.normal.dot(axis_point(f)) for f in outer) < 0:
        for f in outer:
            f.normal_flip()
    bm.normal_update()
    verts = [v for ring in rings for v in ring]
    inner = {v: bm.verts.new(v.co - v.normal * 0.004) for v in verts}
    for f in outer:
        g = bm.faces.new([inner[v] for v in reversed(f.verts)])
        for loop in g.loops:
            loop[uv].uv = (0.03, 0.97)
    for v in verts + list(inner.values()):
        v[part] = tag


def _chains():
    """(bone, parent, head, tail) for the root and the three chains."""
    bones = [("Cape", None, Vector((CX, -0.02, 1.45)), Vector((CX, 0.06, 1.36)))]
    for side, t in (("R", -0.6), ("C", 0.0), ("L", 0.6)):
        parent = "Cape"
        for k in range(4):
            name = f"Cape_{side}{k + 1}"
            bones.append((name, parent, _point(CHAIN_Z[k], t, pleat=False), _point(CHAIN_Z[k + 1], t, pleat=False)))
            parent = name
    # The sleeves: two bones each, shoulder to elbow height to cuff, on the root.
    for side, sign in (("R", -1), ("L", 1)):
        mid = _sleeve_point(sign, 0.5, 0.25).lerp(_sleeve_point(sign, 0.5, 0.75), 0.5)
        top = _sleeve_point(sign, 0.0, 0.25).lerp(_sleeve_point(sign, 0.0, 0.75), 0.5)
        cuff = _sleeve_point(sign, 1.0, 0.25).lerp(_sleeve_point(sign, 1.0, 0.75), 0.5)
        bones.append((f"Sleeve_{side}1", "Cape", top, mid))
        bones.append((f"Sleeve_{side}2", f"Sleeve_{side}1", mid, cuff))
    return bones


def _segment_distance(co, a, b):
    a, b = np.array(a), np.array(b)
    ab = b - a
    t = np.clip(((co - a) @ ab) / (ab @ ab), 0.0, 1.0)
    return np.linalg.norm(co - (a + t[:, None] * ab), axis=1)


def rig(obj):
    name = f"{ID}_rig"
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    arm = bpy.data.objects.new(name, bpy.data.armatures.new(name))
    bpy.context.scene.collection.objects.link(arm)
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    chains = _chains()
    for bone, parent, head, tail in chains:
        eb = arm.data.edit_bones.new(bone)
        eb.head, eb.tail = head, tail
        if parent:
            eb.parent = arm.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")

    me = obj.data
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [c[0] for c in chains]
    part = np.empty(len(co), dtype=np.int64)
    me.attributes["part"].data.foreach_get("value", part)
    w = np.zeros((len(co), len(names)))
    for j, (bone, _, head, tail) in enumerate(chains):
        # Each piece follows only its own bones: the cape its chains, each sleeve its two.
        own = {"Cape": part == 0, "Sleeve_R": part == 1, "Sleeve_L": part == 2}
        mask = next((m for prefix, m in own.items() if bone.startswith(prefix + "_")), part == 0)
        w[:, j] = np.where(mask, np.maximum(_segment_distance(co, head, tail), 0.02) ** -4.0, 0.0)
    # The collar and the cloth over the shoulders stay with the root; so do the sleeves' sewn-on tops.
    pinned = ((part == 0) & (co[:, 2] > 1.38)) | ((part > 0) & (co[:, 2] > SLEEVE["top"][2] - 0.03))
    w[pinned] = 0.0
    w[pinned, 0] = 1.0
    keep = np.argsort(-w, axis=1)[:, :3]
    top = np.take_along_axis(w, keep, axis=1)
    top /= top.sum(axis=1, keepdims=True)
    obj.vertex_groups.clear()
    groups = [obj.vertex_groups.new(name=n) for n in names]
    for i in range(len(co)):
        for k in range(3):
            if top[i, k] > 0.01:
                groups[keep[i, k]].add([i], float(top[i, k]), "REPLACE")
    for m in [m for m in obj.modifiers if m.type == "ARMATURE"]:
        obj.modifiers.remove(m)
    obj.modifiers.new("Armature", "ARMATURE").object = arm
    obj.parent = arm
    return arm


def texture(repo=REPO):
    """Art/Materials/ColumbiaBlueRobe: the sheet's back view as the base colour with an ivory lining patch, and a flat
    roughness (cloth, matte with a slight satin sheen)."""
    sheet = os.path.join(repo, "art_source", "generated", "blue_robe_sheet.png")
    img = bpy.data.images.load(sheet, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    px = px.reshape(h, w, 4)
    x0, x1, y0, y1 = SHEET_CROP
    crop = px[h - y1:h - y0, x0:x1].copy()  # Blender rows run bottom-up
    ch, cw = crop.shape[:2]
    crop[ch - 40:, :40, :3] = LINING  # top-left corner (background in the sheet): the lining
    out_dir = os.path.join(repo, GAME, "Materials", SET)
    os.makedirs(out_dir, exist_ok=True)

    def save(pixels, file, data=False):
        hh, ww = pixels.shape[:2]
        im = bpy.data.images.new(file, ww, hh, alpha=False, is_data=data)
        im.pixels.foreach_set(pixels.ravel())
        im.filepath_raw = os.path.join(out_dir, file)
        im.file_format = "PNG"
        im.save()
        bpy.data.images.remove(im)

    save(crop, f"{SET}_BaseColor.png")
    rough = np.ones_like(crop)
    rough[..., :3] = 0.72
    save(rough, f"{SET}_Roughness.png", data=True)
    # Preview material in Blender.
    mat = bpy.data.materials.get(SET) or bpy.data.materials.new(SET)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    tex = next((n for n in nodes if n.type == "TEX_IMAGE"), None) or nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(out_dir, f"{SET}_BaseColor.png"), check_existing=True)
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.72
    return out_dir


def export(obj, repo=REPO):
    arm = obj.parent
    bpy.ops.object.select_all(action="DESELECT")
    for o in (obj, arm):
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(repo, GAME, "Models", "Artifacts", f"{ID}.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=False, mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y",
        secondary_bone_axis="X", use_armature_deform_only=True, bake_anim=False, path_mode="STRIP",
        embed_textures=False,
    )
    return path


def build(repo=REPO):
    texture(repo)
    obj = mesh()
    rig(obj)
    return export(obj, repo)
