using System.Collections;
using System.Collections.Generic;
using GamePlay.Characters;
using GamePlay.ComponentSystems;
using UnityEngine;

namespace GamePlay.Items
{
    public class CapacityIncreaseGate : StatModifierItem<CapacityIncreaseGateData>
    {
        [Header("Spawn Settings")]
        [SerializeField] private Transform[] slots;

        [SerializeField] private int speed = -75;
        [SerializeField] private float waitForPurchaseFinishDelay = 1f;
        [SerializeField] private int[] cardCosts = { 5, 5, 10, 20, 35, 40, 40, 45, 50, 55, 65, 70, 75, 80, 80 };
        [SerializeField] private bool despawnBeltWhenFull = true;

        private readonly Dictionary<int, List<CharacterUnit>> _beltUnits = new Dictionary<int, List<CharacterUnit>>();
        private int _beltUnitCount;

        private bool[] _purchased;
        private int _spentCoins;
        private int _arrivedCount;
        private int _totalCount;
        private bool _hasActivated;

        public override void Initialize()
        {
            base.Initialize();
            Data.Type = StatType.Character;
            _hasActivated = false;
            ClearBelts();
        }

        protected override void HandleNonWheelCollision(IAttacker source)
        {
        }

        protected override void HandleWheelCollision()
        {
            if (_hasActivated) return;
            _hasActivated = true;

            int maxCoinsNeeded = 0;
            for (int i = 0; i < cardCosts.Length; i++) maxCoinsNeeded += cardCosts[i];
            Data.MaxCoinsToSpawn = maxCoinsNeeded;

            _totalCount = Mathf.Min(GameplayManager.StartCoin, maxCoinsNeeded);
            _arrivedCount = 0;
            _spentCoins = 0;
            _purchased = new bool[cardCosts.Length];

            if (GameplayManager.Instance != null)
            {
                GameplayManager.Instance.ChangeStatModifierData(Data, OnItemArrived);
                GameplayManager.Instance.ChangeStatModifierData(new StatModifierCapacityData(StatType.MoveSpeed, speed, 0));
            }

            if (_totalCount <= 0)
            {
                FinalizeGateState();
                DespawnInterval();
            }
        }

        private void OnItemArrived()
        {
            if (_arrivedCount >= _totalCount) return;
            _arrivedCount++;
            TryPurchaseCards();
            GameEventBus.OnCapacityGateCoinProgress?.Invoke();

            if (_arrivedCount >= _totalCount)
            {
                StartCoroutine(CoFinishGate());
            }
        }

        private IEnumerator CoFinishGate()
        {
            yield return new WaitForSeconds(waitForPurchaseFinishDelay);
            FinalizeGateState();
            DespawnInterval();
        }

        private void TryPurchaseCards()
        {
            int available = _arrivedCount - _spentCoins;
            if (available <= 0 || _purchased == null) return;

            for (int i = 0; i < cardCosts.Length; i++)
            {
                if (_purchased[i]) continue;
                int cost = cardCosts[i];
                if (available < cost) break;

                _spentCoins += cost;
                available -= cost;
                _purchased[i] = true;
                GameEventBus.OnCapacityGateCardGrantedDetailed?.Invoke(GetCardLabel(i));
            }
        }

        private void FinalizeGateState()
        {
            List<string> labels = new List<string>();
            if (_purchased != null)
            {
                for (int i = 0; i < _purchased.Length; i++)
                {
                    if (_purchased[i]) labels.Add(GetCardLabel(i));
                }
            }

            if (labels.Count > 0)
            {
                GameEventBus.OnCapacityGateCardsGrantedDetailed?.Invoke(labels.ToArray());
            }

            if (GameplayManager.Instance != null)
            {
                GameplayManager.Instance.ResetStatModifierData(StatType.MoveSpeed);
            }

            if (GameplayManager.GateCoinExcess > 0)
            {
                GameEventBus.OnCoinChange?.Invoke(-GameplayManager.GateCoinExcess);
                GameplayManager.GateCoinExcess = 0;
            }

            _hasActivated = false;
        }

        protected override void DespawnInterval()
        {
            ClearBelts();
            base.DespawnInterval();
        }

        public Transform AddCharacter(CharacterUnit belt)
        {
            if (belt == null) return null;
            if (slots == null || slots.Length == 0) return null;

            if (_beltUnitCount >= slots.Length)
            {
                if (despawnBeltWhenFull)
                {
                    belt.Transform.parent = null;
                    belt.Transform.localScale = Vector3.one;
                    belt.Despawn();
                }
                return null;
            }

            if (!_beltUnits.TryGetValue(belt.Level, out var list))
            {
                list = new List<CharacterUnit>();
                _beltUnits.Add(belt.Level, list);
            }

            list.Add(belt);
            _beltUnitCount++;
            Data.AdjustValue(belt.Level, 1);
            return slots[_beltUnitCount - 1];
        }

        private void ClearBelts()
        {
            _beltUnitCount = 0;

            foreach (var subList in _beltUnits.Values)
            {
                if (subList == null) continue;

                for (int j = 0; j < subList.Count; j++)
                {
                    var unit = subList[j];
                    if (unit == null) continue;
                    unit.Transform.parent = null;
                    unit.Transform.localScale = Vector3.one;
                    unit.Despawn();
                }

                subList.Clear();
            }

            _beltUnits.Clear();
        }

        private static string GetCardLabel(int index)
        {
            if (index == 0) return "C1";
            if (index == 1) return "U1";
            if (index == 2) return "R1";
            if (index == 3) return "E1";
            if (index == 4) return "L1";
            if (index == 5) return "C2";
            if (index == 6) return "U2";
            if (index == 7) return "R2";
            if (index == 8) return "E2";
            if (index == 9) return "L2";
            if (index == 10) return "C3";
            if (index == 11) return "U3";
            if (index == 12) return "R3";
            if (index == 13) return "E3";
            return "L3";
        }
    }
}
