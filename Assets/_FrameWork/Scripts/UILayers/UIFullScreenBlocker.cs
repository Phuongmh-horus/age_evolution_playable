using System.Collections;
using UnityEngine;
using UnityEngine.UI;


public class UIFullScreenBlocker : MonoSingleton<UIFullScreenBlocker>
{

    [SerializeField] private Image blockerImage;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Options")]
    [Tooltip("Tự động đưa blocker lên sibling cuối cùng (trên cùng) mỗi khi Lock().")]
    [SerializeField] private bool forceTopOnLock = true;

    // Lock dạng stack: cho phép nhiều nơi cùng gọi Lock()/Unlock() lồng nhau
    // mà không vô tình mở khoá sớm khi chỉ 1 trong số đó đã xong việc.
    private int _lockCount;
    private Coroutine _unlockDelayRoutine;

    protected override void Awake()
    {
        base.Awake();

        _lockCount = 1;
        SetBlocked(true);
    }

    public void Lock()
    {
        // Debug.Log("1");
        _lockCount = Mathf.Max(0, _lockCount) + 1;

        if (_unlockDelayRoutine != null)
        {
            StopCoroutine(_unlockDelayRoutine);
            _unlockDelayRoutine = null;
        }

        if (forceTopOnLock && transform.parent != null)
            transform.SetAsLastSibling();

        SetBlocked(true);
    }

    public void Unlock(float delaySeconds = -1f, bool forceUnlockAll = false)
    {
        //Debug.Log("0");
        _lockCount = forceUnlockAll ? 0 : Mathf.Max(0, _lockCount - 1);

        if (_lockCount > 0)
            return; // Vẫn còn nơi khác đang giữ Lock(), chưa mở.

        if (_unlockDelayRoutine != null)
        {
            StopCoroutine(_unlockDelayRoutine);
            _unlockDelayRoutine = null;
        }

        if (delaySeconds < 0f)
        {
            SetBlocked(false);
            return;
        }

        _unlockDelayRoutine = StartCoroutine(CoUnlockAfterDelay(delaySeconds));
    }

    private IEnumerator CoUnlockAfterDelay(float delaySeconds)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSecondsRealtime(delaySeconds);
        else
            yield return null; // 0 giây -> chờ đúng 1 frame

        SetBlocked(false);
        _unlockDelayRoutine = null;
    }

    private void SetBlocked(bool blocked)
    {
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = blocked;
            canvasGroup.interactable = blocked;
        }

        if (blockerImage != null)
        {
            blockerImage.raycastTarget = blocked;
        }
    }
}
