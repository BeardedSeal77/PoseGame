using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LibTessDotNet;

[DisallowMultipleComponent]
public class ScreenWallFitController : MonoBehaviour
{
    private const int MaxOverlayVertices = 32;

    [Header("References")]
    [SerializeField] private RectangleWallDefinition wallDefinition;
    [SerializeField] private bool useShapeFolder;
    [SerializeField] private string shapeResourcesFolder = "WallShapes";
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private GameManager gameManager;

    [Header("Flow")]
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loop;
    [SerializeField, Min(0f)] private float loopDelay = 1.25f;
    [SerializeField] private bool autoAlignToShapeReference = true;

    [Header("3D Wall")]
    [SerializeField] private bool useThreeDimensionalWall = true;
    [SerializeField, Min(0.05f)] private float wallStartDepth = 0.75f;
    [SerializeField, Min(0.02f)] private float wallThickness = 0.35f;
    [SerializeField] private Color wallColor = new Color(0.38f, 0.4f, 0.42f, 1f);
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Texture2D wallTexture;
    [SerializeField] private Vector2 wallTextureTiling = new Vector2(0.45f, 0.45f);
    [SerializeField, Range(0f, 1f)] private float wallSmoothness = 0.15f;

    [Header("Reference Camera (rendering only — evaluation uses metric world space)")]
    [Tooltip("Z distance of the reference camera from the character plane.")]
    [SerializeField, Min(0.5f)] private float evaluationCameraDistance = 4.5f;
    [Tooltip("Field of view for the reference camera.")]
    [SerializeField, Range(20f, 80f)] private float evaluationCameraFOV = 32f;
    [Tooltip("Height of the reference camera.")]
    [SerializeField] private float evaluationCameraHeightOffset = 1.35f;
    [Tooltip("Scale of the wall when it first appears. Shrinks to 1 over the shrink duration.")]
    [SerializeField, Min(1.1f)] private float wallStartScale = 3f;

    [Header("Scoring")]
    [Tooltip("Metric tolerance (meters) around each tracked joint. Points slightly outside the polygon edge still pass.")]
    [SerializeField, Range(0f, 0.3f)] private float fitPadding = 0.15f;
    [SerializeField, Range(0.5f, 2f)] private float orbRadiusMultiplier = 1f;
    [SerializeField] private Color orbInactiveColor = new Color(1f, 0.75f, 0.2f, 0.9f);
    [SerializeField] private Color orbActiveColor = new Color(0.15f, 1f, 0.35f, 0.95f);

    [Header("Overlay")]
    [SerializeField, Range(0.001f, 0.03f)] private float outlineThickness = 0.006f;
    [SerializeField] private Color overlayColor = new Color(0.35f, 0.35f, 0.35f, 0.55f);
    [SerializeField] private Color outlineColor = new Color(0.7f, 0.7f, 0.7f, 0.95f);
    [SerializeField] private Color successFlashColor = new Color(0.1f, 0.85f, 0.2f, 0.6f);
    [SerializeField] private Color failureFlashColor = new Color(0.95f, 0.15f, 0.12f, 0.6f);
    [SerializeField, Min(0.05f)] private float flashDuration = 0.35f;

    private readonly List<Transform> trackedJoints = new List<Transform>(12);
    private readonly List<WallShapeData> loadedShapes = new List<WallShapeData>(16);
    private readonly List<WallShapeData> remainingShapes = new List<WallShapeData>(16);
    private readonly List<Vector2> targetShapePolygon = new List<Vector2>(16);
    private readonly List<Vector2> startShapePolygon = new List<Vector2>(16);
    private readonly List<Vector2> animatedShapePolygon = new List<Vector2>(16);
    private readonly List<WallOrbTargetData> activeOrbTargets = new List<WallOrbTargetData>(8);
    private readonly List<bool> orbHitStates = new List<bool>(8);
    private readonly List<Vector2> viewportShapePolygon = new List<Vector2>(16);
    private readonly List<WallOrbTargetData> viewportOrbTargets = new List<WallOrbTargetData>(8);
    private readonly Rect fullScreenRect = new Rect(0f, 0f, 1f, 1f);

    private Camera evaluationCamera;
    private Rect currentCutoutRect;
    private Rect targetCutoutRect;
    private RuntimeOverlay overlay;
    private RuntimeWall runtimeWall;
    private RuntimeWireframe runtimeWireframe;
    private WallState state;
    private float stateTime;
    private bool lastResultPassed;
    private float currentShrinkDuration;
    private float currentWallTargetDepth;

    private enum WallState
    {
        Idle,
        Shrinking,
        Flashing,
        WaitingToLoop,
    }

    private void Reset()
    {
        targetCamera = Camera.main;
        targetAnimator = FindFirstObjectByType<Animator>();
        wallDefinition = FindFirstObjectByType<RectangleWallDefinition>();
        gameManager = FindFirstObjectByType<GameManager>();
        useShapeFolder = true;
    }

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetAnimator == null)
        {
            targetAnimator = FindFirstObjectByType<Animator>();
        }

        if (wallDefinition == null)
        {
            wallDefinition = FindFirstObjectByType<RectangleWallDefinition>();
        }

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        CreateEvaluationCamera();
        autoAlignToShapeReference = false;
        EnsureOverlay();
        RefreshShapeLibrary();
        CacheTrackedJoints();
        overlay.SetVisible(false);
    }

    private void CreateEvaluationCamera()
    {
        // Match the pose creator camera exactly so wall shapes authored there
        // appear identically at runtime. Pose creator: pos (0, 1.35, -4.5),
        // FOV 32, identity rotation, 16:9 aspect.
        GameObject evalCamObj = new GameObject("EvaluationCamera");
        evalCamObj.transform.SetParent(null, false);
        evaluationCamera = evalCamObj.AddComponent<Camera>();
        evaluationCamera.orthographic = false;
        evaluationCamera.fieldOfView = evaluationCameraFOV;
        evaluationCamera.aspect = 16f / 9f;
        evaluationCamera.nearClipPlane = 0.1f;
        evaluationCamera.farClipPlane = 100f;
        evaluationCamera.enabled = false;
        evaluationCamera.cullingMask = 0;

        evalCamObj.transform.position = new Vector3(0f, evaluationCameraHeightOffset, -evaluationCameraDistance);
        evalCamObj.transform.rotation = Quaternion.identity;
    }

    private void Start()
    {
        if (playOnStart)
        {
            BeginWall();
        }
    }

    private void Update()
    {
        switch (state)
        {
            case WallState.Shrinking:
                UpdateShrink();
                break;
            case WallState.Flashing:
                UpdateFlash();
                break;
            case WallState.WaitingToLoop:
                UpdateLoopDelay();
                break;
        }
    }

    /// <summary>True when no wall cycle is running (Idle state).</summary>
    public bool IsIdle => state == WallState.Idle;

    /// <summary>
    /// Immediately stops the current wall cycle, hides all visuals, and
    /// resets to Idle. Call this when transitioning to menus / game over.
    /// </summary>
    public void StopAndClear()
    {
        state = WallState.Idle;
        stateTime = 0f;

        if (overlay != null)
        {
            overlay.SetFlashColor(Color.clear, 0f);
            overlay.SetVisible(false);
        }

        if (runtimeWall != null)
            runtimeWall.SetVisible(false);

        if (runtimeWireframe != null)
            runtimeWireframe.SetVisible(false);
    }

    public void BeginWall()
    {
        if (!TryPrepareRun())
        {
            return;
        }

        EnsureOverlay();

        // Convert metric target polygon to viewport for 3D wall mesh rendering.
        ConvertMetricToViewportPolygon(targetShapePolygon, viewportShapePolygon);

        stateTime = 0f;
        state = WallState.Shrinking;
        currentWallTargetDepth = Mathf.Max(evaluationCamera.nearClipPlane + 0.1f, EstimateAvatarDepth() - (wallThickness * 0.5f));

        if (useThreeDimensionalWall)
        {
            EnsureRuntimeWall();
            runtimeWall.SetVisible(true);
            runtimeWall.UpdateMesh(viewportShapePolygon, currentWallTargetDepth, wallThickness, wallColor, wallMaterial, wallTexture, wallTextureTiling, wallSmoothness);
            runtimeWall.SetTravelOffset(0f);
            runtimeWall.SetScale(wallStartScale);
        }

        // Show world-space wireframe at the wall's target position.
        float wireframeZ = evaluationCamera.transform.position.z + currentWallTargetDepth;
        EnsureRuntimeWireframe();
        runtimeWireframe.SetVisible(true);
        runtimeWireframe.UpdateOutline(targetShapePolygon, wireframeZ, outlineColor);
        runtimeWireframe.UpdateOrbs(activeOrbTargets, orbHitStates, wireframeZ, orbInactiveColor, orbActiveColor, orbRadiusMultiplier);

        // Overlay is flash-only now.
        if (overlay != null)
        {
            overlay.SetVisible(false);
            overlay.SetFlashColor(Color.clear, 0f);
        }
    }

    private bool TryPrepareRun()
    {
        if (useShapeFolder)
        {
            if (!TrySelectNextShape())
            {
                state = WallState.Idle;
                return false;
            }
        }
        else if (wallDefinition == null)
        {
            Debug.LogWarning("[ScreenWallFitController] No RectangleWallDefinition assigned.");
            state = WallState.Idle;
            return false;
        }

        if (targetAnimator == null)
        {
            Debug.LogWarning("[ScreenWallFitController] No Animator assigned.");
            state = WallState.Idle;
            return false;
        }

        if (targetCamera == null || evaluationCamera == null)
        {
            Debug.LogWarning("[ScreenWallFitController] No Camera assigned.");
            state = WallState.Idle;
            return false;
        }

        if (!useShapeFolder)
        {
            targetCutoutRect = wallDefinition.TargetRect;
            currentShrinkDuration = wallDefinition.ShrinkDuration;
            SetRectPolygon(targetCutoutRect, targetShapePolygon);
            activeOrbTargets.Clear();
            orbHitStates.Clear();
        }

        CacheTrackedJoints();
        return trackedJoints.Count > 0;
    }

    private void UpdateShrink()
    {
        float duration = Mathf.Max(0.05f, currentShrinkDuration);
        stateTime += Time.deltaTime;

        float t = Mathf.Clamp01(stateTime / duration);
        UpdateOrbHitStates();

        if (useThreeDimensionalWall)
        {
            EnsureRuntimeWall();
            float currentScale = Mathf.Lerp(wallStartScale, 1f, t);
            runtimeWall.SetScale(currentScale);
        }

        // Update wireframe orb hit states.
        if (runtimeWireframe != null)
        {
            float wireframeZ = evaluationCamera.transform.position.z + currentWallTargetDepth;
            runtimeWireframe.UpdateOrbs(activeOrbTargets, orbHitStates, wireframeZ, orbInactiveColor, orbActiveColor, orbRadiusMultiplier);
        }

        if (t < 1f)
        {
            return;
        }

        lastResultPassed = EvaluateCurrentPose();
        int orbHits = CountSatisfiedOrbs();

        if (gameManager == null)
        {
            gameManager = GameManager.Instance != null
                ? GameManager.Instance
                : FindFirstObjectByType<GameManager>();
        }

        if (!lastResultPassed)
        {
            if (gameManager != null)
            {
                Debug.Log("[ScreenWallFitController] Pose failed. Losing one life.");
                gameManager.LoseLife();
            }
            else
            {
                Debug.LogWarning("[ScreenWallFitController] Pose failed, but no GameManager was found in the scene.");
            }
        }
        else if (gameManager != null)
        {
            gameManager.RegisterWallResult(true, orbHits, activeOrbTargets.Count);
        }

        stateTime = 0f;
        state = WallState.Flashing;

        if (runtimeWall != null)
        {
            runtimeWall.SetVisible(false);
        }

        if (runtimeWireframe != null)
        {
            runtimeWireframe.SetVisible(false);
        }

        // Flash pass/fail via screen overlay.
        EnsureOverlay();
        if (overlay != null)
        {
            overlay.SetVisible(true);
            overlay.SetFlashColor(lastResultPassed ? successFlashColor : failureFlashColor, 1f);
        }
    }

    private void UpdateFlash()
    {
        stateTime += Time.deltaTime;
        float alpha = 1f - Mathf.Clamp01(stateTime / Mathf.Max(0.05f, flashDuration));
        Color flashColor = lastResultPassed ? successFlashColor : failureFlashColor;

        if (overlay != null)
        {
            overlay.SetVisible(true);
            overlay.SetFlashColor(flashColor, alpha);
        }

        if (alpha > 0f)
        {
            return;
        }

        if (overlay != null)
        {
            overlay.SetVisible(false);
        }

        if (loop && (GameManager.Instance == null || GameManager.Instance.CurrentState == GameManager.GameState.Playing))
        {
            state = WallState.WaitingToLoop;
            stateTime = 0f;
            return;
        }

        state = WallState.Idle;
    }

    private void UpdateLoopDelay()
    {
        stateTime += Time.deltaTime;
        if (stateTime >= loopDelay)
        {
            BeginWall();
        }
    }

    public void RefreshShapeLibrary()
    {
        loadedShapes.Clear();
        remainingShapes.Clear();
        targetShapePolygon.Clear();
        startShapePolygon.Clear();
        animatedShapePolygon.Clear();
        viewportShapePolygon.Clear();
        activeOrbTargets.Clear();
        viewportOrbTargets.Clear();
        orbHitStates.Clear();

        if (!useShapeFolder)
        {
            return;
        }

        string normalizedFolder = NormalizeResourcesFolder(shapeResourcesFolder);
        RectangleWallShapeAsset[] rectangleShapes = Resources.LoadAll<RectangleWallShapeAsset>(normalizedFolder);
        Array.Sort(rectangleShapes, (a, b) => string.CompareOrdinal(a.name, b.name));
        for (int index = 0; index < rectangleShapes.Length; index++)
        {
            RectangleWallShapeAsset shape = rectangleShapes[index];
            Rect metricRect = shape.TargetRect;
            // Legacy detection: if the rect is entirely within 0-1, it was authored
            // in viewport space and needs conversion to metric.
            if (IsLikelyViewportSpace(metricRect))
            {
                Vector2 min = ViewportToMetric(metricRect.min);
                Vector2 max = ViewportToMetric(metricRect.max);
                metricRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            loadedShapes.Add(new WallShapeData(shape.name, metricRect, shape.ShrinkDuration, BuildRectPolygonArray(metricRect), Array.Empty<WallOrbTargetData>(), false, default));
        }

        PolygonWallShapeAsset[] polygonShapes = Resources.LoadAll<PolygonWallShapeAsset>(normalizedFolder);
        Array.Sort(polygonShapes, (a, b) => string.CompareOrdinal(a.name, b.name));
        for (int index = 0; index < polygonShapes.Length; index++)
        {
            PolygonWallShapeAsset shape = polygonShapes[index];
            Vector2[] vertices = NormalizeLoadedPolygon(CopyVertices(shape.Vertices));
            if (vertices.Length < 3)
            {
                continue;
            }

            WallOrbTargetData[] orbTargets = CopyOrbTargets(shape.OrbTargets);

            // Legacy detection: if all vertices are in 0-1, convert from viewport to metric.
            if (IsLikelyViewportPolygon(vertices))
            {
                for (int vi = 0; vi < vertices.Length; vi++)
                {
                    vertices[vi] = ViewportToMetric(vertices[vi]);
                }

                for (int oi = 0; oi < orbTargets.Length; oi++)
                {
                    orbTargets[oi].position = ViewportToMetric(orbTargets[oi].position);
                    orbTargets[oi].radius = orbTargets[oi].radius * GetViewportToMetricScale();
                }
            }

            Rect refBounds = shape.ReferenceAvatarBounds;
            if (shape.HasReferenceAvatarBounds && IsLikelyViewportSpace(refBounds))
            {
                Vector2 rMin = ViewportToMetric(refBounds.min);
                Vector2 rMax = ViewportToMetric(refBounds.max);
                refBounds = Rect.MinMaxRect(rMin.x, rMin.y, rMax.x, rMax.y);
            }

            loadedShapes.Add(new WallShapeData(shape.name, BuildBounds(vertices), shape.ShrinkDuration, vertices, orbTargets, shape.HasReferenceAvatarBounds, refBounds));
        }

        loadedShapes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    private bool TrySelectNextShape()
    {
        if (loadedShapes.Count == 0)
        {
            RefreshShapeLibrary();
        }

        if (loadedShapes.Count == 0)
        {
            Debug.LogWarning($"[ScreenWallFitController] No RectangleWallShapeAsset or PolygonWallShapeAsset found in Resources/{NormalizeResourcesFolder(shapeResourcesFolder)}.");
            return false;
        }

        if (remainingShapes.Count == 0)
        {
            remainingShapes.AddRange(loadedShapes);
        }

        int selectedIndex = UnityEngine.Random.Range(0, remainingShapes.Count);
        WallShapeData selectedShape = remainingShapes[selectedIndex];
        remainingShapes.RemoveAt(selectedIndex);

        targetCutoutRect = selectedShape.Bounds;
        currentShrinkDuration = selectedShape.ShrinkDuration;
        targetShapePolygon.Clear();
        targetShapePolygon.AddRange(selectedShape.PolygonVertices);
        activeOrbTargets.Clear();
        activeOrbTargets.AddRange(selectedShape.OrbTargets);
        orbHitStates.Clear();
        for (int index = 0; index < activeOrbTargets.Count; index++)
        {
            orbHitStates.Add(false);
        }

        if (autoAlignToShapeReference)
        {
            AlignAvatarToShapeReference(selectedShape);
        }

        Rect bounds = selectedShape.Bounds;
        Debug.Log($"[ScreenWallFitController] Selected wall shape '{selectedShape.Name}' with {selectedShape.PolygonVertices.Length} vertices. Metric bounds: x[{bounds.xMin:F2}..{bounds.xMax:F2}] y[{bounds.yMin:F2}..{bounds.yMax:F2}]");

        return true;
    }

    private static string NormalizeResourcesFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "WallShapes";
        }

        return folder.Replace('\\', '/').Trim('/');
    }

    private bool EvaluateCurrentPose()
    {
        if (targetShapePolygon.Count >= 3)
        {
            return EvaluateCurrentPoseAgainstPolygon();
        }

        Rect scoringRect = ExpandRect(targetCutoutRect, fitPadding);

        for (int index = 0; index < trackedJoints.Count; index++)
        {
            Transform joint = trackedJoints[index];
            if (joint == null)
            {
                continue;
            }

            Vector2 metricPoint = new Vector2(joint.position.x, joint.position.y);
            if (!scoringRect.Contains(metricPoint))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Joint-based pose evaluation. For each tracked joint, checks whether
    /// its world XY position falls inside the wall polygon (with fitPadding
    /// tolerance around edges).
    /// </summary>
    private bool EvaluateCurrentPoseAgainstPolygon()
    {
        // Dump all joint positions and polygon bounds for diagnostics.
        Rect polyBounds = BuildBounds(targetShapePolygon);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[WallFit] Evaluating {trackedJoints.Count} joints against polygon y[{polyBounds.yMin:F2}..{polyBounds.yMax:F2}] x[{polyBounds.xMin:F2}..{polyBounds.xMax:F2}]");
        for (int index = 0; index < trackedJoints.Count; index++)
        {
            Transform joint = trackedJoints[index];
            if (joint == null) continue;
            Vector2 p = new Vector2(joint.position.x, joint.position.y);
            bool inside = ContainsPointInPolygonWithPadding(p, targetShapePolygon, fitPadding);
            sb.AppendLine($"  {(inside ? "OK" : "FAIL")} {joint.name} ({p.x:F2}, {p.y:F2})");
        }
        Debug.Log(sb.ToString());

        for (int index = 0; index < trackedJoints.Count; index++)
        {
            Transform joint = trackedJoints[index];
            if (joint == null)
            {
                continue;
            }

            Vector2 metricPoint = new Vector2(joint.position.x, joint.position.y);

            if (!ContainsPointInPolygonWithPadding(metricPoint, targetShapePolygon, fitPadding))
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateOrbHitStates()
    {
        for (int index = 0; index < orbHitStates.Count; index++)
        {
            orbHitStates[index] = IsOrbSatisfied(activeOrbTargets[index]);
        }
    }

    private int CountSatisfiedOrbs()
    {
        int count = 0;
        for (int index = 0; index < orbHitStates.Count; index++)
        {
            if (orbHitStates[index])
            {
                count += 1;
            }
        }

        return count;
    }

    private bool IsOrbSatisfied(WallOrbTargetData orbTarget)
    {
        float radius = orbTarget.radius * orbRadiusMultiplier;
        float squaredRadius = radius * radius;

        for (int index = 0; index < trackedJoints.Count; index++)
        {
            Transform joint = trackedJoints[index];
            if (joint == null)
            {
                continue;
            }

            Vector2 metricPoint = new Vector2(joint.position.x, joint.position.y);
            Vector2 delta = metricPoint - orbTarget.position;
            if (delta.sqrMagnitude <= squaredRadius)
            {
                return true;
            }
        }

        return false;
    }

    private void CacheTrackedJoints()
    {
        trackedJoints.Clear();

        if (targetAnimator == null || !targetAnimator.isHuman)
        {
            return;
        }

        // Track individual joint positions — simpler and more forgiving than
        // sampling along bone segments. Legs excluded (Kinect too noisy).
        // Neck excluded — it sits at the narrowest silhouette pinch between
        // head and shoulders, causing false failures. Head + Chest cover it.
        AddJoint(HumanBodyBones.Head);
        AddJoint(HumanBodyBones.Chest);
        AddJoint(HumanBodyBones.Hips);
        AddJoint(HumanBodyBones.LeftUpperArm);
        AddJoint(HumanBodyBones.RightUpperArm);
        AddJoint(HumanBodyBones.LeftLowerArm);
        AddJoint(HumanBodyBones.RightLowerArm);
        AddJoint(HumanBodyBones.LeftHand);
        AddJoint(HumanBodyBones.RightHand);
    }

    private void AddJoint(HumanBodyBones bone)
    {
        Transform joint = targetAnimator.GetBoneTransform(bone);
        if (joint != null)
        {
            trackedJoints.Add(joint);
        }
    }

    private void EnsureOverlay()
    {
        if (overlay != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("WallOverlayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        overlay = new RuntimeOverlay(canvasRect);
    }

    private void EnsureRuntimeWall()
    {
        if (runtimeWall != null)
        {
            return;
        }

        runtimeWall = new RuntimeWall(evaluationCamera, transform);
    }

    private void EnsureRuntimeWireframe()
    {
        if (runtimeWireframe != null)
        {
            return;
        }

        runtimeWireframe = new RuntimeWireframe(transform);
    }

    private float EstimateAvatarDepth()
    {
        if (evaluationCamera == null || targetAnimator == null)
        {
            return wallStartDepth + wallThickness + 1f;
        }

        HumanBodyBones[] sampledBones =
        {
            HumanBodyBones.Head,
            HumanBodyBones.Chest,
            HumanBodyBones.Hips,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
        };

        float depthSum = 0f;
        int depthCount = 0;
        for (int index = 0; index < sampledBones.Length; index++)
        {
            Transform bone = targetAnimator.GetBoneTransform(sampledBones[index]);
            if (bone == null)
            {
                continue;
            }

            Vector3 localPoint = evaluationCamera.transform.InverseTransformPoint(bone.position);
            if (localPoint.z <= evaluationCamera.nearClipPlane)
            {
                continue;
            }

            depthSum += localPoint.z;
            depthCount += 1;
        }

        if (depthCount == 0)
        {
            return wallStartDepth + wallThickness + 1f;
        }

        return depthSum / depthCount;
    }

    // ----------------------------------------------------------------
    //  Metric ↔ Viewport conversion
    // ----------------------------------------------------------------

    private Vector2 MetricToViewport(Vector2 metric)
    {
        if (evaluationCamera == null)
        {
            return new Vector2(0.5f, 0.5f);
        }

        Vector3 vp = evaluationCamera.WorldToViewportPoint(new Vector3(metric.x, metric.y, 0f));
        return new Vector2(vp.x, vp.y);
    }

    private Vector2 ViewportToMetric(Vector2 viewport)
    {
        if (evaluationCamera == null)
        {
            return Vector2.zero;
        }

        Vector3 world = evaluationCamera.ViewportToWorldPoint(new Vector3(viewport.x, viewport.y, evaluationCameraDistance));
        return new Vector2(world.x, world.y);
    }

    private float GetViewportToMetricScale()
    {
        float halfHeight = evaluationCameraDistance * Mathf.Tan(evaluationCameraFOV * 0.5f * Mathf.Deg2Rad);
        return halfHeight * 2f;
    }

    private void ConvertMetricToViewportPolygon(IReadOnlyList<Vector2> metricPolygon, List<Vector2> viewportPolygon)
    {
        viewportPolygon.Clear();
        for (int index = 0; index < metricPolygon.Count; index++)
        {
            viewportPolygon.Add(MetricToViewport(metricPolygon[index]));
        }
    }

    private void ConvertOrbsToViewport()
    {
        viewportOrbTargets.Clear();
        float scale = GetViewportToMetricScale();
        float inverseScale = scale > 0.001f ? 1f / scale : 1f;

        for (int index = 0; index < activeOrbTargets.Count; index++)
        {
            WallOrbTargetData orb = activeOrbTargets[index];
            viewportOrbTargets.Add(new WallOrbTargetData(
                MetricToViewport(orb.position),
                orb.radius * inverseScale));
        }
    }

    private static bool IsLikelyViewportPolygon(Vector2[] vertices)
    {
        for (int index = 0; index < vertices.Length; index++)
        {
            if (vertices[index].x < -0.01f || vertices[index].x > 1.01f ||
                vertices[index].y < -0.01f || vertices[index].y > 1.01f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLikelyViewportSpace(Rect rect)
    {
        return rect.xMin >= -0.01f && rect.xMax <= 1.01f &&
               rect.yMin >= -0.01f && rect.yMax <= 1.01f;
    }

    private static Rect LerpRect(Rect from, Rect to, float t)
    {
        Vector2 min = Vector2.Lerp(from.min, to.min, t);
        Vector2 max = Vector2.Lerp(from.max, to.max, t);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect ExpandRect(Rect source, float amount)
    {
        return Rect.MinMaxRect(
            source.xMin - amount,
            source.yMin - amount,
            source.xMax + amount,
            source.yMax + amount);
    }

    private static void CopyPolygon(IReadOnlyList<Vector2> source, List<Vector2> destination)
    {
        destination.Clear();
        if (source == null)
        {
            return;
        }

        for (int index = 0; index < source.Count; index++)
        {
            destination.Add(source[index]);
        }
    }

    private static void SetRectPolygon(Rect rect, List<Vector2> destination)
    {
        destination.Clear();
        destination.Add(new Vector2(rect.xMin, rect.yMin));
        destination.Add(new Vector2(rect.xMax, rect.yMin));
        destination.Add(new Vector2(rect.xMax, rect.yMax));
        destination.Add(new Vector2(rect.xMin, rect.yMax));
    }

    private static Vector2[] BuildRectPolygonArray(Rect rect)
    {
        return new[]
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMin, rect.yMax),
        };
    }

    private static void BuildStartPolygon(IReadOnlyList<Vector2> targetPolygon, List<Vector2> destination)
    {
        destination.Clear();
        if (targetPolygon == null || targetPolygon.Count < 3)
        {
            return;
        }

        float expansion = 1.25f;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            destination.Clear();
            bool clockwise = SignedArea(targetPolygon) <= 0f;
            for (int index = 0; index < targetPolygon.Count; index++)
            {
                Vector2 previous = targetPolygon[(index - 1 + targetPolygon.Count) % targetPolygon.Count];
                Vector2 current = targetPolygon[index];
                Vector2 next = targetPolygon[(index + 1) % targetPolygon.Count];
                Vector2 outward = GetOutwardVertexDirection(previous, current, next, clockwise);
                destination.Add(current + (outward * expansion));
            }

            if (ContainsPointInPolygon(new Vector2(0f, 0f), destination) &&
                ContainsPointInPolygon(new Vector2(1f, 0f), destination) &&
                ContainsPointInPolygon(new Vector2(1f, 1f), destination) &&
                ContainsPointInPolygon(new Vector2(0f, 1f), destination))
            {
                return;
            }

            expansion *= 1.8f;
        }
    }

    private void AlignAvatarToShapeReference(WallShapeData shape)
    {
        Camera evalCam = evaluationCamera != null ? evaluationCamera : targetCamera;
        if (!shape.HasReferenceAvatarBounds || targetAnimator == null || evalCam == null)
        {
            return;
        }

        Transform playerRoot = targetAnimator.transform.parent;
        if (playerRoot == null)
        {
            return;
        }

        for (int iteration = 0; iteration < 3; iteration++)
        {
            if (!TryGetAvatarViewportBounds(targetAnimator, evalCam, out Rect currentBounds))
            {
                return;
            }

            float currentHeight = Mathf.Max(0.0001f, currentBounds.height);
            float targetHeight = Mathf.Max(0.0001f, shape.ReferenceAvatarBounds.height);
            float depth = Vector3.Dot(targetAnimator.transform.position - evalCam.transform.position, evalCam.transform.forward);
            float desiredDepth = Mathf.Max(evalCam.nearClipPlane + 0.25f, depth * (currentHeight / targetHeight));
            float depthDelta = desiredDepth - depth;
            playerRoot.position += evalCam.transform.forward * depthDelta;

            if (!TryGetAvatarViewportBounds(targetAnimator, evalCam, out currentBounds))
            {
                return;
            }

            float alignedDepth = Vector3.Dot(targetAnimator.transform.position - evalCam.transform.position, evalCam.transform.forward);
            Vector2 currentCenter = currentBounds.center;
            Vector2 desiredCenter = shape.ReferenceAvatarBounds.center;
            Vector3 currentWorldCenter = evalCam.ViewportToWorldPoint(new Vector3(currentCenter.x, currentCenter.y, alignedDepth));
            Vector3 desiredWorldCenter = evalCam.ViewportToWorldPoint(new Vector3(currentCenter.x, desiredCenter.y, alignedDepth));
            float yDelta = desiredWorldCenter.y - currentWorldCenter.y;
            playerRoot.position += Vector3.up * yDelta;
        }
    }

    private static void LerpPolygon(IReadOnlyList<Vector2> from, IReadOnlyList<Vector2> to, float t, List<Vector2> destination)
    {
        destination.Clear();
        int count = Mathf.Min(from.Count, to.Count);
        for (int index = 0; index < count; index++)
        {
            destination.Add(Vector2.Lerp(from[index], to[index], t));
        }
    }

    private static Vector2[] CopyVertices(IReadOnlyList<Vector2> vertices)
    {
        if (vertices == null || vertices.Count == 0)
        {
            return Array.Empty<Vector2>();
        }

        Vector2[] result = new Vector2[vertices.Count];
        for (int index = 0; index < vertices.Count; index++)
        {
            result[index] = vertices[index];
        }

        return result;
    }

    private static WallOrbTargetData[] CopyOrbTargets(IReadOnlyList<WallOrbTargetData> orbTargets)
    {
        if (orbTargets == null || orbTargets.Count == 0)
        {
            return Array.Empty<WallOrbTargetData>();
        }

        WallOrbTargetData[] result = new WallOrbTargetData[orbTargets.Count];
        for (int index = 0; index < orbTargets.Count; index++)
        {
            result[index] = orbTargets[index];
        }

        return result;
    }

    private static Vector2[] NormalizeLoadedPolygon(Vector2[] vertices)
    {
        if (vertices == null || vertices.Length < 3)
        {
            return Array.Empty<Vector2>();
        }

        int count = vertices.Length;
        if (count > 1 && Vector2.Distance(vertices[0], vertices[count - 1]) <= 0.0001f)
        {
            count -= 1;
        }

        if (count == 4)
        {
            Rect bounds = BuildBounds(vertices);
            return BuildRectPolygonArray(bounds);
        }

        if (count == vertices.Length)
        {
            return vertices;
        }

        Vector2[] trimmed = new Vector2[count];
        Array.Copy(vertices, trimmed, count);
        return trimmed;
    }

    private static Rect BuildBounds(IReadOnlyList<Vector2> vertices)
    {
        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        for (int index = 1; index < vertices.Count; index++)
        {
            min = Vector2.Min(min, vertices[index]);
            max = Vector2.Max(max, vertices[index]);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool ContainsPointInPolygonWithPadding(Vector2 point, IReadOnlyList<Vector2> polygon, float padding)
    {
        if (ContainsPointInPolygon(point, polygon))
        {
            return true;
        }

        float squaredPadding = padding * padding;
        for (int index = 0; index < polygon.Count; index++)
        {
            int nextIndex = (index + 1) % polygon.Count;
            Vector2 closestPoint = ClosestPointOnSegment(point, polygon[index], polygon[nextIndex]);
            if ((closestPoint - point).sqrMagnitude <= squaredPadding)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsPointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        for (int index = 0, previousIndex = polygon.Count - 1; index < polygon.Count; previousIndex = index++)
        {
            Vector2 current = polygon[index];
            Vector2 previous = polygon[previousIndex];

            bool crossesY = ((current.y > point.y) != (previous.y > point.y));
            if (!crossesY)
            {
                continue;
            }

            float interpolatedX = ((previous.x - current.x) * (point.y - current.y) / (previous.y - current.y)) + current.x;
            if (point.x < interpolatedX)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denominator = Mathf.Max(0.000001f, Vector2.Dot(ab, ab));
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
        return a + (ab * t);
    }

    private static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        float area = 0f;
        for (int index = 0; index < polygon.Count; index++)
        {
            Vector2 current = polygon[index];
            Vector2 next = polygon[(index + 1) % polygon.Count];
            area += (current.x * next.y) - (next.x * current.y);
        }

        return area * 0.5f;
    }

    private static Vector2 GetOutwardVertexDirection(Vector2 previous, Vector2 current, Vector2 next, bool clockwise)
    {
        Vector2 previousEdge = (current - previous).normalized;
        Vector2 nextEdge = (next - current).normalized;

        Vector2 previousNormal = clockwise
            ? new Vector2(previousEdge.y, -previousEdge.x)
            : new Vector2(-previousEdge.y, previousEdge.x);
        Vector2 nextNormal = clockwise
            ? new Vector2(nextEdge.y, -nextEdge.x)
            : new Vector2(-nextEdge.y, nextEdge.x);

        Vector2 direction = previousNormal + nextNormal;
        if (direction.sqrMagnitude < 0.000001f)
        {
            direction = previousNormal.sqrMagnitude > 0.000001f ? previousNormal : nextNormal;
        }

        return direction.normalized;
    }

    private static bool TryGetAvatarViewportBounds(Animator animator, Camera camera, out Rect viewportBounds)
    {
        viewportBounds = default;
        if (animator == null || camera == null || !animator.isHuman)
        {
            return false;
        }

        HumanBodyBones[] trackedBones =
        {
            HumanBodyBones.Head,
            HumanBodyBones.Neck,
            HumanBodyBones.Chest,
            HumanBodyBones.Hips,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
        };

        bool foundAny = false;
        Vector2 min = Vector2.one;
        Vector2 max = Vector2.zero;
        for (int index = 0; index < trackedBones.Length; index++)
        {
            Transform bone = animator.GetBoneTransform(trackedBones[index]);
            if (bone == null)
            {
                continue;
            }

            Vector3 viewport = camera.WorldToViewportPoint(bone.position);
            if (viewport.z <= 0f)
            {
                continue;
            }

            Vector2 point = new Vector2(viewport.x, viewport.y);
            if (!foundAny)
            {
                min = point;
                max = point;
                foundAny = true;
                continue;
            }

            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        if (!foundAny)
        {
            return false;
        }

        viewportBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static Vector2 ClampViewportPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }

    private sealed class RuntimeWall
    {
        private readonly Camera targetCamera;
        private readonly GameObject root;
        private readonly MeshFilter meshFilter;
        private readonly MeshRenderer meshRenderer;
        private readonly Mesh mesh;
        private Material faceMaterial;
        private Material edgeMaterial;
        private Material sourceMaterial;
        private static readonly int HoleVerticesProperty = Shader.PropertyToID("_HoleVertices");
        private static readonly int HoleVertexCountProperty = Shader.PropertyToID("_HoleVertexCount");
        private static readonly int BaseMapProperty = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int TextureTilingProperty = Shader.PropertyToID("_TextureTiling");
        private readonly Vector4[] polygonBuffer = new Vector4[MaxOverlayVertices];
        private readonly List<Vector3> tessVertices = new List<Vector3>(128);
        private readonly List<int> tessTriangles = new List<int>(256);

        public RuntimeWall(Camera targetCamera, Transform owner)
        {
            this.targetCamera = targetCamera;

            root = new GameObject("RuntimeWall3D", typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(targetCamera.transform, false);

            meshFilter = root.GetComponent<MeshFilter>();
            meshRenderer = root.GetComponent<MeshRenderer>();
            mesh = new Mesh
            {
                name = "RuntimeWall3DMesh",
            };

            mesh.MarkDynamic();
            meshFilter.sharedMesh = mesh;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
        }

        public void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
        }

        public void SetTravelOffset(float localZOffset)
        {
            root.transform.localPosition = new Vector3(0f, 0f, localZOffset);
        }

        public void SetScale(float scale)
        {
            root.transform.localScale = new Vector3(scale, scale, 1f);
        }

        public void UpdateMesh(IReadOnlyList<Vector2> cutoutPolygon, float frontDepth, float thickness, Color color, Material materialOverride, Texture2D textureOverride, Vector2 textureTiling, float smoothness)
        {
            if (cutoutPolygon == null || cutoutPolygon.Count < 3)
            {
                mesh.Clear();
                return;
            }

            EnsureMaterials(materialOverride, textureOverride, color, textureTiling, smoothness);

            float backDepth = Mathf.Max(frontDepth + 0.001f, frontDepth + thickness);

            // Tessellate wall face: outer rectangle minus inner cutout
            tessVertices.Clear();
            tessTriangles.Clear();
            Tess tess = new Tess();
            // Outer rectangle (CCW)
            tess.AddContour(new[] {
                new ContourVertex { Position = new Vec3(0, 0, 0) },
                new ContourVertex { Position = new Vec3(1, 0, 0) },
                new ContourVertex { Position = new Vec3(1, 1, 0) },
                new ContourVertex { Position = new Vec3(0, 1, 0) },
            }, ContourOrientation.CounterClockwise);
            // Inner cutout (CW)
            var inner = new ContourVertex[cutoutPolygon.Count];
            for (int i = 0; i < cutoutPolygon.Count; i++)
                inner[i] = new ContourVertex { Position = new Vec3(cutoutPolygon[i].x, cutoutPolygon[i].y, 0) };
            tess.AddContour(inner, ContourOrientation.Clockwise);
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);
            // Build mesh for front face
            for (int i = 0; i < tess.Vertices.Length; i++)
            {
                var v = tess.Vertices[i].Position;
                tessVertices.Add(ViewportToCameraLocalPoint(v.X, v.Y, frontDepth));
            }
            for (int i = 0; i < tess.ElementCount; i++)
            {
                int a = tess.Elements[i * 3 + 0];
                int b = tess.Elements[i * 3 + 1];
                int c = tess.Elements[i * 3 + 2];
                tessTriangles.Add(a);
                tessTriangles.Add(b);
                tessTriangles.Add(c);
            }
            // Back face: same tessellation, but at backDepth and reversed winding
            int backBase = tessVertices.Count;
            for (int i = 0; i < tess.Vertices.Length; i++)
            {
                var v = tess.Vertices[i].Position;
                tessVertices.Add(ViewportToCameraLocalPoint(v.X, v.Y, backDepth));
            }
            for (int i = 0; i < tess.ElementCount; i++)
            {
                int a = tess.Elements[i * 3 + 0] + backBase;
                int b = tess.Elements[i * 3 + 1] + backBase;
                int c = tess.Elements[i * 3 + 2] + backBase;
                // Reverse winding for back face
                tessTriangles.Add(a);
                tessTriangles.Add(c);
                tessTriangles.Add(b);
            }
            // Side walls (extrude cutout)
            List<Vector3> vertices = new List<Vector3>(tessVertices);
            List<int> faceTriangles = new List<int>(tessTriangles);
            List<Vector2> uvs = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
                uvs.Add(new Vector2(0, 0)); // Optionally map UVs
            List<int> edgeTriangles = new List<int>(cutoutPolygon.Count * 6);
            for (int i = 0; i < cutoutPolygon.Count; i++)
            {
                int next = (i + 1) % cutoutPolygon.Count;
                Vector2 a = cutoutPolygon[i];
                Vector2 b = cutoutPolygon[next];
                Vector3 fa = ViewportToCameraLocalPoint(a.x, a.y, frontDepth);
                Vector3 fb = ViewportToCameraLocalPoint(b.x, b.y, frontDepth);
                Vector3 ba = ViewportToCameraLocalPoint(a.x, a.y, backDepth);
                Vector3 bb = ViewportToCameraLocalPoint(b.x, b.y, backDepth);
                int vStart = vertices.Count;
                vertices.Add(fa); uvs.Add(new Vector2(0, 0));
                vertices.Add(fb); uvs.Add(new Vector2(1, 0));
                vertices.Add(bb); uvs.Add(new Vector2(1, 1));
                vertices.Add(ba); uvs.Add(new Vector2(0, 1));
                edgeTriangles.Add(vStart + 0);
                edgeTriangles.Add(vStart + 1);
                edgeTriangles.Add(vStart + 2);
                edgeTriangles.Add(vStart + 0);
                edgeTriangles.Add(vStart + 2);
                edgeTriangles.Add(vStart + 3);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(faceTriangles, 0);
            mesh.SetTriangles(edgeTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void EnsureMaterials(Material materialOverride, Texture2D textureOverride, Color color, Vector2 textureTiling, float smoothness)
        {
            if (faceMaterial == null || edgeMaterial == null || sourceMaterial != materialOverride)
            {
                sourceMaterial = materialOverride;

                Shader faceShader = Shader.Find("Hidden/PoseGame/WallCutoutFace");
                Shader edgeShader = materialOverride != null
                    ? materialOverride.shader
                    : Shader.Find("Universal Render Pipeline/Lit");
                if (edgeShader == null)
                {
                    edgeShader = Shader.Find("Standard");
                }

                faceMaterial = new Material(faceShader != null ? faceShader : edgeShader);
                edgeMaterial = materialOverride != null ? new Material(materialOverride) : new Material(edgeShader);
                faceMaterial.hideFlags = HideFlags.DontSave;
                edgeMaterial.hideFlags = HideFlags.DontSave;
                meshRenderer.sharedMaterials = new[] { faceMaterial, edgeMaterial };
            }

            if (faceMaterial == null || edgeMaterial == null)
            {
                return;
            }

            ConfigureLitMaterial(edgeMaterial, color, textureOverride, textureTiling, smoothness, alphaClip: false);
            if (faceMaterial.HasProperty(BaseColorProperty)) faceMaterial.SetColor(BaseColorProperty, color);
            if (faceMaterial.HasProperty(BaseMapProperty)) faceMaterial.SetTexture(BaseMapProperty, textureOverride);
            if (faceMaterial.HasProperty(TextureTilingProperty)) faceMaterial.SetVector(TextureTilingProperty, new Vector4(textureTiling.x, textureTiling.y, 0f, 0f));
        }

        private void ApplyHolePolygon(IReadOnlyList<Vector2> cutoutPolygon)
        {
            if (faceMaterial == null)
            {
                return;
            }

            int count = Mathf.Min(MaxOverlayVertices, cutoutPolygon.Count);
            for (int index = 0; index < polygonBuffer.Length; index++)
            {
                polygonBuffer[index] = index < count ? new Vector4(cutoutPolygon[index].x, cutoutPolygon[index].y, 0f, 0f) : Vector4.zero;
            }

            faceMaterial.SetInt(HoleVertexCountProperty, count);
            faceMaterial.SetVectorArray(HoleVerticesProperty, polygonBuffer);
        }

        private static void ConfigureLitMaterial(Material targetMaterial, Color color, Texture texture, Vector2 tiling, float smoothness, bool alphaClip)
        {
            if (targetMaterial.HasProperty("_BaseColor")) targetMaterial.SetColor("_BaseColor", color);
            if (targetMaterial.HasProperty("_Color")) targetMaterial.SetColor("_Color", color);
            if (targetMaterial.HasProperty("_BaseMap")) targetMaterial.SetTexture("_BaseMap", texture);
            if (targetMaterial.HasProperty("_MainTex")) targetMaterial.SetTexture("_MainTex", texture);
            if (texture != null)
            {
                if (targetMaterial.HasProperty("_BaseMap")) targetMaterial.SetTextureScale("_BaseMap", tiling);
                if (targetMaterial.HasProperty("_MainTex")) targetMaterial.SetTextureScale("_MainTex", tiling);
            }

            if (targetMaterial.HasProperty("_Smoothness")) targetMaterial.SetFloat("_Smoothness", smoothness);
            if (targetMaterial.HasProperty("_Cull")) targetMaterial.SetFloat("_Cull", 0f);
            if (targetMaterial.HasProperty("_AlphaClip")) targetMaterial.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            if (targetMaterial.HasProperty("_Cutoff")) targetMaterial.SetFloat("_Cutoff", 0.5f);
            if (alphaClip)
            {
                targetMaterial.EnableKeyword("_ALPHATEST_ON");
            }
            else
            {
                targetMaterial.DisableKeyword("_ALPHATEST_ON");
            }
        }

        private Vector3 ViewportToCameraLocalPoint(float viewportX, float viewportY, float depth)
        {
            if (targetCamera.orthographic)
            {
                float halfHeight = targetCamera.orthographicSize;
                float halfWidth = halfHeight * targetCamera.aspect;
                return new Vector3(
                    Mathf.Lerp(-halfWidth, halfWidth, viewportX),
                    Mathf.Lerp(-halfHeight, halfHeight, viewportY),
                    depth);
            }

            float halfPerspectiveHeight = Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * depth;
            float halfPerspectiveWidth = halfPerspectiveHeight * targetCamera.aspect;
            return new Vector3(
                Mathf.Lerp(-halfPerspectiveWidth, halfPerspectiveWidth, viewportX),
                Mathf.Lerp(-halfPerspectiveHeight, halfPerspectiveHeight, viewportY),
                depth);
        }

        private static Vector2 ComputeCenter(IReadOnlyList<Vector2> polygon)
        {
            Vector2 center = Vector2.zero;
            for (int index = 0; index < polygon.Count; index++)
            {
                center += polygon[index];
            }

            return center / polygon.Count;
        }

        private void AddOuterFrameSides(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, float frontDepth, float backDepth, float textureScale)
        {
            Vector2[] frame =
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };

            for (int index = 0; index < frame.Length; index++)
            {
                int nextIndex = (index + 1) % frame.Length;
                Vector3 frontA = ViewportToCameraLocalPoint(frame[index].x, frame[index].y, frontDepth);
                Vector3 frontB = ViewportToCameraLocalPoint(frame[nextIndex].x, frame[nextIndex].y, frontDepth);
                Vector3 backA = ViewportToCameraLocalPoint(frame[index].x, frame[index].y, backDepth);
                Vector3 backB = ViewportToCameraLocalPoint(frame[nextIndex].x, frame[nextIndex].y, backDepth);
                AddEdgeQuad(vertices, triangles, uvs, frontB, frontA, backA, backB, textureScale);
            }
        }

        private static void AddSubmeshQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD)
        {
            int startIndex = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            uvs.Add(uvA);
            uvs.Add(uvB);
            uvs.Add(uvC);
            uvs.Add(uvD);

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);
        }

        private static void AddEdgeQuad(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float textureScale)
        {
            int startIndex = vertices.Count;
            float edgeLength = Vector3.Distance(a, b) * textureScale;
            float depthLength = Vector3.Distance(b, c) * textureScale;

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(edgeLength, 0f));
            uvs.Add(new Vector2(edgeLength, depthLength));
            uvs.Add(new Vector2(0f, depthLength));

            triangles.Add(startIndex);
            triangles.Add(startIndex + 1);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex);
            triangles.Add(startIndex + 2);
            triangles.Add(startIndex + 3);
        }
    }

    private sealed class WallShapeData
    {
        public WallShapeData(string name, Rect bounds, float shrinkDuration, Vector2[] polygonVertices, WallOrbTargetData[] orbTargets, bool hasReferenceAvatarBounds, Rect referenceAvatarBounds)
        {
            Name = name;
            Bounds = bounds;
            ShrinkDuration = shrinkDuration;
            PolygonVertices = polygonVertices;
            OrbTargets = orbTargets ?? Array.Empty<WallOrbTargetData>();
            HasReferenceAvatarBounds = hasReferenceAvatarBounds;
            ReferenceAvatarBounds = referenceAvatarBounds;
        }

        public string Name { get; }
        public Rect Bounds { get; }
        public float ShrinkDuration { get; }
        public Vector2[] PolygonVertices { get; }
        public WallOrbTargetData[] OrbTargets { get; }
        public bool HasReferenceAvatarBounds { get; }
        public Rect ReferenceAvatarBounds { get; }
        public bool IsPolygon => PolygonVertices != null && PolygonVertices.Length >= 3;
    }

    private sealed class RuntimeWireframe
    {
        private const int OrbCircleSegments = 32;

        private readonly GameObject root;
        private readonly LineRenderer outline;
        private readonly List<LineRenderer> orbCircles = new List<LineRenderer>(8);

        public RuntimeWireframe(Transform parent)
        {
            root = new GameObject("RuntimeWireframe");
            root.transform.SetParent(parent, false);

            outline = root.AddComponent<LineRenderer>();
            outline.useWorldSpace = true;
            outline.loop = true;
            outline.startWidth = 0.02f;
            outline.endWidth = 0.02f;
            outline.material = CreateLineMaterial(Color.white);
            outline.positionCount = 0;
            outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.receiveShadows = false;
        }

        public void SetVisible(bool visible)
        {
            if (root != null)
            {
                root.SetActive(visible);
            }
        }

        public void UpdateOutline(IReadOnlyList<Vector2> metricPolygon, float worldZ, Color color)
        {
            outline.positionCount = metricPolygon.Count;
            outline.startColor = color;
            outline.endColor = color;
            outline.material.color = color;

            for (int index = 0; index < metricPolygon.Count; index++)
            {
                outline.SetPosition(index, new Vector3(metricPolygon[index].x, metricPolygon[index].y, worldZ));
            }
        }

        public void UpdateOrbs(IReadOnlyList<WallOrbTargetData> orbs, IReadOnlyList<bool> hitStates, float worldZ, Color inactiveColor, Color activeColor, float radiusMultiplier)
        {
            EnsureOrbCircles(orbs != null ? orbs.Count : 0);

            for (int index = 0; index < orbCircles.Count; index++)
            {
                if (orbs == null || index >= orbs.Count)
                {
                    orbCircles[index].gameObject.SetActive(false);
                    continue;
                }

                WallOrbTargetData orb = orbs[index];
                float radius = orb.radius * radiusMultiplier;
                LineRenderer circle = orbCircles[index];
                circle.gameObject.SetActive(true);
                circle.positionCount = OrbCircleSegments + 1;

                bool hit = hitStates != null && index < hitStates.Count && hitStates[index];
                Color orbColor = hit ? activeColor : inactiveColor;
                circle.startColor = orbColor;
                circle.endColor = orbColor;
                circle.material.color = orbColor;

                for (int segment = 0; segment <= OrbCircleSegments; segment++)
                {
                    float angle = (segment / (float)OrbCircleSegments) * Mathf.PI * 2f;
                    circle.SetPosition(segment, new Vector3(
                        orb.position.x + Mathf.Cos(angle) * radius,
                        orb.position.y + Mathf.Sin(angle) * radius,
                        worldZ));
                }
            }
        }

        private void EnsureOrbCircles(int count)
        {
            while (orbCircles.Count < count)
            {
                GameObject orbObj = new GameObject($"OrbCircle_{orbCircles.Count}");
                orbObj.transform.SetParent(root.transform, false);

                LineRenderer circle = orbObj.AddComponent<LineRenderer>();
                circle.useWorldSpace = true;
                circle.loop = false;
                circle.startWidth = 0.015f;
                circle.endWidth = 0.015f;
                circle.material = CreateLineMaterial(Color.white);
                circle.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                circle.receiveShadows = false;
                orbCircles.Add(circle);
            }
        }

        private static Material CreateLineMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material mat = new Material(shader);
            mat.color = color;
            mat.hideFlags = HideFlags.DontSave;
            return mat;
        }
    }

    private sealed class RuntimeOverlay
    {
        private const int FallbackOverlayTextureWidth = 512;
        private const int FallbackOverlayTextureHeight = 512;

        private readonly RawImage polygonOverlay;
        private readonly Image flash;
        private readonly List<Image> orbIndicators = new List<Image>(8);
        private readonly Material overlayMaterial;
        private readonly Vector4[] animatedPolygonBuffer = new Vector4[MaxOverlayVertices];
        private readonly Vector4[] outlinePolygonBuffer = new Vector4[MaxOverlayVertices];
        private readonly Texture2D fallbackOverlayTexture;
        private readonly Color[] fallbackOverlayPixels = new Color[FallbackOverlayTextureWidth * FallbackOverlayTextureHeight];
        private readonly bool useShader;
        private bool isVisible = true;

        public RuntimeOverlay(RectTransform parent)
        {
            polygonOverlay = CreateRawImage(parent, "PolygonOverlay");
            flash = CreateImage(parent, "Flash");

            Shader overlayShader = Shader.Find("Hidden/PoseGame/WallPolygonOverlay");
            if (overlayShader != null && overlayShader.isSupported)
            {
                overlayMaterial = new Material(overlayShader)
                {
                    hideFlags = HideFlags.DontSave,
                };
                polygonOverlay.material = overlayMaterial;
                useShader = true;
            }
            else
            {
                fallbackOverlayTexture = new Texture2D(FallbackOverlayTextureWidth, FallbackOverlayTextureHeight, TextureFormat.RGBA32, false)
                {
                    name = "WallPolygonOverlayFallback",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                fallbackOverlayTexture.SetPixels(fallbackOverlayPixels);
                fallbackOverlayTexture.Apply(false, false);
                polygonOverlay.texture = fallbackOverlayTexture;
                useShader = false;
                Debug.LogWarning("[ScreenWallFitController] Wall polygon overlay shader unavailable, using slower fallback overlay.");
            }

            Stretch(polygonOverlay.rectTransform);
            Stretch(flash.rectTransform);
            polygonOverlay.raycastTarget = false;
            flash.raycastTarget = false;
        }

        public void ApplyOverlay(IReadOnlyList<Vector2> animatedPolygon, IReadOnlyList<Vector2> outlinePolygon, float thickness, Color maskColor, Color outlineEdgeColor)
        {
            if (useShader)
            {
                ApplyPolygon("_AnimatedVertices", "_AnimatedVertexCount", animatedPolygon, animatedPolygonBuffer);
                ApplyPolygon("_OutlineVertices", "_OutlineVertexCount", outlinePolygon, outlinePolygonBuffer);
                overlayMaterial.SetFloat("_OutlineThickness", Mathf.Clamp(thickness, 0.001f, 0.05f));
                overlayMaterial.SetFloat("_EdgeSoftness", 0.0015f);
                overlayMaterial.SetColor("_MaskColor", maskColor);
                overlayMaterial.SetColor("_OutlineColor", outlineEdgeColor);
                return;
            }

            float clampedThickness = Mathf.Clamp(thickness, 0.001f, 0.05f);
            for (int y = 0; y < FallbackOverlayTextureHeight; y++)
            {
                float v = (y + 0.5f) / FallbackOverlayTextureHeight;
                for (int x = 0; x < FallbackOverlayTextureWidth; x++)
                {
                    float u = (x + 0.5f) / FallbackOverlayTextureWidth;
                    Vector2 point = new Vector2(u, v);

                    bool insideAnimated = animatedPolygon != null && animatedPolygon.Count >= 3 && ContainsPointInPolygon(point, animatedPolygon);
                    bool onOutline = outlinePolygon != null && outlinePolygon.Count >= 3 && MinDistanceToPolygon(point, outlinePolygon) <= clampedThickness;

                    Color pixelColor = insideAnimated ? Color.clear : maskColor;
                    if (onOutline)
                    {
                        pixelColor = Color.Lerp(pixelColor, outlineEdgeColor, outlineEdgeColor.a);
                        pixelColor.a = Mathf.Clamp01(pixelColor.a + outlineEdgeColor.a);
                    }

                    fallbackOverlayPixels[(y * FallbackOverlayTextureWidth) + x] = pixelColor;
                }
            }

            fallbackOverlayTexture.SetPixels(fallbackOverlayPixels);
            fallbackOverlayTexture.Apply(false, false);
        }

        public void SetFlashColor(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            flash.color = color;
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            polygonOverlay.enabled = visible;
            flash.enabled = visible;
            for (int index = 0; index < orbIndicators.Count; index++)
            {
                orbIndicators[index].gameObject.SetActive(visible);
            }
        }

        public void SetOrbTargets(IReadOnlyList<WallOrbTargetData> orbTargets, IReadOnlyList<bool> hitStates, Color inactiveColor, Color activeColor, float radiusMultiplier)
        {
            EnsureOrbIndicatorCount(orbTargets == null ? 0 : orbTargets.Count);

            RectTransform parentRect = polygonOverlay.rectTransform.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            for (int index = 0; index < orbIndicators.Count; index++)
            {
                Image orbIndicator = orbIndicators[index];
                bool isVisible = orbTargets != null && index < orbTargets.Count;
                orbIndicator.gameObject.SetActive(this.isVisible);
                orbIndicator.enabled = this.isVisible && isVisible;
                if (!isVisible)
                {
                    continue;
                }

                WallOrbTargetData orbTarget = orbTargets[index];
                RectTransform rectTransform = orbIndicator.rectTransform;
                rectTransform.anchorMin = orbTarget.position;
                rectTransform.anchorMax = orbTarget.position;
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.sizeDelta = new Vector2(
                    orbTarget.radius * radiusMultiplier * 2f * parentRect.rect.width,
                    orbTarget.radius * radiusMultiplier * 2f * parentRect.rect.height);

                bool isHit = hitStates != null && index < hitStates.Count && hitStates[index];
                orbIndicator.color = isHit ? activeColor : inactiveColor;
            }
        }

        private void ApplyPolygon(string verticesProperty, string countProperty, IReadOnlyList<Vector2> polygon, Vector4[] buffer)
        {
            int count = polygon == null ? 0 : Mathf.Min(MaxOverlayVertices, polygon.Count);
            for (int index = 0; index < buffer.Length; index++)
            {
                buffer[index] = index < count ? new Vector4(polygon[index].x, polygon[index].y, 0f, 0f) : Vector4.zero;
            }

            overlayMaterial.SetInt(countProperty, count);
            overlayMaterial.SetVectorArray(verticesProperty, buffer);
        }

        private static float MinDistanceToPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            float minDistance = float.MaxValue;
            for (int index = 0; index < polygon.Count; index++)
            {
                int nextIndex = (index + 1) % polygon.Count;
                Vector2 closestPoint = ClosestPointOnSegment(point, polygon[index], polygon[nextIndex]);
                float distance = Vector2.Distance(point, closestPoint);
                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        private static Image CreateImage(RectTransform parent, string name)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static RawImage CreateRawImage(RectTransform parent, string name)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(parent, false);
            RawImage image = imageObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private void EnsureOrbIndicatorCount(int targetCount)
        {
            while (orbIndicators.Count < targetCount)
            {
                Image indicator = CreateImage(polygonOverlay.rectTransform.parent as RectTransform, $"OrbIndicator_{orbIndicators.Count}");
                indicator.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
                indicator.type = Image.Type.Simple;
                indicator.raycastTarget = false;
                orbIndicators.Add(indicator);
            }
        }
    }
}