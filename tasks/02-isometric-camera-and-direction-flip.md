# Task 2: Isometric Camera View and Direction Flip

## Problem

Currently the camera faces the character head-on (front view), and walls travel **toward** the camera/viewport. The desired setup is:

- **Isometric-style camera** angled 30-40 degrees above the scene, looking down at the character.
- **Character faces away** from the screen (back of character visible to the player).
- **Walls approach from the distance** (far side of the scene), moving toward the character - not from behind the camera.

## Current Architecture

- **Camera**: `Camera.main` with `FixedAspectCamera` enforcing 16:9 aspect. Position/rotation set in scene file ([GameScene.unity](Assets/Scenes/GameScene.unity)).
- **Wall travel**: [ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs) uses `wallStartDepth` (default 0.75 in Z) and lerps `travelOffset` from that depth toward 0. The wall's `root.transform.localPosition` is set to `(0, 0, travelOffset)`.
- **Character facing**: [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs) applies root rotation from shoulder/hip cross-product. The character model is oriented to face the camera in the current setup.
- **Pose evaluation**: `EvaluateCurrentPose()` converts bone world positions to viewport space via `camera.WorldToViewportPoint()` and checks them against the wall polygon. This means the evaluation is inherently camera-relative.

## Proposed Fix

### 1. Camera Repositioning

- Move the camera to an elevated position behind and above the character.
- Set rotation to look down at ~30-40 degrees.
- Example transform:
  - Position: `(0, Y, -Z)` where Y gives the elevation and Z places it behind the character.
  - Rotation: `(30-40, 0, 0)` pitch downward.
- Keep `FixedAspectCamera` 16:9 enforcement active.

### 2. Character Orientation

- Rotate the character's base transform 180 degrees on Y so the character's back faces the camera.
- In `HumanoidPoseDriver`, adjust `rootPositionScale` Z sign and root yaw calculation to account for the flipped orientation.
- Mirror the Kinect X-axis mapping if needed so left/right are still intuitive for the player.

### 3. Wall Travel Direction

- Walls should spawn far away (large positive Z in front of the character) and travel toward the character.
- Reverse the travel offset direction: start at a far Z value and lerp toward the character's Z position.
- The wall should be visible approaching from a distance in the isometric view.

### 4. Pose Evaluation Adjustment

- Since `EvaluateCurrentPose()` projects into viewport space, the camera change automatically updates the projection.
- However, the wall polygon shapes were authored for a front-on view. With an angled camera, the same 2D polygon may not represent the correct silhouette.
- **Option A**: Keep wall shapes in viewport space and re-author them for the new camera angle.
- **Option B**: Define wall shapes in a character-local plane (perpendicular to the wall's approach direction) and project both the wall and the character onto that plane for comparison. This decouples evaluation from camera angle.
- Option B is more robust long-term but requires more refactoring.

### Key Files to Modify

| File | Change |
|------|--------|
| [GameScene.unity](Assets/Scenes/GameScene.unity) | Camera position and rotation |
| [FixedAspectCamera.cs](Assets/Scripts/Runtime/Camera/FixedAspectCamera.cs) | May need to remain unchanged if only transform changes |
| [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs) | Adjust root rotation, axis mirroring for new orientation |
| [ScreenWallFitController.cs](Assets/Scripts/Runtime/WallFit/ScreenWallFitController.cs) | Reverse wall travel direction, adjust spawn position |
| Wall shape assets in [Resources/WallShapes/](Assets/Resources/WallShapes/) | May need re-authoring for new perspective |

### Acceptance Criteria

- Camera shows an isometric view from 30-40 degrees above.
- The player sees the character's back.
- Walls visibly approach from the far end of the scene toward the character.
- Pose evaluation still works correctly with the new camera angle.
- The game feels natural - player movements map intuitively to the on-screen character.
