using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Scoring")]
    [SerializeField, Range(0f, 0.08f)] private float fitPadding = 0.015f;

    [Header("Overlay")]
    [SerializeField, Range(0.001f, 0.03f)] private float outlineThickness = 0.006f;
    [SerializeField] private Color overlayColor = new Color(0.35f, 0.35f, 0.35f, 0.55f);
    [SerializeField] private Color outlineColor = new Color(0.7f, 0.7f, 0.7f, 0.95f);
    [SerializeField] private Color successFlashColor = new Color(0.1f, 0.85f, 0.2f, 0.6f);
    [SerializeField] private Color failureFlashColor = new Color(0.95f, 0.15f, 0.12f, 0.6f);
    [SerializeField, Min(0.05f)] private float flashDuration = 0.35f;

    private readonly List<TrackedSegment> trackedSegments = new List<TrackedSegment>(12);
    private readonly List<WallShapeData> loadedShapes = new List<WallShapeData>(16);
    private readonly List<WallShapeData> remainingShapes = new List<WallShapeData>(16);
    private readonly List<Vector2> targetShapePolygon = new List<Vector2>(16);
    private readonly List<Vector2> startShapePolygon = new List<Vector2>(16);
    private readonly List<Vector2> animatedShapePolygon = new List<Vector2>(16);
    private readonly Rect fullScreenRect = new Rect(0f, 0f, 1f, 1f);

    private Rect currentCutoutRect;
    private Rect targetCutoutRect;
    private RuntimeOverlay overlay;
    private WallState state;
    private float stateTime;
    private bool lastResultPassed;
    private float currentShrinkDuration;

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

        EnsureOverlay();
        RefreshShapeLibrary();
        CacheTrackedSegments();
        overlay.SetVisible(false);
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

    public void BeginWall()
    {
        if (!TryPrepareRun())
        {
            return;
        }

        EnsureOverlay();
        if (overlay == null)
        {
            Debug.LogError("[ScreenWallFitController] Failed to create the runtime overlay.");
            state = WallState.Idle;
            return;
        }

        currentCutoutRect = fullScreenRect;
        BuildStartPolygon(targetShapePolygon, startShapePolygon);
        CopyPolygon(startShapePolygon, animatedShapePolygon);
        stateTime = 0f;
        state = WallState.Shrinking;
        overlay.SetVisible(true);
        overlay.ApplyOverlay(animatedShapePolygon, targetShapePolygon, outlineThickness, overlayColor, outlineColor);
        overlay.SetFlashColor(Color.clear, 0f);
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

        if (targetCamera == null)
        {
            Debug.LogWarning("[ScreenWallFitController] No Camera assigned.");
            state = WallState.Idle;
            return false;
        }

        if (!useShapeFolder)
        {
            targetCutoutRect = wallDefinition.TargetViewportRect;
            currentShrinkDuration = wallDefinition.ShrinkDuration;
            SetRectPolygon(targetCutoutRect, targetShapePolygon);
        }

        CacheTrackedSegments();
        return trackedSegments.Count > 0;
    }

    private void UpdateShrink()
    {
        if (overlay == null)
        {
            EnsureOverlay();
            if (overlay == null)
            {
                Debug.LogError("[ScreenWallFitController] Overlay is null during UpdateShrink.");
                state = WallState.Idle;
                return;
            }
        }

        float duration = Mathf.Max(0.05f, currentShrinkDuration);
        stateTime += Time.deltaTime;

        float t = Mathf.Clamp01(stateTime / duration);
        LerpPolygon(startShapePolygon, targetShapePolygon, t, animatedShapePolygon);
        currentCutoutRect = BuildBounds(animatedShapePolygon);
        overlay.ApplyOverlay(animatedShapePolygon, targetShapePolygon, outlineThickness, overlayColor, outlineColor);

        if (t < 1f)
        {
            return;
        }

        lastResultPassed = EvaluateCurrentPose();

        if (!lastResultPassed)
        {
            if (gameManager == null)
            {
                gameManager = GameManager.Instance != null
                    ? GameManager.Instance
                    : FindFirstObjectByType<GameManager>();
            }

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

        stateTime = 0f;
        state = WallState.Flashing;
        overlay.SetFlashColor(lastResultPassed ? successFlashColor : failureFlashColor, 1f);
    }

    private void UpdateFlash()
    {
        if (overlay == null)
        {
            EnsureOverlay();
            if (overlay == null)
            {
                Debug.LogError("[ScreenWallFitController] Overlay is null during UpdateFlash.");
                state = WallState.Idle;
                return;
            }
        }

        stateTime += Time.deltaTime;
        float alpha = 1f - Mathf.Clamp01(stateTime / Mathf.Max(0.05f, flashDuration));
        Color flashColor = lastResultPassed ? successFlashColor : failureFlashColor;
        overlay.SetFlashColor(flashColor, alpha);
        overlay.ApplyOverlay(targetShapePolygon, targetShapePolygon, outlineThickness, overlayColor, outlineColor);

        if (alpha > 0f)
        {
            return;
        }

        if (loop)
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
            loadedShapes.Add(new WallShapeData(shape.name, shape.TargetViewportRect, shape.ShrinkDuration, BuildRectPolygonArray(shape.TargetViewportRect)));
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

            loadedShapes.Add(new WallShapeData(shape.name, BuildBounds(vertices), shape.ShrinkDuration, vertices));
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
        Debug.Log($"[ScreenWallFitController] Selected wall shape '{selectedShape.Name}' with {selectedShape.PolygonVertices.Length} vertices.");

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

        for (int index = 0; index < trackedSegments.Count; index++)
        {
            TrackedSegment segment = trackedSegments[index];
            if (segment.start == null || segment.end == null)
            {
                continue;
            }

            int samples = Mathf.Max(2, segment.sampleCount);
            for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
            {
                float t = samples == 1 ? 0f : sampleIndex / (float)(samples - 1);
                Vector3 worldPoint = Vector3.Lerp(segment.start.position, segment.end.position, t);
                Vector3 viewportPoint = targetCamera.WorldToViewportPoint(worldPoint);

                if (viewportPoint.z <= 0f)
                {
                    return false;
                }

                if (!scoringRect.Contains(new Vector2(viewportPoint.x, viewportPoint.y)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool EvaluateCurrentPoseAgainstPolygon()
    {
        for (int index = 0; index < trackedSegments.Count; index++)
        {
            TrackedSegment segment = trackedSegments[index];
            if (segment.start == null || segment.end == null)
            {
                continue;
            }

            int samples = Mathf.Max(2, segment.sampleCount);
            for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
            {
                float t = samples == 1 ? 0f : sampleIndex / (float)(samples - 1);
                Vector3 worldPoint = Vector3.Lerp(segment.start.position, segment.end.position, t);
                Vector3 viewportPoint = targetCamera.WorldToViewportPoint(worldPoint);

                if (viewportPoint.z <= 0f)
                {
                    return false;
                }

                if (!ContainsPointInPolygonWithPadding(new Vector2(viewportPoint.x, viewportPoint.y), targetShapePolygon, fitPadding))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void CacheTrackedSegments()
    {
        trackedSegments.Clear();

        if (targetAnimator == null || !targetAnimator.isHuman)
        {
            return;
        }

        AddBoneSegment(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 4);
        AddBoneSegment(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 4);
        AddBoneSegment(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 4);
        AddBoneSegment(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 4);
        AddBoneSegment(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 4);
        AddBoneSegment(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 4);
        AddBoneSegment(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 4);
        AddBoneSegment(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 4);

        Transform leftUpperArm = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightUpperArm = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform leftUpperLeg = targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rightUpperLeg = targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        Transform chest = targetAnimator.GetBoneTransform(HumanBodyBones.Chest);
        Transform hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
        Transform head = targetAnimator.GetBoneTransform(HumanBodyBones.Head);

        AddSegment(leftUpperArm, rightUpperArm, 5);
        AddSegment(leftUpperLeg, rightUpperLeg, 5);
        AddSegment(chest, hips, 5);
        AddSegment(head, head, 1);
    }

    private void AddBoneSegment(HumanBodyBones startBone, HumanBodyBones endBone, int samples)
    {
        AddSegment(
            targetAnimator.GetBoneTransform(startBone),
            targetAnimator.GetBoneTransform(endBone),
            samples);
    }

    private void AddSegment(Transform start, Transform end, int samples)
    {
        if (start == null || end == null)
        {
            return;
        }

        trackedSegments.Add(new TrackedSegment
        {
            start = start,
            end = end,
            sampleCount = Mathf.Max(1, samples),
        });
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

    private static Rect LerpRect(Rect from, Rect to, float t)
    {
        Vector2 min = Vector2.Lerp(from.min, to.min, t);
        Vector2 max = Vector2.Lerp(from.max, to.max, t);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static Rect ExpandRect(Rect source, float amount)
    {
        return Rect.MinMaxRect(
            Mathf.Clamp01(source.xMin - amount),
            Mathf.Clamp01(source.yMin - amount),
            Mathf.Clamp01(source.xMax + amount),
            Mathf.Clamp01(source.yMax + amount));
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

        Vector2 centroid = Vector2.zero;
        for (int index = 0; index < targetPolygon.Count; index++)
        {
            centroid += targetPolygon[index];
        }

        centroid /= targetPolygon.Count;

        float uniformScale = 1f;

        for (int index = 0; index < targetPolygon.Count; index++)
        {
            Vector2 target = targetPolygon[index];
            Vector2 direction = target - centroid;
            if (direction.sqrMagnitude < 0.000001f)
            {
                continue;
            }

            float scale = float.PositiveInfinity;
            if (direction.x > 0f)
            {
                scale = Mathf.Min(scale, (1f - centroid.x) / direction.x);
            }
            else if (direction.x < 0f)
            {
                scale = Mathf.Min(scale, (0f - centroid.x) / direction.x);
            }

            if (direction.y > 0f)
            {
                scale = Mathf.Min(scale, (1f - centroid.y) / direction.y);
            }
            else if (direction.y < 0f)
            {
                scale = Mathf.Min(scale, (0f - centroid.y) / direction.y);
            }

            if (float.IsInfinity(scale) || float.IsNaN(scale) || scale < 0f)
            {
                scale = 1f;
            }

            uniformScale = Mathf.Max(uniformScale, scale);
        }

        uniformScale *= 1.01f;

        for (int index = 0; index < targetPolygon.Count; index++)
        {
            Vector2 target = targetPolygon[index];
            Vector2 direction = target - centroid;
            if (direction.sqrMagnitude < 0.000001f)
            {
                destination.Add(target);
                continue;
            }

            destination.Add(centroid + (direction * uniformScale));
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

            float interpolatedX = ((previous.x - current.x) * (point.y - current.y) / Mathf.Max(0.000001f, previous.y - current.y)) + current.x;
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

    private static Vector2 ClampViewportPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }

    private struct TrackedSegment
    {
        public Transform start;
        public Transform end;
        public int sampleCount;
    }

    private sealed class WallShapeData
    {
        public WallShapeData(string name, Rect bounds, float shrinkDuration, Vector2[] polygonVertices)
        {
            Name = name;
            Bounds = bounds;
            ShrinkDuration = shrinkDuration;
            PolygonVertices = polygonVertices;
        }

        public string Name { get; }
        public Rect Bounds { get; }
        public float ShrinkDuration { get; }
        public Vector2[] PolygonVertices { get; }
        public bool IsPolygon => PolygonVertices != null && PolygonVertices.Length >= 3;
    }

    private sealed class RuntimeOverlay
    {
        private const int FallbackOverlayTextureWidth = 512;
        private const int FallbackOverlayTextureHeight = 512;

        private readonly RawImage polygonOverlay;
        private readonly Image flash;
        private readonly Material overlayMaterial;
        private readonly Vector4[] animatedPolygonBuffer = new Vector4[MaxOverlayVertices];
        private readonly Vector4[] outlinePolygonBuffer = new Vector4[MaxOverlayVertices];
        private readonly Texture2D fallbackOverlayTexture;
        private readonly Color[] fallbackOverlayPixels = new Color[FallbackOverlayTextureWidth * FallbackOverlayTextureHeight];
        private readonly bool useShader;

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
            polygonOverlay.enabled = visible;
            flash.enabled = visible;
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
    }
}