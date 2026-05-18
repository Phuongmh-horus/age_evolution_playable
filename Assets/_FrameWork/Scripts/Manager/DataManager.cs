// DataManager.cs
// Playable/Luna: Simplified data manager
// Loại bỏ: IAP, LocalDatabase, Save/Load, Tracking, Heart/Energy, Profile, UniTask
// Giữ lại: In-memory game state với structure tương thích code cũ

using System;
using UnityEngine;

/// <summary>
/// Simplified DataManager cho Playable Ads
/// - Không save/load - chỉ in-memory
/// - Không IAP, tracking, profile
/// - Structure tương thích với code cũ (PlayerData.Currency.Cash, etc.)
/// </summary>
public static class DataManager
{
    // =========================================================================
    // PLAYABLE GAME STATE (In-memory only)
    // =========================================================================

    public static PlayerData PlayerData { get; private set; }
    public static bool IsInitialized { get; private set; }

    // =========================================================================
    // INITIALIZATION
    // =========================================================================

    /// <summary>
    /// Khởi tạo data cho playable ads
    /// Gọi 1 lần khi scene load
    /// </summary>
    public static void InitData()
    {
        if (IsInitialized) return;

        PlayerData = new PlayerData();
        ResetToDefault();
        IsInitialized = true;

        Debug.Log("[DataManager] Playable data initialized");
    }

    /// <summary>
    /// Reset về giá trị mặc định
    /// </summary>
    public static void ResetToDefault()
    {
        if (PlayerData == null)
            PlayerData = new PlayerData();

        // Currency
        PlayerData.Currency ??= new CurrencyData();
        PlayerData.Currency.Cash = 1000; // Starting cash cho playable
        PlayerData.Currency.Gem = 0;

        // Wheel
        PlayerData.WheelData ??= new WheelData();
        PlayerData.WheelData.CardCount = 1;
        PlayerData.WheelData.CardLevel = 1;
        PlayerData.WheelData.CardStar = 0;

        // Capacity
        PlayerData.CapacityData ??= new CapacityData();
        PlayerData.CapacityData.Level = 1;
        PlayerData.CapacityData.Capacity = 1;
        PlayerData.CapacityData.Progress = 0;

        // Income
        PlayerData.IncomeLevel = 1;

        // Level
        PlayerData.LevelSaveData ??= new LevelSaveData();
        PlayerData.LevelSaveData.EraId = 1;
        PlayerData.LevelSaveData.ContentId = 0;
    }

    // =========================================================================
    // CURRENCY
    // =========================================================================

    /// <summary>
    /// Thay đổi Cash
    /// </summary>
    public static void ChangeCash(int amount)
    {
        if (PlayerData?.Currency == null) return;

        int oldValue = PlayerData.Currency.Cash;
        PlayerData.Currency.Cash = Mathf.Max(0, PlayerData.Currency.Cash + amount);

        GameEventBus.OnCurrencyChanged?.Invoke(oldValue, PlayerData.Currency.Cash);
        GameEventBus.OnCashChange?.Invoke();

        if (amount > 0)
            GameEventBus.OnGainGold?.Invoke();
    }

    /// <summary>
    /// Thay đổi Gem
    /// </summary>
    public static void ChangeGem(int amount)
    {
        if (PlayerData?.Currency == null) return;

        PlayerData.Currency.Gem = Mathf.Max(0, PlayerData.Currency.Gem + amount);
        GameEventBus.OnGemChange?.Invoke();
    }

    /// <summary>
    /// Kiểm tra có đủ Cash không
    /// </summary>
    public static bool HasEnoughCash(int amount)
    {
        return PlayerData?.Currency != null && PlayerData.Currency.Cash >= amount;
    }

    /// <summary>
    /// Kiểm tra có đủ Gem không
    /// </summary>
    public static bool HasEnoughGem(int amount)
    {
        return PlayerData?.Currency != null && PlayerData.Currency.Gem >= amount;
    }

    /// <summary>
    /// Trừ Cash nếu đủ
    /// </summary>
    public static bool TrySpendCash(int amount)
    {
        if (!HasEnoughCash(amount)) return false;
        ChangeCash(-amount);
        return true;
    }

    /// <summary>
    /// Trừ Gem nếu đủ
    /// </summary>
    public static bool TrySpendGem(int amount)
    {
        if (!HasEnoughGem(amount)) return false;
        ChangeGem(-amount);
        return true;
    }

    // =========================================================================
    // WHEEL DATA
    // =========================================================================

    /// <summary>
    /// Tăng số card trên wheel
    /// </summary>
    public static void AddWheelCard()
    {
        if (PlayerData?.WheelData == null) return;

        PlayerData.WheelData.CardCount++;
        GameEventBus.OnAddWheelCard?.Invoke();
    }

    /// <summary>
    /// Boost card (tăng star/level)
    /// </summary>
    public static void BoostWheelCard()
    {
        if (PlayerData?.WheelData == null) return;

        PlayerData.WheelData.CardStar++;

        // Mỗi 3 star = 1 level
        if (PlayerData.WheelData.CardStar >= 3)
        {
            PlayerData.WheelData.CardStar = 0;
            PlayerData.WheelData.CardLevel++;
            // Match reference flow: wheel rebuild only when enough 3 stars -> level up.
            GameEventBus.OnBoostWheelCardLevelUpOnly?.Invoke();
            GameEventBus.OnBoostWheelCard?.Invoke();
        }
    }

    /// <summary>
    /// Set wheel data trực tiếp
    /// </summary>
    public static void SetWheelData(int cardCount, int cardLevel, int cardStar)
    {
        if (PlayerData?.WheelData == null) return;

        PlayerData.WheelData.CardCount = Mathf.Max(1, cardCount);
        PlayerData.WheelData.CardLevel = Mathf.Max(1, cardLevel);
        PlayerData.WheelData.CardStar = Mathf.Clamp(cardStar, 0, 2);
    }

    // =========================================================================
    // CAPACITY DATA
    // =========================================================================

    /// <summary>
    /// Tăng capacity progress
    /// </summary>
    public static void AddCapacityProgress(int amount)
    {
        if (PlayerData?.CapacityData == null) return;

        PlayerData.CapacityData.Progress += amount;

        // Check level up (simplified - cứ 10 progress = 1 level)
        int progressPerLevel = 10;
        while (PlayerData.CapacityData.Progress >= progressPerLevel)
        {
            PlayerData.CapacityData.Progress -= progressPerLevel;
            PlayerData.CapacityData.Level++;
            PlayerData.CapacityData.Capacity++;
        }

        GameEventBus.UpdateCapacityBar?.Invoke();
    }

    /// <summary>
    /// Upgrade capacity trực tiếp
    /// </summary>
    public static void UpgradeCapacity()
    {
        if (PlayerData?.CapacityData == null) return;

        PlayerData.CapacityData.Level++;
        PlayerData.CapacityData.Capacity++;
        GameEventBus.UpgradeCapacity?.Invoke(PlayerData.CapacityData.Level);
    }

    // =========================================================================
    // INCOME
    // =========================================================================

    /// <summary>
    /// Upgrade income level
    /// </summary>
    public static void UpgradeIncome()
    {
        if (PlayerData == null) return;
        PlayerData.IncomeLevel++;
    }

    /// <summary>
    /// Lấy income amount dựa trên level
    /// </summary>
    public static int GetIncomeAmount()
    {
        if (PlayerData == null) return 10;

        // Simplified: base 10 + 5 per level
        return 10 + (PlayerData.IncomeLevel * 5);
    }

    // =========================================================================
    // LEVEL PROGRESSION (Simplified for playable)
    // =========================================================================

    /// <summary>
    /// Chuyển level (playable simplified)
    /// </summary>
    public static void ChangeLevel(bool isWin)
    {
        if (PlayerData?.LevelSaveData == null) return;

        if (isWin)
        {
            // Win: có thể chuyển era hoặc content
            PlayerData.LevelSaveData.ContentId++;
            GameEventBus.OnNewLevel?.Invoke();
        }
        else
        {
            // Lose: giữ nguyên hoặc replay
            PlayerData.LevelSaveData.ContentId = 0;
        }
    }

    /// <summary>
    /// Reset data khi bắt đầu era mới
    /// </summary>
    public static void ResetForNewEra()
    {
        if (PlayerData == null) return;

        if (PlayerData.WheelData != null)
        {
            PlayerData.WheelData.CardCount = 1;
            PlayerData.WheelData.CardLevel = 1;
            PlayerData.WheelData.CardStar = 0;
        }

        if (PlayerData.CapacityData != null)
        {
            PlayerData.CapacityData.Level = 1;
            PlayerData.CapacityData.Capacity = 1;
            PlayerData.CapacityData.Progress = 0;
        }

        PlayerData.IncomeLevel = 1;
    }

    // =========================================================================
    // STUB METHODS (cho compatibility với code cũ - không làm gì)
    // =========================================================================

    /// <summary>
    /// Stub - Playable không save
    /// </summary>
    public static void SavePlayerData() { }
}

// =========================================================================
// DATA CLASSES (Tương thích với code cũ)
// =========================================================================

/// <summary>
/// Player data structure - tương thích với code cũ
/// </summary>
[Serializable]
public class PlayerData
{
    // Currency
    public CurrencyData Currency;

    // Wheel
    public WheelData WheelData;

    // Capacity
    public CapacityData CapacityData;

    // Income
    public int IncomeLevel = 1;

    // Level
    public LevelSaveData LevelSaveData;

    // Display
    public int LevelDisplay = 1;

    public PlayerData()
    {
        Currency = new CurrencyData();
        WheelData = new WheelData();
        CapacityData = new CapacityData();
        LevelSaveData = new LevelSaveData();
    }
}

[Serializable]
public class CurrencyData
{
    public int Cash = 1000;
    public int Gem = 0;
}

[Serializable]
public class WheelData
{
    public int CardCount = 1;
    public int CardLevel = 1;
    public int CardStar = 0;
}

[Serializable]
public class CapacityData
{
    public int Level = 1;
    public int Capacity = 1;
    public int Progress = 0;
}

[Serializable]
public class LevelSaveData
{
    public int TimeLineId = 0;
    public int EraId = 1;
    public int ContentId = 0;
    public int Milestone = 0;
}
