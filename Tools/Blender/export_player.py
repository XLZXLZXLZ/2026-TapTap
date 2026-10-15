"""Run with Blender --background --disable-autoexec --python Tools/Blender/export_player.py.

Exports the existing source files without saving or modifying either .blend file.
The two visible halves retain identical skeleton paths so one baked Walk clip can drive both.
"""

from pathlib import Path
import bpy
from mathutils import Matrix


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Art/3D Resources/Skeleton"
OUTPUT = ROOT / "Assets/Art/3D Resources/Player"
PARTS = {
    "down_rigged": ["RobotBody_UpperLower", "Leg_L", "Leg_R", "LowerEye_L", "LowerEye_R"],
    "up_rigged": ["RobotBody_UpperLower.001", "Arm_L", "Arm_R", "UpperEye_L", "UpperEye_R",
                  "ImportedLight_AntennaSocket", "ImportedLight_AntennaYellowBall", "ImportedLight_Cylinder.001"],
}


def select(objects):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def export(name, objects, animation=False):
    select(objects)
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT / (name + ".fbx")), use_selection=True,
        object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=True,
        axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
        add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=animation, bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0, path_mode="AUTO",
    )


OUTPUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "final.blend"), load_ui=False, use_scripts=False)
rig = bpy.data.objects["Robot_Controls"]
rig.animation_data_clear()
rig.data.pose_position = "REST"
for name, meshes in PARTS.items():
    objects = [bpy.data.objects[mesh] for mesh in meshes]
    for obj in objects:
        # Corrective smoothing is Blender-specific. Subdivision is baked into the rest mesh;
        # the exported armature and vertex weights remain live in Unity.
        for modifier in obj.modifiers:
            if modifier.type == "CORRECTIVE_SMOOTH":
                modifier.show_viewport = modifier.show_render = False
    export(name, [rig] + objects)

# A real rest-pose clip reliably restores every bone after walking or detaching.
rig.data.pose_position = "POSE"
for bone in rig.pose.bones:
    for constraint in bone.constraints:
        constraint.mute = True
    bone.matrix_basis = Matrix.Identity(4)
    for frame in (1, 2):
        bone.keyframe_insert("location", frame=frame)
        bone.keyframe_insert("rotation_quaternion" if bone.rotation_mode == "QUATERNION" else "rotation_euler", frame=frame)
        bone.keyframe_insert("scale", frame=frame)
rig.animation_data.action.name = "Idle"
bpy.context.scene.frame_start = 1
bpy.context.scene.frame_end = 2
bpy.context.scene.frame_set(1)
export("idle_rigged", [rig], animation=True)

bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "walk.blend"), load_ui=False, use_scripts=False)
rig = bpy.data.objects["Robot_Controls"]
rig.data.pose_position = "POSE"
if not rig.animation_data or not rig.animation_data.action:
    raise RuntimeError("walk.blend has no active walking action")
rig.animation_data.action.name = "Walk"
bpy.context.scene.frame_start = 1
bpy.context.scene.frame_end = 24
bpy.context.scene.frame_set(1)
export("walk_rigged", [rig], animation=True)
print("PLAYER EXPORT COMPLETE: up_rigged, down_rigged, idle_rigged, walk_rigged")
