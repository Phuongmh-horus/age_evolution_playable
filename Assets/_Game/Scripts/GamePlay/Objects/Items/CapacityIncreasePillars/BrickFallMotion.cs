using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class BrickFallMotion : MonoBehaviour
{
    [SerializeField] private BrickFallSettings settings;
    private static readonly List<BrickFallMotion> activeMotions = new List<BrickFallMotion>(64);
    public float TotalMotionTime { get; private set; }
    public event Action OnReachedCapacityBar;

    private bool _active;
    private bool _registeredForTick;
    private float _fallPhaseElapsed;
    private float _bouncePhaseElapsed;
    private bool _isInBouncePhase;

    private Vector3 _fallStartPosition;
    private Vector3 _fallTargetPosition;
    private Vector3 _bounceStartPosition;
    private Vector3 _bounceEndPosition;
    private Vector3 _angularVelocity;
    private Vector3 _initialScale;
    private float _calculatedFallDuration;
    private float _elapsedLifetime;
    private bool _isFlyingToCapacity;
    private float _flyElapsed;
    private float _flyDurationWithOffset;
    private Vector3 _flyStartPosition;
    private Vector3 _flyTargetPosition;
    private float _invFallDuration;
    private float _invBounceDuration;
    private float _invFlyDuration;

    private Transform _originalParent;
    private Quaternion _originalRotation;
    private Vector3 _originalPosition;
    private Transform _transform;
    private Rigidbody _cachedRigidbody;

    public static void TickActiveMotions(float deltaTime)
    {
        for (int i = activeMotions.Count - 1; i >= 0; i--)
        {
            var motion = activeMotions[i];
            if (motion == null)
            {
                RemoveAtSwapBack(i);
                continue;
            }

            if (motion._active && motion.StepFall(deltaTime))
            {
                continue;
            }

            motion.UnregisterTick();
            RemoveAtSwapBack(i);
        }
    }

    private void Awake()
    {
        _transform = transform;
        _cachedRigidbody = GetComponent<Rigidbody>();
        _initialScale = _transform.localScale;
        _originalPosition = _transform.localPosition;
        _originalRotation = _transform.localRotation;
        _originalParent = _transform.parent;
        enabled = false;
    }

    private void OnDisable()
    {
        UnregisterTick();

        if (_active)
        {
            ResetMotion();
        }

        OnReachedCapacityBar = null;
    }

    public void StartFall(Vector3 outwardDirection)
    {
        if (_active) return;
        if (settings == null)
        {
            Debug.LogError("[BrickFallMotion] Settings is null.");
            return;
        }

        outwardDirection.y = 0f;
        if (outwardDirection.sqrMagnitude < 0.0001f)
        {
            outwardDirection = Vector3.forward;
        }

        if (_transform.parent != null)
        {
            _transform.SetParent(null, true);
        }

        _fallStartPosition = _transform.position;

        float heightDifference = Mathf.Max(0.1f, _fallStartPosition.y - settings.GroundY);
        float heightRatio = Mathf.Clamp01(heightDifference / settings.MaxPillarHeight);

        float distanceScale = settings.MinDistanceRatio + (settings.MaxDistanceRatio - settings.MinDistanceRatio) * heightRatio;
        float scaledDistance = settings.HorizontalDistance * distanceScale;

        Vector3 adjustedDirection = outwardDirection.normalized * settings.LaunchDistanceMultiplier;
        _fallTargetPosition = _fallStartPosition + adjustedDirection * scaledDistance;
        _fallTargetPosition.y = settings.GroundY;

        _bounceStartPosition = _fallTargetPosition;
        float bounceOutwardDistance = scaledDistance * settings.BounceOutwardRatio;
        _bounceEndPosition = _fallTargetPosition + adjustedDirection * bounceOutwardDistance;
        _bounceEndPosition.y = settings.GroundY;

        float arcPeakHeight = heightDifference + settings.FallArcHeight;
        _calculatedFallDuration = Mathf.Sqrt(2f * arcPeakHeight / 9.8f) * 0.8f;
        _invFallDuration = 1f / Mathf.Max(0.0001f, _calculatedFallDuration);
        _invBounceDuration = 1f / Mathf.Max(0.0001f, settings.BounceOnGroundDuration);

        _fallPhaseElapsed = 0f;
        _bouncePhaseElapsed = 0f;
        _isInBouncePhase = false;
        _elapsedLifetime = 0f;
        _isFlyingToCapacity = false;
        _flyElapsed = 0f;
        _transform.localScale = _initialScale;
        _active = true;

        var flatDir = outwardDirection;
        flatDir.y = 0f;
        var sideAxis = Vector3.Cross(flatDir.normalized, Vector3.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            sideAxis = Vector3.right;
        }

        float tiltSpeed = Random.Range(settings.TiltSpeedRange.x, settings.TiltSpeedRange.y);
        float spinSpeed = Random.Range(settings.SpinSpeedRange.x, settings.SpinSpeedRange.y);
        if (settings.RandomizeTiltDirection && Random.value > 0.5f)
        {
            tiltSpeed = -tiltSpeed;
        }

        _angularVelocity = sideAxis.normalized * tiltSpeed + Vector3.up * spinSpeed;

        if (_cachedRigidbody != null)
        {
            _cachedRigidbody.isKinematic = true;
            _cachedRigidbody.useGravity = false;
        }

        RegisterTick();
    }

    public void ResetMotion()
    {
        UnregisterTick();
        _active = false;
        _fallPhaseElapsed = 0f;
        _bouncePhaseElapsed = 0f;
        _isInBouncePhase = false;
        _angularVelocity = Vector3.zero;
        _elapsedLifetime = 0f;
        _isFlyingToCapacity = false;
        _flyElapsed = 0f;
        TotalMotionTime = 0f;
        OnReachedCapacityBar = null;
        enabled = false;
    }

    private bool StepFall(float dt)
    {
        _elapsedLifetime += dt;

        if (_isFlyingToCapacity)
        {
            return StepFlyToCapacity(dt);
        }

        if (!_isInBouncePhase)
        {
            _fallPhaseElapsed += dt;
            float t = Mathf.Clamp01(_fallPhaseElapsed * _invFallDuration);

            Vector3 flatStart = new Vector3(_fallStartPosition.x, 0f, _fallStartPosition.z);
            Vector3 flatTarget = new Vector3(_fallTargetPosition.x, 0f, _fallTargetPosition.z);
            Vector3 flatPos = Vector3.Lerp(flatStart, flatTarget, t);

            float arcProgress = Mathf.Sin(t * Mathf.PI);
            float currentHeight = Mathf.Lerp(_fallStartPosition.y, settings.GroundY, t) + arcProgress * settings.FallArcHeight;

            _transform.position = new Vector3(flatPos.x, currentHeight, flatPos.z);

            if (t < 0.7f)
            {
                _transform.Rotate(_angularVelocity * dt, Space.World);
            }

            if (t >= 1f)
            {
                _isInBouncePhase = true;
                _bouncePhaseElapsed = 0f;
                _transform.position = new Vector3(flatPos.x, settings.GroundY, flatPos.z);
                _angularVelocity *= 0.3f;
            }
        }
        else
        {
            _bouncePhaseElapsed += dt;
            float t = Mathf.Clamp01(_bouncePhaseElapsed * _invBounceDuration);

            float bounceHeight = 0f;
            float bounceProgress = t * settings.BounceCount;
            int currentBounce = Mathf.FloorToInt(bounceProgress);
            float bounceT = bounceProgress - currentBounce;

            if (currentBounce < settings.BounceCount)
            {
                float dampingFactor = Mathf.Pow(settings.BounceDamping, currentBounce);
                float maxBounceHeight = settings.BounceHeight * dampingFactor;
                bounceHeight = Mathf.Sin(bounceT * Mathf.PI) * maxBounceHeight;
            }

            Vector3 flatStart = new Vector3(_bounceStartPosition.x, 0f, _bounceStartPosition.z);
            Vector3 flatEnd = new Vector3(_bounceEndPosition.x, 0f, _bounceEndPosition.z);
            Vector3 flatPos = Vector3.Lerp(flatStart, flatEnd, t);

            _transform.position = new Vector3(flatPos.x, settings.GroundY + bounceHeight, flatPos.z);
            _transform.Rotate(_angularVelocity * dt * 0.5f, Space.World);

            if (t >= 1f)
            {
                StartFlyToCapacity();
            }
        }

        return _active;
    }

    private void StartFlyToCapacity()
    {
        _isFlyingToCapacity = true;
        _flyElapsed = 0f;
        _flyStartPosition = _transform.position;

        float offset = Random.Range(-settings.FlyDurationOffset, settings.FlyDurationOffset);
        _flyDurationWithOffset = Mathf.Max(0.1f, settings.FlyDuration + offset);
        _invFlyDuration = 1f / Mathf.Max(0.0001f, _flyDurationWithOffset);

        if (!TryResolveCapacityBarTarget(out _flyTargetPosition))
        {
            _flyTargetPosition = _transform.position + Vector3.up * 2f;
        }

        _angularVelocity = Vector3.zero;
    }

    private bool TryResolveCapacityBarTarget(out Vector3 target)
    {
        target = Vector3.zero;

        Camera cam = null;
        if (CameraFollow.Instance != null)
        {
            cam = CameraFollow.Instance.GetCamera();
        }
        if (cam == null)
        {
            cam = Camera.main;
        }

        if (GameEventBus.GetCapacityBarPosition != null)
        {
            Vector3 pos = GameEventBus.GetCapacityBarPosition.Invoke();

            bool looksLikeScreenPoint =
                pos.x >= -16f && pos.x <= Screen.width + 16f &&
                pos.y >= -16f && pos.y <= Screen.height + 16f;

            if (looksLikeScreenPoint && cam != null)
            {

                Ray ray = cam.ScreenPointToRay(pos);
                float depth = Vector3.Dot(
                    _originalParent != null ? _originalParent.position - cam.transform.position
                                           : _flyStartPosition - cam.transform.position,
                    cam.transform.forward);
                depth = Mathf.Max(2f, depth);
                target = ray.origin + ray.direction * depth;
                return true;
            }

            target = pos;
            return true;
        }

        if (CameraFollow.Instance != null)
        {
            target = CameraFollow.Instance.GetCapacityBarWorldPosition();
            return true;
        }

        return false;
    }

    private bool StepFlyToCapacity(float dt)
    {
        _flyElapsed += dt;
        float t = Mathf.Clamp01(_flyElapsed * _invFlyDuration);

        Vector3 flatStart = new Vector3(_flyStartPosition.x, 0f, _flyStartPosition.z);
        Vector3 flatTarget = new Vector3(_flyTargetPosition.x, 0f, _flyTargetPosition.z);
        Vector3 flatPos = Vector3.Lerp(flatStart, flatTarget, t);

        float arcProgress = Mathf.Sin(t * Mathf.PI);
        float currentHeight = Mathf.Lerp(_flyStartPosition.y, _flyTargetPosition.y, t) + arcProgress * settings.FlyArcHeight;

        _transform.position = new Vector3(flatPos.x, currentHeight, flatPos.z);

        float scaleT = Mathf.Clamp01(_flyElapsed / settings.FlyScaleDownDuration);
        _transform.localScale = Vector3.Lerp(_initialScale, Vector3.zero, scaleT);

        if (t < 1f)
        {
            return true;
        }

        TotalMotionTime = _elapsedLifetime;
        _active = false;

        var callback = OnReachedCapacityBar;
        OnReachedCapacityBar = null;
        if (callback != null)
        {
            try
            {
                callback.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogError($"[BrickFallMotion] OnReachedCapacityBar error: {e}");
            }
        }

        ResetBrick();
        gameObject.SetActive(false);
        return false;
    }

    public bool IsActivated() => _active;

    public void ResetBrick()
    {
        UnregisterTick();
        if (_transform == null) return;

        if (_originalParent == null)
        {
            Destroy(gameObject);
            return;
        }

        if (_transform.parent != _originalParent)
        {
            _transform.SetParent(_originalParent, false);
        }

        _transform.localScale = _initialScale;
        _transform.localPosition = _originalPosition;
        _transform.localRotation = _originalRotation;
        _active = false;
    }

    private void RegisterTick()
    {
        if (_registeredForTick) return;
        _registeredForTick = true;
        activeMotions.Add(this);
    }

    private void UnregisterTick()
    {
        _registeredForTick = false;
    }

    private static void RemoveAtSwapBack(int index)
    {
        int last = activeMotions.Count - 1;
        if (index < 0 || index > last) return;

        activeMotions[index] = activeMotions[last];
        activeMotions.RemoveAt(last);
    }
}