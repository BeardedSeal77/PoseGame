using UnityEngine;
using Windows.Kinect;

[DisallowMultipleComponent]
public class KinectPoseSource : PoseSourceBase
{
    [SerializeField] private KinectBodyTracker tracker;

    public void Configure(KinectBodyTracker assignedTracker)
    {
        tracker = assignedTracker;
    }

    private void Reset()
    {
        tracker = FindAnyObjectByType<KinectBodyTracker>();
    }

    public override bool TryGetPose(PoseFrame outputFrame)
    {
        if (outputFrame == null)
        {
            return false;
        }

        if (tracker == null)
        {
            tracker = FindAnyObjectByType<KinectBodyTracker>();
        }

        if (tracker == null || !tracker.TryGetPrimaryBody(out Body body))
        {
            return false;
        }

        outputFrame.Clear();
        outputFrame.timestamp = Time.time;

        SetJoint(outputFrame, body, JointType.ShoulderLeft, PoseJointId.LeftShoulder);
        SetJoint(outputFrame, body, JointType.ShoulderRight, PoseJointId.RightShoulder);
        SetJoint(outputFrame, body, JointType.ElbowLeft, PoseJointId.LeftElbow);
        SetJoint(outputFrame, body, JointType.ElbowRight, PoseJointId.RightElbow);
        SetJoint(outputFrame, body, JointType.WristLeft, PoseJointId.LeftWrist);
        SetJoint(outputFrame, body, JointType.WristRight, PoseJointId.RightWrist);
        SetJoint(outputFrame, body, JointType.HandTipLeft, PoseJointId.LeftHandTip);
        SetJoint(outputFrame, body, JointType.HandTipRight, PoseJointId.RightHandTip);
        SetJoint(outputFrame, body, JointType.ThumbLeft, PoseJointId.LeftThumb);
        SetJoint(outputFrame, body, JointType.ThumbRight, PoseJointId.RightThumb);
        SetJoint(outputFrame, body, JointType.HipLeft, PoseJointId.LeftHip);
        SetJoint(outputFrame, body, JointType.HipRight, PoseJointId.RightHip);
        SetJoint(outputFrame, body, JointType.KneeLeft, PoseJointId.LeftKnee);
        SetJoint(outputFrame, body, JointType.KneeRight, PoseJointId.RightKnee);
        SetJoint(outputFrame, body, JointType.AnkleLeft, PoseJointId.LeftAnkle);
        SetJoint(outputFrame, body, JointType.AnkleRight, PoseJointId.RightAnkle);
        SetJoint(outputFrame, body, JointType.Neck, PoseJointId.Neck);
        SetJoint(outputFrame, body, JointType.Head, PoseJointId.Head);

        if (TryGetJoint(body, JointType.Head, out Vector3 headPosition, out float headConfidence))
        {
            outputFrame.SetJoint(PoseJointId.Nose, headPosition, headConfidence);

            Vector3 lateralDirection = Vector3.right;
            if (TryGetJoint(body, JointType.ShoulderLeft, out Vector3 leftShoulder, out float leftShoulderConfidence) &&
                TryGetJoint(body, JointType.ShoulderRight, out Vector3 rightShoulder, out float rightShoulderConfidence))
            {
                float syntheticConfidence = Mathf.Min(leftShoulderConfidence, rightShoulderConfidence);
                lateralDirection = (rightShoulder - leftShoulder).normalized;

                Vector3 upDirection = Vector3.up;
                if (TryGetJoint(body, JointType.Neck, out Vector3 neckPosition, out float neckConfidence))
                {
                    upDirection = (headPosition - neckPosition).normalized;
                    outputFrame.SetJoint(PoseJointId.Neck, neckPosition, neckConfidence);
                    syntheticConfidence = Mathf.Min(syntheticConfidence, neckConfidence);
                }

                if (lateralDirection.sqrMagnitude < 0.0001f)
                {
                    lateralDirection = Vector3.right;
                }

                if (upDirection.sqrMagnitude < 0.0001f)
                {
                    upDirection = Vector3.up;
                }

                outputFrame.SetJoint(PoseJointId.LeftEye, headPosition + (upDirection * 0.03f) - (lateralDirection * 0.03f), syntheticConfidence);
                outputFrame.SetJoint(PoseJointId.RightEye, headPosition + (upDirection * 0.03f) + (lateralDirection * 0.03f), syntheticConfidence);
                outputFrame.SetJoint(PoseJointId.LeftEar, headPosition - (lateralDirection * 0.07f), syntheticConfidence);
                outputFrame.SetJoint(PoseJointId.RightEar, headPosition + (lateralDirection * 0.07f), syntheticConfidence);
            }
        }

        return true;
    }

    private static void SetJoint(PoseFrame outputFrame, Body body, JointType sourceJoint, PoseJointId targetJoint)
    {
        if (!TryGetJoint(body, sourceJoint, out Vector3 position, out float confidence))
        {
            return;
        }

        outputFrame.SetJoint(targetJoint, position, confidence);
    }

    private static bool TryGetJoint(Body body, JointType jointType, out Vector3 position, out float confidence)
    {
        position = default;
        confidence = 0f;

        if (body == null || !body.Joints.ContainsKey(jointType))
        {
            return false;
        }

        Windows.Kinect.Joint joint = body.Joints[jointType];
        confidence = GetConfidence(joint.TrackingState);
        if (confidence <= 0f)
        {
            return false;
        }

        CameraSpacePoint sourcePosition = joint.Position;
        position = new Vector3(-sourcePosition.X, sourcePosition.Y, sourcePosition.Z);
        return true;
    }

    private static float GetConfidence(TrackingState trackingState)
    {
        return trackingState switch
        {
            TrackingState.Tracked => 1f,
            TrackingState.Inferred => 0.65f,
            _ => 0f,
        };
    }
}