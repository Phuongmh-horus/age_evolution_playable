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
        private int _valueCollect;
        private int _countCollect;

        private const string collectFormat = @"<sprite name=""coin""> {0}";
        private const string bonusCollectFormat = @"+{0} <sprite name=""coin"">";

        [SerializeField] private TMP_Text collectText;
        [SerializeField] private TMP_Text bonusCollectText;

        [Header("Hit Component")]
        [SerializeField] private HitComponent hitComponent;

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
            _valueCollect      = 0;
            _countCollect      = 0;
            _originalScale     = transform.localScale;

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
                collectText.text = string.Format(collectFormat, _countCollect * (Data?.Value ?? 1));
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
            if (value > 0)  
            {
                _valueCollect += value;
                RecalculateCountCollect();
            }
        }
        private void RecalculateCountCollect()
        {
            int maxHealth = healthComponent != null ? Mathf.Max(1, healthComponent.MaxHealth) : 1;
            _countCollect = Mathf.Max(0, _valueCollect / maxHealth);
        }

        protected override void HandleWheelCollision()
        {
            PlayScalePulse();
            CashOutGold();
            base.HandleWheelCollision();
            Pack.Effector?.PlayEffect(EffectType.Land);
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            base.HandleNonWheelCollision(source);
            PlayScalePulse();
        }

        protected override void HandleHealthChange(int current, int max)
        {
            if (current <= 0)
            {
                _awaitingGoldReset = true;
                // _countCollect++;
                _valueCollect += GetBaseGoldValue();
                UpdateCollectVisual();
                Pack.Effector?.PlayEffect(EffectType.Break, transform.position, Quaternion.identity);

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
            _progressMpb ??= new MaterialPropertyBlock();
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
            int amount = Mathf.Max(0, _countCollect * (Data?.Value ?? 1));
            if (0 >= amount) return;
            var gameplayManager = GameplayManager.Instance;
            if (gameplayManager != null)
            {
                gameplayManager.AddCurrency(CurrencyType.Gold, amount, transform.position);
            }

            _valueCollect = 0;
            _countCollect = 0;
        }
    }
}
