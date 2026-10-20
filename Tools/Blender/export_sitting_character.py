"""Export set.blend as a standalone, baked sitting decoration (sources stay untouched).

Blender --background --disable-autoexec --python Tools/Blender/export_sitting_character.py
"""
from pathlib import Path
import bpy

ROOT = Path(__file__).resolve().parents[2]
FOLDER = ROOT / "Assets/Art/3D Resources/Player"
bpy.ops.wm.open_mainfile(filepath=str(FOLDER / "New/set.blend"), load_ui=False, use_scripts=False)
scene = bpy.context.scene
rig = bpy.data.objects["Robot_Controls"]
names = ["RobotBody_UpperLower", "RobotBody_UpperLower.001", "Arm_L", "Arm_R", "Leg_L", "Leg_R",
         "ImportedLight_AntennaSocket", "ImportedLight_AntennaYellowBall", "ImportedLight_Cylinder.001"]
meshes = [bpy.data.objects[name] for name in names]
start, end = scene.frame_start, scene.frame_end
poses = []
for frame in range(start, end + 1):
    scene.frame_set(frame)
    evaluated = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
    poses.append({bone.name: bone.matrix.copy() for bone in evaluated.pose.bones})
rig.animation_data_clear()
for bone in rig.pose.bones:
    for constraint in bone.constraints:
        constraint.mute = True
    bone.rotation_mode = "QUATERNION"
for frame, pose in enumerate(poses, start):
    scene.frame_set(frame)
    for bone in rig.pose.bones:
        options = {} if bone.parent is None else dict(
            parent_matrix=pose[bone.parent.name], parent_matrix_local=bone.parent.bone.matrix_local)
        bone.matrix_basis = bone.bone.convert_local_to_pose(
            pose[bone.name], bone.bone.matrix_local, invert=True, **options)
        for channel in ("location", "rotation_quaternion", "scale"):
            bone.keyframe_insert(channel, frame=frame)
rig.animation_data.action.name = "SittingIdle"
for mesh in meshes:
    for modifier in mesh.modifiers:
        if modifier.type == "CORRECTIVE_SMOOTH":
            modifier.show_viewport = modifier.show_render = False
scene.frame_set(start)
bpy.ops.object.select_all(action="DESELECT")
for obj in [rig] + meshes:
    obj.hide_set(False)
    obj.hide_viewport = False
    obj.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=str(FOLDER / "set_rigged.fbx"), use_selection=True,
    object_types={"ARMATURE", "MESH"}, use_mesh_modifiers=True,
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    add_leaf_bones=False, use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode="AUTO")
print("SITTING CHARACTER EXPORTED:", start, end, "at", scene.render.fps, "fps")
