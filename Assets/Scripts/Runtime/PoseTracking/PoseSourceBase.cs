using UnityEngine;

public abstract class PoseSourceBase : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float minimumConfidence = 0.5f;

    public float MinimumConfidence => minimumConfidence;

    public abstract bool TryGetPose(PoseFrame outputFrame);
}