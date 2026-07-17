using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using DG.Tweening;
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

    public void PlayBreakAnimation()
    {
        DOTween.Kill(this, "Break");
        if (chainAnimator != null)
        {
            chainAnimator.SetTrigger("Break");
            DOVirtual.DelayedCall(1f, () => gameObject.SetActive(false), false).SetId(this).SetId("Break");
        }
        else
        {
            gameObject.SetActive(false);
        }
    }


}
