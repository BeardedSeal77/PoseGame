using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PolygonWallShape", menuName = "PoseGame/Wall Fit/Polygon Wall Shape")]
public class PolygonWallShapeAsset : ScriptableObject
{
    [SerializeField] private List<Vector2> vertices = new List<Vector2>
    {
        new Vector2(0.36f, 0.12f),
        new Vector2(0.64f, 0.12f),
        new Vector2(0.64f, 0.88f),
        new Vector2(0.36f, 0.88f),
    };

    [SerializeField, Min(0.05f)] private float shrinkDuration = 2.5f;

    public IReadOnlyList<Vector2> Vertices => vertices;
    public float ShrinkDuration => shrinkDuration;

    public void SetData(IReadOnlyList<Vector2> sourceVertices, float duration)
    {
        vertices.Clear();

        if (sourceVertices != null)
        {
            for (int index = 0; index < sourceVertices.Count; index++)
            {
                vertices.Add(ClampViewportPoint(sourceVertices[index]));
            }
        }

        shrinkDuration = Mathf.Max(0.05f, duration);
    }

    private void OnValidate()
    {
        shrinkDuration = Mathf.Max(0.05f, shrinkDuration);

        for (int index = 0; index < vertices.Count; index++)
        {
            vertices[index] = ClampViewportPoint(vertices[index]);
        }
    }

    private static Vector2 ClampViewportPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }
}