using System.Collections.Generic;
using UnityEngine;
using Windows.Kinect;

[RequireComponent(typeof(Camera))]
public class KinectDebugRenderer : MonoBehaviour
{
    private const int DebugLayer = 6;
    private const string VideoScreenName = "VideoScreen";

    [SerializeField] private KinectBodyTracker tracker;
    [SerializeField] private bool showColorFeed = true;
    [SerializeField] private bool mirrorView = true;
    [SerializeField] private bool flipY = true;
    [SerializeField] private bool skeletonFlipY = true;
    [SerializeField] private float skeletonZOffset = 0.5f;
    [SerializeField] private float skeletonLineWidth = 0.05f;
    [SerializeField] private Color trackedBoneColor = new Color(0.2f, 1f, 0.35f, 1f);
    [SerializeField] private Color inferredBoneColor = new Color(1f, 0.45f, 0.15f, 1f);
    [SerializeField] private bool showJointMarkers = true;
    [SerializeField] private float jointMarkerSize = 0.16f;
    [SerializeField] private float highlightedJointMarkerSize = 0.28f;
    [SerializeField] private Color trackedJointColor = new Color(0.15f, 0.95f, 1f, 1f);
    [SerializeField] private Color inferredJointColor = new Color(1f, 0.85f, 0.2f, 1f);

    private Camera targetCamera;
    private Material skeletonMaterial;
    private Material jointMarkerMaterial;
    private MeshRenderer videoScreenRenderer;
    private readonly Dictionary<ulong, GameObject> bodyVisuals = new Dictionary<ulong, GameObject>();

    private static readonly Dictionary<JointType, JointType> BoneMap = new Dictionary<JointType, JointType>
    {
        { JointType.FootLeft, JointType.AnkleLeft },
        { JointType.AnkleLeft, JointType.KneeLeft },
        { JointType.KneeLeft, JointType.HipLeft },
        { JointType.HipLeft, JointType.SpineBase },
        { JointType.FootRight, JointType.AnkleRight },
        { JointType.AnkleRight, JointType.KneeRight },
        { JointType.KneeRight, JointType.HipRight },
        { JointType.HipRight, JointType.SpineBase },
        { JointType.HandTipLeft, JointType.HandLeft },
        { JointType.ThumbLeft, JointType.HandLeft },
        { JointType.HandLeft, JointType.WristLeft },
        { JointType.WristLeft, JointType.ElbowLeft },
        { JointType.ElbowLeft, JointType.ShoulderLeft },
        { JointType.ShoulderLeft, JointType.SpineShoulder },
        { JointType.HandTipRight, JointType.HandRight },
        { JointType.ThumbRight, JointType.HandRight },
        { JointType.HandRight, JointType.WristRight },
        { JointType.WristRight, JointType.ElbowRight },
        { JointType.ElbowRight, JointType.ShoulderRight },
        { JointType.ShoulderRight, JointType.SpineShoulder },
        { JointType.SpineBase, JointType.SpineMid },
        { JointType.SpineMid, JointType.SpineShoulder },
        { JointType.SpineShoulder, JointType.Neck },
        { JointType.Neck, JointType.Head },
    };

    private static readonly JointType[] JointTypes = CreateJointTypes();

    public void Configure(KinectBodyTracker assignedTracker)
    {
        tracker = assignedTracker;
    }

    private void Reset()
    {
        tracker = FindAnyObjectByType<KinectBodyTracker>();
        targetCamera = GetComponent<Camera>();
    }

    private void Awake()
    {
        if (Display.displays.Length > 1 && !Display.displays[1].active)
        {
            Display.displays[1].Activate();
        }

        targetCamera = GetComponent<Camera>();
    }

    private void OnEnable()
    {
        if (tracker == null)
        {
            tracker = FindAnyObjectByType<KinectBodyTracker>();
        }

        if (skeletonZOffset < 0f)
        {
            skeletonZOffset = Mathf.Abs(skeletonZOffset);
        }

        targetCamera = GetComponent<Camera>();
        if (targetCamera != null)
        {
            targetCamera.targetDisplay = 1;
        }

        EnsureDebugCameraSetup();
        EnsureVideoScreen();
        EnsureSkeletonMaterial();
    }

    private void OnDisable()
    {
        ClearBodyVisuals();

        if (skeletonMaterial != null)
        {
            DestroyImmediate(skeletonMaterial);
            skeletonMaterial = null;
        }

        if (jointMarkerMaterial != null)
        {
            DestroyImmediate(jointMarkerMaterial);
            jointMarkerMaterial = null;
        }
    }

    private void OnPostRender()
    {
    }

    private void LateUpdate()
    {
        UpdateVideoScreen();
        UpdateSkeletonVisuals();
    }

    private void EnsureSkeletonMaterial()
    {
        if (skeletonMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return;
        }

        skeletonMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        ConfigureOverlayMaterial(skeletonMaterial);
        skeletonMaterial.renderQueue = 5000;
    }

    private void EnsureJointMarkerMaterial()
    {
        if (jointMarkerMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return;
        }

        jointMarkerMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave,
            color = trackedJointColor,
        };
        ConfigureOverlayMaterial(jointMarkerMaterial);
        jointMarkerMaterial.renderQueue = 5000;
    }

    private void EnsureDebugCameraSetup()
    {
        if (targetCamera == null)
        {
            return;
        }

        targetCamera.orthographic = true;
        targetCamera.orthographicSize = 9f;
        targetCamera.clearFlags = CameraClearFlags.SolidColor;
        targetCamera.backgroundColor = Color.black;
        targetCamera.cullingMask = 1 << DebugLayer;
        targetCamera.depth = 0f;
        targetCamera.nearClipPlane = 0.3f;
        targetCamera.farClipPlane = 1000f;
        targetCamera.allowHDR = false;
        targetCamera.allowMSAA = false;

        Transform cameraTransform = targetCamera.transform;
        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.AngleAxis(180f, Vector3.forward);
    }

    private void EnsureVideoScreen()
    {
        if (videoScreenRenderer != null)
        {
            return;
        }

        Transform existing = transform.parent != null ? transform.parent.Find(VideoScreenName) : transform.Find(VideoScreenName);
        GameObject screenObject;
        if (existing != null)
        {
            screenObject = existing.gameObject;
        }
        else
        {
            screenObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenObject.name = VideoScreenName;
            Transform parent = transform.parent != null ? transform.parent : transform;
            screenObject.transform.SetParent(parent, false);
            Collider collider = screenObject.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        screenObject.layer = DebugLayer;
        screenObject.transform.localPosition = new Vector3(0f, 0f, 50f);
        screenObject.transform.localRotation = Quaternion.identity;
        screenObject.transform.localScale = new Vector3(32f, 18f, 1f);

        videoScreenRenderer = screenObject.GetComponent<MeshRenderer>();
        if (videoScreenRenderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader != null && (videoScreenRenderer.sharedMaterial == null || videoScreenRenderer.sharedMaterial.shader != shader))
        {
            videoScreenRenderer.sharedMaterial = new Material(shader)
            {
                name = "KinectVideoScreenMaterial",
            };
        }
    }

    private void UpdateVideoScreen()
    {
        if (videoScreenRenderer == null)
        {
            EnsureVideoScreen();
        }

        if (videoScreenRenderer == null || videoScreenRenderer.sharedMaterial == null)
        {
            return;
        }

        Texture texture = showColorFeed && tracker != null ? tracker.ColorTexture : null;
        videoScreenRenderer.enabled = texture != null;
        if (texture == null)
        {
            return;
        }

        videoScreenRenderer.sharedMaterial.mainTexture = texture;

        Vector2 scale = new Vector2(mirrorView ? -1f : 1f, flipY ? -1f : 1f);
        Vector2 offset = new Vector2(mirrorView ? 1f : 0f, flipY ? 1f : 0f);
        videoScreenRenderer.sharedMaterial.mainTextureScale = scale;
        videoScreenRenderer.sharedMaterial.mainTextureOffset = offset;
    }

    private void UpdateSkeletonVisuals()
    {
        if (tracker == null || !tracker.IsInitialized)
        {
            ClearBodyVisuals();
            return;
        }

        Body[] bodies = tracker.Bodies;
        if (bodies == null)
        {
            ClearBodyVisuals();
            return;
        }

        EnsureSkeletonMaterial();
        if (skeletonMaterial == null)
        {
            return;
        }

        EnsureJointMarkerMaterial();

        HashSet<ulong> trackedIds = new HashSet<ulong>();
        for (int index = 0; index < bodies.Length; index++)
        {
            Body body = bodies[index];
            if (body == null || !body.IsTracked)
            {
                continue;
            }

            trackedIds.Add(body.TrackingId);
            if (!bodyVisuals.TryGetValue(body.TrackingId, out GameObject bodyRoot) || bodyRoot == null)
            {
                bodyRoot = CreateBodyVisual(body.TrackingId);
                bodyVisuals[body.TrackingId] = bodyRoot;
            }

            RefreshBodyVisual(body, bodyRoot);
        }

        List<ulong> knownIds = new List<ulong>(bodyVisuals.Keys);
        for (int index = 0; index < knownIds.Count; index++)
        {
            ulong trackingId = knownIds[index];
            if (trackedIds.Contains(trackingId))
            {
                continue;
            }

            if (bodyVisuals[trackingId] != null)
            {
                Destroy(bodyVisuals[trackingId]);
            }

            bodyVisuals.Remove(trackingId);
        }
    }

    private GameObject CreateBodyVisual(ulong trackingId)
    {
        GameObject bodyRoot = new GameObject($"Body:{trackingId}");
        bodyRoot.layer = DebugLayer;
        bodyRoot.transform.SetParent(transform.parent != null ? transform.parent : transform, false);

        foreach (KeyValuePair<JointType, JointType> bone in BoneMap)
        {
            GameObject jointObject = new GameObject(bone.Key.ToString());
            jointObject.layer = DebugLayer;
            jointObject.transform.SetParent(bodyRoot.transform, false);

            LineRenderer lineRenderer = jointObject.AddComponent<LineRenderer>();
            lineRenderer.sharedMaterial = skeletonMaterial;
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = skeletonLineWidth;
            lineRenderer.numCapVertices = 2;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.textureMode = LineTextureMode.Stretch;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.sortingOrder = 10;
            lineRenderer.enabled = false;
        }

        foreach (JointType jointType in JointTypes)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"Marker:{jointType}";
            marker.layer = DebugLayer;
            marker.transform.SetParent(bodyRoot.transform, false);
            marker.transform.localScale = Vector3.one * GetMarkerSize(jointType);

            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            MeshRenderer markerRenderer = marker.GetComponent<MeshRenderer>();
            if (markerRenderer != null && jointMarkerMaterial != null)
            {
                markerRenderer.sharedMaterial = jointMarkerMaterial;
                markerRenderer.enabled = false;
            }
        }

        return bodyRoot;
    }

    private void RefreshBodyVisual(Body body, GameObject bodyRoot)
    {
        foreach (KeyValuePair<JointType, JointType> bone in BoneMap)
        {
            Transform jointTransform = bodyRoot.transform.Find(bone.Key.ToString());
            if (jointTransform == null)
            {
                continue;
            }

            LineRenderer lineRenderer = jointTransform.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                continue;
            }

            Windows.Kinect.Joint sourceJoint = body.Joints[bone.Key];
            Windows.Kinect.Joint targetJoint = body.Joints[bone.Value];

            if (sourceJoint.TrackingState == TrackingState.NotTracked || targetJoint.TrackingState == TrackingState.NotTracked)
            {
                lineRenderer.enabled = false;
                continue;
            }

            if (!TryMapJointToWorld(sourceJoint, out Vector3 sourcePosition) || !TryMapJointToWorld(targetJoint, out Vector3 targetPosition))
            {
                lineRenderer.enabled = false;
                continue;
            }

            jointTransform.position = sourcePosition;
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, sourcePosition);
            lineRenderer.SetPosition(1, targetPosition);

            Color lineColor = sourceJoint.TrackingState == TrackingState.Tracked && targetJoint.TrackingState == TrackingState.Tracked
                ? trackedBoneColor
                : inferredBoneColor;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor = lineColor;
        }

        RefreshJointMarkers(body, bodyRoot);
    }

    private void RefreshJointMarkers(Body body, GameObject bodyRoot)
    {
        foreach (JointType jointType in JointTypes)
        {
            Transform markerTransform = bodyRoot.transform.Find($"Marker:{jointType}");
            if (markerTransform == null)
            {
                continue;
            }

            MeshRenderer markerRenderer = markerTransform.GetComponent<MeshRenderer>();
            if (markerRenderer == null)
            {
                continue;
            }

            if (!showJointMarkers)
            {
                markerRenderer.enabled = false;
                continue;
            }

            Windows.Kinect.Joint joint = body.Joints[jointType];
            if (joint.TrackingState == TrackingState.NotTracked || !TryMapJointToWorld(joint, out Vector3 worldPoint))
            {
                markerRenderer.enabled = false;
                continue;
            }

            markerTransform.position = worldPoint;
            markerTransform.localScale = Vector3.one * GetMarkerSize(jointType);
            markerRenderer.enabled = true;
            markerRenderer.material.color = joint.TrackingState == TrackingState.Tracked ? trackedJointColor : inferredJointColor;
        }
    }

    private bool TryMapJointToWorld(Windows.Kinect.Joint joint, out Vector3 worldPoint)
    {
        worldPoint = default;

        CoordinateMapper coordinateMapper = tracker != null ? tracker.CoordinateMapper : null;
        if (coordinateMapper == null || videoScreenRenderer == null || tracker.ColorWidth <= 0 || tracker.ColorHeight <= 0)
        {
            return false;
        }

        ColorSpacePoint colorPoint = coordinateMapper.MapCameraPointToColorSpace(joint.Position);
        if (float.IsNaN(colorPoint.X) || float.IsNaN(colorPoint.Y) || float.IsInfinity(colorPoint.X) || float.IsInfinity(colorPoint.Y))
        {
            return false;
        }

        float xNorm = colorPoint.X / tracker.ColorWidth;
        float yNorm = colorPoint.Y / tracker.ColorHeight;

        Vector3 scale = videoScreenRenderer.transform.localScale;
        float x = mirrorView
            ? (1f - xNorm - 0.5f) * scale.x
            : (xNorm - 0.5f) * scale.x;

        float y = skeletonFlipY
            ? (yNorm - 0.5f) * scale.y
            : (1f - yNorm - 0.5f) * scale.y;

        worldPoint = videoScreenRenderer.transform.position +
                     (videoScreenRenderer.transform.right * x) +
                     (videoScreenRenderer.transform.up * y) -
                     (videoScreenRenderer.transform.forward * skeletonZOffset);
        return true;
    }

    private void ClearBodyVisuals()
    {
        List<ulong> ids = new List<ulong>(bodyVisuals.Keys);
        for (int index = 0; index < ids.Count; index++)
        {
            GameObject bodyRoot = bodyVisuals[ids[index]];
            if (bodyRoot != null)
            {
                Destroy(bodyRoot);
            }
        }

        bodyVisuals.Clear();
    }

    private static JointType[] CreateJointTypes()
    {
        HashSet<JointType> joints = new HashSet<JointType>();
        foreach (KeyValuePair<JointType, JointType> bone in BoneMap)
        {
            joints.Add(bone.Key);
            joints.Add(bone.Value);
        }

        JointType[] jointTypes = new JointType[joints.Count];
        joints.CopyTo(jointTypes);
        return jointTypes;
    }

    private float GetMarkerSize(JointType jointType)
    {
        return IsHighlightedJoint(jointType) ? highlightedJointMarkerSize : jointMarkerSize;
    }

    private static bool IsHighlightedJoint(JointType jointType)
    {
        return jointType == JointType.Head ||
               jointType == JointType.ElbowLeft ||
               jointType == JointType.ElbowRight ||
               jointType == JointType.KneeLeft ||
               jointType == JointType.KneeRight;
    }

    private static void ConfigureOverlayMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 0);
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
    }
}