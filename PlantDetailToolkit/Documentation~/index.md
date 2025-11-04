# Terrain Detail Converter

The Terrain Detail Converter window is available under **Tools ▸ Terrain Details ▸ Convert Mesh To Terrain Detail**. Use it to transform imported static mesh assets (FBX/GLB models, prefabs, or loose meshes) into terrain detail assets with built-in wind sway animation.

## Conversion Options

- **Detail Shader** – defaults to the package's `Plant/Terrain/PlantDetail` shader. Assign a custom shader if desired.
- **Animation Preset** – choose between underwater sway and lightweight grass motion presets. Values can be adjusted directly on the generated material afterwards.
- The converter writes sway weights into vertex colors (alpha) so only the upper portions of each mesh bend with the wind. Leave **Create combined mesh asset** enabled to generate these weights.
- Grass preset materials also receive wind direction and strength settings so neighboring instances lean together by default. Tune `_WindDirection`, `_WindStrength`, and `_WindGustStrength` on the generated material to taste.
- **Register detail prefabs on a terrain** – when enabled, the tool adds or reuses mesh detail prototypes on the chosen terrain (defaults to the active terrain if left empty).
- **Collapse LODGroup to first LOD** – when enabled, only meshes from the first LOD in any LODGroup are combined. Disable to include every renderer in the prefab.
- **Copy base texture/color from source material** – copies the main texture and tint color from the source material when creating the detail material.
- **Create combined mesh asset** – generates a new combined mesh asset for the prefab.
- **Create material asset** – builds a material that references the detail shader.
- **Create preview prefab** – produces a prefab containing the combined mesh and material, ready to be assigned to a terrain detail prototype.

Generated assets are written into `Meshes`, `Materials`, and `Prefabs` subfolders under the output root you choose.
