using System.Collections;
using GamePlay.Entities; // [MỚI] Thêm namespace này
using UnityEngine;

// [SỬA] Kế thừa PoolEntity thay vì MonoBehaviour
public class MilestoneOnMap : PoolEntity
{
    [SerializeField] private Transform mileStoneTrans;
    [SerializeField] public float duration = 0.5f;

    private Coroutine _animRoutine;

    public void PlayAnimOpen()
    {
        if (mileStoneTrans == null)
        {
            Debug.LogError($"[{nameof(MilestoneOnMap)}] mileStoneTrans is null", this);
            return;
        }

        gameObject.SetActive(true);

        if (_animRoutine != null)
        {
            StopCoroutine(_animRoutine);
            _animRoutine = null;
        }

        _animRoutine = StartCoroutine(CoPlayOpen());
    }

    private IEnumerator CoPlayOpen()
    {
        float from = -90f;
        float to = 0f;

        float t = 0f;

        mileStoneTrans.localEulerAngles = new Vector3(from, 0f, 0f);

        if (duration <= 0f)
        {
            mileStoneTrans.localEulerAngles = new Vector3(to, 0f, 0f);
            _animRoutine = null;
            yield break;
        }

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);

            float x = Mathf.Lerp(from, to, k);
            mileStoneTrans.localEulerAngles = new Vector3(x, 0f, 0f);

            yield return null;
        }

        mileStoneTrans.localEulerAngles = new Vector3(to, 0f, 0f);
        _animRoutine = null;
    }
}