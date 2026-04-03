using UnityEngine;

[DisallowMultipleComponent]
public class RectangleWallDefinition : MonoBehaviour
{
    [SerializeField] private Vector2 center = new Vector2(0.5f, 0.5f);
    [SerializeField] private Vector2 size = new Vector2(0.28f, 0.72f);
    [SerializeField, Min(0.05f)] private float shrinkDuration = 2.5f;

    public float ShrinkDuration => shrinkDuration;

    public Rect TargetViewportRect
    {
        get
        {
            Vector2 clampedSize = new Vector2(
                Mathf.Clamp(size.x, 0.05f, 1f),
                Mathf.Clamp(size.y, 0.05f, 1f));

            Vector2 clampedCenter = new Vector2(
                Mathf.Clamp(center.x, clampedSize.x * 0.5f, 1f - (clampedSize.x * 0.5f)),
                Mathf.Clamp(center.y, clampedSize.y * 0.5f, 1f - (clampedSize.y * 0.5f)));

            Vector2 min = clampedCenter - (clampedSize * 0.5f);
            return new Rect(min, clampedSize);
        }
    }

    private void OnValidate()
    {
        size.x = Mathf.Clamp(size.x, 0.05f, 1f);
        size.y = Mathf.Clamp(size.y, 0.05f, 1f);
    }
}