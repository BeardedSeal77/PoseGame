# PoseGame Design Document

## Overview

A pose-matching game where pre-baked walls with human-shaped cutouts move toward the player. The player must match the cutout pose using their body (via webcam pose detection). Their pose is compared against stored pose data for that wall, scored with an error tolerance.

## Core Concept

- 10-15 pre-defined poses, each with a baked wall prefab
- Walls move toward the player during gameplay
- Player matches the pose shown in the cutout
- Scoring based on bone/joint angle comparison with an allowed error %

## Architecture

### 1. Pose Data (Scriptable Objects)

Each pose is defined as a `PoseData` ScriptableObject containing:
- Joint positions (normalized skeleton: shoulder, elbow, wrist, hip, knee, ankle, head)
- Joint angles for scoring comparison
- Difficulty rating
- Display name

### 2. Wall Baking (Editor-Time Only)

An editor tool that runs once per pose to produce a wall prefab:

1. Read pose joint positions from `PoseData`
2. Generate a body silhouette polygon by inflating capsule shapes around each limb segment
3. Create a wall rectangle mesh
4. Boolean-subtract the silhouette from the wall rect (using polygon clipping)
5. Apply jungle texture to the solid wall surface
6. Optionally add decorative sprites (vines, leaves) around cutout edges
7. Save as a prefab bundling: baked wall mesh, material, and reference to the source `PoseData`

Output: 10-15 wall prefabs stored in `Assets/Prefabs/Walls/`

### 3. Runtime Game Loop

1. Load all wall prefabs from the walls folder
2. Pick a random wall (no repeats until all played)
3. Spawn the wall at a distance, move it toward the player
4. Capture player pose via webcam + pose detection
5. Compare player joint angles to the wall's `PoseData`
6. Score: percentage of joints within the error threshold
7. Pass/fail feedback, then next wall

### 4. Pose Comparison / Scoring

- Compare joint angles (not absolute positions) so player size doesn't matter
- Each joint has an angular error tolerance (e.g. 15-25 degrees)
- Overall score = % of joints within tolerance
- Pass threshold configurable (e.g. 70% of joints must match)

## Tech Stack

- **Engine:** Unity (2D URP)
- **Pose Detection:** MediaPipe or YOLO pose (via Python backend streaming keypoints over WebSocket/UDP)
- **Wall Generation:** Custom Unity editor script using C# polygon clipping
- **Texturing:** Jungle-themed materials and sprite overlays

## Project Structure

```
Assets/
  Data/
    Poses/           # PoseData ScriptableObjects (15 poses)
  Prefabs/
    Walls/            # Baked wall prefabs (1 per pose)
  Scripts/
    Data/
      PoseData.cs     # ScriptableObject definition
    Editor/
      WallBaker.cs    # Editor tool to bake walls from pose data
    Runtime/
      GameManager.cs  # Game loop, wall selection, scoring
      WallMover.cs    # Moves wall toward player
      PoseDetector.cs # Webcam pose detection / receiver
      PoseComparer.cs # Compares player pose to target pose
  Textures/
    Jungle/           # Wall textures and decorative sprites
  Scenes/
    Game.unity        # Main game scene
```
