using UnityEngine;
using System;

/// <summary>
/// Stores a single pose as normalized keypoint positions (0-1 range).
/// Created from captured JSON pose files via the PoseImporter editor tool.
/// </summary>
[CreateAssetMenu(fileName = "NewPose", menuName = "PoseGame/Pose Data")]
public class PoseData : ScriptableObject
{
    public string displayName;

    [Range(1, 5)]
    public int difficulty = 1;

    public Keypoint[] keypoints = new Keypoint[17];

    /// <summary>
    /// Get a keypoint by name (e.g. "left_shoulder").
    /// </summary>
    public Keypoint GetKeypoint(string jointName)
    {
        foreach (var kp in keypoints)
        {
            if (kp.name == jointName)
                return kp;
        }
        return default;
    }

    /// <summary>
    /// Get the angle at a joint formed by two connected joints.
    /// E.g. angle at elbow = shoulder -> elbow -> wrist.
    /// </summary>
    public float GetJointAngle(string from, string joint, string to)
    {
        var a = GetKeypoint(from);
        var b = GetKeypoint(joint);
        var c = GetKeypoint(to);

        if (a.confidence < 0.3f || b.confidence < 0.3f || c.confidence < 0.3f)
            return -1f; // Unreliable

        Vector2 ba = new Vector2(a.x - b.x, a.y - b.y);
        Vector2 bc = new Vector2(c.x - b.x, c.y - b.y);

        float dot = Vector2.Dot(ba, bc);
        float cross = ba.x * bc.y - ba.y * bc.x;

        return Mathf.Atan2(Mathf.Abs(cross), dot) * Mathf.Rad2Deg;
    }
}

[Serializable]
public struct Keypoint
{
    public string name;
    [Range(0f, 1f)] public float x;
    [Range(0f, 1f)] public float y;
    [Range(0f, 1f)] public float confidence;
}
