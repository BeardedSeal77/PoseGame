using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Editor tool: bakes PoseData into wall sprite prefabs.
/// Generates a texture with a human-shaped cutout, saves as PNG + prefab.
/// Access via menu: PoseGame > Bake Walls from Poses
/// </summary>
public class WallBaker : EditorWindow
{
    private string posesFolder = "Assets/Data/Poses";
    private string textureOutput = "Assets/Textures/Walls";
    private string prefabOutput = "Assets/Prefabs/Walls";

    private int textureWidth = 512;
    private int textureHeight = 512;
    private Color cutoutColor = new Color(0f, 0f, 0f, 0f);       // Transparent
    private int jointRadius = 18;
    private int headRadius = 28;
    private int padding = 40; // Pixels of padding around the silhouette

    // Jungle texture settings
    private Texture2D customWallTexture = null; // Optional: drag a tileable texture here
    private float textureScale = 2f;            // How many times to tile the texture
    private Color stoneBase = new Color(0.35f, 0.32f, 0.25f, 1f);
    private Color stoneDark = new Color(0.18f, 0.15f, 0.10f, 1f);
    private Color mossColor = new Color(0.15f, 0.35f, 0.10f, 1f);
    private Color vineColor = new Color(0.10f, 0.25f, 0.05f, 1f);
    [Range(0f, 1f)] private float mossAmount = 0.4f;
    [Range(0f, 1f)] private float crackAmount = 0.3f;
    private bool generateNormalMap = true;

    // Skeleton limb connections (indices into the 17 COCO keypoints)
    private static readonly int[][] SKELETON = new int[][]
    {
        new[] {5, 6},   // shoulders
        new[] {5, 7},   // left shoulder -> left elbow
        new[] {7, 9},   // left elbow -> left wrist
        new[] {6, 8},   // right shoulder -> right elbow
        new[] {8, 10},  // right elbow -> right wrist
        new[] {5, 11},  // left shoulder -> left hip
        new[] {6, 12},  // right shoulder -> right hip
        new[] {11, 12}, // hips
        new[] {11, 13}, // left hip -> left knee
        new[] {13, 15}, // left knee -> left ankle
        new[] {12, 14}, // right hip -> right knee
        new[] {14, 16}, // right knee -> right ankle
    };

    [MenuItem("PoseGame/Bake Walls from Poses")]
    public static void ShowWindow()
    {
        GetWindow<WallBaker>("Wall Baker");
    }

    private void OnGUI()
    {
        GUILayout.Label("Bake Wall Prefabs from Pose Data", EditorStyles.boldLabel);
        GUILayout.Space(10);

        posesFolder = EditorGUILayout.TextField("Poses Folder", posesFolder);
        textureOutput = EditorGUILayout.TextField("Texture Output", textureOutput);
        prefabOutput = EditorGUILayout.TextField("Prefab Output", prefabOutput);

        GUILayout.Space(10);
        GUILayout.Label("Cutout Settings", EditorStyles.boldLabel);
        textureWidth = EditorGUILayout.IntField("Texture Width", textureWidth);
        textureHeight = EditorGUILayout.IntField("Texture Height", textureHeight);
        jointRadius = EditorGUILayout.IntSlider("Joint Radius", jointRadius, 8, 40);
        EditorGUILayout.LabelField("Limb Thickness", $"{jointRadius * 2} (auto: 2x joint radius)");
        headRadius = EditorGUILayout.IntSlider("Head Radius", headRadius, 12, 60);
        padding = EditorGUILayout.IntSlider("Edge Padding", padding, 10, 80);

        GUILayout.Space(10);
        GUILayout.Label("Jungle Wall Appearance", EditorStyles.boldLabel);
        customWallTexture = (Texture2D)EditorGUILayout.ObjectField(
            "Custom Texture (optional)", customWallTexture, typeof(Texture2D), false);

        if (customWallTexture != null)
        {
            textureScale = EditorGUILayout.Slider("Texture Tiling", textureScale, 0.5f, 8f);
        }
        else
        {
            EditorGUILayout.LabelField("Using procedural jungle stone");
            stoneBase = EditorGUILayout.ColorField("Stone Base", stoneBase);
            stoneDark = EditorGUILayout.ColorField("Stone Dark", stoneDark);
            mossColor = EditorGUILayout.ColorField("Moss Color", mossColor);
            mossAmount = EditorGUILayout.Slider("Moss Amount", mossAmount, 0f, 1f);
            crackAmount = EditorGUILayout.Slider("Crack Amount", crackAmount, 0f, 1f);
        }
        generateNormalMap = EditorGUILayout.Toggle("Generate Normal Map", generateNormalMap);

        GUILayout.Space(15);

        if (GUILayout.Button("Bake All Walls", GUILayout.Height(30)))
        {
            BakeAll();
        }
    }

    private void BakeAll()
    {
        EnsureFolder(textureOutput);
        EnsureFolder(prefabOutput);

        string[] guids = AssetDatabase.FindAssets("t:PoseData", new[] { posesFolder });

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Error",
                $"No PoseData assets found in {posesFolder}", "OK");
            return;
        }

        int baked = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            PoseData pose = AssetDatabase.LoadAssetAtPath<PoseData>(path);
            if (pose == null) continue;

            BakeWall(pose);
            baked++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Bake Complete",
            $"Baked {baked} wall(s) to {prefabOutput}", "OK");
    }

    private void BakeWall(PoseData pose)
    {
        // Create the wall texture
        Texture2D tex = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);

        // Fill with jungle wall texture
        if (customWallTexture != null)
            FillWithTiledTexture(tex, customWallTexture, textureScale);
        else
            FillWithProceduralJungle(tex);

        // Map normalized keypoints to pixel coordinates
        // Fit the pose within the texture with padding
        Vector2[] pixelPositions = MapKeypointsToTexture(pose);

        int limbThickness = jointRadius * 2;

        // --- HEAD: amalgamate all head joints (0-4) into one smooth circle ---
        // Keypoints: 0=nose, 1=left_eye, 2=right_eye, 3=left_ear, 4=right_ear
        Vector2 headCenter = Vector2.zero;
        int headCount = 0;
        for (int i = 0; i <= 4; i++)
        {
            if (pose.keypoints[i].confidence < 0.3f) continue;
            headCenter += pixelPositions[i];
            headCount++;
        }

        if (headCount > 0)
        {
            headCenter /= headCount;

            // Radius = distance to farthest head keypoint + headRadius for padding
            float maxDist = 0f;
            for (int i = 0; i <= 4; i++)
            {
                if (pose.keypoints[i].confidence < 0.3f) continue;
                float dist = Vector2.Distance(headCenter, pixelPositions[i]);
                if (dist > maxDist) maxDist = dist;
            }
            int amalgamatedHeadRadius = Mathf.RoundToInt(maxDist) + headRadius;
            DrawFilledCircle(tex, headCenter, amalgamatedHeadRadius, cutoutColor);

            // Connect shoulders to head center (neck)
            if (pose.keypoints[5].confidence >= 0.3f) // left shoulder
                DrawThickLine(tex, headCenter, pixelPositions[5], limbThickness, cutoutColor);
            if (pose.keypoints[6].confidence >= 0.3f) // right shoulder
                DrawThickLine(tex, headCenter, pixelPositions[6], limbThickness, cutoutColor);
        }

        // --- BODY LIMBS: draw skeleton connections (skip head joints) ---
        foreach (int[] bone in SKELETON)
        {
            Keypoint kpA = pose.keypoints[bone[0]];
            Keypoint kpB = pose.keypoints[bone[1]];

            if (kpA.confidence < 0.3f || kpB.confidence < 0.3f)
                continue;

            DrawThickLine(tex, pixelPositions[bone[0]], pixelPositions[bone[1]],
                limbThickness, cutoutColor);
        }

        // --- BODY JOINTS: draw circles (skip head joints 0-4, handled above) ---
        for (int i = 5; i < pose.keypoints.Length; i++)
        {
            if (pose.keypoints[i].confidence < 0.3f)
                continue;

            DrawFilledCircle(tex, pixelPositions[i], jointRadius, cutoutColor);
        }

        // --- LEGS: open the wall from hips downward to bottom edge ---
        // Left leg: draw from hip (or lowest visible leg joint) down to y=0
        int[] leftLeg = { 11, 13, 15 };  // left_hip, left_knee, left_ankle
        int[] rightLeg = { 12, 14, 16 }; // right_hip, right_knee, right_ankle
        OpenLegToBottom(tex, pose, pixelPositions, leftLeg, limbThickness, cutoutColor);
        OpenLegToBottom(tex, pose, pixelPositions, rightLeg, limbThickness, cutoutColor);

        // Flood fill any enclosed regions (e.g. torso area between shoulders and hips)
        FloodFillEnclosedRegions(tex, cutoutColor);

        tex.Apply();

        // Save diffuse texture as PNG
        string texPath = $"{textureOutput}/{pose.displayName}_wall.png";
        byte[] pngData = tex.EncodeToPNG();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", texPath), pngData);

        // Generate and save normal map
        string normalPath = null;
        if (generateNormalMap)
        {
            Texture2D normalMap = GenerateNormalMap(tex, 2f);
            normalPath = $"{textureOutput}/{pose.displayName}_wall_normal.png";
            byte[] normalPng = normalMap.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(Application.dataPath, "..", normalPath), normalPng);
            Object.DestroyImmediate(normalMap);
        }

        Object.DestroyImmediate(tex);
        AssetDatabase.Refresh();

        // Configure diffuse texture import
        TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        // Configure normal map import
        if (normalPath != null)
        {
            TextureImporter normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.filterMode = FilterMode.Bilinear;
                normalImporter.SaveAndReimport();
            }
        }

        // Load sprite
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        if (sprite == null)
        {
            Debug.LogError($"[WallBaker] Failed to load sprite: {texPath}");
            return;
        }

        // Find or create a Sprite-Lit material
        Material litMaterial = FindOrCreateLitMaterial();

        // Create prefab
        GameObject wallGO = new GameObject(pose.displayName + "_wall");
        SpriteRenderer sr = wallGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.material = litMaterial;

        // Attach pose data reference
        WallPoseRef poseRef = wallGO.AddComponent<WallPoseRef>();
        poseRef.poseData = pose;

        string prefabPath = $"{prefabOutput}/{pose.displayName}_wall.prefab";
        PrefabUtility.SaveAsPrefabAsset(wallGO, prefabPath);
        Object.DestroyImmediate(wallGO);

        Debug.Log($"[WallBaker] Baked: {prefabPath}");
    }

    // =========================================================================
    // JUNGLE TEXTURE GENERATION
    // =========================================================================

    private void FillWithTiledTexture(Texture2D target, Texture2D source, float tiling)
    {
        // Make source readable
        string srcPath = AssetDatabase.GetAssetPath(source);
        TextureImporter srcImporter = AssetImporter.GetAtPath(srcPath) as TextureImporter;
        bool wasReadable = true;
        if (srcImporter != null && !srcImporter.isReadable)
        {
            wasReadable = false;
            srcImporter.isReadable = true;
            srcImporter.SaveAndReimport();
        }

        for (int y = 0; y < target.height; y++)
        {
            for (int x = 0; x < target.width; x++)
            {
                float u = (x / (float)target.width) * tiling;
                float v = (y / (float)target.height) * tiling;
                u -= Mathf.Floor(u); // Wrap
                v -= Mathf.Floor(v);

                Color c = source.GetPixelBilinear(u, v);
                c.a = 1f;
                target.SetPixel(x, y, c);
            }
        }

        // Restore readability
        if (!wasReadable && srcImporter != null)
        {
            srcImporter.isReadable = false;
            srcImporter.SaveAndReimport();
        }
    }

    private void FillWithProceduralJungle(Texture2D tex)
    {
        // Random seed per wall so each looks different
        float seed = Random.Range(0f, 1000f);
        int w = tex.width;
        int h = tex.height;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float nx = x / (float)w;
                float ny = y / (float)h;

                // Layer 1: Base stone with large-scale variation
                float stoneNoise = Mathf.PerlinNoise(
                    (nx * 4f) + seed, (ny * 4f) + seed);
                Color baseCol = Color.Lerp(stoneDark, stoneBase, stoneNoise);

                // Layer 2: Fine grain stone texture
                float grainNoise = Mathf.PerlinNoise(
                    (nx * 20f) + seed + 100f, (ny * 20f) + seed + 100f);
                baseCol = Color.Lerp(baseCol, baseCol * 0.8f, grainNoise * 0.3f);

                // Layer 3: Block/brick pattern
                float blockX = Mathf.PerlinNoise(
                    (nx * 8f) + seed + 200f, (ny * 3f) + seed + 200f);
                float blockY = Mathf.PerlinNoise(
                    (nx * 3f) + seed + 300f, (ny * 8f) + seed + 300f);
                float blockEdge = Mathf.Max(
                    Mathf.Abs(blockX - 0.5f), Mathf.Abs(blockY - 0.5f));
                if (blockEdge > 0.35f)
                    baseCol = Color.Lerp(baseCol, stoneDark, 0.5f);

                // Layer 4: Cracks (high frequency, thresholded)
                if (crackAmount > 0f)
                {
                    float crackNoise = Mathf.PerlinNoise(
                        (nx * 30f) + seed + 400f, (ny * 30f) + seed + 400f);
                    float crackNoise2 = Mathf.PerlinNoise(
                        (nx * 45f) + seed + 500f, (ny * 15f) + seed + 500f);
                    float crack = Mathf.Min(crackNoise, crackNoise2);
                    float crackThreshold = 1f - crackAmount * 0.5f;
                    if (crack < (1f - crackThreshold))
                        baseCol = Color.Lerp(baseCol, stoneDark * 0.5f, 0.7f);
                }

                // Layer 5: Moss patches
                if (mossAmount > 0f)
                {
                    float mossNoise = Mathf.PerlinNoise(
                        (nx * 6f) + seed + 600f, (ny * 6f) + seed + 600f);
                    float mossDetail = Mathf.PerlinNoise(
                        (nx * 15f) + seed + 700f, (ny * 15f) + seed + 700f);
                    float mossThreshold = 1f - mossAmount;

                    if (mossNoise > mossThreshold)
                    {
                        float mossStrength = (mossNoise - mossThreshold) / mossAmount;
                        Color moss = Color.Lerp(mossColor, vineColor, mossDetail);
                        baseCol = Color.Lerp(baseCol, moss, mossStrength * 0.8f);
                    }
                }

                // Layer 6: Subtle vertical moisture streaks
                float streakNoise = Mathf.PerlinNoise(
                    (nx * 12f) + seed + 800f, (ny * 2f) + seed + 800f);
                if (streakNoise > 0.7f)
                    baseCol = Color.Lerp(baseCol, baseCol * 0.85f, 0.3f);

                baseCol.a = 1f;
                tex.SetPixel(x, y, baseCol);
            }
        }
    }

    /// <summary>
    /// Generates a normal map from a diffuse texture using Sobel filter.
    /// Gives the wall a 3D depth appearance when lit.
    /// </summary>
    private Texture2D GenerateNormalMap(Texture2D source, float strength)
    {
        int w = source.width;
        int h = source.height;
        Texture2D normalMap = new Texture2D(w, h, TextureFormat.RGBA32, false);

        Color[] srcPixels = source.GetPixels();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                // Skip transparent pixels
                Color center = srcPixels[y * w + x];
                if (center.a < 0.5f)
                {
                    normalMap.SetPixel(x, y, new Color(0.5f, 0.5f, 1f, 0f));
                    continue;
                }

                // Sample heights from grayscale
                float tl = GetGray(srcPixels, x - 1, y + 1, w, h);
                float t  = GetGray(srcPixels, x,     y + 1, w, h);
                float tr = GetGray(srcPixels, x + 1, y + 1, w, h);
                float l  = GetGray(srcPixels, x - 1, y,     w, h);
                float r  = GetGray(srcPixels, x + 1, y,     w, h);
                float bl = GetGray(srcPixels, x - 1, y - 1, w, h);
                float b  = GetGray(srcPixels, x,     y - 1, w, h);
                float br = GetGray(srcPixels, x + 1, y - 1, w, h);

                // Sobel filter
                float dx = (tr + 2f * r + br) - (tl + 2f * l + bl);
                float dy = (tl + 2f * t + tr) - (bl + 2f * b + br);

                Vector3 normal = new Vector3(-dx * strength, -dy * strength, 1f).normalized;

                // Encode to 0-1 range
                normalMap.SetPixel(x, y, new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f,
                    1f
                ));
            }
        }

        normalMap.Apply();
        return normalMap;
    }

    private float GetGray(Color[] pixels, int x, int y, int w, int h)
    {
        x = Mathf.Clamp(x, 0, w - 1);
        y = Mathf.Clamp(y, 0, h - 1);
        Color c = pixels[y * w + x];
        if (c.a < 0.5f) return 0f;
        return c.grayscale;
    }

    private Material FindOrCreateLitMaterial()
    {
        string matPath = "Assets/Textures/Walls/WallLit.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null) return mat;

        // Find the Sprite-Lit-Default shader (URP 2D)
        Shader litShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (litShader == null)
        {
            // Fallback
            litShader = Shader.Find("Sprites/Default");
            Debug.LogWarning("[WallBaker] Sprite-Lit-Default not found, using fallback");
        }

        mat = new Material(litShader);
        mat.name = "WallLit";

        EnsureFolder("Assets/Textures/Walls");
        AssetDatabase.CreateAsset(mat, matPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[WallBaker] Created lit material: {matPath}");
        return mat;
    }

    /// <summary>
    /// Maps normalized 0-1 keypoints into pixel coordinates,
    /// fitting the pose within the texture bounds with padding.
    /// </summary>
    private Vector2[] MapKeypointsToTexture(PoseData pose)
    {
        // Find bounding box of confident keypoints
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        int validCount = 0;

        foreach (var kp in pose.keypoints)
        {
            if (kp.confidence < 0.3f) continue;
            if (kp.x < minX) minX = kp.x;
            if (kp.x > maxX) maxX = kp.x;
            if (kp.y < minY) minY = kp.y;
            if (kp.y > maxY) maxY = kp.y;
            validCount++;
        }

        if (validCount < 2)
        {
            // Fallback: center everything
            minX = 0; maxX = 1; minY = 0; maxY = 1;
        }

        float poseW = maxX - minX;
        float poseH = maxY - minY;
        if (poseW < 0.01f) poseW = 1f;
        if (poseH < 0.01f) poseH = 1f;

        // Available pixel area
        float availW = textureWidth - padding * 2;
        float availH = textureHeight - padding * 2;

        // Scale to fit, preserving aspect ratio
        float scale = Mathf.Min(availW / poseW, availH / poseH);
        float offsetX = padding + (availW - poseW * scale) * 0.5f;
        float offsetY = padding + (availH - poseH * scale) * 0.5f;

        Vector2[] result = new Vector2[pose.keypoints.Length];
        for (int i = 0; i < pose.keypoints.Length; i++)
        {
            float px = offsetX + (pose.keypoints[i].x - minX) * scale;
            // Flip Y: texture 0 is bottom, pose 0 is top
            float py = textureHeight - (offsetY + (pose.keypoints[i].y - minY) * scale);
            result[i] = new Vector2(px, py);
        }

        return result;
    }

    private void DrawFilledCircle(Texture2D tex, Vector2 center, int radius, Color color)
    {
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);
        int r2 = radius * radius;

        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y <= r2)
                {
                    int px = cx + x;
                    int py = cy + y;
                    if (px >= 0 && px < tex.width && py >= 0 && py < tex.height)
                        tex.SetPixel(px, py, color);
                }
            }
        }
    }

    private void DrawThickLine(Texture2D tex, Vector2 a, Vector2 b, int thickness, Color color)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Mathf.CeilToInt(dist);
        float halfThick = thickness * 0.5f;

        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            Vector2 point = Vector2.Lerp(a, b, t);
            // Draw a filled circle at each step for a rounded thick line
            DrawFilledCircle(tex, point, Mathf.RoundToInt(halfThick), color);
        }
    }

    /// <summary>
    /// Draws a thick line from the lowest visible joint in a leg chain
    /// straight down to the bottom of the texture, opening the wall.
    /// </summary>
    private void OpenLegToBottom(Texture2D tex, PoseData pose, Vector2[] positions,
        int[] jointIndices, int thickness, Color color)
    {
        // Find the lowest visible joint in the chain (hip -> knee -> ankle)
        // "Lowest" in texture coords means smallest Y (bottom = 0)
        Vector2 lowestPoint = Vector2.zero;
        bool found = false;

        foreach (int idx in jointIndices)
        {
            if (pose.keypoints[idx].confidence >= 0.3f)
            {
                if (!found || positions[idx].y < lowestPoint.y)
                {
                    lowestPoint = positions[idx];
                    found = true;
                }
            }
        }

        if (!found) return;

        // Draw from lowest joint straight down to bottom edge
        Vector2 bottomPoint = new Vector2(lowestPoint.x, 0);
        DrawThickLine(tex, lowestPoint, bottomPoint, thickness, color);
    }

    /// <summary>
    /// Fills enclosed wall-colored regions with the cutout color.
    /// Works by flood-filling from all edges to mark "outside" wall pixels,
    /// then anything still wall-colored that wasn't reached is enclosed.
    /// </summary>
    private void FloodFillEnclosedRegions(Texture2D tex, Color cutoutColor)
    {
        int w = tex.width;
        int h = tex.height;
        Color[] pixels = tex.GetPixels();
        bool[] visited = new bool[w * h];

        // A pixel is "wall" if it's not cutout (alpha > 0.5)
        Queue<int> queue = new Queue<int>();

        // Seed from all edge pixels that are wall-colored
        for (int x = 0; x < w; x++)
        {
            TrySeed(x, 0, w, h, pixels, visited, queue);
            TrySeed(x, h - 1, w, h, pixels, visited, queue);
        }
        for (int y = 0; y < h; y++)
        {
            TrySeed(0, y, w, h, pixels, visited, queue);
            TrySeed(w - 1, y, w, h, pixels, visited, queue);
        }

        // BFS flood fill from edges
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w;
            int y = idx / w;

            if (x > 0) TrySeed(x - 1, y, w, h, pixels, visited, queue);
            if (x < w - 1) TrySeed(x + 1, y, w, h, pixels, visited, queue);
            if (y > 0) TrySeed(x, y - 1, w, h, pixels, visited, queue);
            if (y < h - 1) TrySeed(x, y + 1, w, h, pixels, visited, queue);
        }

        // Any wall pixel not visited is enclosed — make it cutout
        int filled = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!visited[i] && pixels[i].a > 0.5f)
            {
                pixels[i] = cutoutColor;
                filled++;
            }
        }

        if (filled > 0)
        {
            tex.SetPixels(pixels);
            Debug.Log($"[WallBaker] Flood filled {filled} enclosed pixels");
        }
    }

    private void TrySeed(int x, int y, int w, int h, Color[] pixels, bool[] visited, Queue<int> queue)
    {
        int idx = y * w + x;
        if (visited[idx]) return;
        if (pixels[idx].a < 0.5f) return; // Already cutout
        visited[idx] = true;
        queue.Enqueue(idx);
    }

    private void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
