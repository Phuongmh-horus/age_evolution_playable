using System;
using System.Collections.Generic;
using GamePlay.Crushers;
using GamePlay.Items;
using UnityEngine;

namespace CardSystem.Domain.Enums
{
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
        Mythic
    }
}

namespace CardSystem.Data
{
    [Serializable]
    public class BuffDefinition
    {
        public string BuffId;
        public string Name;
        public BuffEffectType EffectType;
        public int Value;
        public CardSystem.Domain.Enums.Rarity Rarity;
        public GameObject VisualPrefab;
    }

    public enum BuffEffectType
    {
        None,
        SummonOneSoldier,
        SummonTwoSoldiers,
        AddDamage,
        AddFireRate,
        AddCoin,
        AddEvolveRate,
    }
}

namespace CardSystem.BuffSystems
{
    public class IngameBuffEffectSystem : MonoBehaviour
    {
        public static IngameBuffEffectSystem Instance { get; private set; }

        [SerializeField] private List<CardSystem.Data.BuffDefinition> commonPool = new List<CardSystem.Data.BuffDefinition>();
        [SerializeField] private List<CardSystem.Data.BuffDefinition> uncommonPool = new List<CardSystem.Data.BuffDefinition>();
        [SerializeField] private List<CardSystem.Data.BuffDefinition> rarePool = new List<CardSystem.Data.BuffDefinition>();
        [SerializeField] private List<CardSystem.Data.BuffDefinition> epicPool = new List<CardSystem.Data.BuffDefinition>();
        [SerializeField] private List<CardSystem.Data.BuffDefinition> legendaryPool = new List<CardSystem.Data.BuffDefinition>();
        [SerializeField] private List<CardSystem.Data.BuffDefinition> mythicPool = new List<CardSystem.Data.BuffDefinition>();

        private readonly List<CardSystem.Data.BuffDefinition> _owned = new List<CardSystem.Data.BuffDefinition>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            GameEventBus.OnCapacityGateCardsGrantedDetailed += HandleCapacityGateCardsGrantedDetailed;
        }

        private void OnDisable()
        {
            GameEventBus.OnCapacityGateCardsGrantedDetailed -= HandleCapacityGateCardsGrantedDetailed;
        }

        private void HandleCapacityGateCardsGrantedDetailed(string[] labels)
        {
            if (labels == null) return;

            for (int i = 0; i < labels.Length; i++)
            {
                var buff = ResolveByLabel(labels[i]);
                if (buff == null) continue;

                _owned.Add(buff);
                ApplyBuff(buff);
                GameEventBus.OnCapacityGateCardRevealedBuff?.Invoke(buff);
            }
        }

        private void ApplyBuff(CardSystem.Data.BuffDefinition buff)
        {
            if (buff == null || GameplayManager.Instance == null) return;

            switch (buff.EffectType)
            {
                case CardSystem.Data.BuffEffectType.AddFireRate:
                    GameplayManager.Instance.ChangeStatModifierData(new StatModifierCapacityData(StatType.FireRate, buff.Value, 0));
                    break;
                case CardSystem.Data.BuffEffectType.AddDamage:
                    GameplayManager.Instance.ChangeStatModifierData(new StatModifierCapacityData(StatType.FireRange, buff.Value, 0));
                    break;
                case CardSystem.Data.BuffEffectType.AddCoin:
                    GameplayManager.StartCoin += Mathf.Max(0, buff.Value);
                    GameplayManager.StartCoinPending += Mathf.Max(0, buff.Value);
                    GameEventBus.OnCoinChange?.Invoke(Mathf.Max(0, buff.Value));
                    break;
                case CardSystem.Data.BuffEffectType.SummonOneSoldier:
                case CardSystem.Data.BuffEffectType.SummonTwoSoldiers:
                {
                    int amount = buff.EffectType == CardSystem.Data.BuffEffectType.SummonTwoSoldiers ? 2 : 1;
                    var gateData = new CapacityIncreaseGateData { Type = StatType.Character };
                    gateData.RequestDataList.Add(new CardSpawnRequestData(1, amount, CardType.Character));
                    GameplayManager.Instance.ChangeStatModifierData(gateData);
                    break;
                }
                case CardSystem.Data.BuffEffectType.AddEvolveRate:
                    GameplayManager.Instance.ChangeStatModifierData(new StatModifierCapacityData(StatType.EvolveRate, buff.Value, 0));
                    break;
            }
        }

        private CardSystem.Data.BuffDefinition ResolveByLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;

            char rarity = char.ToUpper(label[0]);
            List<CardSystem.Data.BuffDefinition> pool = null;
            if (rarity == 'C') pool = commonPool;
            else if (rarity == 'U') pool = uncommonPool;
            else if (rarity == 'R') pool = rarePool;
            else if (rarity == 'E') pool = epicPool;
            else if (rarity == 'L') pool = legendaryPool;
            else if (rarity == 'M') pool = mythicPool;

            if (pool == null || pool.Count == 0) return null;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }
    }
}
