# Plant Terrain Detail Toolkit

The Plant Terrain Detail Toolkit wraps the plant conversion scripts and shaders into a Unity Package Manager package. It turns static mesh prefabs (FBX/GLB) into animated terrain detail assets that can be painted with the terrain tools.

## Requirements

- Unity 2021.3 or newer (tested with Universal Render Pipeline projects).
- Universal Render Pipeline package installed in the project (the shader depends on URP lighting includes).

## Installation

Install from Git by adding the repository URL plus the package path in the Unity Package Manager (`Window > Package Manager > Add package from git URL...`). For example:

```
https://github.com/your-org/your-repo.git?path=PlantDetailToolkit
```

Replace the repository and branch information as needed.

## Features

- Combines the first LOD (or all renderers) of imported prefabs into a single mesh asset.
- Generates URP detail shader materials with configurable sway animation and alpha clipping.
- Creates ready-to-paint prefab assets, leaving source prefabs untouched.
- Copies diffuse textures and colors from the source material when possible.

## Usage

1. Select one or more prefab assets in the Project window (FBX/GLB imports or regular prefabs).
2. Open **Tools ▸ Terrain Details ▸ Convert Mesh To Terrain Detail**.
3. Pick an output folder inside `Assets/` for the generated meshes, materials, and prefabs.
4. (Optional) Assign a different shader or tweak conversion options.
5. Click **Convert Selected**. Mesh, material, and prefab assets will be generated in subfolders under the chosen output root.
6. Assign the generated prefabs to a terrain detail prototype and paint them using Unity's terrain detail tools.

## Folders Created

- `Meshes` – combined mesh assets that match the detail prefabs.
- `Materials` – URP materials configured for wind sway animation.
- `Prefabs` – lightweight prefabs used as terrain detail prototypes.

## Shader

The included shader `Plant/Terrain/PlantDetail` matches URP's detail lighting with additional vertex animation controls (_SwayAmplitude_, _SwaySpeed_, etc.).

## Extending

The conversion code is contained in `Editor/TerrainDetailConversionWindow.cs`. Feel free to extend it with custom naming conventions, additional material property transfers, or terrain prototype automation.
