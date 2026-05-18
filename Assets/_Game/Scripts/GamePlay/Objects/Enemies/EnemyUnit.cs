using System;
using GamePlay.AnimationSystems;
using GamePlay.CombatSystems;
using GamePlay.Entities;
using GamePlay.Items;
using GamePlay.Weapons;
using GamePlay.HealthSystems;
using GamePlay.ComponentSystems;
using GamePlay.Effects;
using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Enemies
{
    public class EnemyUnit : ItemUnit
    {
        public WeaponUnit WeaponPrefab;
        public Transform HandTransform;

        [SerializeField] private SpriteRenderer hpBarRenderer;
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;

        [Header("HP Bar Settings")]
        [SerializeField] private int defaultMaxHealth = 3;

        [Header("Death VFX")]
        [SerializeField] private GameObject dieVfxPrefab;
        [SerializeField] private Vector3 dieVfxOffset = Vector3.zero;
        [SerializeField] private float dieVfxLifetime = 1.2f;
        [SerializeField] private int maxDeathVfxPerFrame = 8;
        private Renderer _mainRenderer;
        private static int s_lastDeathVfxFrame = -1;
        private static int s_deathVfxCountInFrame = 0;
        private static readonly Dictionary<int, TimedAutoDisable> s_timedAutoDisableCache = new Dictionary<int, TimedAutoDisable>(128);

        private WeaponUnit _currentWeapon;
        private bool _despawnHandled;
        private bool _initialized; // [FIX] Prevent double initialization in Luna
        private HealthComponent _healthComponent;
        private Vector3 _originalBarScale;
        private Vector3 _originalLocalPos;
        [SerializeField, HideInInspector] private bool _healthOverriddenFromContent;

#if UNITY_EDITOR
        // Không override để tránh lỗi nếu ItemUnit.OnValidate() không virtual
        private new void OnValidate()
        {
            if (HandTransform == null)
            {
                HandTransform = FindChildContains(transform, "WeaponHolder");
            }

            // [FIX] Auto-set EntityType for Enemy if not already set
            if (_entityType == EntityType.None)
            {
                _entityType = EntityType.Enemy;
            }

            EnsureHitTextEffect(false);
        }
#endif

        private void Awake()
        {
            // [FIX] Ensure EntityType is Enemy at runtime
            if (_entityType == EntityType.None)
            {
                _entityType = EntityType.Enemy;
            }
        }

        private static Transform FindChildContains(Transform root, string contains)
        {
            if (root == null) return null;

            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name != null && c.name.Contains(contains))
                    return c;

                var sub = FindChildContains(c, contains);
                if (sub != null) return sub;
            }

            return null;
        }

        private void OnDisable()
        {
            // Ensure weapon cleanup happens when enemy is returned to pool / disabled.
            DespawnInterval();
        }

        public override void Initialize()
        {
            // [FIX] Prevent double initialization in Luna
            if (_initialized) return;
            _initialized = true;

            base.Initialize();

            // giữ nguyên logic register
            if (EnemyManager.Instance != null)
                EnemyManager.Instance.RegisterEnemy(this);

            EnsureAnimatorComponent();
            _healthComponent = Pack.Healable as HealthComponent;
            EnsureHealthComponent();
            EnsureHitTextEffect(false);
            _despawnHandled = false;

            if (hitTextFlyEffect != null)
                hitTextFlyEffect.enabled = true;

            if (hpBarRenderer != null)
            {
                hpBarRenderer.gameObject.SetActive(true);
                hpBarRenderer.enabled = true; 
                hpBarRenderer.sortingOrder = 50; 

                // Cache Scale FIRST
                _originalBarScale = hpBarRenderer.transform.localScale;
                
                // Initialize Visuals
                if (_healthComponent != null)
                    UpdateImage(_healthComponent.CurrentHealth, _healthComponent.MaxHealth);
                else
                    UpdateImage(defaultMaxHealth, defaultMaxHealth); 
            }
        }

        protected override void DespawnInterval()
        {
            // Debug.Log($"[EnemyUnit] DespawnInterval called on {name}. Handled? {_despawnHandled}");
            if (_despawnHandled) return;
            _despawnHandled = true;

            // [FIX] Reset initialization flag for pool reuse
            _initialized = false;

            if (EnemyManager.Instance != null)
                EnemyManager.Instance.UnregisterEnemy(this);

            if (_healthComponent != null)
            {
                _healthComponent.OnHealthChange -= HandleHealthChange;
            }

            base.DespawnInterval();
        }

        // ...

        // [FIX] Play VFX on Wheel Collision (Instant Death)
        protected override void HandleWheelCollision()
        {
            PlayDeathVfx();
            base.HandleWheelCollision();
        }

        public void PlayAnimation(AnimationType animationType, float waitForAction = 0.5f, Action onComplete = null)
        {
            Pack.Animator?.PlayAnimation(animationType, waitForAction, onComplete);
        }

        public void AttachWeapon(WeaponUnit weaponUnit)
        {
            _currentWeapon = weaponUnit;
        }

        public void ThrowWeapon()
        {
            _currentWeapon = null;
        }

        protected override void HandleHealthChange(int current, int max)
        {
            // Debug.Log($"[EnemyUnit] HandleHealthChange: {current}/{max}");
            UpdateImage(current, max);

            // [FIX] Spawn VFX ONLY on death (Health <= 0)
            if (current <= 0)
            {
                // Debug.Log($"[EnemyUnit] Health <= 0. Triggering PlayDeathVfx on {name}");
                PlayDeathVfx();
            }

            base.HandleHealthChange(current, max);
        }

        private void UpdateImage(int currentHealth, int maxHealth)
        {
            if (hpBarRenderer == null) return;

            // [FIX] Simple Transform Scaling with Pivot Correction
            float healthPercent = maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
            
            // Capture original state
            if (_originalBarScale == Vector3.zero) 
            {
                _originalBarScale = hpBarRenderer.transform.localScale;
                _originalLocalPos = hpBarRenderer.transform.localPosition;
            }

            Vector3 targetScale = _originalBarScale;
            targetScale.x *= healthPercent;
            
            hpBarRenderer.transform.localScale = targetScale;

            // [FIX] Compensate for Center Pivot (Sprite shrinks from both sides)
            // Shift position LEFT to keep the left edge stationary.
            // Formula: Shift = (NewScale - OldScale) * Width * 0.5
            if (hpBarRenderer.sprite != null)
            {
                float spriteWidth = hpBarRenderer.sprite.bounds.size.x;
                float scaleDiff = targetScale.x - _originalBarScale.x; // Negative when shrinking
                float shift = scaleDiff * spriteWidth * 0.5f;
                
                // [FIX] Inverted direction per user request ("Đảo lại đi")
                // Current: Move Right (shift is negative, so -shift is positive).
                hpBarRenderer.transform.localPosition = _originalLocalPos - new Vector3(shift, 0, 0);
            }
        }

        private void EnsureHealthComponent()
        {
            if (Pack.Healable != null) return;

            _healthComponent = GetComponentInChildren<HealthComponent>(true);
            if (_healthComponent == null)
            {
                _healthComponent = gameObject.AddComponent<HealthComponent>();
            }

            if (defaultMaxHealth > 0 && !_healthOverriddenFromContent)
            {
                _healthComponent.SetMaxHealth(defaultMaxHealth, refill: true);
            }

            Pack.Healable = _healthComponent;
            ActiveFlags |= CapabilityFlags.Heal;

            _healthComponent.Initialize();
            _healthComponent.OnHealthChange += HandleHealthChange;
        }

        public void MarkHealthOverriddenFromContent()
        {
            _healthOverriddenFromContent = true;
        }

        private void EnsureAnimatorComponent()
        {
            if (Pack.Animator != null) return;
            var monos = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < monos.Length; i++)
            {
                if (monos[i] is IAnimator animator)
                {
                    Pack.Animator = animator;
                    ActiveFlags |= CapabilityFlags.Animator;
                    animator.Initialize();
                    break;
                }
            }
        }

        private void PlayDeathVfx()
        {
            if (dieVfxPrefab == null)
                 return;
            if (!CanSpawnDeathVfxThisFrame())
                return;

            Vector3 spawnPos = transform.position + dieVfxOffset;
            GameObject vfx = PoolManager.Instance != null ? PoolManager.Instance.Get(dieVfxPrefab) : Instantiate(dieVfxPrefab);
            if (vfx == null) return;

            vfx.transform.position = spawnPos;
            vfx.transform.rotation = Quaternion.identity;
            vfx.SetActive(true);

            var autoDisable = GetOrAddTimedAutoDisable(vfx);
            if (autoDisable == null) return;
            autoDisable.Play(Mathf.Max(0.05f, dieVfxLifetime));
        }

        private static TimedAutoDisable GetOrAddTimedAutoDisable(GameObject vfxObject)
        {
            if (vfxObject == null) return null;

            int key = vfxObject.GetInstanceID();
            if (s_timedAutoDisableCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            if (!vfxObject.TryGetComponent(out cached))
                cached = vfxObject.AddComponent<TimedAutoDisable>();

            s_timedAutoDisableCache[key] = cached;
            return cached;
        }

        private bool CanSpawnDeathVfxThisFrame()
        {
            if (Time.frameCount != s_lastDeathVfxFrame)
            {
                s_lastDeathVfxFrame = Time.frameCount;
                s_deathVfxCountInFrame = 0;
            }

            int frameCap = Mathf.Max(1, maxDeathVfxPerFrame);
            if (s_deathVfxCountInFrame >= frameCap)
                return false;

            s_deathVfxCountInFrame++;
            return true;
        }

        private void EnsureHitTextEffect(bool allowAddRuntime)
        {
            if (hitTextFlyEffect != null) return;
            hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);
            if (hitTextFlyEffect == null && allowAddRuntime)
                hitTextFlyEffect = gameObject.AddComponent<HitTextFlyEffect>();
        }
    }
}
