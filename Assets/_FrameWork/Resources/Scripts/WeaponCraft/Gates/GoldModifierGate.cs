using System.Collections;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.HealthSystems;
using GamePlay.Items;
using TMPro;
using UnityEngine;

namespace WeaponCraft
{
    public class GoldModifierGate : StatModifierItem<GoldModifierGateData>
    {
        private static readonly int FillAmountProp = Shader.PropertyToID("_FillAmount");

        [Header("Health Visual Settings")]
        [SerializeField] private SpriteRenderer progressSprite;
        [SerializeField] private float progressMinFill = 0.532f;
        [SerializeField] private float progressMaxFill = 0.792f;
        private MaterialPropertyBlock _progressMpb;
        private const string collectFormat = @"<sprite name=""coin""> {0}";
        private const string bonusCollectFormat = @"+{0} <sprite name=""coin"">";

        [SerializeField] private TMP_Text collectText;
        [SerializeField] private TMP_Text bonusCollectText;

        [Header("Hit Component")]
        [SerializeField] private HitComponent hitComponent;
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;

        [Header("Effect Component")]
        [SerializeField] private EffectComponent effectComponent;

        [Header("Health Component")]
        [SerializeField] private HealthComponent healthComponent;

        [Header("Hit Scale Pulse")]
        [SerializeField] private float scaleUp = 1.08f;
        [SerializeField] private float scaleUpDuration = 0.08f;
        [SerializeField] private float scaleDownDuration = 0.15f;

        private Vector3 _originalScale;
        private Coroutine _scalePulseRoutine;
        private int _lastScalePulseFrame = -1;
        private bool _awaitingGoldReset;
        private int _lastBreakFxFrame = -1;

        protected void Awake()
        {
            _progressMpb = new MaterialPropertyBlock();

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

            if (bonusCollectText != null)
            {
                bonusCollectText.text = string.Format(bonusCollectFormat, Data != null ? Data.Value : 1);
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

            _awaitingGoldReset = false;
            _lastBreakFxFrame = -1;
            _originalScale     = transform.localScale;
            RegisterCapacityBarEvents();

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

            UpdateCollectVisual();
            EnsureHitTextEffect(false);
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

        private int GetBaseGoldValue()
        {
            return Mathf.Max(1, Data != null ? Data.Value : 1);
        }

        private void UpdateCollectVisual()
        {
            if (collectText != null)
            {
                int pooled = Mathf.Max(0, GameplayManager.StartCoin);
                collectText.text = string.Format(collectFormat, pooled);
            }

            if (bonusCollectText != null)
            {
                int rewardPerTick = ResolveGoldRewardPerProgressTick();
                bonusCollectText.text = string.Format(bonusCollectFormat, rewardPerTick);
            }
        }

        private void OnDisable()
        {
            UnregisterHealthVisualEvents();
            UnregisterCapacityBarEvents();

            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
                _scalePulseRoutine = null;
            }
        }

        private void OnDestroy()
        {
            UnregisterHealthVisualEvents();
            UnregisterCapacityBarEvents();

            if (_scalePulseRoutine != null)
            {
                StopCoroutine(_scalePulseRoutine);
                _scalePulseRoutine = null;
            }
        }

        protected override void AdjustStatModifierValue(int value = 0) { }

        protected override void HandleWheelCollision()
        {
            PlayScalePulse();
            CashOutGold();
            DespawnInterval();
            Pack.Effector?.PlayEffect(EffectType.Land);
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            ApplyDamageAcrossProgressCycles(source);
            PlayScalePulse();
        }

        protected override void HandleHealthChange(int current, int max)
        {
            if (current <= 0)
            {
                bool canPlayBreakFx = !_awaitingGoldReset && _lastBreakFxFrame != Time.frameCount;
                _awaitingGoldReset = true;
                GrantCoinOnProgressTick();
                UpdateCollectVisual();
                if (canPlayBreakFx)
                {
                    _lastBreakFxFrame = Time.frameCount;
                    Pack.Effector?.PlayEffect(EffectType.Break, transform.position, Quaternion.identity);
                }

                if (healthComponent != null)
                {
                    healthComponent.SetMaxHealth(max, refill: true);
                }

                return;
            }

            if (_awaitingGoldReset && current >= max)
            {
                _awaitingGoldReset = false;
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

        private void RegisterCapacityBarEvents()
        {
            GameEventBus.UpdateCapacityBar -= HandleCapacityBarUpdated;
            GameEventBus.UpdateCapacityBar += HandleCapacityBarUpdated;
        }

        private void UnregisterCapacityBarEvents()
        {
            GameEventBus.UpdateCapacityBar -= HandleCapacityBarUpdated;
        }

        private void HandleCapacityBarUpdated()
        {
            UpdateCollectVisual();
        }

        private void HandleHealthVisualChanged(int current, int max)
        {
            UpdateHealthVisual(current, max);
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
            float fillAmount = Mathf.Lerp(progressMinFill, progressMaxFill, Mathf.Clamp01(healthPercent));

            UpdateCollectVisual();
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

        private void CashOutGold()
        {
            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager == null)
            {
                return;
            }

            int amount = gameplayManager.ConsumeCapacityCoinPool();
            if (0 >= amount) return;
            gameplayManager.AddCurrency(CurrencyType.Gold, amount, transform.position);

            UpdateCollectVisual();
        }

        private void GrantCoinOnProgressTick()
        {
            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager == null)
            {
                return;
            }

            int reward = ResolveGoldRewardPerProgressTick();
            gameplayManager.AddCapacityCoinToPool(reward);
        }

        private int ResolveGoldRewardPerProgressTick()
        {
            int baseReward = GetBaseGoldValue();
            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager == null)
            {
                return baseReward;
            }

            return gameplayManager.GetGoldGateRewardPerProgressTick(baseReward);
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
    }
}

