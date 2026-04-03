using System;
using UnityEngine;

[Serializable]
public struct PoseJoint
{
    public Vector3 position;
    [Range(0f, 1f)] public float confidence;

    public bool IsTracked(float minimumConfidence)
    {
        return confidence >= minimumConfidence;
    }
}

[Serializable]
public class PoseFrame
{
    [SerializeField] private PoseJoint[] joints = new PoseJoint[PoseJointIdUtility.JointCount];

    public float timestamp;

    public PoseJoint[] Joints => joints;

    public PoseJoint this[PoseJointId jointId]
    {
        get => joints[(int)jointId];
        set => joints[(int)jointId] = value;
    }

    public void CopyFrom(PoseFrame other)
    {
        if (other == null)
        {
            return;
        }

        EnsureCapacity();
        other.EnsureCapacity();
        Array.Copy(other.joints, joints, PoseJointIdUtility.JointCount);
        timestamp = other.timestamp;
    }

    public bool TryGetJoint(PoseJointId jointId, float minimumConfidence, out Vector3 position)
    {
        EnsureCapacity();
        PoseJoint joint = joints[(int)jointId];
        if (!joint.IsTracked(minimumConfidence))
        {
            position = default;
            return false;
        }

        position = joint.position;
        return true;
    }

    public void SetJoint(PoseJointId jointId, Vector3 position, float confidence = 1f)
    {
        EnsureCapacity();
        joints[(int)jointId] = new PoseJoint
        {
            position = position,
            confidence = confidence,
        };
    }

    public void Clear()
    {
        EnsureCapacity();
        Array.Clear(joints, 0, joints.Length);
        timestamp = 0f;
    }

    private void EnsureCapacity()
    {
        if (joints != null && joints.Length == PoseJointIdUtility.JointCount)
        {
            return;
        }

        Array.Resize(ref joints, PoseJointIdUtility.JointCount);
    }
}