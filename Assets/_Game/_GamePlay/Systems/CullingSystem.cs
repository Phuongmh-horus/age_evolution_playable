using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class CullingSystem : MonoBehaviour
{
    public static CullingSystem Instance { get; private set; }

    [SerializeField] private CullingComponent config;

    private CullingState _state;
    private Transform _cachedTarget;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            _state = new CullingState();

            if (config == null)
            {
                config = GetComponent<CullingComponent>();
            }

            if (config == null)
            {
                config = GetComponentInChildren<CullingComponent>();
            }

            if (config != null)
            {
                InitializeGrid();
            }
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (config == null || _state == null || _state.cells.Count == 0) return;

        _state.frameCounter++;
        if (_state.frameCounter % config.FrameInterval == 0)
        {
            PerformCullingCheck();
        }
    }

    /// <summary>
    /// Set up and pre-calculate all grid cells based on configuration.
    /// </summary>
    public void InitializeGrid()
    {
        if (config == null || _state == null) return;

        _state.cells.Clear();
        Vector3 origin = config.GridOrigin;
        int countX = config.CellCountX;
        int countZ = config.CellCountZ;
        float sizeX = config.CellSizeX;
        float sizeZ = config.CellSizeZ;

        for (int i = 0; i < countX; i++)
        {
            for (int j = 0; j < countZ; j++)
            {
                CullingCell cell = new CullingCell
                {
                    XIndex = i,
                    ZIndex = j,
                    MinX = origin.x + i * sizeX,
                    MaxX = origin.x + (i + 1) * sizeX,
                    MinZ = origin.z + j * sizeZ,
                    MaxZ = origin.z + (j + 1) * sizeZ
                };

                cell.Center = new Vector3((cell.MinX + cell.MaxX) / 2f, origin.y, (cell.MinZ + cell.MaxZ) / 2f);
                cell.Size = new Vector3(sizeX, 0f, sizeZ);

                _state.cells.Add(cell);
            }
        }
    }

    /// <summary>
    /// Registers a CullingObject into the grid partition system.
    /// </summary>
    public void Register(CullingObject obj)
    {
        if (_state == null || obj == null) return;

        // If cells are not initialized yet, try to initialize
        if (_state.cells.Count == 0 && config != null)
        {
            InitializeGrid();
        }

        if (_state.cells.Count == 0) return;

        CullingCell cell = GetCellFromPosition(obj.transform.position);
        if (cell != null)
        {
            obj.CurrentCell = cell;
            if (!cell.Objects.Contains(obj))
            {
                cell.Objects.Add(obj);
            }
        }
        else
        {
            obj.CurrentCell = null;
            if (!_state.outOfGridObjects.Contains(obj))
            {
                _state.outOfGridObjects.Add(obj);
            }
        }

        if (obj.IsDynamic && !_state.dynamicObjects.Contains(obj))
        {
            _state.dynamicObjects.Add(obj);
        }

        // Perform an initial distance check so the object starts with the correct culled state
        float cullDistance = obj.CustomCullDistance > 0f ? obj.CustomCullDistance : config.DefaultCullDistance;
        Vector3 targetPos = GetTargetPosition();
        Vector3 diff = obj.transform.position - targetPos;
        float distSqr = diff.x * diff.x + diff.z * diff.z;
        obj.SetCulled(distSqr > cullDistance * cullDistance);
    }

    /// <summary>
    /// Unregisters a CullingObject from the grid partition system.
    /// </summary>
    public void Unregister(CullingObject obj)
    {
        if (_state == null || obj == null) return;

        if (obj.CurrentCell != null)
        {
            obj.CurrentCell.Objects.Remove(obj);
            obj.CurrentCell = null;
        }

        _state.outOfGridObjects.Remove(obj);

        if (obj.IsDynamic)
        {
            _state.dynamicObjects.Remove(obj);
        }
    }

    public void AddDynamicObject(CullingObject obj)
    {
        if (_state != null && obj != null && !_state.dynamicObjects.Contains(obj))
        {
            _state.dynamicObjects.Add(obj);
        }
    }

    public void RemoveDynamicObject(CullingObject obj)
    {
        if (_state != null && obj != null)
        {
            _state.dynamicObjects.Remove(obj);
        }
    }

    /// <summary>
    /// Map a 3D coordinate to the nearest grid cell (clamped to edge boundaries).
    /// Returns null if position lies completely outside the grid layout.
    /// </summary>
    public CullingCell GetCellFromPosition(Vector3 position)
    {
        if (config == null || _state == null || _state.cells.Count == 0) return null;

        Vector3 origin = config.GridOrigin;
        float totalWidth = config.CellCountX * config.CellSizeX;
        float totalHeight = config.CellCountZ * config.CellSizeZ;

        // Check if coordinate is outside grid boundaries
        if (position.x < origin.x || position.x > origin.x + totalWidth ||
            position.z < origin.z || position.z > origin.z + totalHeight)
        {
            return null;
        }

        int i = Mathf.FloorToInt((position.x - origin.x) / config.CellSizeX);
        int j = Mathf.FloorToInt((position.z - origin.z) / config.CellSizeZ);

        i = Mathf.Clamp(i, 0, config.CellCountX - 1);
        j = Mathf.Clamp(j, 0, config.CellCountZ - 1);

        int index = i * config.CellCountZ + j;
        if (index >= 0 && index < _state.cells.Count)
        {
            return _state.cells[index];
        }

        return null;
    }

    private void PerformCullingCheck()
    {
        Vector3 targetPos = GetTargetPosition();

        // 1. Update dynamic object cells
        for (int i = 0; i < _state.dynamicObjects.Count; i++)
        {
            CullingObject obj = _state.dynamicObjects[i];
            if (obj == null) continue;

            CullingCell newCell = GetCellFromPosition(obj.transform.position);
            if (newCell != obj.CurrentCell)
            {
                if (obj.CurrentCell != null)
                {
                    obj.CurrentCell.Objects.Remove(obj);
                }
                else
                {
                    _state.outOfGridObjects.Remove(obj);
                }

                obj.CurrentCell = newCell;

                if (newCell != null)
                {
                    newCell.Objects.Add(obj);
                }
                else
                {
                    if (!_state.outOfGridObjects.Contains(obj))
                    {
                        _state.outOfGridObjects.Add(obj);
                    }
                }
            }
        }

        // 2. Iterate cells and execute spatial distance checks
        float defaultLimit = config.DefaultCullDistance;
        float defaultLimitSqr = defaultLimit * defaultLimit;

        for (int c = 0; c < _state.cells.Count; c++)
        {
            CullingCell cell = _state.cells[c];
            if (cell.Objects.Count == 0) continue;

            // Calculate the maximum possible cull distance limit for this cell
            float maxLimit = defaultLimit;
            for (int o = 0; o < cell.Objects.Count; o++)
            {
                CullingObject obj = cell.Objects[o];
                if (obj != null && obj.CustomCullDistance > maxLimit)
                {
                    maxLimit = obj.CustomCullDistance;
                }
            }

            float maxLimitSqr = maxLimit * maxLimit;
            float cellSqrDist = cell.GetSqrDistanceToPoint(targetPos.x, targetPos.z);

            if (cellSqrDist > maxLimitSqr)
            {
                // Cell is completely outside the culling boundary, cull all objects inside directly
                for (int o = 0; o < cell.Objects.Count; o++)
                {
                    CullingObject obj = cell.Objects[o];
                    if (obj != null)
                    {
                        obj.SetCulled(true);
                    }
                }
            }
            else
            {
                // Cell is within range, check each individual object's distance
                for (int o = 0; o < cell.Objects.Count; o++)
                {
                    CullingObject obj = cell.Objects[o];
                    if (obj == null) continue;

                    float limit = obj.CustomCullDistance > 0f ? obj.CustomCullDistance : defaultLimit;
                    Vector3 diff = obj.transform.position - targetPos;
                    float distSqr = diff.x * diff.x + diff.z * diff.z;

                    obj.SetCulled(distSqr > limit * limit);
                }
            }
        }

        // 3. Check culling for out of grid objects
        for (int i = 0; i < _state.outOfGridObjects.Count; i++)
        {
            CullingObject obj = _state.outOfGridObjects[i];
            if (obj == null) continue;

            float limit = obj.CustomCullDistance > 0f ? obj.CustomCullDistance : defaultLimit;
            Vector3 diff = obj.transform.position - targetPos;
            float distSqr = diff.x * diff.x + diff.z * diff.z;

            obj.SetCulled(distSqr > limit * limit);
        }
    }

    private Vector3 GetTargetPosition()
    {
        if (_cachedTarget != null && _cachedTarget.gameObject.activeInHierarchy)
        {
            return _cachedTarget.position;
        }

        if (config.TargetTransform != null && config.TargetTransform.gameObject.activeInHierarchy)
        {
            _cachedTarget = config.TargetTransform;
            return _cachedTarget.position;
        }

        // Try dynamically looking for a Player component as fallback
        // var player = FindObjectOfType<PlayerCharacterLunaMovementController>();
        // if (player != null)
        // {
        //     _cachedTarget = player.transform;
        //     return _cachedTarget.position;
        // }

        return Vector3.zero;
    }

#if UNITY_EDITOR
    [ContextMenu("Try Refresh Culling in Editor")]
    public void EditorTryRefresh()
    {
        if (config == null)
        {
            config = GetComponent<CullingComponent>();
        }
        if (config == null)
        {
            config = GetComponentInChildren<CullingComponent>();
        }
        if (config == null)
        {
            Debug.LogError("[CullingSystem] CullingComponent reference is missing! Cannot run refresh.");
            return;
        }

        CullingObject[] allObjects = FindObjectsOfType<CullingObject>(true);

        if (allObjects == null || allObjects.Length == 0)
        {
            Debug.LogWarning("[CullingSystem] No CullingObject instances found in the scene.");
            return;
        }

        Vector3 targetPos = Vector3.zero;
        if (config.TargetTransform != null)
        {
            targetPos = config.TargetTransform.position;
        }
        else
        {
            // var player = FindObjectOfType<PlayerCharacterLunaMovementController>();
            // if (player != null)
            // {
            //     targetPos = player.transform.position;
            // }
            // else
            {
                var mainCam = Camera.main;
                if (mainCam != null)
                {
                    targetPos = mainCam.transform.position;
                }
                else if (UnityEditor.SceneView.lastActiveSceneView != null && UnityEditor.SceneView.lastActiveSceneView.camera != null)
                {
                    targetPos = UnityEditor.SceneView.lastActiveSceneView.camera.transform.position;
                }
            }
        }

        float defaultLimit = config.DefaultCullDistance;

        foreach (var obj in allObjects)
        {
            if (obj == null) continue;

            float limit = obj.CustomCullDistance > 0f ? obj.CustomCullDistance : defaultLimit;
            Vector3 diff = obj.transform.position - targetPos;
            float distSqr = diff.x * diff.x + diff.z * diff.z;

            obj.ForceSetCulled(distSqr > limit * limit);
            UnityEditor.EditorUtility.SetDirty(obj.gameObject);
        }

        Debug.Log($"[CullingSystem] Editor Try Refresh completed. Evaluated {allObjects.Length} objects directly around target position {targetPos}.");
    }
#endif
}
