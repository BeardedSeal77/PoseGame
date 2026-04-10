using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public sealed class IsometricCameraController : MonoBehaviour
{
    [Tooltip("The transform the camera looks at (typically the avatar root).")]
    [SerializeField] private Transform target;

    [Tooltip("Elevation angle in degrees above the horizontal.")]
    [SerializeField, Range(10f, 60f)] private float elevationAngle = 35f;

    [Tooltip("Distance from the target.")]
    [SerializeField, Min(1f)] private float distance = 8f;

    [Tooltip("Vertical offset above the target's position (aim at chest height).")]
    [SerializeField] private float verticalOffset = 1f;

    private void Start()
    {
        ApplyPosition();
    }

    private void LateUpdate()
    {
        ApplyPosition();
    }

    private void OnValidate()
    {
        ApplyPosition();
    }

    private void ApplyPosition()
    {
        if (target == null)
        {
            return;
        }

        Vector3 lookTarget = target.position + Vector3.up * verticalOffset;
        float elevationRad = elevationAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            0f,
            distance * Mathf.Sin(elevationRad),
            -distance * Mathf.Cos(elevationRad));

        transform.position = lookTarget + offset;
        transform.rotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
    }
}
