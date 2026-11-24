"""
Turns a generated held artifact (Tripo image-to-3D, imported by MCP for Blender) into the game's artifact FBX and
texture set, posed in the player's hand. Run inside Blender with the generated object and the rigged player
(rig_guardians.py: rig("player"), so the scene has "player" and "player_rig") in the scene:

    # Lion's Fang (art_source/generated/lions_fang_sheet.png): measured on the generation, see orient().
    obj = decimate("GEN_lions_fang", "columbia_lions_fang", 16000)
    orient(obj, grip=(-0.236, 0.036, 0.223), to_tip=(0.709, -0.146, -0.689), edge=(0.209, -0.89, 0.404), length=0.40)
    hold(obj, "player_rig", "Hand_R", **HOLDS["columbia_lions_fang"])
    export(obj, repo)  # Art/Models/Artifacts/columbia_lions_fang.fbx, Art/Materials/ColumbiaLionsFang/

The FBX keeps the held pose as the root's transform, in the same space and export settings as player.fbx. The sandbox
instantiates it under the player model while the model is in its rest pose, then reparents it to the bone keeping its
world pose, so it lands in the hand without any offsets in code (CombatSandbox.AttachArtifacts).
"""

import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")) if "__file__" in globals() else None
GAME = "game/Assets/_Project/Art"

# Where each held artifact sits in the player's rest pose (Blender world space, metres): the grip centre and the
# directions its blade (pommel to tip) and its edge (the side the blade curves to) point.
HOLDS = {
    # Across the right palm, in the fist PlayerMotion closes (Fingers_R curled 80°): blade forward and 25° down,
    # curving down, the lion pommel behind the fist. (Along the fingers, the open hand did not read as holding it.)
    "columbia_lions_fang": dict(grip=(-0.272, -0.12, 0.81), to_tip=(0.0, -0.9, -0.43), edge=(0.0, 0.43, -0.9)),
}


def _set_name(artifact_id):
    return "".join(w.capitalize() for w in artifact_id.split("_"))  # columbia_lions_fang -> ColumbiaLionsFang


def _frame(to_tip, edge):
    """Rotation whose columns are the artifact's flat normal, edge, and blade directions mapped from (-X, -Y, -Z)."""
    b = Vector(to_tip).normalized()
    e = Vector(edge)
    e = (e - b * e.dot(b)).normalized()
    f = b.cross(e)
    # Local -Z is the blade, local -Y the edge, local -X their cross product.
    return Matrix((-f, -e, -b)).transposed()


def decimate(obj_name, artifact_id, target_tris, repo=REPO):
    """A decimated copy named after the artifact; keeps the raw generation in art_source/generated (not in git)."""
    src = bpy.data.objects[obj_name]
    raw_path = os.path.join(repo, "art_source", "generated", f"{artifact_id}_raw.glb")
    if not os.path.exists(raw_path):
        bpy.ops.object.select_all(action="DESELECT")
        src.select_set(True)
        bpy.context.view_layer.objects.active = src
        bpy.ops.export_scene.gltf(filepath=raw_path, use_selection=True, export_format="GLB")
    old = bpy.data.objects.get(artifact_id)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = obj.data.name = artifact_id
    bpy.context.scene.collection.objects.link(obj)
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris > target_tris:
        mod = obj.modifiers.new("decimate", "DECIMATE")
        mod.ratio = target_tris / tris
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.materials[0].name = "Main"
    return obj


def orient(obj, grip, to_tip, edge, length):
    """
    Moves the mesh into the artifact's own frame: origin at the grip centre, blade along -Z, edge toward -Y, scaled
    to its length (pommel to tip). grip, to_tip and edge are measured on the generated mesh in its own coordinates.
    """
    rot = _frame(to_tip, edge).to_4x4()
    co = np.empty(len(obj.data.vertices) * 3)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    span = (co - np.array(grip)) @ np.array(Vector(to_tip).normalized())
    s = length / (span.max() - span.min())
    obj.data.transform(Matrix.Scale(s, 4) @ rot.inverted() @ Matrix.Translation(-Vector(grip)))
    obj.matrix_world = Matrix.Identity(4)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth()


def hold(obj, rig_name, bone, grip, to_tip, edge):
    """Places the artifact at its held pose and parents it to the bone (for previewing poses in Blender)."""
    rig = bpy.data.objects[rig_name]
    world = Matrix.Translation(Vector(grip)) @ _frame(to_tip, edge).to_4x4()
    obj.parent = rig
    obj.parent_type = "BONE"
    obj.parent_bone = bone
    bpy.context.view_layer.update()
    obj.matrix_world = world


def export(obj, repo=REPO):
    """The texture set for SandboxArtMenu and the FBX, at the held pose in the player's rest space."""
    artifact_id = obj.name
    name = _set_name(artifact_id)
    tex_dir = os.path.join(repo, GAME, "Materials", name)
    os.makedirs(tex_dir, exist_ok=True)
    images = {n.image.name.split("_")[0]: n.image
              for n in obj.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image}

    def save(img, path, data=False):
        out = img.copy()
        out.filepath_raw = path
        out.file_format = "PNG"
        if data:
            out.colorspace_settings.name = "Non-Color"
        out.save()
        bpy.data.images.remove(out)

    def channel(src, c, path):
        w, h = src.size
        px = np.empty(w * h * 4, dtype=np.float32)
        src.pixels.foreach_get(px)
        px = px.reshape(-1, 4)
        out = np.ones_like(px)
        out[:, 0] = out[:, 1] = out[:, 2] = px[:, c]
        img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False, is_data=True)
        img.pixels.foreach_set(out.ravel())
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
        bpy.data.images.remove(img)

    save(images["Color"], os.path.join(tex_dir, f"{name}_BaseColor.png"))
    save(images["NormalGL"], os.path.join(tex_dir, f"{name}_Normal.png"), data=True)
    channel(images["ORM"], 0, os.path.join(tex_dir, f"{name}_AO.png"))
    channel(images["ORM"], 1, os.path.join(tex_dir, f"{name}_Roughness.png"))
    channel(images["ORM"], 2, os.path.join(tex_dir, f"{name}_Metallic.png"))

    # Export unparented at its world pose, with player.fbx's settings, so both import into the same space.
    parent, parent_type, parent_bone, world = obj.parent, obj.parent_type, obj.parent_bone, obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
    bpy.ops.object.select_all(action="DESELECT")
    obj.hide_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    fbx = os.path.join(repo, GAME, "Models", "Artifacts", f"{artifact_id}.fbx")
    os.makedirs(os.path.dirname(fbx), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", embed_textures=False,
    )
    if parent:
        obj.parent, obj.parent_type, obj.parent_bone = parent, parent_type, parent_bone
        bpy.context.view_layer.update()
        obj.matrix_world = world
    return {"fbx": fbx, "textures": tex_dir, "tris": sum(len(p.vertices) - 2 for p in obj.data.polygons)}
