using System;
using System.Collections.Generic;
using GamePlay.AnimationSystems;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Entities;
using GamePlay.OscillationSystems;
using UnityEngine;

namespace GamePlay.Items
{
    /// <summary>
    /// Base class cho tất cả Item trong gameplay.
    /// - Implement IAttacker để tương tác với hệ thống hit/collision.
    /// - Implement IComponent để được quản lý bởi component system (nếu có).
    /// 
    /// Không phụ thuộc Alchemy/KBCore/... (phù hợp build playable/Luna).
    /// </summary>
    public class ItemUnit : PoolEntity, IAttacker, IComponent, IHitable
    {
        [Header("Attack Settings")]
        [SerializeField] private int damage = 0;

        // Unity LayerMask (Inspector). IAttacker yêu cầu uint => convert từ value.
        [SerializeField] private LayerMask targetMask = ~0;

        [Tooltip("Kích thước dùng cho tính toán hit/overlap (nếu hệ thống cần).")]
        [SerializeField] private Vector2 size = Vector2.one;

        [Header("Collision Config (Strict Logic)")]
        [SerializeField] private ShapeType shapeType = ShapeType.Box;
        [SerializeField] protected Vector3 colliderOffsets = Vector3.zero;
        [SerializeField] protected Vector3 colliderSize = Vector3.one;



        [Header("Offset Properties")]
        public float LeftOffset;
        public float RightOffset;

        [Header("Death Scale Effect")]
        [SerializeField] public DeathScaleEffect deathScaleEffect;

        [Header("Tutorial Element")]
        [SerializeField] protected GamePlayTutElement _tutElement;

        [Header("Runtime Fallbacks")]
        [SerializeField] protected bool autoAddHitTextFlyEffectAtRuntime = true;

        private bool _initDataFirst;

        // IAttacker event
        public event Action<IHitable> OnAttackComplete;

        // IHitable event
        public event Action<IAttacker> OnHitComplete;
        public bool IsActive => isActiveAndEnabled;

        public GamePlay.ComponentSystems.ColliderData GetColliderData()
        {
            // [FIX] Apply Entity Scale AND Rotation to Collider Size (AABB)
            Vector3 worldScale = transform.lossyScale;
            Quaternion rot = transform.rotation;

            // 1. Calculate Local Scaled Half-Extents
            Vector3 localHalfExtents = new Vector3(
                colliderSize.x * Mathf.Abs(worldScale.x) * 0.5f,
                colliderSize.y * Mathf.Abs(worldScale.y) * 0.5f,
                colliderSize.z * Mathf.Abs(worldScale.z) * 0.5f
            );

            // 2. Rotate Local Extents to World Space AABB Extents
            // For a Box with half-extents (hx, hy, hz) and rotation matrix M:
            // WorldExtents.x = |Mxx * hx| + |Mxy * hy| + |Mxz * hz|
            Matrix4x4 m = Matrix4x4.Rotate(rot);
            Vector3 worldHalfExtents = new Vector3(
               Mathf.Abs(m.m00 * localHalfExtents.x) + Mathf.Abs(m.m01 * localHalfExtents.y) + Mathf.Abs(m.m02 * localHalfExtents.z),
               Mathf.Abs(m.m10 * localHalfExtents.x) + Mathf.Abs(m.m11 * localHalfExtents.y) + Mathf.Abs(m.m12 * localHalfExtents.z),
               Mathf.Abs(m.m20 * localHalfExtents.x) + Mathf.Abs(m.m21 * localHalfExtents.y) + Mathf.Abs(m.m22 * localHalfExtents.z)
            );

            return new GamePlay.ComponentSystems.ColliderData
            {
                Type = shapeType,
                Size = worldHalfExtents,
                Offset = shapeType == ShapeType.Box ? colliderSize.z : colliderSize.x,
                CategoryBits = (uint)(1 << (int)EntityType)
            };
        }

        private void ConfigureCollider()
        {
            // --- COMBAT SYSTEM: Custom Collision Logic Only ---
            // We do NOT use Unity Physics (Rigidbody/Collider).
            // Logic validation primarily happens via GetColliderData().
        }

#if UNITY_EDITOR
        protected virtual void OnDrawGizmosSelected()
        {
            // Visualize the ACTUAL CombatSystem Collider (Cyan)
            // LOGIC MATCH: Visualize the AABB that GetColliderData returns.
            Gizmos.color = Color.cyan;
            Gizmos.matrix = Matrix4x4.identity; // World Space

            Vector3 worldScale = transform.lossyScale;
            Quaternion rot = transform.rotation;

            // 1. Local Scaled Half-Extents
            Vector3 localHalfExtents = new Vector3(
                colliderSize.x * Mathf.Abs(worldScale.x) * 0.5f,
                colliderSize.y * Mathf.Abs(worldScale.y) * 0.5f,
                colliderSize.z * Mathf.Abs(worldScale.z) * 0.5f
            );

            // 2. Rotate to World AABB
            Matrix4x4 m = Matrix4x4.Rotate(rot);
            Vector3 worldHalfExtents = new Vector3(
               Mathf.Abs(m.m00 * localHalfExtents.x) + Mathf.Abs(m.m01 * localHalfExtents.y) + Mathf.Abs(m.m02 * localHalfExtents.z),
               Mathf.Abs(m.m10 * localHalfExtents.x) + Mathf.Abs(m.m11 * localHalfExtents.y) + Mathf.Abs(m.m12 * localHalfExtents.z),
               Mathf.Abs(m.m20 * localHalfExtents.x) + Mathf.Abs(m.m21 * localHalfExtents.y) + Mathf.Abs(m.m22 * localHalfExtents.z)
            );

            // 3. Draw AABB (Size = Extents * 2)
            if (shapeType == ShapeType.Box)
            {
                Gizmos.DrawWireCube(transform.position, worldHalfExtents * 2f);
            }
            else if (shapeType == ShapeType.Sphere)
            {
                // For Sphere, standard gizmo is fine, or AABB of sphere
                Gizmos.DrawWireSphere(transform.position, worldHalfExtents.x); // Radius
            }
        }
#endif

        public void OnHit(IAttacker source)
        {
            HandleHitComplete(source);
            OnHitComplete?.Invoke(source);
        }

        #region IAttacker

        /// <summary>
        /// Base item không ép EntityType.Item vì enum project bạn không có.
        /// Class con có thể override để trả về đúng type cần dùng.
        /// </summary>
        /// <summary>
        /// Base item now correctly returns the EntityType assigned in PoolEntity (via Inspector or Start).
        /// </summary>
        public virtual EntityType EntityType => _entityType;

        public virtual Vector2 Size => size;

        public virtual int Damage => damage;

        public virtual uint TargetMask => (uint)targetMask.value;

        // IAttacker cần Position
        public virtual Vector3 Position => transform.position;

        public virtual void Setup(int newDamage)
        {
            damage = newDamage;
        }

        public virtual void OnAttackSucceed(IHitable target)
        {
            // báo cho hệ thống biết attack xong
            OnAttackComplete?.Invoke(target);
        }

        #endregion

        #region IComponent

        public Transform Transform => transform;

        public virtual bool IsEnabled => isActiveAndEnabled;

        public virtual void OnUpdate(float dt)
        {
            // mặc định không làm gì
        }

        public virtual void Dispose()
        {
            // mặc định không làm gì
        }

        #endregion

        #region Unity Messages

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            ConfigureCollider(); // Updates Collider in Editor immediately
        }
#endif

        #endregion

        #region Component System

        protected virtual void InitComponent()
        {
            ConfigureCollider(); // Ensures Runtime setup
            BuildCapabilityPack();

            // [FIX] ItemUnit implements IHitable itself. If no separate Hitable component was found, use \'this\'.
            if (Pack.Hitable == null && this is IHitable selfHitable)
            {
                Pack.Hitable = selfHitable;
                ActiveFlags |= CapabilityFlags.Hit;
            }
        }

        public virtual void Initialize()
        {
            InitComponent();

            if (_tutElement) _tutElement.Initialize();

            // FIX: Prevent infinite recursion if Pack.Hitable is 'this' (ItemUnit/Pillar)
            if ((ActiveFlags & CapabilityFlags.Hit) != 0 && Pack.Hitable != (object)this)
                Pack.Hitable.Initialize();

            if ((ActiveFlags & CapabilityFlags.Heal) != 0) Pack.Healable.Initialize();
            if ((ActiveFlags & CapabilityFlags.Animator) != 0) Pack.Animator.Initialize();

            Pack.Oscillator?.Initialize();
            if ((ActiveFlags & CapabilityFlags.Effector) != 0) Pack.Effector.Initialize();

            bool hasItemOffsets = !Mathf.Approximately(LeftOffset, 0f) || !Mathf.Approximately(RightOffset, 0f);

            // Restore factory/pillar oscillation (original game behavior)
            if ((ActiveFlags & CapabilityFlags.Oscillate) != 0 &&
                Pack.Oscillator != null &&
                (hasItemOffsets ||
                 !Mathf.Approximately(Pack.Oscillator.LeftOffset, 0f) ||
                 !Mathf.Approximately(Pack.Oscillator.RightOffset, 0f)))
            {
                OscillationSystem.Register(Transform, Pack, ActiveFlags);
            }

            // Ref Restored: Register to CollisionSystem (Manual Physics)
            // [FIX] ORIGINAL GAME FLOW: Always use ItemUnit.Transform (this.transform), NOT HitComponent.transform!
            // GameplayManager.StartGame() does: transforms.Add(g.Transform) where g is ItemUnit
            if ((ActiveFlags & CapabilityFlags.Hit) != 0 && Pack.Hitable != null)
            {
                // [DEBUG] Log which IHitable is being registered and its collider data
                var colData = Pack.Hitable.GetColliderData();
                bool isHitComponent = Pack.Hitable is HitComponent;
                // CRITICAL: Use this.transform (ItemUnit root), NOT HitComponent.transform
                CollisionSystem.Register(Pack.Hitable, transform);
            }

            RegisterEvents(true);
            WarmupHitTextRuntimeCache();
        }

        #endregion

        #region Events

        protected void RegisterEvents(bool register)
        {
            if (register)
            {
                if (Pack.Hitable != null) Pack.Hitable.OnHitComplete += HandleHitComplete;
                if (Pack.Healable != null) Pack.Healable.OnHealthChange += HandleHealthChange;
            }
            else
            {
                if (Pack.Hitable != null) Pack.Hitable.OnHitComplete -= HandleHitComplete;
                if (Pack.Healable != null) Pack.Healable.OnHealthChange -= HandleHealthChange;
            }
        }

        private int _lastHitFrame = -1;
        private IAttacker _lastAttacker;
        private HitTextFlyEffect _hitTextFlyEffect;

        protected virtual void HandleHitComplete(IAttacker source)
        {
            if (source == null) return;

            // Debounce only Wheel collisions to avoid duplicate trigger bugs.
            // For Character/Projectile hits, multiple attacks can land in the same frame
            // and must all be processed.
            if (source.EntityType == GamePlay.Entities.EntityType.Wheel)
            {
                if (_lastHitFrame == Time.frameCount && _lastAttacker == source)
                    return;

                _lastHitFrame = Time.frameCount;
                _lastAttacker = source;
            }

            // Debug.Log($"[ItemUnit] {gameObject.name} HandleHitComplete from {source.EntityType}");

            if (source.EntityType == GamePlay.Entities.EntityType.Wheel)
            {
                HandleWheelCollision();
            }
            else
            {
                // Debug.Log($"[ItemUnit] {gameObject.name} → HandleNonWheelCollision()");
                HandleNonWheelCollision(source);
            }
        }

        protected virtual void HandleHealthChange(int current, int max)
        {
            if (current <= 0)
            {
                DespawnInterval();
            }
        }

        #endregion

        #region Gameplay API (virtual để class con override được)

        /// <summary>
        /// Item bị bánh xe đâm.
        /// </summary>
        protected virtual void HandleWheelCollision()
        {
            DespawnInterval();
        }

        /// <summary>
        /// Item bị thứ khác đâm (enemy/character/weapon...).
        /// </summary>
        protected virtual void HandleNonWheelCollision(IAttacker source)
        {
            // Damage text should come from HealthComponent.OnTakeDamaged to avoid double popups.
            Pack.Healable?.TakeDamage(source);
        }

        /// <summary>
        /// Cleanup và despawn item.
        /// Class con override để thêm logic cleanup riêng.
        /// </summary>
        protected virtual void DespawnInterval()
        {
            RegisterEvents(false);

            // Unregister oscillation if it was enabled
            if ((ActiveFlags & CapabilityFlags.Oscillate) != 0 &&
                Pack.Oscillator != null &&
                (!Mathf.Approximately(Pack.Oscillator.LeftOffset, 0f) ||
                 !Mathf.Approximately(Pack.Oscillator.RightOffset, 0f) ||
                 !Mathf.Approximately(LeftOffset, 0f) ||
                 !Mathf.Approximately(RightOffset, 0f)))
            {
                OscillationSystem.Unregister(Pack.Oscillator);
            }

            // Ref Restored: Unregister from CollisionSystem
            if ((ActiveFlags & CapabilityFlags.Hit) != 0 && Pack.Hitable != null)
            {
                CollisionSystem.Unregister(Pack.Hitable);
            }

            if ((ActiveFlags & CapabilityFlags.Hit) != 0) Pack.Hitable.Dispose();
            if ((ActiveFlags & CapabilityFlags.Heal) != 0) Pack.Healable.Dispose();
            if ((ActiveFlags & CapabilityFlags.Animator) != 0) Pack.Animator.Dispose();
            if ((ActiveFlags & CapabilityFlags.Oscillate) != 0) Pack.Oscillator.Dispose();
            if ((ActiveFlags & CapabilityFlags.Effector) != 0) Pack.Effector.Dispose();

            if (deathScaleEffect != null)
            {
                deathScaleEffect.PlayDeathEffect(onComplete: Despawn);
            }
            else
            {
                Despawn();
            }
        }

        #endregion

        private HitTextFlyEffect GetHitTextFlyEffect()
        {
            if (_hitTextFlyEffect != null) return _hitTextFlyEffect;
            if (!TryGetComponent(out _hitTextFlyEffect))
                _hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);

            if (_hitTextFlyEffect == null && autoAddHitTextFlyEffectAtRuntime && Application.isPlaying)
                _hitTextFlyEffect = gameObject.AddComponent<HitTextFlyEffect>();
            return _hitTextFlyEffect;
        }

        private void WarmupHitTextRuntimeCache()
        {
            var hitText = GetHitTextFlyEffect();
            if (hitText == null) return;
            hitText.WarmupRuntimeCaches();
        }
    }
}
