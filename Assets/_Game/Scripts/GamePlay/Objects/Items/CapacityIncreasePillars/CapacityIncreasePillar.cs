using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.CollisionSystems;
using GamePlay.CombatSystems;
using GamePlay.ComponentSystems;
using GamePlay.Items;
using UnityEngine;
using UnityEngine.Serialization;
using DG.Tweening;

#if UNITY_EDITOR
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
    [SerializeField, FormerlySerializedAs("halveBricksPerDamage")]
    private bool reduceBricksPerDamage = false;
    [SerializeField] private int maxVisualBricksPerHit = 8;
    [SerializeField] private int maxBricksInFlight = 28;
    [SerializeField] private int maxVisualBricksPerBurst = 3;
    [SerializeField] private float minVisualSpawnInterval = 0.05f;
    [SerializeField] private bool forceVisualBricksMatchDamage = true;
    [SerializeField] private bool batchCapacityGainPerFrame = true;
    [SerializeField] private BrickLayer brickLayer;
    public BrickLayer BrickLayerPrefab => brickLayer;
    [SerializeField] private BrickFallSettings _brickFallSettings;

    [Header("Pre-placed Layers")]
    [SerializeField] private List<BrickLayer> preplacedLayers = new List<BrickLayer>();
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

    // state
    private int _scaleStage;
    private float _scaleTimer;
    private Vector3 _baseScale;
    private Vector3 _originalScale;
    private int _nextReplacementIndex;
    private Coroutine _scalePulseRoutine;

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
    private int _lastHitFxFrame = -1;
    private readonly StatModifierCapacityData _capacityGainData = new StatModifierCapacityData();

    protected override void Awake()
    {
        base.Awake();


        if (bricksRoot != null)
        {
            preplacedLayers.Clear();
            preplacedLayers.AddRange(bricksRoot.GetComponentsInChildren<BrickLayer>(true));
        }

        if (_entityType == GamePlay.Entities.EntityType.None || _entityType == GamePlay.Entities.EntityType.Item)
        {
            _entityType = GamePlay.Entities.EntityType.ResourceTower;
        }

        EnsureHitTextEffect(true);
    }

    private void Start()
    {
        _nextReplacementIndex = 0;
        _lastHitFxFrame = -1;

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
        // if (_tutElement) _tutElement.Initialize();

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

        for (int i = 0; i < preplacedLayers.Count; i++)
        {
            var layer = preplacedLayers[i];
            if (layer == null) continue;

            layer.ResetLayer(forceResetFlying: true);
            if (i == 0)
            {
                layer.isActivated = true;
                layer.isCached = false;
                layer.gameObject.SetActive(true);
            }
            else
            {
                layer.isActivated = false;
                layer.isCached = true;
                layer.gameObject.SetActive(false);
            }
        }

        brickLayer = preplacedLayers.Count > 0 ? preplacedLayers[0] : null;
        _currentLayerCount = 0;
        _currentBrickIndex = (brickLayer != null && brickLayer.bricks != null && brickLayer.bricks.Count > 0)
            ? brickLayer.bricks.Count - 1
            : 0;
    }

    protected override void HandleNonWheelCollision(IAttacker source)
    {
        if (source != null && source.EntityType == GamePlay.Entities.EntityType.Character)
        {
            StartCoroutine(CoScaleDownAndDespawn());
            return;
        }

        int shownDamage = source != null ? Mathf.Max(1, source.Damage) : 1;

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
        PlayScalePulse();
        StartCoroutine(CoHandleWheelCollisionAfterPulse());
    }

    private IEnumerator CoHandleWheelCollisionAfterPulse()
    {
        float waitTime = Mathf.Max(0f, scaleUpDuration + scaleDownDuration);
        if (waitTime > 0f)
        {
            yield return new WaitForSeconds(waitTime);
        }

        base.HandleWheelCollision();
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
        Pack.Effector?.PlayEffect(nonWheelHitEffectType, transform.position + transform.up * 2.7f + transform.forward * -2.5f, Quaternion.identity, transform);
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
                visualBrickCount = Mathf.Max(1, Mathf.CeilToInt(logicalBrickCount * 0.8f));
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
        if (!isActiveAndEnabled) return;

        Vector3 currentScale = transform.localScale;
        if (_originalScale == Vector3.zero)
        {
            _originalScale = currentScale;
        }

        if (_scalePulseRoutine != null)
        {
            StopCoroutine(_scalePulseRoutine);
            _scalePulseRoutine = null;
        }

        _scalePulseRoutine = StartCoroutine(CoScalePulse(currentScale));
    }

    private System.Collections.IEnumerator CoScalePulse(Vector3 from)
    {
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

        if (_originalScale != Vector3.zero)
        {
            transform.localScale = _originalScale;
        }
    }

    private void OnDisable()
    {
        StopScalePulse();
        _ignoreBrickCallbacks = false;
        FlushQueuedCapacityGain();
        FlushDeliveredEvent();

        if (preplacedLayers != null)
        {
            for (int i = 0; i < preplacedLayers.Count; i++)
            {
                if (preplacedLayers[i] != null)
                {
                    preplacedLayers[i].ResetLayer(forceResetFlying: true);
                }
            }
        }
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

        if (!batchCapacityGainPerFrame || !isActiveAndEnabled)
        {
            ApplyCapacityGain(safeGain);
            return;
        }

        bool wasEmpty = _pendingCapacityGain == 0;
        _pendingCapacityGain += safeGain;
        if (wasEmpty)
        {
            DOVirtual.DelayedCall(0.02f, FlushQueuedCapacityGain, false).SetId(this);
        }
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
        bool wasEmpty = _pendingDeliveredEventGain == 0;
        _pendingDeliveredEventGain += Mathf.Max(1, gained);
        if (!isActiveAndEnabled)
        {
            FlushDeliveredEvent();
        }
        else if (wasEmpty)
        {
            DOVirtual.DelayedCall(0.02f, FlushDeliveredEvent, false).SetId(this);
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
            // Kept active so flying bricks don't disappear abruptly
        }

        if (preplacedLayers == null || preplacedLayers.Count == 0) return;

        _nextReplacementIndex++;
        if (_nextReplacementIndex >= preplacedLayers.Count)
        {
            _nextReplacementIndex = 0; // Loop back to reuse existing layers
        }

        var newLayer = preplacedLayers[_nextReplacementIndex];
        if (newLayer != null)
        {
            newLayer.ResetLayer(forceResetFlying: false);
            newLayer.gameObject.SetActive(true);
            newLayer.isActivated = true;
            newLayer.isCached = false;
            brickLayer = newLayer;
        }

        if (insideLayerRenderer != null && brickMats != null && brickMats.Count > 0)
        {
            int matIndex = Mathf.Clamp(_nextReplacementIndex, 0, brickMats.Count - 1);
            insideLayerRenderer.sharedMaterial = brickMats[matIndex];
        }

        _currentLayerCount++;
        _currentBrickIndex = (brickLayer != null && brickLayer.bricks != null) ? brickLayer.bricks.Count - 1 : 0;
    }

    private void EnsureHitTextEffect(bool allowAddRuntime)
    {
        if (hitTextFlyEffect != null) return;
#if UNITY_EDITOR
        Debug.LogWarning($"[CapacityIncreasePillar] hitTextFlyEffect missing on {name}. Please assign in Inspector.");
#endif
    }

    protected override void DespawnInterval()
    {
        StopScalePulse();

        base.DespawnInterval();
    }

#if UNITY_EDITOR
    private new void OnDrawGizmosSelected()
    {
        if (bricksRoot == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(bricksRoot.position, 0.25f);
    }
#endif

    private bool _isArmyDespawning = false;
    private IEnumerator CoScaleDownAndDespawn()
    {
        if (_isArmyDespawning) yield break;
        _isArmyDespawning = true;

        if (Pack.Hitable != null)
        {
            GamePlay.CollisionSystems.CollisionSystem.Unregister(Pack.Hitable);
        }

        float t = 0;
        float duration = despawnShrinkDuration;
        Vector3 startScale = transform.localScale;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t / duration);
            yield return null;
        }
        transform.localScale = Vector3.zero;

        deathScaleEffect = null;
        DespawnInterval();
    }
}

