# Underwater Swim Controller

A lightweight third-person swimming locomotion system designed for underwater scenes. The package contains runtime behaviours for movement, camera-driven buoyancy, water-volume detection, and optional visual polish such as fog tinting and bubble bursts.

## Requirements

- Unity 2021.3 or newer.
- Input System package (`com.unity.inputsystem`) enabled in the project.
- URP or HDRP project if you plan to leverage the Volume-based post-effects (optional).

## Installation

Install from Git via `Window ▸ Package Manager ▸ Add package from git URL…` and append the package folder path:

```
https://github.com/mdj128/yt-underwater-scene.git?path=UnderwaterSwimController
```

Replace the repository URL, branch, or revision tag as needed.

## Contents

- `UnderwaterSwimController` – Player locomotion using Camera-relative input, optional sprinting, height limits, and terrain avoidance.
- `WaterVolume` & `WaterVolumeZone` – Helpers for defining finite underwater regions and syncing boxed water materials.
- `UnderwaterVisuals` – Camera-side fog/post-processing and bubble burst system that reacts to the player's water state.

## Quick Start

1. Add a `PlayerInput` component configured with actions named **Move**, **Jump**, **Crouch**, and **Sprint**.
2. Attach `UnderwaterSwimController` to the player root. Assign the camera transform and animator (optional).
3. Create a GameObject with a `BoxCollider` (set to trigger) and add `WaterVolume` to define the swimmable area.
4. (Optional) Add `UnderwaterVisuals` to the main camera to drive fog, post-effects, and bubble particles while underwater.

## License

Distributed under the same license as the hosting repository unless otherwise noted.
