using UnityEditor;
using UnityEditor.SceneManagement;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PoseCreatorSceneSetup
{
    private const string ScenePath = "Assets/Scenes/PoseCreator.unity";

    [MenuItem("PoseGame/Wall Fit/Setup Pose Creator Scene From Selection")]
    public static void SetupPoseCreatorSceneFromSelection()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        GameObject source = Selection.activeGameObject;
        if (source == null)
        {
            EditorUtility.DisplayDialog(
                "Select A Player Model",
                "Select your player prefab or a scene object that has a Humanoid Animator, then run the setup again.",
                "OK");
            return;
        }

        GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(source);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "PoseCreator";

        GameObject rigObject = new GameObject("PoseShapeAuthoringRig");
        PoseShapeAuthoringRig rig = rigObject.AddComponent<PoseShapeAuthoringRig>();

        Camera camera = CreateCamera();
        GameObject keyLight = CreateKeyLight();
        GameObject fillLight = CreateFillLight();
        GameObject backdrop = CreateBackdrop();

        GameObject playerRoot = new GameObject("PoseCreatorPlayerRoot");
        playerRoot.transform.SetParent(rigObject.transform, false);
        playerRoot.transform.position = Vector3.zero;

        GameObject playerInstance = sourcePrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, scene)
            : Object.Instantiate(source);

        GameObject referenceInstance = sourcePrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab, scene)
            : Object.Instantiate(source);

        playerInstance.name = "PoseCreatorPlayer";
        playerInstance.transform.SetParent(playerRoot.transform, false);
        playerInstance.transform.localPosition = Vector3.zero;
        playerInstance.transform.localRotation = Quaternion.identity;
        playerInstance.transform.localScale = Vector3.one;

        referenceInstance.name = "PoseCreatorReferencePose";
        referenceInstance.transform.SetParent(rigObject.transform, false);
        referenceInstance.transform.localPosition = Vector3.zero;
        referenceInstance.transform.localRotation = Quaternion.identity;
        referenceInstance.transform.localScale = Vector3.one;
        referenceInstance.SetActive(false);
        referenceInstance.hideFlags = HideFlags.HideInHierarchy | HideFlags.NotEditable;

        Animator animator = playerInstance.GetComponentInChildren<Animator>();
        Animator referenceAnimator = referenceInstance.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Object.DestroyImmediate(playerInstance);
            Object.DestroyImmediate(referenceInstance);
            EditorUtility.DisplayDialog(
                "Humanoid Animator Required",
                "The selected object must contain a Humanoid Animator.",
                "OK");
            return;
        }

        if (referenceAnimator == null || !referenceAnimator.isHuman)
        {
            Object.DestroyImmediate(playerInstance);
            Object.DestroyImmediate(referenceInstance);
            EditorUtility.DisplayDialog(
                "Humanoid Animator Required",
                "The selected object must contain a Humanoid Animator.",
                "OK");
            return;
        }

        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        SerializedObject rigSerializedObject = new SerializedObject(rig);
        rigSerializedObject.FindProperty("targetAnimator").objectReferenceValue = animator;
        rigSerializedObject.FindProperty("authoringCamera").objectReferenceValue = camera;
        rigSerializedObject.FindProperty("referencePoseAnimator").objectReferenceValue = referenceAnimator;
        rigSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        rig.EnsureTargetHandlesCreated();
        rig.CaptureImportedPose();
        rig.ResetPoseToImportedPose();
        rig.ResetShapeToRectangle();
        DisableScenePicking(playerInstance, camera.gameObject, keyLight, fillLight, backdrop);
        HideComponentIcons();

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = rig.gameObject;
        EditorGUIUtility.PingObject(rig.gameObject);

        EditorUtility.DisplayDialog(
            "Pose Creator Ready",
            "Created Assets/Scenes/PoseCreator.unity. The avatar is front-facing, avatar picking is disabled, and the authoring rig is selected.",
            "OK");
    }

    private static void DisableScenePicking(params GameObject[] roots)
    {
        SceneVisibilityManager visibilityManager = SceneVisibilityManager.instance;
        foreach (GameObject root in roots)
        {
            if (root != null)
            {
                visibilityManager.DisablePicking(root, true);
            }
        }
    }

    private static void HideComponentIcons()
    {
        TrySetIconEnabled(20, string.Empty, 0);
        TrySetIconEnabled(108, string.Empty, 0);
    }

    private static void TrySetIconEnabled(int classId, string scriptClass, int enabled)
    {
        System.Type annotationUtilityType = typeof(Editor).Assembly.GetType("UnityEditor.AnnotationUtility");
        if (annotationUtilityType == null)
        {
            return;
        }

        MethodInfo setIconEnabledMethod = annotationUtilityType.GetMethod(
            "SetIconEnabled",
            BindingFlags.Static | BindingFlags.NonPublic,
            null,
            new[] { typeof(int), typeof(string), typeof(int) },
            null);

        if (setIconEnabledMethod == null)
        {
            return;
        }

        setIconEnabledMethod.Invoke(null, new object[] { classId, scriptClass, enabled });
    }

    private static Camera CreateCamera()
    {
        GameObject cameraObject = new GameObject("Authoring Camera");
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<FixedAspectCamera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.11f, 0.14f, 0.18f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.fieldOfView = 32f;

        cameraObject.transform.position = new Vector3(0f, 1.35f, -4.5f);
        cameraObject.transform.rotation = Quaternion.identity;
        return camera;
    }

    private static GameObject CreateKeyLight()
    {
        GameObject lightObject = new GameObject("Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(28f, -16f, 0f);
        return lightObject;
    }

    private static GameObject CreateFillLight()
    {
        GameObject lightObject = new GameObject("Fill Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.55f;
        light.color = new Color(0.6f, 0.72f, 1f);
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(340f, 30f, 0f);
        return lightObject;
    }

    private static GameObject CreateBackdrop()
    {
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "Backdrop";
        backdrop.transform.position = new Vector3(0f, 1.35f, 4f);
        backdrop.transform.localScale = new Vector3(8f, 5f, 1f);
        Object.DestroyImmediate(backdrop.GetComponent<Collider>());

        Material material = CreateBackdropMaterial();
        material.name = "PoseCreatorBackdrop";
        material.color = new Color(0.16f, 0.2f, 0.24f);
        backdrop.GetComponent<MeshRenderer>().sharedMaterial = material;
        return backdrop;
    }

    private static Material CreateBackdropMaterial()
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        return new Material(shader);
    }
}