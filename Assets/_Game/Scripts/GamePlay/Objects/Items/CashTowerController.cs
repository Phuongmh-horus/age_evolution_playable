using System;
using System.Collections.Generic;
using GamePlay.ComponentSystems;
using GamePlay.CombatSystems;
using GamePlay.CollisionSystems;
using GamePlay.Effects;
using GamePlay.HealthSystems;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

namespace GamePlay.Managers
{
    /// <summary>
    /// Abstraction để gameplay object (vd: CashTower) có thể báo về flow manager mà KHÔNG phụ thuộc package bên ngoài.
    /// GameplayManager (hoặc 1 manager khác) chỉ cần implement interface này.
    /// </summary>
    public interface IGameplayFlow
    {
        bool IsGameStarted { get; }
        void OnCashTowerDestroyed();
    }
}

namespace GamePlay.Items
{
    /// <summary>
    /// Tower bị đánh/va chạm -> giảm HP. Khi chết thì báo về GameplayFlow.
    /// 
    /// Lưu ý:
    /// - Không dùng Cysharp/UniTask.
    /// - Không dùng TextUtility/Pack (thư viện ngoài).
    /// - Không override HandleHealthChange (base ItemUnit không có).
    /// </summary>
    public class CashTowerController : ItemUnit
    {
        [Header("Refs")]
        [SerializeField] private HitComponent _hitComponent;
        [SerializeField] private HealthComponent healthComponent;
        [SerializeField] private TMP_Text currentHpText;
        [SerializeField] private TMP_Text maxHpText;
        [SerializeField] private BlockDebrisController blockDebrisController;
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;

        [Header("Sound Effects")]
        [SerializeField] private AudioClipName destroySfx;
        [SerializeField] private AudioClipName hitByWheelSfx;

        [Header("Hit Effect")]
        [SerializeField] private EffectType nonWheelHitEffectType = EffectType.Break;

        [Header("Money Drop")]
        [SerializeField] private float moneyDropImpulse = 1.5f;
        [SerializeField] private float moneyGroundY = 0f;
        [SerializeField] private bool dropMoneyOnWheelDestroy = true;

        [Header("Hit Scale Pulse")]
        [SerializeField] private float scaleUp = 1.08f;
        [SerializeField] private float scaleUpDuration = 0.08f;
        [SerializeField] private float scaleDownDuration = 0.15f;

        // ===== Cached data (NO runtime scan) =====
        private readonly List<CurrencyDropItem> _moneyItems = new List<CurrencyDropItem>(32);
        //private readonly List<Rigidbody> _moneyRB = new List<Rigidbody>(32);
        private readonly List<Collider> _moneyCol = new List<Collider>(32);
        private readonly List<Transform> _towerVisuals = new List<Transform>(32);
        private DropCurrencyEffect _dropCurrencyEffect;

        private bool _deathHandled;
        private Vector3 _originalScale;
        private float _scaleTimer = -1f;
        private bool _isScalingUp;
        private Vector3 _toScale;

        private int _lastShownCurrentHp = int.MinValue;
        private int _lastShownMaxHp = int.MinValue;
        private int _hitFxCountThisFrame;
        private int _lastHitFxFrame = -1;

        protected override void Awake()
        {
            base.Awake();
            autoAddHitTextFlyEffectAtRuntime = false;

            // Entity type
            if (_entityType == Entities.EntityType.None || _entityType == Entities.EntityType.Item)
                _entityType = Entities.EntityType.FinishTower;

            CacheAll(); //  ONLY ONCE
        }

        // ===== ONE-TIME CACHE =====
        private void CacheAll()
        {
            _moneyItems.Clear();
            // _moneyRB.Clear();
            _moneyCol.Clear();
            _towerVisuals.Clear();

            var all = GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;

                // Cache money
                var currency = t.GetComponent<CurrencyDropItem>();
                if (currency != null)
                {
                    _moneyItems.Add(currency);
                    // _moneyRB.Add(t.GetComponent<Rigidbody>());
                    _moneyCol.Add(t.GetComponent<Collider>());
                    continue;
                }

                // Cache tower visuals
                string n = t.name;
                if (n.StartsWith("finish_tower", StringComparison.Ordinal) ||
                    n.StartsWith("tower_m", StringComparison.Ordinal))
                {
                    _towerVisuals.Add(t);
                }
            }

            _dropCurrencyEffect = GetComponentInChildren<DropCurrencyEffect>(true);
        }

        public override void Initialize()
        {
            base.Initialize();
            EnsureCollisionRegistration();

            if (healthComponent != null)
            {
                Pack.Healable = healthComponent;
                ActiveFlags |= CapabilityFlags.Heal;
                Pack.Healable.Initialize();
                RegisterEvents(false);
                RegisterEvents(true);
            }

            _deathHandled = false;
            _lastHitFxFrame = -1;
            _lastDamageFrame = -1;
            _tookDirectDamageThisFrame = false;
            _tookAoeDamageThisFrame = false;

            if (hitTextFlyEffect != null)
            {
                hitTextFlyEffect.enabled = true;
                hitTextFlyEffect.LimitToOneTextPerFrame = true;
            }

            _originalScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnHealthChanged += HandleHealthChanged;
                HandleHealthChanged(healthComponent.CurrentHealth, healthComponent.MaxHealth);
            }
        }

        private void OnDisable()
        {
            if (healthComponent != null)
                healthComponent.OnHealthChanged -= HandleHealthChanged;
            transform.DOKill();
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (current == _lastShownCurrentHp && max == _lastShownMaxHp) return;

            if (currentHpText != null)
                currentHpText.text = TextUtility.ToShortNumberString(current);

            if (maxHpText != null)
                maxHpText.text = TextUtility.ToShortNumberString(max);

            _lastShownCurrentHp = current;
            _lastShownMaxHp = max;
        }

        protected override void HandleHealthChange(int current, int max)
        {
            if (current > 0 || _deathHandled) return;

            _deathHandled = true;
            HandleDead();
            DespawnInterval();
        }

        private int _lastDamageFrame = -1;
        private bool _tookDirectDamageThisFrame = false;
        private bool _tookAoeDamageThisFrame = false;

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            int currentFrame = Time.frameCount;
            if (_lastDamageFrame != currentFrame)
            {
                _lastDamageFrame = currentFrame;
                _tookDirectDamageThisFrame = false;
                _tookAoeDamageThisFrame = false;
            }

            bool isAoe = source != null && source.GetType().Name == "ExplosionShotAttacker";

            if (isAoe)
            {
                if (_tookDirectDamageThisFrame || _tookAoeDamageThisFrame)
                {
                    return; // Skip AOE damage if we already took direct or AOE damage this frame
                }
                _tookAoeDamageThisFrame = true;
            }
            else
            {
                _tookDirectDamageThisFrame = true;
            }

            PlayNonWheelHitEffect();
            base.HandleNonWheelCollision(source);
            PlayScalePulse();

            if (!_deathHandled && healthComponent != null && healthComponent.CurrentHealth <= 0)
            {
                _deathHandled = true;
                HandleDead();
                DespawnInterval();
            }
        }

        private void PlayNonWheelHitEffect()
        {
            if (nonWheelHitEffectType == EffectType.None) return;

            if (_lastHitFxFrame != Time.frameCount)
            {
                _lastHitFxFrame = Time.frameCount;
                _hitFxCountThisFrame = 0;
            }

            if (_hitFxCountThisFrame >= 1) return;
            _hitFxCountThisFrame++;

            Vector3 pos = transform.position + transform.up * 2f + transform.forward * -1f;
            Pack.Effector?.PlayEffect(nonWheelHitEffectType, pos, Quaternion.identity, transform);
        }

        private void HandleDead()
        {
            BreakTowerVisuals();
            DropMoneyItems();

            blockDebrisController?.TriggerDebrisEffect();

            if (SoundManager.Instance != null && destroySfx != AudioClipName.None)
                SoundManager.Instance.PlayOneShot(destroySfx);

        }

        private void EnsureCollisionRegistration()
        {
            if (_hitComponent == null) return;
            _hitComponent.Initialize();

            if (Pack.Hitable != null && !ReferenceEquals(Pack.Hitable, _hitComponent))
            {
                CollisionSystem.Unregister(Pack.Hitable);
            }

            Pack.Hitable = _hitComponent;
            ActiveFlags |= CapabilityFlags.Hit;
            CollisionSystem.Register(_hitComponent, transform);
        }

        private void DropMoneyItems()
        {
            int dropped = 0;

            for (int i = 0; i < _moneyItems.Count; i++)
            {
                var currency = _moneyItems[i];
                if (currency == null) continue;

                var t = currency.transform;
                var col = _moneyCol[i];

                // OPTIMIZATION: Disable collider before activating object 
                // to avoid expensive physics broadphase insertion and removal in the same frame.
                if (col != null)
                    col.enabled = false;

                // OPTIMIZATION: Manually preserve world transform to avoid SetParent(null, true) overhead
                Vector3 wPos = t.position;
                Quaternion wRot = t.rotation;
                Vector3 wScale = t.lossyScale;

                t.SetParent(null, false);
                t.SetPositionAndRotation(wPos, wRot);
                t.localScale = wScale;

                t.gameObject.SetActive(true);

                currency.Initialize();
                currency.SetAutoClaimOnGround(true);
                currency.SetClaimType(CurrencyType.Cash);
                currency.SetGroundY(moneyGroundY);

                Vector3 dir = (wPos - transform.position).normalized;
                if (dir == Vector3.zero) dir = Vector3.up;

                currency.Initialize(dir * moneyDropImpulse, currency.Amount > 0 ? currency.Amount : 1f, true);

                dropped++;
            }

            if (dropped == 0 && _dropCurrencyEffect != null)
                _dropCurrencyEffect.SpawnCurrency(transform.position);
        }

        private void BreakTowerVisuals()
        {
            for (int i = 0; i < _towerVisuals.Count; i++)
            {
                var t = _towerVisuals[i];
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private void PlayScalePulse()
        {
            if (!isActiveAndEnabled) return;

            DOTween.Kill(transform);
            transform.localScale = _originalScale;
            Sequence seq = DOTween.Sequence();
            seq.Append(transform.DOScale(_originalScale * scaleUp, scaleUpDuration).SetEase(Ease.OutQuad));
            seq.Append(transform.DOScale(_originalScale, scaleDownDuration).SetEase(Ease.InQuad));
            seq.SetId(transform);
        }

    }
}