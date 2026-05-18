using System;
using System.Collections.Generic;
using GamePlay.Crushers;
using UnityEngine.Serialization;

namespace GamePlay.Items
{
    public enum SwitchIncreaseGateRewardType : byte
    {
        Stat,
        ArmyTierUpgrade,
        ArmyCount,
    }

    [Serializable]
    public class SwitchIncreaseGateRewardConfig
    {
        public int Cost = 1;
        public SwitchIncreaseGateRewardType RewardType = SwitchIncreaseGateRewardType.Stat;
        public StatModifierData StatModifierData = new StatModifierData();
        [FormerlySerializedAs("TargetLevel")]
        public int UpgradeTargetLevel = 1;
        public List<CardSpawnRequestData> RequestDataList = new List<CardSpawnRequestData>();
    }

    [Serializable]
    public class SwitchIncreaseGateData : StatModifierData
    {
        public List<SwitchIncreaseGateRewardConfig> RewardConfigs = new List<SwitchIncreaseGateRewardConfig>();
    }
}
