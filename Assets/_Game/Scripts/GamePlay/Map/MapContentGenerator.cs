using System.Collections;
using System.Collections.Generic;
using GamePlay.Entities;
using GamePlay.Items;
using GamePlay.Roads;
using Pools;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GamePlay.Map
{
    /// <summary>
    /// Runtime content spawner for a generated map.
    /// [FIX] All PoolManager.Get() calls are now wrapped with try-catch fallback to Instantiate()
    public class MapContentGenerator : MonoBehaviour
    {
        public Vector3 Position => transform.position;

        [Header("Data")]
        [SerializeField] private ContentDataSO contentData;
        [SerializeField] private ContentDataSO contentTowerZoneData;
        [SerializeField] private MapGenerator mapGenerator;
        [Header("Startup Performance")]
        [SerializeField, Min(1)] private int spawnItemsPerFrame = 20;

        public readonly List<ItemUnit> generatedObjects = new List<ItemUnit>();
        public readonly HashSet<float> MilestonePoints = new HashSet<float>();

        public Transform GateNewEraTrans { get; private set; }

        [Header("Random Content Generation (Optional)")]
        [SerializeField] private List<GameObject> spawnablePrefabs;
        [SerializeField] private float laneWidth = 4f;
        [SerializeField] private float spawnChance = 0.3f;
        [SerializeField] private float minDistanceBetweenObjects = 5f;

        private readonly Dictionary<GameObject, GameObject> instanceToPrefabMap = new Dictionary<GameObject, GameObject>();

        private static ItemUnit SafeSpawnItemUnit(ItemUnit prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null) return null;

            // Try Pools.Spawn extension first (if it exists on the type)
            try
            {
                var spawned = prefab.Spawn(pos, rot, parent);
                if (spawned != null) return spawned;
            }
            catch { }

            // Direct Instantiate fallback — always works in Luna
            return Object.Instantiate(prefab, pos, rot, parent);
        }

        private static MilestoneOnMap SafeSpawnMilestone(MilestoneOnMap prefab)
        {
            if (prefab == null) return null;
            try
            {
                var spawned = prefab.Spawn();
                if (spawned != null) return spawned;
            }
            catch { }
            return Object.Instantiate(prefab);
        }

        public void GenerateContentData(ContentDataSO contentDataSo, bool initializeItems = true)
        {
            contentData = contentDataSo;
            contentTowerZoneData = null;
            SpawnObjectsFromContent(destroyImmediate: true, initializeItems: initializeItems);
        }

        public void GenerateContentData(ContentDataSO contentDataSo, ContentDataSO towerZoneDataSo, bool initializeItems = true)
        {
            contentData = contentDataSo;
            contentTowerZoneData = towerZoneDataSo;
            SpawnObjectsFromContent(destroyImmediate: true, initializeItems: initializeItems);
        }

        public IEnumerator GenerateContentDataAsync(ContentDataSO contentDataSo, bool initializeItems = true, int customBatchSize = -1)
        {
            contentData = contentDataSo;
            contentTowerZoneData = null;
            int batchSize = customBatchSize > 0 ? customBatchSize : spawnItemsPerFrame;
            yield return CoSpawnObjectsFromContentBatched(destroyImmediate: true, initializeItems: initializeItems, batchSize: batchSize);
        }

        public IEnumerator GenerateContentDataAsync(
            ContentDataSO contentDataSo,
            ContentDataSO towerZoneDataSo,
            bool initializeItems = true,
            int customBatchSize = -1)
        {
            contentData = contentDataSo;
            contentTowerZoneData = towerZoneDataSo;
            int batchSize = customBatchSize > 0 ? customBatchSize : spawnItemsPerFrame;
            yield return CoSpawnObjectsFromContentBatched(destroyImmediate: true, initializeItems: initializeItems, batchSize: batchSize);
        }

        public void ClearContent()
        {
            ClearGeneratedContent(destroyImmediate: true);
        }

        public bool HasPrebakedContent()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.GetComponent<RoadSegment>() != null) continue;
                if (child.GetComponent<ItemUnit>() != null) return true;
            }

            return false;
        }

        public void UsePrebakedContent(bool initializeItems)
        {
            MilestonePoints.Clear();
            generatedObjects.Clear();
            instanceToPrefabMap.Clear();
            GateNewEraTrans = null;

            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.GetComponent<RoadSegment>() != null) continue;

                var item = child.GetComponent<ItemUnit>();
                if (item == null) continue;

                if (initializeItems && Application.isPlaying)
                    item.Initialize();

                generatedObjects.Add(item);

                if (item.EntityType == EntityType.GateNewEra)
                    GateNewEraTrans = item.transform;

                if (item.EntityType == EntityType.FinishTower)
                {
                    float positionOnMap = item.transform.position.z - Position.z;
                    MilestonePoints.Add(positionOnMap);
                }
            }
        }

        public void SetPositionOnMap(Transform trans, float positionOnMap)
        {
            if (trans == null) return;
            Vector3 spawnPosition = Position + Vector3.forward * positionOnMap;
            trans.position = spawnPosition;
            trans.rotation = Quaternion.identity;
        }

        // [FIX] Use SafeSpawnMilestone to avoid pool registry exceptions
        public MilestoneOnMap SpawnMilestoneItem(MilestoneOnMap milestonePrefab)
        {
            if (milestonePrefab == null) return null;

            float positionOnMap = 0f;
            if (MilestonePoints.Count > 0)
            {
                bool hasValue = false;
                foreach (float value in MilestonePoints)
                {
                    if (!hasValue || value < positionOnMap)
                    {
                        positionOnMap = value;
                        hasValue = true;
                    }
                }
            }

            MilestoneOnMap result = SafeSpawnMilestone(milestonePrefab);

            result.transform.SetParent(transform);
            SetPositionOnMap(result.transform, positionOnMap);
            return result;
        }

        #region Spawn from ContentDataSO

        [ContextMenu("Spawn Objects From Content Data")]
        private void SpawnObjectsFromContentData()
        {
            SpawnObjectsFromContent(destroyImmediate: true, initializeItems: true);
        }

        private void SpawnObjectsFromContent(bool destroyImmediate, bool initializeItems)
        {
            if (contentData == null && contentTowerZoneData == null)
            {
                Debug.LogWarning("[MapContentGenerator] ContentData and TowerZoneData are not set.");
                return;
            }

            ClearGeneratedContent(destroyImmediate);
            SpawnFromContentData(contentData, initializeItems);
            SpawnFromContentData(contentTowerZoneData, initializeItems);
        }

        private void SpawnFromContentData(ContentDataSO sourceData, bool initializeItems)
        {
            if (sourceData == null || sourceData.SpawnableObjects == null || sourceData.SpawnableObjects.Count == 0)
                return;

            for (int i = 0; i < sourceData.SpawnableObjects.Count; i++)
            {
                var spawnable = sourceData.SpawnableObjects[i];
                if (spawnable == null || spawnable.Prefab == null) continue;

                if (spawnable.Prefab.EntityType == EntityType.FinishTower)
                    MilestonePoints.Add(spawnable.PositionOnMap);

                Vector3 spawnPosition = Position + Vector3.forward * spawnable.PositionOnMap + spawnable.PositionOffset;
                Quaternion spawnRotation = Quaternion.Euler(spawnable.Rotation);

                // [FIX] SafeSpawnItemUnit avoids Luna pool registry exceptions
                ItemUnit itemUnit = SafeSpawnItemUnit(spawnable.Prefab, spawnPosition, spawnRotation, transform);

                if (itemUnit == null)
                {
                    Debug.LogError($"[MapContentGenerator] Failed to spawn object at index {i} from data '{sourceData.name}'.");
                    continue;
                }

                itemUnit.transform.localScale = spawnable.Scale;
                spawnable.ApplyPropertyOverrides(itemUnit);

                if (initializeItems && Application.isPlaying)
                    itemUnit.Initialize();

                generatedObjects.Add(itemUnit);
                instanceToPrefabMap[itemUnit.gameObject] = spawnable.Prefab.gameObject;

                if (itemUnit.EntityType == EntityType.GateNewEra)
                    GateNewEraTrans = itemUnit.transform;
            }
        }

        private IEnumerator CoSpawnObjectsFromContentBatched(bool destroyImmediate, bool initializeItems, int batchSize)
        {
            if (contentData == null && contentTowerZoneData == null)
            {
                Debug.LogWarning("[MapContentGenerator] ContentData and TowerZoneData are not set.");
                yield break;
            }

            ClearGeneratedContent(destroyImmediate);

            int safeBatchSize = Mathf.Max(1, batchSize);
            int spawnedThisBatch = 0;
            int totalSpawnables = 0;
            if (contentData != null && contentData.SpawnableObjects != null)
                totalSpawnables += contentData.SpawnableObjects.Count;
            if (contentTowerZoneData != null && contentTowerZoneData.SpawnableObjects != null)
                totalSpawnables += contentTowerZoneData.SpawnableObjects.Count;
            if (generatedObjects.Capacity < totalSpawnables)
            {
                generatedObjects.Capacity = totalSpawnables;
            }

            IEnumerable<SpawnableObject> Enumerate(ContentDataSO src)
            {
                if (src == null || src.SpawnableObjects == null) yield break;
                for (int i = 0; i < src.SpawnableObjects.Count; i++)
                    yield return src.SpawnableObjects[i];
            }

            foreach (var spawnable in Enumerate(contentData))
            {
                if (spawnable == null || spawnable.Prefab == null) continue;

                if (spawnable.Prefab.EntityType == EntityType.FinishTower)
                    MilestonePoints.Add(spawnable.PositionOnMap);

                Vector3 spawnPosition = Position + Vector3.forward * spawnable.PositionOnMap + spawnable.PositionOffset;
                Quaternion spawnRotation = Quaternion.Euler(spawnable.Rotation);

                // [FIX] SafeSpawnItemUnit
                ItemUnit itemUnit = SafeSpawnItemUnit(spawnable.Prefab, spawnPosition, spawnRotation, transform);

                if (itemUnit == null)
                {
                    Debug.LogError("[MapContentGenerator] Failed to spawn object.");
                    continue;
                }

                itemUnit.transform.localScale = spawnable.Scale;
                spawnable.ApplyPropertyOverrides(itemUnit);

                if (initializeItems && Application.isPlaying)
                    itemUnit.Initialize();

                generatedObjects.Add(itemUnit);
                instanceToPrefabMap[itemUnit.gameObject] = spawnable.Prefab.gameObject;

                if (itemUnit.EntityType == EntityType.GateNewEra)
                    GateNewEraTrans = itemUnit.transform;

                spawnedThisBatch++;
                if (spawnedThisBatch >= safeBatchSize)
                {
                    spawnedThisBatch = 0;
                    yield return null;
                }
            }

            foreach (var spawnable in Enumerate(contentTowerZoneData))
            {
                if (spawnable == null || spawnable.Prefab == null) continue;

                if (spawnable.Prefab.EntityType == EntityType.FinishTower)
                    MilestonePoints.Add(spawnable.PositionOnMap);

                Vector3 spawnPosition = Position + Vector3.forward * spawnable.PositionOnMap + spawnable.PositionOffset;
                Quaternion spawnRotation = Quaternion.Euler(spawnable.Rotation);

                // [FIX] SafeSpawnItemUnit
                ItemUnit itemUnit = SafeSpawnItemUnit(spawnable.Prefab, spawnPosition, spawnRotation, transform);

                if (itemUnit == null)
                {
                    Debug.LogError("[MapContentGenerator] Failed to spawn object.");
                    continue;
                }

                itemUnit.transform.localScale = spawnable.Scale;
                spawnable.ApplyPropertyOverrides(itemUnit);

                if (initializeItems && Application.isPlaying)
                    itemUnit.Initialize();

                generatedObjects.Add(itemUnit);
                instanceToPrefabMap[itemUnit.gameObject] = spawnable.Prefab.gameObject;

                if (itemUnit.EntityType == EntityType.GateNewEra)
                    GateNewEraTrans = itemUnit.transform;

                spawnedThisBatch++;
                if (spawnedThisBatch >= safeBatchSize)
                {
                    spawnedThisBatch = 0;
                    yield return null;
                }
            }
        }

        private void ClearGeneratedContent(bool destroyImmediate)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && destroyImmediate)
            {
                var toDelete = new List<GameObject>();
                foreach (Transform child in transform)
                {
                    if (child == null) continue;
                    if (child.GetComponent<RoadSegment>() != null) continue;
                    if (child.GetComponent<ItemUnit>() != null)
                        toDelete.Add(child.gameObject);
                }

                foreach (var obj in toDelete)
                {
                    if (obj == null) continue;
                    Undo.DestroyObjectImmediate(obj);
                }
            }
            else
#endif
            {
                foreach (var item in generatedObjects)
                {
                    if (item != null)
                        Destroy(item.gameObject);
                }
            }

            MilestonePoints.Clear();
            generatedObjects.Clear();
            instanceToPrefabMap.Clear();
            GateNewEraTrans = null;
        }

        #endregion

        #region Optional random generation

        [Header("Grid Spawn Settings (Optional)")]
        [SerializeField] private GameObject gridPrefab;
        [SerializeField] private float gridSpacingX = 2f;
        [SerializeField] private float gridSpacingY = 5f;
        [SerializeField] private int gridRows = 5;

        [ContextMenu("Generate Random Content")]
        private void GenerateRandomContent()
        {
            if (contentData == null)
            {
                Debug.LogWarning("[MapContentGenerator] ContentData is not set.");
                return;
            }

            if (spawnablePrefabs == null || spawnablePrefabs.Count == 0)
            {
                if (contentData.SpawnableObjects != null)
                {
                    spawnablePrefabs = new List<GameObject>();
                    for (int i = 0; i < contentData.SpawnableObjects.Count; i++)
                    {
                        var entry = contentData.SpawnableObjects[i];
                        if (entry == null || entry.Prefab == null) continue;
                        GameObject prefabGo = entry.Prefab.gameObject;
                        if (prefabGo == null) continue;
                        if (!spawnablePrefabs.Contains(prefabGo))
                            spawnablePrefabs.Add(prefabGo);
                    }
                }
            }

            if (spawnablePrefabs == null || spawnablePrefabs.Count == 0)
            {
                Debug.LogWarning("[MapContentGenerator] No spawnable prefabs available.");
                return;
            }

            if (mapGenerator == null)
            {
                Debug.LogWarning("[MapContentGenerator] MapGenerator not assigned.");
                return;
            }

            var activeSegments = mapGenerator.GetActiveSegments();
            if (activeSegments == null || activeSegments.Count == 0)
            {
                Debug.LogWarning("[MapContentGenerator] No active road segments. Generate map first.");
                return;
            }

            ClearGeneratedContent(destroyImmediate: true);

            int totalSegments = activeSegments.Count;

            for (int segmentIndex = 0; segmentIndex < totalSegments; segmentIndex++)
            {
                var segment = activeSegments[segmentIndex];
                if (segment == null) continue;

                if (segmentIndex < 1) continue;
                if (segmentIndex >= totalSegments - 1) continue;

                bool isSecondToLast = segmentIndex == totalSegments - 2;

                Vector3 roadDir = (segment.ExitPoint.position - segment.EntryPoint.position).normalized;
                if (roadDir == Vector3.zero) roadDir = Vector3.forward;

                Vector3 roadRight = Vector3.Cross(Vector3.up, roadDir).normalized;
                if (roadRight == Vector3.zero) roadRight = Vector3.right;

                if (isSecondToLast && gridPrefab != null)
                {
                    var lastSegment = activeSegments[totalSegments - 1];
                    Vector3 startPosition = lastSegment.ExitPoint.position;

                    var gridPrefabItemUnit = gridPrefab.GetComponent<ItemUnit>();
                    if (gridPrefabItemUnit == null)
                    {
                        Debug.LogWarning("[MapContentGenerator] Grid Prefab has no ItemUnit. Skipping grid spawn.");
                        continue;
                    }

                    for (int row = 0; row < gridRows; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            Vector3 positionOnLine = startPosition - roadDir * (row * gridSpacingY);
                            float xOffset = (col - 1) * gridSpacingX;
                            Vector3 worldPos = positionOnLine + roadRight * xOffset;

                            var itemUnit = Instantiate(gridPrefabItemUnit, worldPos, Quaternion.LookRotation(roadDir), transform);
                            if (Application.isPlaying) itemUnit.Initialize();

                            generatedObjects.Add(itemUnit);
                            instanceToPrefabMap[itemUnit.gameObject] = gridPrefab;
                        }
                    }
                }
                else
                {
                    float segmentLength = segment.Length;
                    int steps = Mathf.FloorToInt(segmentLength / Mathf.Max(0.01f, minDistanceBetweenObjects));

                    for (int i = 0; i < steps; i++)
                    {
                        if (Random.value > spawnChance) continue;

                        float distanceInSegment = i * minDistanceBetweenObjects;
                        float t = segmentLength > 0 ? distanceInSegment / segmentLength : 0;

                        Vector3 positionOnLine = Vector3.Lerp(segment.EntryPoint.position, segment.ExitPoint.position, t);
                        float randomX = Random.Range(-laneWidth / 2f, laneWidth / 2f);
                        Vector3 worldPos = positionOnLine + roadRight * randomX;

                        GameObject prefab = spawnablePrefabs[Random.Range(0, spawnablePrefabs.Count)];
                        var prefabItemUnit = prefab != null ? prefab.GetComponent<ItemUnit>() : null;
                        if (prefabItemUnit == null) continue;

                        var itemUnit = Instantiate(prefabItemUnit, worldPos, Quaternion.LookRotation(roadDir), transform);
                        if (Application.isPlaying) itemUnit.Initialize();

                        generatedObjects.Add(itemUnit);
                        instanceToPrefabMap[itemUnit.gameObject] = prefab;
                    }
                }
            }
        }

        #endregion
    }
}