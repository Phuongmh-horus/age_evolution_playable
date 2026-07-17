using System;
using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Items
{
    [Serializable]
    public class IncreaseElementData
    {
        public StatType Type;
        public int Value;
        public int ValueUpgrade;
        public int StartLevel;
        public int Cost;
        public int UpgradeRequire;
        [Tooltip("Sử dụng cho các loại buff đặc biệt như SwordSkill")]
        public global::CardSystem.Data.BuffDefinition BuffDef;
    }

    [Serializable]
    public class CapacityIncreaseGateData : StatModifierData
    {
        public List<IncreaseElementData> ElementDataList = new List<IncreaseElementData>();
        public int UpgradeSteps;
    }
}
