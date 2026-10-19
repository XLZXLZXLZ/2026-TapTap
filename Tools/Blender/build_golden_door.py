"""Blender --background --factory-startup --python Tools/Blender/build_golden_door.py.

Build the 1 x 3 cartoon door. Unity's origin is the bottom cell centre;
the front is Z=0 and the depth extends into positive Z, like Block_1_Visual.
Face colours are baked into vertices for the Unlit shader (no scene lighting).
"""
from pathlib import Path
import math
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/Art/3D Resources/ReturnJourney"
OUTPUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

materials = {}
for name, color in {
    "DoorGold": (1.0, 0.65, 0.16, 1),
    "DoorTrim": (1.0, 0.82, 0.34, 1),
    "DoorRecess": (0.17, 0.085, 0.025, 1),
}.items():
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    materials[name] = mat


def apply(obj, modifier):
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=modifier.name)


def rounded_box(name, size, location, radius, material):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new("Soft cartoon corners", "BEVEL")
    bevel.width, bevel.segments = radius, 4
    apply(obj, bevel)
    obj.data.materials.append(materials[material])
    return obj


def extrude_polygon(name, points, front, back):
    # XY in Unity -> XZ in Blender; Blender Y becomes Unity depth.
    vertices = [(x, y, z) for y in (front, back) for x, z in points]
    n = len(points)
    faces = [tuple(reversed(range(n))), tuple(range(n, n * 2))]
    faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    # Recalculate winding before Boolean operations.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)
    return obj


# One continuous slab, with a real recessed keyhole and a raised rounded inset.
body = rounded_box("Golden rounded door", (1, 0.94, 3), (0, 0.53, 1), 0.085, "DoorGold")
plate = rounded_box("Rounded front border", (0.87, 0.09, 2.83), (0, 0.075, 1), 0.04, "DoorTrim")
panel = rounded_box("Inset front panel", (0.72, 0.055, 2.65), (0, 0.024, 1), 0.025, "DoorGold")

# Classic round head plus tapering stem, sized to read in a distant game camera.
radius, centre = 0.115, 1.075
points = []
for i in range(29):
    angle = math.radians(-35 + i * 250 / 28)
    points.append((math.cos(angle) * radius, centre + math.sin(angle) * radius))
points += [(-0.105, centre - 0.275), (0.105, centre - 0.275)]
for obj, expansion in ((body, 1.04), (plate, 1.02), (panel, 1.0)):
    # Stepped recess walls avoid overlapping coplanar faces between nested panels.
    cut_points = [(x * expansion, 1 + (z - 1) * expansion) for x, z in points]
    cutter = extrude_polygon("Keyhole cutter", cut_points, -0.15, 0.235)
    cut = obj.modifiers.new("Recessed keyhole", "BOOLEAN")
    cut.operation, cut.solver, cut.object = "DIFFERENCE", "EXACT", cutter
    apply(obj, cut)
    bpy.data.objects.remove(cutter, do_unlink=True)
back_points = [(x * 1.06, 1 + (z - 1) * 1.06) for x, z in points]
back = extrude_polygon("Dark keyhole interior", back_points, 0.222, 0.24)
back.data.materials.append(materials["DoorRecess"])

for x in (-0.35, 0.35):
    for z in (-0.34, 2.34):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, radius=1,
                                          location=(x, 0.006, z))
        rivet = bpy.context.object
        rivet.name = "Round golden corner rivet"
        rivet.scale = (0.034, 0.02, 0.034)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        rivet.data.materials.append(materials["DoorTrim"])

# Join by material, then apply transforms so FBX meshes share one coordinate system.
parts = []
for material in materials.values():
    objects = [o for o in bpy.context.scene.objects if o.type == "MESH"
               and o.data.materials and o.data.materials[0] == material]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = material.name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    colors = obj.data.color_attributes.new(name="Color", type="BYTE_COLOR", domain="CORNER")
    # Fixed graphic shading only: bright front/top bevel, darker side/depth.
    for face in obj.data.polygons:
        normal = face.normal
        tone = 0.78 + 0.16 * max(0, -normal.y) + 0.20 * max(0, normal.z)
        tone -= 0.12 * max(0, normal.x) + 0.08 * max(0, -normal.z)
        for loop in face.loop_indices:
            colors.data[loop].color = (tone, tone, tone, 1)
    # FBX's Blender +Y is Unity -Z. Mirror depth so the recessed face is Z=0,
    # facing the gameplay camera, and the back extends into positive Unity Z.
    obj.scale.y = -1
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    parts.append(obj)

bpy.ops.object.select_all(action="DESELECT")
for obj in parts:
    obj.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
source = ROOT / "Design/Models/GoldenDoor.blend"
source.parent.mkdir(parents=True, exist_ok=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(source))
bpy.ops.export_scene.fbx(
    filepath=str(OUTPUT / "GoldenDoor.fbx"), use_selection=True,
    object_types={"MESH"}, axis_forward="-Z", axis_up="Y",
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
    colors_type="LINEAR", path_mode="AUTO",
)
print("GoldenDoor exported with baked Unlit face colours.")
