using System.Collections;
using System.Collections.Generic;
using GamePlay.CollisionSystems;
using GamePlay.ComponentSystems;
using GamePlay.CombatSystems;
using GamePlay.HealthSystems;
using GamePlay.Items;
using TMPro;
using UnityEngine;

namespace WeaponCraft
{
    public class WeaponModifierGate : StatModifierItem<WeaponModifierGateData>
    {
        private static readonly int FillAmountProp = Shader.PropertyToID("_FillAmount");

        [Header("Health Visual Settings")]
        [SerializeField] private SpriteRenderer progressSprite;
        [SerializeField] private float progressMinFill = 0.532f;
        [SerializeField] private float progressMaxFill = 0.792f;
        private MaterialPropertyBlock _progressMpb;
        private int _valueCollect;
        private int _countCollect;

        private const string collectFormat = @"x {0}";

        [SerializeField] private TMP_Text collectText;

        [Header("Hit Component")]
        [SerializeField] private HitComponent hitComponent;
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;

        [Header("Effect Component")]
        [SerializeField] private EffectComponent effectComponent;
        [SerializeField] private EffectType nonWheelHitEffectType = EffectType.Break;

        [Header("Health Component")]
        [SerializeField] private HealthComponent healthComponent;

        [Header("Hit Scale Pulse")]
        [SerializeField] private float scaleUp = 1.08f;
        [SerializeField] private float scaleUpDuration = 0.08f;
        [SerializeField] private float scaleDownDuration = 0.15f;

        [Header("Despawn Scale FX")]
        [SerializeField] private bool ensureDespawnScaleEffect = true;
        [SerializeField, Min(1f)] private float despawnScaleMultiplier = 1.08f;
        [SerializeField, Min(0.01f)] private float despawnExpandDuration = 0.06f;
        [SerializeField, Min(0.01f)] private float despawnShrinkDuration = 0.12f;

        private Vector3 _originalScale;
        private Coroutine _scalePulseRoutine;
        private int _lastScalePulseFrame = -1;
        private bool _awaitingCraftReset;
        private int _lastBreakFxFrame = -1;
        private int _lastHitFxFrame = -1;

        protected void Awake()
        {
            _progressMpb = new MaterialPropertyBlock();
            EnsureDespawnScaleEffect();

            if (_entityType == GamePlay.Entities.EntityType.None)
            {
                _entityType = GamePlay.Entities.EntityType.PowerGate;
            }

            if (progressSprite == null)
            {
                progressSprite = GetComponentInChildren<SpriteRenderer>(true);
            }
            
            if (collectText == null)
            {
                collectText = GetComponentInChildren<TMP_Text>(true);
            }

            if (effectComponent == null)
            {
                effectComponent = GetComponentInChildren<EffectComponent>(true);
            }

            if (hitTextFlyEffect == null)
            {
                hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);
            }

            _originalScale = transform.localScale;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            _entityType = GamePlay.Entities.EntityType.PowerGate;

            if (hitComponent == null)
            {
                hitComponent = GetComponentInChildren<HitComponent>(true);
            }

            if (healthComponent == null)
            {
                healthComponent = GetComponentInChildren<HealthComponent>(true);
            }

            if (progressSprite == null)
            {
                progressSprite = GetComponentInChildren<SpriteRenderer>(true);
            }

            if (collectText == null)
            {
                collectText = GetComponentInChildren<TMP_Text>(true);
            }

            if (effectComponent == null)
            {
                effectComponent = GetComponentInChildren<EffectComponent>(true);
            }

            _originalScale = transform.localScale;
        }
#endif

        public override void Initialize()
        {
            base.Initialize();

            _awaitingCraftReset = false;
            _lastBreakFxFrame = -1;
            _lastHitFxFrame = -1;
            _valueCollect = 0;
            _countCollect = 0;
            _originalScale = transform.localScale;

            bool shouldRefreshEvents = false;

            if (healthComponent == null)
            {
                healthComponent = GetComponentInChildren<HealthComponent>(true);
            }

            if (healthComponent != null)
            {
                shouldRefreshEvents = true;
                Pack.Healable = healthComponent;
                ActiveFlags |= CapabilityFlags.Heal;
                healthComponent.Initialize();
                healthComponent.SetImmortal(false);
                healthComponent.SetMaxHealth(healthComponent.MaxHealth, refill: true);

                RegisterHealthVisualEvents();
                UpdateHealthVisual(healthComponent.CurrentHealth, healthComponent.MaxHealth);
            }

            if (hitComponent == null)
            {
                hitComponent = GetComponentInChildren<HitComponent>(true);
            }

            if (hitComponent != null)
            {
                shouldRefreshEvents = true;

                if (Pack.Hitable != null && !ReferenceEquals(Pack.Hitable, hitComponent))
                {
                    CollisionSystem.Unregister(Pack.Hitable);
                }

                Pack.Hitable = hitComponent;
                ActiveFlags |= CapabilityFlags.Hit;
                hitComponent.Initialize();
                CollisionSystem.Register(hitComponent, hitComponent.transform);
            }

            if (effectComponent == null)
            {
                effectComponent = GetComponentInChildren<EffectComponent>(true);
            }

            if (effectComponent != null)
            {
                Pack.Effector = effectComponent;
                ActiveFlags |= CapabilityFlags.Effector;
                effectComponent.Initialize();
            }

            UpdateCollectVirual();
            EnsureHitTextEffect(true);
            if (hitTextFlyEffect != null)
            {
                hitTextFlyEffect.enabled = true;
                hitTextFlyEffect.WarmupRuntimeCaches();
            }

            if (shouldRefreshEvents)
            {
                RegisterEvents(false);
                RegisterEvents(true);
            }
        }

        private void UpdateCollectVirual()
        {
            if (collectText != null)
            {
                collectText.text = string.Format(collectFormat, _countCollect + (Data?.Value ?? 0));
            }
        }

        private void OnDisable()
        {
            UnregisterHealthVisualEvents();

            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
                _scalePulseRoutine = null;
            }
        }

        private void OnDestroy()
        {
            UnregisterHealthVisualEvents();

            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
                _scalePulseRoutine = null;
            }
        }

        protected override void AdjustStatModifierValue(int value = 0)
        {
            // base.AdjustStatModifierValue(value);

            if (value > 0)
            {
                _valueCollect += value;
                RecalculateCountCollect();
            }
        }

        protected override void HandleWheelCollision()
        {
            CraftStoredReward();
            base.HandleWheelCollision();
            Pack.Effector?.PlayEffect(EffectType.Land, transform.position, Quaternion.identity);
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            PlayNonWheelHitEffect();
            ApplyDamageAcrossProgressCycles(source);
            if (source != null)
            {
                AdjustStatModifierValue(source.Damage);
            }
            PlayScalePulse();
        }

        protected override void HandleHealthChange(int current, int max)
        {
            if (current <= 0)
            {
                bool canPlayBreakFx = !_awaitingCraftReset &&
                                      _lastBreakFxFrame != Time.frameCount &&
                                      _lastHitFxFrame != Time.frameCount;
                _awaitingCraftReset = true;
                if (canPlayBreakFx)
                {
                    _lastBreakFxFrame = Time.frameCount;
                    Pack.Effector?.PlayEffect(EffectType.Break);
                }

                if (healthComponent != null)
                {
                    healthComponent.SetMaxHealth(max, refill: true);
                }

                return;
            }

            if (_awaitingCraftReset && current >= max)
            {
                _awaitingCraftReset = false;
            }

            base.HandleHealthChange(current, max);
        }

        private void RegisterHealthVisualEvents()
        {
            if (healthComponent == null)
            {
                return;
            }

            healthComponent.OnHealthChange -= HandleHealthVisualChanged;
            healthComponent.OnHealthChange += HandleHealthVisualChanged;
        }

        private void UnregisterHealthVisualEvents()
        {
            if (healthComponent == null)
            {
                return;
            }

            healthComponent.OnHealthChange -= HandleHealthVisualChanged;
        }

        private void HandleHealthVisualChanged(int current, int max)
        {
            UpdateHealthVisual(current, max);
        }

        private void RecalculateCountCollect()
        {
            int maxHealth = healthComponent != null ? Mathf.Max(1, healthComponent.MaxHealth) : 1;
            _countCollect = Mathf.Max(0, _valueCollect / maxHealth);
        }

        private void UpdateHealthVisual(int currentHealth, int maxHealth)
        {
            if (maxHealth <= 0)
            {
                return;
            }

            if (progressSprite == null)
            {
                return;
            }

            float healthPercent = (float)Mathf.Clamp(currentHealth, 0, maxHealth) / maxHealth;
            float fillAmount    = Mathf.Lerp(progressMinFill, progressMaxFill, Mathf.Clamp01(healthPercent));

            UpdateCollectVirual();
            if (_progressMpb == null) _progressMpb = new MaterialPropertyBlock();
            progressSprite.GetPropertyBlock(_progressMpb);
            _progressMpb.SetFloat(FillAmountProp, fillAmount);
            progressSprite.SetPropertyBlock(_progressMpb);
        }

        private void PlayScalePulse()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_lastScalePulseFrame == Time.frameCount)
            {
                return;
            }

            _lastScalePulseFrame = Time.frameCount;
            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
            }

            _scalePulseRoutine = StartCoroutine(CoScalePulse());
        }

        private IEnumerator CoScalePulse()
        {
            if (_originalScale == Vector3.zero)
            {
                _originalScale = transform.localScale;
            }

            Vector3 from = _originalScale;
            Vector3 to = _originalScale * scaleUp;

            float t = 0f;
            while (t < scaleUpDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, scaleUpDuration));
                transform.localScale = Vector3.Lerp(from, to, k);
                yield return null;
            }

            t = 0f;
            while (t < scaleDownDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.0001f, scaleDownDuration));
                transform.localScale = Vector3.Lerp(to, _originalScale, k);
                yield return null;
            }

            transform.localScale = _originalScale;
            _scalePulseRoutine = null;
        }

        private void StopScalePulse()
        {
            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
                _scalePulseRoutine = null;
            }

            if (transform != null && _originalScale != Vector3.zero)
            {
                transform.localScale = _originalScale;
            }
        }

        private void PlayNonWheelHitEffect()
        {
            if (nonWheelHitEffectType == EffectType.None)
            {
                return;
            }

            if (_lastHitFxFrame == Time.frameCount)
            {
                return;
            }

            _lastHitFxFrame = Time.frameCount;
            Pack.Effector?.PlayEffect(nonWheelHitEffectType, transform.position, Quaternion.identity, transform);
        }

        private void CraftStoredReward()
        {
            if (Data == null)
            {
                return;
            }

            var system = ResolveWeaponCraftSystem();
            if (system == null)
            {
                return;
            }

            int tier = ResolveCollectTier(system);
            int amount = Mathf.Max(1, _countCollect + Data.Value);
            Vector3 spawnPosition = Transform != null ? Transform.position : transform.position;

            system.ReceiveItem(tier, spawnPosition, amount);

            _valueCollect = 0;
            _countCollect = 0;
            UpdateCollectVirual();
        }

        private void ApplyDamageAcrossProgressCycles(IAttacker source)
        {
            if (source == null)
            {
                return;
            }

            if (healthComponent == null)
            {
                healthComponent = GetComponentInChildren<HealthComponent>(true);
            }

            if (healthComponent == null)
            {
                Pack.Healable?.TakeDamage(source);
                return;
            }

            int remainingDamage = Mathf.Max(0, source.Damage);
            bool firstCycle = true;
            while (remainingDamage > 0)
            {
                int maxHealth = Mathf.Max(1, healthComponent.MaxHealth);
                int currentHealth = Mathf.Clamp(healthComponent.CurrentHealth, 0, maxHealth);
                if (currentHealth <= 0)
                {
                    healthComponent.SetMaxHealth(maxHealth, refill: true);
                    currentHealth = maxHealth;
                }

                int damageThisCycle = Mathf.Min(remainingDamage, currentHealth);
                if (firstCycle)
                {
                    // Keep one hit text with full original damage.
                    healthComponent.TakeDamage(remainingDamage);
                    firstCycle = false;
                }
                else
                {
                    // Overflow cycles should not spawn extra hit texts.
                    healthComponent.TakeDamageSilently(damageThisCycle);
                }
                remainingDamage -= damageThisCycle;
            }
        }

        private int ResolveCollectTier(WeaponCraftSystem system)
        {
            if (Data != null && Data.TierRequestList != null && Data.TierRequestList.Count > 0)
            {
                return ResolveTierByConfig(system, Data.TierRequestList[0].Tier);
            }

            int fallbackTier = Mathf.Max(1, Data != null ? Data.Value : 1);
            return ResolveTierByConfig(system, fallbackTier);
        }

        private int ResolveTierByConfig(WeaponCraftSystem system, int tier)
        {
            int resolvedTier = Mathf.Max(1, tier);
            if (system == null || system.Config == null)
            {
                return resolvedTier;
            }

            return Mathf.Min(resolvedTier, system.Config.MaxTier);
        }

        private WeaponCraftSystem ResolveWeaponCraftSystem()
        {
            return WeaponCraftSystem.Instance;
        }

        private void EnsureHitTextEffect(bool allowAddRuntime)
        {
            if (hitTextFlyEffect != null) return;
            hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);
            if (hitTextFlyEffect == null && allowAddRuntime)
            {
                hitTextFlyEffect = gameObject.AddComponent<HitTextFlyEffect>();
            }
        }

        private void EnsureDespawnScaleEffect()
        {
            if (!ensureDespawnScaleEffect) return;

            if (deathScaleEffect == null)
            {
                deathScaleEffect = GetComponent<DeathScaleEffect>();
            }

            if (deathScaleEffect == null)
            {
                deathScaleEffect = gameObject.AddComponent<DeathScaleEffect>();
            }

            if (deathScaleEffect.Transform == null)
            {
                deathScaleEffect.Transform = transform;
            }

            deathScaleEffect.Configure(despawnScaleMultiplier, despawnExpandDuration, despawnShrinkDuration);
        }

        protected override void DespawnInterval()
        {
            StopScalePulse();
            EnsureDespawnScaleEffect();
            base.DespawnInterval();
        }
    }
}

