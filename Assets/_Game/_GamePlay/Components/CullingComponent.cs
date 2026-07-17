using UnityEngine;

public class CullingComponent : MonoBehaviour
{
    [Header("Grid Partitioning Layout")]
    [Tooltip("The transform anchor that defines the bottom-left corner of the grid.")]
    [SerializeField] private Transform gridOriginTransform;
    [SerializeField] private int cellCountX = 10;
    [SerializeField] private int cellCountZ = 10;
    [SerializeField] private float cellSizeX = 10f;
    [SerializeField] private float cellSizeZ = 10f;

    [Header("Performance Settings")]
    [Tooltip("How many frames to wait between culling checks. 1 = check every frame, 10 = check every 10 frames.")]
    [SerializeField] private int frameInterval = 5;
    [SerializeField] private float defaultCullDistance = 30f;
    [SerializeField] private Transform targetTransform;

    // Public getters for CullingSystem
    public Vector3 GridOrigin => gridOriginTransform != null ? gridOriginTransform.position : Vector3.zero;
    
    public int CellCountX
    {
        get => cellCountX;
        set => cellCountX = value;
    }
    
    public int CellCountZ
    {
        get => cellCountZ;
        set => cellCountZ = value;
    }
    
    public float CellSizeX
    {
        get => cellSizeX;
        set => cellSizeX = value;
    }
    
    public float CellSizeZ
    {
        get => cellSizeZ;
        set => cellSizeZ = value;
    }

    public int FrameInterval
    {
        get => frameInterval;
        set => frameInterval = value;
    }

    public float DefaultCullDistance
    {
        get => defaultCullDistance;
        set => defaultCullDistance = value;
    }

    public Transform TargetTransform
    {
        get => targetTransform;
        set => targetTransform = value;
    }

    public Transform GridOriginTransform
    {
        get => gridOriginTransform;
        set => gridOriginTransform = value;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 origin = GridOrigin;
        int countX = cellCountX;
        int countZ = cellCountZ;
        float sizeX = cellSizeX;
        float sizeZ = cellSizeZ;

        // Draw individual cells
        for (int i = 0; i < countX; i++)
        {
            for (int j = 0; j < countZ; j++)
            {
                float minX = origin.x + i * sizeX;
                float maxX = minX + sizeX;
                float minZ = origin.z + j * sizeZ;
                float maxZ = minZ + sizeZ;

                Vector3 center = new Vector3((minX + maxX) / 2f, origin.y, (minZ + maxZ) / 2f);
                Vector3 size = new Vector3(sizeX, 0.1f, sizeZ);

                Gizmos.DrawWireCube(center, size);
            }
        }

        // Draw a bolder boundary around the entire grid
        Gizmos.color = Color.cyan;
        float totalWidth = countX * sizeX;
        float totalHeight = countZ * sizeZ;
        Vector3 gridCenter = new Vector3(origin.x + totalWidth / 2f, origin.y, origin.z + totalHeight / 2f);
        Vector3 gridOutlineSize = new Vector3(totalWidth, 0.2f, totalHeight);
        Gizmos.DrawWireCube(gridCenter, gridOutlineSize);
    }
}
