# Underwater Swim Controller

The Underwater Swim Controller package contains the runtime behaviours required to prototype underwater characters quickly. Use this page as a high-level setup reference.

## Components

### UnderwaterSwimController
Handles character locomotion. Requires a `PlayerInput` component that exposes actions named `Move` (Vector2), `Jump`, `Crouch`, and `Sprint`. Optional references include a `Terrain` for bottom clamping, a camera transform for heading, and an `Animator` for swim blend parameters.

### WaterVolume
Marks trigger volumes that count as underwater space. Add it to a `BoxCollider` (trigger). Swimmers query the static helpers (`WaterVolume.IsPointInside`) to determine whether they should be active.

### UnderwaterVisuals
Attach to the main camera to lerp RenderSettings fog, activate a `Volume`, and emit particle bursts while underwater. Assign an optional `UnderwaterSwimController` reference to keep camera fog in sync with the player state.

### WaterVolumeZone
Utility for WaterWorks style boxed volumes. Syncs shader parameters with a collider's bounds so the volumetric water matches the playable space.

## Sample Workflow

1. Create a prefab with `PlayerInput` and `UnderwaterSwimController`.
2. Configure your Input Action Asset so the action names match the defaults, or rename them in the inspector.
3. Place `WaterVolume` objects anywhere you want players to be allowed to swim.
4. Add `UnderwaterVisuals` to your camera and link any bubble particle systems or post-processing volumes.
5. Enter play mode and tweak speeds, acceleration, and rotation damping until the movement feels right.
