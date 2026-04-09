using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public sealed class FixedAspectCamera : MonoBehaviour
{
    [SerializeField] private float aspectWidth = 16f;
    [SerializeField] private float aspectHeight = 9f;

    private Camera targetCamera;

    private void OnEnable()
    {
        ApplyAspect();
    }

    private void OnValidate()
    {
        ApplyAspect();
    }

    private void LateUpdate()
    {
        ApplyAspect();
    }

    private void ApplyAspect()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (targetCamera == null)
        {
            return;
        }

        float safeHeight = Mathf.Max(0.0001f, aspectHeight);
        targetCamera.aspect = Mathf.Max(0.0001f, aspectWidth) / safeHeight;
    }
}