using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(PoseShapeAuthoringRig))]
public class PoseShapeAuthoringRigEditor : Editor
{
    private const float VertexSnapStep = 0.025f;
    private const float VertexPickThreshold = 0.02f;
    private const float RotateSensitivity = 0.35f;

    private readonly HashSet<int> selectedVertexIndices = new HashSet<int>();
    private readonly Dictionary<int, Vector2> dragStartVertices = new Dictionary<int, Vector2>();

    private int activeVertexIndex = -1;
    private bool isDraggingVertices;
    private bool isMarqueeSelecting;
    private bool isRotatingPlayer;
    private Vector2 dragStartViewportPoint;
    private Vector2 marqueeStart;
    private Vector2 marqueeEnd;
    private float rotateStartMouseX;
    private float rotateStartYaw;
    private PoseShapeAuthoringRig.JointHandleId selectedJointHandle = PoseShapeAuthoringRig.JointHandleId.LeftHand;
    private int selectedOrbIndex = -1;

    private void OnDisable()
    {
        Tools.hidden = false;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        PoseShapeAuthoringRig rig = (PoseShapeAuthoringRig)target;
        DrawActionButtons(rig);

        EditorGUILayout.Space();
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "hipsTarget",
            "chestTarget",
            "leftHandTarget",
            "rightHandTarget",
            "leftFootTarget",
            "rightFootTarget");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox(
            "Vertices snap by default and hold Shift for free move. Hold Ctrl to marquee-select or add individual vertices. Right click a vertex to delete it, or right drag an edge to create and immediately drag a new vertex.",
            MessageType.Info);
    }

    private void OnSceneGUI()
    {
        PoseShapeAuthoringRig rig = (PoseShapeAuthoringRig)target;
        if (rig.AuthoringCamera == null)
        {
            return;
        }

        LockSelectionToRig(rig);
        Tools.hidden = true;

        Event currentEvent = Event.current;
        HandlePlayerRotation(rig, currentEvent);
        HandleVertexInput(rig, currentEvent);

        DrawPolygon(rig);
        DrawJointHandles(rig);
        DrawOrbHandles(rig);
        DrawSelectionMarquee();
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
    private static void DrawPoseShapeRigGizmo(PoseShapeAuthoringRig rig, GizmoType gizmoType)
    {
        if (rig == null || rig.AuthoringCamera == null || rig.PolygonVertices.Count < 3)
        {
            return;
        }

        Handles.zTest = CompareFunction.Always;
        Handles.color = new Color(1f, 0.85f, 0.2f, 0.95f);
        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            int nextIndex = (index + 1) % rig.PolygonVertices.Count;
            if (rig.TryViewportToWorld(rig.PolygonVertices[index], out Vector3 a) &&
                rig.TryViewportToWorld(rig.PolygonVertices[nextIndex], out Vector3 b))
            {
                Handles.DrawAAPolyLine(4f, a, b);
            }
        }

        Handles.zTest = CompareFunction.LessEqual;
    }

    private void DrawPolygon(PoseShapeAuthoringRig rig)
    {
        if (rig.PolygonVertices.Count < 3)
        {
            return;
        }

        Handles.zTest = CompareFunction.Always;
        Handles.color = new Color(1f, 0.85f, 0.2f, 1f);
        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            int nextIndex = (index + 1) % rig.PolygonVertices.Count;
            if (rig.TryViewportToWorld(rig.PolygonVertices[index], out Vector3 a) &&
                rig.TryViewportToWorld(rig.PolygonVertices[nextIndex], out Vector3 b))
            {
                Handles.DrawAAPolyLine(4f, a, b);
            }
        }

        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            if (!rig.TryViewportToWorld(rig.PolygonVertices[index], out Vector3 worldPoint))
            {
                continue;
            }

            float handleSize = HandleUtility.GetHandleSize(worldPoint) * 0.08f;
            bool isSelected = selectedVertexIndices.Contains(index);
            bool isActive = index == activeVertexIndex;

            Handles.color = isActive
                ? new Color(0.2f, 1f, 0.95f, 1f)
                : isSelected
                    ? new Color(1f, 0.92f, 0.28f, 1f)
                    : new Color(1f, 0.45f, 0.25f, 1f);

            Handles.SphereHandleCap(0, worldPoint, Quaternion.identity, handleSize, EventType.Repaint);
            Handles.Label(worldPoint + (Vector3.up * handleSize * 0.5f), $"{index}");
        }

        Handles.zTest = CompareFunction.LessEqual;
    }

    private void DrawJointHandles(PoseShapeAuthoringRig rig)
    {
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.Hips, new Color(0.35f, 0.9f, 1f, 1f));
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.Chest, new Color(0.85f, 0.45f, 1f, 1f));
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.LeftHand, new Color(0.25f, 1f, 0.35f, 1f));
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.RightHand, new Color(0.25f, 1f, 0.35f, 1f));
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.LeftFoot, new Color(1f, 0.55f, 0.25f, 1f));
        DrawJointHandle(rig, PoseShapeAuthoringRig.JointHandleId.RightFoot, new Color(1f, 0.55f, 0.25f, 1f));
    }

    private void DrawOrbHandles(PoseShapeAuthoringRig rig)
    {
        for (int index = 0; index < rig.OrbTargets.Count; index++)
        {
            if (!rig.TryGetOrbWorldPosition(index, out Vector3 worldPosition))
            {
                continue;
            }

            float worldRadius = rig.GetOrbWorldRadius(index);
            float handleSize = HandleUtility.GetHandleSize(worldPosition) * 0.08f;

            Handles.zTest = CompareFunction.Always;
            Handles.color = index == selectedOrbIndex ? new Color(0.35f, 1f, 0.95f, 1f) : new Color(0.35f, 0.8f, 1f, 0.95f);
            Handles.DrawWireDisc(worldPosition, rig.AuthoringCamera.transform.forward, worldRadius, 2f);

            if (Handles.Button(worldPosition, Quaternion.identity, handleSize, handleSize, Handles.CircleHandleCap))
            {
                selectedOrbIndex = index;
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            Vector3 movedPosition = Handles.FreeMoveHandle(worldPosition, handleSize * 0.9f, Vector3.zero, Handles.CircleHandleCap);
            if (EditorGUI.EndChangeCheck() && rig.TryWorldToViewport(movedPosition, out Vector2 viewportPoint))
            {
                Undo.RecordObject(rig, "Move Orb Target");
                rig.SetOrbTargetPosition(index, viewportPoint);
                selectedOrbIndex = index;
                EditorUtility.SetDirty(rig);
            }

            Handles.Label(worldPosition + (Vector3.up * handleSize * 0.75f), $"Orb {index + 1}");
        }
    }

    private void DrawJointHandle(PoseShapeAuthoringRig rig, PoseShapeAuthoringRig.JointHandleId jointHandleId, Color color)
    {
        if (!rig.TryGetJointHandlePosition(jointHandleId, out Vector3 targetWorldPosition))
        {
            return;
        }

        Vector3 handleWorldPosition = rig.GetVisualHandlePosition(targetWorldPosition);
        float handleSize = HandleUtility.GetHandleSize(handleWorldPosition) * 0.09f;

        Handles.zTest = CompareFunction.Always;
        Handles.color = jointHandleId == selectedJointHandle ? Color.white : color;
        if (Handles.Button(handleWorldPosition, Quaternion.identity, handleSize, handleSize, Handles.SphereHandleCap))
        {
            selectedJointHandle = jointHandleId;
            Repaint();
        }

        EditorGUI.BeginChangeCheck();
        var fmh_172_77_639108238817124780 = Quaternion.identity; Vector3 movedPosition = Handles.FreeMoveHandle(handleWorldPosition, handleSize * 0.95f, Vector3.zero, Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(rig, $"Move {jointHandleId} Handle");
            rig.SetJointHandlePosition(jointHandleId, movedPosition);
            selectedJointHandle = jointHandleId;
            EditorUtility.SetDirty(rig);
        }

        if (jointHandleId == selectedJointHandle)
        {
            Handles.Label(handleWorldPosition + (Vector3.up * handleSize * 0.6f), jointHandleId.ToString());
        }
    }

    private void HandleVertexInput(PoseShapeAuthoringRig rig, Event currentEvent)
    {
        if (currentEvent.alt)
        {
            return;
        }

        switch (currentEvent.type)
        {
            case EventType.MouseDown:
                HandleMouseDown(rig, currentEvent);
                break;
            case EventType.MouseDrag:
                HandleMouseDrag(rig, currentEvent);
                break;
            case EventType.MouseUp:
                HandleMouseUp(rig, currentEvent);
                break;
        }
    }

    private void HandleMouseDown(PoseShapeAuthoringRig rig, Event currentEvent)
    {
        if (currentEvent.button == 0)
        {
            if (TryGetViewportPointFromMouse(rig, currentEvent.mousePosition, out Vector2 viewportPoint))
            {
                int vertexIndex = GetClosestVertexIndex(rig, viewportPoint, VertexPickThreshold);

                if (currentEvent.control)
                {
                    if (vertexIndex >= 0)
                    {
                        ToggleVertexSelection(vertexIndex);
                        activeVertexIndex = vertexIndex;
                        currentEvent.Use();
                        Repaint();
                        return;
                    }

                    isMarqueeSelecting = true;
                    marqueeStart = currentEvent.mousePosition;
                    marqueeEnd = currentEvent.mousePosition;
                    currentEvent.Use();
                    return;
                }

                if (vertexIndex >= 0)
                {
                    if (!selectedVertexIndices.Contains(vertexIndex) || selectedVertexIndices.Count <= 1)
                    {
                        SetSingleVertexSelection(vertexIndex);
                    }
                    else
                    {
                        activeVertexIndex = vertexIndex;
                    }

                    BeginVertexDrag(rig, viewportPoint, "Move Polygon Vertices");
                    currentEvent.Use();
                    return;
                }
            }

            ClearVertexSelection();
            Repaint();
            return;
        }

        if (currentEvent.button != 1 || !TryGetViewportPointFromMouse(rig, currentEvent.mousePosition, out Vector2 rightClickViewport))
        {
            return;
        }

        int vertexToDelete = GetClosestVertexIndex(rig, rightClickViewport, VertexPickThreshold);
        if (vertexToDelete >= 0)
        {
            if (rig.PolygonVertices.Count > 3)
            {
                Undo.RecordObject(rig, "Delete Polygon Vertex");
                rig.RemoveVertexAt(vertexToDelete);
                RemoveSelectionAt(vertexToDelete, rig.PolygonVertices.Count);
                activeVertexIndex = Mathf.Clamp(vertexToDelete - 1, -1, rig.PolygonVertices.Count - 1);
                EditorUtility.SetDirty(rig);
            }

            currentEvent.Use();
            return;
        }

        int segmentIndex = GetClosestSegmentIndex(rig, rightClickViewport, VertexPickThreshold, out Vector2 projectedPoint);
        if (segmentIndex < 0)
        {
            return;
        }

        Undo.RecordObject(rig, "Insert Polygon Vertex");
        activeVertexIndex = rig.InsertVertex(segmentIndex + 1, ApplySnapPolicy(projectedPoint, currentEvent.shift));
        SetSingleVertexSelection(activeVertexIndex);
        BeginVertexDrag(rig, projectedPoint, "Insert Polygon Vertex");
        currentEvent.Use();
    }

    private void HandleMouseDrag(PoseShapeAuthoringRig rig, Event currentEvent)
    {
        if (isRotatingPlayer || currentEvent.alt)
        {
            return;
        }

        if (isMarqueeSelecting)
        {
            marqueeEnd = currentEvent.mousePosition;
            currentEvent.Use();
            Repaint();
            return;
        }

        if (!isDraggingVertices || activeVertexIndex < 0 || !dragStartVertices.ContainsKey(activeVertexIndex))
        {
            return;
        }

        if (!TryGetViewportPointFromMouse(rig, currentEvent.mousePosition, out Vector2 currentViewportPoint))
        {
            return;
        }

        Vector2 originalActivePosition = dragStartVertices[activeVertexIndex];
        Vector2 desiredActivePosition = originalActivePosition + (currentViewportPoint - dragStartViewportPoint);
        desiredActivePosition = ApplySnapPolicy(desiredActivePosition, currentEvent.shift);
        Vector2 delta = desiredActivePosition - originalActivePosition;

        foreach (KeyValuePair<int, Vector2> pair in dragStartVertices)
        {
            rig.SetVertex(pair.Key, pair.Value + delta);
        }

        EditorUtility.SetDirty(rig);
        currentEvent.Use();
    }

    private void HandleMouseUp(PoseShapeAuthoringRig rig, Event currentEvent)
    {
        if (isRotatingPlayer && currentEvent.button == 0)
        {
            isRotatingPlayer = false;
            currentEvent.Use();
            return;
        }

        if (isMarqueeSelecting && currentEvent.button == 0)
        {
            ApplyMarqueeSelection(rig);
            isMarqueeSelecting = false;
            currentEvent.Use();
            return;
        }

        if (isDraggingVertices && (currentEvent.button == 0 || currentEvent.button == 1))
        {
            isDraggingVertices = false;
            dragStartVertices.Clear();
            currentEvent.Use();
        }
    }

    private void BeginVertexDrag(PoseShapeAuthoringRig rig, Vector2 startViewportPoint, string undoLabel)
    {
        if (activeVertexIndex < 0)
        {
            return;
        }

        dragStartVertices.Clear();
        if (selectedVertexIndices.Count == 0)
        {
            selectedVertexIndices.Add(activeVertexIndex);
        }

        foreach (int selectedIndex in selectedVertexIndices)
        {
            if (selectedIndex >= 0 && selectedIndex < rig.PolygonVertices.Count)
            {
                dragStartVertices[selectedIndex] = rig.PolygonVertices[selectedIndex];
            }
        }

        dragStartViewportPoint = startViewportPoint;
        isDraggingVertices = true;
        Undo.RecordObject(rig, undoLabel);
    }

    private void HandlePlayerRotation(PoseShapeAuthoringRig rig, Event currentEvent)
    {
        if (!currentEvent.alt)
        {
            return;
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            rotateStartMouseX = currentEvent.mousePosition.x;
            rotateStartYaw = rig.GetPlayerYaw();
            isRotatingPlayer = true;
            Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Rotate Player Yaw");
            currentEvent.Use();
            return;
        }

        if (currentEvent.type == EventType.MouseDrag && isRotatingPlayer)
        {
            float deltaDegrees = (currentEvent.mousePosition.x - rotateStartMouseX) * RotateSensitivity;
            rig.SetPlayerYaw(rotateStartYaw + deltaDegrees);
            EditorUtility.SetDirty(rig);
            currentEvent.Use();
            return;
        }

        if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && isRotatingPlayer)
        {
            isRotatingPlayer = false;
            currentEvent.Use();
        }
    }

    private void DrawSelectionMarquee()
    {
        if (!isMarqueeSelecting)
        {
            return;
        }

        Rect rect = GetScreenRect(marqueeStart, marqueeEnd);
        Handles.BeginGUI();
        Color fillColor = new Color(0.2f, 0.7f, 1f, 0.18f);
        Color outlineColor = new Color(0.2f, 0.7f, 1f, 0.95f);
        EditorGUI.DrawRect(rect, fillColor);
        DrawRectOutline(rect, outlineColor);
        Handles.EndGUI();
    }

    private static int GetClosestVertexIndex(PoseShapeAuthoringRig rig, Vector2 point, float threshold)
    {
        float bestDistance = float.MaxValue;
        int bestIndex = -1;

        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            float distance = Vector2.Distance(point, rig.PolygonVertices[index]);
            if (distance < threshold && distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    private static int GetClosestSegmentIndex(PoseShapeAuthoringRig rig, Vector2 point, float threshold, out Vector2 projectedPoint)
    {
        float bestDistance = float.MaxValue;
        int bestIndex = -1;
        projectedPoint = default;

        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            int nextIndex = (index + 1) % rig.PolygonVertices.Count;
            Vector2 a = rig.PolygonVertices[index];
            Vector2 b = rig.PolygonVertices[nextIndex];
            Vector2 closestPoint = ClosestPointOnSegment(point, a, b);
            float distance = Vector2.Distance(point, closestPoint);
            if (distance < threshold && distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
                projectedPoint = closestPoint;
            }
        }

        return bestIndex;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float denominator = Mathf.Max(0.000001f, Vector2.Dot(ab, ab));
        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
        return a + (ab * t);
    }

    private static Vector2 ApplySnapPolicy(Vector2 viewportPoint, bool freeMove)
    {
        if (freeMove)
        {
            return ClampViewportPoint(viewportPoint);
        }

        Vector2 snapped = new Vector2(
            Mathf.Round(viewportPoint.x / VertexSnapStep) * VertexSnapStep,
            Mathf.Round(viewportPoint.y / VertexSnapStep) * VertexSnapStep);
        return ClampViewportPoint(snapped);
    }

    private static Vector2 ClampViewportPoint(Vector2 viewportPoint)
    {
        return new Vector2(
            Mathf.Clamp01(viewportPoint.x),
            Mathf.Clamp01(viewportPoint.y));
    }

    private static void LockSelectionToRig(PoseShapeAuthoringRig rig)
    {
        if (Selection.activeGameObject != rig.gameObject)
        {
            Selection.activeGameObject = rig.gameObject;
        }
    }

    private void DrawActionButtons(PoseShapeAuthoringRig rig)
    {
        EditorGUILayout.BeginHorizontal();
        try
        {
            if (GUILayout.Button("Reset Pose", EditorStyles.miniButtonLeft))
            {
                Undo.RegisterFullObjectHierarchyUndo(rig.gameObject, "Reset Pose To Imported Pose");
                rig.EnsureTargetHandlesCreated();
                rig.ResetPoseToImportedPose();
                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Reset Shape", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(rig, "Reset Polygon Shape");
                rig.ResetShapeToRectangle();
                ClearVertexSelection();
                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Shape Avatar", EditorStyles.miniButtonRight))
            {
                Undo.RecordObject(rig, "Shape Polygon Around Avatar");
                rig.FitPolygonToAvatar();
                ClearVertexSelection();
                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
            }
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        try
        {
            if (GUILayout.Button("Save Shape", EditorStyles.miniButtonLeft))
            {
                SaveShapeAs(rig);
            }

            if (GUILayout.Button("Load Shape", EditorStyles.miniButtonRight))
            {
                LoadShapeFromFolder(rig);
            }
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        try
        {
            if (GUILayout.Button("Add Orb", EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(rig, "Add Orb Target");
                selectedOrbIndex = rig.AddOrbTarget(new Vector2(0.5f, 0.5f));
                EditorUtility.SetDirty(rig);
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(rig.OrbTargets.Count == 0))
            {
                if (GUILayout.Button("Clear Orbs", EditorStyles.miniButtonRight))
                {
                    Undo.RecordObject(rig, "Clear Orb Targets");
                    rig.ClearOrbTargets();
                    selectedOrbIndex = -1;
                    EditorUtility.SetDirty(rig);
                    SceneView.RepaintAll();
                }
            }
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }
    }

    private static void SaveShapeAs(PoseShapeAuthoringRig rig)
    {
        string defaultFolder = GetSuggestedShapeFolder(rig);
        string defaultName = rig.ShapeAsset != null ? rig.ShapeAsset.name : "PolygonWallShape";
        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Save Polygon Wall Shape",
            defaultName,
            "asset",
            "Choose a name and folder for the polygon wall shape asset.",
            defaultFolder);

        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        PolygonWallShapeAsset asset = AssetDatabase.LoadAssetAtPath<PolygonWallShapeAsset>(assetPath);
        if (asset == null)
        {
            asset = CreateInstance<PolygonWallShapeAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        Undo.RecordObject(asset, "Save Polygon Shape Asset");
        asset.SetData(rig.PolygonVertices, rig.ShrinkDuration);
        rig.AssignShapeAsset(asset);
        EditorUtility.SetDirty(asset);
        EditorUtility.SetDirty(rig);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void LoadShapeFromFolder(PoseShapeAuthoringRig rig)
    {
        string folder = GetSuggestedShapeFolder(rig);
        string absoluteFolder = Path.GetFullPath(folder);
        string selectedPath = EditorUtility.OpenFilePanel("Load Polygon Wall Shape", absoluteFolder, "asset");

        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        string relativePath = FileUtil.GetProjectRelativePath(selectedPath);
        PolygonWallShapeAsset asset = AssetDatabase.LoadAssetAtPath<PolygonWallShapeAsset>(relativePath);
        if (asset == null)
        {
            EditorUtility.DisplayDialog("Invalid Shape Asset", "Pick a PolygonWallShapeAsset inside this Unity project.", "OK");
            return;
        }

        Undo.RecordObject(rig, "Assign Polygon Shape Asset");
        rig.AssignShapeAsset(asset);
        rig.LoadFromShapeAsset();
        EditorUtility.SetDirty(rig);
        SceneView.RepaintAll();
    }

    private static string GetSuggestedShapeFolder(PoseShapeAuthoringRig rig)
    {
        if (rig.ShapeAsset != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(rig.ShapeAsset);
            string assetFolder = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(assetFolder) && AssetDatabase.IsValidFolder(assetFolder))
            {
                return assetFolder.Replace('\\', '/');
            }
        }

        if (AssetDatabase.IsValidFolder("Assets/Resources/WallShapes"))
        {
            return "Assets/Resources/WallShapes";
        }

        return "Assets";
    }

    private static bool TryGetViewportPointFromMouse(PoseShapeAuthoringRig rig, Vector2 mousePosition, out Vector2 viewportPoint)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        return rig.TryProjectSceneRay(ray, out viewportPoint);
    }

    private void ToggleVertexSelection(int index)
    {
        if (index < 0)
        {
            return;
        }

        if (!selectedVertexIndices.Add(index))
        {
            selectedVertexIndices.Remove(index);
            if (activeVertexIndex == index)
            {
                activeVertexIndex = selectedVertexIndices.Count > 0 ? GetFirstSelectedIndex() : -1;
            }
        }
    }

    private void SetSingleVertexSelection(int index)
    {
        selectedVertexIndices.Clear();
        activeVertexIndex = index;
        if (index >= 0)
        {
            selectedVertexIndices.Add(index);
        }
    }

    private void ClearVertexSelection()
    {
        selectedVertexIndices.Clear();
        activeVertexIndex = -1;
    }

    private int GetFirstSelectedIndex()
    {
        foreach (int index in selectedVertexIndices)
        {
            return index;
        }

        return -1;
    }

    private void ApplyMarqueeSelection(PoseShapeAuthoringRig rig)
    {
        Rect selectionRect = GetScreenRect(marqueeStart, marqueeEnd);
        for (int index = 0; index < rig.PolygonVertices.Count; index++)
        {
            if (!rig.TryViewportToWorld(rig.PolygonVertices[index], out Vector3 worldPoint))
            {
                continue;
            }

            Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldPoint);
            if (selectionRect.Contains(guiPoint))
            {
                selectedVertexIndices.Add(index);
                activeVertexIndex = index;
            }
        }

        Repaint();
    }

    private void RemoveSelectionAt(int removedIndex, int newVertexCount)
    {
        HashSet<int> remappedSelection = new HashSet<int>();
        foreach (int selectedIndex in selectedVertexIndices)
        {
            if (selectedIndex == removedIndex)
            {
                continue;
            }

            remappedSelection.Add(selectedIndex > removedIndex ? selectedIndex - 1 : selectedIndex);
        }

        selectedVertexIndices.Clear();
        foreach (int selectedIndex in remappedSelection)
        {
            if (selectedIndex >= 0 && selectedIndex < newVertexCount)
            {
                selectedVertexIndices.Add(selectedIndex);
            }
        }

        if (activeVertexIndex == removedIndex)
        {
            activeVertexIndex = selectedVertexIndices.Count > 0 ? GetFirstSelectedIndex() : -1;
        }
        else if (activeVertexIndex > removedIndex)
        {
            activeVertexIndex -= 1;
        }
    }

    private static Rect GetScreenRect(Vector2 start, Vector2 end)
    {
        Vector2 topLeft = Vector2.Min(start, end);
        Vector2 bottomRight = Vector2.Max(start, end);
        return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    private static void DrawRectOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }
}
