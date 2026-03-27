using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Editor tool: bakes PoseData into 3D wall prefabs.
/// Front/back faces use baked texture with alpha cutout (URP Lit + alpha clip).
/// Inner cutout walls are extruded from edge detection — real geometry with solid wall texture.
/// Access via menu: PoseGame > Bake Walls from Poses
/// </summary>
public class WallBaker : EditorWindow
{
    private string posesFolder = "Assets/Data/Poses";
    private string textureOutput = "Assets/Textures/Walls";
    private string prefabOutput = "Assets/Prefabs/Walls";

    private int texWidth = 1024;
    private int texHeight = 1024;

    private int jointRadius = 22;
    private int headRadius = 32;
    private int padding = 50;

    private Texture2D baseColorMap;
    private Texture2D normalMap;
    private Texture2D heightMap;
    private Texture2D aoMap;
    private bool texturesAutoLoaded;

    private Vector2 scrollPos;

    private float textureTiling = 2f;
    private float bumpStrength = 1.0f;
    private float smoothness = 0.2f;

    private float wallWidth = 5f;
    private float wallHeight = 5f;
    private float wallDepth = 0.3f;

    private static readonly int[][] SKELETON = new int[][]
    {
        new[] {5, 6},  new[] {5, 7},  new[] {7, 9},
        new[] {6, 8},  new[] {8, 10}, new[] {5, 11},
        new[] {6, 12}, new[] {11, 12}, new[] {11, 13},
        new[] {13, 15}, new[] {12, 14}, new[] {14, 16},
    };

    [MenuItem("PoseGame/Bake Walls from Poses")]
    public static void ShowWindow()
    {
        GetWindow<WallBaker>("Wall Baker");
    }

    private void OnEnable()
    {
        AutoLoadTextures();
    }

    private void AutoLoadTextures()
    {
        if (texturesAutoLoaded) return;
        texturesAutoLoaded = true;

        string folder = "Assets/Phoenix3D/Textures/Outdoor_Wall_T13";
        baseColorMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Outdoor_Wall_T13_Base_Color.png");
        normalMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Outdoor_Wall_T13_Normal_DirectX.png");
        heightMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Outdoor_Wall_T13_Height.png");
        aoMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/Outdoor_Wall_T13_Ambient_occlusion.png");

        if (baseColorMap != null)
            Debug.Log("[WallBaker] Auto-loaded wall textures from Phoenix3D");
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Bake 3D Wall Prefabs", EditorStyles.boldLabel);
        GUILayout.Space(10);

        posesFolder = EditorGUILayout.TextField("Poses Folder", posesFolder);
        textureOutput = EditorGUILayout.TextField("Texture Output", textureOutput);
        prefabOutput = EditorGUILayout.TextField("Prefab Output", prefabOutput);

        GUILayout.Space(10);
        GUILayout.Label("Cutout Settings", EditorStyles.boldLabel);
        texWidth = EditorGUILayout.IntField("Texture Width", texWidth);
        texHeight = EditorGUILayout.IntField("Texture Height", texHeight);
        jointRadius = EditorGUILayout.IntSlider("Joint Radius", jointRadius, 8, 50);
        EditorGUILayout.LabelField("Limb Thickness", $"{jointRadius * 2} (auto: 2x joint radius)");
        headRadius = EditorGUILayout.IntSlider("Head Radius", headRadius, 12, 70);
        padding = EditorGUILayout.IntSlider("Edge Padding", padding, 10, 100);

        GUILayout.Space(10);
        GUILayout.Label("Wall Texture Pack", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag textures from your wall texture folder (e.g. Phoenix3D)", MessageType.Info);
        baseColorMap = (Texture2D)EditorGUILayout.ObjectField(
            "Base Color", baseColorMap, typeof(Texture2D), false);
        normalMap = (Texture2D)EditorGUILayout.ObjectField(
            "Normal Map", normalMap, typeof(Texture2D), false);
        heightMap = (Texture2D)EditorGUILayout.ObjectField(
            "Height Map (optional)", heightMap, typeof(Texture2D), false);
        aoMap = (Texture2D)EditorGUILayout.ObjectField(
            "AO Map (optional)", aoMap, typeof(Texture2D), false);

        GUILayout.Space(10);
        GUILayout.Label("Material Settings", EditorStyles.boldLabel);
        textureTiling = EditorGUILayout.Slider("Texture Tiling", textureTiling, 0.5f, 8f);
        bumpStrength = EditorGUILayout.Slider("Bump Strength", bumpStrength, 0f, 3f);
        smoothness = EditorGUILayout.Slider("Smoothness", smoothness, 0f, 1f);

        GUILayout.Space(10);
        GUILayout.Label("Wall Mesh", EditorStyles.boldLabel);
        wallWidth = EditorGUILayout.FloatField("Width", wallWidth);
        wallHeight = EditorGUILayout.FloatField("Height", wallHeight);
        wallDepth = EditorGUILayout.Slider("Depth (Thickness)", wallDepth, 0.05f, 1f);

        GUILayout.Space(15);

        GUI.enabled = baseColorMap != null;
        if (GUILayout.Button("Bake All Walls", GUILayout.Height(35)))
            BakeAll();
        GUI.enabled = true;

        if (baseColorMap == null)
            EditorGUILayout.HelpBox("Assign at least the Base Color texture to bake.", MessageType.Warning);

        EditorGUILayout.EndScrollView();
    }

    private void BakeAll()
    {
        EnsureFolder(textureOutput);
        EnsureFolder(prefabOutput);
        EnsureFolder("Assets/Materials/Walls");

        var readableRestore = new List<TextureImporter>();
        MakeReadable(baseColorMap, readableRestore);
        if (normalMap != null) MakeReadable(normalMap, readableRestore);

        string[] guids = AssetDatabase.FindAssets("t:PoseData", new[] { posesFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", $"No PoseData assets found in {posesFolder}", "OK");
            RestoreReadable(readableRestore);
            return;
        }

        // Create shared inner wall material (solid, no cutout)
        Material innerMat = CreateInnerWallMaterial();

        int baked = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            PoseData pose = AssetDatabase.LoadAssetAtPath<PoseData>(path);
            if (pose == null) continue;
            BakeWall(pose, innerMat);
            baked++;
        }

        RestoreReadable(readableRestore);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Bake Complete", $"Baked {baked} 3D wall(s) to {prefabOutput}", "OK");
    }

    private void BakeWall(PoseData pose, Material innerMat)
    {
        // ---- Step 1: Generate mask as bool array ----
        bool[] mask = new bool[texWidth * texHeight]; // true = wall, false = cutout
        for (int i = 0; i < mask.Length; i++) mask[i] = true;

        Vector2[] positions = MapKeypointsToTexture(pose);
        int limbThickness = jointRadius * 2;

        // HEAD
        Vector2 headCenter = Vector2.zero;
        int headCount = 0;
        for (int i = 0; i <= 4; i++)
        {
            if (pose.keypoints[i].confidence < 0.3f) continue;
            headCenter += positions[i];
            headCount++;
        }
        if (headCount > 0)
        {
            headCenter /= headCount;
            float maxDist = 0f;
            for (int i = 0; i <= 4; i++)
            {
                if (pose.keypoints[i].confidence < 0.3f) continue;
                float dist = Vector2.Distance(headCenter, positions[i]);
                if (dist > maxDist) maxDist = dist;
            }
            int headR = Mathf.RoundToInt(maxDist) + headRadius;
            MaskCircle(mask, headCenter, headR);
            if (pose.keypoints[5].confidence >= 0.3f)
                MaskLine(mask, headCenter, positions[5], limbThickness);
            if (pose.keypoints[6].confidence >= 0.3f)
                MaskLine(mask, headCenter, positions[6], limbThickness);
        }

        // BODY LIMBS
        foreach (int[] bone in SKELETON)
        {
            if (pose.keypoints[bone[0]].confidence < 0.3f || pose.keypoints[bone[1]].confidence < 0.3f)
                continue;
            MaskLine(mask, positions[bone[0]], positions[bone[1]], limbThickness);
        }

        // JOINTS (skip head 0-4)
        for (int i = 5; i < pose.keypoints.Length; i++)
        {
            if (pose.keypoints[i].confidence < 0.3f) continue;
            MaskCircle(mask, positions[i], jointRadius);
        }

        // OPEN LEGS
        MaskLegToBottom(mask, pose, positions, new[] { 11, 13, 15 }, limbThickness);
        MaskLegToBottom(mask, pose, positions, new[] { 12, 14, 16 }, limbThickness);

        // FLOOD FILL enclosed regions
        FloodFillEnclosed(mask);

        // ---- Step 2: Bake RGBA texture (tiled wall + alpha from mask) ----
        Texture2D bakedTex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false);
        for (int y = 0; y < texHeight; y++)
        {
            for (int x = 0; x < texWidth; x++)
            {
                float u = (x / (float)texWidth) * textureTiling;
                float v = (y / (float)texHeight) * textureTiling;
                u -= Mathf.Floor(u);
                v -= Mathf.Floor(v);
                Color c = baseColorMap.GetPixelBilinear(u, v);
                c.a = mask[y * texWidth + x] ? 1f : 0f;
                bakedTex.SetPixel(x, y, c);
            }
        }
        bakedTex.Apply();

        string texPath = $"{textureOutput}/{pose.displayName}_wall.png";
        File.WriteAllBytes(Path.Combine(Application.dataPath, "..", texPath), bakedTex.EncodeToPNG());
        Object.DestroyImmediate(bakedTex);
        AssetDatabase.Refresh();

        TextureImporter imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.filterMode = FilterMode.Bilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }

        Texture2D loadedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        // ---- Step 3: Create cutout material (front/back faces) ----
        string matPath = $"Assets/Materials/Walls/{pose.displayName}_wall.mat";
        if (File.Exists(Path.Combine(Application.dataPath, "..", matPath)))
            AssetDatabase.DeleteAsset(matPath);

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null)
        {
            Debug.LogError("[WallBaker] URP Lit shader not found!");
            return;
        }

        Material cutoutMat = new Material(litShader);
        cutoutMat.name = pose.displayName + "_wall";
        cutoutMat.SetTexture("_BaseMap", loadedTex);
        cutoutMat.SetColor("_BaseColor", Color.white);
        cutoutMat.SetFloat("_AlphaClip", 1f);
        cutoutMat.SetFloat("_Cutoff", 0.5f);
        cutoutMat.SetFloat("_Surface", 0f);
        cutoutMat.EnableKeyword("_ALPHATEST_ON");
        cutoutMat.renderQueue = (int)RenderQueue.AlphaTest;

        if (normalMap != null)
        {
            cutoutMat.SetTexture("_BumpMap", normalMap);
            cutoutMat.SetFloat("_BumpScale", bumpStrength);
            cutoutMat.EnableKeyword("_NORMALMAP");
        }
        if (heightMap != null)
        {
            cutoutMat.SetTexture("_ParallaxMap", heightMap);
            cutoutMat.SetFloat("_Parallax", 0.02f);
            cutoutMat.EnableKeyword("_PARALLAXMAP");
        }
        if (aoMap != null)
        {
            cutoutMat.SetTexture("_OcclusionMap", aoMap);
            cutoutMat.SetFloat("_OcclusionStrength", 1f);
        }
        cutoutMat.SetFloat("_Smoothness", smoothness);
        cutoutMat.SetFloat("_Metallic", 0f);

        AssetDatabase.CreateAsset(cutoutMat, matPath);

        // ---- Step 4: Generate mesh with 2 submeshes ----
        Mesh wallMesh = CreateWallMesh(mask, wallWidth, wallHeight, wallDepth);

        string meshPath = $"{prefabOutput}/{pose.displayName}_wall_mesh.asset";
        if (File.Exists(Path.Combine(Application.dataPath, "..", meshPath)))
            AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(wallMesh, meshPath);

        // ---- Step 5: Assemble prefab ----
        GameObject wallGO = new GameObject(pose.displayName + "_wall");

        MeshFilter mf = wallGO.AddComponent<MeshFilter>();
        mf.sharedMesh = wallMesh;

        MeshRenderer mr = wallGO.AddComponent<MeshRenderer>();
        // Submesh 0 = front/back (cutout), Submesh 1 = inner walls (solid)
        mr.sharedMaterials = new Material[] { cutoutMat, innerMat };
        mr.shadowCastingMode = ShadowCastingMode.On;

        WallPoseRef poseRef = wallGO.AddComponent<WallPoseRef>();
        poseRef.poseData = pose;

        BoxCollider col = wallGO.AddComponent<BoxCollider>();
        col.size = new Vector3(wallWidth, wallHeight, wallDepth);

        string prefabPath = $"{prefabOutput}/{pose.displayName}_wall.prefab";
        PrefabUtility.SaveAsPrefabAsset(wallGO, prefabPath);
        Object.DestroyImmediate(wallGO);

        Debug.Log($"[WallBaker] Baked: {prefabPath} (inner wall quads: submesh 1)");
    }

    // =========================================================================
    // INNER WALL MATERIAL (shared, solid, no cutout)
    // =========================================================================

    private Material CreateInnerWallMaterial()
    {
        string matPath = "Assets/Materials/Walls/_inner_wall.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null)
        {
            // Update settings in case they changed
            existing.SetFloat("_BumpScale", bumpStrength);
            existing.SetFloat("_Smoothness", smoothness);
            return existing;
        }

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        Material mat = new Material(litShader);
        mat.name = "_inner_wall";

        mat.SetTexture("_BaseMap", baseColorMap);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 0f); // Opaque, no alpha clip
        mat.SetFloat("_AlphaClip", 0f);

        if (normalMap != null)
        {
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", bumpStrength);
            mat.EnableKeyword("_NORMALMAP");
        }
        if (aoMap != null)
        {
            mat.SetTexture("_OcclusionMap", aoMap);
            mat.SetFloat("_OcclusionStrength", 1f);
        }

        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        // Double-sided so inner walls visible from both directions
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.doubleSidedGI = true;

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    // =========================================================================
    // MASK DRAWING (operates on bool array)
    // =========================================================================

    private void MaskCircle(bool[] mask, Vector2 center, int radius)
    {
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);
        int r2 = radius * radius;
        for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
                if (x * x + y * y <= r2)
                {
                    int px = cx + x, py = cy + y;
                    if (px >= 0 && px < texWidth && py >= 0 && py < texHeight)
                        mask[py * texWidth + px] = false;
                }
    }

    private void MaskLine(bool[] mask, Vector2 a, Vector2 b, int thickness)
    {
        float dist = Vector2.Distance(a, b);
        int steps = Mathf.CeilToInt(dist);
        int halfThick = Mathf.RoundToInt(thickness * 0.5f);
        for (int i = 0; i <= steps; i++)
        {
            float t = steps > 0 ? (float)i / steps : 0;
            Vector2 point = Vector2.Lerp(a, b, t);
            MaskCircle(mask, point, halfThick);
        }
    }

    private void MaskLegToBottom(bool[] mask, PoseData pose, Vector2[] positions,
        int[] jointIndices, int thickness)
    {
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
        MaskLine(mask, lowestPoint, new Vector2(lowestPoint.x, 0), thickness);
    }

    private void FloodFillEnclosed(bool[] mask)
    {
        int w = texWidth, h = texHeight;
        bool[] visited = new bool[w * h];
        Queue<int> queue = new Queue<int>();

        for (int x = 0; x < w; x++)
        {
            TrySeed(x, 0, w, h, mask, visited, queue);
            TrySeed(x, h - 1, w, h, mask, visited, queue);
        }
        for (int y = 0; y < h; y++)
        {
            TrySeed(0, y, w, h, mask, visited, queue);
            TrySeed(w - 1, y, w, h, mask, visited, queue);
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            if (x > 0) TrySeed(x - 1, y, w, h, mask, visited, queue);
            if (x < w - 1) TrySeed(x + 1, y, w, h, mask, visited, queue);
            if (y > 0) TrySeed(x, y - 1, w, h, mask, visited, queue);
            if (y < h - 1) TrySeed(x, y + 1, w, h, mask, visited, queue);
        }

        int filled = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            if (!visited[i] && mask[i])
            {
                mask[i] = false;
                filled++;
            }
        }
        if (filled > 0)
            Debug.Log($"[WallBaker] Flood filled {filled} enclosed pixels");
    }

    private void TrySeed(int x, int y, int w, int h, bool[] mask, bool[] visited, Queue<int> queue)
    {
        int idx = y * w + x;
        if (visited[idx]) return;
        if (!mask[idx]) return; // Cutout pixel, skip
        visited[idx] = true;
        queue.Enqueue(idx);
    }

    // =========================================================================
    // KEYPOINT MAPPING
    // =========================================================================

    private Vector2[] MapKeypointsToTexture(PoseData pose)
    {
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

        if (validCount < 2) { minX = 0; maxX = 1; minY = 0; maxY = 1; }

        float poseW = Mathf.Max(maxX - minX, 0.01f);
        float poseH = Mathf.Max(maxY - minY, 0.01f);
        float availW = texWidth - padding * 2;
        float availH = texHeight - padding * 2;
        float scale = Mathf.Min(availW / poseW, availH / poseH);
        float offsetX = padding + (availW - poseW * scale) * 0.5f;
        float offsetY = padding + (availH - poseH * scale) * 0.5f;

        Vector2[] result = new Vector2[pose.keypoints.Length];
        for (int i = 0; i < pose.keypoints.Length; i++)
        {
            float px = offsetX + (pose.keypoints[i].x - minX) * scale;
            float py = texHeight - (offsetY + (pose.keypoints[i].y - minY) * scale);
            result[i] = new Vector2(px, py);
        }
        return result;
    }

    // =========================================================================
    // MESH GENERATION
    // =========================================================================

    /// <summary>
    /// Creates a wall mesh with 2 submeshes:
    ///   Submesh 0: Front + back face quads (UV 0-1, used with cutout material)
    ///   Submesh 1: Inner wall quads extruded along cutout edges (solid material)
    /// </summary>
    private Mesh CreateWallMesh(bool[] mask, float w, float h, float d)
    {
        float hw = w * 0.5f, hh = h * 0.5f, hd = d * 0.5f;

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var normals = new List<Vector3>();
        var faceTris = new List<int>();  // Submesh 0: front/back
        var innerTris = new List<int>(); // Submesh 1: inner walls

        // --- Submesh 0: Front and back faces ---
        // Front face (Z = -hd)
        AddQuad(verts, uvs, normals, faceTris,
            new Vector3(-hw, -hh, -hd), new Vector3(hw, -hh, -hd),
            new Vector3(hw, hh, -hd), new Vector3(-hw, hh, -hd),
            Vector3.back,
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));

        // Back face (Z = +hd)
        AddQuad(verts, uvs, normals, faceTris,
            new Vector3(hw, -hh, hd), new Vector3(-hw, -hh, hd),
            new Vector3(-hw, hh, hd), new Vector3(hw, hh, hd),
            Vector3.forward,
            new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1));

        // --- Submesh 1: Outer depth edges (top, bottom, left, right of the box) ---
        // Top edge
        AddInnerQuad(verts, uvs, normals, innerTris,
            new Vector3(-hw, hh, -hd), new Vector3(hw, hh, -hd),
            new Vector3(hw, hh, hd), new Vector3(-hw, hh, hd),
            Vector3.up, w, d);
        // Bottom edge
        AddInnerQuad(verts, uvs, normals, innerTris,
            new Vector3(-hw, -hh, hd), new Vector3(hw, -hh, hd),
            new Vector3(hw, -hh, -hd), new Vector3(-hw, -hh, -hd),
            Vector3.down, w, d);
        // Left edge
        AddInnerQuad(verts, uvs, normals, innerTris,
            new Vector3(-hw, -hh, hd), new Vector3(-hw, -hh, -hd),
            new Vector3(-hw, hh, -hd), new Vector3(-hw, hh, hd),
            Vector3.left, h, d);
        // Right edge
        AddInnerQuad(verts, uvs, normals, innerTris,
            new Vector3(hw, -hh, -hd), new Vector3(hw, -hh, hd),
            new Vector3(hw, hh, hd), new Vector3(hw, hh, -hd),
            Vector3.right, h, d);

        // --- Submesh 1 (continued): Inner wall quads from edge detection ---
        // For each cutout pixel adjacent to a wall pixel, create a quad from front to back
        float pixW = w / texWidth;
        float pixH = h / texHeight;

        int mw = texWidth, mh = texHeight;
        int edgeCount = 0;

        for (int py = 0; py < mh; py++)
        {
            for (int px = 0; px < mw; px++)
            {
                // We only care about cutout pixels on the boundary
                if (mask[py * mw + px]) continue; // This is wall, skip

                // World position of this pixel center
                float wx = (px + 0.5f) / mw * w - hw;
                float wy = (py + 0.5f) / mh * h - hh;

                // Check 4 neighbors — if neighbor is wall, place a quad on that edge
                // Right neighbor is wall → place quad facing +X
                if (px + 1 < mw && mask[py * mw + (px + 1)])
                {
                    float ex = wx + pixW * 0.5f;
                    AddInnerQuad(verts, uvs, normals, innerTris,
                        new Vector3(ex, wy - pixH * 0.5f, -hd),
                        new Vector3(ex, wy - pixH * 0.5f, hd),
                        new Vector3(ex, wy + pixH * 0.5f, hd),
                        new Vector3(ex, wy + pixH * 0.5f, -hd),
                        Vector3.right, pixH, d);
                    edgeCount++;
                }
                // Left neighbor is wall → quad facing -X
                if (px - 1 >= 0 && mask[py * mw + (px - 1)])
                {
                    float ex = wx - pixW * 0.5f;
                    AddInnerQuad(verts, uvs, normals, innerTris,
                        new Vector3(ex, wy - pixH * 0.5f, hd),
                        new Vector3(ex, wy - pixH * 0.5f, -hd),
                        new Vector3(ex, wy + pixH * 0.5f, -hd),
                        new Vector3(ex, wy + pixH * 0.5f, hd),
                        Vector3.left, pixH, d);
                    edgeCount++;
                }
                // Top neighbor is wall → quad facing +Y
                if (py + 1 < mh && mask[(py + 1) * mw + px])
                {
                    float ey = wy + pixH * 0.5f;
                    AddInnerQuad(verts, uvs, normals, innerTris,
                        new Vector3(wx + pixW * 0.5f, ey, -hd),
                        new Vector3(wx + pixW * 0.5f, ey, hd),
                        new Vector3(wx - pixW * 0.5f, ey, hd),
                        new Vector3(wx - pixW * 0.5f, ey, -hd),
                        Vector3.up, pixW, d);
                    edgeCount++;
                }
                // Bottom neighbor is wall → quad facing -Y
                if (py - 1 >= 0 && mask[(py - 1) * mw + px])
                {
                    float ey = wy - pixH * 0.5f;
                    AddInnerQuad(verts, uvs, normals, innerTris,
                        new Vector3(wx - pixW * 0.5f, ey, -hd),
                        new Vector3(wx - pixW * 0.5f, ey, hd),
                        new Vector3(wx + pixW * 0.5f, ey, hd),
                        new Vector3(wx + pixW * 0.5f, ey, -hd),
                        Vector3.down, pixW, d);
                    edgeCount++;
                }
            }
        }

        Debug.Log($"[WallBaker] Generated {edgeCount} inner wall quads");

        Mesh mesh = new Mesh();
        mesh.name = "WallMesh";
        // Use 32-bit indices if we have lots of verts
        if (verts.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(faceTris, 0);  // Front/back faces
        mesh.SetTriangles(innerTris, 1); // Inner walls
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddQuad(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals,
        List<int> tris, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        Vector3 normal, Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL)
    {
        int s = verts.Count;
        verts.Add(bl); verts.Add(br); verts.Add(tr); verts.Add(tl);
        uvs.Add(uvBL); uvs.Add(uvBR); uvs.Add(uvTR); uvs.Add(uvTL);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
        tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
    }

    /// <summary>
    /// Adds an inner wall quad with UVs that tile the wall texture naturally.
    /// U = position along the edge, V = depth through the wall.
    /// </summary>
    private void AddInnerQuad(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals,
        List<int> tris, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
        Vector3 normal, float edgeSize, float depth)
    {
        int s = verts.Count;
        verts.Add(bl); verts.Add(br); verts.Add(tr); verts.Add(tl);

        // UV: tile based on world-space size so texture scale matches the front face
        float uScale = depth * textureTiling;
        float vScale = edgeSize * textureTiling;
        uvs.Add(new Vector2(0, 0));
        uvs.Add(new Vector2(uScale, 0));
        uvs.Add(new Vector2(uScale, vScale));
        uvs.Add(new Vector2(0, vScale));

        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
        tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void MakeReadable(Texture2D tex, List<TextureImporter> restoreList)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        TextureImporter imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null && !imp.isReadable)
        {
            imp.isReadable = true;
            imp.SaveAndReimport();
            restoreList.Add(imp);
        }
    }

    private void RestoreReadable(List<TextureImporter> importers)
    {
        foreach (var imp in importers)
        {
            imp.isReadable = false;
            imp.SaveAndReimport();
        }
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
