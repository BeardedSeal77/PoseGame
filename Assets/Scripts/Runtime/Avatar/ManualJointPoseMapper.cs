using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ManualJointPoseMapper : MonoBehaviour
{
    [SerializeField] private PoseSourceBase poseSource;
    [SerializeField] private Transform rootSpace;
    [SerializeField] private bool mirrorX = true;
    [SerializeField] private bool swapLeftRight = true;
    [SerializeField, Range(0.01f, 1f)] private float minimumConfidenceOverride = 0.5f;
    [SerializeField, Min(1f)] private float rotationResponsiveness = 12f;
    [SerializeField, Min(1f)] private float positionResponsiveness = 12f;
    [SerializeField] private List<ManualBinding> bindings = new List<ManualBinding>();

    private readonly PoseFrame workingFrame = new PoseFrame();
    private readonly Dictionary<PoseJointId, Vector3> rawJoints = new Dictionary<PoseJointId, Vector3>(PoseJointIdUtility.JointCount);
    private readonly Dictionary<PoseJointId, Vector3> normalizedJoints = new Dictionary<PoseJointId, Vector3>(PoseJointIdUtility.JointCount);
    private bool cacheInitialized;

    private void Reset()
    {
        poseSource = FindAnyObjectByType<PoseSourceBase>();
        rootSpace = transform;
    }

    private void Awake()
    {
        if (poseSource == null)
        {
            poseSource = FindAnyObjectByType<PoseSourceBase>();
        }

        if (rootSpace == null)
        {
            rootSpace = transform;
        }

        RebuildBindingCache();
    }

    private void OnEnable()
    {
        if (!cacheInitialized)
        {
            RebuildBindingCache();
        }
    }

    [ContextMenu("Rebuild Binding Cache")]
    public void RebuildBindingCache()
    {
        Quaternion inverseRoot = Quaternion.Inverse(GetRootRotation());

        for (int index = 0; index < bindings.Count; index++)
        {
            ManualBinding binding = bindings[index];
            if (binding == null || binding.target == null)
            {
                continue;
            }

            binding.initialLocalPosition = binding.target.localPosition;
            binding.initialRotationInRootSpace = inverseRoot * binding.target.rotation;

            Vector3 restAim = inverseRoot * binding.target.TransformDirection(GetAxisOrDefault(binding.localAimAxis, Vector3.up));
            Vector3 restUp = inverseRoot * binding.target.TransformDirection(GetAxisOrDefault(binding.localUpAxis, Vector3.forward));
            binding.restBasisInRootSpace = CreateOrientation(restAim, restUp);
        }

        cacheInitialized = true;
    }

    private void LateUpdate()
    {
        if (poseSource == null || !poseSource.TryGetPose(workingFrame))
        {
            return;
        }

        if (!BuildPoseData(workingFrame))
        {
            return;
        }

        float rotationBlend = 1f - Mathf.Exp(-rotationResponsiveness * Time.deltaTime);
        float positionBlend = 1f - Mathf.Exp(-positionResponsiveness * Time.deltaTime);

        for (int index = 0; index < bindings.Count; index++)
        {
            ManualBinding binding = bindings[index];
            if (binding == null || binding.target == null)
            {
                continue;
            }

            if (binding.drivePosition && TryGetNormalizedJoint(binding.positionJoint, out Vector3 position))
            {
                Vector3 scaledPosition = Vector3.Scale(position, binding.positionScale);
                Vector3 targetLocalPosition = binding.initialLocalPosition + scaledPosition + binding.localPositionOffset;
                binding.target.localPosition = Vector3.Lerp(binding.target.localPosition, targetLocalPosition, positionBlend);
            }

            if (!binding.driveRotation)
            {
                continue;
            }

            if (!TryGetNormalizedJoint(binding.startJoint, out Vector3 start) ||
                !TryGetNormalizedJoint(binding.endJoint, out Vector3 end))
            {
                continue;
            }

            Vector3 aimDirection = end - start;
            if (binding.invertAimDirection)
            {
                aimDirection = -aimDirection;
            }

            Vector3 upDirection = Vector3.up;
            if (binding.useUpVector &&
                TryGetNormalizedJoint(binding.upFromJoint, out Vector3 upFrom) &&
                TryGetNormalizedJoint(binding.upToJoint, out Vector3 upTo))
            {
                upDirection = upTo - upFrom;
            }

            Quaternion targetBasis = CreateOrientation(aimDirection, upDirection);
            Quaternion delta = targetBasis * Quaternion.Inverse(binding.restBasisInRootSpace);
            Quaternion eulerOffset = Quaternion.Euler(binding.localEulerOffset);
            Quaternion targetRotation = ToRootSpaceRotation(delta * binding.initialRotationInRootSpace * eulerOffset);
            binding.target.rotation = Quaternion.Slerp(binding.target.rotation, targetRotation, rotationBlend);
        }
    }

    private bool BuildPoseData(PoseFrame frame)
    {
        rawJoints.Clear();
        normalizedJoints.Clear();

        float minimumConfidence = poseSource != null
            ? Mathf.Max(minimumConfidenceOverride, poseSource.MinimumConfidence)
            : minimumConfidenceOverride;

        if (!TryGetMidpoint(frame, PoseJointId.LeftHip, PoseJointId.RightHip, minimumConfidence, out Vector3 hipCenter) ||
            !TryGetMidpoint(frame, PoseJointId.LeftShoulder, PoseJointId.RightShoulder, minimumConfidence, out Vector3 shoulderCenter) ||
            !frame.TryGetJoint(ResolveSourceJoint(PoseJointId.LeftShoulder), minimumConfidence, out Vector3 leftShoulder) ||
            !frame.TryGetJoint(ResolveSourceJoint(PoseJointId.RightShoulder), minimumConfidence, out Vector3 rightShoulder))
        {
            return false;
        }

        if (mirrorX)
        {
            leftShoulder.x *= -1f;
            rightShoulder.x *= -1f;
        }

        float referenceScale = Vector3.Distance(leftShoulder, rightShoulder);
        if (referenceScale < 0.001f)
        {
            referenceScale = Vector3.Distance(hipCenter, shoulderCenter);
        }

        if (referenceScale < 0.001f)
        {
            return false;
        }

        foreach (PoseJointId jointId in Enum.GetValues(typeof(PoseJointId)))
        {
            PoseJointId sourceJoint = ResolveSourceJoint(jointId);
            if (!frame.TryGetJoint(sourceJoint, minimumConfidence, out Vector3 worldPoint))
            {
                continue;
            }

            if (mirrorX)
            {
                worldPoint.x *= -1f;
            }

            rawJoints[jointId] = worldPoint;
            normalizedJoints[jointId] = (worldPoint - hipCenter) / referenceScale;
        }

        return true;
    }

    private Quaternion ToRootSpaceRotation(Quaternion rootSpaceRotation)
    {
        return GetRootRotation() * rootSpaceRotation;
    }

    private Quaternion GetRootRotation()
    {
        return rootSpace != null ? rootSpace.rotation : transform.rotation;
    }

    private bool TryGetNormalizedJoint(PoseJointId jointId, out Vector3 position)
    {
        return normalizedJoints.TryGetValue(jointId, out position);
    }

    private PoseJointId ResolveSourceJoint(PoseJointId jointId)
    {
        if (!swapLeftRight)
        {
            return jointId;
        }

        return jointId switch
        {
            PoseJointId.LeftEye => PoseJointId.RightEye,
            PoseJointId.RightEye => PoseJointId.LeftEye,
            PoseJointId.LeftEar => PoseJointId.RightEar,
            PoseJointId.RightEar => PoseJointId.LeftEar,
            PoseJointId.LeftShoulder => PoseJointId.RightShoulder,
            PoseJointId.RightShoulder => PoseJointId.LeftShoulder,
            PoseJointId.LeftElbow => PoseJointId.RightElbow,
            PoseJointId.RightElbow => PoseJointId.LeftElbow,
            PoseJointId.LeftWrist => PoseJointId.RightWrist,
            PoseJointId.RightWrist => PoseJointId.LeftWrist,
            PoseJointId.LeftHip => PoseJointId.RightHip,
            PoseJointId.RightHip => PoseJointId.LeftHip,
            PoseJointId.LeftKnee => PoseJointId.RightKnee,
            PoseJointId.RightKnee => PoseJointId.LeftKnee,
            PoseJointId.LeftAnkle => PoseJointId.RightAnkle,
            PoseJointId.RightAnkle => PoseJointId.LeftAnkle,
            _ => jointId,
        };
    }

    private bool TryGetMidpoint(PoseFrame frame, PoseJointId first, PoseJointId second, float minimumConfidence, out Vector3 midpoint)
    {
        if (frame.TryGetJoint(ResolveSourceJoint(first), minimumConfidence, out Vector3 a) &&
            frame.TryGetJoint(ResolveSourceJoint(second), minimumConfidence, out Vector3 b))
        {
            if (mirrorX)
            {
                a.x *= -1f;
                b.x *= -1f;
            }

            midpoint = (a + b) * 0.5f;
            return true;
        }

        midpoint = default;
        return false;
    }

    private static Vector3 GetAxisOrDefault(Vector3 axis, Vector3 fallback)
    {
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : fallback;
    }

    private static Quaternion CreateOrientation(Vector3 forward, Vector3 up)
    {
        Vector3 sanitizedForward = GetAxisOrDefault(forward, Vector3.forward);
        Vector3 sanitizedUp = GetAxisOrDefault(up, Vector3.up);

        if (Mathf.Abs(Vector3.Dot(sanitizedForward.normalized, sanitizedUp.normalized)) > 0.98f)
        {
            sanitizedUp = Mathf.Abs(Vector3.Dot(sanitizedForward.normalized, Vector3.up)) > 0.98f
                ? Vector3.forward
                : Vector3.up;
        }

        return Quaternion.LookRotation(sanitizedForward, sanitizedUp);
    }

    [Serializable]
    private class ManualBinding
    {
        public string name;
        public Transform target;
        public bool driveRotation = true;
        public PoseJointId startJoint = PoseJointId.LeftShoulder;
        public PoseJointId endJoint = PoseJointId.LeftElbow;
        public bool invertAimDirection;
        public bool useUpVector;
        public PoseJointId upFromJoint = PoseJointId.LeftShoulder;
        public PoseJointId upToJoint = PoseJointId.Neck;
        public Vector3 localAimAxis = Vector3.up;
        public Vector3 localUpAxis = Vector3.forward;
        public Vector3 localEulerOffset;
        public bool drivePosition;
        public PoseJointId positionJoint = PoseJointId.LeftShoulder;
        public Vector3 positionScale = Vector3.one;
        public Vector3 localPositionOffset;

        [NonSerialized] public Vector3 initialLocalPosition;
        [NonSerialized] public Quaternion initialRotationInRootSpace;
        [NonSerialized] public Quaternion restBasisInRootSpace;
    }
}