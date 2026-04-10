using UnityEngine;

[CreateAssetMenu(fileName = "RectangleWallShape", menuName = "PoseGame/Wall Fit/Rectangle Wall Shape")]
public class RectangleWallShapeAsset : ScriptableObject
{
    [SerializeField] private Vector2 center = new Vector2(0f, 1.35f);
    [SerializeField] private Vector2 size = new Vector2(1.3f, 1.96f);
    [SerializeField, Min(0.05f)] private float shrinkDuration = 2.5f;

    public float ShrinkDuration => shrinkDuration;

    public Rect TargetRect
    {
        get
        {
            Vector2 clampedSize = new Vector2(
                Mathf.Max(0.1f, size.x),
                Mathf.Max(0.1f, size.y));

            Vector2 min = center - (clampedSize * 0.5f);
            return new Rect(min, clampedSize);
        }
    }

    private void OnValidate()
    {
        size.x = Mathf.Max(0.1f, size.x);
        size.y = Mathf.Max(0.1f, size.y);
    }
}
