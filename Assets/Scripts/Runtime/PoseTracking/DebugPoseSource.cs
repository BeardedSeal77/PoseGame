using UnityEngine;

public class DebugPoseSource : PoseSourceBase
{
    [Header("Animation")]
    [SerializeField] private bool animate = true;
    [SerializeField] private float armSwingDegrees = 60f;
    [SerializeField] private float legSwingDegrees = 25f;
    [SerializeField] private float animationSpeed = 1.25f;

    [Header("Manual Offsets")]
    [SerializeField] private Vector2 bodyOffset;
    [SerializeField] private Vector2 leftHandOffset;
    [SerializeField] private Vector2 rightHandOffset;

    [Header("Pose Shape")]
    [SerializeField] private float shoulderWidth = 0.45f;
    [SerializeField] private float hipWidth = 0.3f;
    [SerializeField] private float torsoHeight = 0.6f;
    [SerializeField] private float upperArmLength = 0.32f;
    [SerializeField] private float lowerArmLength = 0.28f;
    [SerializeField] private float upperLegLength = 0.42f;
    [SerializeField] private float lowerLegLength = 0.42f;
    [SerializeField] private float headHeight = 0.28f;

    private readonly PoseFrame poseFrame = new PoseFrame();

    public override bool TryGetPose(PoseFrame outputFrame)
    {
        if (outputFrame == null)
        {
            return false;
        }

        BuildPose();
        outputFrame.CopyFrom(poseFrame);
        return true;
    }

    private void BuildPose()
    {
        poseFrame.Clear();
        poseFrame.timestamp = Time.time;

        float cycle = animate ? Mathf.Sin(Time.time * animationSpeed) : 0f;
        float inverseCycle = animate ? Mathf.Sin(Time.time * animationSpeed + Mathf.PI) : 0f;

        Vector3 hipCenter = new Vector3(bodyOffset.x, bodyOffset.y, 0f);
        Vector3 leftHip = hipCenter + Vector3.left * (hipWidth * 0.5f);
        Vector3 rightHip = hipCenter + Vector3.right * (hipWidth * 0.5f);
        Vector3 shoulderCenter = hipCenter + Vector3.up * torsoHeight;
        Vector3 leftShoulder = shoulderCenter + Vector3.left * (shoulderWidth * 0.5f);
        Vector3 rightShoulder = shoulderCenter + Vector3.right * (shoulderWidth * 0.5f);

        float leftArmAngle = 90f + (armSwingDegrees * cycle);
        float rightArmAngle = 90f + (armSwingDegrees * inverseCycle);
        float leftLegAngle = -90f + (legSwingDegrees * inverseCycle);
        float rightLegAngle = -90f + (legSwingDegrees * cycle);

        Vector3 leftElbow = leftShoulder + DirectionFromDegrees(leftArmAngle) * upperArmLength;
        Vector3 rightElbow = rightShoulder + DirectionFromDegrees(180f - rightArmAngle) * upperArmLength;
        Vector3 leftWrist = leftElbow + DirectionFromDegrees(leftArmAngle) * lowerArmLength + (Vector3)leftHandOffset;
        Vector3 rightWrist = rightElbow + DirectionFromDegrees(180f - rightArmAngle) * lowerArmLength + (Vector3)rightHandOffset;

        Vector3 leftKnee = leftHip + DirectionFromDegrees(leftLegAngle) * upperLegLength;
        Vector3 rightKnee = rightHip + DirectionFromDegrees(rightLegAngle) * upperLegLength;
        Vector3 leftAnkle = leftKnee + DirectionFromDegrees(leftLegAngle) * lowerLegLength;
        Vector3 rightAnkle = rightKnee + DirectionFromDegrees(rightLegAngle) * lowerLegLength;

        Vector3 nose = shoulderCenter + Vector3.up * headHeight;
        Vector3 leftEye = nose + new Vector3(-0.03f, 0.03f, 0f);
        Vector3 rightEye = nose + new Vector3(0.03f, 0.03f, 0f);
        Vector3 leftEar = nose + new Vector3(-0.08f, 0.01f, 0f);
        Vector3 rightEar = nose + new Vector3(0.08f, 0.01f, 0f);

        SetTracked(PoseJointId.Nose, nose);
        SetTracked(PoseJointId.LeftEye, leftEye);
        SetTracked(PoseJointId.RightEye, rightEye);
        SetTracked(PoseJointId.LeftEar, leftEar);
        SetTracked(PoseJointId.RightEar, rightEar);
        SetTracked(PoseJointId.LeftShoulder, leftShoulder);
        SetTracked(PoseJointId.RightShoulder, rightShoulder);
        SetTracked(PoseJointId.LeftElbow, leftElbow);
        SetTracked(PoseJointId.RightElbow, rightElbow);
        SetTracked(PoseJointId.LeftWrist, leftWrist);
        SetTracked(PoseJointId.RightWrist, rightWrist);
        SetTracked(PoseJointId.LeftHip, leftHip);
        SetTracked(PoseJointId.RightHip, rightHip);
        SetTracked(PoseJointId.LeftKnee, leftKnee);
        SetTracked(PoseJointId.RightKnee, rightKnee);
        SetTracked(PoseJointId.LeftAnkle, leftAnkle);
        SetTracked(PoseJointId.RightAnkle, rightAnkle);
    }

    private void SetTracked(PoseJointId jointId, Vector3 position)
    {
        poseFrame.SetJoint(jointId, position, 1f);
    }

    private static Vector3 DirectionFromDegrees(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f).normalized;
    }
}