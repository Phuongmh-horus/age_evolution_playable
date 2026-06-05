using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.ComponentSystems;
using GamePlay.CombatSystems;
using GamePlay.CollisionSystems;
using GamePlay.Effects;
using GamePlay.HealthSystems;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

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
        [SerializeField] private HealthComponent healthComponent;
        [SerializeField] private TMP_Text currentHpText;
        [SerializeField] private TMP_Text maxHpText;
        [SerializeField] private BlockDebrisController blockDebrisController;
        [SerializeField] private HitTextFlyEffect hitTextFlyEffect;

        [Header("Visuals")]
        [SerializeField] private bool isOverrideVisual = true;
        [SerializeField] private bool applyBaseColorOnInitialize = false;
        [SerializeField] private MeshRenderer baseMesh;
        [SerializeField] private Color baseColor = Color.white;
        [SerializeField] private Transform towerVisualRoot;
        [SerializeField] private string[] colorPropertyNames = { "_BaseColor", "_Color", "_TintColor" };

        [Header("Sound Effects")]
        [SerializeField] private AudioClipName destroySfx;
        [SerializeField] private AudioClipName hitByWheelSfx;

        [Header("Hit Effect")]
        [SerializeField] private EffectType nonWheelHitEffectType = EffectType.Break;

        [Header("Money Drop")]
        [SerializeField] private Transform moneyRoot;
        [SerializeField] private float moneyDropImpulse = 1.5f;
        [SerializeField] private float moneyGroundY = 0f;
        [SerializeField] private bool dropMoneyOnWheelDestroy = true;

        [Header("Hit Scale Pulse")]
        [SerializeField] private float scaleUp = 1.08f;
        [SerializeField] private float scaleUpDuration = 0.08f;
        [SerializeField] private float scaleDownDuration = 0.15f;

        [Header("Events")]
        [Tooltip("Nếu không tìm được IGameplayFlow trong scene, event này sẽ được gọi khi tower chết.")]
        [SerializeField] private UnityEvent onTowerDestroyedFallback;

        [Header("Flow")]
        [SerializeField] private MonoBehaviour flowProvider;
        private GamePlay.Managers.IGameplayFlow _flow;
        private bool _deathHandled;
        private Vector3 _originalScale;
        private Coroutine _scalePulseRoutine;
        private bool _registeredCollision;
        [SerializeField] private HitComponent _hitComponent;
        private readonly List<MeshRenderer> _cachedColorRenderers = new List<MeshRenderer>(16);
        private readonly List<int> _cachedColorPropertyIds = new List<int>(16);
        private MaterialPropertyBlock _colorMpb;
        private bool _warnedMissingHitComponentRuntime;
        private int _lastShownCurrentHp = int.MinValue;
        private int _lastShownMaxHp = int.MinValue;
        private int _lastNonWheelHitFxFrame = -1;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            if (healthComponent == null)
                Debug.LogWarning($"[CashTowerController] Missing HealthComponent on {name}. Assign in Inspector.");

            if (baseMesh == null)
                Debug.LogWarning($"[CashTowerController] Missing Base MeshRenderer on {name}. Assign in Inspector.");

            if (hitTextFlyEffect == null)
                Debug.LogWarning($"[CashTowerController] Missing HitTextFlyEffect on {name}. Assign in Inspector.");

            if (towerVisualRoot == null)
                towerVisualRoot = transform.Find("Tower");

            if (moneyRoot == null)
                moneyRoot = towerVisualRoot != null ? towerVisualRoot : transform;

            CacheMoneyItems();
        }
#endif

        private void Awake()
        {
            // Ensure correct EntityType for filtering and flow
            if (_entityType == GamePlay.Entities.EntityType.None || _entityType == GamePlay.Entities.EntityType.Item)
            {
                _entityType = GamePlay.Entities.EntityType.FinishTower;
            }
        }

        public override void Initialize()
        {
            base.Initialize();

            EnsureCollisionRegistration();

            // Ensure Pack.Healable is wired so damage reduces HP and despawns at 0.
            if (healthComponent != null)
            {
                Pack.Healable = healthComponent;
                ActiveFlags |= CapabilityFlags.Heal;
                Pack.Healable.Initialize();
                RegisterEvents(false);
                RegisterEvents(true);
            }

            _deathHandled = false;
            _lastNonWheelHitFxFrame = -1;
            if (hitTextFlyEffect != null)
                hitTextFlyEffect.enabled = true;

            CacheMoneyItems();
            BuildColorRendererCache();
            if (isOverrideVisual && applyBaseColorOnInitialize)
                ApplyColor(baseColor);

            _originalScale = transform.localScale;

            if (healthComponent != null)
            {
                HandleHealthChanged(healthComponent.CurrentHealth, healthComponent.MaxHealth);
            }
        }

        private void OnEnable()
        {
            ResolveFlow();
            EnsureCollisionRegistration();

            if (healthComponent != null)
            {
                healthComponent.OnHealthChanged += HandleHealthChanged;

                // sync UI ngay khi bật
                HandleHealthChanged(healthComponent.CurrentHealth, healthComponent.MaxHealth);
            }
        }

        private void OnDisable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnHealthChanged -= HandleHealthChanged;
            }

            if (_registeredCollision && Pack.Hitable != null)
            {
                CollisionSystem.Unregister(Pack.Hitable);
                _registeredCollision = false;
            }
        }

        private void ResolveFlow()
        {
            // Prefer explicit assignment to avoid heavy FindObjectsOfType.
            _flow = flowProvider as GamePlay.Managers.IGameplayFlow;
        }

        private void EnsureCollisionRegistration()
        {
            if (!Application.isPlaying) return;
            if (_registeredCollision) return;

            if (_hitComponent == null)
            {
                if (!_warnedMissingHitComponentRuntime)
                {
                    _warnedMissingHitComponentRuntime = true;
                }
            }

            if (_hitComponent == null) return;

            _hitComponent.Initialize();

            if (Pack.Hitable != null && !ReferenceEquals(Pack.Hitable, _hitComponent))
            {
                CollisionSystem.Unregister(Pack.Hitable);
            }

            Pack.Hitable = _hitComponent;
            ActiveFlags |= CapabilityFlags.Hit;
            CollisionSystem.Register(_hitComponent, transform);
            _registeredCollision = true;
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (current == _lastShownCurrentHp && max == _lastShownMaxHp) return;

            if (current != _lastShownCurrentHp && currentHpText != null)
                currentHpText.text = TextUtility.ToShortNumberString(current);

            if (max != _lastShownMaxHp && maxHpText != null)
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

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            PlayNonWheelHitEffect();
            base.HandleNonWheelCollision(source);
            PlayScalePulse();

            // Fallback: if health events are not wired in Luna, ensure drop on death.
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
            if (_lastNonWheelHitFxFrame == Time.frameCount) return;

            _lastNonWheelHitFxFrame = Time.frameCount;
            Pack.Effector?.PlayEffect(nonWheelHitEffectType, transform.position + transform.up * 2f, Quaternion.identity, transform);
        }

        private void HandleDead()
        {
            BreakTowerVisuals();
            DropMoneyItems();

            if (blockDebrisController != null)
                blockDebrisController.TriggerDebrisEffect();

            if (SoundManager.Instance != null && destroySfx != AudioClipName.None)
                SoundManager.Instance.PlayOneShot(destroySfx);

            // Fallback: cho designer hook trong inspector.
            onTowerDestroyedFallback?.Invoke();
        }

        protected override void HandleWheelCollision()
        {
            if (!GameplayManager.IsGameStarted) return;
            GameplayManager.IsGameStarted = false;
            RegisterEvents(false);

            if (SoundManager.Instance != null && hitByWheelSfx != AudioClipName.None)
                SoundManager.Instance.PlayOneShot(hitByWheelSfx);

            if (dropMoneyOnWheelDestroy && !_deathHandled)
            {
                _deathHandled = true;
                HandleDead();
                DespawnInterval();
            }

            GameplayManager.Instance.PauseGame();
            if (GameplayManager.Instance != null)
            {
                GameplayManager.Instance.SetMilestoneOverridePosition(transform.position);
            }
            GameplayManager.Instance.EndGame(true);
        }

        public void ApplyColor(Color color)
        {
            baseColor = color;

            ApplyColorToRenderers();

            if (blockDebrisController != null)
                blockDebrisController.BaseColor = baseColor;
        }

        private void ApplyColorToRenderers()
        {
            if (_cachedColorRenderers.Count == 0 || _cachedColorRenderers.Count != _cachedColorPropertyIds.Count)
            {
                BuildColorRendererCache();
            }

            if (_cachedColorRenderers.Count == 0) return;
            if (_colorMpb == null) _colorMpb = new MaterialPropertyBlock();

            for (int i = 0; i < _cachedColorRenderers.Count; i++)
            {
                var renderer = _cachedColorRenderers[i];
                if (renderer == null) continue;
                int propId = _cachedColorPropertyIds[i];
                if (propId == 0) continue;

                renderer.GetPropertyBlock(_colorMpb);
                _colorMpb.SetColor(propId, baseColor);
                renderer.SetPropertyBlock(_colorMpb);
            }
        }

        protected override void DespawnInterval()
        {
            base.DespawnInterval();
        }

        private void BuildColorRendererCache()
        {
            _cachedColorRenderers.Clear();
            _cachedColorPropertyIds.Clear();

            var root = towerVisualRoot != null ? towerVisualRoot : transform;
            if (root == null) return;

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                if (IsMoneyRenderer(renderer)) continue;

                var mat = renderer.sharedMaterial;
                if (mat == null) continue;

                int propId = ResolveColorPropertyId(mat);
                if (propId == 0) continue;

                _cachedColorRenderers.Add(renderer);
                _cachedColorPropertyIds.Add(propId);
            }
        }

        private int ResolveColorPropertyId(Material mat)
        {
            if (mat == null) return 0;

            if (colorPropertyNames != null)
            {
                for (int i = 0; i < colorPropertyNames.Length; i++)
                {
                    string prop = colorPropertyNames[i];
                    if (string.IsNullOrEmpty(prop)) continue;
                    if (!mat.HasProperty(prop)) continue;
                    return Shader.PropertyToID(prop);
                }
            }

            if (mat.HasProperty("_BaseColor")) return Shader.PropertyToID("_BaseColor");
            if (mat.HasProperty("_Color")) return Shader.PropertyToID("_Color");
            if (mat.HasProperty("_TintColor")) return Shader.PropertyToID("_TintColor");

            return 0;
        }

        private bool IsMoneyRenderer(MeshRenderer renderer)
        {
            if (renderer == null) return false;
            if (renderer.name.StartsWith("finish_money", StringComparison.Ordinal)) return true;

            if (moneyRoot != null && moneyRoot != towerVisualRoot && moneyRoot != transform)
            {
                if (renderer.transform.IsChildOf(moneyRoot)) return true;
            }

            return renderer.name.IndexOf("money", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CacheMoneyItems()
        {
            if (moneyRoot == null)
            {
                moneyRoot = towerVisualRoot != null ? towerVisualRoot : transform;
            }
        }

        private void DropMoneyItems()
        {
            var root = moneyRoot != null ? moneyRoot : transform;
            if (root == null) return;
            var children = root.GetComponentsInChildren<Transform>(true);
            int droppedCount = 0;

            for (int i = 0; i < children.Length; i++)
            {
                var t = children[i];
                if (t == null || t == root) continue;

                bool isMoneyName = t.name.StartsWith("finish_money", StringComparison.Ordinal);
                var existingCurrency = t.GetComponent<CurrencyDropItem>();
                if (!isMoneyName && existingCurrency == null) continue;

                t.SetParent(null, true);
                t.gameObject.SetActive(true);

                var rb = t.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }

                var col = t.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                }

                var currency = existingCurrency != null ? existingCurrency : t.gameObject.AddComponent<CurrencyDropItem>();

                if (currency == null)
                    continue;

                currency.Initialize();
                currency.SetAutoClaimOnGround(false);
                currency.SetClaimType(CurrencyType.Cash);
                currency.SetGroundY(moneyGroundY);

                float value = currency.Amount > 0f ? currency.Amount : 1f;
                var dir = (t.position - Transform.position).normalized;
                if (dir == Vector3.zero) dir = UnityEngine.Random.onUnitSphere;
                Vector3 velocity = dir * moneyDropImpulse;
                currency.Initialize(velocity, value, flyUp: true);

                droppedCount++;
            }

            // Fallback for Luna build: if no baked money meshes found, spawn via DropCurrencyEffect.
            if (droppedCount == 0)
            {
                var effect = GetComponentInChildren<DropCurrencyEffect>(true);
                if (effect != null)
                {
                    effect.SpawnCurrency(root.position);
                }
                else
                {
                    Debug.LogWarning($"[CashTower] No finish_money children and no DropCurrencyEffect on {gameObject.name}.");
                }
            }
        }

        private void BreakTowerVisuals()
        {
            if (towerVisualRoot == null)
                towerVisualRoot = transform.Find("Tower") ?? transform;

            if (towerVisualRoot == null)
                return;

            var children = towerVisualRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                var t = children[i];
                if (t == null || string.IsNullOrEmpty(t.name)) continue;
                if (t.name.StartsWith("finish_money", StringComparison.Ordinal)) continue;

                if (t.name.StartsWith("finish_tower", StringComparison.Ordinal) ||
                    t.name.StartsWith("tower_m", StringComparison.Ordinal))
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        private void PlayScalePulse()
        {
            if (!isActiveAndEnabled) return;
            if (_scalePulseRoutine != null) StopCoroutine(_scalePulseRoutine);
            _scalePulseRoutine = StartCoroutine(CoScalePulse());
        }

        private IEnumerator CoScalePulse()
        {
            if (_originalScale == Vector3.zero) _originalScale = transform.localScale;

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
    }
}
