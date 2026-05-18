using System;
using GamePlay.Characters;
using UnityEngine;

/// <summary>
/// Runtime gameplay variables used by the playable build.
/// 
/// Mục tiêu:
/// - Giữ đúng flow core gameplay (wheel / character / evolution).
/// - Không phụ thuộc các hệ thống chỉ có ở bản full (GameEventBus, reward, shop, IAP...).
/// - Không phụ thuộc package ngoài (Alchemy/KBCore/...) ở compile-time.
/// </summary>
[CreateAssetMenu(fileName = "GamePlayVariable", menuName = "ScriptableObjects/GamePlay/GamePlayVariable")]
public class GamePlayVariable : ScriptableObject
{
    [Header("Variables")]
    public WheelVariable WheelVariable;
    public CharacterVariable CharacterVariable;
    public EvolutionVariable EvolutionVariable;

    [Header("Tuning (Playable)")]
    [Tooltip("Mỗi điểm FireRate sẽ giảm Duration của Character MoveVariable (duration càng thấp = fire rate càng cao).")]
    [SerializeField] private float fireRateDurationStep = 0.05f;

    [Tooltip("Giới hạn thấp nhất cho Duration để tránh về 0.")]
    [SerializeField] private float minFireRateDuration = 0.1f;

    [Tooltip("Mỗi điểm FireRange sẽ cộng vào MaxDistance của Character MoveVariable.")]
    [SerializeField] private float fireRangeDistanceStep = 1f;

    [Tooltip("Mỗi điểm MoveSpeed sẽ cộng vào ForwardSpeed của WheelVariable.")]
    [SerializeField] private float moveSpeedStep = 0.5f;

    /// <summary>
    /// Reset state theo phạm vi run (không reset evolution/capacity).
    /// </summary>
    public void ResetNewGame()
    {
        // Reference flow keeps capacity/evolution progression.
        // GameplayManager already resets wheel + character variables explicitly.
    }

    public void ResetCharacterVariable()
    {
        if (CharacterVariable != null) CharacterVariable.ResetValues();
    }

    public void ResetWheelVariable()
    {
        if (WheelVariable != null) WheelVariable.ResetValues();
    }

    public void ResetWheelVariable_MoveSpeed()
    {
        if (WheelVariable == null) return;
        WheelVariable.ForwardSpeed = WheelVariable.DefaultForwardSpeed;
    }

    public void ResetEvolutionVariable()
    {
        if (EvolutionVariable != null) 
        {
            EvolutionVariable.ResetValues();
            SyncCapacityDataToPlayerState();
            GameEventBus.UpdateCapacityBar?.Invoke(); // Refresh UI on reset
        }
    }

    /// <summary>
    /// Modifier: FireRate.
    /// Map theo game gốc: điều chỉnh WheelVariable.TurnDuration dựa trên ItemConfig.
    /// </summary>
    public void ChangeFireRateVariable(int addPoint)
    {
        if (WheelVariable == null) return;

        // Reference flow: clamp within ItemConfig range using ItemData points
        if (TryResolveItemConfig(out var itemData, out var itemConfig) &&
            itemData.MaxFireRatePoint > 0f)
        {
            float minFireRate = itemConfig.MinFireRate;
            float maxFireRate = itemConfig.MaxFireRate;
            float fireRatePerPoint = (maxFireRate - minFireRate) / itemData.MaxFireRatePoint;
            float addFireRate = addPoint * fireRatePerPoint;
            float newFireRate = Mathf.Clamp(WheelVariable.TurnDuration - addFireRate, minFireRate, maxFireRate);

            if (!Mathf.Approximately(newFireRate, WheelVariable.TurnDuration))
            {
                WheelVariable.TurnDuration = newFireRate;
            }
            return;
        }

        // Fallback with ItemData only (no ItemConfigSO): derive per-point from defaults
        if (TryResolveItemDataOnly(out var dataOnly) && dataOnly.MaxFireRatePoint > 0f)
        {
            float maxFireRate = WheelVariable != null ? WheelVariable.DefaultTurnDuration : WheelVariable.TurnDuration;
            float minFireRate = minFireRateDuration;
            float fireRatePerPoint = (maxFireRate - minFireRate) / dataOnly.MaxFireRatePoint;
            float addFireRate = addPoint * fireRatePerPoint;
            float newFireRate = Mathf.Clamp(WheelVariable.TurnDuration - addFireRate, minFireRate, maxFireRate);

            if (!Mathf.Approximately(newFireRate, WheelVariable.TurnDuration))
            {
                WheelVariable.TurnDuration = newFireRate;
            }
            return;
        }

        // Fallback: simple step-based change
        WheelVariable.TurnDuration = Mathf.Max(minFireRateDuration, WheelVariable.TurnDuration - addPoint * fireRateDurationStep);
    }

    /// <summary>
    /// Modifier: FireRange.
    /// Map theo game gốc: điều chỉnh CharacterVariable.MoveVariable.MaxDistance dựa trên ItemConfig.
    /// </summary>
    public void ChangeFireRangeVariable(int addPoint)
    {
        if (CharacterVariable == null) return;
        var mv = CharacterVariable.MoveVariable;
        if (mv == null) return;

        // Reference flow: clamp within ItemConfig range using ItemData points
        if (TryResolveItemConfig(out var itemData, out var itemConfig) &&
            itemData.MaxFireRangePoint > 0f)
        {
            float minFireRange = itemConfig.MinFireRange;
            float maxFireRange = itemConfig.MaxFireRange;
            float fireRangePerPoint = (maxFireRange - minFireRange) / itemData.MaxFireRangePoint;
            float addFireRange = addPoint * fireRangePerPoint;

            float newFireRange = Mathf.Clamp(mv.MaxDistance + addFireRange, minFireRange, maxFireRange);
            if (!Mathf.Approximately(newFireRange, mv.MaxDistance))
            {
                mv.MaxDistance = newFireRange;
            }
            return;
        }

        // Fallback: scale by MaxFireRangePoint to avoid overgrowth when ItemConfigSO is missing.
        if (TryResolveItemDataOnly(out var itemDataOnly) && itemDataOnly.MaxFireRangePoint > 0f)
        {
            float minFireRange = mv.DefaultMaxDistance;
            float totalIncrease = fireRangeDistanceStep;
            float maxFireRange = minFireRange + totalIncrease;
            if (maxFireRange < minFireRange) maxFireRange = minFireRange;

            float perPoint = totalIncrease / Mathf.Max(1f, itemDataOnly.MaxFireRangePoint);
            float newFireRange = Mathf.Clamp(mv.MaxDistance + addPoint * perPoint, minFireRange, maxFireRange);
            if (!Mathf.Approximately(newFireRange, mv.MaxDistance))
            {
                mv.MaxDistance = newFireRange;
            }
            return;
        }

        // Fallback: simple step-based change
        mv.MaxDistance = Mathf.Max(0f, mv.MaxDistance + addPoint * fireRangeDistanceStep);
    }

    /// <summary>
    /// Modifier: MoveSpeed.
    /// Ở playable này, move speed được map vào WheelVariable.ForwardSpeed.
    /// </summary>
    public void ChangeMoveSpeedVariable(int addPoint)
    {
        if (WheelVariable == null) return;
        WheelVariable.ForwardSpeed = Mathf.Max(0f, WheelVariable.ForwardSpeed + addPoint * moveSpeedStep);
    }

    private static bool TryResolveItemConfig(out ItemDataSO itemData, out ItemConfigSO itemConfig)
    {
        itemData = null;
        itemConfig = null;

        if (ConfigHolder.Instance != null)
        {
            itemData = ConfigHolder.Instance.GetCurrentItemConfig();
            itemConfig = ConfigHolder.Instance.ItemConfigSO;
        }

        if (itemData == null && GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
        {
            itemData = GameplayManager.Instance.PlayableEra.ItemConfig;
        }

        if (itemConfig == null)
        {
            itemConfig = Resources.Load<ItemConfigSO>("ItemConfigSO");
        }

        return itemData != null && itemConfig != null;
    }

    private static bool TryResolveItemDataOnly(out ItemDataSO itemData)
    {
        itemData = null;

        if (ConfigHolder.Instance != null)
        {
            itemData = ConfigHolder.Instance.GetCurrentItemConfig();
        }

        if (itemData == null && GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
        {
            itemData = GameplayManager.Instance.PlayableEra.ItemConfig;
        }

        return itemData != null;
    }

    /// <summary>
    /// Modifier: EvolutionPoint.
    /// Ở playable này, điểm tiến hoá được lưu trong EvolutionVariable.ProgressPoint.
    /// </summary>
    public void ChangeEvolutionPointVariable(int addPoint)
    {
        if (EvolutionVariable == null) return;
        EvolutionVariable.ProgressPoint = Mathf.Max(0, EvolutionVariable.ProgressPoint + addPoint);
        int previousCapacity = EvolutionVariable.Capacity;

        var evoConfig = ResolveEvolutionConfig();
        if (evoConfig != null)
        {
            int maxLevel = evoConfig.GetMaxLevel();

            // Level up immediately in gameplay logic (not in UI), supports burst gains.
            while (EvolutionVariable.Capacity < maxLevel)
            {
                int required = evoConfig.GetPointsRequiredForLevel(EvolutionVariable.Capacity + 1);
                if (required <= 0) break;
                if (EvolutionVariable.ProgressPoint < required) break;

                EvolutionVariable.ProgressPoint -= required;
                EvolutionVariable.Capacity++;
            }
        }

        if (EvolutionVariable.Capacity > previousCapacity)
            GameEventBus.UpgradeCapacity?.Invoke(EvolutionVariable.Capacity);

        SyncCapacityDataToPlayerState();

        GameEventBus.UpdateCapacityBar?.Invoke();
    }

    /// <summary>
    /// Modifier: Capacity (nếu có).
    /// </summary>
    public void ChangeCapacityVariable(int addPoint)
    {
        if (EvolutionVariable == null) return;
        EvolutionVariable.Capacity = Mathf.Max(0, EvolutionVariable.Capacity + addPoint);
        SyncCapacityDataToPlayerState();
    }

    private EvolutionConfigSO ResolveEvolutionConfig()
    {
        if (ConfigHolder.Instance != null)
        {
            var cfg = ConfigHolder.Instance.GetCurrentEvolutionConfig();
            if (cfg != null) return cfg;
        }

        if (GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
        {
            return GameplayManager.Instance.PlayableEra.EvolutionConfig;
        }

        return null;
    }

    private void SyncCapacityDataToPlayerState()
    {
        if (EvolutionVariable == null) return;
        if (DataManager.PlayerData == null) return;

        if (DataManager.PlayerData.CapacityData == null)
            DataManager.PlayerData.CapacityData = new CapacityData();

        DataManager.PlayerData.CapacityData.Capacity = Mathf.Max(1, EvolutionVariable.Capacity);
        DataManager.PlayerData.CapacityData.Level = Mathf.Max(1, EvolutionVariable.Capacity);
        DataManager.PlayerData.CapacityData.Progress = Mathf.Max(0, EvolutionVariable.ProgressPoint);
    }
}
