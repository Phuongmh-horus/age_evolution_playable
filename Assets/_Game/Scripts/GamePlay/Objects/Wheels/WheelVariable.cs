using UnityEngine;
using DG.Tweening;

[CreateAssetMenu(fileName = "WheelVariable", menuName = "GameVariables/Wheels/WheelVariable")]
public class WheelVariable : ScriptableObject
{
    [Header("Movement Settings")]
    public float ForwardSpeed = 5f;
    public float XLimit = 4f;
    public float StrafeMultiplier = 1.0f;
    [Range(0.01f, 1f)] public float MoveSmoothness = 0.07f;

    [Header("Rotate Settings")]
    public float TurnDuration = 2f; // Giây/vòng

    [Header("Wheel Settings")]
    public int TotalSlots = 8;
    public float Radius = 2.15f; // Bán kính bàn xoay
    public float LayerHeight = 0.5f;

    [Header("Spawn Settings")]
    public float UnitHorizontalSpace = 0.8f;

    [Tooltip("Góc của mũi tên Trigger so với hướng tiến của xe")]
    public float ArrowAngleOffset = 0f;

    [Header("Knockback Settings")]
    [Tooltip("Khoảng cách wheel bị đẩy lùi khi va chạm Obstacle")]
    public float KnockbackDistance = 2f;
    [Tooltip("Thời gian knockback (giây)")]
    public float KnockbackDuration = 2f;

    [Header("Arrows Settings")]
    public float ArrowKickForce = 45f;      // Lực bật mỗi lần va chạm (độ)
    public float ArrowMaxAngle = 70f;       // Góc bật tối đa (Clamp)
    public float KickDuration = 0.05f;      // Thời gian bật ra (Rất nhanh)
    public float ReturnDuration = 0.25f;    // Thời gian hồi về (Chậm hơn)

    [Header("Card Spawn Animation")]
    public float DropHeight = 8f;          // Độ cao thả rơi
    public float DropDuration = 0.3f;      // Thời gian rơi 1 card
    public float DelayPerCard = 0.08f;     // Delay giữa các card rơi liên tiếp
    public Ease DropEase = Ease.OutBounce; // Hiệu ứng nảy
    [Range(0.1f, 1f)]
    public float SlowMotionSpeedMultiplier = 0.2f; // Tỷ lệ tốc độ khi spawn card

    [Header("Card Remove Animation")]
    public float TargetGroundY = -0.8f;    // Độ cao mặt đất
    public float RemoveThrowDistance = 3f; // Khoảng cách văng xa
    public float RemoveJumpHeight = 4f;    // Độ cao văng lên
    public float RemoveDuration = 0.5f;    // Thời gian văng

    [Header("Bounce Effect")]
    public float BounceHeight = 0.5f;      // Độ cao nảy lên sau khi chạm đất
    public float BounceDuration = 0.3f;    // Thời gian nảy
    public float BounceSlideDist = 0.5f;   // Trượt thêm 1 chút khi nảy

    [Header("Default Values")]
    public float DefaultForwardSpeed = 5f;
    public float DefaultXLimit = 5f;
    public float DefaultStrafeMultiplier = 0.1f;
    public float DefaultMoveSmoothness = 0.07f;
    public float DefaultTurnDuration = 1.5f;
    public int DefaultTotalSlots = 8;
    public float DefaultRadius = 1.55f;
    public float DefaultLayerHeight = 0.5f;
    public float DefaultUnitHorizontalSpace = 0.8f;
    public float DefaultArrowAngleOffset = 0f;
    public float DefaultKnockbackDistance = 2f;
    public float DefaultKnockbackDuration = 2f;

    private void OnDisable()
    {
        ResetValues();
    }

    public void ResetValues()
    {
        ForwardSpeed = DefaultForwardSpeed;
        XLimit = DefaultXLimit;
        StrafeMultiplier = DefaultStrafeMultiplier;
        MoveSmoothness = DefaultMoveSmoothness;

        TurnDuration = DefaultTurnDuration;

        TotalSlots = DefaultTotalSlots;
        Radius = DefaultRadius;
        LayerHeight = DefaultLayerHeight;

        UnitHorizontalSpace = DefaultUnitHorizontalSpace;

        ArrowAngleOffset = DefaultArrowAngleOffset;

        KnockbackDistance = DefaultKnockbackDistance;
        KnockbackDuration = DefaultKnockbackDuration;
    }
}
