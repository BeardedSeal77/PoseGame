# Player Model Setup

This is the first runtime slice for driving a humanoid avatar from tracked joints.

## What exists now

- `PoseSourceBase`: abstraction for live pose input.
- `DebugPoseSource`: synthetic pose generator so you can verify avatar retargeting before camera integration.
- `HumanoidPoseDriver`: reads tracked joints and rotates a Mecanim humanoid rig.

## Importing a player model

1. Import an FBX or other character asset into `Assets/`.
2. Open the model import settings.
3. Set `Rig > Animation Type` to `Humanoid`.
4. Apply, then confirm Unity successfully maps the avatar bones.
5. Drag the model into the scene.

## Wiring the runtime test

1. Create an empty GameObject named `PoseRuntime`.
2. Add `DebugPoseSource` to `PoseRuntime`.
3. Add `HumanoidPoseDriver` to the imported character root or the same object that owns the `Animator`.
4. Assign the `Pose Source` field to `PoseRuntime`.
5. Assign the `Animator` field to the model animator.
6. Leave `Avatar Root` as the model root unless the character is nested under another transform.
7. Enter Play Mode and verify the arms and legs animate.

## One-click main scene setup

There is now an editor command that builds the first front-facing player scene for the Nature hero:

1. Open Unity and wait for scripts to compile.
2. Run `PoseGame > Setup Main Game Scene`.
3. Open `Assets/Scenes/MainGame.unity` if it is not opened automatically.
4. Press Play.

That setup creates:

- a centered `PlayerAvatar` using `Hero_Nature.prefab`
- a front camera so the avatar faces you like a mirror
- a `PoseRuntime` object with `DebugPoseSource`
- a `HumanoidPoseDriver` configured for mirrored reflection

When you add a real tracking source later, remove `DebugPoseSource` and add your live `PoseSourceBase` implementation to `PoseRuntime`.

## Coordinate convention for future live sources

`HumanoidPoseDriver` expects joint positions in a body-local camera plane:

- `+Y` is up
- `+X` is the actor's right
- `Z` is optional depth
- positions should be in consistent units, but exact body size does not matter

The driver normalizes each pose using hips and shoulder width before applying it to the avatar.

## Why normalize through a model

Using a player model helps with presentation and gameplay consistency, but it does not automatically solve body-size mismatch by itself. The important part is the normalization step:

- center the pose around the hips
- scale it by shoulder width or torso length
- compare angles or normalized joint positions instead of raw world distances

That means a tall player and a short player can still fit the same target pose if their body shape is reduced to the same normalized skeleton.

## Kinect v2 and webcam plan

Use the same `PoseSourceBase` pipeline for both inputs:

- `KinectV2PoseSource`: best first source for your current hardware because it already provides body joints and depth.
- `WebcamPoseSource`: later source for laptop support, backed by ONNX or a local Python streamer.

The avatar retargeter should stay the same. Only the source component changes.