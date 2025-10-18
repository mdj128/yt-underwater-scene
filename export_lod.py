import bpy
import os
from contextlib import contextmanager

# ---------------- CONFIG ----------------
EXPORT_DIR = r"C:\temp\lod_exports"   # <-- change me
PER_GROUP_FILE = True                 # True = one .glb per LOD group; False = single combined file
EXPORT_SELECTED_GROUPS_ONLY = True    # True = export only selected _LOD_GROUP empties
INCLUDE_ONLY_VISIBLE = False          # If True, skip meshes hidden in viewport
APPLY_MODIFIERS_AND_TRANSFORMS = True # glTF 'export_apply'
TRIANGULATE = False                   # If your target wants triangles only, set True
# ---------------------------------------

@contextmanager
def restore_selection_visibility():
    """Save & restore selection and visibility states."""
    # Selection
    prev_active = bpy.context.view_layer.objects.active
    prev_sel = [o for o in bpy.context.selected_objects]
    # Visibility
    states = []
    for o in bpy.data.objects:
        states.append((o, o.hide_viewport, o.hide_render))
    try:
        yield
    finally:
        # Restore visibility
        for o, hv, hr in states:
            o.hide_viewport = hv
            o.hide_render = hr
        # Restore selection
        bpy.ops.object.select_all(action='DESELECT')
        for o in prev_sel:
            if o.name in bpy.data.objects:
                o.select_set(True)
        if prev_active and prev_active.name in bpy.data.objects:
            bpy.context.view_layer.objects.active = prev_active

def ensure_dir(path):
    os.makedirs(path, exist_ok=True)

def is_lod_group(obj):
    return obj.type == 'EMPTY' and obj.name.endswith("_LOD_GROUP")

def collect_group_meshes(root_empty, include_only_visible=False):
    """Return all MESH children (direct or nested) of a group empty."""
    meshes = []
    stack = list(root_empty.children)
    while stack:
        o = stack.pop()
        stack.extend(list(o.children))
        if o.type == 'MESH':
            if include_only_visible and (o.hide_viewport or o.hide_render):
                continue
            meshes.append(o)
    return meshes

def select_only(objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    if objs:
        bpy.context.view_layer.objects.active = objs[0]

def export_glb(filepath, use_selection=True):
    # Build a conservative set of options (no deprecated args).
    wanted_kwargs = dict(
        filepath=filepath,
        export_format='GLB',
        use_selection=use_selection,
        use_visible=INCLUDE_ONLY_VISIBLE,
        export_apply=APPLY_MODIFIERS_AND_TRANSFORMS,
        export_texcoords=True,
        export_normals=True,
        export_tangents=True,
        export_cameras=False,
        export_lights=False,
        export_extras=True,
    )

    # Filter to only the properties supported by THIS Blender's glTF operator.
    op_props = set(bpy.ops.export_scene.gltf.get_rna_type().properties.keys())
    safe_kwargs = {k: v for k, v in wanted_kwargs.items() if k in op_props}

    # Call exporter with safe kwargs
    bpy.ops.export_scene.gltf(**safe_kwargs)
    print(f"[OK] Exported {filepath} with options: {sorted(safe_kwargs.keys())}")

def export_groups(groups):
    ensure_dir(EXPORT_DIR)

    if PER_GROUP_FILE:
        for g in groups:
            meshes = collect_group_meshes(g, include_only_visible=INCLUDE_ONLY_VISIBLE)
            if not meshes:
                print(f"[WARN] No meshes found under {g.name}, skipping.")
                continue
            # Temporarily unhide selected meshes for export
            with restore_selection_visibility():
                for m in meshes:
                    m.hide_viewport = False
                    m.hide_render = False
                select_only(meshes)
                out = os.path.join(EXPORT_DIR, f"{g.name}.glb")
                export_glb(out, use_selection=True)
    else:
        # Single combined file of all selected groups
        all_meshes = []
        for g in groups:
            all_meshes.extend(collect_group_meshes(g, include_only_visible=INCLUDE_ONLY_VISIBLE))
        if not all_meshes:
            print("[WARN] No meshes found in the chosen groups.")
            return
        with restore_selection_visibility():
            for m in all_meshes:
                m.hide_viewport = False
                m.hide_render = False
            select_only(all_meshes)
            out = os.path.join(EXPORT_DIR, "combined_lod_groups.glb")
            export_glb(out, use_selection=True)

def find_target_groups():
    if EXPORT_SELECTED_GROUPS_ONLY:
        groups = [o for o in bpy.context.selected_objects if is_lod_group(o)]
        if groups:
            return groups
        else:
            print("[INFO] No selected _LOD_GROUP empties; falling back to all in scene.")
    # Fallback: all LOD groups in scene
    return [o for o in bpy.data.objects if is_lod_group(o)]

def main():
    groups = find_target_groups()
    if not groups:
        print("[ERROR] No *_LOD_GROUP empties found.")
        return
    print(f"[INFO] Exporting {len(groups)} LOD group(s).")
    export_groups(groups)

if __name__ == "__main__":
    main()
