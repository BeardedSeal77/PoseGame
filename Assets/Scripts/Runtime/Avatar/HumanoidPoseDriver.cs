using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class HumanoidPoseDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PoseSourceBase poseSource;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform avatarRoot;

    [Header("Pose Input")]
    [SerializeField] private bool mirrorX;
    [SerializeField] private bool swapLeftRight;
    [SerializeField, Range(0.01f, 1f)] private float minimumConfidenceOverride = 0.5f;
    [SerializeField, Min(1f)] private float rotationResponsiveness = 12f;

    [Header("Root Rotation")]
    [SerializeField] private bool driveRootYaw = true;
    [SerializeField] private bool invertRootYaw;
    [SerializeField, Range(-180f, 180f)] private float rootFacingOffsetDegrees = 180f;
    [SerializeField, Min(1f)] private float rootYawResponsiveness = 8f;
    [SerializeField, Range(0f, 85f)] private float maxRootYawDegrees = 70f;

    [Header("Root Translation")]
    [SerializeField] private bool driveRootPosition = true;
    [SerializeField] private bool driveRootHeight;
    [SerializeField, Min(1f)] private float rootPositionResponsiveness = 8f;
    [Tooltip("Scales Kinect hip movement into avatar root movement on X/Y/Z.")]
    [SerializeField] private Vector3 rootPositionScale = new Vector3(1f, 1f, -1f);
    [Tooltip("Keeps the avatar root from sinking below an ankle-based floor estimate.")]
    [SerializeField] private bool keepFeetGrounded = true;
    [SerializeField, Min(0f)] private float maxFootHeightOffset = 0.2f;

    [Header("Local Body Motion")]
    [SerializeField] private bool driveHipHeight = true;
    [SerializeField, Min(0.01f)] private float maxHipDrop = 0.2f;
    [SerializeField, Min(1f)] private float hipHeightResponsiveness = 10f;

    [Header("Bone Rotation")]
    [FormerlySerializedAs("driveHandBones")]
    [Tooltip("Drives the lower-arm chain from elbow to wrist. Disable only if wrist tracking is too noisy.")]
    [SerializeField] private bool driveLowerArmBones = true;
    [SerializeField] private bool invertArmDirections;
    [SerializeField] private bool invertLegDirections;
    [SerializeField] private bool invertHeadDirection;
    [SerializeField] private Vector3 armRotationOffsetEuler;
    [SerializeField] private Vector3 legRotationOffsetEuler = new Vector3(0f, 180f, 0f);
    [SerializeField] private Vector3 headRotationOffsetEuler = new Vector3(0f, 180f, 0f);
    [Tooltip("Optional Kinect hand rotation layer. Disable this to fall back to the current forearm-only behavior.")]
    [SerializeField] private bool driveHandRotations;
    [SerializeField] private bool invertLeftHandAimDirection;
    [SerializeField] private bool invertRightHandAimDirection;
    [SerializeField] private bool invertLeftHandRoll;
    [SerializeField] private bool invertRightHandRoll;
    [SerializeField] private Vector3 leftHandRotationOffsetEuler;
    [SerializeField] private Vector3 rightHandRotationOffsetEuler;

    private readonly PoseFrame workingFrame = new PoseFrame();
    private readonly Dictionary<PoseJointId, Vector3> rawJoints = new Dictionary<PoseJointId, Vector3>(PoseJointIdUtility.JointCount);
    private readonly Dictionary<PoseJointId, Vector3> normalizedJoints = new Dictionary<PoseJointId, Vector3>(PoseJointIdUtility.JointCount);
    private readonly List<BoneBinding> boneBindings = new List<BoneBinding>(8);

    private Vector3 restTorsoDirection = Vector3.up;
    private Quaternion avatarRootInitialRotation = Quaternion.identity;
    private Quaternion chestInitialWorldRotation;
    private Quaternion spineInitialWorldRotation;
    private Quaternion hipsInitialWorldRotation;
    private float referenceShoulderWidth;
    private float referenceHipWidth;
    private float referenceLegExtension;
    private Vector3 referenceFacingDirection;
    private Vector3 avatarRootInitialPosition;
    private Vector3 avatarRootInitialRight;
    private Vector3 avatarRootInitialForward;
    private Vector3 referenceHipCenter;
    private float referenceFootHeight;
    private bool hasRootPositionReference;
    private Transform chestBone;
    private Transform spineBone;
    private Transform hipsBone;
    private Vector3 hipsInitialLocalPosition;
    private HandBinding leftHandBinding;
    private HandBinding rightHandBinding;

    private void Reset()
    {
        animator = GetComponentInChildren<Animator>();
        avatarRoot = transform;
        poseSource = FindAnyObjectByType<PoseSourceBase>();
    }

    private void Awake()
    {
        if (poseSource == null)
        {
            poseSource = FindAnyObjectByType<PoseSourceBase>();
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

        ApplyRootYaw();
        ApplyRootPosition();
        ApplyHipHeight();
        ApplyTorsoRotation();
        ApplyBoneBindings();
        ApplyHandRotations();
    }

    private void CacheBindings()
    {
        boneBindings.Clear();

        if (animator == null)
        {
            return;
        }

        Quaternion initialRootRotation = avatarRoot != null ? avatarRoot.rotation : transform.rotation;
        avatarRootInitialPosition = avatarRoot != null ? avatarRoot.position : transform.position;
        avatarRootInitialRight = Vector3.ProjectOnPlane(initialRootRotation * Vector3.right, Vector3.up).normalized;
        avatarRootInitialForward = Vector3.ProjectOnPlane(initialRootRotation * Vector3.forward, Vector3.up).normalized;
        if (avatarRootInitialRight.sqrMagnitude < 0.0001f)
        {
            avatarRootInitialRight = Vector3.right;
        }

        if (avatarRootInitialForward.sqrMagnitude < 0.0001f)
        {
            avatarRootInitialForward = Vector3.forward;
        }

        avatarRootInitialRotation = initialRootRotation * Quaternion.AngleAxis(rootFacingOffsetDegrees, Vector3.up);
        referenceShoulderWidth = 0f;
        referenceHipWidth = 0f;
        referenceLegExtension = 0f;
        referenceFacingDirection = Vector3.zero;
        hasRootPositionReference = false;

        hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);

        if (hipsBone != null)
        {
            hipsInitialWorldRotation = hipsBone.rotation;
            hipsInitialLocalPosition = hipsBone.localPosition;
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

        AddBinding(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, PoseJointId.LeftShoulder, PoseJointId.LeftElbow, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, PoseJointId.RightShoulder, PoseJointId.RightElbow, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, PoseJointId.Neck, PoseJointId.LeftShoulder, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, PoseJointId.Neck, PoseJointId.RightShoulder, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, PoseJointId.LeftElbow, PoseJointId.LeftWrist, driveIfEnabled: driveLowerArmBones, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, PoseJointId.RightElbow, PoseJointId.RightWrist, driveIfEnabled: driveLowerArmBones, group: BindingGroup.Arms);
        AddBinding(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, PoseJointId.LeftHip, PoseJointId.LeftKnee, group: BindingGroup.Legs);
        AddBinding(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, PoseJointId.LeftKnee, PoseJointId.LeftAnkle, group: BindingGroup.Legs);
        AddBinding(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, PoseJointId.RightHip, PoseJointId.RightKnee, group: BindingGroup.Legs);
        AddBinding(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, PoseJointId.RightKnee, PoseJointId.RightAnkle, group: BindingGroup.Legs);
        AddBinding(HumanBodyBones.Head, HumanBodyBones.Head, PoseJointId.Neck, PoseJointId.Head, true, group: BindingGroup.Head);

        leftHandBinding = CreateHandBinding(
            HumanBodyBones.LeftHand,
            HumanBodyBones.LeftMiddleProximal,
            HumanBodyBones.LeftThumbProximal,
            Quaternion.Euler(leftHandRotationOffsetEuler),
            invertLeftHandAimDirection,
            invertLeftHandRoll);
        rightHandBinding = CreateHandBinding(
            HumanBodyBones.RightHand,
            HumanBodyBones.RightMiddleProximal,
            HumanBodyBones.RightThumbProximal,
            Quaternion.Euler(rightHandRotationOffsetEuler),
            invertRightHandAimDirection,
            invertRightHandRoll);
    }

    private bool BuildNormalizedPose(PoseFrame frame)
    {
        rawJoints.Clear();
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

            if (mirrorX)
            {
                worldPoint.x *= -1f;
            }

            rawJoints[jointId] = worldPoint;

            Vector3 normalized = (worldPoint - hipCenter) / referenceScale;
            normalizedJoints[jointId] = normalized;
        }

        return true;
    }

    private void ApplyRootYaw()
    {
        if (!driveRootYaw || avatarRoot == null)
        {
            return;
        }

        if (TryGetFacingDirection3D(out Vector3 facingDirection))
        {
            if (referenceFacingDirection == Vector3.zero)
            {
                referenceFacingDirection = facingDirection;
            }

            float targetYaw = Vector3.SignedAngle(referenceFacingDirection, facingDirection, Vector3.up);
            targetYaw = Mathf.Clamp(targetYaw, -maxRootYawDegrees, maxRootYawDegrees);
            if (invertRootYaw)
            {
                targetYaw *= -1f;
            }

            Quaternion targetRotation = avatarRootInitialRotation * Quaternion.AngleAxis(targetYaw, Vector3.up);
            float directBlend = 1f - Mathf.Exp(-rootYawResponsiveness * Time.deltaTime);
            avatarRoot.rotation = Quaternion.Slerp(avatarRoot.rotation, targetRotation, directBlend);
            return;
        }

        if (!TryGetRawJoint(PoseJointId.LeftShoulder, out Vector3 leftShoulder) ||
            !TryGetRawJoint(PoseJointId.RightShoulder, out Vector3 rightShoulder) ||
            !TryGetRawJoint(PoseJointId.LeftHip, out Vector3 leftHip) ||
            !TryGetRawJoint(PoseJointId.RightHip, out Vector3 rightHip) ||
            !TryGetFacingAnchor(out Vector3 facingAnchor))
        {
            return;
        }

        float shoulderWidth = Mathf.Abs(rightShoulder.x - leftShoulder.x);
        float hipWidth = Mathf.Abs(rightHip.x - leftHip.x);
        if (shoulderWidth < 0.01f || hipWidth < 0.01f)
        {
            return;
        }

        referenceShoulderWidth = Mathf.Max(referenceShoulderWidth, shoulderWidth);
        referenceHipWidth = Mathf.Max(referenceHipWidth, hipWidth);
        if (referenceShoulderWidth < 0.01f || referenceHipWidth < 0.01f)
        {
            return;
        }

        float shoulderRatio = Mathf.Clamp01(shoulderWidth / referenceShoulderWidth);
        float hipRatio = Mathf.Clamp01(hipWidth / referenceHipWidth);
        float widthRatio = Mathf.Min(shoulderRatio, hipRatio);
        float agreement = 1f - Mathf.Clamp01(Mathf.Abs(shoulderRatio - hipRatio) * 2.5f);
        float unsignedYaw = Mathf.Acos(widthRatio) * Mathf.Rad2Deg * agreement;
        if (unsignedYaw < 1f)
        {
            unsignedYaw = 0f;
        }

        Vector3 shoulderCenter = (leftShoulder + rightShoulder) * 0.5f;
        float signedFacingOffset = (facingAnchor.x - shoulderCenter.x) / Mathf.Max(shoulderWidth * 0.5f, 0.001f);
        float yawSign = Mathf.Abs(signedFacingOffset) > 0.08f ? Mathf.Sign(signedFacingOffset) : 0f;
        float fallbackTargetYaw = Mathf.Clamp(unsignedYaw, 0f, maxRootYawDegrees) * yawSign;
        if (invertRootYaw)
        {
            fallbackTargetYaw *= -1f;
        }

        Quaternion fallbackTargetRotation = avatarRootInitialRotation * Quaternion.AngleAxis(fallbackTargetYaw, Vector3.up);
        float blend = 1f - Mathf.Exp(-rootYawResponsiveness * Time.deltaTime);
        avatarRoot.rotation = Quaternion.Slerp(avatarRoot.rotation, fallbackTargetRotation, blend);
    }

    private bool TryGetFacingDirection3D(out Vector3 facingDirection)
    {
        facingDirection = default;

        if (!TryGetRawJoint(PoseJointId.LeftShoulder, out Vector3 leftShoulder) ||
            !TryGetRawJoint(PoseJointId.RightShoulder, out Vector3 rightShoulder) ||
            !TryGetAverageRawJoint(PoseJointId.LeftHip, PoseJointId.RightHip, out Vector3 hipCenter))
        {
            return false;
        }

        // Only trust the 3D cross-product method if there is meaningful depth
        // variation. 2D webcam sources produce near-zero or noisy Z values which
        // make the cross product unreliable — fall through to the 2D shoulder-
        // width compression fallback instead.
        float shoulderSpanXY = new Vector2(rightShoulder.x - leftShoulder.x, rightShoulder.y - leftShoulder.y).magnitude;
        float depthSpread = Mathf.Abs(rightShoulder.z - leftShoulder.z) +
                            Mathf.Abs(((leftShoulder.z + rightShoulder.z) * 0.5f) - hipCenter.z);
        if (shoulderSpanXY > 0.001f && depthSpread / shoulderSpanXY < 0.05f)
        {
            return false;
        }

        Vector3 shoulderCenter = (leftShoulder + rightShoulder) * 0.5f;
        Vector3 across = rightShoulder - leftShoulder;
        Vector3 up = shoulderCenter - hipCenter;
        Vector3 forward = Vector3.Cross(across, up);
        Vector3 horizontal = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (horizontal.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        facingDirection = horizontal.normalized;
        return true;
    }

    private void ApplyHipHeight()
    {
        if (!driveHipHeight || hipsBone == null)
        {
            return;
        }

        if (!TryGetAverageRawJoint(PoseJointId.LeftHip, PoseJointId.RightHip, out Vector3 hipCenter) ||
            !TryGetAverageRawJoint(PoseJointId.LeftAnkle, PoseJointId.RightAnkle, out Vector3 ankleCenter))
        {
            return;
        }

        float legExtension = Mathf.Abs(hipCenter.y - ankleCenter.y);
        if (legExtension < 0.01f)
        {
            return;
        }

        referenceLegExtension = Mathf.Max(referenceLegExtension, legExtension);
        if (referenceLegExtension < 0.01f)
        {
            return;
        }

        float extensionRatio = Mathf.Clamp01(legExtension / referenceLegExtension);
        float crouchAmount = 1f - extensionRatio;
        Vector3 targetLocalPosition = hipsInitialLocalPosition + (Vector3.down * (crouchAmount * maxHipDrop));
        float blend = 1f - Mathf.Exp(-hipHeightResponsiveness * Time.deltaTime);
        hipsBone.localPosition = Vector3.Lerp(hipsBone.localPosition, targetLocalPosition, blend);
    }

    private void ApplyRootPosition()
    {
        if (!driveRootPosition || avatarRoot == null)
        {
            return;
        }

        if (!TryGetAverageRawJoint(PoseJointId.LeftHip, PoseJointId.RightHip, out Vector3 hipCenter))
        {
            return;
        }

        bool hasFootCenter = TryGetAverageRawJoint(PoseJointId.LeftAnkle, PoseJointId.RightAnkle, out Vector3 footCenter);
        if (!hasRootPositionReference)
        {
            referenceHipCenter = hipCenter;
            referenceFootHeight = hasFootCenter ? footCenter.y : hipCenter.y;
            hasRootPositionReference = true;
        }

        Vector3 hipDelta = hipCenter - referenceHipCenter;
        Vector3 horizontalOffset = (avatarRootInitialRight * (hipDelta.x * rootPositionScale.x)) +
                                   (avatarRootInitialForward * (hipDelta.z * rootPositionScale.z));

        float targetY = avatarRootInitialPosition.y;
        if (driveRootHeight)
        {
            targetY += hipDelta.y * rootPositionScale.y;
        }

        if (keepFeetGrounded && hasFootCenter)
        {
            float footHeightDelta = Mathf.Clamp((footCenter.y - referenceFootHeight) * rootPositionScale.y, -maxFootHeightOffset, maxFootHeightOffset);
            float floorHeight = avatarRootInitialPosition.y + footHeightDelta;
            targetY = Mathf.Max(targetY, floorHeight);
        }

        Vector3 targetPosition = new Vector3(
            avatarRootInitialPosition.x + horizontalOffset.x,
            targetY,
            avatarRootInitialPosition.z + horizontalOffset.z);

        float blend = 1f - Mathf.Exp(-rootPositionResponsiveness * Time.deltaTime);
        avatarRoot.position = Vector3.Lerp(avatarRoot.position, targetPosition, blend);
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

            if (binding.invertDirection)
            {
                targetDirection = -targetDirection;
            }

            Quaternion delta = Quaternion.FromToRotation(binding.restDirectionRootSpace, targetDirection);
            Quaternion targetRotation = ToRootSpaceRotation(delta * binding.initialWorldRotationInRootSpace * binding.rotationOffsetInRootSpace);
            binding.bone.rotation = Quaternion.Slerp(binding.bone.rotation, targetRotation, blend);
        }
    }

    private void ApplyHandRotations()
    {
        if (!driveHandRotations)
        {
            return;
        }

        float blend = 1f - Mathf.Exp(-rotationResponsiveness * Time.deltaTime);
        ApplyHandRotation(leftHandBinding, PoseJointId.LeftWrist, PoseJointId.LeftHandTip, PoseJointId.LeftThumb, blend);
        ApplyHandRotation(rightHandBinding, PoseJointId.RightWrist, PoseJointId.RightHandTip, PoseJointId.RightThumb, blend);
    }

    private void ApplyHandRotation(HandBinding binding, PoseJointId wristJoint, PoseJointId handTipJoint, PoseJointId thumbJoint, float blend)
    {
        if (!binding.isValid || binding.bone == null)
        {
            return;
        }

        if (!TryGetNormalizedJoint(wristJoint, out Vector3 wrist) ||
            !TryGetNormalizedJoint(handTipJoint, out Vector3 handTip))
        {
            return;
        }

        Vector3 forward = handTip - wrist;
        if (forward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        if (binding.invertAimDirection)
        {
            forward = -forward;
        }

        Vector3 up = binding.restUpRootSpace;
        if (TryGetNormalizedJoint(thumbJoint, out Vector3 thumb))
        {
            Vector3 thumbDirection = Vector3.ProjectOnPlane(thumb - wrist, forward);
            if (thumbDirection.sqrMagnitude > 0.0001f)
            {
                up = thumbDirection.normalized;
            }
        }

        if (binding.invertRoll)
        {
            up = -up;
        }

        Quaternion targetBasis = CreateBasisRotation(forward, up);
        Quaternion delta = targetBasis * Quaternion.Inverse(binding.restBasisInRootSpace);
        Quaternion targetRotation = ToRootSpaceRotation(delta * binding.initialWorldRotationInRootSpace * binding.rotationOffsetInRootSpace);
        binding.bone.rotation = Quaternion.Slerp(binding.bone.rotation, targetRotation, blend);
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
        bool fallbackToSelf = false,
        bool driveIfEnabled = true,
        BindingGroup group = BindingGroup.Other)
    {
        if (!driveIfEnabled)
        {
            return;
        }

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

        Vector3 restDirection = GetRestDirectionRootSpace(bone, childBone, GetFallbackDirection(group, endJoint));

        Quaternion inverseRoot = Quaternion.Inverse(avatarRoot != null ? avatarRoot.rotation : Quaternion.identity);
        boneBindings.Add(new BoneBinding
        {
            bone = bone,
            startJoint = startJoint,
            endJoint = endJoint,
            restDirectionRootSpace = restDirection,
            invertDirection = ShouldInvertDirection(group),
            rotationOffsetInRootSpace = Quaternion.Euler(GetRotationOffset(group)),
            initialWorldRotationInRootSpace = inverseRoot * bone.rotation,
        });
    }

    private Vector3 GetFallbackDirection(BindingGroup group, PoseJointId endJoint)
    {
        if (endJoint == PoseJointId.Head || endJoint == PoseJointId.Nose)
        {
            return Vector3.up;
        }

        return group switch
        {
            BindingGroup.Legs => Vector3.down,
            BindingGroup.Head => Vector3.up,
            _ => Vector3.right,
        };
    }

    private bool ShouldInvertDirection(BindingGroup group)
    {
        return group switch
        {
            BindingGroup.Arms => invertArmDirections,
            BindingGroup.Legs => invertLegDirections,
            BindingGroup.Head => invertHeadDirection,
            _ => false,
        };
    }

    private Vector3 GetRotationOffset(BindingGroup group)
    {
        return group switch
        {
            BindingGroup.Arms => armRotationOffsetEuler,
            BindingGroup.Legs => legRotationOffsetEuler,
            BindingGroup.Head => headRotationOffsetEuler,
            _ => Vector3.zero,
        };
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

    private HandBinding CreateHandBinding(
        HumanBodyBones handBoneId,
        HumanBodyBones fingerBoneId,
        HumanBodyBones thumbBoneId,
        Quaternion rotationOffset,
        bool invertAimDirection,
        bool invertRoll)
    {
        Transform handBone = animator.GetBoneTransform(handBoneId);
        if (handBone == null)
        {
            return default;
        }

        Transform fingerBone = animator.GetBoneTransform(fingerBoneId);
        Transform thumbBone = animator.GetBoneTransform(thumbBoneId);
        Quaternion inverseRoot = Quaternion.Inverse(avatarRoot != null ? avatarRoot.rotation : Quaternion.identity);

        Vector3 restForward = GetRestDirectionRootSpace(handBone, fingerBone, Vector3.right);
        Vector3 restUp = thumbBone != null
            ? inverseRoot * (thumbBone.position - handBone.position)
            : inverseRoot * handBone.TransformDirection(Vector3.up);

        if (restUp.sqrMagnitude < 0.0001f)
        {
            restUp = Vector3.up;
        }

        return new HandBinding
        {
            bone = handBone,
            isValid = true,
            invertAimDirection = invertAimDirection,
            invertRoll = invertRoll,
            restUpRootSpace = restUp.normalized,
            restBasisInRootSpace = CreateBasisRotation(restForward, restUp),
            initialWorldRotationInRootSpace = inverseRoot * handBone.rotation,
            rotationOffsetInRootSpace = rotationOffset,
        };
    }

    private static Quaternion CreateBasisRotation(Vector3 forward, Vector3 up)
    {
        Vector3 normalizedForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 projectedUp = Vector3.ProjectOnPlane(up, normalizedForward);
        if (projectedUp.sqrMagnitude < 0.0001f)
        {
            projectedUp = Mathf.Abs(Vector3.Dot(normalizedForward, Vector3.up)) > 0.98f ? Vector3.right : Vector3.up;
            projectedUp = Vector3.ProjectOnPlane(projectedUp, normalizedForward);
        }

        return Quaternion.LookRotation(normalizedForward, projectedUp.normalized);
    }

    private bool TryGetNormalizedJoint(PoseJointId jointId, out Vector3 position)
    {
        return normalizedJoints.TryGetValue(jointId, out position);
    }

    private bool TryGetRawJoint(PoseJointId jointId, out Vector3 position)
    {
        return rawJoints.TryGetValue(jointId, out position);
    }

    private bool TryGetFacingAnchor(out Vector3 position)
    {
        if (TryGetRawJoint(PoseJointId.Nose, out position))
        {
            return true;
        }

        if (TryGetAverageRawJoint(PoseJointId.LeftEye, PoseJointId.RightEye, out position))
        {
            return true;
        }

        if (TryGetAverageRawJoint(PoseJointId.LeftEar, PoseJointId.RightEar, out position))
        {
            return true;
        }

        return false;
    }

    private bool TryGetAverageRawJoint(PoseJointId first, PoseJointId second, out Vector3 position)
    {
        bool hasFirst = TryGetRawJoint(first, out Vector3 a);
        bool hasSecond = TryGetRawJoint(second, out Vector3 b);

        if (hasFirst && hasSecond)
        {
            position = (a + b) * 0.5f;
            return true;
        }

        if (hasFirst)
        {
            position = a;
            return true;
        }

        if (hasSecond)
        {
            position = b;
            return true;
        }

        position = default;
        return false;
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
        public bool invertDirection;
        public Quaternion rotationOffsetInRootSpace;
        public Quaternion initialWorldRotationInRootSpace;
    }

    private struct HandBinding
    {
        public Transform bone;
        public bool isValid;
        public bool invertAimDirection;
        public bool invertRoll;
        public Vector3 restUpRootSpace;
        public Quaternion restBasisInRootSpace;
        public Quaternion initialWorldRotationInRootSpace;
        public Quaternion rotationOffsetInRootSpace;
    }

    private enum BindingGroup
    {
        Other,
        Arms,
        Legs,
        Head,
    }
}