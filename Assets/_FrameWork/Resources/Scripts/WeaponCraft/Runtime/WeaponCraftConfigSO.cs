using System;
using System.Collections.Generic;
using UnityEngine;

// namespace WeaponCraft
// {
    [CreateAssetMenu(menuName = "WeaponCraft/Weapon Craft Config", fileName = "WeaponCraftConfig")]
    public sealed class WeaponCraftConfigSO : ScriptableObject
    {
        [Serializable]
        public sealed class TierVisualEntry
        {
            [Min(1)] public int Tier = 1;
            public string TypeId;
            public GameObject Prefab;
        }

        [Header("Craft Rules")]
        [SerializeField, Min(2)] private int mergeCount = 3;
        [SerializeField, Min(1)] private int maxTier = 5;

        [Header("Layout")]
        [SerializeField] private Vector3 slotStartLocalPosition = Vector3.zero;
        [SerializeField, Min(0.01f)] private float slotSpacing = 0.9f;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float addMoveDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float mergeMoveDuration = 0.2f;
        [SerializeField, Min(0.01f)] private float layoutReflowDuration = 0.15f;
        [SerializeField, Min(0f)] private float mergeSpawnDelay = 0.05f;

        [Header("Visuals")]
        [SerializeField] private GameObject fallbackPrefab;
        [SerializeField] private List<TierVisualEntry> tierVisuals = new List<TierVisualEntry>();

        public int MergeCount => Mathf.Max(2, mergeCount);
        public int MaxTier => Mathf.Max(1, maxTier);
        public Vector3 SlotStartLocalPosition => slotStartLocalPosition;
        public float SlotSpacing => Mathf.Max(0.01f, slotSpacing);
        public float AddMoveDuration => addMoveDuration;
        public float MergeMoveDuration => mergeMoveDuration;
        public float LayoutReflowDuration => layoutReflowDuration;
        public float MergeSpawnDelay => mergeSpawnDelay;
        public GameObject FallbackPrefab => fallbackPrefab;
        public IReadOnlyList<TierVisualEntry> TierVisuals => tierVisuals;

        public Vector3 GetSlotLocalPosition(int index)
        {
            return slotStartLocalPosition + Vector3.right * (SlotSpacing * Mathf.Max(0, index));
        }

        public GameObject GetPrefabForTier(int tier)
        {
            int safeTier = Mathf.Max(1, tier);

            // Use ONLY hard-reference tierVisuals — Resources.Load is unreliable on Luna/WebGL
            if (tierVisuals != null)
            {
                // First pass: exact tier match
                for (int i = 0; i < tierVisuals.Count; i++)
                {
                    var entry = tierVisuals[i];
                    if (entry != null && entry.Tier == safeTier && entry.Prefab != null)
                    {
                        return entry.Prefab;
                    }
                }

                // Second pass: ordered index fallback (tier 1 = index 0, etc.)
                int orderedIndex = safeTier - 1;
                if (orderedIndex >= 0 && orderedIndex < tierVisuals.Count)
                {
                    var entry = tierVisuals[orderedIndex];
                    if (entry != null && entry.Prefab != null)
                    {
                        return entry.Prefab;
                    }
                }
            }

            return fallbackPrefab;
        }
    }
// }
