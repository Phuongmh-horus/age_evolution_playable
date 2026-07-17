using System.Collections.Generic;

public class CullingState
{
    public int frameCounter = 0;
    public List<CullingCell> cells = new List<CullingCell>();
    public List<CullingObject> dynamicObjects = new List<CullingObject>();
    public List<CullingObject> outOfGridObjects = new List<CullingObject>();
}
