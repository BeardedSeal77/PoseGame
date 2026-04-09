using System;

public enum PoseJointId
{
    Nose = 0,
    LeftEye = 1,
    RightEye = 2,
    LeftEar = 3,
    RightEar = 4,
    LeftShoulder = 5,
    RightShoulder = 6,
    LeftElbow = 7,
    RightElbow = 8,
    LeftWrist = 9,
    RightWrist = 10,
    LeftHandTip = 11,
    RightHandTip = 12,
    LeftThumb = 13,
    RightThumb = 14,
    LeftHip = 15,
    RightHip = 16,
    LeftKnee = 17,
    RightKnee = 18,
    LeftAnkle = 19,
    RightAnkle = 20,
    Neck = 21,
    Head = 22,
}

public static class PoseJointIdUtility
{
    public const int JointCount = 23;

    public static bool IsValidIndex(int index)
    {
        return index >= 0 && index < JointCount;
    }
}