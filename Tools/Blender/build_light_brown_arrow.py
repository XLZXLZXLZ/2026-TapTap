"""Run with Blender --background --factory-startup --python this_file.

Build a beveled solid arrow, its native Unity mesh/material/prefab, and Blender source.
Unity: XY plane, +X points right, centered pivot, 2 x 0.96 x 0.18 units.
"""
from pathlib import Path
import re
import struct
import uuid

import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Assets/Art/3D Resources/Arrow"
OUT.mkdir(parents=True, exist_ok=True)
PREVIEW = ROOT / "Logs/LightBrownArrow"
PREVIEW.mkdir(parents=True, exist_ok=True)


def write(path, value):
    path.write_text(value, encoding="utf-8", newline="\n")


def meta(path, kind="DefaultImporter", file_id=None):
    target = Path(str(path) + ".meta")
    if target.exists():
        return re.search(r"guid: ([0-9a-f]+)", target.read_text()).group(1)
    guid = uuid.uuid5(uuid.NAMESPACE_URL, path.relative_to(ROOT).as_posix()).hex
    text = f"fileFormatVersion: 2\nguid: {guid}\n"
    if path.is_dir():
        text += "folderAsset: yes\n"
    text += f"{kind}:\n  externalObjects: {{}}\n"
    if file_id is not None:
        text += f"  mainObjectFileID: {file_id}\n"
    text += "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    write(target, text)
    return guid


meta(OUT)
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
outline = [(-1, -0.18), (0.15, -0.18), (0.15, -0.48), (1, 0),
           (0.15, 0.48), (0.15, 0.18), (-1, 0.18)]
n = len(outline)
verts = [(x, y, z) for z in (-0.09, 0.09) for x, y in outline]
faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
faces += [(i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n)]
mesh = bpy.data.meshes.new("Arrow_LightBrown")
mesh.from_pydata(verts, [], faces)
mesh.update()
arrow = bpy.data.objects.new("Arrow_LightBrown", mesh)
bpy.context.collection.objects.link(arrow)
bpy.context.view_layer.objects.active = arrow
arrow.select_set(True)
bevel = arrow.modifiers.new("Soft edges 0.025m", "BEVEL")
bevel.width = 0.025
bevel.segments = 3
bevel.affect = "EDGES"
bpy.ops.object.modifier_apply(modifier=bevel.name)
bm = bmesh.new()
bm.from_mesh(arrow.data)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
assert all(e.is_manifold for e in bm.edges), "Arrow must be a closed solid"
assert bm.calc_volume(signed=True) > 0
bm.to_mesh(arrow.data)
bm.free()
mesh = arrow.data
# Keep the final beveled bounds exactly centered and at the advertised size.
for axis, size in enumerate((2.0, 0.96, 0.18)):
    lo = min(v.co[axis] for v in mesh.vertices)
    hi = max(v.co[axis] for v in mesh.vertices)
    for v in mesh.vertices:
        v.co[axis] = (v.co[axis] - (lo + hi) / 2) * size / (hi - lo)
mesh.update()
mesh.calc_loop_triangles()

# A triangle-local stream preserves the flat front and the bevel's facet normals.
# Reflect Blender Z into Unity Z; reverse triangle winding with the reflection.
vertex_data = bytearray()
index_data = bytearray()
for tri in mesh.loop_triangles:
    for loop_id in reversed(tri.loops):
        v = mesh.vertices[mesh.loops[loop_id].vertex_index].co
        normal = mesh.polygons[tri.polygon_index].normal
        index_data += struct.pack("<H", len(vertex_data) // 32)
        vertex_data += struct.pack("<8f", v.x, v.y, -v.z,
                                   normal.x, normal.y, -normal.z,
                                   (v.x + 1) / 2, (v.y + 0.48) / 0.96)
count = len(vertex_data) // 32
assert count < 65536
xs, ys, zs = zip(*[(v.co.x, v.co.y, v.co.z) for v in mesh.vertices])
center = [(min(a) + max(a)) / 2 for a in (xs, ys, zs)]
extent = [(max(a) - min(a)) / 2 for a in (xs, ys, zs)]
bounds = ("m_Center: {x: %.9g, y: %.9g, z: %.9g}\n"
          "      m_Extent: {x: %.9g, y: %.9g, z: %.9g}") % (*center, *extent)
template = ROOT / "Assets/ArtStyleTest/OvergrownFactoryStudy/Meshes/Process_Diagram_Arrow_8.asset"
text = template.read_text(encoding="utf-8")
text = text.replace("Process_Diagram_Arrow_8", "Arrow_LightBrown")
text = text.replace("indexCount: 3", f"indexCount: {count}")
text = text.replace("vertexCount: 3", f"vertexCount: {count}")
text = text.replace("m_VertexCount: 3", f"m_VertexCount: {count}")
text = re.sub(r"m_Center: \{[^\n]+\}\n\s+m_Extent: \{[^\n]+\}", bounds, text)
# Local bounds use four-space indentation, unlike the submesh bounds.
text = text.replace("  m_LocalAABB:\n    " + bounds,
                    "  m_LocalAABB:\n    " + bounds.replace("      m_Extent", "    m_Extent"))
channels = [(0, 3), (12, 3), (0, 0), (0, 0), (24, 2)] + [(0, 0)] * 9
channel_text = "".join(f"    - stream: 0\n      offset: {off}\n      format: 0\n      dimension: {dim}\n"
                       for off, dim in channels)
text = re.sub(r"    m_Channels:\n.*?    m_DataSize:",
              "    m_Channels:\n" + channel_text + "    m_DataSize:", text, flags=re.S)
text = re.sub(r"m_DataSize: \d+", f"m_DataSize: {len(vertex_data)}", text)
text = re.sub(r"m_IndexBuffer: [^\n]*", "m_IndexBuffer: " + index_data.hex(), text)
text = re.sub(r"_typelessdata: [^\n]*", "_typelessdata: " + vertex_data.hex(), text)
mesh_path = OUT / "Arrow_LightBrown.asset"
write(mesh_path, text)
mesh_guid = meta(mesh_path, "NativeFormatImporter", 4300000)

rgb = (200 / 255, 164 / 255, 126 / 255)
color = "{r: %.9g, g: %.9g, b: %.9g, a: 1}" % rgb
mat_path = OUT / "Arrow_LightBrown_Unlit.mat"
write(mat_path, """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 8
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Arrow_LightBrown_Unlit
  m_Shader: {fileID: 4800000, guid: 650dd9526735d5b46b79224bc6e94025, type: 3}
  m_Parent: {fileID: 0}
  m_ModifiedSerializedProperties: 0
  m_ValidKeywords: []
  m_InvalidKeywords: []
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 1
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap:
    RenderType: Opaque
  disabledShaderPasses: []
  m_LockedProperties:
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _BaseMap:
        m_Texture: {fileID: 0}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Ints: []
    m_Floats:
    - _Surface: 0
    - _Blend: 0
    - _Cull: 2
    - _AlphaClip: 0
    - _Cutoff: 0.5
    - _SrcBlend: 1
    - _DstBlend: 0
    - _SrcBlendAlpha: 1
    - _DstBlendAlpha: 0
    - _ZWrite: 1
    - _AlphaToMask: 0
    - _QueueOffset: 0
    - _ZTest: 4
    m_Colors:
    - _BaseColor: COLOR
  m_BuildTextureStacks: []
""".replace("COLOR", color))
mat_guid = meta(mat_path, "NativeFormatImporter", 2100000)

# Reuse the project's Unity 2022 serialized renderer layout.
prefab = (ROOT / "Assets/Prefabs/Models/Saw_Hazard_Model.prefab").read_text(encoding="utf-8")
prefab = "--- !u!1".join(prefab.split("--- !u!1")[:2])
prefab = prefab.replace("Coral_Warning_Stripe", "Arrow_LightBrown")
for key, value in {"m_LocalRotation": "{x: 0, y: 0, z: 0, w: 1}",
                   "m_LocalPosition": "{x: 0, y: 0, z: 0}",
                   "m_LocalScale": "{x: 1, y: 1, z: 1}",
                   "m_Father": "{fileID: 0}", "m_RootOrder": "0",
                   "m_CastShadows": "0", "m_ReceiveShadows": "0",
                   "m_LightProbeUsage": "0", "m_ReflectionProbeUsage": "0",
                   "m_SelectedEditorRenderState": "0"}.items():
    prefab = re.sub(r"  " + key + r": [^\n]*", "  " + key + ": " + value, prefab)
prefab = re.sub(r"(m_Mesh: \{fileID: 4300000, guid: )[0-9a-f]+", r"\g<1>" + mesh_guid, prefab)
prefab = re.sub(r"(fileID: 2100000, guid: )[0-9a-f]+", r"\g<1>" + mat_guid, prefab)
prefab_path = ROOT / "Assets/Prefabs/Models/Arrow_LightBrown.prefab"
write(prefab_path, prefab)
meta(prefab_path, "PrefabImporter")

# Emission provides the same constant color as the Unity URP/Unlit material.
mat = bpy.data.materials.new("Arrow_LightBrown_Unlit")
mat.use_nodes = True
nodes = mat.node_tree.nodes
nodes.clear()
emission = nodes.new("ShaderNodeEmission")
linear = tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb)
emission.inputs["Color"].default_value = (*linear, 1)
output = nodes.new("ShaderNodeOutputMaterial")
mat.node_tree.links.new(emission.outputs[0], output.inputs[0])
mat.diffuse_color = (*linear, 1)
arrow.data.materials.append(mat)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / "Design/Models/LightBrownArrow.blend"))
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE"
scene.render.resolution_x = 1000
scene.render.resolution_y = 560
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
scene.world.color = (0.025, 0.032, 0.045)
camera_data = bpy.data.cameras.new("Preview")
camera = bpy.data.objects.new("Preview", camera_data)
bpy.context.collection.objects.link(camera)
scene.camera = camera
camera_data.type = "ORTHO"
camera_data.ortho_scale = 2.65
for name, position in [("front", (0, 0, 5)), ("perspective", (1.6, 0.8, 5))]:
    camera.location = position
    forward = (Vector((0, 0, 0)) - camera.location).normalized()
    right = forward.cross(Vector((0, 1, 0))).normalized()
    up = right.cross(forward)
    camera.rotation_euler = Matrix((right, up, -forward)).transposed().to_euler()
    scene.render.filepath = str(PREVIEW / (name + ".png"))
    bpy.ops.render.render(write_still=True)
print(f"Arrow exported: {len(mesh.vertices)} source vertices, {count // 3} triangles, closed manifold.")
print(f"Prefab: {prefab_path}")
