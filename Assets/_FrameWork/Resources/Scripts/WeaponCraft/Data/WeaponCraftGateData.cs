using System;
using System.Collections.Generic;
using GamePlay.Items;
using UnityEngine;

namespace WeaponCraft
{
    [Serializable]
    public struct WeaponCraftTierRequest
    {
        [Min(1)] public int Tier;
        [Min(1)] public int Amount;

        public WeaponCraftTierRequest(int tier, int amount = 1)
        {
            Tier = Mathf.Max(1, tier);
            Amount = Mathf.Max(1, amount);
        }
    }

    [Serializable]
    public class WeaponCraftGateData : StatModifierData
    {
        public List<WeaponCraftTierRequest> TierRequestList = new List<WeaponCraftTierRequest>();

        public override void AdjustValue(int amount)
        {
            base.AdjustValue(amount);
        }

        public void AddTier(int tier, int amount = 1)
        {
            if (TierRequestList == null)
            {
                TierRequestList = new List<WeaponCraftTierRequest>();
            }

            tier = Mathf.Max(1, tier);
            amount = Mathf.Max(1, amount);

            for (int i = 0; i < TierRequestList.Count; i++)
            {
                var request = TierRequestList[i];
                if (request.Tier != tier)
                {
                    continue;
                }

                request.Amount += amount;
                TierRequestList[i] = request;
                return;
            }

            TierRequestList.Add(new WeaponCraftTierRequest(tier, amount));
        }

        public void AddStarterTier()
        {
            AddTier(1, 1);
        }
    }
}
