using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GamePlay.Data;

namespace GamePlay.Items
{
    public class IncreaseElement : MonoBehaviour
    {
        [SerializeField] private UIGradient gradient;
        [SerializeField] private Slider slider;
        [SerializeField] private Image icon;
        [SerializeField] private Image iconBackground;
        
        [SerializeField] private SpriteCardTypeData spriteCardTypeData;
        [SerializeField] private StatsUpgradeIcon statsUpgradeIcon;
        [SerializeField] private BackgroundGradientData bgGradientData;
        [SerializeField] private GameObject LockImage;
        [SerializeField] private GameObject UnlockImage;
        
        [SerializeField] private TextMeshProUGUI goldText;
        
        private StatModifierData _statData;
        private IncreaseElementData elementData;

        public int GoldCost => elementData != null ? elementData.Cost : 0;

        private int m_levelCard;

        public StatModifierData StatData => _statData;
        public int LevelCard => m_levelCard;
        public IncreaseElementData ElementData => elementData;
        public bool IsEligible(int gold)
        {
            if (elementData == null)
            {
                return false;
            }

            int nextCost = GetNextUpgradeCost();
            return nextCost != int.MaxValue && gold >= nextCost;
        }

        public int GetNextUpgradeCost()
        {
            return GetUpgradeCostForLevel(m_levelCard);
        }

        public int GetUpgradeCostForLevel(int level)
        {
            if (elementData == null) return int.MaxValue;
            if (IsLevelMaxed(level)) return int.MaxValue;

            int currentLevel = Mathf.Max(0, level);
            int baseCost = Mathf.Max(0, elementData.Cost);
            int stepCost = Mathf.Max(0, elementData.UpgradeRequire);

            return baseCost + (stepCost * currentLevel);
        }

        public bool IsMaxLevel()
        {
            return IsLevelMaxed(m_levelCard);
        }

        private bool IsLevelMaxed(int level)
        {
            if (spriteCardTypeData == null || spriteCardTypeData.spriteCards == null || spriteCardTypeData.spriteCards.Count <= 0)
            {
                return false;
            }

            int maxLevel = spriteCardTypeData.spriteCards.Count - 1;
            return level >= maxLevel;
        }

        private void Awake()
        {
            TryAutoResolveLockVisuals();
            SetNormalVisual();
            RefreshLockVisual();
        }

        private void SetGradient(GradientColor gradientColor)
        {
            gradient?.Set(gradientColor.from, gradientColor.to);
        }

        public void SetActiveVisual()
        {
            if (bgGradientData == null) return;
            SetGradient(bgGradientData.Active);
        }

        [ContextMenu("Set InActive Visual")]
        public void SetNormalVisual()
        {
            if (bgGradientData == null) return;
            SetGradient(bgGradientData.Normal);
        }

        public void InitProgress(int maxGold)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = maxGold;
            slider.value = maxGold;
        }

        public void RefreshByLevelCard()
        {
            if (_statData == null) return;

            var value = elementData != null
                ? elementData.Value + (elementData.ValueUpgrade * (m_levelCard - 1))
                : 0;

            StatData.Value = value;
        }

        public void SetElementData(IncreaseElementData data)
        {
            elementData = data;
            if (elementData == null) return;
            
            if (goldText != null)
                goldText.text = data.Cost.ToString();
            
            _statData = new CapacityIncreaseGateData()
            {
                Type = elementData.Type,
                Value = elementData.Value,
                
                ElementDataList = new List<IncreaseElementData>() { elementData },
            };

            m_levelCard = data.StartLevel;
            if (spriteCardTypeData.TryGetSprite(m_levelCard, out var spriteBackground))
                iconBackground.sprite = spriteBackground.Unknown;

            RefreshLockVisual();
        }

        public void UpdateProgress(int remainingGold)
        {
            if (slider != null)
                slider.value = remainingGold;
        }

        public void UpdateLevelCard(int level)
        {
            if (m_levelCard >= level) return;
            m_levelCard = level;

            // Hidden ngay tu dau nen khong can
            // if (icon != null)
            // {
            //     icon.enabled = level >= 1;
            //     if (level >= 1 && elementData != null && statsUpgradeIcon != null)
            //     {
            //         var sprite = statsUpgradeIcon.GetIcon(elementData.Type);
            //         if (sprite != null)
            //             icon.sprite = sprite;
            //         else icon.enabled = false;
            //     }
            // }

            if (icon != null && elementData != null && statsUpgradeIcon != null)
            {
                var statIcon = statsUpgradeIcon.GetIcon(elementData.Type);
                if (statIcon != null)
                {
                    icon.sprite = statIcon;
                    icon.enabled = true;
                }
                else
                {
                    icon.enabled = false;
                }
            }

            if (iconBackground != null)
            {
                if (spriteCardTypeData.TryGetSprite(level, out var spriteBackground))
                    iconBackground.sprite = spriteBackground.Unknown; // spriteBackground.Normal;
                else iconBackground.enabled = false;
            }

            RefreshLockVisual();
        }

        public void ShowGoldDrainFeedback()
        {
            SetActiveVisual();
        }

        private void RefreshLockVisual()
        {
            bool isUnlocked = m_levelCard > 0;

            if (LockImage != null)
            {
                LockImage.SetActive(!isUnlocked);
            }

            if (UnlockImage != null)
            {
                UnlockImage.SetActive(isUnlocked);
            }
        }

        private void TryAutoResolveLockVisuals()
        {
            if (LockImage == null)
            {
                LockImage = FindChildByNameContains("Lock_Image");
            }

            if (UnlockImage == null)
            {
                UnlockImage = FindChildByNameContains("Unlock_Image");
            }
        }

        private GameObject FindChildByNameContains(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var transforms = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child == null || child == transform)
                {
                    continue;
                }

                if (child.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return child.gameObject;
                }
            }

            return null;
        }
    }
}
