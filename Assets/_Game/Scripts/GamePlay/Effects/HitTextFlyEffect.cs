using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

public class HitTextFlyEffect : MonoBehaviour
{
    [SerializeField] private GamePlay.HealthSystems.HealthComponent healthComponent;
    [SerializeField] private TMP_Text healthTextPrefab;
    [SerializeField] private bool autoResolveHealthComponent = true;
    [SerializeField] private string defaultTextPrefabResourcePath = "SpawnableObject/HitDamage";
    [SerializeField] private float flyUpDistance = 2f;
    [SerializeField] private float flyUpDuration = 0.25f;
    [SerializeField] private float fallDownDistance = 2.5f;
    [SerializeField] private float fallDuration = 0.5f;
    [SerializeField] private float horizontalRandomRange = 0f;
    [SerializeField] private float heightOffset = 2f;
    [SerializeField] private float zOffset = 0f;
    [SerializeField, Min(0)] private int prewarmPoolCount = 12;

    private static readonly Stack<HitTextController> controllerPool = new Stack<HitTextController>();
    private static readonly List<HitTextController> activeControllers = new List<HitTextController>(64);
    private static readonly HashSet<int> warmedTextPrefabIds = new HashSet<int>();

    private bool _isSubscribed;
    private int _lastHitFrame = -1;

    public static void TickActiveControllers(float deltaTime)
    {
        for (int i = activeControllers.Count - 1; i >= 0; i--)
        {
            var controller = activeControllers[i];
            if (controller != null && controller.Step(deltaTime))
            {
                continue;
            }

            int last = activeControllers.Count - 1;
            activeControllers[i] = activeControllers[last];
            activeControllers.RemoveAt(last);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        TryResolveDependencies();
    }
#endif

    private void Awake()
    {
        TryResolveDependencies();
        WarmupRuntimeCaches();
    }

    private void OnEnable()
    {
        TryResolveDependencies();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_isSubscribed || healthComponent == null) return;
        healthComponent.OnTakeDamaged += OnHit;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed || healthComponent == null) return;
        healthComponent.OnTakeDamaged -= OnHit;
        _isSubscribed = false;
    }

    private void TryResolveDependencies()
    {
        if (healthComponent == null && autoResolveHealthComponent)
        {
            healthComponent = GetComponent<GamePlay.HealthSystems.HealthComponent>();
            if (healthComponent == null)
            {
                Transform currentParent = transform.parent;
                while (healthComponent == null && currentParent != null)
                {
                    healthComponent = currentParent.GetComponent<GamePlay.HealthSystems.HealthComponent>();
                    currentParent = currentParent.parent;
                }
            }

            if (healthComponent == null)
            {
                healthComponent = GetComponentInChildren<GamePlay.HealthSystems.HealthComponent>(true);
            }
        }

        if (healthTextPrefab == null && !string.IsNullOrEmpty(defaultTextPrefabResourcePath))
        {
            healthTextPrefab = Resources.Load<TMP_Text>(defaultTextPrefabResourcePath);
        }
    }

    public void WarmupRuntimeCaches()
    {
        TryResolveDependencies();
        WarmupHitTextPoolIfNeeded();
    }

    private void WarmupHitTextPoolIfNeeded()
    {
        if (healthTextPrefab == null) return;
        if (PoolManager.Instance == null) return;

        int prefabId = healthTextPrefab.GetInstanceID();
        if (!warmedTextPrefabIds.Add(prefabId)) return;

        int warmCount = Mathf.Max(0, prewarmPoolCount);
        for (int i = 0; i < warmCount; i++)
        {
            var text = PoolManager.Instance.Get(healthTextPrefab);
            if (text == null) continue;
            if (text.gameObject.activeSelf)
            {
                text.gameObject.SetActive(false);
            }
        }
    }

    public void OnHit(int damage)
    {
        if (damage <= 0) return;

        if (_lastHitFrame == Time.frameCount) return;
        _lastHitFrame = Time.frameCount;

        if (healthTextPrefab == null)
        {
            TryResolveDependencies();
        }

        if (healthTextPrefab == null) return;

        HitTextController controller = controllerPool.Count > 0
            ? controllerPool.Pop()
            : new HitTextController();

        float horizontalOffset = horizontalRandomRange > 0f
            ? Random.Range(-horizontalRandomRange, horizontalRandomRange)
            : 0f;

        bool activated = controller.Initialize(
            healthTextPrefab,
            transform.position + new Vector3(0f, heightOffset, zOffset),
            damage,
            flyUpDistance,
            flyUpDuration,
            fallDownDistance,
            fallDuration,
            horizontalOffset,
            () => controllerPool.Push(controller));

        if (activated)
        {
            activeControllers.Add(controller);
        }
        else
        {
            controllerPool.Push(controller);
        }
    }

    private sealed class HitTextController
    {
        private TMP_Text _textInstance;
        private Transform _textTransform;
        private Vector3 _startPos;
        private float _midX;
        private float _upY;
        private float _downY;
        private float _flyUpDuration;
        private float _fallDuration;
        private float _fadeInDuration;
        private float _elapsed;
        private Action _onComplete;

        public bool Initialize(
            TMP_Text prefab,
            Vector3 startPos,
            int damage,
            float flyUpDistance,
            float flyUpDuration,
            float fallDownDistance,
            float fallDuration,
            float horizontalOffset,
            Action onComplete)
        {
            Complete();

            if (PoolManager.Instance == null) return false;

            _textInstance = PoolManager.Instance.Get(prefab);
            if (_textInstance == null) return false;

            _textTransform = _textInstance.transform;
            _startPos = startPos;
            _midX = startPos.x + horizontalOffset;
            _upY = startPos.y + flyUpDistance;
            _downY = _upY - fallDownDistance;
            _flyUpDuration = Mathf.Max(0.0001f, flyUpDuration);
            _fallDuration = Mathf.Max(0.0001f, fallDuration);
            _fadeInDuration = Mathf.Max(0.0001f, _flyUpDuration * 0.3f);
            _elapsed = 0f;
            _onComplete = onComplete;

            if (!_textInstance.gameObject.activeSelf)
            {
                _textInstance.gameObject.SetActive(true);
            }

            _textTransform.position = startPos;
            _textInstance.text = damage.ToString();

            Color color = _textInstance.color;
            color.a = 0f;
            _textInstance.color = color;

            return true;
        }

        public bool Step(float deltaTime)
        {
            if (_textInstance == null || _textTransform == null)
            {
                Complete();
                return false;
            }

            _elapsed += deltaTime;
            float totalDuration = _flyUpDuration + _fallDuration;
            float x = _midX;
            float y;
            float alpha;

            if (_elapsed <= _flyUpDuration)
            {
                float t = Mathf.Clamp01(_elapsed / _flyUpDuration);
                float eased = EaseOutQuad(t);
                x = Mathf.Lerp(_startPos.x, _midX, eased);
                y = Mathf.Lerp(_startPos.y, _upY, eased);

                float fadeT = Mathf.Clamp01(_elapsed / _fadeInDuration);
                alpha = EaseOutQuad(fadeT);
            }
            else
            {
                float fallElapsed = Mathf.Min(_elapsed - _flyUpDuration, _fallDuration);
                float t = Mathf.Clamp01(fallElapsed / _fallDuration);
                float eased = EaseInQuad(t);
                y = Mathf.Lerp(_upY, _downY, eased);
                alpha = 1f - eased;
            }

            Vector3 position = _textTransform.position;
            position.x = x;
            position.y = y;
            position.z = _startPos.z;
            _textTransform.position = position;

            Color color = _textInstance.color;
            color.a = alpha;
            _textInstance.color = color;

            if (_elapsed < totalDuration)
            {
                return true;
            }

            Complete();
            return false;
        }

        private void Complete()
        {
            if (_textInstance != null && _textInstance.gameObject.activeSelf)
            {
                _textInstance.gameObject.SetActive(false);
            }

            _textInstance = null;
            _textTransform = null;
            _elapsed = 0f;

            var onComplete = _onComplete;
            _onComplete = null;
            onComplete?.Invoke();
        }

        private static float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        private static float EaseInQuad(float t)
        {
            return t * t;
        }
    }
}
