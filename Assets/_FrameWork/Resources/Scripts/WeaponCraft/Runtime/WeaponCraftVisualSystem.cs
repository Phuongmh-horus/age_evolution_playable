using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WeaponCraft
{
    public sealed class WeaponCraftVisualSystem : MonoBehaviour
    {
        public event System.Action<WeaponItem> MergeVisualCompleted;

        private sealed class VisualEntry
        {
            public WeaponItem Item;
            public GameObject Instance;

            public Transform Transform => Instance != null ? Instance.transform : null;
            public RectTransform RectTransform => Instance != null ? Instance.GetComponent<RectTransform>() : null;
        }

        private sealed class PendingAddEntry
        {
            public VisualEntry Entry;
            public int TargetIndex;
        }

        [Header("Layout")]
        [SerializeField] private List<RectTransform> slots = new List<RectTransform>();
        [Header("Speed")]
        [SerializeField, Min(1f)] private float mergeSpeedMultiplier = 1.5f;

        private WeaponCraftConfigSO config;

        [SerializeField]
        private RectTransform itemRoot;
        private readonly List<VisualEntry> entries = new List<VisualEntry>();

        public void Bind(WeaponCraftConfigSO config)
        {
            this.config = config;
            EnsureRoot();
        }

        private void Awake()
        {
            EnsureRoot();
        }

        private void OnDisable()
        {
            ClearEntries();
        }

        public IEnumerator PlayBatch(List<WeaponCraftOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                yield break;
            }

            EnsureRoot();

            var pendingAdds = new List<WeaponCraftOperation>();
            for (int i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation == null)
                {
                    continue;
                }

                if ((int)operation.Type == (int)WeaponCraftOperationType.AddItem)
                {
                    pendingAdds.Add(operation);
                    continue;
                }

                if (pendingAdds.Count > 0)
                {
                    yield return StartCoroutine(PlayAddGroup(pendingAdds));
                    pendingAdds.Clear();
                }

                if ((int)operation.Type == (int)WeaponCraftOperationType.Merge)
                {
                    yield return StartCoroutine(PlayMergeOperation(operation));
                }
            }

            if (pendingAdds.Count > 0)
            {
                yield return StartCoroutine(PlayAddGroup(pendingAdds));
            }
        }

        private IEnumerator PlayAddGroup(List<WeaponCraftOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                yield break;
            }

            var orderedOperations = new List<WeaponCraftOperation>(operations.Count);
            for (int i = 0; i < operations.Count; i++)
            {
                var operation = operations[i];
                if (operation == null || operation.Item == null)
                {
                    continue;
                }

                orderedOperations.Add(operation);
            }

            if (orderedOperations.Count == 0)
            {
                yield break;
            }

            orderedOperations.Sort((left, right) => left.TargetIndex.CompareTo(right.TargetIndex));

            var pendingAdds = new List<PendingAddEntry>(orderedOperations.Count);
            var movingEntries = new List<VisualEntry>(orderedOperations.Count);
            var startPositions = new Vector3[orderedOperations.Count];
            var targetPositions = new Vector3[orderedOperations.Count];

            for (int i = 0; i < orderedOperations.Count; i++)
            {
                var operation = orderedOperations[i];
                var targetIndex = Mathf.Max(0, operation.TargetIndex);
                var startLocalPosition = ConvertWorldToRootLocalPosition(operation.FlyFromPosition);
                var entry = CreateEntry(operation.Item, startLocalPosition);
                var slot = GetSlotTransform(targetIndex);

                if (slot != null)
                {
                    SetEntryParent(entry, slot);
                    SetEntryPosition(entry, startLocalPosition);
                }

                movingEntries.Add(entry);
                pendingAdds.Add(new PendingAddEntry { Entry = entry, TargetIndex = targetIndex });
                startPositions[i] = slot != null ? GetEntryPosition(entry) : startLocalPosition;
                targetPositions[i] = Vector3.zero;
            }

            yield return StartCoroutine(AnimateEntries(movingEntries, startPositions, targetPositions, GetAddDuration()));

            CommitAddEntries(pendingAdds);
            yield return StartCoroutine(ReflowAllEntries(GetReflowDuration()));
        }

        private void CommitAddEntries(List<PendingAddEntry> pendingAdds)
        {
            if (pendingAdds == null || pendingAdds.Count == 0)
            {
                return;
            }

            var orderedAdds = new List<PendingAddEntry>(pendingAdds.Count);
            for (int i = 0; i < pendingAdds.Count; i++)
            {
                var pendingAdd = pendingAdds[i];
                if (pendingAdd == null || pendingAdd.Entry == null)
                {
                    continue;
                }

                orderedAdds.Add(pendingAdd);
            }

            if (orderedAdds.Count == 0)
            {
                return;
            }

            orderedAdds.Sort((left, right) => left.TargetIndex.CompareTo(right.TargetIndex));

            var existingEntries = new List<VisualEntry>(entries);
            entries.Clear();

            int existingIndex = 0;
            int addIndex = 0;
            int totalCount = existingEntries.Count + orderedAdds.Count;

            for (int position = 0; position < totalCount; position++)
            {
                if (addIndex < orderedAdds.Count && orderedAdds[addIndex].TargetIndex == position)
                {
                    while (addIndex < orderedAdds.Count && orderedAdds[addIndex].TargetIndex == position)
                    {
                        entries.Add(orderedAdds[addIndex].Entry);
                        addIndex++;
                    }

                    continue;
                }

                if (existingIndex < existingEntries.Count)
                {
                    entries.Add(existingEntries[existingIndex]);
                    existingIndex++;
                }
            }

            while (addIndex < orderedAdds.Count)
            {
                entries.Add(orderedAdds[addIndex].Entry);
                addIndex++;
            }

            while (existingIndex < existingEntries.Count)
            {
                entries.Add(existingEntries[existingIndex]);
                existingIndex++;
            }
        }


        private IEnumerator PlayMergeOperation(WeaponCraftOperation operation)
        {
            if (operation.Item == null)
            {
                yield break;
            }

            var sourceEntries = FindEntries(operation.SourceItems);
            if (sourceEntries.Count == 0)
            {
                yield break;
            }

            int resultIndex = Mathf.Clamp(operation.TargetIndex, 0, Mathf.Max(0, entries.Count - sourceEntries.Count));
            Vector3 targetPosition = GetSlotLocalPosition(resultIndex);
            for (int i = 0; i < sourceEntries.Count; i++)
            {
                PrepareEntryForMove(sourceEntries[i]);
            }

            yield return StartCoroutine(AnimateEntries(sourceEntries, targetPosition, GetMergeMoveDuration()));

            float delay = GetMergeSpawnDelay();
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            for (int i = 0; i < sourceEntries.Count; i++)
            {
                RemoveEntry(sourceEntries[i]);
            }

            var resultEntry = CreateEntry(operation.Item, targetPosition);
            entries.Insert(resultIndex, resultEntry);
            AttachEntryToSlot(resultEntry, resultIndex);
            MergeVisualCompleted?.Invoke(operation.Item);

            yield return StartCoroutine(ReflowAllEntries(GetReflowDuration()));
        }

        private VisualEntry CreateEntry(WeaponItem item, Vector3 startPosition)
        {
            EnsureRoot();
            var prefab = ResolvePrefab(item.Tier);
            GameObject instance = prefab != null ? Instantiate(prefab, itemRoot, false) : CreateFallbackInstance(item.Tier);
            instance.name = $"WeaponItem_T{item.Tier}";

            if (instance.TryGetComponent(out RectTransform rect))
            {
                rect.SetParent(itemRoot, false);
                rect.anchoredPosition3D = startPosition;
                rect.localScale = Vector3.one;
            }
            else
            {
                instance.transform.SetParent(itemRoot, false);
                instance.transform.localPosition = startPosition;
                instance.transform.localScale = Vector3.one;
            }

            return new VisualEntry
            {
                Item = item,
                Instance = instance
            };
        }

        private GameObject CreateFallbackInstance(int tier)
        {
            var fallback = new GameObject($"WeaponItemFallback_T{tier}", typeof(RectTransform));
            fallback.transform.SetParent(itemRoot, false);
            return fallback;
        }

        private IEnumerator ReflowAllEntries(float duration)
        {
            if (entries == null || entries.Count == 0)
            {
                yield break;
            }

            var startPositions = new Vector3[entries.Count];
            var targetPositions = new Vector3[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null)
                {
                    continue;
                }
                PrepareEntryForMove(entries[i]);
                startPositions[i] = GetEntryPosition(entries[i]);
                targetPositions[i] = GetSlotLocalPosition(i);
            }

            yield return StartCoroutine(AnimateEntries(entries, startPositions, targetPositions, duration));

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null)
                {
                    continue;
                }
                AttachEntryToSlot(entries[i], i);
            }
        }

        private IEnumerator AnimateEntries(List<VisualEntry> movingEntries, Vector3 targetPosition, float duration)
        {
            var startPositions = new Vector3[movingEntries.Count];
            var targetPositions = new Vector3[movingEntries.Count];
            for (int i = 0; i < movingEntries.Count; i++)
            {
                startPositions[i] = GetEntryPosition(movingEntries[i]);
                targetPositions[i] = targetPosition;
            }

            yield return AnimateEntries(movingEntries, startPositions, targetPositions, duration);
        }

        private IEnumerator AnimateEntries(List<VisualEntry> movingEntries, Vector3[] startPositions, Vector3[] targetPositions, float duration)
        {
            if (movingEntries == null || movingEntries.Count == 0)
            {
                yield break;
            }

            int safeCount = Mathf.Min(movingEntries.Count, Mathf.Min(startPositions.Length, targetPositions.Length));
            duration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < safeCount; i++)
                {
                    SetEntryPosition(movingEntries[i], Vector3.Lerp(startPositions[i], targetPositions[i], t));
                }

                yield return null;
            }

            for (int i = 0; i < safeCount; i++)
            {
                SetEntryPosition(movingEntries[i], targetPositions[i]);
            }
        }

        private List<VisualEntry> FindEntries(List<WeaponItem> sourceItems)
        {
            var found = new List<VisualEntry>();
            if (sourceItems == null || sourceItems.Count == 0)
            {
                return found;
            }

            for (int i = 0; i < sourceItems.Count; i++)
            {
                var sourceItem = sourceItems[i];
                if (sourceItem == null)
                {
                    continue;
                }

                for (int j = 0; j < entries.Count; j++)
                {
                    if (ReferenceEquals(entries[j].Item, sourceItem))
                    {
                        found.Add(entries[j]);
                        break;
                    }
                }
            }

            return found;
        }

        private void RemoveEntry(VisualEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            entries.Remove(entry);
            if (entry.Instance != null)
            {
                Destroy(entry.Instance);
            }
        }


        private Vector3 GetSlotLocalPosition(int index)
        {
            var slot = GetSlotTransform(index);
            if (slot == null || itemRoot == null)
            {
                return Vector3.zero;
            }

            return itemRoot.InverseTransformPoint(slot.position);
        }

        private Vector3 ConvertWorldToRootLocalPosition(Vector3 worldPosition)
        {
            if (itemRoot == null)
            {
                return worldPosition;
            }

            Canvas canvas = itemRoot.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Camera sourceCamera = Camera.main;
                if (sourceCamera != null)
                {
                    Vector3 screenPoint = sourceCamera.WorldToScreenPoint(worldPosition);
                    Camera eventCamera = null;
                    if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    {
                        eventCamera = canvas.worldCamera != null ? canvas.worldCamera : sourceCamera;
                    }

                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(itemRoot, screenPoint, eventCamera, out Vector2 localPoint))
                    {
                        return new Vector3(localPoint.x, localPoint.y, 0f);
                    }
                }
            }

            return itemRoot.InverseTransformPoint(worldPosition);
        }

        private RectTransform GetSlotTransform(int index)
        {
            if (slots == null || slots.Count == 0)
            {
                return null;
            }

            int clampedIndex = Mathf.Clamp(index, 0, slots.Count - 1);
            return slots[clampedIndex];
        }

        private void PrepareEntryForMove(VisualEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.RectTransform != null)
            {
                entry.RectTransform.SetParent(itemRoot, true);
                return;
            }

            if (entry.Transform != null)
            {
                entry.Transform.SetParent(itemRoot, true);
            }
        }

        private void AttachEntryToSlot(VisualEntry entry, int slotIndex)
        {
            if (entry == null)
            {
                return;
            }

            var slot = GetSlotTransform(slotIndex);
            if (slot == null)
            {
                return;
            }

            var rect = entry.RectTransform;
            if (rect != null)
            {
                rect.SetParent(slot, false);
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                return;
            }

            if (entry.Transform != null)
            {
                entry.Transform.SetParent(slot, false);
                entry.Transform.localPosition = Vector3.zero;
                entry.Transform.localRotation = Quaternion.identity;
                entry.Transform.localScale = Vector3.one;
            }
        }

        private void SetEntryParent(VisualEntry entry, Transform parent)
        {
            if (entry == null || parent == null)
            {
                return;
            }

            if (entry.RectTransform != null)
            {
                entry.RectTransform.SetParent(parent, false);
                return;
            }

            if (entry.Transform != null)
            {
                entry.Transform.SetParent(parent, false);
            }
        }

        private Vector3 GetSlotLocalStartPosition(Transform slot, Vector3 worldPosition)
        {
            if (slot == null)
            {
                return Vector3.zero;
            }

            return slot.InverseTransformPoint(worldPosition);
        }

        private Vector3 GetEntryPosition(VisualEntry entry)
        {
            if (entry == null)
            {
                return Vector3.zero;
            }

            var rect = entry.RectTransform;
            if (rect != null)
            {
                return rect.anchoredPosition3D;
            }

            return entry.Transform != null ? entry.Transform.localPosition : Vector3.zero;
        }

        private void SetEntryPosition(VisualEntry entry, Vector3 position)
        {
            if (entry == null)
            {
                return;
            }

            var rect = entry.RectTransform;
            if (rect != null)
            {
                rect.anchoredPosition3D = position;
                return;
            }

            if (entry.Transform != null)
            {
                entry.Transform.localPosition = position;
            }
        }

        private GameObject ResolvePrefab(int tier)
        {
            if (config == null)
            {
                return null;
            }

            return config.GetPrefabForTier(tier);
        }

        private float GetAddDuration()
        {
            return config != null ? config.AddMoveDuration : 0.25f;
        }

        private float GetMergeMoveDuration()
        {
            float baseDuration = config != null ? config.MergeMoveDuration : 0.2f;
            return Mathf.Max(0.01f, baseDuration / Mathf.Max(1f, mergeSpeedMultiplier));
        }

        private float GetReflowDuration()
        {
            float baseDuration = config != null ? config.LayoutReflowDuration : 0.15f;
            return Mathf.Max(0.01f, baseDuration / Mathf.Max(1f, mergeSpeedMultiplier));
        }

        private float GetMergeSpawnDelay()
        {
            float baseDelay = config != null ? config.MergeSpawnDelay : 0.05f;
            return Mathf.Max(0f, baseDelay / Mathf.Max(1f, mergeSpeedMultiplier));
        }

        private void EnsureRoot()
        {
            if (itemRoot != null)
            {
                return;
            }

            var existing = transform.Find("WeaponCraftItems") as RectTransform;
            if (existing != null)
            {
                itemRoot = existing;
                return;
            }

            var root = new GameObject("WeaponCraftItems", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            itemRoot = root.GetComponent<RectTransform>();
        }

        private void ClearEntries()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && entry.Instance != null)
                {
                    Destroy(entry.Instance);
                }
            }

            entries.Clear();
        }
    }
}
