using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class MainGameSceneSetup
{
    private const string ScenePath = "Assets/Scenes/MainGame.unity";
    private const string NaturePrefabPath = "Assets/imports/Pack_Heros/Prefabs/Hero_Nature.prefab";

    [MenuItem("PoseGame/Setup Main Game Scene")]
    public static void SetupMainGameScene()
    {
        GameObject naturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NaturePrefabPath);
        if (naturePrefab == null)
        {
            EditorUtility.DisplayDialog(
                "Nature Prefab Missing",
                $"Could not load the Nature prefab at:\n{NaturePrefabPath}",
                "OK");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "MainGame";

        ConfigureRenderSettings();

        Camera camera = CreateCamera();
        CreateKeyLight();
        CreateFillLight();
        CreateBackdrop();

        GameObject poseRuntime = new GameObject("PoseRuntime");
        DebugPoseSource debugPoseSource = poseRuntime.AddComponent<DebugPoseSource>();

        GameObject playerRoot = new GameObject("PlayerRoot");
        playerRoot.transform.position = new Vector3(0f, -1.15f, 0f);

        GameObject playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(naturePrefab, scene);
        playerInstance.name = "PlayerAvatar";
        playerInstance.transform.SetParent(playerRoot.transform, false);
        playerInstance.transform.localPosition = Vector3.zero;
        playerInstance.transform.localScale = Vector3.one;
        playerInstance.transform.rotation = Quaternion.LookRotation(camera.transform.position - playerInstance.transform.position, Vector3.up);

        Animator animator = playerInstance.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            EditorUtility.DisplayDialog(
                "Animator Missing",
                "The Nature prefab does not contain an Animator component. Set the model rig to Humanoid and reimport it.",
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

        poseDriver.Configure(debugPoseSource, animator, playerInstance.transform, horizontalMirror: true, swapSides: true);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EnsureSceneInBuildSettings(ScenePath);

        Selection.activeGameObject = playerInstance;
        EditorGUIUtility.PingObject(playerInstance);

        EditorUtility.DisplayDialog(
            "Main Scene Ready",
            "Created Assets/Scenes/MainGame.unity with the Nature hero centered and linked to the pose system. Replace DebugPoseSource on PoseRuntime when you add a real tracking source.",
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

        Transform transform = camera.transform;
        transform.position = new Vector3(0f, 1.35f, -4.5f);
        transform.rotation = Quaternion.identity;
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

        Material material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.name = "MainGameBackdrop";
        material.color = new Color(0.16f, 0.2f, 0.24f);
        backdrop.GetComponent<MeshRenderer>().sharedMaterial = material;
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