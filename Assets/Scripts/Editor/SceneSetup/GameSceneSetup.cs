using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class GameSceneSetup
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("PoseGame/Setup Gameplay Scene From Selection")]
    public static void SetupGameplaySceneFromSelection()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null)
        {
            EditorUtility.DisplayDialog(
                "Select A Player Model",
                "Select your player prefab or a scene object that has a Humanoid Animator, then run the setup again.",
                "OK");
            return;
        }

        GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(selectedObject);
        GameObject playerSource = sourcePrefab != null ? sourcePrefab : selectedObject;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "GameScene";

        ConfigureRenderSettings();

        Camera camera = CreateCamera();
        CreateKeyLight();
        CreateFillLight();
        CreateBackdrop();

        GameObject poseRuntime = new GameObject("PoseRuntime");
        KinectBodyTracker kinectTracker = poseRuntime.AddComponent<KinectBodyTracker>();
        KinectPoseSource kinectPoseSource = poseRuntime.AddComponent<KinectPoseSource>();
        UdpPoseSource udpPoseSource = poseRuntime.AddComponent<UdpPoseSource>();
        PriorityPoseSource priorityPoseSource = poseRuntime.AddComponent<PriorityPoseSource>();

        kinectPoseSource.Configure(kinectTracker);
        priorityPoseSource.Configure(kinectPoseSource, udpPoseSource);

        SerializedObject udpSerializedObject = new SerializedObject(udpPoseSource);
        udpSerializedObject.FindProperty("autoLaunchPythonStreamer").boolValue = false;
        udpSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject kinectSerializedObject = new SerializedObject(kinectTracker);
        kinectSerializedObject.FindProperty("autoStartOnEnable").boolValue = true;
        kinectSerializedObject.FindProperty("readColorFrames").boolValue = true;
        kinectSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        Camera kinectDebugCamera = CreateKinectDebugRig();
        KinectDebugRenderer debugRenderer = kinectDebugCamera.gameObject.AddComponent<KinectDebugRenderer>();
        debugRenderer.Configure(kinectTracker);

        GameObject playerRoot = new GameObject("PlayerRoot");
        playerRoot.transform.position = new Vector3(0f, -1.15f, 0f);

        GameObject playerInstance = sourcePrefab != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(playerSource, scene)
            : Object.Instantiate(playerSource);

        playerInstance.name = "PlayerAvatar";
        playerInstance.transform.SetParent(playerRoot.transform, false);
        playerInstance.transform.localPosition = Vector3.zero;
        playerInstance.transform.localRotation = Quaternion.identity;
        playerInstance.transform.localScale = Vector3.one;
        playerInstance.transform.rotation = Quaternion.LookRotation(camera.transform.position - playerInstance.transform.position, Vector3.up);

        Animator animator = playerInstance.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman)
        {
            Object.DestroyImmediate(playerInstance);
            EditorUtility.DisplayDialog(
                "Humanoid Animator Required",
                "The selected object must contain a Humanoid Animator.",
                "OK");
            return;
        }

        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        HumanoidPoseDriver poseDriver = playerInstance.GetComponent<HumanoidPoseDriver>();
        if (poseDriver == null)
        {
            poseDriver = playerInstance.AddComponent<HumanoidPoseDriver>();
        }

        poseDriver.Configure(priorityPoseSource, animator, playerInstance.transform, horizontalMirror: true, swapSides: true);

        GameObject wallControllerObject = new GameObject("WallController");
        ScreenWallFitController wallController = wallControllerObject.AddComponent<ScreenWallFitController>();

        SerializedObject wallSerializedObject = new SerializedObject(wallController);
        wallSerializedObject.FindProperty("useShapeFolder").boolValue = true;
        wallSerializedObject.FindProperty("shapeResourcesFolder").stringValue = "WallShapes";
        wallSerializedObject.FindProperty("targetCamera").objectReferenceValue = camera;
        wallSerializedObject.FindProperty("targetAnimator").objectReferenceValue = animator;
        wallSerializedObject.FindProperty("playOnStart").boolValue = true;
        wallSerializedObject.FindProperty("loop").boolValue = true;
        wallSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EnsureSceneInBuildSettings(ScenePath);

        Scene reopenedScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject reopenedPlayer = GameObject.Find("PlayerAvatar");
        if (reopenedPlayer != null)
        {
            Selection.activeGameObject = reopenedPlayer;
            EditorGUIUtility.PingObject(reopenedPlayer);
        }

        EditorUtility.DisplayDialog(
            "Gameplay Scene Ready",
            "Rebuilt Assets/Scenes/GameScene.unity with Kinect-first pose tracking, UDP fallback, and a display-2 debug view. The player was re-instantiated from the selected source, so any stuck authoring pose overrides are gone.",
            "OK");
    }

    private static void ConfigureRenderSettings()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.6f);
        RenderSettings.fog = false;
    }

    private static Camera CreateCamera()
    {
        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.11f, 0.14f, 0.18f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.fieldOfView = 32f;

        cameraObject.transform.position = new Vector3(0f, 1.35f, -4.5f);
        cameraObject.transform.rotation = Quaternion.identity;
        return camera;
    }

    private static Camera CreateKinectDebugRig()
    {
        GameObject rig = new GameObject("KinectDebugRig");
        rig.layer = 6;
        rig.transform.position = new Vector3(0f, -100f, 0f);

        GameObject videoScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        videoScreen.name = "VideoScreen";
        videoScreen.layer = 6;
        videoScreen.transform.SetParent(rig.transform, false);
        videoScreen.transform.localPosition = new Vector3(0f, 0f, 50f);
        videoScreen.transform.localRotation = Quaternion.identity;
        videoScreen.transform.localScale = new Vector3(32f, 18f, 1f);
        Object.DestroyImmediate(videoScreen.GetComponent<Collider>());

        GameObject cameraObject = new GameObject("Camera");
        cameraObject.layer = 6;
        cameraObject.transform.SetParent(rig.transform, false);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 6;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1000f;
        camera.depth = 0f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.orthographic = true;
        camera.orthographicSize = 9f;
        camera.targetDisplay = 1;

        cameraObject.transform.localPosition = Vector3.zero;
        cameraObject.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.forward);

        return camera;
    }

    private static void CreateKeyLight()
    {
        GameObject lightObject = new GameObject("Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.shadows = LightShadows.Soft;
        lightObject.transform.rotation = Quaternion.Euler(28f, -16f, 0f);
    }

    private static void CreateFillLight()
    {
        GameObject lightObject = new GameObject("Fill Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.55f;
        light.color = new Color(0.6f, 0.72f, 1f);
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(340f, 30f, 0f);
    }

    private static void CreateBackdrop()
    {
        GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "Backdrop";
        backdrop.transform.position = new Vector3(0f, 1.35f, 4f);
        backdrop.transform.localScale = new Vector3(8f, 5f, 1f);
        Object.DestroyImmediate(backdrop.GetComponent<Collider>());

        Material material = CreateBackdropMaterial();
        material.name = "GameSceneBackdrop";
        material.color = new Color(0.16f, 0.2f, 0.24f);
        backdrop.GetComponent<MeshRenderer>().sharedMaterial = material;
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

    private static void EnsureSceneInBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (EditorBuildSettingsScene existingScene in scenes)
        {
            if (existingScene.path == scenePath)
            {
                return;
            }
        }

        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}