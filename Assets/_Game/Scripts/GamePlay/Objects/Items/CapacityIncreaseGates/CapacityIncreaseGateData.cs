using System;
using System.Collections.Generic;
using GamePlay.Crushers;
using UnityEngine;

namespace GamePlay.Items
{
    [Serializable] // Bắt buộc để hiện trong Inspector
    public class CapacityIncreaseGateData : StatModifierData
    {
        public List<CardSpawnRequestData> RequestDataList = new List<CardSpawnRequestData>();
        [NonSerialized] public int MaxCoinsToSpawn = -1;

        public void AdjustValue(int level, int amount)
        {
            if (RequestDataList == null)
            {
                Debug.LogError($"[GateData] RequestDataList is NULL! Creating new list.");
                RequestDataList = new List<CardSpawnRequestData>();
            }

            int count = RequestDataList.Count;
            for (int i = 0; i < count; i++)
            {
                if (RequestDataList[i].Level == level)
                {
                    var data = RequestDataList[i];
                    data.Amount += amount;
                    RequestDataList[i] = data;
                    return;
                }
            }

            // Logic thêm mới
            RequestDataList.Add(new CardSpawnRequestData(level, amount, CardType.Character));
        }

        public override void ResetValue()
        {
            base.ResetValue();
            RequestDataList.Clear();
        }
    }
}
