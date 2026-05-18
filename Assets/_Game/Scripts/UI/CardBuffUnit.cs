using UnityEngine;
using UnityEngine.UI;
using CardSystem.Data;

public enum CardRarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public class CardBuffUnit : MonoBehaviour
{
    [SerializeField] public RectTransform rectTransform;
    [SerializeField] private Transform _visualContainer;
    [SerializeField] Sprite[] _rarityFrames; // Optional: assign different frames for each rarity in the inspector
    [SerializeField] Image _frameImage; // Optional: reference to an Image component to show rarity frame

    private CardRarity _rarity = CardRarity.Common;
    private GameObject _currentVisual;

    public BuffDefinition Definition;

    public void Initialize(string label = null)
    {
        _rarity = CardRarity.Common;
        SetVisual(null);
        RefreshFrame();
        gameObject.SetActive(true);
    }

    public void Initialize(BuffDefinition definition)
    {
        if (definition == null)
        {
            Initialize((string)null);
            return;
        }

        Definition = definition;

        // Map rarity directly
        switch (definition.Rarity)
        {
            case CardSystem.Domain.Enums.Rarity.Uncommon: _rarity = CardRarity.Uncommon; break;
            case CardSystem.Domain.Enums.Rarity.Rare: _rarity = CardRarity.Rare; break;
            case CardSystem.Domain.Enums.Rarity.Epic: _rarity = CardRarity.Epic; break;
            case CardSystem.Domain.Enums.Rarity.Legendary: _rarity = CardRarity.Legendary; break;
            default: _rarity = CardRarity.Common; break;
        }

        SetVisual(definition.VisualPrefab);
        RefreshFrame();
        gameObject.SetActive(true);
    }

    public void ApplyUnitLabel(string unitLabel)
    {
        // Expect unitLabel like "U1", "R1", "E1", "L1" or cluster like "C1".
        if (string.IsNullOrEmpty(unitLabel)) return;

        var token = unitLabel.Trim().ToUpper();
        if (token.Length == 0) return;

        // map first char to rarity when applicable
        switch (token[0])
        {
            case 'U': _rarity = CardRarity.Uncommon; break;
            case 'R': _rarity = CardRarity.Rare; break;
            case 'E': _rarity = CardRarity.Epic; break;
            case 'L': _rarity = CardRarity.Legendary; break;
            case 'C': _rarity = CardRarity.Common; break;
            default: break;
        }

        RefreshFrame();
    }

    public CardRarity GetRarity() => _rarity;

    private void RefreshFrame()
    {
        if (_frameImage == null || _rarityFrames == null) return;
        int index = (int)_rarity;
        _frameImage.sprite = index < _rarityFrames.Length ? _rarityFrames[index] : null;
    }

    private void SetVisual(GameObject prefab)
    {
        if (_currentVisual != null)
        {
            Destroy(_currentVisual);
            _currentVisual = null;
        }

        if (_frameImage != null) _frameImage.enabled = true;

        if (prefab == null || _visualContainer == null) return;

        if (_frameImage != null) _frameImage.enabled = false;

        _currentVisual = Instantiate(prefab, _visualContainer);
        _currentVisual.transform.localScale = Vector3.one;
    }

}
