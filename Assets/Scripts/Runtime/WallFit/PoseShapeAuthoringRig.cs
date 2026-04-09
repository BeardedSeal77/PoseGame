using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class PoseShapeAuthoringRig : MonoBehaviour
{
    [Serializable]
    private struct BoneLocalPose
    {
        public string relativePath;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    public enum JointHandleId
    {
        Hips,
        Chest,
        LeftHand,
        RightHand,
        LeftFoot,
        RightFoot,
    }

    [Header("References")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private Camera authoringCamera;
    [SerializeField] private PolygonWallShapeAsset shapeAsset;
    [SerializeField, HideInInspector] private Animator referencePoseAnimator;

    [Header("Authoring")]
    [SerializeField] private bool autoSolveInEditMode = true;
    [SerializeField, Min(1)] private int ikIterations = 8;
    [SerializeField, Min(0.001f)] private float silhouettePadding = 0.035f;
    [SerializeField, Min(0.05f)] private float shrinkDuration = 2.5f;
    [SerializeField, Min(0f)] private float jointHandleVisualOffset = 0.12f;

    [Header("Targets")]
    [SerializeField] private Transform hipsTarget;
    [SerializeField] private Transform chestTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform leftFootTarget;
    [SerializeField] private Transform rightFootTarget;
    [SerializeField, HideInInspector] private List<BoneLocalPose> importedBoneLocalPoses = new List<BoneLocalPose>();
    [SerializeField] private List<WallOrbTargetData> orbTargets = new List<WallOrbTargetData>();

    [SerializeField] private List<Vector2> polygonVertices = new List<Vector2>
    {
        new Vector2(0.36f, 0.12f),
        new Vector2(0.64f, 0.12f),
        new Vector2(0.64f, 0.88f),
        new Vector2(0.36f, 0.88f),
    };

    public Animator TargetAnimator => targetAnimator;
    public Camera AuthoringCamera => authoringCamera;
    public PolygonWallShapeAsset ShapeAsset => shapeAsset;
    public IReadOnlyList<Vector2> PolygonVertices => polygonVertices;
    public IReadOnlyList<WallOrbTargetData> OrbTargets => orbTargets;
    public float ShrinkDuration => shrinkDuration;
    public float SilhouettePadding => silhouettePadding;
    public float JointHandleVisualOffset => jointHandleVisualOffset;

    private void Reset()
    {
        targetAnimator = GetComponentInChildren<Animator>();
        authoringCamera = Camera.main;
    }

    private void OnEnable()
    {
        EnsureDefaults();
    }

    private void OnValidate()
    {
        EnsureDefaults();
    }

    private void LateUpdate()
    {
        if (Application.isPlaying || !autoSolveInEditMode || targetAnimator == null || !targetAnimator.isHuman)
        {
            return;
        }

        SolveTargets();
    }

    public void AssignShapeAsset(PolygonWallShapeAsset asset)
    {
        shapeAsset = asset;
    }

    public void AssignReferencePoseAnimator(Animator animator)
    {
        referencePoseAnimator = animator;
    }

    public void LoadFromShapeAsset()
    {
        if (shapeAsset == null)
        {
            return;
        }

        polygonVertices.Clear();
        IReadOnlyList<Vector2> sourceVertices = shapeAsset.Vertices;
        for (int index = 0; index < sourceVertices.Count; index++)
        {
            polygonVertices.Add(ClampViewportPoint(sourceVertices[index]));
        }

        orbTargets.Clear();
        IReadOnlyList<WallOrbTargetData> sourceOrbTargets = shapeAsset.OrbTargets;
        for (int index = 0; index < sourceOrbTargets.Count; index++)
        {
            WallOrbTargetData orbTarget = sourceOrbTargets[index];
            orbTarget.viewportPosition = ClampViewportPoint(orbTarget.viewportPosition);
            orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.01f, 0.2f);
            orbTargets.Add(orbTarget);
        }

        shrinkDuration = shapeAsset.ShrinkDuration;
        EnsureMinimumVertexCount();
    }

    public void SaveToShapeAsset()
    {
        if (shapeAsset == null)
        {
            return;
        }

        Rect? referenceBounds = TryGetAvatarViewportBounds(out Rect viewportBounds) ? viewportBounds : null;
        shapeAsset.SetData(polygonVertices, shrinkDuration, orbTargets, referenceBounds);
    }

    public void EnsureTargetHandlesCreated()
    {
        Transform targetRoot = FindOrCreateTargetRoot();

        hipsTarget = FindOrCreateTarget(targetRoot, hipsTarget, "Hips Target");
        chestTarget = FindOrCreateTarget(targetRoot, chestTarget, "Chest Target");
        leftHandTarget = FindOrCreateTarget(targetRoot, leftHandTarget, "Left Hand Target");
        rightHandTarget = FindOrCreateTarget(targetRoot, rightHandTarget, "Right Hand Target");
        leftFootTarget = FindOrCreateTarget(targetRoot, leftFootTarget, "Left Foot Target");
        rightFootTarget = FindOrCreateTarget(targetRoot, rightFootTarget, "Right Foot Target");

        ResetTargetsFromCurrentPose();
        HideTargetObjects();
    }

    public void ResetTargetsFromCurrentPose()
    {
        if (targetAnimator == null || !targetAnimator.isHuman)
        {
            return;
        }

        SetTargetPosition(hipsTarget, HumanBodyBones.Hips);
        SetChestTargetPositionFromCurrentPose();
        SetTargetPosition(leftHandTarget, HumanBodyBones.LeftHand);
        SetTargetPosition(rightHandTarget, HumanBodyBones.RightHand);
        SetTargetPosition(leftFootTarget, HumanBodyBones.LeftFoot);
        SetTargetPosition(rightFootTarget, HumanBodyBones.RightFoot);
    }

    public void ResetPoseToTPose()
    {
        ResetPoseToImportedPose();
    }

    public void CaptureImportedPose()
    {
        importedBoneLocalPoses.Clear();

        Animator sourceAnimator = referencePoseAnimator != null ? referencePoseAnimator : targetAnimator;
        if (sourceAnimator == null)
        {
            return;
        }

        Transform sourceRoot = sourceAnimator.transform;
        Transform[] bones = sourceRoot.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < bones.Length; index++)
        {
            Transform bone = bones[index];
            importedBoneLocalPoses.Add(new BoneLocalPose
            {
                relativePath = GetRelativePath(sourceRoot, bone),
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale,
            });
        }
    }

    public void ResetPoseToImportedPose()
    {
        if (targetAnimator == null || !targetAnimator.isHuman)
        {
            return;
        }

        if (importedBoneLocalPoses == null || importedBoneLocalPoses.Count == 0)
        {
            CaptureImportedPose();
        }

        for (int index = 0; index < importedBoneLocalPoses.Count; index++)
        {
            BoneLocalPose pose = importedBoneLocalPoses[index];
            Transform bone = FindRelativeTransform(targetAnimator.transform, pose.relativePath);
            if (bone == null)
            {
                continue;
            }

            bone.localPosition = pose.localPosition;
            bone.localRotation = pose.localRotation;
            bone.localScale = pose.localScale;
        }

        if (TryGetPlayerRoot(out Transform playerRoot))
        {
            Vector3 localEuler = playerRoot.localEulerAngles;
            playerRoot.localRotation = Quaternion.Euler(localEuler.x, 180f, localEuler.z);
        }

        ResetTargetsFromCurrentPose();
    }

    public void ResetShapeToRectangle()
    {
        if (TryGetAvatarViewportBounds(out Rect viewportBounds))
        {
            polygonVertices.Clear();
            polygonVertices.Add(new Vector2(viewportBounds.xMin, viewportBounds.yMin));
            polygonVertices.Add(new Vector2(viewportBounds.xMax, viewportBounds.yMin));
            polygonVertices.Add(new Vector2(viewportBounds.xMax, viewportBounds.yMax));
            polygonVertices.Add(new Vector2(viewportBounds.xMin, viewportBounds.yMax));
            EnsureMinimumVertexCount();
            return;
        }

        polygonVertices.Clear();
        polygonVertices.Add(new Vector2(0.36f, 0.12f));
        polygonVertices.Add(new Vector2(0.64f, 0.12f));
        polygonVertices.Add(new Vector2(0.64f, 0.88f));
        polygonVertices.Add(new Vector2(0.36f, 0.88f));
        EnsureMinimumVertexCount();
    }

    public void FitPolygonToAvatar()
    {
        if (!TryBuildAvatarOutline(out List<Vector2> outline))
        {
            return;
        }

        polygonVertices.Clear();
        for (int index = 0; index < outline.Count; index++)
        {
            polygonVertices.Add(ClampViewportPoint(outline[index]));
        }

        EnsureMinimumVertexCount();
    }

    public int AddVertex(Vector2 viewportPoint)
    {
        polygonVertices.Add(ClampViewportPoint(viewportPoint));
        EnsureMinimumVertexCount();
        return polygonVertices.Count - 1;
    }

    public int InsertVertex(int index, Vector2 viewportPoint)
    {
        int safeIndex = Mathf.Clamp(index, 0, polygonVertices.Count);
        polygonVertices.Insert(safeIndex, ClampViewportPoint(viewportPoint));
        EnsureMinimumVertexCount();
        return safeIndex;
    }

    public void RemoveVertexAt(int index)
    {
        if (polygonVertices.Count <= 3 || index < 0 || index >= polygonVertices.Count)
        {
            return;
        }

        polygonVertices.RemoveAt(index);
    }

    public void SetVertex(int index, Vector2 viewportPoint)
    {
        if (index < 0 || index >= polygonVertices.Count)
        {
            return;
        }

        polygonVertices[index] = ClampViewportPoint(viewportPoint);
    }

    public bool TryViewportToWorld(Vector2 viewportPoint, out Vector3 worldPoint)
    {
        if (authoringCamera == null)
        {
            worldPoint = default;
            return false;
        }

        float depth = GetAuthoringDepth();
        worldPoint = authoringCamera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, depth));
        return true;
    }

    public bool TryGetOrbWorldPosition(int index, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (index < 0 || index >= orbTargets.Count)
        {
            return false;
        }

        return TryViewportToWorld(orbTargets[index].viewportPosition, out worldPosition);
    }

    public float GetOrbWorldRadius(int index)
    {
        if (authoringCamera == null || index < 0 || index >= orbTargets.Count)
        {
            return 0.05f;
        }

        WallOrbTargetData orbTarget = orbTargets[index];
        if (!TryViewportToWorld(orbTarget.viewportPosition, out Vector3 centerWorld) ||
            !TryViewportToWorld(ClampViewportPoint(orbTarget.viewportPosition + new Vector2(orbTarget.radius, 0f)), out Vector3 edgeWorld))
        {
            return 0.05f;
        }

        return Mathf.Max(0.02f, Vector3.Distance(centerWorld, edgeWorld));
    }

    public int AddOrbTarget(Vector2 viewportPoint, float radius = 0.05f)
    {
        orbTargets.Add(new WallOrbTargetData(ClampViewportPoint(viewportPoint), Mathf.Clamp(radius, 0.01f, 0.2f)));
        return orbTargets.Count - 1;
    }

    public void SetOrbTargetPosition(int index, Vector2 viewportPoint)
    {
        if (index < 0 || index >= orbTargets.Count)
        {
            return;
        }

        WallOrbTargetData orbTarget = orbTargets[index];
        orbTarget.viewportPosition = ClampViewportPoint(viewportPoint);
        orbTargets[index] = orbTarget;
    }

    public void SetOrbTargetRadius(int index, float radius)
    {
        if (index < 0 || index >= orbTargets.Count)
        {
            return;
        }

        WallOrbTargetData orbTarget = orbTargets[index];
        orbTarget.radius = Mathf.Clamp(radius, 0.01f, 0.2f);
        orbTargets[index] = orbTarget;
    }

    public void ClearOrbTargets()
    {
        orbTargets.Clear();
    }

    public void RemoveOrbTargetAt(int index)
    {
        if (index < 0 || index >= orbTargets.Count)
        {
            return;
        }

        orbTargets.RemoveAt(index);
    }

    public bool TryWorldToViewport(Vector3 worldPoint, out Vector2 viewportPoint)
    {
        if (authoringCamera == null)
        {
            viewportPoint = default;
            return false;
        }

        Vector3 projected = authoringCamera.WorldToViewportPoint(worldPoint);
        viewportPoint = ClampViewportPoint(new Vector2(projected.x, projected.y));
        return projected.z > 0f;
    }

    public bool TryProjectSceneRay(Ray ray, out Vector2 viewportPoint)
    {
        viewportPoint = default;

        if (authoringCamera == null)
        {
            return false;
        }

        Vector3 planePoint = GetAuthoringPlanePoint();
        Plane plane = new Plane(authoringCamera.transform.forward, planePoint);
        if (!plane.Raycast(ray, out float distance))
        {
            return false;
        }

        return TryWorldToViewport(ray.GetPoint(distance), out viewportPoint);
    }

    public void SetShrinkDuration(float duration)
    {
        shrinkDuration = Mathf.Max(0.05f, duration);
    }

    public bool TryGetJointHandlePosition(JointHandleId jointHandleId, out Vector3 worldPosition)
    {
        worldPosition = jointHandleId switch
        {
            JointHandleId.Hips => hipsTarget != null ? hipsTarget.position : default,
            JointHandleId.Chest => chestTarget != null ? chestTarget.position : default,
            JointHandleId.LeftHand => leftHandTarget != null ? leftHandTarget.position : default,
            JointHandleId.RightHand => rightHandTarget != null ? rightHandTarget.position : default,
            JointHandleId.LeftFoot => leftFootTarget != null ? leftFootTarget.position : default,
            JointHandleId.RightFoot => rightFootTarget != null ? rightFootTarget.position : default,
            _ => default,
        };

        return jointHandleId switch
        {
            JointHandleId.Hips => hipsTarget != null,
            JointHandleId.Chest => chestTarget != null,
            JointHandleId.LeftHand => leftHandTarget != null,
            JointHandleId.RightHand => rightHandTarget != null,
            JointHandleId.LeftFoot => leftFootTarget != null,
            JointHandleId.RightFoot => rightFootTarget != null,
            _ => false,
        };
    }

    public Vector3 GetVisualHandlePosition(Vector3 worldPosition)
    {
        if (authoringCamera == null || jointHandleVisualOffset <= 0f)
        {
            return worldPosition;
        }

        return worldPosition - (authoringCamera.transform.forward * jointHandleVisualOffset);
    }

    public void SetJointHandlePosition(JointHandleId jointHandleId, Vector3 displayWorldPosition)
    {
        Vector3 targetWorldPosition = displayWorldPosition;
        if (authoringCamera != null && jointHandleVisualOffset > 0f)
        {
            targetWorldPosition += authoringCamera.transform.forward * jointHandleVisualOffset;
        }

        switch (jointHandleId)
        {
            case JointHandleId.Hips:
                if (hipsTarget != null)
                {
                    Vector3 delta = targetWorldPosition - hipsTarget.position;
                    ApplyTargetDelta(delta);
                }
                break;
            case JointHandleId.Chest:
                if (chestTarget != null)
                {
                    chestTarget.position = targetWorldPosition;
                }
                break;
            case JointHandleId.LeftHand:
                SetConstrainedTarget(leftHandTarget, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, targetWorldPosition);
                break;
            case JointHandleId.RightHand:
                SetConstrainedTarget(rightHandTarget, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, targetWorldPosition);
                break;
            case JointHandleId.LeftFoot:
                SetConstrainedTarget(leftFootTarget, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, targetWorldPosition);
                break;
            case JointHandleId.RightFoot:
                SetConstrainedTarget(rightFootTarget, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, targetWorldPosition);
                break;
        }
    }

    public void RotatePlayerYaw(float deltaDegrees)
    {
        if (Mathf.Abs(deltaDegrees) < 0.0001f || !TryGetPlayerRoot(out Transform playerRoot))
        {
            return;
        }

        Vector3 pivot = hipsTarget != null ? hipsTarget.position : playerRoot.position;
        Quaternion deltaRotation = Quaternion.AngleAxis(deltaDegrees, Vector3.up);

        playerRoot.RotateAround(pivot, Vector3.up, deltaDegrees);
        RotateTargetAroundPivot(hipsTarget, pivot, deltaRotation);
        RotateTargetAroundPivot(chestTarget, pivot, deltaRotation);
        RotateTargetAroundPivot(leftHandTarget, pivot, deltaRotation);
        RotateTargetAroundPivot(rightHandTarget, pivot, deltaRotation);
        RotateTargetAroundPivot(leftFootTarget, pivot, deltaRotation);
        RotateTargetAroundPivot(rightFootTarget, pivot, deltaRotation);
    }

    public void SetPlayerYaw(float yawDegrees)
    {
        float currentYaw = GetPlayerYaw();
        RotatePlayerYaw(Mathf.DeltaAngle(currentYaw, yawDegrees));
    }

    public float GetPlayerYaw()
    {
        if (!TryGetPlayerRoot(out Transform playerRoot))
        {
            return 0f;
        }

        return NormalizeAngle(playerRoot.localEulerAngles.y);
    }

    public bool TryGetPlayerRoot(out Transform playerRoot)
    {
        playerRoot = targetAnimator != null ? targetAnimator.transform.parent : null;
        return playerRoot != null;
    }

    private void EnsureDefaults()
    {
        shrinkDuration = Mathf.Max(0.05f, shrinkDuration);
        silhouettePadding = Mathf.Max(0.001f, silhouettePadding);
        jointHandleVisualOffset = Mathf.Max(0f, jointHandleVisualOffset);
        ikIterations = Mathf.Max(1, ikIterations);
        EnsureMinimumVertexCount();

        if (orbTargets == null)
        {
            orbTargets = new List<WallOrbTargetData>();
        }

        for (int index = 0; index < orbTargets.Count; index++)
        {
            WallOrbTargetData orbTarget = orbTargets[index];
            orbTarget.viewportPosition = ClampViewportPoint(orbTarget.viewportPosition);
            orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.01f, 0.2f);
            orbTargets[index] = orbTarget;
        }
    }

    private void EnsureMinimumVertexCount()
    {
        if (polygonVertices == null)
        {
            polygonVertices = new List<Vector2>();
        }

        while (polygonVertices.Count < 3)
        {
            polygonVertices.Add(new Vector2(0.35f + (polygonVertices.Count * 0.15f), 0.2f + (polygonVertices.Count * 0.25f)));
        }

        for (int index = 0; index < polygonVertices.Count; index++)
        {
            polygonVertices[index] = ClampViewportPoint(polygonVertices[index]);
        }
    }

    private void SolveTargets()
    {
        Transform hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null && hipsTarget != null)
        {
            hips.position = hipsTarget.position;
        }

        SolveTorso();
        SolveLimb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, leftHandTarget);
        SolveLimb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rightHandTarget);
        SolveLimb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, leftFootTarget);
        SolveLimb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rightFootTarget);
    }

    private void SolveTorso()
    {
        if (chestTarget == null)
        {
            return;
        }

        List<Transform> torsoBones = GetTorsoChain();
        if (torsoBones.Count == 0)
        {
            return;
        }

        for (int iteration = 0; iteration < ikIterations; iteration++)
        {
            for (int index = torsoBones.Count - 1; index >= 0; index--)
            {
                Vector3 currentAnchor = GetShoulderCenterWorldPosition();
                RotateJointTowardTarget(torsoBones[index], currentAnchor, chestTarget.position);
            }
        }
    }

    private void SolveLimb(HumanBodyBones upperBoneId, HumanBodyBones lowerBoneId, HumanBodyBones endBoneId, Transform target)
    {
        if (target == null)
        {
            return;
        }

        Transform upperBone = targetAnimator.GetBoneTransform(upperBoneId);
        Transform lowerBone = targetAnimator.GetBoneTransform(lowerBoneId);
        Transform endBone = targetAnimator.GetBoneTransform(endBoneId);
        if (upperBone == null || lowerBone == null || endBone == null)
        {
            return;
        }

        for (int iteration = 0; iteration < ikIterations; iteration++)
        {
            RotateJointTowardTarget(lowerBone, endBone.position, target.position);
            RotateJointTowardTarget(upperBone, endBone.position, target.position);
        }
    }

    private static void RotateJointTowardTarget(Transform joint, Vector3 endPosition, Vector3 targetPosition)
    {
        Vector3 toEnd = endPosition - joint.position;
        Vector3 toTarget = targetPosition - joint.position;
        if (toEnd.sqrMagnitude < 0.000001f || toTarget.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Quaternion rotationDelta = Quaternion.FromToRotation(toEnd, toTarget);
        joint.rotation = rotationDelta * joint.rotation;
    }

    private void SetTargetPosition(Transform target, HumanBodyBones boneId)
    {
        Transform bone = targetAnimator.GetBoneTransform(boneId);
        if (target == null || bone == null)
        {
            return;
        }

        target.position = bone.position;
        target.rotation = bone.rotation;
    }

    private Transform FindOrCreateTargetRoot()
    {
        Transform existing = transform.Find("Pose Authoring Targets");
        if (existing != null)
        {
            return existing;
        }

        GameObject targetRoot = new GameObject("Pose Authoring Targets");
        targetRoot.transform.SetParent(transform, false);
        return targetRoot.transform;
    }

    private static Transform FindOrCreateTarget(Transform parent, Transform currentTarget, string name)
    {
        if (currentTarget != null)
        {
            return currentTarget;
        }

        Transform existing = parent.Find(name);
        if (existing != null)
        {
            return existing;
        }

        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetObject.name = name;
        targetObject.transform.SetParent(parent, false);
        targetObject.transform.localScale = Vector3.one * 0.12f;

        Collider collider = targetObject.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }

        targetObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;

        return targetObject.transform;
    }

    private void HideTargetObjects()
    {
        HideTargetObject(hipsTarget);
        HideTargetObject(chestTarget);
        HideTargetObject(leftHandTarget);
        HideTargetObject(rightHandTarget);
        HideTargetObject(leftFootTarget);
        HideTargetObject(rightFootTarget);
    }

    private static void HideTargetObject(Transform target)
    {
        if (target == null)
        {
            return;
        }

        target.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;
    }

    private void SetChestTargetPositionFromCurrentPose()
    {
        if (chestTarget == null)
        {
            return;
        }

        chestTarget.position = GetShoulderCenterWorldPosition();
        chestTarget.rotation = Quaternion.identity;
    }

    private void ApplyTargetDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude < 0.000001f)
        {
            return;
        }

        OffsetTarget(hipsTarget, delta);
        OffsetTarget(chestTarget, delta);
        OffsetTarget(leftHandTarget, delta);
        OffsetTarget(rightHandTarget, delta);
        OffsetTarget(leftFootTarget, delta);
        OffsetTarget(rightFootTarget, delta);
    }

    private static void OffsetTarget(Transform target, Vector3 delta)
    {
        if (target != null)
        {
            target.position += delta;
        }
    }

    private static void RotateTargetAroundPivot(Transform target, Vector3 pivot, Quaternion deltaRotation)
    {
        if (target == null)
        {
            return;
        }

        Vector3 offset = target.position - pivot;
        target.position = pivot + (deltaRotation * offset);
        target.rotation = deltaRotation * target.rotation;
    }

    private List<Transform> GetTorsoChain()
    {
        List<Transform> torsoBones = new List<Transform>(3);
        AddUniqueBone(torsoBones, HumanBodyBones.Spine);
        AddUniqueBone(torsoBones, HumanBodyBones.Chest);
        AddUniqueBone(torsoBones, HumanBodyBones.UpperChest);
        return torsoBones;
    }

    private void AddUniqueBone(List<Transform> bones, HumanBodyBones boneId)
    {
        Transform bone = targetAnimator.GetBoneTransform(boneId);
        if (bone != null && !bones.Contains(bone))
        {
            bones.Add(bone);
        }
    }

    private Vector3 GetShoulderCenterWorldPosition()
    {
        Transform leftShoulder = GetShoulderBone(true);
        Transform rightShoulder = GetShoulderBone(false);
        if (leftShoulder != null && rightShoulder != null)
        {
            return Vector3.Lerp(leftShoulder.position, rightShoulder.position, 0.5f);
        }

        Transform chestBone = GetBestChestBone();
        if (chestBone != null)
        {
            return chestBone.position;
        }

        Transform hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
        return hips != null ? hips.position : transform.position;
    }

    private Transform GetShoulderBone(bool leftSide)
    {
        HumanBodyBones shoulderBone = leftSide ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
        Transform shoulder = targetAnimator.GetBoneTransform(shoulderBone);
        if (shoulder != null)
        {
            return shoulder;
        }

        return targetAnimator.GetBoneTransform(leftSide ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
    }

    private Transform GetBestChestBone()
    {
        Transform upperChest = targetAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (upperChest != null)
        {
            return upperChest;
        }

        Transform chest = targetAnimator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest != null)
        {
            return chest;
        }

        return targetAnimator.GetBoneTransform(HumanBodyBones.Spine);
    }

    private bool TryBuildAvatarOutline(out List<Vector2> outline)
    {
        outline = null;

        if (authoringCamera == null || targetAnimator == null || !targetAnimator.isHuman)
        {
            return false;
        }

        if (!TryGetViewportPoint(HumanBodyBones.Head, out Vector2 head) ||
            !TryGetViewportPoint(HumanBodyBones.Hips, out Vector2 hips) ||
            !TryGetViewportPoint(HumanBodyBones.LeftHand, out Vector2 leftHand) ||
            !TryGetViewportPoint(HumanBodyBones.RightHand, out Vector2 rightHand) ||
            !TryGetViewportPoint(HumanBodyBones.LeftFoot, out Vector2 leftFoot) ||
            !TryGetViewportPoint(HumanBodyBones.RightFoot, out Vector2 rightFoot))
        {
            return false;
        }

        Vector2 leftShoulder = GetViewportPointOrFallback(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm);
        Vector2 rightShoulder = GetViewportPointOrFallback(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm);
        Vector2 leftElbow = GetViewportPointOrFallback(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        Vector2 rightElbow = GetViewportPointOrFallback(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        Vector2 leftHip = GetViewportPointOrFallback(HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips);
        Vector2 rightHip = GetViewportPointOrFallback(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips);

        OrderByScreenX(ref leftShoulder, ref rightShoulder);
        OrderByScreenX(ref leftElbow, ref rightElbow);
        OrderByScreenX(ref leftHand, ref rightHand);
        OrderByScreenX(ref leftHip, ref rightHip);
        OrderByScreenX(ref leftFoot, ref rightFoot);

        float shoulderWidth = Mathf.Max(0.08f, Mathf.Abs(rightShoulder.x - leftShoulder.x));
        float sidePadding = Mathf.Max(silhouettePadding, shoulderWidth * 0.18f);
        float shoulderLift = Mathf.Max(silhouettePadding, shoulderWidth * 0.22f);
        float headLift = Mathf.Max(silhouettePadding * 1.5f, shoulderWidth * 0.28f);
        float handOut = Mathf.Max(silhouettePadding * 1.4f, shoulderWidth * 0.18f);
        float handDrop = Mathf.Max(silhouettePadding * 1.2f, shoulderWidth * 0.14f);
        float elbowOut = Mathf.Max(silhouettePadding, shoulderWidth * 0.1f);
        float hipOut = Mathf.Max(silhouettePadding, shoulderWidth * 0.16f);
        float footOut = Mathf.Max(silhouettePadding, shoulderWidth * 0.12f);
        float footDrop = Mathf.Max(silhouettePadding * 0.8f, shoulderWidth * 0.05f);
        float headHalfWidth = Mathf.Max(shoulderWidth * 0.22f, silhouettePadding * 1.2f);
        float shoulderY = Mathf.Max((leftShoulder.y + rightShoulder.y) * 0.5f + shoulderLift, hips.y + (silhouettePadding * 2f));
        float headY = Mathf.Max(head.y + headLift, shoulderY + (silhouettePadding * 1.5f));
        float hipY = Mathf.Min(hips.y - (sidePadding * 0.15f), shoulderY - (silhouettePadding * 2f));
        float leftHipX = Mathf.Min(leftHip.x, hips.x - hipOut);
        float rightHipX = Mathf.Max(rightHip.x, hips.x + hipOut);
        float leftShoulderX = Mathf.Min(leftShoulder.x, head.x - (headHalfWidth * 0.85f)) - sidePadding;
        float rightShoulderX = Mathf.Max(rightShoulder.x, head.x + (headHalfWidth * 0.85f)) + sidePadding;

        outline = new List<Vector2>
        {
            ClampViewportPoint(leftFoot + new Vector2(-footOut, -footDrop)),
            ClampViewportPoint(new Vector2(leftHipX, hipY)),
            ClampViewportPoint(leftElbow + new Vector2(-elbowOut, -handDrop * 0.35f)),
            ClampViewportPoint(leftHand + new Vector2(-handOut * 0.82f, -handDrop)),
            ClampViewportPoint(leftHand + new Vector2(-handOut, 0f)),
            ClampViewportPoint(leftElbow + new Vector2(-elbowOut, handDrop * 0.35f)),
            ClampViewportPoint(new Vector2(leftShoulderX, shoulderY)),
            ClampViewportPoint(new Vector2(head.x - headHalfWidth, headY)),
            ClampViewportPoint(new Vector2(head.x + headHalfWidth, headY)),
            ClampViewportPoint(new Vector2(rightShoulderX, shoulderY)),
            ClampViewportPoint(rightElbow + new Vector2(elbowOut, handDrop * 0.35f)),
            ClampViewportPoint(rightHand + new Vector2(handOut, 0f)),
            ClampViewportPoint(rightHand + new Vector2(handOut * 0.82f, -handDrop)),
            ClampViewportPoint(rightElbow + new Vector2(elbowOut, -handDrop * 0.35f)),
            ClampViewportPoint(new Vector2(rightHipX, hipY)),
            ClampViewportPoint(rightFoot + new Vector2(footOut, -footDrop)),
        };

        return true;
    }

    private bool TryGetAvatarViewportBounds(out Rect viewportBounds)
    {
        viewportBounds = default;

        if (authoringCamera == null || targetAnimator == null || !targetAnimator.isHuman)
        {
            return false;
        }

        List<Vector2> points = new List<Vector2>(10);
        AddViewportPoint(points, HumanBodyBones.Head);
        AddViewportPoint(points, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm);
        AddViewportPoint(points, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm);
        AddViewportPoint(points, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        AddViewportPoint(points, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        AddViewportPoint(points, HumanBodyBones.LeftHand);
        AddViewportPoint(points, HumanBodyBones.RightHand);
        AddViewportPoint(points, HumanBodyBones.Hips);
        AddViewportPoint(points, HumanBodyBones.LeftFoot);
        AddViewportPoint(points, HumanBodyBones.RightFoot);

        if (points.Count < 2)
        {
            return false;
        }

        Vector2 min = points[0];
        Vector2 max = points[0];
        for (int index = 1; index < points.Count; index++)
        {
            min = Vector2.Min(min, points[index]);
            max = Vector2.Max(max, points[index]);
        }

        Vector2 padding = new Vector2(
            Mathf.Max(silhouettePadding * 2f, (max.x - min.x) * 0.08f),
            Mathf.Max(silhouettePadding * 2f, (max.y - min.y) * 0.08f));

        min -= padding;
        max += padding;
        min = ClampViewportPoint(min);
        max = ClampViewportPoint(max);

        viewportBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return viewportBounds.width > 0.01f && viewportBounds.height > 0.01f;
    }

    private bool TryGetViewportPoint(HumanBodyBones boneId, out Vector2 viewportPoint)
    {
        Transform bone = targetAnimator.GetBoneTransform(boneId);
        if (bone == null)
        {
            viewportPoint = default;
            return false;
        }

        Vector3 viewport = authoringCamera.WorldToViewportPoint(bone.position);
        if (viewport.z <= 0f)
        {
            viewportPoint = default;
            return false;
        }

        viewportPoint = ClampViewportPoint(new Vector2(viewport.x, viewport.y));
        return true;
    }

    private void AddViewportPoint(List<Vector2> points, HumanBodyBones boneId)
    {
        if (TryGetViewportPoint(boneId, out Vector2 point))
        {
            points.Add(point);
        }
    }

    private void AddViewportPoint(List<Vector2> points, HumanBodyBones primaryBoneId, HumanBodyBones fallbackBoneId)
    {
        if (TryGetViewportPoint(primaryBoneId, out Vector2 point) || TryGetViewportPoint(fallbackBoneId, out point))
        {
            points.Add(point);
        }
    }

    private Vector2 GetViewportPointOrFallback(HumanBodyBones primaryBoneId, HumanBodyBones fallbackBoneId)
    {
        if (TryGetViewportPoint(primaryBoneId, out Vector2 point))
        {
            return point;
        }

        return TryGetViewportPoint(fallbackBoneId, out point) ? point : new Vector2(0.5f, 0.5f);
    }

    private float GetAuthoringDepth()
    {
        if (authoringCamera == null)
        {
            return 5f;
        }

        Vector3 anchor = GetAuthoringPlanePoint();
        float depth = Vector3.Dot(anchor - authoringCamera.transform.position, authoringCamera.transform.forward);
        return Mathf.Max(authoringCamera.nearClipPlane + 0.5f, depth);
    }

    private Vector3 GetAuthoringPlanePoint()
    {
        if (targetAnimator != null)
        {
            return targetAnimator.transform.position;
        }

        return transform.position;
    }

    private Vector3 ProjectOntoAuthoringPlane(Vector3 worldPosition)
    {
        if (authoringCamera == null)
        {
            return worldPosition;
        }

        Vector3 planePoint = GetAuthoringPlanePoint();
        Plane plane = new Plane(authoringCamera.transform.forward, planePoint);
        float distanceToPlane = plane.GetDistanceToPoint(worldPosition);
        return worldPosition - (authoringCamera.transform.forward * distanceToPlane);
    }

    private void SetConstrainedTarget(
        Transform target,
        HumanBodyBones upperBoneId,
        HumanBodyBones lowerBoneId,
        HumanBodyBones endBoneId,
        Vector3 desiredWorldPosition)
    {
        if (targetAnimator == null || target == null)
        {
            return;
        }

        Transform upperBone = targetAnimator.GetBoneTransform(upperBoneId);
        Transform lowerBone = targetAnimator.GetBoneTransform(lowerBoneId);
        Transform endBone = targetAnimator.GetBoneTransform(endBoneId);
        if (upperBone == null || lowerBone == null || endBone == null)
        {
            return;
        }

        Vector3 rootPosition = upperBone.position;
        float upperLength = Vector3.Distance(upperBone.position, lowerBone.position);
        float lowerLength = Vector3.Distance(lowerBone.position, endBone.position);
        float maxReach = Mathf.Max(0.001f, upperLength + lowerLength - 0.0001f);

        Vector3 constrainedPosition = desiredWorldPosition;
        Vector3 toDesired = constrainedPosition - rootPosition;
        if (toDesired.magnitude > maxReach)
        {
            constrainedPosition = rootPosition + (toDesired.normalized * maxReach);
        }

        target.position = constrainedPosition;
    }

    private static void OrderByScreenX(ref Vector2 a, ref Vector2 b)
    {
        if (a.x <= b.x)
        {
            return;
        }

        (a, b) = (b, a);
    }

    private static Vector2 ClampViewportPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }

    private static string GetRelativePath(Transform root, Transform current)
    {
        if (root == current)
        {
            return string.Empty;
        }

        List<string> pathParts = new List<string>(8);
        Transform cursor = current;
        while (cursor != null && cursor != root)
        {
            pathParts.Add(cursor.name);
            cursor = cursor.parent;
        }

        pathParts.Reverse();
        return string.Join("/", pathParts);
    }

    private static Transform FindRelativeTransform(Transform root, string relativePath)
    {
        if (root == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            return root;
        }

        return root.Find(relativePath);
    }

    private static float NormalizeAngle(float angleDegrees)
    {
        while (angleDegrees > 180f)
        {
            angleDegrees -= 360f;
        }

        while (angleDegrees < -180f)
        {
            angleDegrees += 360f;
        }

        return angleDegrees;
    }
}
