using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.UI;

public class ImageSlider : MonoBehaviour
{
    [SerializeField] private Image filterImg;
    private float _maxValue;
    private float _value;
    [Range(0f, 1f)] public float _progress;

    public float MaxValue => _maxValue;
    public float Value => _value;
    private void OnValidate()
    {
        if (filterImg) filterImg.fillAmount = _progress;
    }

    public void SetMaxValue(float maxValue)
    {
        _maxValue = maxValue;
    }

    public void SetValue(float value, float maxValue = 0)
    {
        _tween?.Kill();
        _value = value;
        if (maxValue != 0) SetMaxValue(maxValue);
        UpdateProgress();
    }

    private Tween _tween;

    public void SetValueSmooth(float value, float duration, Ease ease = Ease.Linear, Action onDone = null)
    {
        // Hủy tween cũ và tạo mới
        _tween?.Kill();
        
        float startValue = _value;
        _tween = DOTween.To(() => startValue, x => 
            {
                _value = x;
                UpdateProgress();
            }, value, duration)
            .SetEase(ease)
            .OnComplete(() => onDone?.Invoke())
            .SetLink(gameObject);
    }

    public void SetProgress(float progress)
    {
        _value = (int)(progress * _maxValue);
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_maxValue == 0) return;
        _progress = Mathf.Clamp01(_value / _maxValue);
        if (filterImg) filterImg.fillAmount = _progress;
    }

    private void OnDestroy()
    {
        _tween?.Kill();
    }

    public float GetProgress()
    {
        return _progress;
    }
}
