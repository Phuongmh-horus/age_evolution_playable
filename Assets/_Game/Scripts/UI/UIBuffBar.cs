using System.Collections.Generic;
using CardSystem.Data;
using TMPro;
using UnityEngine;

public class UIBuffBar : MonoBehaviour
{
    [SerializeField] private GameObject coinBuffGameObject;
    [SerializeField] private TextMeshProUGUI coinBuffText;
    [SerializeField] private CardBuffUnit cardBuffPrefab;
    [SerializeField] private RectTransform cardBuffParent;
    [SerializeField] private float cardSpacing = 180f;

    private readonly List<CardBuffUnit> _sessionUnits = new List<CardBuffUnit>();
    private int _remainingCoins;
    private int _revealedIndex;

    private void OnEnable()
    {
        GameEventBus.OnCoinChange += OnCoinChange;
        GameEventBus.OnCapacityGateCoinProgress += OnCapacityGateCoinProgress;
        GameEventBus.OnCapacityGateCardGrantedDetailed += OnCapacityGateCardGrantedDetailed;
        GameEventBus.OnCapacityGateCardsGrantedDetailed += OnCapacityGateCardsGrantedDetailed;
        GameEventBus.OnCapacityGateCardRevealedBuff += OnCapacityGateCardRevealedBuff;
    }

    private void OnDisable()
    {
        GameEventBus.OnCoinChange -= OnCoinChange;
        GameEventBus.OnCapacityGateCoinProgress -= OnCapacityGateCoinProgress;
        GameEventBus.OnCapacityGateCardGrantedDetailed -= OnCapacityGateCardGrantedDetailed;
        GameEventBus.OnCapacityGateCardsGrantedDetailed -= OnCapacityGateCardsGrantedDetailed;
        GameEventBus.OnCapacityGateCardRevealedBuff -= OnCapacityGateCardRevealedBuff;
        ClearSessionUnits();
    }

    private void OnCoinChange(int amount)
    {
        if (amount >= 0) return;

        _remainingCoins = Mathf.Abs(amount);
        _revealedIndex = 0;
        ClearSessionUnits();
        SetCoinVisible(true);
        UpdateCoinText();
    }

    private void OnCapacityGateCoinProgress()
    {
        _remainingCoins = Mathf.Max(0, _remainingCoins - 1);
        UpdateCoinText();
    }

    private void OnCapacityGateCardGrantedDetailed(string label)
    {
        if (string.IsNullOrEmpty(label) || cardBuffPrefab == null || cardBuffParent == null) return;

        var unit = Instantiate(cardBuffPrefab, cardBuffParent);
        unit.transform.localPosition = Vector3.zero;
        unit.transform.localRotation = Quaternion.identity;
        unit.Initialize(label);
        _sessionUnits.Add(unit);
        RepositionUnits();
    }

    private void OnCapacityGateCardsGrantedDetailed(string[] labels)
    {
        SetCoinVisible(false);
    }

    private void OnCapacityGateCardRevealedBuff(BuffDefinition definition)
    {
        if (definition == null) return;
        if (_revealedIndex < _sessionUnits.Count)
        {
            _sessionUnits[_revealedIndex]?.Initialize(definition);
        }
        _revealedIndex++;
    }

    private void RepositionUnits()
    {
        int count = _sessionUnits.Count;
        if (count <= 0) return;

        float total = (count - 1) * cardSpacing;
        float startX = -total * 0.5f;
        for (int i = 0; i < count; i++)
        {
            var unit = _sessionUnits[i];
            if (unit == null) continue;
            unit.transform.localPosition = new Vector3(startX + i * cardSpacing, 0f, 0f);
        }
    }

    private void UpdateCoinText()
    {
        if (coinBuffText != null) coinBuffText.text = _remainingCoins.ToString();
    }

    private void SetCoinVisible(bool visible)
    {
        if (coinBuffGameObject != null)
        {
            coinBuffGameObject.SetActive(visible);
        }
    }

    private void ClearSessionUnits()
    {
        for (int i = 0; i < _sessionUnits.Count; i++)
        {
            if (_sessionUnits[i] != null) Destroy(_sessionUnits[i].gameObject);
        }

        _sessionUnits.Clear();
    }
}
