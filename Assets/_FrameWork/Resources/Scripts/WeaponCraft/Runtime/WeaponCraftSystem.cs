using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace WeaponCraft
{
    public sealed class WeaponCraftSystem : MonoSingleton<WeaponCraftSystem>
    {
        private sealed class IncomingBatch
        {
            public readonly List<WeaponItem> Items;
            public readonly Vector3 FlyFromPosition;

            public IncomingBatch(List<WeaponItem> items, Vector3 flyFromPosition)
            {
                Items = items;
                FlyFromPosition = flyFromPosition;
            }
        }

        [Header("Craft Settings")]
        [SerializeField] private WeaponCraftConfigSO config;
        [SerializeField] private WeaponCraftVisualSystem visualSystem;
        [SerializeField] private EffectComponent upgradeEffectComponent;
        [SerializeField] private AudioClipName fallbackMergeSfx = AudioClipName.SFX_Merge_Weapon;

        private readonly List<WeaponItem> items = new List<WeaponItem>();
        private readonly Queue<IncomingBatch> incomingItems = new Queue<IncomingBatch>();
        private readonly Dictionary<WeaponItem, int> sequenceByItem = new Dictionary<WeaponItem, int>();
        private int nextSequence = 1;
        private Coroutine processRoutine;
        private int _equippedTopTier = -1;

        public event Action<WeaponItem> ItemAdded;

        public List<WeaponItem> Items => items;
        public bool HasItems => items.Count > 0;
        public bool IsProcessing => processRoutine != null;
        public WeaponCraftConfigSO Config => config;
        public WeaponCraftVisualSystem VisualSystem => visualSystem;

        protected override void Awake()
        {
            base.Awake();
            EnsureVisualSystem();
        }

        private void OnEnable()
        {
            EnsureVisualSystem();
            TryStartProcessing();
        }

        private void OnDisable()
        {
            if (processRoutine != null)
            {
                StopCoroutine(processRoutine);
                processRoutine = null;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (visualSystem == null)
            {
                visualSystem = GetComponentInChildren<WeaponCraftVisualSystem>(true);
            }
        }
#endif

        public WeaponItem ReceiveItem(WeaponItem item, Vector3 flyFromPosition)
        {
            if (item == null)
            {
                return null;
            }

            var runtimeItem = item.Clone();
            EnsureSequence(runtimeItem);
            EnqueueIncomingBatch(new List<WeaponItem>(1) { runtimeItem }, flyFromPosition);
            ItemAdded?.Invoke(runtimeItem);
            return runtimeItem;
        }

        public WeaponItem ReceiveItem(WeaponItem item)
        {
            return ReceiveItem(item, transform.position);
        }

        public WeaponItem ReceiveItem(int tier, Vector3 flyFromPosition)
        {
            var spawnedItems = ReceiveItem(tier, flyFromPosition, 1);
            return spawnedItems.Count > 0 ? spawnedItems[0] : null;
        }

        public List<WeaponItem> ReceiveItem(int tier, Vector3 flyFromPosition, int count)
        {
            count = Mathf.Max(1, count);
            int safeTier = Mathf.Clamp(tier, 1, GetMaxTier());

            var runtimeItems = new List<WeaponItem>(count);
            for (int i = 0; i < count; i++)
            {
                var runtimeItem = new WeaponItem(safeTier);
                EnsureSequence(runtimeItem);
                runtimeItems.Add(runtimeItem);
                ItemAdded?.Invoke(runtimeItem);
            }

            EnqueueIncomingBatch(runtimeItems, flyFromPosition);
            return runtimeItems;
        }

        private void EnqueueIncomingBatch(List<WeaponItem> runtimeItems, Vector3 flyFromPosition)
        {
            if (runtimeItems == null || runtimeItems.Count == 0)
            {
                Debug.LogWarning("[WeaponCraftSystem] EnqueueIncomingBatch skipped: runtimeItems is null or empty.");
                return;
            }

            incomingItems.Enqueue(new IncomingBatch(runtimeItems, flyFromPosition));
            TryStartProcessing();
        }

        public WeaponItem GetFirstItemOrDefault()
        {
            return items.Count > 0 ? items[0] : null;
        }

        public WeaponItem EnsureStarterItem()
        {
            var firstItem = GetFirstItemOrDefault();
            if (firstItem != null)
            {
                return firstItem;
            }

            var starterItem = ReceiveItem(1, transform.position, 1);
            return starterItem.Count > 0 ? starterItem[0] : null;
        }

#if UNITY_EDITOR
        [ContextMenu("Test Receive 1 Item From Screen Center")]
        private void TestReceiveOneItemFromScreenCenter()
        {
            ReceiveItem(1, GetScreenCenterWorldPosition());
        }

        [ContextMenu("Test Receive 3 Items From Screen Center")]
        private void TestReceiveThreeItemsFromScreenCenter()
        {
            ReceiveItem(1, GetScreenCenterWorldPosition(), 3);
        }
#endif

        private Vector3 GetScreenCenterWorldPosition()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return transform.position;
            }

            float depth = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);
            if (depth <= cam.nearClipPlane)
            {
                depth = Mathf.Max(1f, cam.nearClipPlane + 1f);
            }

            return cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, depth));
        }

        private void EnsureVisualSystem()
        {
            ResolveUpgradeEffectComponent();

            if (visualSystem == null)
            {
                visualSystem = GetComponentInChildren<WeaponCraftVisualSystem>(true);
            }

            if (visualSystem == null)
            {
                var visualRoot = new GameObject("WeaponCraftVisual");
                visualRoot.transform.SetParent(transform, false);
                visualSystem = visualRoot.AddComponent<WeaponCraftVisualSystem>();
            }

            visualSystem.MergeVisualCompleted -= HandleMergeVisualCompleted;
            visualSystem.MergeVisualCompleted += HandleMergeVisualCompleted;
            visualSystem.Bind(config);
        }

        private void TryStartProcessing()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (processRoutine != null || incomingItems.Count == 0)
            {
                return;
            }

            processRoutine = StartCoroutine(ProcessQueue());
        }

        private IEnumerator ProcessQueue()
        {
            // try
            // {
            while (incomingItems.Count > 0)
            {
                var incoming = incomingItems.Dequeue();
                var batch = BuildBatch(incoming);

                if (visualSystem != null && batch.Count > 0)
                {
                    yield return visualSystem.PlayBatch(batch);
                }

                NotifyMainWeaponChanged();
            }
            // }
            // finally
            // {
            processRoutine = null;
            // }
        }

        /// <summary>
        /// Called after each craft batch completes. If the top-tier weapon has changed,
        /// notifies GameplayManager to update its main weapon state.
        /// </summary>
        private void NotifyMainWeaponChanged()
        {
            if (items.Count == 0)
            {
                return;
            }

            var topItem = items[0];
            TryEquipIfHigher(topItem);
        }

        private void HandleMergeVisualCompleted(WeaponItem mergedItem)
        {
            bool isNewTopTier = false;
            if (mergedItem != null)
            {
                int candidateTier = Mathf.Max(1, mergedItem.Tier);
                if (candidateTier > _equippedTopTier)
                {
                    isNewTopTier = true;
                }
            }

            if (isNewTopTier)
            {
                if (!PlayLocalUpgradeEffect())
                {
                    if (fallbackMergeSfx != AudioClipName.None &&
                        SoundManager.Instance != null &&
                        SoundManager.Instance.TryPlayOneShot(fallbackMergeSfx))
                    {
                        // Audio fallback is enough when the craft prefab has no local effect component.
                    }

                    if (GameplayManager.Instance != null)
                    {
                        GameplayManager.Instance.RunUpgradeEffectAt(transform.position, transform);
                    }
                }
            }

            TryEquipIfHigher(mergedItem);
        }

        private bool PlayLocalUpgradeEffect()
        {
            var effectComponent = ResolveUpgradeEffectComponent();
            if (effectComponent == null)
            {
                return false;
            }

            effectComponent.PlayEffect(EffectType.Upgrade, transform.position, transform.rotation, transform, 0f);
            return true;
        }

        private EffectComponent ResolveUpgradeEffectComponent()
        {
            if (upgradeEffectComponent != null)
            {
                return upgradeEffectComponent;
            }

            upgradeEffectComponent = GetComponentInChildren<EffectComponent>(true);
            return upgradeEffectComponent;
        }

        private void TryEquipIfHigher(WeaponItem candidate)
        {
            if (candidate == null)
            {
                return;
            }

            int candidateTier = Mathf.Max(1, candidate.Tier);
            if (candidateTier <= _equippedTopTier)
            {
                return;
            }

            _equippedTopTier = candidateTier;

            var manager = GameplayManager.Instance;
            if (manager != null)
            {
                manager.SetMainWeapon(candidate);
            }
        }

        private List<WeaponCraftOperation> BuildBatch(IncomingBatch incoming)
        {
            var operations = new List<WeaponCraftOperation>(8);
            if (incoming == null || incoming.Items == null || incoming.Items.Count == 0)
            {
                return operations;
            }

            var incomingLookup = new HashSet<WeaponItem>(incoming.Items);
            for (int i = 0; i < incoming.Items.Count; i++)
            {
                var runtimeItem = incoming.Items[i];
                items.Add(runtimeItem);
                EnsureSequence(runtimeItem);
            }

            SortItems();

            for (int i = 0; i < items.Count; i++)
            {
                var runtimeItem = items[i];
                if (!incomingLookup.Contains(runtimeItem))
                {
                    continue;
                }

                operations.Add(WeaponCraftOperation.CreateAdd(runtimeItem, incoming.FlyFromPosition, i));
            }

            while (true)
            {
                int craftTier = FindLowestCraftableTier();
                if (craftTier < 0)
                {
                    break;
                }

                var mergeCount = GetMergeCount();
                var sources = CollectSources(craftTier, mergeCount);
                if (sources.Count < mergeCount)
                {
                    break;
                }

                var resultTier = craftTier + 1;
                if (resultTier > GetMaxTier())
                {
                    break;
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    items.Remove(source);
                    sequenceByItem.Remove(source);
                }

                var resultItem = new WeaponItem(resultTier);
                EnsureSequence(resultItem);
                items.Add(resultItem);
                SortItems();
                operations.Add(WeaponCraftOperation.CreateMerge(resultItem, sources, items.IndexOf(resultItem)));
            }

            return operations;
        }

        private int FindLowestCraftableTier()
        {
            if (items.Count < GetMergeCount())
            {
                return -1;
            }

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < items.Count; i++)
            {
                var currentTier = items[i].Tier;
                if (counts.TryGetValue(currentTier, out int count))
                {
                    counts[currentTier] = count + 1;
                }
                else
                {
                    counts[currentTier] = 1;
                }
            }

            int lowestTier = int.MaxValue;
            foreach (var pair in counts)
            {
                if (pair.Key >= GetMaxTier())
                {
                    continue;
                }

                if (pair.Value >= GetMergeCount() && pair.Key < lowestTier)
                {
                    lowestTier = pair.Key;
                }
            }

            if (lowestTier == int.MaxValue)
            {
                return -1;
            }

            return lowestTier;
        }

        private bool TryFindLowestCraftableTier(out int tier)
        {
            tier = -1;
            if (items.Count < GetMergeCount())
            {
                return false;
            }

            var counts = new Dictionary<int, int>();
            for (int i = 0; i < items.Count; i++)
            {
                var currentTier = items[i].Tier;
                if (counts.TryGetValue(currentTier, out int count))
                {
                    counts[currentTier] = count + 1;
                }
                else
                {
                    counts[currentTier] = 1;
                }
            }

            int lowestTier = int.MaxValue;
            foreach (var pair in counts)
            {
                if (pair.Key >= GetMaxTier())
                {
                    continue;
                }

                if (pair.Value >= GetMergeCount() && pair.Key < lowestTier)
                {
                    lowestTier = pair.Key;
                }
            }

            if (lowestTier == int.MaxValue)
            {
                return false;
            }

            tier = lowestTier;
            return true;
        }

        private List<WeaponItem> CollectSources(int tier, int count)
        {
            var sources = new List<WeaponItem>(count);
            for (int i = items.Count - 1; i >= 0 && sources.Count < count; i--)
            {
                if (items[i].Tier == tier)
                {
                    sources.Add(items[i]);
                }
            }

            sources.Reverse();
            return sources;
        }

        private void SortItems()
        {
            items.Sort(CompareItems);
        }

        private int CompareItems(WeaponItem left, WeaponItem right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            int tierCompare = right.Tier.CompareTo(left.Tier);
            if (tierCompare != 0)
            {
                return tierCompare;
            }

            return GetSequence(left).CompareTo(GetSequence(right));
        }

        private int GetSequence(WeaponItem item)
        {
            if (item == null)
            {
                return int.MaxValue;
            }

            return sequenceByItem.TryGetValue(item, out int sequence) ? sequence : int.MaxValue;
        }

        private void EnsureSequence(WeaponItem item)
        {
            if (item == null || sequenceByItem.ContainsKey(item))
            {
                return;
            }

            sequenceByItem[item] = nextSequence++;
        }

        private int GetMergeCount()
        {
            return config != null ? config.MergeCount : 3;
        }

        private int GetMaxTier()
        {
            int maxTier = config != null ? config.MaxTier : 1;

            if (config != null && config.TierVisuals != null)
            {
                for (int i = 0; i < config.TierVisuals.Count; i++)
                {
                    var entry = config.TierVisuals[i];
                    if (entry == null) continue;
                    if (entry.Tier > maxTier)
                    {
                        maxTier = entry.Tier;
                    }
                }
            }

            var manager = GameplayManager.Instance;
            var characterList = manager != null && manager.PlayableEra != null ? manager.PlayableEra.CharacterList : null;
            var lookup = characterList != null ? characterList.GetCharacterLookup() : null;
            if (lookup != null)
            {
                foreach (var level in lookup.Keys)
                {
                    if (level > maxTier)
                    {
                        maxTier = level;
                    }
                }
            }

            return Mathf.Max(1, maxTier);
        }
    }
}
