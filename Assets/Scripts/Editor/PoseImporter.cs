using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Editor tool: imports JSON pose files from pose_detection/poses/ into Unity ScriptableObjects.
/// Access via menu: PoseGame > Import Poses from JSON
/// </summary>
public class PoseImporter : EditorWindow
{
    private string sourcePath = "";
    private string outputPath = "Assets/Data/Poses";

    [MenuItem("PoseGame/Import Poses from JSON")]
    public static void ShowWindow()
    {
        GetWindow<PoseImporter>("Pose Importer");
    }

    private void OnEnable()
    {
        // Default to the pose_detection/poses folder relative to project root
        sourcePath = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "pose_detection", "poses")
        );
    }

    private void OnGUI()
    {
        GUILayout.Label("Import Pose JSON Files", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.LabelField("Source Folder (JSON files):");
        sourcePath = EditorGUILayout.TextField(sourcePath);

        if (GUILayout.Button("Browse..."))
        {
            string selected = EditorUtility.OpenFolderPanel("Select Poses Folder", sourcePath, "");
            if (!string.IsNullOrEmpty(selected))
                sourcePath = selected;
        }

        GUILayout.Space(5);
        EditorGUILayout.LabelField("Output Folder (ScriptableObjects):");
        outputPath = EditorGUILayout.TextField(outputPath);

        GUILayout.Space(15);

        if (GUILayout.Button("Import All Poses", GUILayout.Height(30)))
        {
            ImportAll();
        }
    }

    private void ImportAll()
    {
        if (!Directory.Exists(sourcePath))
        {
            EditorUtility.DisplayDialog("Error",
                $"Source folder not found:\n{sourcePath}", "OK");
            return;
        }

        // Ensure output folder exists
        if (!AssetDatabase.IsValidFolder(outputPath))
        {
            string[] parts = outputPath.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        string[] jsonFiles = Directory.GetFiles(sourcePath, "*.json");
        int imported = 0;

        foreach (string jsonFile in jsonFiles)
        {
            string json = File.ReadAllText(jsonFile);
            PoseJson poseJson = JsonUtility.FromJson<PoseJson>(json);

            if (poseJson == null || poseJson.keypoints == null)
            {
                Debug.LogWarning($"[PoseImporter] Skipping invalid file: {jsonFile}");
                continue;
            }

            string assetName = Path.GetFileNameWithoutExtension(jsonFile);
            string assetPath = $"{outputPath}/{assetName}.asset";

            // Load existing or create new
            PoseData poseData = AssetDatabase.LoadAssetAtPath<PoseData>(assetPath);
            bool isNew = poseData == null;

            if (isNew)
            {
                poseData = ScriptableObject.CreateInstance<PoseData>();
            }

            poseData.displayName = poseJson.name;
            poseData.keypoints = new Keypoint[]
            {
                ToKeypoint("nose", poseJson.keypoints.nose),
                ToKeypoint("left_eye", poseJson.keypoints.left_eye),
                ToKeypoint("right_eye", poseJson.keypoints.right_eye),
                ToKeypoint("left_ear", poseJson.keypoints.left_ear),
                ToKeypoint("right_ear", poseJson.keypoints.right_ear),
                ToKeypoint("left_shoulder", poseJson.keypoints.left_shoulder),
                ToKeypoint("right_shoulder", poseJson.keypoints.right_shoulder),
                ToKeypoint("left_elbow", poseJson.keypoints.left_elbow),
                ToKeypoint("right_elbow", poseJson.keypoints.right_elbow),
                ToKeypoint("left_wrist", poseJson.keypoints.left_wrist),
                ToKeypoint("right_wrist", poseJson.keypoints.right_wrist),
                ToKeypoint("left_hip", poseJson.keypoints.left_hip),
                ToKeypoint("right_hip", poseJson.keypoints.right_hip),
                ToKeypoint("left_knee", poseJson.keypoints.left_knee),
                ToKeypoint("right_knee", poseJson.keypoints.right_knee),
                ToKeypoint("left_ankle", poseJson.keypoints.left_ankle),
                ToKeypoint("right_ankle", poseJson.keypoints.right_ankle),
            };

            if (isNew)
            {
                AssetDatabase.CreateAsset(poseData, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(poseData);
            }

            imported++;
            Debug.Log($"[PoseImporter] Imported: {assetName}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Import Complete",
            $"Imported {imported} pose(s) to {outputPath}", "OK");
    }

    private static Keypoint ToKeypoint(string name, KeypointJson kj)
    {
        if (kj == null) return new Keypoint { name = name };
        return new Keypoint
        {
            name = name,
            x = kj.x,
            y = kj.y,
            confidence = kj.confidence,
        };
    }

    // JSON deserialization classes matching the Python output format
    [System.Serializable]
    private class PoseJson
    {
        public string name;
        public int frame_width;
        public int frame_height;
        public KeypointsJson keypoints;
    }

    [System.Serializable]
    private class KeypointsJson
    {
        public KeypointJson nose;
        public KeypointJson left_eye;
        public KeypointJson right_eye;
        public KeypointJson left_ear;
        public KeypointJson right_ear;
        public KeypointJson left_shoulder;
        public KeypointJson right_shoulder;
        public KeypointJson left_elbow;
        public KeypointJson right_elbow;
        public KeypointJson left_wrist;
        public KeypointJson right_wrist;
        public KeypointJson left_hip;
        public KeypointJson right_hip;
        public KeypointJson left_knee;
        public KeypointJson right_knee;
        public KeypointJson left_ankle;
        public KeypointJson right_ankle;
    }

    [System.Serializable]
    private class KeypointJson
    {
        public float x;
        public float y;
        public float confidence;
    }
}
