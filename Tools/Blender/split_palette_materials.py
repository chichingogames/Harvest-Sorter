"""Blender: split each Harvest Sorter FBX into one material per palette swatch.

blender -b --factory-startup -P split_palette_materials.py -- <models_dir> <swatches.json> <out_dir> <mode: analyze|convert> <surface_materials.json>

Each face is assigned to the HS_Palette.png swatch cell its UVs sit in, and gets a solid-colour material
named <Model>_<SwatchName>. "analyze" only writes report_analyze.json; "convert" also writes the split FBX
files to out_dir. Unity side: one .mat per material in Materials/<Model>/ and an externalObjects remap per
name in the model's .fbx.meta.

In "convert" mode UV channel 0 is rewritten with a box projection in model space, in units of the face's
surface tileMeters (HS_Surface_Materials.json), so the tileable surface textures repeat at their real-world
size with material tiling (1, 1). The palette UVs are kept in channel 1 ("Palette").
"""
import bpy, json, math, os, sys
from collections import Counter, defaultdict

models_dir, swatch_json, out_dir, mode, surface_json = sys.argv[sys.argv.index("--") + 1:]
N = 8
swatches = json.load(open(swatch_json))
tile_of = {m["sourceName"]: m["tileMeters"] for m in json.load(open(surface_json))["materials"]}
cell_to_swatch = {}
for name, s in swatches.items():
    u, v = s["uv"]
    cell_to_swatch[(int(u * N), int(v * N))] = name


def pascal(name):
    return "".join(w[:1].upper() + w[1:] for w in name.replace("-", " ").split())


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def cell_of(uv):
    # clamp so u == 1.0 stays in the last cell
    return (min(N - 1, max(0, math.floor(uv[0] * N))), min(N - 1, max(0, math.floor(uv[1] * N))))


def box_project(ob, face_swatch):
    """Copy the palette UVs to a "Palette" layer, then box-project the first layer in tile units."""
    me = ob.data
    first = me.uv_layers[0]
    if "Palette" not in me.uv_layers:
        pal = me.uv_layers.new(name="Palette")
        for i, d in enumerate(first.data):
            pal.data[i].uv = d.uv
    first.name = "Surface"
    mw = ob.matrix_world
    nmat = mw.inverted_safe().transposed().to_3x3()
    for poly, name in zip(me.polygons, face_swatch):
        tile = tile_of.get(name, 1.0)
        n = nmat @ poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        flip = -1 if n[ax] < 0 else 1
        for li in poly.loop_indices:
            p = mw @ me.vertices[me.loops[li].vertex_index].co
            # Blender is Z up: X faces use (y, z), Y faces (x, z), Z faces (x, y); flip keeps it unmirrored
            u, v = ((flip * p.y, p.z), (-flip * p.x, p.z), (flip * p.x, p.y))[ax]
            first.data[li].uv = (u / tile, v / tile)


report = {}
os.makedirs(out_dir, exist_ok=True)
for f in sorted(os.listdir(models_dir)):
    if not f.lower().endswith(".fbx"):
        continue
    model = f[:-4]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(models_dir, f))

    info = {"faces": 0, "straddle": 0, "unknown": 0, "swatches": Counter(), "objects": {}}
    mats = {}
    seen = set()
    for ob in [o for o in bpy.context.scene.objects if o.type == "MESH"]:
        me = ob.data
        if me.name in seen:  # shared mesh data is converted once
            continue
        seen.add(me.name)
        uvl = me.uv_layers.active
        face_swatch = []
        for poly in me.polygons:
            cells = Counter(cell_of(uvl.data[li].uv) for li in poly.loop_indices)
            us = [uvl.data[li].uv[0] for li in poly.loop_indices]
            vs = [uvl.data[li].uv[1] for li in poly.loop_indices]
            c = cell_of((sum(us) / len(us), sum(vs) / len(vs)))  # face centre decides
            if len(cells) > 1:
                info["straddle"] += 1
            name = cell_to_swatch.get(c)
            if name is None:
                info["unknown"] += 1
            face_swatch.append(name)
            info["faces"] += 1
            info["swatches"][name] += 1
        info["objects"][ob.name] = len(me.polygons)

        if mode != "convert":
            continue
        me.materials.clear()
        slot_of = {}
        for name in sorted(set(face_swatch), key=lambda n: (n is None, n or "")):
            key = name or "Unmapped"
            if key not in mats:
                m = bpy.data.materials.new(f"{model}_{pascal(key)}")
                if name:
                    h = swatches[name]["hex"].lstrip("#")
                    rgb = [srgb_to_linear(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4)]
                    m.diffuse_color = (*rgb, 1)
                    bsdf = m.node_tree.nodes.get("Principled BSDF") if m.node_tree else None
                    if bsdf:
                        bsdf.inputs["Base Color"].default_value = (*rgb, 1)
                        bsdf.inputs["Roughness"].default_value = swatches[name]["roughness"]
                        bsdf.inputs["Metallic"].default_value = swatches[name]["metallic"]
                mats[key] = m
            me.materials.append(mats[key])
            slot_of[name] = len(me.materials) - 1
        for poly, name in zip(me.polygons, face_swatch):
            poly.material_index = slot_of[name]
        box_project(ob, face_swatch)

    if mode == "convert":
        # remove the old palette material/images so they are not exported
        for m in list(bpy.data.materials):
            if m.users == 0:
                bpy.data.materials.remove(m)
        for im in list(bpy.data.images):
            bpy.data.images.remove(im)
        # FBX_SCALE_ALL + bake_space_transform: roots import into Unity with no rotation and scale 1,
        # instead of the (90, 0, 0) / x100 root transform a default Blender export produces
        bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, f), use_selection=False,
                                 object_types={"EMPTY", "MESH"}, path_mode="STRIP",
                                 embed_textures=False, bake_anim=False,
                                 apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                                 bake_space_transform=True, axis_forward="-Z", axis_up="Y")
    info["swatches"] = {(k or "UNMAPPED"): v for k, v in info["swatches"].items()}
    report[model] = info

json.dump(report, open(os.path.join(out_dir, f"report_{mode}.json"), "w"), indent=1)
tot = sum(r["faces"] for r in report.values())
print(f"MODELS {len(report)} FACES {tot} STRADDLE {sum(r['straddle'] for r in report.values())} "
      f"UNKNOWN {sum(r['unknown'] for r in report.values())} "
      f"MATERIALS {sum(len(r['swatches']) for r in report.values())}")
