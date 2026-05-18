using System.Collections.Generic;
using GamePlay.Items;
using UnityEngine;

/// <summary>
/// Generates a shuffled set of Vector3 locations within a configurable range.
/// Use the custom Inspector (ShuffleLocationSystemEditor) to control all settings.
/// </summary>
public class ShuffleLocationSystem : MonoBehaviour
{
    // ── General ─────────────────────────────────────────────────────────────
    [Header("General")]
    public bool enableShuffle = true;
    public int count = 10;
    public Vector3 rangeSize = new Vector3(10f, 0f, 10f);

    // ── Axis Lock ────────────────────────────────────────────────────────────
    [Header("Axis Lock")]
    public bool lockX = false;
    public bool lockY = true;
    public bool lockZ = false;

    // ── Row Alignment ────────────────────────────────────────────────────────
    [Header("Row Alignment")]
    public bool enableRowAlignment = false;

    [System.Flags]
    public enum RowAxis
    {
        None = 0,
        X    = 1 << 0,
        Y    = 1 << 1,
        Z    = 1 << 2
    }

    public RowAxis rowAxes = RowAxis.X;
    public Vector3 rowChaos = Vector3.zero;
    public Vector3 minAxisSpacing = Vector3.zero;

    // ── Minimum Distance ─────────────────────────────────────────────────────
    [Header("Minimum Distance")]
    public bool enableMinDistance = false;
    public float minDistance = 1f;

    // ── Prefab Random ────────────────────────────────────────────────────────
    [Header("Prefab Random")]
    public bool enablePrefabRandom = false;
    public List<GameObject> prefabList = new List<GameObject>();

    // ── Content Override ─────────────────────────────────────────────────────
    [Header("Content Override")]
    public ContentDataSO contentData;
    public Vector2 overrideRange = new Vector2(-1f, -1f);

    // ── Internal State ───────────────────────────────────────────────────────
    [HideInInspector] public List<Vector3> generatedLocations = new List<Vector3>();
    [HideInInspector][SerializeField] private List<GameObject> _spawnedObjects = new List<GameObject>();

    // ═══════════════════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════════════════

    public void Generate()
    {
        generatedLocations.Clear();
        if (count <= 0) return;

        if (enableRowAlignment)
            GenerateWithRowAlignment();
        else
            GenerateRandom();
    }

    public void SpawnPrefabs()
    {
        ClearSpawned();
        Generate();

        foreach (var loc in generatedLocations)
        {
            int idx = Random.Range(0, prefabList.Count);
            GameObject prefab = prefabList[idx];
            if (prefab == null) continue;

#if UNITY_EDITOR
            var spawned = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
            spawned.transform.position = loc;
            _spawnedObjects.Add(spawned);
#else
            _spawnedObjects.Add(Instantiate(prefab, loc, Quaternion.identity, transform));
#endif
        }
    }

    public void ClearSpawned()
    {
        foreach (var obj in _spawnedObjects)
        {
            if (obj == null) continue;
#if UNITY_EDITOR
            DestroyImmediate(obj);
#else
            Destroy(obj);
#endif
        }
        _spawnedObjects.Clear();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Content override
    // ═══════════════════════════════════════════════════════════════════════

    public bool CanOverrideContent()
    {
        return contentData != null
            && overrideRange.x >= 0f
            && generatedLocations.Count > 0
            && prefabList != null
            && prefabList.Count > 0;
    }

    public void OverrideContentRange()
    {
        if (!CanOverrideContent()) return;

        List<SpawnableObject> nextObjects = CreateGeneratedSpawnableObjects();
        if (nextObjects.Count == 0) return;

        List<SpawnableObject> targetObjects = contentData.SpawnableObjects;
        if (targetObjects == null)
        {
            targetObjects = new List<SpawnableObject>();
            contentData.SpawnableObjects = targetObjects;
        }

        int startIndex = Mathf.FloorToInt(overrideRange.x);
        if (startIndex > targetObjects.Count)
            startIndex = targetObjects.Count;

        int replaceCount = GetReplaceCount(startIndex, targetObjects.Count);
        targetObjects.RemoveRange(startIndex, replaceCount);
        targetObjects.InsertRange(startIndex, nextObjects);
    }

    private List<SpawnableObject> CreateGeneratedSpawnableObjects()
    {
        var spawnableObjects = new List<SpawnableObject>();

        for (int i = 0; i < generatedLocations.Count; i++)
        {
            ItemUnit prefab = GetItemUnitPrefab(i);
            if (prefab == null) continue;

            Vector3 localPosition = generatedLocations[i];
            Vector3 rotation = Vector3.zero;
            Vector3 scale = Vector3.one;

            // Dùng transform thực tế của spawned objects nếu đã có (virusal đã chạy)
            if (i < _spawnedObjects.Count && _spawnedObjects[i] != null)
            {
                Transform t = _spawnedObjects[i].transform;
                localPosition = t.position;
                rotation = t.localEulerAngles;
                scale = t.localScale;
            }

            spawnableObjects.Add(new SpawnableObject
            {
                Prefab = prefab,
                PositionOnMap = localPosition.z,
                PositionOffset = new Vector3(localPosition.x, localPosition.y, 0f),
                Rotation = rotation,
                Scale = scale
            });
        }

        return spawnableObjects;
    }

    private ItemUnit GetItemUnitPrefab(int index)
    {
        if (prefabList == null || prefabList.Count == 0) return null;

        GameObject prefabObject = prefabList[Mathf.Clamp(index, 0, prefabList.Count - 1)];
        if (enablePrefabRandom)
            prefabObject = prefabList[Random.Range(0, prefabList.Count)];

        return prefabObject != null ? prefabObject.GetComponent<ItemUnit>() : null;
    }

    private int GetReplaceCount(int startIndex, int objectCount)
    {
        int endIndex = Mathf.FloorToInt(overrideRange.y);
        if (endIndex <= startIndex || endIndex == -1)
            return startIndex < objectCount ? 1 : 0;

        return Mathf.Clamp(endIndex - startIndex + 1, 0, objectCount - startIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Generation helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void GenerateRandom()
    {
        int maxAttempts = count * 300;
        int attempts = 0;

        while (generatedLocations.Count < count && attempts < maxAttempts)
        {
            attempts++;
            Vector3 pos = SamplePosition();
            if (enableMinDistance && IsTooClose(pos)) continue;
            generatedLocations.Add(pos);
        }
    }

    private void GenerateWithRowAlignment()
    {
        bool alignX = (rowAxes & RowAxis.X) != 0;
        bool alignY = (rowAxes & RowAxis.Y) != 0;
        bool alignZ = (rowAxes & RowAxis.Z) != 0;
        int axisCount = (alignX ? 1 : 0) + (alignY ? 1 : 0) + (alignZ ? 1 : 0);

        if (axisCount == 0) { GenerateRandom(); return; }

        int slotsX = alignX ? count : 1;
        int slotsY = alignY ? count : 1;
        int slotsZ = alignZ ? count : 1;

        if (axisCount >= 2)
            ReshapeGrid(ref slotsX, ref slotsY, ref slotsZ);

        Vector3 half = rangeSize * 0.5f;

        for (int i = 0; i < count; i++)
        {
            int ix = i % slotsX;
            int iy = (i / slotsX) % slotsY;
            int iz = i / (slotsX * slotsY);

            generatedLocations.Add(transform.position + new Vector3(
                GetComponent(alignX, lockX, ix, slotsX, rangeSize.x, rowChaos.x, half.x),
                GetComponent(alignY, lockY, iy, slotsY, rangeSize.y, rowChaos.y, half.y),
                GetComponent(alignZ, lockZ, iz, slotsZ, rangeSize.z, rowChaos.z, half.z)
            ));
        }
    }

    private static float GetComponent(bool aligned, bool locked, int idx, int slots,
        float range, float chaos, float half)
    {
        if (locked) return 0f;
        if (!aligned) return Random.Range(-half, half);

        float t = slots > 1 ? ((float)idx + 0.5f) / slots : 0.5f;
        float ideal = Mathf.Lerp(-half, half, t);
        float cellSize = range / slots;
        float randomized = Random.Range(ideal - cellSize * 0.5f, ideal + cellSize * 0.5f);
        return Mathf.Lerp(ideal, randomized, Mathf.Clamp01(chaos));
    }

    private void ReshapeGrid(ref int slotsX, ref int slotsY, ref int slotsZ)
    {
        bool actX = slotsX > 1, actY = slotsY > 1, actZ = slotsZ > 1;

        if (actX && actY && actZ)
        {
            int cbrt = Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(count, 1f / 3f)));
            int a = cbrt, b = cbrt, c_val = cbrt;
            int maxX = Mathf.Max(1, Mathf.FloorToInt(rangeSize.x / Mathf.Max(minAxisSpacing.x, 0.001f)));
            int maxY = Mathf.Max(1, Mathf.FloorToInt(rangeSize.y / Mathf.Max(minAxisSpacing.y, 0.001f)));
            int maxZ = Mathf.Max(1, Mathf.FloorToInt(rangeSize.z / Mathf.Max(minAxisSpacing.z, 0.001f)));
            a = Mathf.Min(a, maxX);
            b = Mathf.Min(b, maxY);
            c_val = Mathf.Min(c_val, maxZ);
            while (a * b * c_val < count)
            {
                if ((float)a / rangeSize.x <= (float)b / rangeSize.y && (float)a / rangeSize.x <= (float)c_val / rangeSize.z) a = Mathf.Min(a + 1, maxX);
                else if ((float)b / rangeSize.y <= (float)c_val / rangeSize.z) b = Mathf.Min(b + 1, maxY);
                else c_val = Mathf.Min(c_val + 1, maxZ);
            }
            slotsX = a; slotsY = b; slotsZ = c_val;
            return;
        }

        if (actX && actY)      ReshapePair(ref slotsX, ref slotsY, rangeSize.x, rangeSize.y, minAxisSpacing.x, minAxisSpacing.y);
        else if (actX && actZ) ReshapePair(ref slotsX, ref slotsZ, rangeSize.x, rangeSize.z, minAxisSpacing.x, minAxisSpacing.z);
        else if (actY && actZ) ReshapePair(ref slotsY, ref slotsZ, rangeSize.y, rangeSize.z, minAxisSpacing.y, minAxisSpacing.z);
    }

    private void ReshapePair(ref int slotsA, ref int slotsB, float rangeA, float rangeB, float spacingA, float spacingB)
    {
        float ratio = rangeA / Mathf.Max(rangeB, 0.001f);

        int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(count * ratio)));
        int rows = Mathf.Max(1, Mathf.CeilToInt((float)count / cols));

        int maxCols = Mathf.Max(1, Mathf.FloorToInt(rangeA / Mathf.Max(spacingA, 0.001f)));
        int maxRows = Mathf.Max(1, Mathf.FloorToInt(rangeB / Mathf.Max(spacingB, 0.001f)));
        cols = Mathf.Min(cols, maxCols);
        rows = Mathf.Min(rows, maxRows);
        if (cols * rows < count)
        {
            cols = Mathf.Min(maxCols, Mathf.CeilToInt((float)count / rows));
            if (cols * rows < count)
                rows = Mathf.Min(maxRows, Mathf.CeilToInt((float)count / cols));
        }

        slotsA = cols;
        slotsB = rows;
    }

    private Vector3 SamplePosition()
    {
        Vector3 half = rangeSize * 0.5f;
        float x = lockX ? 0f : Random.Range(-half.x, half.x);
        float y = lockY ? 0f : Random.Range(-half.y, half.y);
        float z = lockZ ? 0f : Random.Range(-half.z, half.z);
        return transform.position + new Vector3(x, y, z);
    }

    private bool IsTooClose(Vector3 candidate)
    {
        foreach (var loc in generatedLocations)
            if (Vector3.Distance(candidate, loc) < minDistance) return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Gizmos
    // ═══════════════════════════════════════════════════════════════════════

    private void OnDrawGizmos()
    {
        if (!enableShuffle) return;
        DrawRange();
        DrawLocations();
    }

    private void DrawRange()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 0.3f, 0.12f);
        Gizmos.DrawCube(transform.position, rangeSize);
        Gizmos.color = new Color(0.3f, 0.9f, 0.3f, 1f);
        Gizmos.DrawWireCube(transform.position, rangeSize);
    }

    private void DrawLocations()
    {
        const float radius = 0.15f;

        for (int i = 0; i < generatedLocations.Count; i++)
        {
            Vector3 loc = generatedLocations[i];

            // Point sphere
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(loc, radius);

            // Min-distance ring
            if (enableMinDistance)
            {
                Gizmos.color = new Color(1f, 0.4f, 0f, 0.25f);
                Gizmos.DrawWireSphere(loc, minDistance * 0.5f);
            }

            // Row alignment connector line
            if (enableRowAlignment && i > 0)
            {
                Gizmos.color = new Color(0.5f, 0.75f, 1f, 0.6f);
                Gizmos.DrawLine(generatedLocations[i - 1], loc);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.Label(loc + Vector3.up * (radius * 2f), i.ToString());
#endif
        }
    }
}
