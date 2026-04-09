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
    [SerializeField] private List<WallOrbTargetData> orbTargets = new List<WallOrbTargetData>();
    [SerializeField] private bool hasReferenceAvatarBounds;
    [SerializeField] private Rect referenceAvatarViewportBounds = new Rect(0.3f, 0.1f, 0.4f, 0.8f);

    public IReadOnlyList<Vector2> Vertices => vertices;
    public float ShrinkDuration => shrinkDuration;
    public IReadOnlyList<WallOrbTargetData> OrbTargets => orbTargets;
    public bool HasReferenceAvatarBounds => hasReferenceAvatarBounds;
    public Rect ReferenceAvatarViewportBounds => referenceAvatarViewportBounds;

    public void SetData(IReadOnlyList<Vector2> sourceVertices, float duration, IReadOnlyList<WallOrbTargetData> sourceOrbTargets = null, Rect? sourceReferenceAvatarBounds = null)
    {
        vertices.Clear();
        orbTargets.Clear();

        if (sourceVertices != null)
        {
            for (int index = 0; index < sourceVertices.Count; index++)
            {
                vertices.Add(ClampViewportPoint(sourceVertices[index]));
            }
        }

        if (sourceOrbTargets != null)
        {
            for (int index = 0; index < sourceOrbTargets.Count; index++)
            {
                WallOrbTargetData orbTarget = sourceOrbTargets[index];
                orbTarget.viewportPosition = ClampViewportPoint(orbTarget.viewportPosition);
                orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.01f, 0.2f);
                orbTargets.Add(orbTarget);
            }
        }

        shrinkDuration = Mathf.Max(0.05f, duration);
        hasReferenceAvatarBounds = sourceReferenceAvatarBounds.HasValue;
        if (sourceReferenceAvatarBounds.HasValue)
        {
            referenceAvatarViewportBounds = ClampRect(sourceReferenceAvatarBounds.Value);
        }
    }

    private void OnValidate()
    {
        shrinkDuration = Mathf.Max(0.05f, shrinkDuration);

        for (int index = 0; index < vertices.Count; index++)
        {
            vertices[index] = ClampViewportPoint(vertices[index]);
        }

        for (int index = 0; index < orbTargets.Count; index++)
        {
            WallOrbTargetData orbTarget = orbTargets[index];
            orbTarget.viewportPosition = ClampViewportPoint(orbTarget.viewportPosition);
            orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.01f, 0.2f);
            orbTargets[index] = orbTarget;
        }

        if (hasReferenceAvatarBounds)
        {
            referenceAvatarViewportBounds = ClampRect(referenceAvatarViewportBounds);
        }
    }

    private static Vector2 ClampViewportPoint(Vector2 point)
    {
        return new Vector2(
            Mathf.Clamp01(point.x),
            Mathf.Clamp01(point.y));
    }

    private static Rect ClampRect(Rect rect)
    {
        Vector2 min = ClampViewportPoint(rect.min);
        Vector2 max = ClampViewportPoint(rect.max);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}