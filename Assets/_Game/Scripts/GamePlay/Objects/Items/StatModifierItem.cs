using System;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.Items
{
    /// <summary>
    /// Danh sách các loại chỉ số trong game (playable dùng subset tùy kịch bản).
    /// </summary>
    public enum StatType : short
    {
        None = 0,
        FireRate,
        FireRange,
        Damage,
        Character,
        MoveSpeed,
        EvolutionPoint,
        CharacterLevel,
        ExplosionShot,
        SwordSkill,
    }

    [Serializable]
    public class StatModifierData
    {
        [Tooltip("Loại Stat")]
        public StatType Type;

        [Tooltip("Giá trị. Dùng số Âm để giảm, Dương để tăng.")]
        public int Value;

        [Tooltip("Giá trị giáp")]
        public int Armor;

        // QUAN TRỌNG: phải virtual để các Data con (CapacityIncreaseFactoryData...) override được
        public virtual void AdjustValue(int amount)
        {
            if (Armor > 0)
            {
                Armor -= 1;
            }
            else
            {
                Value += amount;
            }
        }

        public virtual void ResetValue() { }
    }

    /// <summary>
    /// Playable/Luna-safe:
    /// - Không phụ thuộc GameplayManager
    /// - Khi wheel ăn item -> bắn event để playable script tự xử lý (tăng wheel, spawn card, v.v.)
    /// </summary>
    public abstract class StatModifierItem<TData> : ItemUnit where TData : StatModifierData
    {
        [Header("Data Settings")]
        [SerializeField] public TData Data;

        /// <summary>
        /// Hook cho playable: khi wheel collision xảy ra (item được "apply").
        /// Project playable sẽ subscribe và xử lý theo kịch bản (không cần GameplayManager).
        /// </summary>
        public static event Action<StatModifierData> OnAppliedToWheel;

        public override void Initialize()
        {
            base.Initialize();
            AdjustStatModifierValue();
        }

        protected override void HandleWheelCollision()
        {
            // [FIX] Restore original flow: Call GameplayManager directly
            // The Playable version tried to decouple this, but GameplayManager relies on it.
            if (Data != null)
            {
                GameplayManager.Instance.ChangeStatModifierData(Data);
                OnAppliedToWheel?.Invoke(Data); // Keep event for external listeners if any
            }

            base.HandleWheelCollision();
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            base.HandleNonWheelCollision(source);

            if (source != null)
                AdjustStatModifierValue(source.Damage);
        }

        protected virtual void AdjustStatModifierValue(int value = 0)
        {
            if (Data == null) return;
            Data.AdjustValue(value);
        }
    }
}
