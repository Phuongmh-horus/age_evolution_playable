using System;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public class AttackComponent : BaseComponent, IAttacker
    {
        private static readonly Action<IHitable> NoAttackComplete = _ => { };

        public event Action<IHitable> OnAttackComplete = NoAttackComplete;

        [Header("Attack Config (Active Check)")]
        [SerializeField] protected int damage = 1; // [FIX] Default to 1 (safe) instead of 50 to avoid "x10 damage" bug if value missing
        [SerializeField] protected Vector2 size = Vector2.one;

        [Header("Target Config")]
        [Tooltip("Primary target. Character should use AttackTargetPreset for multiple targets.")]
        [SerializeField] protected EntityType attackTarget = EntityType.Enemy;

        [Tooltip("Use preset for common attack patterns")]
        [SerializeField] protected AttackTargetPreset targetPreset = AttackTargetPreset.Default;

        [Tooltip("Final mask (auto-calculated or manual). Shows combined targets.")]
        [SerializeField] protected uint targetMask;

        [Header("Debug")]
        [SerializeField] protected bool isCustomCaster;
        [SerializeField] protected Transform casterTransform;
        private bool _sizeSanitized;
        private AttackTargetPreset _cachedPreset;
        private EntityType _cachedAttackTarget;
        private EntityType _cachedOwnerType;

        /// <summary>
        /// Preset patterns for common attack configurations
        /// </summary>
        public enum AttackTargetPreset
        {
            Default,          // Use attackTarget field only
            Character,        // Characters attack: Enemy, ResourceTower, CapacityFactory, CapacityGate, PowerGate, FinishTower
            Enemy,            // Enemies attack: Character
            Wheel,            // Wheel attacks: Item, Enemy, ResourceTower, CapacityFactory, CapacityGate
            PlayerProjectile  // Player projectile attacks: Enemy + world targets
        }

        public Vector2 Size => size;
        public int Damage => damage;
        public uint TargetMask => targetMask;

        public Vector3 Position
        {
            get
            {
                if (isCustomCaster && casterTransform != null)
                {
                    return casterTransform.position;
                }
                return CachePosition;
            }
        }



        public override void Initialize()
        {
            base.Initialize();
            OnAttackComplete = NoAttackComplete;

            if (!_sizeSanitized)
            {
                if (size.x >= 1f || size.y >= 1f)
                    size = new Vector2(0.5f, 0.6f);

                _sizeSanitized = true;
            }

            EntityType ownerType = EntityType;
            if (targetMask == 0 ||
                _cachedPreset != targetPreset ||
                _cachedAttackTarget != attackTarget ||
                _cachedOwnerType != ownerType)
            {
                targetMask = CalculateTargetMask(ownerType);
                _cachedPreset = targetPreset;
                _cachedAttackTarget = attackTarget;
                _cachedOwnerType = ownerType;
            }

            // if (targetMask == 0)
            // {
            //     Debug.LogWarning($"[AttackComponent] {gameObject.name} has targetMask=0! Will not hit anything.");
            // }
        }

        public void SetTargetPreset(AttackTargetPreset preset)
        {
            if (targetPreset == preset && targetMask != 0)
                return;

            targetPreset = preset;
            targetMask = GetPresetMask(preset);
            _cachedPreset = preset;
            _cachedAttackTarget = attackTarget;
            _cachedOwnerType = EntityType;
        }

        /// <summary>
        /// Tính toán targetMask từ preset hoặc attackTarget
        /// </summary>
        private uint CalculateTargetMask(EntityType ownerType)
        {


            // Priority 1: Use preset if not Default
            if (targetPreset != AttackTargetPreset.Default)
            {
                return GetPresetMask(targetPreset);
            }

            // Priority 1.5: Safe default for Character units (factory/pillar interactions)
            // If inspector left default attackTarget=Enemy, still allow Character to hit Factory/ResourceTower.
            if (attackTarget == EntityType.Enemy && ownerType == EntityType.Character)
            {
                return GetPresetMask(AttackTargetPreset.Character);
            }

            // Priority 2: Auto-detect based on owner EntityType
            if (attackTarget == EntityType.None && ownerType != EntityType.None)
            {
                AttackTargetPreset preset = AttackTargetPreset.Default;
                if (ownerType == EntityType.Character)
                    preset = AttackTargetPreset.Character;
                else if (ownerType == EntityType.Enemy)
                    preset = AttackTargetPreset.Enemy;
                else if (ownerType == EntityType.Wheel)
                    preset = AttackTargetPreset.Wheel;

                return GetPresetMask(preset);
            }

            // Priority 3: Single target from attackTarget field
            if (attackTarget == EntityType.All)
            {
                return uint.MaxValue;
            }

            int targetVal = (int)attackTarget;
            if (targetVal <= 0 || targetVal >= 32) return 0;

            return 1u << targetVal;
        }

        /// <summary>
        /// Get bitmask for preset attack patterns
        /// </summary>
        private uint GetPresetMask(AttackTargetPreset preset)
        {
            switch (preset)
            {
                case AttackTargetPreset.Character:
                    return (1u << (int)EntityType.Enemy) |
                           (1u << (int)EntityType.ResourceTower) |
                           (1u << (int)EntityType.CapacityFactory) |
                           (1u << (int)EntityType.CapacityGate) |
                           (1u << (int)EntityType.PowerGate) |
                           (1u << (int)EntityType.FinishTower);

                case AttackTargetPreset.Enemy:
                    return (1u << (int)EntityType.Character) |
                           (1u << (int)EntityType.Wheel);


                case AttackTargetPreset.PlayerProjectile:
                    return (1u << (int)EntityType.Enemy) |
                           (1u << (int)EntityType.ResourceTower) |
                           (1u << (int)EntityType.CapacityFactory) |
                           (1u << (int)EntityType.CapacityGate) |
                           (1u << (int)EntityType.PowerGate) |
                           (1u << (int)EntityType.FinishTower) |
                           (1u << (int)EntityType.GateNewEra);

                default:
                    return 0u;
            }
        }

        public void OnAttackSucceed(IHitable target)
        {
            OnAttackComplete?.Invoke(target);
        }

        public void Setup(int dam)
        {
            damage = dam;
        }

    }
}
