# Task 1: Fix Wall Scaling Relative to Player Size

## Problem

Walls are defined in viewport space (0-1) and player pose is projected into viewport space for comparison. In theory this should be resolution-independent, but it does **not** account for the player's physical size or distance from the Kinect sensor.

A tall player's limbs project to different viewport coordinates than a short player's, meaning the same wall cutout can be trivially easy for one person and impossible for another. Likewise, stepping closer to the sensor makes the player's projection larger, and stepping back makes it smaller.

### Current Architecture

- **Wall shapes**: defined as polygons in viewport space (0-1) via `PolygonWallShapeAsset` / `RectangleWallShapeAsset`.
- **Player evaluation** ([ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs)): samples points along tracked bone segments, converts them to viewport space with `camera.WorldToViewportPoint()`, and checks whether they fall inside the wall polygon.
- **Character pose** ([HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs)): normalizes Kinect joints relative to hip center and shoulder width. Root position is driven by hip delta from a reference frame captured on the first frame.
- **Root position**: the character can drift in world space as the player walks around, which shifts the viewport projection.

### Why It Doesn't Work

1. The wall cutout is a fixed polygon in viewport space, but the character's viewport footprint changes with distance and body size.
2. `HumanoidPoseDriver` normalizes bone *rotations* by shoulder width, but the root *position* (and therefore the character's world-space scale on screen) is still driven by raw Kinect hip deltas.
3. There is no runtime rescaling of wall geometry to match the player's current viewport footprint.

## Proposed Fix

**Fix the character in world space and scale Kinect inputs to the character, rather than scaling walls to the player.**

### Approach

1. **Lock the character's root position** in world space (no lateral/depth movement from Kinect hip tracking). The character stands at a fixed world position and only rotates and poses in place.
   - In `HumanoidPoseDriver.ApplyRootPosition()`: stop applying `hipDelta` to `avatarRoot.position`. Keep the avatar at its initial spawn point.
   - Optionally allow small lateral sway for feel, but clamp it tightly.

2. **Scale incoming Kinect joint positions to match the avatar's proportions**, so a tall person and a short person produce the same character pose in world space.
   - Use the player's measured shoulder width and/or torso height (hip-to-head distance) from the Kinect skeleton to compute a scale factor.
   - Apply this factor when converting Kinect positions into avatar-local bone directions.
   - `HumanoidPoseDriver` already normalizes by `referenceScale` (shoulder width) for rotations - extend this to ensure the avatar's world-space limb endpoints are consistent regardless of player size.

3. **Remove or disable forward/backward movement** so depth changes don't affect viewport projection. The character stays at a known distance from the camera, meaning its viewport projection is predictable and matches the wall cutouts.

### Key Files to Modify

| File | Change |
|------|--------|
| [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs) | Lock root position; scale joint inputs to avatar proportions |
| [ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs) | May need to adjust `EvaluateCurrentPose()` if character positioning changes |
| [KinectPoseSource.cs](Assets/Scripts/Runtime/Kinect/KinectPoseSource.cs) | Potentially apply normalization earlier in the pipeline |

### Acceptance Criteria

- A tall player and a short player produce the same character viewport footprint.
- Stepping closer or further from the Kinect does **not** change the character's apparent size on screen.
- The character no longer walks around the environment (fixed world position).
- Wall cutouts remain a fair, consistent challenge regardless of player body type.
