using UnityEngine;
using GamePlay.Effects;
using GamePlay.Weapons;

namespace CardSystem.Data
{
    public enum BuffStatType
    {
        None = 0,
        Damage = 1,
        Critical = 2,
        FireRate = 3,
        Income = 4,
        CritDamage = 5
    }

    /// <summary>
    /// ScriptableObject định nghĩa cấu hình một buff/shot in-game.
    /// Dùng cho các hiệu ứng như Electric/Explosive/Piercing theo design spec.
    /// </summary>
    [CreateAssetMenu(fileName = "BuffDefinition", menuName = "CardSystem/BuffDefinition", order = 2)]
    public class BuffDefinition : ScriptableObject
    {
        [Header("Identity")]
        public Sprite Icon;
        public string BuffId;
        public string DisplayName;
        public BuffStatType StatType;
        public bool Enabled = true;
        public int Priority = 100;

        [Header("Core Config")]
        [Tooltip("Giá trị % chính (damage %, chance %, dot %, ... tuỳ EffectType)")]
        public float ValuePercent = 100f;

        [Tooltip("Giá trị % phụ (dmg AOE/pulse lan ra, giảm dần mỗi bounce, ... tuỳ EffectType). Không ảnh hưởng DoT nếu DotPercent > 0.")]
        public float SecondaryPercent;

        [Tooltip("Damage % mỗi tick DoT (0 = dùng SecondaryPercent làm fallback)")]
        public float DotPercent;

        [Tooltip("Tỉ lệ kích hoạt (0-100)")]
        public float ChancePercent = 100f;

        [Tooltip("Bán kính ảnh hưởng (0 = không dùng)")]
        public float Radius;

        [Tooltip("Thời lượng hiệu ứng theo giây (0 = tức thời)")]
        public float Duration;

        [Tooltip("Khoảng interval tick theo giây cho DoT (0 = không tick)")]
        public float Interval = 1f;

        [Tooltip("Số mục tiêu tối đa bị ảnh hưởng (0 = không giới hạn)")]
        public int MaxTargets = 1;

        [Tooltip("Số lần xuyên thêm (chỉ dùng cho PiercingShot)")]
        public int MaxPenetrations;

        [Header("Stacking")]
        [Tooltip("Số stack tối đa (>= 1)")]
        public int MaxStacks = 1;

        [Tooltip("Khi re-apply thì refresh thời gian hiệu ứng")]
        public bool RefreshDurationOnReapply = true;

        [Tooltip("Cho phép áp dụng cả khi projectile xuyên mục tiêu")]
        public bool ApplyOnPiercingHit = true;

        [Header("Sword Skill Config (Samurai)")]
        [Tooltip("Cấu hình kiếm — để null nếu skill này không phải kiếm Samurai")]
        public SamuraiSkillConfigSO SamuraiConfig;

        [Header("Hero Skill Config")]
        [Tooltip("0 = xuất hiện ở tất cả cổng. 1 = chỉ cổng 1. 2 = chỉ cổng 2. GD cấu hình theo spec.")]
        public int GateIndex = 0;

        [Header("Visual")]
        public GameObject VisualPrefab;

        public WeaponUnit AssociatedWeapon; // vũ khí liên quan (nếu có)

#if UNITY_EDITOR
        [Header("Notes")]
        [TextArea(2, 6)]
        public string Notes;
#endif
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BuffId))
            {
                Debug.LogWarning($"BuffDefinition {name} has empty BuffId");
            }

            if (StatType == BuffStatType.None)
            {
                Debug.LogWarning($"BuffDefinition {name} is StatModifier but StatType is None");
            }

            if (MaxStacks < 1)
            {
                MaxStacks = 1;
            }

            ChancePercent = Mathf.Clamp(ChancePercent, 0f, 100f);

            if (MaxTargets < 0)
            {
                MaxTargets = 0;
            }

            if (MaxPenetrations < 0)
            {
                MaxPenetrations = 0;
            }

            if (Interval < 0f)
            {
                Interval = 0f;
            }

            if (Duration < 0f)
            {
                Duration = 0f;
            }

            if (Radius < 0f)
            {
                Radius = 0f;
            }
        }

        private void OnValidate()
        {
            Validate();
        }
    }
}
