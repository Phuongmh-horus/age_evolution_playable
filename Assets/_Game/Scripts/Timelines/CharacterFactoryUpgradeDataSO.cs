using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CharacterFactoryUpgradeConfig", menuName = "Game Config/Character Factory Upgrade Data")]
public class CharacterFactoryUpgradeDataSO : ScriptableObject
{
    // ----------------------------------------------------------------
    // 1. DATA INSPECTOR
    // Index của List chính là Level. Giá trị là RequiredExp.
    // ----------------------------------------------------------------
    [Tooltip("Index của List chính là Level. Giá trị là RequiredExp để đạt level đó.")]
    [SerializeField] private List<int> upgradeValues;

    // ----------------------------------------------------------------
    // 2. TYPE + API
    // ----------------------------------------------------------------
    [Serializable]
    public struct UpgradeConfig
    {
        public int Level;
        public int Value;
        public int RequiredExp; // Field này để CapacityIncreaseFactoryData dùng

        public UpgradeConfig(int level, int value)
        {
            Level = level;
            Value = value;
            RequiredExp = value; // RequiredExp = Value trong logic này
        }
    }

    /// <summary>
    /// List runtime build từ upgradeValues
    /// </summary>
    public IReadOnlyList<UpgradeConfig> UpgradeConfigs
    {
        get
        {
            _upgradeConfigsCache ??= BuildUpgradeConfigs();
            return _upgradeConfigsCache;
        }
    }

    private List<UpgradeConfig> _upgradeConfigsCache;

    private List<UpgradeConfig> BuildUpgradeConfigs()
    {
        var list = new List<UpgradeConfig>();

        if (upgradeValues == null) return list;

        for (int i = 0; i < upgradeValues.Count; i++)
        {
            list.Add(new UpgradeConfig(i, upgradeValues[i]));
        }

        return list;
    }

    // ----------------------------------------------------------------
    // 3. RUNTIME DICTIONARY
    // ----------------------------------------------------------------
    private Dictionary<int, int> _upgradeDict;

    public Dictionary<int, int> UpgradeDict
    {
        get
        {
            if (_upgradeDict == null)
                BuildDictionary();

            return _upgradeDict;
        }
    }

    private void BuildDictionary()
    {
        _upgradeDict = new Dictionary<int, int>();

        if (upgradeValues == null) return;

        for (int i = 0; i < upgradeValues.Count; i++)
            _upgradeDict[i] = upgradeValues[i];
    }

    // ----------------------------------------------------------------
    // 4. EDITOR VALIDATION
    // ----------------------------------------------------------------
#if UNITY_EDITOR
    private void OnValidate()
    {
        // Level 0 luôn = 0
        if (upgradeValues != null && upgradeValues.Count > 0 && upgradeValues[0] != 0)
            upgradeValues[0] = 0;

        // Reset cache
        _upgradeDict = null;
        _upgradeConfigsCache = null;

        if (Application.isPlaying)
        {
            BuildDictionary();
            _upgradeConfigsCache = BuildUpgradeConfigs();
        }
    }
#endif

    // ----------------------------------------------------------------
    // 5. API tiện ích
    // ----------------------------------------------------------------
    public int GetValueAtLevel(int level)
    {
        if (upgradeValues == null || level < 0 || level >= upgradeValues.Count)
            return -1;

        return upgradeValues[level];
    }
}