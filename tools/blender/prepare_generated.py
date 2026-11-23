"""
Turns a generated Guardian model (Tripo image-to-3D, imported by MCP for Blender) into the game's Guardian FBX and
texture set. Run inside Blender with the generated object in the scene:

    prepare("GEN_columbia_lion", "columbia_lion", height=3.3, front_y=-2.5, facing="+Y")

It keeps the raw model (art_source/generated/<id>_raw.glb, not in git: too large), decimates a copy to the
triangle budget, turns it to face -Y (Blender front), scales it to the Guardian's height with
its feet on the floor and its nose at front_y (Unity imports it facing -Z; CombatSandbox turns it to the Guardian's +Z), writes the texture set Art/Materials/<SetName>/ (BaseColor, Normal,
and Roughness / Metallic / AO split from the ORM map) for SandboxArtMenu, and exports Art/Models/Guardians/<id>.fbx.
guardian_blockouts.py skips the ids listed in GENERATED so a rebuild does not overwrite these.
"""

import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")) if "__file__" in globals() else None
GAME = "game/Assets/_Project/Art"


def _set_name(guardian_id):
    return "".join(w.capitalize() for w in guardian_id.split("_"))  # columbia_lion -> ColumbiaLion


def _save_png(img, path, data=False):
    out = img.copy()
    out.filepath_raw = path
    out.file_format = "PNG"
    if data:
        out.colorspace_settings.name = "Non-Color"
    out.save()
    bpy.data.images.remove(out)


def _channel_png(src, channel, path):
    w, h = src.size
    px = np.empty(w * h * 4, dtype=np.float32)
    src.pixels.foreach_get(px)
    px = px.reshape(-1, 4)
    out = np.ones_like(px)
    out[:, 0] = out[:, 1] = out[:, 2] = px[:, channel]
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False, float_buffer=False, is_data=True)
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def prepare(obj_name, guardian_id, height, front_y, facing="+Y", target_tris=80000, repo=REPO):
    src = bpy.data.objects[obj_name]
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    # Keep the raw generation: it cannot be regenerated identically.
    raw_dir = os.path.join(repo, "art_source", "generated")
    os.makedirs(raw_dir, exist_ok=True)
    raw_path = os.path.join(raw_dir, f"{guardian_id}_raw.glb")
    if not os.path.exists(raw_path):
        bpy.ops.object.select_all(action="DESELECT")
        src.select_set(True)
        bpy.context.view_layer.objects.active = src
        bpy.ops.export_scene.gltf(filepath=raw_path, use_selection=True, export_format="GLB")

    # Work on a copy, decimated to the budget.
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = obj.data.name = guardian_id
    bpy.context.scene.collection.objects.link(obj)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > target_tris:
        mod = obj.modifiers.new("decimate", "DECIMATE")
        mod.ratio = target_tris / tris
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)

    # Face -Y, at the Guardian's height, feet on the floor, centred in X, nose at front_y.
    turn = {"+Y": math.pi, "-Y": 0.0, "+X": -math.pi / 2, "-X": math.pi / 2}[facing]
    obj.data.transform(Matrix.Rotation(turn, 4, "Z") @ obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    co = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lo, hi = co.min(axis=0), co.max(axis=0)
    s = height / (hi[2] - lo[2])
    offset = Vector((-(lo[0] + hi[0]) / 2 * s, front_y - lo[1] * s, -lo[2] * s))
    obj.data.transform(Matrix.Translation(offset) @ Matrix.Scale(s, 4))
    obj.data.materials[0].name = "Main"  # the sandbox gives "Main" slots the Guardian's material
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth()

    # The texture set for SandboxArtMenu.
    name = _set_name(guardian_id)
    tex_dir = os.path.join(repo, GAME, "Materials", name)
    os.makedirs(tex_dir, exist_ok=True)
    images = {n.image.name.split("_")[0]: n.image
              for n in obj.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
    _save_png(images["Color"], os.path.join(tex_dir, f"{name}_BaseColor.png"))
    _save_png(images["NormalGL"], os.path.join(tex_dir, f"{name}_Normal.png"), data=True)
    orm = images["ORM"]
    _channel_png(orm, 0, os.path.join(tex_dir, f"{name}_AO.png"))
    _channel_png(orm, 1, os.path.join(tex_dir, f"{name}_Roughness.png"))
    _channel_png(orm, 2, os.path.join(tex_dir, f"{name}_Metallic.png"))

    # The FBX, like guardian_blockouts.export.
    fbx = os.path.join(repo, GAME, "Models", "Guardians", f"{guardian_id}.fbx")
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", embed_textures=False,
    )
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    dims = [round(v, 2) for v in obj.dimensions]
    return {"fbx": fbx, "textures": tex_dir, "raw": raw_path, "tris": tris, "size": dims, "object": obj.name}
