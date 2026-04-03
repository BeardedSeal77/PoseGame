using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class HumanoidPoseDriver : MonoBehaviour
{
    [SerializeField] private PoseSourceBase poseSource;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform avatarRoot;
    [SerializeField] private bool mirrorX;
    [SerializeField] private bool swapLeftRight;
    [SerializeField, Range(0.01f, 1f)] private float minimumConfidenceOverride = 0.5f;
    [SerializeField, Min(1f)] private float rotationResponsiveness = 12f;

    private readonly PoseFrame workingFrame = new PoseFrame();
    private readonly Dictionary<PoseJointId, Vector3> normalizedJoints = new Dictionary<PoseJointId, Vector3>(PoseJointIdUtility.JointCount);
    private readonly List<BoneBinding> boneBindings = new List<BoneBinding>(8);

    private Vector3 restTorsoDirection = Vector3.up;
    private Quaternion chestInitialWorldRotation;
    private Quaternion spineInitialWorldRotation;
    private Quaternion hipsInitialWorldRotation;
    private Transform chestBone;
    private Transform spineBone;
    private Transform hipsBone;

    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        avatarRoot = transform;
        poseSource = FindFirstObjectByType<PoseSourceBase>();
    }

    private void Awake()
    {
        if (poseSource == null)
        {
            poseSource = FindFirstObjectByType<PoseSourceBase>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (avatarRoot == null)
        {
            avatarRoot = transform;
        }

        CacheBindings();
    }

    public void Configure(
        PoseSourceBase source,
        Animator assignedAnimator,
        Transform root,
        bool horizontalMirror,
        bool swapSides)
    {
        poseSource = source;
        animator = assignedAnimator;
        avatarRoot = root;
        mirrorX = horizontalMirror;
        swapLeftRight = swapSides;
        CacheBindings();
    }

    private void LateUpdate()
    {
        if (poseSource == null || animator == null || !poseSource.TryGetPose(workingFrame))
        {
            return;
        }

        if (!BuildNormalizedPose(workingFrame))
        {
            return;
        }

        ApplyTorsoRotation();
        ApplyBoneBindings();
    }

    private void CacheBindings()
    {
        boneBindings.Clear();

        if (animator == null)
        {
            return;
        }

        hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);

        if (hipsBone != null)
        {
            hipsInitialWorldRotation = hipsBone.rotation;
        }

        if (spineBone != null)
        {
            spineInitialWorldRotation = spineBone.rotation;
        }

        if (chestBone != null)
        {
            chestInitialWorldRotation = chestBone.rotation;
        }

        restTorsoDirection = GetRestTorsoDirection();

        AddBinding(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, PoseJointId.LeftShoulder, PoseJointId.LeftElbow);
        AddBinding(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, PoseJointId.LeftElbow, PoseJointId.LeftWrist);
        AddBinding(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, PoseJointId.RightShoulder, PoseJointId.RightElbow);
        AddBinding(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, PoseJointId.RightElbow, PoseJointId.RightWrist);
        AddBinding(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, PoseJointId.LeftHip, PoseJointId.LeftKnee);
        AddBinding(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, PoseJointId.LeftKnee, PoseJointId.LeftAnkle);
        AddBinding(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, PoseJointId.RightHip, PoseJointId.RightKnee);
        AddBinding(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, PoseJointId.RightKnee, PoseJointId.RightAnkle);
        AddBinding(HumanBodyBones.Head, HumanBodyBones.Head, PoseJointId.LeftShoulder, PoseJointId.Nose, true);
    }

    private bool BuildNormalizedPose(PoseFrame frame)
    {
        normalizedJoints.Clear();

        float minimumConfidence = poseSource != null
            ? Mathf.Max(minimumConfidenceOverride, poseSource.MinimumConfidence)
            : minimumConfidenceOverride;

        if (!TryGetMidpoint(frame, PoseJointId.LeftHip, PoseJointId.RightHip, minimumConfidence, out Vector3 hipCenter))
        {
            return false;
        }

        if (!TryGetMidpoint(frame, PoseJointId.LeftShoulder, PoseJointId.RightShoulder, minimumConfidence, out Vector3 shoulderCenter))
        {
            return false;
        }

        if (!frame.TryGetJoint(PoseJointId.LeftShoulder, minimumConfidence, out Vector3 leftShoulder) ||
            !frame.TryGetJoint(PoseJointId.RightShoulder, minimumConfidence, out Vector3 rightShoulder))
        {
            return false;
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
            PoseJointId sourceJointId = ResolveSourceJoint(jointId);
            if (!frame.TryGetJoint(sourceJointId, minimumConfidence, out Vector3 worldPoint))
            {
                continue;
            }

            Vector3 normalized = (worldPoint - hipCenter) / referenceScale;
            if (mirrorX)
            {
                normalized.x *= -1f;
            }

            normalizedJoints[jointId] = normalized;
        }

        return true;
    }

    private void ApplyTorsoRotation()
    {
        if (!TryGetNormalizedJoint(PoseJointId.LeftHip, out Vector3 leftHip) ||
            !TryGetNormalizedJoint(PoseJointId.RightHip, out Vector3 rightHip) ||
            !TryGetNormalizedJoint(PoseJointId.LeftShoulder, out Vector3 leftShoulder) ||
            !TryGetNormalizedJoint(PoseJointId.RightShoulder, out Vector3 rightShoulder))
        {
            return;
        }

        Vector3 torsoDirection = ((leftShoulder + rightShoulder) * 0.5f) - ((leftHip + rightHip) * 0.5f);
        if (torsoDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion torsoDelta = Quaternion.FromToRotation(restTorsoDirection, torsoDirection.normalized);

        ApplyTorsoBone(hipsBone, hipsInitialWorldRotation, torsoDelta, 0.35f);
        ApplyTorsoBone(spineBone, spineInitialWorldRotation, torsoDelta, 0.7f);
        ApplyTorsoBone(chestBone, chestInitialWorldRotation, torsoDelta, 1f);
    }

    private void ApplyTorsoBone(Transform bone, Quaternion initialWorldRotation, Quaternion torsoDelta, float weight)
    {
        if (bone == null)
        {
            return;
        }

        Quaternion weightedDelta = Quaternion.Slerp(Quaternion.identity, torsoDelta, weight);
        Quaternion targetRotation = ToRootSpaceRotation(weightedDelta * Quaternion.Inverse(Quaternion.identity) * initialWorldRotation);
        float blend = 1f - Mathf.Exp(-rotationResponsiveness * Time.deltaTime);
        bone.rotation = Quaternion.Slerp(bone.rotation, targetRotation, blend);
    }

    private void ApplyBoneBindings()
    {
        float blend = 1f - Mathf.Exp(-rotationResponsiveness * Time.deltaTime);

        for (int i = 0; i < boneBindings.Count; i++)
        {
            BoneBinding binding = boneBindings[i];
            if (binding.bone == null)
            {
                continue;
            }

            if (!TryGetNormalizedJoint(binding.startJoint, out Vector3 start) ||
                !TryGetNormalizedJoint(binding.endJoint, out Vector3 end))
            {
                continue;
            }

            Vector3 targetDirection = (end - start).normalized;
            if (targetDirection.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            Quaternion delta = Quaternion.FromToRotation(binding.restDirectionRootSpace, targetDirection);
            Quaternion targetRotation = ToRootSpaceRotation(delta * binding.initialWorldRotationInRootSpace);
            binding.bone.rotation = Quaternion.Slerp(binding.bone.rotation, targetRotation, blend);
        }
    }

    private Quaternion ToRootSpaceRotation(Quaternion rootSpaceRotation)
    {
        Quaternion rootRotation = avatarRoot != null ? avatarRoot.rotation : Quaternion.identity;
        return rootRotation * rootSpaceRotation;
    }

    private Vector3 GetRestTorsoDirection()
    {
        if (hipsBone == null || chestBone == null)
        {
            return Vector3.up;
        }

        Quaternion inverseRoot = Quaternion.Inverse(avatarRoot != null ? avatarRoot.rotation : Quaternion.identity);
        Vector3 direction = inverseRoot * (chestBone.position - hipsBone.position);
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
    }

    private void AddBinding(
        HumanBodyBones boneId,
        HumanBodyBones childBoneId,
        PoseJointId startJoint,
        PoseJointId endJoint,
        bool fallbackToSelf = false)
    {
        Transform bone = animator.GetBoneTransform(boneId);
        if (bone == null)
        {
            return;
        }

        Transform childBone = childBoneId == HumanBodyBones.LastBone
            ? null
            : animator.GetBoneTransform(childBoneId);

        if (childBone == null && fallbackToSelf)
        {
            childBone = bone;
        }

        Vector3 restDirection = GetRestDirectionRootSpace(bone, childBone, endJoint == PoseJointId.Nose ? Vector3.up : Vector3.right);

        Quaternion inverseRoot = Quaternion.Inverse(avatarRoot != null ? avatarRoot.rotation : Quaternion.identity);
        boneBindings.Add(new BoneBinding
        {
            bone = bone,
            startJoint = startJoint,
            endJoint = endJoint,
            restDirectionRootSpace = restDirection,
            initialWorldRotationInRootSpace = inverseRoot * bone.rotation,
        });
    }

    private Vector3 GetRestDirectionRootSpace(Transform bone, Transform childBone, Vector3 fallbackDirection)
    {
        Quaternion inverseRoot = Quaternion.Inverse(avatarRoot != null ? avatarRoot.rotation : Quaternion.identity);

        if (bone != null && childBone != null && bone != childBone)
        {
            Vector3 direction = inverseRoot * (childBone.position - bone.position);
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }
        }

        return fallbackDirection.normalized;
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

    private bool TryGetMidpoint(
        PoseFrame frame,
        PoseJointId first,
        PoseJointId second,
        float minimumConfidence,
        out Vector3 midpoint)
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

    [Serializable]
    private struct BoneBinding
    {
        public Transform bone;
        public PoseJointId startJoint;
        public PoseJointId endJoint;
        public Vector3 restDirectionRootSpace;
        public Quaternion initialWorldRotationInRootSpace;
    }
}