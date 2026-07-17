using System.Collections.Generic;
using UnityEngine;

public class CullingCell
{
    public int XIndex { get; set; }
    public int ZIndex { get; set; }
    
    public float MinX { get; set; }
    public float MaxX { get; set; }
    public float MinZ { get; set; }
    public float MaxZ { get; set; }
    
    public Vector3 Center { get; set; }
    public Vector3 Size { get; set; }
    
    public List<CullingObject> Objects { get; } = new List<CullingObject>();

    public float GetSqrDistanceToPoint(float tx, float tz)
    {
        float dx = Mathf.Max(0f, Mathf.Max(MinX - tx, tx - MaxX));
        float dz = Mathf.Max(0f, Mathf.Max(MinZ - tz, tz - MaxZ));
        return dx * dx + dz * dz;
    }
}
