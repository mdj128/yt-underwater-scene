# Changelog

## [0.8.0] - 2025-11-04
- Allow conversion of individual mesh assets in addition to prefabs and FBX/GLB model roots.
- Added animation presets for underwater plants and above-ground grass, applied to generated materials.
- Auto-paint sway weights on generated meshes so only tips bend, for more natural motion in the included shader.
- Grass preset materials now include directional wind controls so large fields sway together.
- Grass wind direction now respects Unity terrain detail rotations, keeping instanced details leaning in the same world direction.
- Fixed shader include path so the package version of `Plant/Terrain/PlantDetail` compiles when imported via UPM.

## [0.1.0] - 2025-11-04
- Initial package creation including the terrain detail conversion editor window and URP shader assets.
