using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class GameSceneSetup
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("PoseGame/Upgrade Current Gameplay Scene In Place")]
    public static void UpgradeCurrentGameplaySceneInPlace()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog(
                "No Open Scene",
                "Open the gameplay scene you want to upgrade, then run the command again.",
                "OK");
            return;
        }

        ConfigureRenderSettings();

        Camera camera = FindOrCreateMainCamera(scene);
        EnsureLight("Key Light", 1.35f, new Color(1f, 0.96f, 0.9f), LightShadows.Soft, Quaternion.Euler(28f, -16f, 0f));
        EnsureLight("Fill Light", 0.55f, new Color(0.6f, 0.72f, 1f), LightShadows.None, Quaternion.Euler(340f, 30f, 0f));

        GameObject poseRuntime = FindOrCreateRootObject(scene, "PoseRuntime");
        KinectBodyTracker kinectTracker = AddOrGetComponent<KinectBodyTracker>(poseRuntime);
        KinectPoseSource kinectPoseSource = AddOrGetComponent<KinectPoseSource>(poseRuntime);
        UdpPoseSource udpPoseSource = AddOrGetComponent<UdpPoseSource>(poseRuntime);
        PriorityPoseSource priorityPoseSource = AddOrGetComponent<PriorityPoseSource>(poseRuntime);

        kinectPoseSource.Configure(kinectTracker);
        priorityPoseSource.Configure(kinectPoseSource, udpPoseSource);

        SerializedObject udpSerializedObject = new SerializedObject(udpPoseSource);
        udpSerializedObject.FindProperty("autoLaunchPythonStreamer").boolValue = false;
        udpSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject kinectSerializedObject = new SerializedObject(kinectTracker);
        kinectSerializedObject.FindProperty("autoStartOnEnable").boolValue = true;
        kinectSerializedObject.FindProperty("readColorFrames").boolValue = true;
        kinectSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        Camera kinectDebugCamera = FindOrCreateKinectDebugRig(scene);
        KinectDebugRenderer debugRenderer = AddOrGetComponent<KinectDebugRenderer>(kinectDebugCamera.gameObject);
        debugRenderer.Configure(kinectTracker);

        Animator animator = FindGameplayAnimator();
        if (animator == null || !animator.isHuman)
        {
            EditorUtility.DisplayDialog(
                "Humanoid Animator Required",
                "Could not find a humanoid Animator in the open scene. Open the gameplay scene with your player avatar first.",
                "OK");
            return;
        }

        GameObject playerRoot = animator.transform.parent != null ? animator.transform.parent.gameObject : animator.gameObject;
        HumanoidPoseDriver poseDriver = animator.GetComponent<HumanoidPoseDriver>();
        if (poseDriver == null)
        {
            poseDriver = animator.gameObject.AddComponent<HumanoidPoseDriver>();
        }

        poseDriver.Configure(priorityPoseSource, animator, animator.transform, horizontalMirror: true, swapSides: true);

        GameObject wallControllerObject = FindOrCreateRootObject(scene, "WallController");
        ScreenWallFitController wallController = AddOrGetComponent<ScreenWallFitController>(wallControllerObject);
        SerializedObject wallSerializedObject = new SerializedObject(wallController);
        wallSerializedObject.FindProperty("useShapeFolder").boolValue = true;
        wallSerializedObject.FindProperty("shapeResourcesFolder").stringValue = "WallShapes";
        wallSerializedObject.FindProperty("targetCamera").objectReferenceValue = camera;
        wallSerializedObject.FindProperty("targetAnimator").objectReferenceValue = animator;
        wallSerializedObject.FindProperty("playOnStart").boolValue = true;
        wallSerializedObject.FindProperty("loop").boolValue = true;
        wallSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        RuntimeUiReferences uiReferences = EnsureRuntimeUi(playerRoot.transform, scene);

        GameObject managerObject = FindOrCreateRootObject(scene, "GameManager");
        GameManager gameManager = AddOrGetComponent<GameManager>(managerObject);
        SerializedObject managerSerializedObject = new SerializedObject(gameManager);
        managerSerializedObject.FindProperty("playerTransform").objectReferenceValue = playerRoot.transform;
        managerSerializedObject.FindProperty("hudScreen").objectReferenceValue = uiReferences.hudScreen;
        managerSerializedObject.FindProperty("startScreen").objectReferenceValue = uiReferences.startScreen;
        managerSerializedObject.FindProperty("gameOverScreen").objectReferenceValue = uiReferences.gameOverScreen;
        managerSerializedObject.FindProperty("settingsScreen").objectReferenceValue = uiReferences.settingsScreen;
        managerSerializedObject.FindProperty("hudText").objectReferenceValue = uiReferences.hudText;
        managerSerializedObject.FindProperty("startPromptText").objectReferenceValue = uiReferences.startText;
        managerSerializedObject.FindProperty("gameOverSummaryText").objectReferenceValue = uiReferences.gameOverText;
        managerSerializedObject.FindProperty("gameplaySceneName").stringValue = scene.name;
        managerSerializedObject.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeObject = wallControllerObject;
        EditorGUIUtility.PingObject(wallControllerObject);

        EditorUtility.DisplayDialog(
            "Gameplay Scene Upgraded",
            "Updated the open scene in place: camera framing helper, pose runtime, Kinect debug rig, wall controller wiring, and runtime UI/GameManager references were patched without rebuilding the scene.",
            "OK");
    }

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

        RuntimeUiReferences uiReferences = EnsureRuntimeUi(playerRoot.transform, scene);

        GameObject managerObject = FindOrCreateRootObject(scene, "GameManager");
        GameManager gameManager = AddOrGetComponent<GameManager>(managerObject);
        SerializedObject managerSerializedObject = new SerializedObject(gameManager);
        managerSerializedObject.FindProperty("playerTransform").objectReferenceValue = playerRoot.transform;
        managerSerializedObject.FindProperty("hudScreen").objectReferenceValue = uiReferences.hudScreen;
        managerSerializedObject.FindProperty("startScreen").objectReferenceValue = uiReferences.startScreen;
        managerSerializedObject.FindProperty("gameOverScreen").objectReferenceValue = uiReferences.gameOverScreen;
        managerSerializedObject.FindProperty("settingsScreen").objectReferenceValue = uiReferences.settingsScreen;
        managerSerializedObject.FindProperty("hudText").objectReferenceValue = uiReferences.hudText;
        managerSerializedObject.FindProperty("startPromptText").objectReferenceValue = uiReferences.startText;
        managerSerializedObject.FindProperty("gameOverSummaryText").objectReferenceValue = uiReferences.gameOverText;
        managerSerializedObject.FindProperty("gameplaySceneName").stringValue = "GameScene";
        managerSerializedObject.ApplyModifiedPropertiesWithoutUndo();

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

    private static Camera FindOrCreateMainCamera(Scene scene)
    {
        Camera camera = Camera.main;
        if (camera != null)
        {
            if (camera.GetComponent<FixedAspectCamera>() == null)
            {
                camera.gameObject.AddComponent<FixedAspectCamera>();
            }

            return camera;
        }

        GameObject existing = GameObject.Find("Main Camera");
        if (existing != null)
        {
            Camera existingCamera = AddOrGetComponent<Camera>(existing);
            if (existing.GetComponent<FixedAspectCamera>() == null)
            {
                existing.AddComponent<FixedAspectCamera>();
            }

            return existingCamera;
        }

        return CreateCamera();
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

    private static Camera FindOrCreateKinectDebugRig(Scene scene)
    {
        GameObject rig = GameObject.Find("KinectDebugRig");
        if (rig != null)
        {
            Transform cameraTransform = rig.transform.Find("Camera");
            if (cameraTransform != null)
            {
                Camera existingCamera = cameraTransform.GetComponent<Camera>();
                if (existingCamera != null)
                {
                    return existingCamera;
                }
            }
        }

        return CreateKinectDebugRig();
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

    private static void EnsureLight(string name, float intensity, Color color, LightShadows shadows, Quaternion rotation)
    {
        GameObject lightObject = GameObject.Find(name);
        if (lightObject == null)
        {
            lightObject = new GameObject(name);
        }

        Light light = AddOrGetComponent<Light>(lightObject);
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.color = color;
        light.shadows = shadows;
        lightObject.transform.rotation = rotation;
    }

    private static RuntimeUiReferences EnsureRuntimeUi(Transform playerRoot, Scene scene)
    {
        GameObject canvasObject = GameObject.Find("RuntimeUI");
        if (canvasObject == null)
        {
            canvasObject = new GameObject("RuntimeUI");
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
        }

        Canvas canvas = AddOrGetComponent<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        CanvasScaler scaler = AddOrGetComponent<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        AddOrGetComponent<GraphicRaycaster>(canvasObject);

        GameObject hudScreen = FindOrCreateChildPanel(canvas.transform, "HUD", new Color(0f, 0f, 0f, 0.18f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -18f), new Vector2(210f, 120f));
        Text hudText = FindOrCreateChildText(hudScreen.transform, "HUDText", TextAnchor.UpperLeft, 28, FontStyle.Bold, new Color(0.95f, 0.97f, 1f, 1f));
        Stretch(hudText.rectTransform, new Vector2(14f, 14f), new Vector2(-14f, -14f));

        GameObject startScreen = FindOrCreateChildPanel(canvas.transform, "StartScreen", new Color(0.04f, 0.06f, 0.1f, 0.82f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Text startText = FindOrCreateChildText(startScreen.transform, "StartText", TextAnchor.MiddleCenter, 46, FontStyle.Bold, Color.white);
        Stretch(startText.rectTransform, new Vector2(120f, 80f), new Vector2(-120f, -80f));

        GameObject gameOverScreen = FindOrCreateChildPanel(canvas.transform, "GameOverScreen", new Color(0.12f, 0.03f, 0.03f, 0.84f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Text gameOverText = FindOrCreateChildText(gameOverScreen.transform, "GameOverText", TextAnchor.MiddleCenter, 40, FontStyle.Bold, new Color(1f, 0.92f, 0.92f, 1f));
        Stretch(gameOverText.rectTransform, new Vector2(120f, 80f), new Vector2(-120f, -80f));

        GameObject settingsScreen = FindOrCreateChildPanel(canvas.transform, "SettingsScreen", new Color(0.04f, 0.06f, 0.08f, 0.84f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        Text settingsText = FindOrCreateChildText(settingsScreen.transform, "SettingsText", TextAnchor.MiddleCenter, 30, FontStyle.Bold, new Color(0.92f, 0.96f, 1f, 1f));
        settingsText.text = "SETTINGS\nUse the GameManager inspector to adjust gameplay values.";
        Stretch(settingsText.rectTransform, new Vector2(120f, 80f), new Vector2(-120f, -80f));

        return new RuntimeUiReferences
        {
            hudScreen = hudScreen,
            startScreen = startScreen,
            gameOverScreen = gameOverScreen,
            settingsScreen = settingsScreen,
            hudText = hudText,
            startText = startText,
            gameOverText = gameOverText,
        };
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

    private static GameObject CreateFullscreenPanel(Transform parent, string name, Color color)
    {
        return CreatePanel(parent, name, color, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static GameObject CreatePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject panelObject = new GameObject(name);
        panelObject.transform.SetParent(parent, false);

        RectTransform rectTransform = panelObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;

        Image image = panelObject.AddComponent<Image>();
        image.color = color;
        return panelObject;
    }

    private static GameObject FindOrCreateChildPanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        Transform existing = parent.Find(name);
        GameObject panelObject = existing != null ? existing.gameObject : CreatePanel(parent, name, color, anchorMin, anchorMax, offsetMin, offsetMax);
        RectTransform rectTransform = panelObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = panelObject.AddComponent<RectTransform>();
        }

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;

        Image image = AddOrGetComponent<Image>(panelObject);
        image.color = color;
        return panelObject;
    }

    private static Text CreateText(Transform parent, string name, TextAnchor alignment, int fontSize, FontStyle fontStyle, Color color)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Text FindOrCreateChildText(Transform parent, string name, TextAnchor alignment, int fontSize, FontStyle fontStyle, Color color)
    {
        Transform existing = parent.Find(name);
        Text text = existing != null ? AddOrGetComponent<Text>(existing.gameObject) : CreateText(parent, name, alignment, fontSize, fontStyle, color);
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rectTransform, Vector2 offsetMin, Vector2 offsetMax)
    {
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;
    }

    private static GameObject FindOrCreateRootObject(Scene scene, string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null)
        {
            return existing;
        }

        GameObject created = new GameObject(name);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    private static Animator FindGameplayAnimator()
    {
        HumanoidPoseDriver poseDriver = Object.FindFirstObjectByType<HumanoidPoseDriver>();
        if (poseDriver != null)
        {
            Animator driverAnimator = poseDriver.GetComponentInChildren<Animator>();
            if (driverAnimator != null && driverAnimator.isHuman)
            {
                return driverAnimator;
            }
        }

        Animator[] animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int index = 0; index < animators.Length; index++)
        {
            if (animators[index] != null && animators[index].isHuman)
            {
                return animators[index];
            }
        }

        return null;
    }

    private static T AddOrGetComponent<T>(GameObject gameObject) where T : Component
    {
        T existing = gameObject.GetComponent<T>();
        return existing != null ? existing : gameObject.AddComponent<T>();
    }

    private struct RuntimeUiReferences
    {
        public GameObject hudScreen;
        public GameObject startScreen;
        public GameObject gameOverScreen;
        public GameObject settingsScreen;
        public Text hudText;
        public Text startText;
        public Text gameOverText;
    }
}