using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "EvolutionConfig", menuName = "Game Config/Evolution Config")]
public class EvolutionConfigSO : ScriptableObject
{
    [System.Serializable]
    public class EvolutionLevel
    {
        [Tooltip("Level number (starting from 1)")]
        public int Level;

        [Tooltip("Points required to reach this level")]
        public int PointsRequired;
    }

    [Serializable]
    public class LevelUpgrade
    {
        public int Level;
        public int Points;
    }

    [Header("Evolution Settings")]
    public List<EvolutionLevel> EvolutionLevels = new List<EvolutionLevel>();

    public List<LevelUpgrade> LevelUpgrades = new List<LevelUpgrade>();

    [Header("Statistics")]
    public int TotalPoints;
    public int TotalPointsRequired;

    // Cache để tăng tốc độ tìm kiếm LevelUpgrades
    private Dictionary<int, LevelUpgrade> _levelUpgradeCache;
    private bool _isLevelUpgradeCacheBuilt = false;
    private int _minLevelUpgrade;
    private int _maxLevelUpgrade;

    // Cache để tăng tốc độ tìm kiếm EvolutionLevels
    private Dictionary<int, EvolutionLevel> _evolutionLevelCache;
    private bool _isEvolutionLevelCacheBuilt = false;

    /// <summary>
    /// Xây dựng cache cho LevelUpgrades
    /// </summary>
    private void BuildLevelUpgradeCache()
    {
        if (_isLevelUpgradeCacheBuilt) return;

        _levelUpgradeCache = new Dictionary<int, LevelUpgrade>(LevelUpgrades.Count);
        _minLevelUpgrade = int.MaxValue;
        _maxLevelUpgrade = int.MinValue;

        foreach (var upgrade in LevelUpgrades)
        {
            // Tối ưu: sử dụng TryAdd thay vì ContainsKey + indexer
            if (_levelUpgradeCache.TryAdd(upgrade.Level, upgrade))
            {
                // Cập nhật min/max trong cùng vòng lặp
                if (upgrade.Level < _minLevelUpgrade) _minLevelUpgrade = upgrade.Level;
                if (upgrade.Level > _maxLevelUpgrade) _maxLevelUpgrade = upgrade.Level;
            }
            else
            {
                Debug.LogWarning($"Duplicate level {upgrade.Level} found in LevelUpgrades!");
            }
        }

        _isLevelUpgradeCacheBuilt = true;
    }

    private void BuildEvolutionLevelCache()
    {
        if (_isEvolutionLevelCacheBuilt) return;

        _evolutionLevelCache = new Dictionary<int, EvolutionLevel>(EvolutionLevels.Count);

        foreach (var evolutionLevel in EvolutionLevels)
        {
            // Tối ưu: sử dụng TryAdd thay vì ContainsKey + indexer
            if (!_evolutionLevelCache.TryAdd(evolutionLevel.Level, evolutionLevel))
            {
                Debug.LogWarning($"Duplicate level {evolutionLevel.Level} found in EvolutionLevels!");
            }
        }

        _isEvolutionLevelCacheBuilt = true;
    }

    /// <summary>
    /// Lấy LevelUpgrade theo level với cache
    /// </summary>
    /// <param name="level">Level cần tìm</param>
    /// <returns>LevelUpgrade tương ứng hoặc null nếu không tìm thấy</returns>
    public LevelUpgrade GetLevelUpgradeByLevel(int level)
    {
        BuildLevelUpgradeCache();

        if (_levelUpgradeCache.TryGetValue(level, out var upgrade))
        {
            return upgrade;
        }

        Debug.LogWarning($"LevelUpgrade for level {level} not found!");
        return null;
    }

    /// <summary>
    /// Lấy LevelUpgrade theo level với logic clamp
    /// Nếu level nằm ngoài vùng data, sẽ clamp về min/max level có sẵn
    /// </summary>
    /// <param name="level">Level cần tìm</param>
    /// <returns>LevelUpgrade tương ứng (đã được clamp nếu cần)</returns>
    public LevelUpgrade GetLevelUpgrade(int level)
    {
        if (LevelUpgrades == null || LevelUpgrades.Count == 0)
        {
            Debug.LogWarning("LevelUpgrades is empty!");
            return null;
        }

        BuildLevelUpgradeCache();

        // Sử dụng cache min/max đã được tính sẵn trong BuildLevelUpgradeCache
        int clampedLevel = Mathf.Clamp(level, _minLevelUpgrade, _maxLevelUpgrade);

        // Lấy LevelUpgrade theo level đã clamp
        if (_levelUpgradeCache.TryGetValue(clampedLevel, out var upgrade))
        {
            return upgrade;
        }

        // Fallback: nếu không tìm thấy sau khi clamp, trả về upgrade đầu tiên
        Debug.LogWarning($"LevelUpgrade for clamped level {clampedLevel} not found! Returning first upgrade.");
        return LevelUpgrades[0];
    }

    /// <summary>
    /// Lấy EvolutionLevel theo level với cache
    /// </summary>
    /// <param name="level">Level cần tìm</param>
    /// <returns>EvolutionLevel tương ứng hoặc null nếu không tìm thấy</returns>
    public EvolutionLevel GetEvolutionLevelByLevel(int level)
    {
        BuildEvolutionLevelCache();

        if (_evolutionLevelCache.TryGetValue(level, out var evolutionLevel))
        {
            return evolutionLevel;
        }

        Debug.LogWarning($"EvolutionLevel for level {level} not found!");
        return null;
    }

    /// <summary>
    /// Reset cache khi data thay đổi (gọi từ Editor hoặc khi cần)
    /// </summary>
    public void ResetCache()
    {
        _isLevelUpgradeCacheBuilt = false;
        _levelUpgradeCache?.Clear();

        _isEvolutionLevelCacheBuilt = false;
        _evolutionLevelCache?.Clear();
    }

    private void OnValidate()
    {
        // Auto-setup level cho EvolutionLevel
        for (int i = 0; i < EvolutionLevels.Count; i++)
        {
            if (EvolutionLevels[i] == null) continue;
            EvolutionLevels[i].Level = i + 1; // Level bắt đầu từ 1
        }

        // Auto-setup level cho LevelUpgrade
        for (int i = 0; i < LevelUpgrades.Count; i++)
        {
            if (LevelUpgrades[i] == null) continue;
            LevelUpgrades[i].Level = i + 1; // Level bắt đầu từ 1
        }

        // Tính tổng số points từ LevelUpgrades
        TotalPoints = 0;
        foreach (var upgrade in LevelUpgrades)
        {
            if (upgrade != null)
            {
                TotalPoints += upgrade.Points;
            }
        }

        // Tính tổng số points required từ EvolutionLevels
        TotalPointsRequired = 0;
        foreach (var evolutionLevel in EvolutionLevels)
        {
            if (evolutionLevel != null)
            {
                TotalPointsRequired += evolutionLevel.PointsRequired;
            }
        }

        // Reset cache khi data thay đổi trong Editor
        ResetCache();
    }


    /// <summary>
    /// Lấy số điểm cần thiết để đạt level chỉ định (sử dụng cache)
    /// </summary>
    public int GetPointsRequiredForLevel(int level)
    {
        if (level <= 0) return -1;

        var evolutionLevel = GetEvolutionLevelByLevel(level);
        if (evolutionLevel != null)
        {
            return evolutionLevel.PointsRequired;
        }

        return -1;
    }

    /// <summary>
    /// Kiếm tra có thể lên level tiếp theo không
    /// </summary>
    public bool CanNextLevel(int currentLevel, int currentPoints)
    {
        int requirePoint = GetPointsRequiredForLevel(currentLevel);
        return currentPoints >= requirePoint;
    }

    public int GetMaxLevel() => EvolutionLevels.Count;

}
