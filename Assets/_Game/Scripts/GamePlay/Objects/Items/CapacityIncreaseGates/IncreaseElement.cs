using UnityEngine;
using UnityEngine.UI;

namespace GamePlay.Items
{
    public class IncreaseElement : MonoBehaviour
    {
        [SerializeField] private IncreaseElementData elementData;
        [SerializeField] private MeshRenderer renderBackground;
        [SerializeField] private Slider slider;
        [SerializeField] private Image icon;
        [SerializeField] private SpriteListData spriteListData;
        [SerializeField] private Color activeColor = Color.yellow;

        private StatModifierData _statData;
        private MaterialPropertyBlock _propertyBlock;

        public int GoldCost => elementData != null ? elementData.Cost : 0;

        private int m_levelCard;

        public StatModifierData StatData => _statData;
        public int LevelCard => m_levelCard;
        public IncreaseElementData ElementData => elementData;
        public bool IsEligible(int gold) => elementData != null && gold >= elementData.Cost;

        public int GetCurrentValue()
        {
            if (elementData == null) return 0;
            return elementData.Value + (elementData.ValueUpgrade * m_levelCard);
        }

        private void Awake()
        {
            if (elementData == null) return;
            _statData = new StatModifierData
            {
                Type = elementData.Type,
                Value = elementData.Value
            };
        }

        public void SetActiveVisual()
        {
            if (renderBackground == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();
            renderBackground.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_EmissionColor", activeColor);
            _propertyBlock.SetColor("_Color", activeColor);
            _propertyBlock.SetColor("_BaseColor", activeColor);
            renderBackground.SetPropertyBlock(_propertyBlock);
        }

        public void InitProgress(int maxGold)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = maxGold;
            slider.value = maxGold;
        }

        public void SetElementData(IncreaseElementData data)
        {
            elementData = data;
            if (elementData == null) return;
            _statData = new StatModifierData
            {
                Type = elementData.Type,
                Value = elementData.Value
            };
        }

        public void UpdateProgress(int remainingGold)
        {
            if (slider != null)
                slider.value = remainingGold;
        }

        public void UpdateLevelCard(int level)
        {
            m_levelCard = level;
            if (icon != null && spriteListData != null && spriteListData.sprites != null && level < spriteListData.sprites.Count)
                icon.sprite = spriteListData.sprites[level];
        }
    }
}
