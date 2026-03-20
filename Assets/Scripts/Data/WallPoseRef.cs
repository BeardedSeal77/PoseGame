using UnityEngine;

/// <summary>
/// Attached to baked wall prefabs. Links the wall back to its source PoseData
/// so the runtime can compare the player's pose against the target.
/// </summary>
public class WallPoseRef : MonoBehaviour
{
    public PoseData poseData;
}
