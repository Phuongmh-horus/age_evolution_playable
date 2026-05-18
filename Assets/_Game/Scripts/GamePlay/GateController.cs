using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

public class GateController : MonoBehaviour
{
    [Header("Gate Doors")]
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;

    [Header("Animation Settings")]
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private Ease easeType = Ease.OutBack;

    [Header("Axis")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Header("Events")]
    public UnityEvent OnOpenComplete;
    public UnityEvent OnCloseComplete;

    private Tween _leftTween;
    private Tween _rightTween;
    private Tween _completeTween;

    private bool _isOpen = false;

    private void OnDestroy()
    {
        KillAllTweens();
    }

    [ContextMenu("Open Gate")]
    public void OpenGate()
    {
        if (_isOpen) return;

        KillAllTweens();

        // No animation setup: open instantly and fire event to avoid gameplay stall.
        if (leftDoor == null && rightDoor == null)
        {
            _isOpen = true;
            OnOpenComplete?.Invoke();
            return;
        }

        if (duration <= 0f)
        {
            _isOpen = true;
            OnOpenComplete?.Invoke();
            return;
        }

        Vector3 leftTargetRotation = rotationAxis * -openAngle;
        Vector3 rightTargetRotation = rotationAxis * openAngle;

        if (leftDoor)
        {
            _leftTween = leftDoor.DOLocalRotate(leftTargetRotation, duration)
                .SetEase(easeType)
                .SetUpdate(false);
        }

        if (rightDoor)
        {
            _rightTween = rightDoor.DOLocalRotate(rightTargetRotation, duration)
                .SetEase(easeType)
                .SetUpdate(false);
        }

        // dùng tween "dummy" để gọi complete đúng thời gian như code cũ
        _completeTween = DOVirtual.Float(0f, 1f, duration, _ => { })
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                _isOpen = true;
                OnOpenComplete?.Invoke();
            })
            .SetUpdate(false);
    }

    [ContextMenu("Close Gate")]
    public void CloseGate()
    {
        if (!_isOpen) return;

        KillAllTweens();

        if (leftDoor == null && rightDoor == null)
        {
            _isOpen = false;
            OnCloseComplete?.Invoke();
            return;
        }

        if (duration <= 0f)
        {
            _isOpen = false;
            OnCloseComplete?.Invoke();
            return;
        }

        Vector3 closedRotation = Vector3.zero;

        if (leftDoor)
        {
            _leftTween = leftDoor.DOLocalRotate(closedRotation, duration)
                .SetEase(easeType)
                .SetUpdate(false);
        }

        if (rightDoor)
        {
            _rightTween = rightDoor.DOLocalRotate(closedRotation, duration)
                .SetEase(easeType)
                .SetUpdate(false);
        }

        _completeTween = DOVirtual.Float(0f, 1f, duration, _ => { })
            .SetEase(Ease.Linear)
            .OnComplete(() =>
            {
                _isOpen = false;
                OnCloseComplete?.Invoke();
            })
            .SetUpdate(false);
    }

    public void ToggleGate()
    {
        if (_isOpen) CloseGate();
        else OpenGate();
    }

    private void KillAllTweens()
    {
        _leftTween?.Kill();
        _rightTween?.Kill();
        _completeTween?.Kill();

        _leftTween = null;
        _rightTween = null;
        _completeTween = null;
    }

    public bool IsOpen => _isOpen;
}
