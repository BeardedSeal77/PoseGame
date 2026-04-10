# Task 2: Isometric Camera View and Wall Direction Flip

## Problem

Currently the camera faces the character head-on (front view), and walls travel **toward** the camera/viewport (from behind the camera toward the player). The desired setup is:

- **Isometric-style camera** angled 30-40 degrees above the scene, looking down at the character.
- **Character still faces the camera** (mirror mode - player sees the front of the character, movements are mirrored).
- **Walls approach from the distance** (far side of the scene, visible to the player), moving toward the character - not from behind the camera.

## Current Architecture

- **Camera**: `Camera.main` with `FixedAspectCamera` enforcing 16:9 aspect. Position/rotation set in scene file ([GameScene.unity](Assets/Scenes/GameScene.unity)).
- **Wall travel**: [ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs) uses `wallStartDepth` (default 0.75 in Z) and lerps `travelOffset` from that depth toward 0. The wall's `root.transform.localPosition` is set to `(0, 0, travelOffset)`.
- **Character facing**: [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs) applies root rotation from shoulder/hip cross-product. The character model faces the camera (mirror mode) in the current setup.
- **Pose evaluation**: `EvaluateCurrentPose()` converts bone world positions to viewport space via `camera.WorldToViewportPoint()` and checks them against the wall polygon.

## Proposed Fix

### 1. Camera Repositioning

- Move the camera to an elevated position in front of and above the character, looking down at ~30-40 degrees.
- Since the character still faces the camera, the camera should be on the same side as before but elevated.
- Example transform:
  - Position: `(0, Y, -Z)` where Y gives the elevation and -Z places it in front of the character.
  - Rotation: `(30-40, 0, 0)` pitch downward.
- Keep `FixedAspectCamera` 16:9 enforcement active.

### 2. Character Orientation

- **No change to character facing** - character continues to face the camera (mirror mode).
- Kinect left/right mirroring stays as-is.
- Root yaw and rotation logic in `HumanoidPoseDriver` remains unchanged.

### 3. Wall Travel Direction

- Currently walls spawn near the camera and travel toward the character. Reverse this:
- Walls should spawn far behind the character (large positive Z, away from camera) and travel toward the character/camera.
- The player sees walls approaching from the distance, getting closer and eventually reaching the character.
- Reverse the travel offset: start at a far Z value and lerp toward the character's Z position.

### 4. Pose Evaluation Adjustment

- With an angled camera, viewport-space projection changes. Wall polygons authored for a front-on view will be distorted in an isometric projection.
- **Recommended approach**: Decouple pose evaluation from the camera. Evaluate on the wall's own facing plane (perpendicular to the wall's travel direction) rather than viewport space. Project both character bone samples and wall polygon onto this plane.
- This makes wall shapes camera-independent - existing shapes continue to work regardless of camera angle.

### Key Files to Modify

| File | Change |
|------|--------|
| [GameScene.unity](Assets/Scenes/GameScene.unity) | Camera position and rotation |
| [ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs) | Reverse wall travel direction; decouple evaluation from camera projection |
| [FixedAspectCamera.cs](Assets/Scripts/Runtime/Camera/FixedAspectCamera.cs) | Likely unchanged, only transform changes needed |

### Acceptance Criteria

- Camera shows an isometric view from 30-40 degrees above.
- The character still faces the camera (mirror mode preserved).
- Walls visibly approach from the far end of the scene toward the character.
- Pose evaluation works correctly with the new camera angle (decoupled from viewport projection).
- The game feels natural - player movements still map intuitively as a mirror.
