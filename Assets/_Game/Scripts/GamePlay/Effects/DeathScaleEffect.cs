using System;
using System.Collections.Generic;
using UnityEngine;

public class DeathScaleEffect : MonoBehaviour
{
    [SerializeField] public Transform Transform;

    [Header("Settings")]
    [SerializeField] private float _targetScaleMultiplier = 1.3f;
    [SerializeField] private float _expandDuration = 0.1f;
    [SerializeField] private float _shrinkDuration = 0.2f;

    private static readonly List<DeathScaleEffect> activeEffects = new List<DeathScaleEffect>(32);

    private Vector3 _originalScale;
    private Vector3 _peakScale;
    private float _phaseElapsed;
    private int _phase;
    private bool _registeredForTick;
    private Action _onComplete;

    public static void TickActiveEffects(float deltaTime)
    {
        for (int i = activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = activeEffects[i];
            if (effect == null)
            {
                RemoveAtSwapBack(i);
                continue;
            }

            if (effect.Step(deltaTime))
            {
                continue;
            }

            effect.UnregisterTick();
            RemoveAtSwapBack(i);
        }
    }

    private void Reset()
    {
        if (Transform == null) Transform = transform;
    }

    private void Awake()
    {
        if (Transform == null) Transform = transform;
        _originalScale = Transform.localScale;
    }

    public void PlayDeathEffect(Action onComplete = null)
    {
        if (Transform == null) Transform = transform;

        _phaseElapsed = 0f;
        _phase = 1;
        _onComplete = onComplete;
        _peakScale = _originalScale * _targetScaleMultiplier;
        Transform.localScale = _originalScale;

        RegisterTick();
    }

    private bool Step(float deltaTime)
    {
        if (Transform == null)
        {
            Complete();
            return false;
        }

        if (_phase == 1)
        {
            _phaseElapsed += deltaTime;
            float duration = Mathf.Max(0.0001f, _expandDuration);
            float t = Mathf.Clamp01(_phaseElapsed / duration);
            float eased = EaseOutBack(t);
            Transform.localScale = Vector3.LerpUnclamped(_originalScale, _peakScale, eased);

            if (t < 1f)
            {
                return true;
            }

            _phase = 2;
            _phaseElapsed = 0f;
            Transform.localScale = _peakScale;
            return true;
        }

        _phaseElapsed += deltaTime;
        float shrinkDuration = Mathf.Max(0.0001f, _shrinkDuration);
        float shrinkT = Mathf.Clamp01(_phaseElapsed / shrinkDuration);
        float shrinkEase = EaseInQuad(shrinkT);
        Transform.localScale = Vector3.LerpUnclamped(_peakScale, Vector3.zero, shrinkEase);

        if (shrinkT < 1f)
        {
            return true;
        }

        Transform.localScale = Vector3.zero;
        Complete();
        return false;
    }

    private void Complete()
    {
        var onComplete = _onComplete;
        _onComplete = null;
        _phase = 0;
        _phaseElapsed = 0f;
        onComplete?.Invoke();
    }

    private void OnDisable()
    {
        UnregisterTick();
        _onComplete = null;
        _phase = 0;
        _phaseElapsed = 0f;

        if (Transform == null) Transform = transform;
        Transform.localScale = _originalScale;
    }

    private void RegisterTick()
    {
        if (_registeredForTick) return;
        _registeredForTick = true;
        activeEffects.Add(this);
    }

    private void UnregisterTick()
    {
        _registeredForTick = false;
    }

    private static void RemoveAtSwapBack(int index)
    {
        int last = activeEffects.Count - 1;
        if (index < 0 || index > last) return;

        activeEffects[index] = activeEffects[last];
        activeEffects.RemoveAt(last);
    }

    private static float EaseInQuad(float t)
    {
        return t * t;
    }

    private static float EaseOutBack(float t)
    {
        const float overshoot = 1.70158f;
        float u = t - 1f;
        return u * u * ((overshoot + 1f) * u + overshoot) + 1f;
    }
}
