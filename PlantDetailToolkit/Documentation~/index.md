# Terrain Detail Converter

The Terrain Detail Converter window is available under **Tools ▸ Terrain Details ▸ Convert Mesh To Terrain Detail**. Use it to transform imported static mesh prefabs into terrain detail assets with built-in wind sway animation.

## Conversion Options

- **Detail Shader** – defaults to the package's `Plant/Terrain/PlantDetail` shader. Assign a custom shader if desired.
- **Collapse LODGroup to first LOD** – when enabled, only meshes from the first LOD in any LODGroup are combined. Disable to include every renderer in the prefab.
- **Copy base texture/color from source material** – copies the main texture and tint color from the source material when creating the detail material.
- **Create combined mesh asset** – generates a new combined mesh asset for the prefab.
- **Create material asset** – builds a material that references the detail shader.
- **Create preview prefab** – produces a prefab containing the combined mesh and material, ready to be assigned to a terrain detail prototype.

Generated assets are written into `Meshes`, `Materials`, and `Prefabs` subfolders under the output root you choose.
