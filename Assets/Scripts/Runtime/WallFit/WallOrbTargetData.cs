using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct WallOrbTargetData
{
    [FormerlySerializedAs("viewportPosition")]
    public Vector2 position;
    public float radius;

    public WallOrbTargetData(Vector2 position, float radius)
    {
        this.position = position;
        this.radius = radius;
    }
}
