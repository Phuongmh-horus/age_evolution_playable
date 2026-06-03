using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Items;
using Pools;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class CapacityIncreasePillar : StatModifierItem<StatModifierCapacityData>
{
    /// <summary>
    /// Playable hook: fired when a brick reaches the capacity bar target.
    /// Value is the gained amount (delta).
    /// </summary>
    public static event Action<int> OnCapacityBrickDelivered;

    [Header("Brick Fall Trigger")]
    [SerializeField] private Transform bricksRoot;
    [SerializeField] private int bricksPerDamage = 1;
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("halveBricksPerDamage")]
    private bool reduceBricksPerDamage = false;
    [SerializeField] private int maxVisualBricksPerHit = 8;
    [SerializeField] private int maxBricksInFlight = 28;
    [SerializeField] private int maxVisualBricksPerBurst = 3;
    [SerializeField] private float minVisualSpawnInterval = 0.05f;
    [SerializeField] private bool forceVisualBricksMatchDamage = true;
    [SerializeField] private bool batchCapacityGainPerFrame = true;
    [SerializeField] private BrickLayer brickLayer;
    [SerializeField] private BrickFallSettings _brickFallSettings;

    [Header("Replacement Layers (pooled)")]
    [SerializeField] private List<BrickLayer> replacementLayerPrefabs = new List<BrickLayer>();
    [SerializeField] private float layerReturnDelay = 1.5f;
    [SerializeField] private List<Material> brickMats = new List<Material>();
    [SerializeField] private MeshRenderer insideLayerRenderer;

    [Header("Pillar Scale Pulse")]
    [SerializeField] private float scaleUp = 1.1f;
    [SerializeField] private float scaleUpDuration = 0.1f;
    [SerializeField] private float scaleDownDuration = 0.2f;

    [Header("Despawn Scale FX")]
    [SerializeField] private bool ensureDespawnScaleEffect = true;
    [SerializeField, Min(1f)] private float despawnScaleMultiplier = 1.08f;
    [SerializeField, Min(0.01f)] private float despawnExpandDuration = 0.06f;
    [SerializeField, Min(0.01f)] private float despawnShrinkDuration = 0.12f;

    [Header("Hit Fly Text")]
    [SerializeField] private HitTextFlyEffect hitTextFlyEffect;
    [SerializeField] private HitComponent hitComponent;
    [SerializeField] private EffectType nonWheelHitEffectType = EffectType.Hit;

    [Header("Chain")]
    [SerializeField] private bool isChainedPillar = false;
    [SerializeField] private LockChain chain;

    // state
    private int _scaleStage;
    private float _scaleTimer;
    private Vector3 _baseScale;
    private Vector3 _originalScale;
    private int _nextReplacementIndex;

    [SerializeField] private int _currentLayerCount;
    [SerializeField] private int _currentBrickIndex;

    // Optimization: Track bricks reaching capacity bar
    private int _bricksInFlight;
    private int _bricksReachedCapacity;
    private int _pendingCapacityGain;
    private int _pendingDeliveredEventGain;
    private int _inFlightCapacityGain;
    private bool _ignoreBrickCallbacks;
    private float _nextVisualSpawnTime;
    private bool _warnedMissingChain;
    private int _lastHitFxFrame = -1;
    private readonly StatModifierCapacityData _capacityGainData = new StatModifierCapacityData();
    private readonly List<Stack<BrickLayer>> _replacementLayerPools = new List<Stack<BrickLayer>>(8);

    private void Awake()
    {
        EnsureDespawnScaleEffect();

        if (_entityType == GamePlay.Entities.EntityType.None || _entityType == GamePlay.Entities.EntityType.Item)
        {
            _entityType = GamePlay.Entities.EntityType.ResourceTower;
        }

        ResolveChain();
        EnsureHitTextEffect(true);
    }

    private void Start()
    {
        _nextReplacementIndex = 0;
        EnsureReplacementLayerPools();
        _lastHitFxFrame = -1;

        SetupChainFromData();

        _currentLayerCount = 0;
        _currentBrickIndex = (brickLayer != null && brickLayer.bricks != null && brickLayer.bricks.Count > 0)
            ? brickLayer.bricks.Count - 1
            : 0;

        _originalScale = transform.localScale;
        _baseScale = _originalScale;

        _bricksInFlight = 0;
        _bricksReachedCapacity = 0;
        _pendingCapacityGain = 0;
        _pendingDeliveredEventGain = 0;
        _inFlightCapacityGain = 0;
        _ignoreBrickCallbacks = false;
        _nextVisualSpawnTime = 0f;
    }

    private void OnEnable()
    {
        _ignoreBrickCallbacks = false;
        _lastHitFxFrame = -1;
    }

    public override void Initialize()
    {
        _entityType = GamePlay.Entities.EntityType.ResourceTower;
        _lastHitFxFrame = -1;

        var hitComp = hitComponent;
        if (hitComp != null)
        {
            hitComp.Initialize();
        }
        else
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                Debug.LogWarning($"[Pillar] No HitComponent found! Will use ItemUnit as fallback.");
#endif
        }

        InitComponent();

        if ((ActiveFlags & CapabilityFlags.Hit) != 0 && Pack.Hitable != (object)this)
            Pack.Hitable.Initialize();
        if ((ActiveFlags & CapabilityFlags.Heal) != 0) Pack.Healable.Initialize();
        if (_tutElement) _tutElement.Initialize();

        SetupChainFromData();

        // Strip existing Unity Physics if any
        if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
        if (TryGetComponent<Collider>(out var col)) Destroy(col);

        if (hitComp != null)
        {
            var colData = hitComp.GetColliderData();
            CollisionSystem.Register(hitComp, hitComp.transform);
        }
        else if ((ActiveFlags & CapabilityFlags.Hit) != 0 && Pack.Hitable != null)
        {
            CollisionSystem.Register(Pack.Hitable, transform);
        }
        RegisterEvents(true);

        PrepareInitialBrickLayer();

        _originalScale = transform.localScale;
        _baseScale = _originalScale;

        _bricksInFlight = 0;
        _bricksReachedCapacity = 0;
        _pendingCapacityGain = 0;
        _pendingDeliveredEventGain = 0;
        _inFlightCapacityGain = 0;
        _ignoreBrickCallbacks = false;
        _nextVisualSpawnTime = 0f;

        EnsureHitTextEffect(true);
        if (hitTextFlyEffect != null)
        {
            hitTextFlyEffect.enabled = true;
            hitTextFlyEffect.WarmupRuntimeCaches();
        }
    }

    private void PrepareInitialBrickLayer()
    {
        _nextReplacementIndex = 0;
        EnsureReplacementLayerPools();

        if (brickLayer != null)
        {
            brickLayer.ResetLayer(forceResetFlying: true);
            ReturnLayerToPool(brickLayer, 0);
            brickLayer = null;
        }

        if (replacementLayerPrefabs != null && replacementLayerPrefabs.Count > 0
        && replacementLayerPrefabs[0] != null)
        {
            brickLayer = GetLayerFromPool(0);
            brickLayer.transform.localPosition = Vector3.zero;
            brickLayer.transform.localRotation = Quaternion.identity;
            brickLayer.transform.localScale = Vector3.one;
            brickLayer.isActivated = true;
            brickLayer.isCached = false;
        }

        _currentLayerCount = 0;
        _currentBrickIndex = (brickLayer != null && brickLayer.bricks != null && brickLayer.bricks.Count > 0)
            ? brickLayer.bricks.Count - 1
            : 0;
    }

    protected override void HandleNonWheelCollision(IAttacker source)
    {
        int shownDamage = source != null ? Mathf.Max(1, source.Damage) : 1;
        if (isChainedPillar && chain)
        {
            HandleChainHit(shownDamage);
            return;
        }

        PlayNonWheelHitEffect();
        Pack.Healable?.TakeDamage(source);

        if (brickLayer == null) return;
        if (!brickLayer.isActivated) brickLayer.isActivated = true;

        int damage = shownDamage;
        TriggerBrickFall(source != null ? source.Position : transform.position, damage);
        PlayScalePulse();
    }

    protected override void HandleWheelCollision()
    {
        if (isChainedPillar && chain)
        {
            HandleChainHit(1);
            return;
        }

        base.HandleWheelCollision();
    }

    private void HandleChainHit(int shownDamage)
    {
        if (chain == null) return;

        chain.ApplyDamage();
        hitTextFlyEffect?.OnHit(Mathf.Max(1, shownDamage));
        PlayNonWheelHitEffect();
        if (Data != null)
            Data.Armor = Mathf.Max(0, chain.RemainingHealth);

        if (chain.RemainingHealth < 1)
        {
            chain.PlayBreakAnimation();
            isChainedPillar = false;
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

    private void SetupChainFromData()
    {
        int armor = Data != null ? Data.Armor : 0;
        isChainedPillar = armor > 0;
        ResolveChain();

        if (chain != null)
        {
            chain.AutoBind();
            chain.Initialize(armor);
            if (!isChainedPillar)
                chain.gameObject.SetActive(false);
        }
    }

    private void ResolveChain()
    {
        if (chain != null) return;
        if (_warnedMissingChain) return;
        if (!isChainedPillar) return;

        _warnedMissingChain = true;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Debug.LogWarning($"[Pillar] Missing LockChain on {name}. Assign in Inspector.");
#endif
    }

    private void TriggerBrickFall(Vector3 attackerWorldPos, int damage)
    {
        if (_brickFallSettings == null) return;
        if (brickLayer == null || brickLayer.bricks == null || brickLayer.bricks.Count == 0) return;

        Vector3 outwardDirection = (brickLayer.transform.position - attackerWorldPos);
        outwardDirection.y = 0f;
        if (outwardDirection.sqrMagnitude < 0.0001f) outwardDirection = Vector3.forward;
        outwardDirection.Normalize();

        int safeDamage = Mathf.Max(1, damage);
        int logicalBrickCount = Mathf.Max(1, bricksPerDamage * safeDamage);
        int visualBrickCount = logicalBrickCount;

        if (!forceVisualBricksMatchDamage)
        {
            // Performance mode: reduce only visual bricks, keep total capacity reward equivalent.
            if (reduceBricksPerDamage)
            {
                visualBrickCount = Mathf.Max(1, Mathf.CeilToInt(logicalBrickCount * 0.2f));
            }

            if (maxVisualBricksPerHit > 0)
            {
                visualBrickCount = Mathf.Min(visualBrickCount, maxVisualBricksPerHit);
            }

            if (maxBricksInFlight > 0)
            {
                int room = Mathf.Max(0, maxBricksInFlight - _bricksInFlight);
                visualBrickCount = Mathf.Min(visualBrickCount, room);
            }

            if (maxVisualBricksPerBurst > 0)
            {
                visualBrickCount = Mathf.Min(visualBrickCount, maxVisualBricksPerBurst);
            }
        }

        int capacityUnit = 1;
        if (_brickFallSettings != null && _brickFallSettings.CapacityData != null)
        {
            capacityUnit = Mathf.Max(1, _brickFallSettings.CapacityData.Value);
        }

        int logicalCapacity = logicalBrickCount * capacityUnit;
        float spawnInterval = forceVisualBricksMatchDamage ? 0f : Mathf.Max(0f, minVisualSpawnInterval);
        if (spawnInterval > 0f && Time.time < _nextVisualSpawnTime)
        {
            QueueCapacityGain(logicalCapacity);
            QueueDeliveredEvent(logicalCapacity);
            return;
        }

        _nextVisualSpawnTime = Time.time + spawnInterval;

        int remainingCapacity = logicalCapacity;
        int spawnedVisualCount = 0;

        for (int i = 0; i < visualBrickCount; i++)
        {
            if (_currentBrickIndex < 0)
            {
                // layer finished -> spawn replacement
                SpawnReplacementForLayer(_currentLayerCount, brickLayer);
                if (_currentBrickIndex < 0) break;
            }

            if (_currentBrickIndex >= brickLayer.bricks.Count)
                _currentBrickIndex = brickLayer.bricks.Count - 1;

            var brick = brickLayer.bricks[_currentBrickIndex];
            _currentBrickIndex--;

            if (brick == null) continue;
            spawnedVisualCount++;

            // Calculate direction PER BRICK to ensure radial fall
            // Direction = Brick Center - Pillar Center (Outwards)
            Vector3 brickOutward = brick.transform.position - transform.position;
            brickOutward.y = 0f;
            if (brickOutward.sqrMagnitude < 0.0001f) brickOutward = Vector3.forward;
            brickOutward.Normalize();

            if (!brick.gameObject.activeSelf)
                brick.gameObject.SetActive(true);

            _bricksInFlight++;

            int bricksLeftIncludingCurrent = Mathf.Max(1, visualBrickCount - i);
            int capacityForThisBrick = Mathf.Max(1, Mathf.CeilToInt((float)remainingCapacity / bricksLeftIncludingCurrent));
            remainingCapacity = Mathf.Max(0, remainingCapacity - capacityForThisBrick);
            _inFlightCapacityGain += capacityForThisBrick;

            brick.SetCapacityValue(capacityForThisBrick);
            brick.StartFall(brickOutward);
            brick.OnReachedCapacityBar -= OnBrickReachedCapacity;
            brick.OnReachedCapacityBar += OnBrickReachedCapacity;
        }

        // If we cannot spawn enough visual bricks (cap or no bricks available), grant remaining capacity directly.
        if (remainingCapacity > 0)
        {
            QueueCapacityGain(remainingCapacity);
            QueueDeliveredEvent(remainingCapacity);
        }

        // Safety: if no visual brick was actually spawned, flush immediately so reward is never delayed.
        if (spawnedVisualCount == 0)
        {
            FlushQueuedCapacityGain();
        }
    }

    private void PlayScalePulse()
    {
        _scaleStage = 1;
        _scaleTimer = 0f;
        _baseScale = transform.localScale;
    }

    private void StopScalePulse()
    {
        _scaleStage = 0;
        _scaleTimer = 0f;

        if (_originalScale != Vector3.zero)
        {
            transform.localScale = _originalScale;
        }
    }

    private void Update()
    {
        if (_scaleStage == 0 && _pendingCapacityGain <= 0)
        {
            return;
        }

        if (_scaleStage != 0)
        {
            _scaleTimer += Time.deltaTime;

            if (_scaleStage == 1)
            {
                float t = Mathf.Clamp01(_scaleTimer / Mathf.Max(0.0001f, scaleUpDuration));
                transform.localScale = Vector3.Lerp(_baseScale, _originalScale * scaleUp, t);
                if (t >= 1f)
                {
                    _scaleStage = 2;
                    _scaleTimer = 0f;
                }
            }
            else if (_scaleStage == 2)
            {
                float t = Mathf.Clamp01(_scaleTimer / Mathf.Max(0.0001f, scaleDownDuration));
                transform.localScale = Vector3.Lerp(_originalScale * scaleUp, _originalScale, t);
                if (t >= 1f)
                {
                    transform.localScale = _originalScale;
                    _scaleStage = 0;
                    _scaleTimer = 0f;
                }
            }
        }

        // Capacity updates are batched once per frame to reduce heavy UI/event churn on Luna.
        FlushQueuedCapacityGain();
        FlushDeliveredEvent();
    }

    private void OnDisable()
    {
        _ignoreBrickCallbacks = false;
        FlushQueuedCapacityGain();
        FlushDeliveredEvent();
    }

    private void OnBrickReachedCapacity(int gained)
    {
        _bricksInFlight = Mathf.Max(0, _bricksInFlight - 1);
        _inFlightCapacityGain = Mathf.Max(0, _inFlightCapacityGain - Mathf.Max(1, gained));
        _bricksReachedCapacity++;
        QueueCapacityGain(gained);
        QueueDeliveredEvent(gained);
    }

    private void QueueCapacityGain(int gained)
    {
        int safeGain = Mathf.Max(1, gained);

        // Pillar can be disabled/despawned while spawned bricks are still flying to the capacity bar.
        // In that case Update() won't run to flush batched gain, so apply immediately to keep UI/gameplay in sync.
        if (!batchCapacityGainPerFrame || !isActiveAndEnabled)
        {
            ApplyCapacityGain(safeGain);
            return;
        }

        _pendingCapacityGain += safeGain;
    }

    private void FlushQueuedCapacityGain()
    {
        if (_pendingCapacityGain <= 0) return;

        int gain = _pendingCapacityGain;
        _pendingCapacityGain = 0;
        ApplyCapacityGain(gain);
    }

    private void QueueDeliveredEvent(int gained)
    {
        _pendingDeliveredEventGain += Mathf.Max(1, gained);
        if (!isActiveAndEnabled)
        {
            FlushDeliveredEvent();
        }
    }

    private void FlushDeliveredEvent()
    {
        if (_pendingDeliveredEventGain <= 0) return;
        int gain = _pendingDeliveredEventGain;
        _pendingDeliveredEventGain = 0;
        OnCapacityBrickDelivered?.Invoke(gain);
    }

    private void ApplyCapacityGain(int gained)
    {
        if (GameplayManager.Instance == null) return;

        if (_brickFallSettings != null && _brickFallSettings.CapacityData != null)
        {
            var rewardType = _brickFallSettings.CapacityData.Type;
            if (rewardType != StatType.EvolutionPoint)
            {
                rewardType = StatType.EvolutionPoint;
            }

            _capacityGainData.Type = rewardType;
            _capacityGainData.Value = Mathf.Max(1, gained);
            _capacityGainData.Armor = 0;
            GameplayManager.Instance.ChangeStatModifierData(_capacityGainData);
        }
        else
        {
            _capacityGainData.Type = StatType.EvolutionPoint;
            _capacityGainData.Value = Mathf.Max(1, gained);
            _capacityGainData.Armor = 0;
            GameplayManager.Instance.ChangeStatModifierData(_capacityGainData);
        }
    }

    private void SpawnReplacementForLayer(int layerIndex, BrickLayer finishedLayer)
    {
        if (finishedLayer != null)
        {
            finishedLayer.isActivated = true;
            finishedLayer.isCached = true;
            ReturnLayerToPool(finishedLayer, layerIndex);
        }

        if (replacementLayerPrefabs == null || replacementLayerPrefabs.Count == 0) return;

        _nextReplacementIndex++;
        if (_nextReplacementIndex >= replacementLayerPrefabs.Count)
            _nextReplacementIndex = replacementLayerPrefabs.Count - 1;

        var newLayer = GetLayerFromPool(_nextReplacementIndex);
        newLayer.transform.localPosition = Vector3.zero;
        newLayer.transform.localRotation = Quaternion.identity;
        newLayer.transform.localScale = bricksRoot.localScale;
        newLayer.isActivated = true;

        brickLayer = newLayer;

        if (insideLayerRenderer != null && brickMats != null && brickMats.Count > 0)
        {
            int matIndex = Mathf.Clamp(_nextReplacementIndex, 0, brickMats.Count - 1);
            // Avoid per-renderer material instancing that causes RAM growth over time.
            insideLayerRenderer.sharedMaterial = brickMats[matIndex];
        }

        _currentLayerCount++;
        _currentBrickIndex = newLayer.bricks != null ? newLayer.bricks.Count - 1 : 0;
    }

    private void EnsureReplacementLayerPools()
    {
        int targetCount = replacementLayerPrefabs != null ? replacementLayerPrefabs.Count : 0;
        while (_replacementLayerPools.Count < targetCount)
        {
            _replacementLayerPools.Add(new Stack<BrickLayer>(2));
        }
    }

    private BrickLayer GetLayerFromPool(int prefabIndex)
    {
        EnsureReplacementLayerPools();
        if (replacementLayerPrefabs == null || prefabIndex < 0 || prefabIndex >= replacementLayerPrefabs.Count)
            return null;

        var stack = _replacementLayerPools[prefabIndex];
        BrickLayer layer = null;
        while (stack.Count > 0 && layer == null)
        {
            layer = stack.Pop();
        }

        if (layer == null)
        {
            var prefab = replacementLayerPrefabs[prefabIndex];
            if (prefab == null) return null;
            layer = Instantiate(prefab, bricksRoot);
        }
        else
        {
            layer.transform.SetParent(bricksRoot, false);
            layer.gameObject.SetActive(true);
        }

        layer.ResetLayer();
        layer.isCached = false;
        layer.isActivated = true;
        return layer;
    }

    private void ReturnLayerToPool(BrickLayer layer, int poolIndexHint)
    {
        if (layer == null)
            return;

        layer.ResetLayer(forceResetFlying: false);
        layer.isActivated = false;
        layer.isCached = true;
        layer.gameObject.SetActive(false);
        layer.transform.SetParent(bricksRoot, false);

        EnsureReplacementLayerPools();
        int safeIndex = Mathf.Clamp(poolIndexHint, 0, Mathf.Max(0, _replacementLayerPools.Count - 1));
        _replacementLayerPools[safeIndex].Push(layer);
    }

    private void EnsureHitTextEffect(bool allowAddRuntime)
    {
        if (hitTextFlyEffect != null) return;
        hitTextFlyEffect = GetComponentInChildren<HitTextFlyEffect>(true);
        if (hitTextFlyEffect == null && allowAddRuntime)
            hitTextFlyEffect = gameObject.AddComponent<HitTextFlyEffect>();
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

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (bricksRoot == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(bricksRoot.position, 0.25f);
    }
#endif
}

