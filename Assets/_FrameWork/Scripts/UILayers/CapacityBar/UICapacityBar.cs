using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class UICapacityBar : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    [SerializeField] private RectTransform capacityBarTransform;
    [SerializeField] private ImageSlider slider;
    [SerializeField] private TMP_Text currentLevel;
    [SerializeField] private TMP_Text nextLevel;

    [Header("VFX Settings")]
    [SerializeField] private Sprite vfxSprite; // Sprite-based VFX (UIParticle alternative)
    [SerializeField] private string vfxSpriteResourcesPath = "UILayers/CapacityIncrease";
    [SerializeField] private Color vfxSpriteColor = Color.white;
    [SerializeField] private Vector2 vfxSpriteScaleRange = new Vector2(0.8f, 1.2f);
    [SerializeField] private float vfxSpriteScaleUp = 1.4f;
    [SerializeField] private Vector2 xOffsetRange = new Vector2(-50f, 50f);
    [SerializeField] private Vector2 yOffsetRange = new Vector2(-20f, 20f);
    [SerializeField] private float vfxLifetime = 0.5f;
    [SerializeField] private float smoothDuration = 0.15f;
    [SerializeField, Range(5, 20)] private int maxVFXCount = 5;
    [SerializeField, Range(1, 8)] private int maxVFXPerBurst = 4;
    [SerializeField] private float vfxBatchWindow = 0.06f;
    [SerializeField] private bool enableVfx = false;

    [Header("Sound Effects")]
    [SerializeField] private AudioClipName levelUpSfx = AudioClipName.None;

    private int _previousLevel = -1;
    private int _previousPoints = -1;
    private bool _isFirstSetup = true;
    private Coroutine _updateCoroutine;
    private float _lastUpdateTime;
    private const float UPDATE_THROTTLE = 0.016f;
    private const float FALLBACK_POLL_INTERVAL = 0.2f;
    private float _lastFallbackPollTime;
    private int _lastObservedCapacity = int.MinValue;
    private int _lastObservedProgress = int.MinValue;

    private int _activeVFXCount = 0;
    private bool _vfxRoutineRunning;
    private Coroutine _vfxBatchCoroutine;
    private int _pendingVfxStartPoints = -1;
    private int _pendingVfxEndPoints = -1;
    private int _pendingVfxMaxPoints;
    private bool _warnedMissingGamePlay;
    private bool _warnedMissingEraConfig;
    private bool _warnedMissingEvolutionConfig;
    private bool _warnedInvalidPointsRequired;
    private bool _warnedMissingVfxSprite;
    private GamePlayVariable _cachedGamePlayVariable;
    private EraDataSO _cachedEraConfig;

    private void Awake()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
    }

    private void OnEnable()
    {
        GameEventBus.UpdateCapacityBar -= UpdateDataThrottled;
        GameEventBus.UpdateCapacityBar += UpdateDataThrottled;
        GameEventBus.GetCapacityBarPosition = GetCapacityBarPosition;
    }

    private void Start()
    {
        _isFirstSetup = true;
        if (enableVfx)
            EnsureVfxSpriteLoaded();
        SyncFromPlayerData();
        UpdateData();
    }

    private void LateUpdate()
    {
        if (!GameplayManager.IsGameStarted) return;
        if (Time.unscaledTime - _lastFallbackPollTime < FALLBACK_POLL_INTERVAL) return;
        _lastFallbackPollTime = Time.unscaledTime;

        var gamePlayConfig = ResolveGamePlayVariable();
        var evolutionVariable = gamePlayConfig?.EvolutionVariable;
        if (evolutionVariable == null) return;

        int capacity = evolutionVariable.Capacity;
        int progress = evolutionVariable.ProgressPoint;
        if (capacity == _lastObservedCapacity && progress == _lastObservedProgress) return;

        _lastObservedCapacity = capacity;
        _lastObservedProgress = progress;
        UpdateDataThrottled();
    }

    private void SyncFromPlayerData()
    {
        var gamePlayVariable = ResolveGamePlayVariable();
        if (gamePlayVariable == null) return;

        var evolutionVariable = gamePlayVariable.EvolutionVariable;

        // Only sync from DataManager when using ConfigHolder-backed variables.
        if (evolutionVariable != null &&
            ConfigHolder.Instance != null &&
            ConfigHolder.Instance.GamePlayVariable == gamePlayVariable &&
            DataManager.PlayerData != null &&
            DataManager.PlayerData.CapacityData != null)
        {
            evolutionVariable.Capacity = DataManager.PlayerData.CapacityData.Capacity;
            evolutionVariable.ProgressPoint = DataManager.PlayerData.CapacityData.Progress;
        }
    }

    private void UpdateDataThrottled()
    {
        if (_updateCoroutine != null) return;

        float timeSinceLastUpdate = Time.time - _lastUpdateTime;

        if (timeSinceLastUpdate >= UPDATE_THROTTLE)
        {
            UpdateData();
            _lastUpdateTime = Time.time;
        }
        else
        {
            _updateCoroutine = StartCoroutine(DelayedUpdate(UPDATE_THROTTLE - timeSinceLastUpdate));
        }
    }

    private IEnumerator DelayedUpdate(float waitTime)
    {
        yield return new WaitForSeconds(waitTime);
        _updateCoroutine = null;
        UpdateData();
        _lastUpdateTime = Time.time;
    }

    public void UpdateData()
    {
        var gamePlayConfig = ResolveGamePlayVariable();
        var evolutionVariable = gamePlayConfig?.EvolutionVariable;
        var currentEraConfig = ResolveEraConfig();

        if (gamePlayConfig == null || evolutionVariable == null)
        {
            if (!_warnedMissingGamePlay)
            {
                Debug.LogWarning("[UICapacityBar] Missing GamePlayVariable/EvolutionVariable. Capacity bar won't update.");
                _warnedMissingGamePlay = true;
            }
            return;
        }

        if (currentEraConfig == null)
        {
            if (!_warnedMissingEraConfig)
            {
                Debug.LogWarning("[UICapacityBar] Missing Era config. Assign GameplayManager.playableEra or ConfigHolder campaign.");
                _warnedMissingEraConfig = true;
            }
            return;
        }

        var evolutionConfig = currentEraConfig.EvolutionConfig;

        if (evolutionConfig == null)
        {
            if (!_warnedMissingEvolutionConfig)
            {
                Debug.LogWarning("[UICapacityBar] Missing EvolutionConfig in EraDataSO. Capacity bar won't update.");
                _warnedMissingEvolutionConfig = true;
            }
            return;
        }

        if (evolutionConfig && evolutionVariable)
        {
            int currentPoints = evolutionVariable.ProgressPoint;
            int currentCapacity = evolutionVariable.Capacity;
            int maxLevel = evolutionConfig.GetMaxLevel();
            if (currentCapacity >= maxLevel)
            {
                currentLevel.text = currentCapacity.ToString();
                nextLevel.text = "MAX";
                int maxPointsNeeded = evolutionConfig.GetPointsRequiredForLevel(maxLevel);
                if (maxPointsNeeded <= 0)
                {
                    if (!_warnedInvalidPointsRequired)
                    {
                        Debug.LogWarning("[UICapacityBar] Invalid max points required. Check EvolutionLevels PointsRequired.");
                        _warnedInvalidPointsRequired = true;
                    }
                    maxPointsNeeded = Mathf.Max(1, maxLevel * 10);
                }

                slider.SetValue(maxPointsNeeded, maxPointsNeeded);
                if (!_isFirstSetup)
                {
                    if (currentPoints > _previousPoints)
                        SpawnVFXForPoints(_previousPoints, currentPoints, maxPointsNeeded);
                }
                _previousLevel = currentCapacity;
                _previousPoints = currentPoints;
            }
            else
            {
                int nextCapacity = currentCapacity + 1;
                int pointsNeeded = evolutionConfig.GetPointsRequiredForLevel(nextCapacity);
                if (pointsNeeded <= 0)
                {
                    if (!_warnedInvalidPointsRequired)
                    {
                        Debug.LogWarning("[UICapacityBar] Invalid points required. Check EvolutionLevels PointsRequired.");
                        _warnedInvalidPointsRequired = true;
                    }
                    pointsNeeded = Mathf.Max(1, nextCapacity * 10);
                }
                int pointsProgress = currentPoints;

                currentLevel.text = currentCapacity.ToString();
                nextLevel.text = nextCapacity.ToString();

                if (_isFirstSetup || currentCapacity != _previousLevel || currentPoints < _previousPoints)
                {
                    // Level-up or reset: snap instantly to current progress.
                    slider.SetValue(pointsProgress, pointsNeeded);
                }
                else if (currentPoints != _previousPoints)
                {
                    slider.SetMaxValue(pointsNeeded);
                    slider.SetValueSmooth(pointsProgress, smoothDuration);
                    SpawnVFXForPoints(_previousPoints, currentPoints, pointsNeeded);
                }

                _previousLevel = currentCapacity;
                _previousPoints = currentPoints;
            }
            _isFirstSetup = false;
        }
    }

    private GamePlayVariable ResolveGamePlayVariable()
    {
        if (_cachedGamePlayVariable != null)
            return _cachedGamePlayVariable;

        if (GameplayManager.Instance != null && GameplayManager.Instance.gamePlayVariable != null)
        {
            _cachedGamePlayVariable = GameplayManager.Instance.gamePlayVariable;
            return _cachedGamePlayVariable;
        }

        if (ConfigHolder.Instance != null)
        {
            _cachedGamePlayVariable = ConfigHolder.Instance.GamePlayVariable;
            return _cachedGamePlayVariable;
        }

        return null;
    }

    private EraDataSO ResolveEraConfig()
    {
        if (_cachedEraConfig != null)
            return _cachedEraConfig;

        if (GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
        {
            _cachedEraConfig = GameplayManager.Instance.PlayableEra;
            return _cachedEraConfig;
        }

        if (ConfigHolder.Instance != null)
        {
            _cachedEraConfig = ConfigHolder.Instance.GetCurrentEraConfig();
            return _cachedEraConfig;
        }

        return null;
    }

    // Level-up animation removed to keep capacity level in sync immediately.

    private void OnDestroy()
    {
        StopAllCoroutines();
    }

    private void OnDisable()
    {
        GameEventBus.UpdateCapacityBar -= UpdateDataThrottled;
        if (GameEventBus.GetCapacityBarPosition == GetCapacityBarPosition)
            GameEventBus.GetCapacityBarPosition = null;
    }

    private Vector3[] _worldCorners = new Vector3[4];

    private void SpawnVFXForPoints(int previousPoints, int currentPoints, int maxPoints)
    {
        if (!enableVfx) return;
        EnsureVfxSpriteLoaded();
        if (vfxSprite == null || !slider || maxPoints <= 0) return;
        int pointsGained = currentPoints - previousPoints;
        if (pointsGained <= 0) return;
        if (_activeVFXCount >= maxVFXCount) return;

        if (_vfxBatchCoroutine == null)
        {
            _pendingVfxStartPoints = previousPoints;
            _pendingVfxEndPoints = currentPoints;
            _pendingVfxMaxPoints = maxPoints;
            _vfxBatchCoroutine = StartCoroutine(FlushVfxBatch());
        }
        else
        {
            if (_pendingVfxStartPoints < 0) _pendingVfxStartPoints = previousPoints;
            _pendingVfxEndPoints = Mathf.Max(_pendingVfxEndPoints, currentPoints);
            _pendingVfxMaxPoints = maxPoints;
        }
    }

    private IEnumerator FlushVfxBatch()
    {
        if (vfxBatchWindow > 0f)
        {
            yield return new WaitForSeconds(vfxBatchWindow);
        }

        int startPoints = _pendingVfxStartPoints;
        int endPoints = _pendingVfxEndPoints;
        int maxPoints = _pendingVfxMaxPoints;

        _pendingVfxStartPoints = -1;
        _pendingVfxEndPoints = -1;
        _vfxBatchCoroutine = null;

        if (endPoints <= startPoints || maxPoints <= 0) yield break;
        if (_vfxRoutineRunning) yield break;

        _vfxRoutineRunning = true;
        yield return StartCoroutine(SpawnVFXSequentially(startPoints, endPoints, maxPoints));
        _vfxRoutineRunning = false;
    }

    private IEnumerator SpawnVFXSequentially(int previousPoints, int currentPoints, int maxPoints)
    {
        int pointsGained = currentPoints - previousPoints;
        if (pointsGained <= 0)
        {
            yield break;
        }

        int availableSlots = maxVFXCount - _activeVFXCount;
        if (availableSlots <= 0)
        {
            yield break;
        }

        int vfxToSpawn = Mathf.Min(pointsGained, availableSlots, maxVFXPerBurst);
        float pointStep = pointsGained > vfxToSpawn ? (float)pointsGained / vfxToSpawn : 1f;
        float delay = smoothDuration / Mathf.Max(1, vfxToSpawn);

        Rect rect = capacityBarTransform.rect;
        float centerX = rect.center.x;

        for (int i = 0; i < vfxToSpawn; i++)
        {
            int targetPoint = previousPoints + Mathf.RoundToInt((i + 1) * pointStep);
            targetPoint = Mathf.Clamp(targetPoint, previousPoints + 1, currentPoints);
            float progress = Mathf.Clamp01((float)targetPoint / maxPoints);
            float progressY = Mathf.Lerp(rect.yMin, rect.yMax, progress);

            // Simple random spawn logic for demo parity
            float randomOffsetY = UnityEngine.Random.Range(yOffsetRange.x, yOffsetRange.y);
            float randomOffsetX = UnityEngine.Random.Range(xOffsetRange.x, xOffsetRange.y);

            // Note: In real logic we used progress to chart Y. 
            // Simplified here: spawn randomly near bar center or slider handle position if we calculated it.
            // Using logic from original:
            // float progress = (float)points / maxPoints; 
            // Here just random around center for visual feedback.

            _activeVFXCount++;

            var go = new GameObject("VFX_IncreaseCapacityBar_Sprite", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var img = go.GetComponent<Image>();
            img.sprite = vfxSprite;
            img.color = vfxSpriteColor;
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.maskable = false;

            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(capacityBarTransform, false);
            rt.localPosition = new Vector3(centerX + randomOffsetX, progressY + randomOffsetY, 0);
            float scale = UnityEngine.Random.Range(vfxSpriteScaleRange.x, vfxSpriteScaleRange.y);
            rt.localScale = new Vector3(scale, scale, scale);

            StartCoroutine(DespawnVFXSprite(img, vfxLifetime));

            if (i < vfxToSpawn - 1) yield return new WaitForSeconds(delay);
        }
    }

    private IEnumerator DespawnVFXSprite(Image img, float delay)
    {
        if (img == null)
        {
            _activeVFXCount = Mathf.Max(0, _activeVFXCount - 1);
            yield break;
        }

        float t = 0f;
        RectTransform rt = img.rectTransform;
        Vector3 startScale = rt.localScale;
        Vector3 endScale = startScale * vfxSpriteScaleUp;
        Color startColor = img.color;

        while (t < delay)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / delay);
            rt.localScale = Vector3.Lerp(startScale, endScale, k);
            img.color = new Color(startColor.r, startColor.g, startColor.b, Mathf.Lerp(startColor.a, 0f, k));
            yield return null;
        }

        Destroy(img.gameObject);
        _activeVFXCount = Mathf.Max(0, _activeVFXCount - 1);
    }

    private void EnsureVfxSpriteLoaded()
    {
        if (vfxSprite != null && vfxSprite.texture != null) return;
        if (string.IsNullOrEmpty(vfxSpriteResourcesPath)) return;

        vfxSprite = Resources.Load<Sprite>(vfxSpriteResourcesPath);
        if (vfxSprite == null && !_warnedMissingVfxSprite)
        {
            Debug.LogWarning($"[UICapacityBar] Missing VFX sprite at Resources/{vfxSpriteResourcesPath}. Add sprite to Resources to show VFX in Luna build.");
            _warnedMissingVfxSprite = true;
        }
    }


    public Vector3 GetCapacityBarPosition()
    {
        if (!canvas) canvas = GetComponentInParent<Canvas>();
        Camera cam = (canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;

        if (!slider) return Vector3.zero;
        float progress = slider.GetProgress();

        capacityBarTransform.GetWorldCorners(_worldCorners);

        float xPos = (_worldCorners[0].x + _worldCorners[3].x) * 0.5f;
        float yPos = _worldCorners[0].y + (_worldCorners[1].y - _worldCorners[0].y) * progress;

        Vector3 worldPosition = new Vector3(xPos, yPos, _worldCorners[0].z);

        // Contract with CameraFollow.GetCapacityBarWorldPosition:
        // this callback must provide a screen-space point.
        return RectTransformUtility.WorldToScreenPoint(cam, worldPosition);
    }

}
