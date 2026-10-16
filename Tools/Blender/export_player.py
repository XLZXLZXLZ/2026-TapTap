"""Run with Blender --background --disable-autoexec --python Tools/Blender/export_player.py.

Exports the new body meshes and bakes the eye drivers without modifying the sources.
Idle comes from stand.blend; the existing Walk and animation binding paths are retained.
"""

from pathlib import Path
import bpy
import sys


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/Art/3D Resources/Player/New"
OUTPUT = ROOT / "Assets/Art/3D Resources/Player"
PARTS = {
    "down_rigged": ["RobotBody_UpperLower", "Leg_L", "Leg_R"],
    "up_rigged": ["RobotBody_UpperLower.001", "Arm_L", "Arm_R",
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


def export_idle():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "stand.blend"), load_ui=False, use_scripts=False)
    scene = bpy.context.scene
    rig = bpy.data.objects["Robot_Controls"]
    start, end = scene.frame_start, scene.frame_end
    poses = []
    for frame in range(start, end + 1):
        scene.frame_set(frame)
        evaluated = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
        poses.append({bone.name: bone.matrix.copy() for bone in evaluated.pose.bones})
    # Preserve the evaluated IK pose while adapting foot-control paths to the
    # runtime skeleton. Changing parents before sampling would alter the motion.
    rig.animation_data_clear()
    for bone in rig.pose.bones:
        for constraint in bone.constraints:
            constraint.mute = True
        bone.rotation_mode = "QUATERNION"
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name in ("CTRL_Foot_L", "CTRL_Foot_R"):
        bone = rig.data.edit_bones[name]
        matrix = bone.matrix.copy()
        bone.parent = rig.data.edit_bones["Lower"]
        bone.matrix = matrix
    bpy.ops.object.mode_set(mode="OBJECT")
    for frame, pose in enumerate(poses, start):
        scene.frame_set(frame)
        for bone in rig.pose.bones:
            options = {} if bone.parent is None else dict(
                parent_matrix=pose[bone.parent.name], parent_matrix_local=bone.parent.bone.matrix_local)
            bone.matrix_basis = bone.bone.convert_local_to_pose(
                pose[bone.name], bone.bone.matrix_local, invert=True, **options)
            for channel in ("location", "rotation_quaternion", "scale"):
                bone.keyframe_insert(channel, frame=frame)
    rig.animation_data.action.name = "Idle"
    scene.frame_set(start)
    export("idle_rigged", [rig], animation=True)
    print("IDLE EXPORT COMPLETE: stand.blend frames", start, "-", end)


OUTPUT.mkdir(parents=True, exist_ok=True)
export_idle()
if "--idle-only" in sys.argv:
    sys.exit(0)
bpy.ops.wm.open_mainfile(filepath=str(SOURCE / "noeyes.blend"), load_ui=False, use_scripts=False)
rig = bpy.data.objects["Robot_Controls"]
rig.animation_data_clear()
# The new authoring file moved the foot controls to CTRL_Root. Keep the hierarchy
# used by the already imported Generic Idle/Walk clips, without moving rest bones.
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for name in ("CTRL_Foot_L", "CTRL_Foot_R"):
    bone = rig.data.edit_bones[name]
    matrix = bone.matrix.copy()
    bone.parent = rig.data.edit_bones["Lower"]
    bone.matrix = matrix
bpy.ops.object.mode_set(mode="OBJECT")
rig.data.pose_position = "REST"
bpy.context.scene.frame_set(1)
for name, meshes in PARTS.items():
    objects = [bpy.data.objects[mesh] for mesh in meshes]
    for obj in objects:
        # Corrective smoothing is Blender-specific. Subdivision is baked into the rest mesh;
        # the exported armature and vertex weights remain live in Unity.
        for modifier in obj.modifiers:
            if modifier.type == "CORRECTIVE_SMOOTH":
                modifier.show_viewport = modifier.show_render = False
    export(name, [rig] + objects)

for color, prefix in (("red", "Upper"), ("blue", "Lower")):
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE / ("blink_" + color + "eyes.blend")),
                             load_ui=False, use_scripts=False)
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1, 9
    names = [prefix + "Eye_L", prefix + "Eye_R"]
    matrices = {name: [] for name in names}
    # Sample the evaluated drivers, not just the controller-bone keys. These
    # include the blue eyes' authored shape and the red eyes' 0.95 open scale.
    for frame in range(1, 10):
        scene.frame_set(frame)
        for name in names:
            matrices[name].append(bpy.data.objects[name].matrix_world.copy())
    scene.frame_set(1)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    eyes = []
    for name in names:
        source = bpy.data.objects[name]
        mesh = bpy.data.meshes.new_from_object(source.evaluated_get(depsgraph))
        eye = bpy.data.objects.new("Baked_" + name, mesh)
        scene.collection.objects.link(eye)
        eyes.append(eye)
    for obj in list(scene.objects):
        if obj not in eyes:
            bpy.data.objects.remove(obj, do_unlink=True)
    for name, eye in zip(names, eyes):
        eye.name = name
        eye.rotation_mode = "QUATERNION"
        for frame, matrix in enumerate(matrices[name], 1):
            eye.matrix_world = matrix
            for channel in ("location", "rotation_quaternion", "scale"):
                eye.keyframe_insert(channel, frame=frame)
        for curve in eye.animation_data.action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = "LINEAR"
    scene.frame_set(1)
    export("blink_" + color + "eyes_baked", eyes, animation=True)

print("PLAYER EXPORT COMPLETE: new bodies, stand Idle, and baked eyes; existing Walk retained")
