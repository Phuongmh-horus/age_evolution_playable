using System;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public class AttackComponent : BaseComponent, IAttacker
    {
        public event Action<IHitable> OnAttackComplete = delegate { };

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

        public static string TargetMaskPropertyName => nameof(targetMask);

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            // Always recalculate in editor to ensure consistency
            targetMask = CalculateTargetMask();
        }
#endif

        public override void Initialize()
        {
            base.Initialize();
            OnAttackComplete = delegate { };

            // [FIX] Luna serialization issue: size may be default Vector2.one if not deserialized properly
            // Clamp size to reasonable values for weapon projectiles
            if (size.x >= 1f || size.y >= 1f)
            {
                // Default size is likely not deserialized correctly, use safe fallback
                size = new Vector2(0.5f, 0.6f);
            }

            // Runtime: Always calculate targetMask based on preset/target
            targetMask = CalculateTargetMask();

            if (targetMask == 0)
            {
                Debug.LogWarning($"[AttackComponent] {gameObject.name} has targetMask=0! Will not hit anything.");
            }
        }

        public void SetTargetPreset(AttackTargetPreset preset)
        {
            targetPreset = preset;
            targetMask = GetPresetMask(preset);
        }

        /// <summary>
        /// Tính toán targetMask từ preset hoặc attackTarget
        /// </summary>
        private uint CalculateTargetMask()
        {
            // Ensure poolEntity is available at runtime (weapon or child objects can miss cache)
            if (poolEntity == null)
            {
                poolEntity = GetComponentInParent<GamePlay.Entities.PoolEntity>();
            }

            // Priority 1: Use preset if not Default
            if (targetPreset != AttackTargetPreset.Default)
            {
                return GetPresetMask(targetPreset);
            }
            
            // Priority 1.5: Safe default for Character units (factory/pillar interactions)
            // If inspector left default attackTarget=Enemy, still allow Character to hit Factory/ResourceTower.
            if (attackTarget == EntityType.Enemy && EntityType == EntityType.Character)
            {
                return GetPresetMask(AttackTargetPreset.Character);
            }
            
            // Priority 2: Auto-detect based on owner EntityType
            if (attackTarget == EntityType.None && EntityType != EntityType.None)
            {
                AttackTargetPreset preset = AttackTargetPreset.Default;
                if (EntityType == EntityType.Character)
                    preset = AttackTargetPreset.Character;
                else if (EntityType == EntityType.Enemy)
                    preset = AttackTargetPreset.Enemy;
                else if (EntityType == EntityType.Wheel)
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

                case AttackTargetPreset.Wheel:
                    return (1u << (int)EntityType.Item) |
                           (1u << (int)EntityType.Enemy) |
                           (1u << (int)EntityType.ResourceTower) |
                           (1u << (int)EntityType.CapacityFactory) |
                           (1u << (int)EntityType.CapacityGate) |
                           (1u << (int)EntityType.FinishTower);

                case AttackTargetPreset.PlayerProjectile:
                    return (1u << (int)EntityType.Enemy) |
                           (1u << (int)EntityType.ResourceTower) |
                           (1u << (int)EntityType.CapacityFactory) |
                           (1u << (int)EntityType.CapacityGate) |
                           (1u << (int)EntityType.PowerGate) |
                           (1u << (int)EntityType.FinishTrigger) |
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

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;

            // Sử dụng logic vị trí tương tự như thuộc tính Position
            Vector3 casterPosition = CachePosition;
            Quaternion casterRotation = CacheRotation;

            if (isCustomCaster && casterTransform != null)
            {
                casterPosition = casterTransform.position;
                casterRotation = casterTransform.rotation;
            }

            // Trong logic cũ: cylinder đi từ p.y đến p.y + 2*h => tâm ở p.y + h
            Vector3 center = casterPosition + new Vector3(0, size.y, 0);

            // Mesh cylinder mặc định: radius=0.5, height=2
            // radius=size.x => scale XZ = size.x /0.5 = size.x*2
            // height = size.y*2 => scale Y = (size.y*2)/2 = size.y
            Vector3 scale = new Vector3(size.x * 2f, size.y, size.x * 2f);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, casterRotation, scale);

            Gizmos.DrawWireMesh(GetCylinderMesh());

            Gizmos.color = new Color(0, 1, 1, 0.3f);
            Gizmos.DrawMesh(GetCylinderMesh());

            Gizmos.matrix = old;
        }

        private Mesh _cylinderMesh;
        private Mesh GetCylinderMesh()
        {
            if (_cylinderMesh == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _cylinderMesh = go.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(go);
            }
            return _cylinderMesh;
        }
#endif
    }
}
