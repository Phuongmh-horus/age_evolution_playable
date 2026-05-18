using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public class LockChain : MonoBehaviour
{
    [SerializeField] private TextMeshPro healthText;
    [SerializeField] private Animator chainAnimator;
    public int currentHitPoint;

    public int RemainingHealth => currentHitPoint;

    private void OnEnable()
    {
        UpdateHealthDisplay();
    }

    public void Initialize(int hitPoint)
    {
        currentHitPoint = Mathf.Max(0, hitPoint);
        UpdateHealthDisplay();
        gameObject.SetActive(currentHitPoint > 0);
    }

    public void ApplyDamage()
    {
        currentHitPoint--;
        UpdateHealthDisplay();
    }

    public void UpdateHealthDisplay()
    {
        if (healthText != null)
        {
            healthText.text = RemainingHealth.ToString();
        }
    }

    public void AutoBind()
    {
        if (healthText == null)
            Debug.LogWarning($"[LockChain] Missing healthText on {name}. Assign in Inspector.");
        if (chainAnimator == null)
            Debug.LogWarning($"[LockChain] Missing chainAnimator on {name}. Assign in Inspector.");
    }

    private Coroutine _breakRoutine;

    public void PlayBreakAnimation()
    {
        if (_breakRoutine != null)
        {
            StopCoroutine(_breakRoutine);
            _breakRoutine = null;
        }

        _breakRoutine = StartCoroutine(BreakRoutine());
    }

    private IEnumerator BreakRoutine()
    {
        if (chainAnimator != null)
        {
            chainAnimator.SetTrigger("Break");
            yield return new WaitForSeconds(1f);
        }

        gameObject.SetActive(false);
        _breakRoutine = null;
    }


}
