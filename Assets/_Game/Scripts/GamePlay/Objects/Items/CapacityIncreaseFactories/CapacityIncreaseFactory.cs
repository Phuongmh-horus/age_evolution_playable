using GamePlay.ComponentSystems;
using TMPro;
using UnityEngine;

namespace GamePlay.Items
{
    public class CapacityIncreaseFactory : StatModifierItem<CapacityIncreaseFactoryData>
    {
        [SerializeField] private TextMeshPro valueText;
        [SerializeField] private TextMeshPro coinText;
        [SerializeField] private int maxVisualCoin = 20;
        [SerializeField] private int fallbackCoinPerDamage = 1;

        private int _coinPerDamage;

        public override void Initialize()
        {
            base.Initialize();
            Data.Type = StatType.Character;
            if (Data.Value <= 0) Data.Value = 1;
            SyncCoinPerDamageFromCapacity();
            UpdateText();
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
            if (source == null) return;
            Data.AdjustValue(Mathf.Max(1, source.Damage));
            Data.Coin += Mathf.Max(1, _coinPerDamage);
            UpdateText();
        }

        protected override void HandleWheelCollision()
        {
            if (Data.Armor > 0)
            {
                Data.Armor = Mathf.Max(0, Data.Armor - 1);
                return;
            }

            Data.Coin = Mathf.Max(0, Data.Coin);

            if (GameplayManager.Instance != null)
            {
                GameplayManager.Instance.ChangeStatModifierData(Data);
            }

            GameEventBus.OnCoinChange?.Invoke(Data.Coin);
            SpawnCoinVisualSimple(Data.Coin);

            Data.ResetValue();
            UpdateText();

            base.HandleWheelCollision();
        }

        private void OnEnable()
        {
            GameEventBus.UpgradeCapacity += HandleUpgradeCapacity;
            SyncCoinPerDamageFromCapacity();
        }

        private void OnDisable()
        {
            GameEventBus.UpgradeCapacity -= HandleUpgradeCapacity;
        }

        private void HandleUpgradeCapacity(int capacity)
        {
            if (Data == null) return;
            Data.Value = Mathf.Max(1, capacity);
            SyncCoinPerDamageFromCapacity();
            UpdateText();
        }

        private void SyncCoinPerDamageFromCapacity()
        {
            int capacity = 0;
            if (GameplayManager.Instance != null &&
                GameplayManager.Instance.gamePlayVariable != null &&
                GameplayManager.Instance.gamePlayVariable.EvolutionVariable != null)
            {
                capacity = GameplayManager.Instance.gamePlayVariable.EvolutionVariable.Capacity;
            }

            if (capacity <= 0 && Data != null)
            {
                capacity = Data.Value;
            }

            _coinPerDamage = Mathf.Max(1, capacity > 0 ? capacity : fallbackCoinPerDamage);
        }

        private void SpawnCoinVisualSimple(int coin)
        {
            int safeCoin = Mathf.Max(0, coin);
            if (safeCoin <= 0) return;

            int visualCount = maxVisualCoin > 0 ? Mathf.Min(safeCoin, maxVisualCoin) : safeCoin;
            for (int i = 0; i < visualCount; i++)
            {
                GameEventBus.OnGainGold?.Invoke();
            }
        }

        private void UpdateText()
        {
            if (valueText != null) valueText.text = "+" + Data.Value;
            if (coinText != null) coinText.text = Data.Coin.ToString();
        }
    }
}
