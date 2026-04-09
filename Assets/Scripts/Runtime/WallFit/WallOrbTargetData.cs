using System;
using UnityEngine;

[Serializable]
public struct WallOrbTargetData
{
    public Vector2 viewportPosition;
    public float radius;

    public WallOrbTargetData(Vector2 viewportPosition, float radius)
    {
        this.viewportPosition = viewportPosition;
        this.radius = radius;
    }
}
