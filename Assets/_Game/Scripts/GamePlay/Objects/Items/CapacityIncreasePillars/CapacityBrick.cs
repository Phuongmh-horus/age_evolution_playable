using System;
using System.Collections;
using System.Collections.Generic;
// using Alchemy.Inspector;
using UnityEngine;

public class CapacityBrick : MonoBehaviour
{
    [SerializeField] private MeshRenderer brickMeshRenderer;
    [Header("Scale Pulse")]
    [SerializeField] private float scaleUp = 1.1f;
    [SerializeField] private float scaleDown = 0.9f;
    [SerializeField] private float scaleStageDuration = 0.08f;

    public float brickWidth;
    public float brickLength;
    public float brickHeight;
    public BrickFallMotion brickFallMotion;
    public bool isActivated;

    // Each visual brick can represent multiple logical brick rewards.
    private int _capacityValue = 1;

    public event Action<int> OnReachedCapacityBar;

    private bool _isScaling;
    private int _scaleStage;
    private float _scaleTimer;
    private Vector3 _baseScale;


    // [Button]
    public void GetBrickSize()
    {
        if (brickMeshRenderer != null && brickMeshRenderer.bounds.size != Vector3.zero)
        {
            brickWidth = brickMeshRenderer.bounds.size.x;
            brickLength = brickMeshRenderer.bounds.size.z;
            brickHeight = brickMeshRenderer.bounds.size.y;
        }
        else
        {
            Debug.LogWarning("Brick Mesh Renderer is not assigned or has zero size.");
        }
    }

    public void ActivateBrickEffect()
    {
        // Play some visual or sound effect when the brick is activated
    }

    public void StartFall(Vector3 outwardDirection)
    {
        // Subscribe to motion callback
        brickFallMotion.OnReachedCapacityBar -= HandleReachedCapacityBar;
        brickFallMotion.OnReachedCapacityBar += HandleReachedCapacityBar;

        brickFallMotion.StartFall(outwardDirection);
        TriggerScalePulse();
        isActivated = true;
    }

    public void SetCapacityValue(int capacityValue)
    {
        _capacityValue = Mathf.Max(1, capacityValue);
    }

    private void HandleReachedCapacityBar()
    {
        if (brickFallMotion != null)
            brickFallMotion.OnReachedCapacityBar -= HandleReachedCapacityBar;

        // Propagate event to pillar
        OnReachedCapacityBar?.Invoke(_capacityValue);
        OnReachedCapacityBar = null; // Clear to avoid stale subscriptions
        _capacityValue = 1;
    }

    private void TriggerScalePulse()
    {
        _baseScale = transform.localScale;
        _scaleStage = 0;
        _scaleTimer = 0f;
        _isScaling = true;
    }
}

