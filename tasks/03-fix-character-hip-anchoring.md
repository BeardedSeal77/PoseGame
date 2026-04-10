# Task 3: Fix Character Hip Anchoring (Crouching Lifts Legs)

## Problem

The character is anchored at the hips. When the player crouches, the hips drop in Kinect space, but because the character's root is hip-anchored, the hip bone stays roughly in place and the **legs lift upward** instead of the torso lowering downward. The character appears to float rather than crouch.

## Current Architecture

In [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs):

1. **Root position** (`ApplyRootPosition()`): The avatar root is positioned based on the Kinect hip center delta from a reference frame. The reference frame is captured once (first valid frame).

2. **Hip height** (`ApplyHipHeight()`): Adjusts the local Y position of the hips bone based on leg extension ratio (hip-to-ankle distance). When legs are more bent, `maxHipDrop` (0.2) lowers the hips bone locally.

3. **The core issue**: The avatar root follows the hips. When the player crouches:
   - Kinect reports hips dropping (lower Y).
   - `ApplyRootPosition()` moves the root down by `hipDelta.y`.
   - But the **feet** also move in Kinect space (they stay on the ground).
   - The bone chain from hips to feet is driven by rotations derived from relative joint positions.
   - If the root doesn't drop enough (or the hip height adjustment fights it), the legs appear to lift because the foot targets relative to the hips haven't changed enough.

4. **No foot grounding / IK**: There is no inverse kinematics pass to pin feet to the ground plane. Legs are driven purely by forward kinematics from the Kinect joint rotations.

## Proposed Fix

### Approach: Anchor at Feet, Not Hips

Instead of anchoring the character at the hips and letting the feet float, anchor the character so the **feet stay on the ground plane** and the hips/torso move naturally above them.

### Implementation Steps

1. **Track foot ground position**: Use the Kinect foot joint Y positions to determine the ground plane. The lowest foot Y in the reference frame defines "ground."

2. **Anchor root to ground**: Instead of driving `avatarRoot.position` from hip delta, compute the root position so that the character's feet remain at ground level (Y=0 or the scene's floor height).
   - Calculate where the feet *would* be given current hip position + leg rotations.
   - Offset the root so feet touch the floor.

3. **Alternative - simpler approach**: Keep hip-driven root but add a vertical correction:
   - Each frame, measure the lowest foot world position.
   - Compute the difference between that foot position and the ground plane.
   - Shift the entire root down by that difference.
   - This ensures at least one foot always touches the ground.

4. **Adjust hip height logic**: `ApplyHipHeight()` currently fights the crouch. When the player crouches:
   - The root should drop (hips go down).
   - The feet should stay planted.
   - The hip height adjustment should **not** limit how far the hips can drop. Consider increasing or removing `maxHipDrop` cap, or replacing the entire hip height system with the foot-grounding approach.

5. **Optional IK pass**: For polish, add a simple two-bone IK solve for each leg to ensure feet land exactly on the ground plane. Unity's `Animator` supports IK callbacks (`OnAnimatorIK`) which could be leveraged.

### Key Files to Modify

| File | Change |
|------|--------|
| [HumanoidPoseDriver.cs](Assets/Scripts/Runtime/Avatar/HumanoidPoseDriver.cs) | Rework `ApplyRootPosition()` and `ApplyHipHeight()` to anchor from feet instead of hips; add foot grounding offset |
| [PoseFrame.cs](Assets/Scripts/Runtime/PoseTracking/PoseFrame.cs) | May need to expose foot joint positions more directly |

### Acceptance Criteria

- When the player crouches, the character's torso lowers and legs bend naturally with feet staying on the ground.
- Legs do **not** lift upward when crouching.
- Standing up returns the character to full height smoothly.
- The fix works regardless of player height (connects with Task 1's scaling work).
