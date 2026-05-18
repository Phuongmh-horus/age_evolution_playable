using System;
using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Items
{
    [Serializable]
    public class CapacityIncreaseFactoryData : StatModifierData
    {
        [Tooltip("Cấp độ hiện tại")]
        public int Level = 1;
        public int Coin = 0;

        private int _currentUpgradeValue;
        private Dictionary<int, int> _cacheCharacterFactoryUpgrade;

        private Dictionary<int, int> GetUpgradeDict()
        {
            if (_cacheCharacterFactoryUpgrade != null && _cacheCharacterFactoryUpgrade.Count > 0)
            {
                return _cacheCharacterFactoryUpgrade;
            }

            _cacheCharacterFactoryUpgrade = new Dictionary<int, int>();
            EraDataSO era = null;

            // Primary: ConfigHolder (full game flow)
            if (ConfigHolder.Instance != null)
            {
                era = ConfigHolder.Instance.GetCurrentEraConfig();
                var upgradeConfig = era != null ? era.CharacterFactoryUpgradeConfig : null;
                if (upgradeConfig != null && upgradeConfig.UpgradeDict != null && upgradeConfig.UpgradeDict.Count > 0)
                {
                    _cacheCharacterFactoryUpgrade = upgradeConfig.UpgradeDict;
                    return _cacheCharacterFactoryUpgrade;
                }
            }

            // Playable fallback: use GameplayManager.PlayableEra if ConfigHolder is not present
            if (GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
            {
                era = GameplayManager.Instance.PlayableEra;
                var upgradeConfig = era.CharacterFactoryUpgradeConfig;
                if (upgradeConfig != null && upgradeConfig.UpgradeDict != null && upgradeConfig.UpgradeDict.Count > 0)
                {
                    _cacheCharacterFactoryUpgrade = upgradeConfig.UpgradeDict;
                    return _cacheCharacterFactoryUpgrade;
                }
            }

            // Fallback: build upgrade thresholds from Character damage (closer to original flow).
            if (era != null && era.CharacterList != null)
            {
                var dictFromCharacters = BuildUpgradeDictFromCharacters(era.CharacterList);
                if (dictFromCharacters != null && dictFromCharacters.Count > 0)
                {
                    _cacheCharacterFactoryUpgrade = dictFromCharacters;
                    Debug.LogWarning("[CapacityIncreaseFactoryData] Upgrade config missing. Using Character damage-based thresholds.");
                    return _cacheCharacterFactoryUpgrade;
                }
            }

            // Last resort: prevent zero dict to keep leveling functional.
            if (_cacheCharacterFactoryUpgrade == null || _cacheCharacterFactoryUpgrade.Count == 0)
            {
                _cacheCharacterFactoryUpgrade = new Dictionary<int, int> { { 0, 0 }, { 1, 1 } };
                Debug.LogWarning("[CapacityIncreaseFactoryData] Upgrade config missing. Using minimal fallback {0:0,1:1}.");
            }

            return _cacheCharacterFactoryUpgrade;
        }

        public int GetMaxLevel()
        {
            GetUpgradeDict();
            if (_cacheCharacterFactoryUpgrade == null || _cacheCharacterFactoryUpgrade.Count == 0)
                return 0;

            int maxKey = 0;
            foreach (var key in _cacheCharacterFactoryUpgrade.Keys)
            {
                if (key > maxKey) maxKey = key;
            }

            return maxKey;
        }

        private Dictionary<int, int> BuildUpgradeDictFromCharacters(CharacterListDataSO list)
        {
            if (list == null || list.Characters == null || list.Characters.Count == 0) return null;

            var dict = new Dictionary<int, int>
            {
                { 0, 0 }
            };

            for (int i = 0; i < list.Characters.Count; i++)
            {
                var data = list.Characters[i];
                int level = i + 1;
                int damage = data != null ? Mathf.Max(1, data.UnitDamage + data.WeaponDamage) : 1;
                dict[level] = damage;
            }

            return dict;
        }

        public override void AdjustValue(int amount)
        {
            // Initial Setup if needed
            GetUpgradeDict();

            // 1. Armor Logic: Damage reduces armor first
            if (Armor > 0)
            {
                Armor -= 1;
                return;
            }

            // 2. Validate Data
            if (_cacheCharacterFactoryUpgrade == null || _cacheCharacterFactoryUpgrade.Count == 0) return;
            if (Level < 1) Level = 1;

            int maxLevel = GetMaxLevel();

            // If maxed out, stop
            if (Level >= maxLevel)
            {
                _currentUpgradeValue = 0;
                return;
            }

            // 3. Add EXP
            _currentUpgradeValue += amount;

            // 4. Level Up Logic
            while (true)
            {
                if (Level >= maxLevel)
                {
                    _currentUpgradeValue = 0;
                    break;
                }

                int reqExp = _cacheCharacterFactoryUpgrade.GetValueOrDefault(Level);
                if (reqExp <= 0) break;

                if (_currentUpgradeValue < reqExp)
                {
                    break;
                }

                _currentUpgradeValue -= reqExp;
                Level++;
                Coin += Mathf.Max(0, Value);
            }
        }

        public float GetUpgradeProgress()
        {
            GetUpgradeDict();

            if (_cacheCharacterFactoryUpgrade == null || _cacheCharacterFactoryUpgrade.Count == 0) return 0f;

            int maxLevel = GetMaxLevel();

            if (Level >= maxLevel) return 1f;

            int reqExp = _cacheCharacterFactoryUpgrade.GetValueOrDefault(Level);

            if (reqExp <= 0) return 1f;

            return (float)_currentUpgradeValue / reqExp;
        }

        public float GetCurrentUpgradeProgress()
        {
            return GetUpgradeProgress();
        }

        public override void ResetValue()
        {
            base.ResetValue();
            _currentUpgradeValue = 0;
            Level = 1;
            Coin = 0;          // Armor reset is handled by StatModifierData.ResetValue() if logic exists, check if needed
        }
    }
}