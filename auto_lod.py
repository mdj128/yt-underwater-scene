import bpy

# --------------------- CONFIG ---------------------
LOD_RATIOS = [1.0, 0.4, 0.15]  # LOD0, LOD1, LOD2 (1.0 keeps original)
APPLY_SCALE = True             # Apply object scale before decimating
DECIMATE_SYMMETRIC = False     # Toggle if your mesh needs symmetry care
# --------------------------------------------------

def link_to_collections(obj, collections):
    for c in collections:
        c.objects.link(obj)

def unique_copy(obj):
    dup = obj.copy()
    dup.data = obj.data.copy()
    return dup

def ensure_object_mode():
    if bpy.ops.object.mode_set.poll():
        bpy.ops.object.mode_set(mode='OBJECT')

def apply_scale(obj):
    m = obj.matrix_world.copy()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.select_set(False)
    obj.matrix_world = m

def add_decimate(obj, ratio):
    mod = obj.modifiers.new(name=f"LOD_Decimate_{ratio:.2f}", type='DECIMATE')
    mod.ratio = ratio
    mod.use_collapse_triangulate = False
    mod.use_symmetry = DECIMATE_SYMMETRIC
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)

def make_group_empty(base_name, collection):
    empty = bpy.data.objects.new(f"{base_name}_LOD_GROUP", None)
    collection.objects.link(empty)
    return empty

def add_blender_lod_entries(controller, lod_objs, distances=(0.0, 15.0, 30.0)):
    """Tries to populate Blender's LOD panel (if available)."""
    try:
        # Clear existing
        while controller.lod_levels:
            controller.lod_levels.remove(controller.lod_levels[-1])
        # LOD0 uses controller itself
        lvl0 = controller.lod_levels.add()
        lvl0.object = controller
        lvl0.distance = distances[0]
        for i, o in enumerate(lod_objs[1:], start=1):
            lvl = controller.lod_levels.add()
            lvl.object = o
            lvl.distance = distances[min(i, len(distances)-1)]
    except Exception as e:
        print(f"[INFO] Skipped Blender LOD setup (not supported?): {e}")

ensure_object_mode()

selected = [o for o in bpy.context.selected_objects if o.type == 'MESH']
if not selected:
    print("Select at least one mesh object.")
else:
    for src in selected:
        # Work on a safe reference to collections
        cols = src.users_collection or [bpy.context.scene.collection]

        # Optionally apply scale for consistent decimation
        if APPLY_SCALE:
            apply_scale(src)

        base_name = src.name.split("_LOD")[0]  # strip any existing suffix
        src.name = f"{base_name}_LOD0"

        # Create group empty for neat hierarchy
        group_empty = make_group_empty(base_name, cols[0])
        src.parent = group_empty

        lod_objs = [src]

        # Create reduced copies for LOD1..N
        for i, ratio in enumerate(LOD_RATIOS[1:], start=1):
            lod = unique_copy(src)
            lod.name = f"{base_name}_LOD{i}"
            link_to_collections(lod, cols)
            lod.parent = group_empty

            # Reset transforms relative to parent
            lod.location = src.location
            lod.rotation_euler = src.rotation_euler
            lod.scale = src.scale

            # Decimate (skip for ratio >= 0.999 to avoid needless work)
            if ratio < 0.999:
                add_decimate(lod, ratio)

            lod_objs.append(lod)

        # Try to populate Blender's LOD panel on LOD0 controller
        add_blender_lod_entries(src, lod_objs)

        print(f"[OK] Generated LODs for {base_name}: {[o.name for o in lod_objs]}")
