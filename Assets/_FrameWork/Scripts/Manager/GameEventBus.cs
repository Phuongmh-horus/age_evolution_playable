// GameEventBus.cs
// Playable/Luna: Simple event bus cho communication giữa các systems
// Loại bỏ: IAP, Shop, Profile, BottomBar, Offer Pack, Heart/Energy, Language settings
// Giữ lại: Gameplay events, Sound, Capacity, Wheel, CTA

using System;
using UnityEngine;

public static class GameEventBus
{
    // =========================================================================
    // CTA (Call-To-Action) - PLAYABLE ADS SPECIFIC
    // =========================================================================

    /// <summary>
    /// Gọi khi cần show CTA button (ví dụ: đến đích, hết demo)
    /// </summary>
    public static Action OnShowCTA;

    /// <summary>
    /// Gọi khi user click CTA button
    /// </summary>
    public static Action OnCTAClicked;

    // =========================================================================
    // GAMEPLAY EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi game bắt đầu (user tap to play)
    /// </summary>
    public static Action OnGameStart;

    /// <summary>
    /// Gọi khi game kết thúc (win/lose)
    /// </summary>
    public static Action<bool> OnGameEnd; // bool = isWin

    /// <summary>
    /// Gọi khi character được spawn
    /// </summary>
    public static Action<int> OnCharacterSpawned; // int = characterLevel

    /// <summary>
    /// Gọi khi wheel đi qua gate
    /// </summary>
    public static Action<int> OnGatePassed; // int = gateIndex

    /// <summary>
    /// Gọi khi wheel bị knockback
    /// </summary>
    public static Action OnWheelKnockback;

    /// <summary>
    /// Gọi khi era thay đổi
    /// </summary>
    public static Action<int> OnEraChanged; // int = eraIndex

    // =========================================================================
    // STAT MODIFIER EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi StatModifier được apply lên wheel
    /// </summary>
    public static Action<GamePlay.Items.StatModifierData> OnStatModifierApplied;

    // =========================================================================
    // CAPACITY EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi capacity bar cần update UI
    /// </summary>
    public static Action UpdateCapacityBar;

    /// <summary>
    /// Gọi khi capacity được upgrade
    /// </summary>
    public static Action<int> UpgradeCapacity;

    /// <summary>
    /// Trả về vị trí screen của Capacity Bar (dùng cho camera/brick fly target)
    /// </summary>
    public static Func<Vector3> GetCapacityBarPosition;

    // =========================================================================
    // WHEEL EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi thêm card vào wheel
    /// </summary>
    public static Action OnAddWheelCard;

    /// <summary>
    /// Gọi khi boost card trên wheel
    /// </summary>
    public static Action OnBoostWheelCard;

    /// <summary>
    /// Chỉ bắn khi boost đủ 3 sao và card wheel tăng level thật sự.
    /// Dùng cho wheel gameplay rebuild deck, tránh rebuild ở mỗi lần tăng star.
    /// </summary>
    public static Action OnBoostWheelCardLevelUpOnly;

    // =========================================================================
    // CURRENCY EVENTS (Simplified for playable)
    // =========================================================================

    /// <summary>
    /// Gọi khi currency thay đổi (oldValue, newValue)
    /// </summary>
    public static Action<int, int> OnCurrencyChanged;

    /// <summary>
    /// Hiệu ứng tiền bay (visual only)
    /// </summary>
    public static Action OnGainGold;

    /// <summary>
    /// Cash thay đổi (visual only)
    /// </summary>
    public static Action OnCashChange;

    /// <summary>
    /// Gem thay đổi (visual only)
    /// </summary>
    public static Action OnGemChange;

    // =========================================================================
    // SOUND EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi thay đổi volume background music (0-1)
    /// </summary>
    public static Action<float> OnChangeSound;

    /// <summary>
    /// Gọi khi thay đổi volume sound fx (0-1)
    /// </summary>
    public static Action<float> OnChangeSoundFx;

    // =========================================================================
    // TUTORIAL EVENTS (Optional for playable)
    // =========================================================================

    /// <summary>
    /// Gọi khi hoàn thành tutorial trong gameplay
    /// </summary>
    public static Action OnCompleteGamePlayTutorial;

    // =========================================================================
    // LEVEL EVENTS
    // =========================================================================

    /// <summary>
    /// Gọi khi lên level mới
    /// </summary>
    public static Action OnNewLevel;

    // =========================================================================
    // HELPER METHODS
    // =========================================================================

    /// <summary>
    /// Reset tất cả events (gọi khi scene unload)
    /// </summary>
    public static void ClearAll()
    {
        // CTA
        OnShowCTA = null;
        OnCTAClicked = null;

        // Gameplay
        OnGameStart = null;
        OnGameEnd = null;
        OnCharacterSpawned = null;
        OnGatePassed = null;
        OnWheelKnockback = null;
        OnEraChanged = null;

        // Stat Modifier
        OnStatModifierApplied = null;

        // Capacity
        UpdateCapacityBar = null;
        UpgradeCapacity = null;
        GetCapacityBarPosition = null;

        // Wheel
        OnAddWheelCard = null;
        OnBoostWheelCard = null;
        OnBoostWheelCardLevelUpOnly = null;

        // Currency
        OnCurrencyChanged = null;
        OnGainGold = null;
        OnCashChange = null;
        OnGemChange = null;

        // Sound
        OnChangeSound = null;
        OnChangeSoundFx = null;

        // Tutorial
        OnCompleteGamePlayTutorial = null;

        // Level
        OnNewLevel = null;
    }

    /// <summary>
    /// Subscribe StatModifierItem.OnAppliedToWheel tới GameEventBus
    /// Gọi 1 lần khi scene load
    /// </summary>
    public static void SubscribeStatModifierEvents()
    {
        GamePlay.Items.StatModifierItem<GamePlay.Items.StatModifierData>.OnAppliedToWheel += HandleStatModifierApplied;
    }

    /// <summary>
    /// Unsubscribe StatModifierItem events
    /// Gọi khi scene unload
    /// </summary>
    public static void UnsubscribeStatModifierEvents()
    {
        GamePlay.Items.StatModifierItem<GamePlay.Items.StatModifierData>.OnAppliedToWheel -= HandleStatModifierApplied;
    }

    private static void HandleStatModifierApplied(GamePlay.Items.StatModifierData data)
    {
        OnStatModifierApplied?.Invoke(data);
    }
}
