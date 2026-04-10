using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "PolygonWallShape", menuName = "PoseGame/Wall Fit/Polygon Wall Shape")]
public class PolygonWallShapeAsset : ScriptableObject
{
    [SerializeField] private List<Vector2> vertices = new List<Vector2>
    {
        new Vector2(-0.64f, 0.37f),
        new Vector2(0.64f, 0.37f),
        new Vector2(0.64f, 2.33f),
        new Vector2(-0.64f, 2.33f),
    };

    [SerializeField, Min(0.05f)] private float shrinkDuration = 2.5f;
    [SerializeField] private List<WallOrbTargetData> orbTargets = new List<WallOrbTargetData>();
    [SerializeField] private bool hasReferenceAvatarBounds;
    [FormerlySerializedAs("referenceAvatarViewportBounds")]
    [SerializeField] private Rect referenceAvatarBounds = new Rect(-0.46f, 0.32f, 0.92f, 2.06f);

    public IReadOnlyList<Vector2> Vertices => vertices;
    public float ShrinkDuration => shrinkDuration;
    public IReadOnlyList<WallOrbTargetData> OrbTargets => orbTargets;
    public bool HasReferenceAvatarBounds => hasReferenceAvatarBounds;
    public Rect ReferenceAvatarBounds => referenceAvatarBounds;

    public void SetData(IReadOnlyList<Vector2> sourceVertices, float duration, IReadOnlyList<WallOrbTargetData> sourceOrbTargets = null, Rect? sourceReferenceAvatarBounds = null)
    {
        vertices.Clear();
        orbTargets.Clear();

        if (sourceVertices != null)
        {
            for (int index = 0; index < sourceVertices.Count; index++)
            {
                vertices.Add(sourceVertices[index]);
            }
        }

        if (sourceOrbTargets != null)
        {
            for (int index = 0; index < sourceOrbTargets.Count; index++)
            {
                WallOrbTargetData orbTarget = sourceOrbTargets[index];
                orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.02f, 0.5f);
                orbTargets.Add(orbTarget);
            }
        }

        shrinkDuration = Mathf.Max(0.05f, duration);
        hasReferenceAvatarBounds = sourceReferenceAvatarBounds.HasValue;
        if (sourceReferenceAvatarBounds.HasValue)
        {
            referenceAvatarBounds = sourceReferenceAvatarBounds.Value;
        }
    }

    private void OnValidate()
    {
        shrinkDuration = Mathf.Max(0.05f, shrinkDuration);

        for (int index = 0; index < orbTargets.Count; index++)
        {
            WallOrbTargetData orbTarget = orbTargets[index];
            orbTarget.radius = Mathf.Clamp(orbTarget.radius, 0.02f, 0.5f);
            orbTargets[index] = orbTarget;
        }
    }
}
