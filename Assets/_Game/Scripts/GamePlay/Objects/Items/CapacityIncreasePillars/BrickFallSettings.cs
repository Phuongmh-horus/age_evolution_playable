using System;
using UnityEngine;
using GamePlay.ComponentSystems;
using GamePlay.Items;

[CreateAssetMenu(fileName = "BrickFallSettings", menuName = "Game/Brick Fall Settings")]
public class BrickFallSettings : ScriptableObject
{
    [Header("Fall Settings - Fixed Duration")]
    [SerializeField, Tooltip("Thời gian cố định cho phase nảy trên mặt đất")]
    private float bounceOnGroundDuration = 0.2f;
    [SerializeField, Tooltip("Số lần nảy trên đất")]
    private int bounceCount = 1;
    [SerializeField, Tooltip("Độ cao của bounce (sẽ giảm dần)")]
    private float bounceHeight = 0.25f;
    [SerializeField, Tooltip("Tỷ lệ giảm độ cao mỗi lần nảy")]
    [Range(0.3f, 0.8f)]
    private float bounceDamping = 0.5f;
    [SerializeField, Tooltip("Khoảng cách ngang gạch sẽ rơi")]
    private float horizontalDistance = 3f;
    [SerializeField, Tooltip("Độ cao tối đa của arc khi rơi")]
    private float fallArcHeight = 1.5f;
    [SerializeField] private float groundY = 0f;
    [SerializeField, Range(0.1f, 2f)] private float launchDistanceMultiplier = 0.5f;

    [Header("Height-Based Distance Scaling")]
    [SerializeField, Tooltip("Chiều cao tối đa của pillar (dùng để tính tỷ lệ)")]
    private float maxPillarHeight = 10f;
    [SerializeField, Range(0f, 1f), Tooltip("Tỷ lệ khoảng cách tối thiểu cho gạch thấp nhất (0.3 = 30% khoảng cách)")]
    private float minDistanceRatio = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Tỷ lệ khoảng cách tối đa cho gạch cao nhất (1.0 = 100% khoảng cách)")]
    private float maxDistanceRatio = 1f;
    [SerializeField, Range(0f, 0.5f), Tooltip("Khoảng cách thêm khi nảy ra (% của khoảng cách ban đầu)")]
    private float bounceOutwardRatio = 0.25f;

    [Header("Rotational Push")]
    [SerializeField] private Vector2 tiltSpeedRange = new Vector2(120f, 220f); // deg/s around side axis
    [SerializeField] private Vector2 spinSpeedRange = new Vector2(60f, 140f); // deg/s around up axis
    [SerializeField] private bool randomizeTiltDirection = true;

    [Header("Fly to Capacity Bar")]
    [SerializeField] private float flyDuration = 0.5f;
    [SerializeField] private float flyArcHeight = 2f; // height of the arc curve
    [SerializeField] private float flyScaleDownDuration = 0.3f; // duration for scale down while flying (slower = longer)
    [SerializeField, Range(0f, 0.3f), Tooltip("Random time offset range for fly duration (±offset)")]
    private float flyDurationOffset = 0.1f;

    [Header("Capacity progress per brick")]
    [SerializeField] private int capacityPerBrick = 1;
    [SerializeField] private StatModifierCapacityData capacityData;

    [Header("Total Motion Duration (Read Only)")]
    [SerializeField, Tooltip("Tổng thời gian thực hiện toàn bộ motion (tự động tính)")]
    private float totalMotionDuration;

    // Public properties for accessing settings
    public float BounceOnGroundDuration => bounceOnGroundDuration;
    public int BounceCount => bounceCount;
    public float BounceHeight => bounceHeight;
    public float BounceDamping => bounceDamping;
    public float HorizontalDistance => horizontalDistance;
    public float FallArcHeight => fallArcHeight;
    public float GroundY => groundY;
    public float LaunchDistanceMultiplier => launchDistanceMultiplier;

    public float MaxPillarHeight => maxPillarHeight;
    public float MinDistanceRatio => minDistanceRatio;
    public float MaxDistanceRatio => maxDistanceRatio;
    public float BounceOutwardRatio => bounceOutwardRatio;

    public Vector2 TiltSpeedRange => tiltSpeedRange;
    public Vector2 SpinSpeedRange => spinSpeedRange;
    public bool RandomizeTiltDirection => randomizeTiltDirection;

    public float FlyDuration => flyDuration;
    public float FlyArcHeight => flyArcHeight;
    public float FlyScaleDownDuration => flyScaleDownDuration;
    public float FlyDurationOffset => flyDurationOffset;

    public StatModifierCapacityData CapacityData => capacityData;
    public float TotalMotionDuration => totalMotionDuration;

    private void OnValidate()
    {
        if (capacityData == null)
        {
            capacityData = new StatModifierCapacityData();
        }

        // Keep brick reward bound to capacity progression even if StatType enum order changes.
        capacityData.Type = StatType.EvolutionPoint;
        capacityData.Value = capacityPerBrick;
        capacityData.Armor = 0;

        // Total motion time = max possible fall time + fixed bounce duration + fly duration
        // Max fall time assumes falling from highest arc
        float maxFallTime = Mathf.Sqrt(2f * fallArcHeight / 9.8f); // approximate
        totalMotionDuration = maxFallTime + bounceOnGroundDuration + flyDuration;
    }
}
