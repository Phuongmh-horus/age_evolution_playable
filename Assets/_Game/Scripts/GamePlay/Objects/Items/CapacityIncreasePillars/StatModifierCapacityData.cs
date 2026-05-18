using System;
using System.Collections;
using System.Collections.Generic;
using GamePlay.Items;
using UnityEngine;

[Serializable]
public class StatModifierCapacityData : StatModifierData
{
    public StatModifierCapacityData() { }

    public StatModifierCapacityData(StatType type, int value, int armor = 0)
    {
        Type = type;
        Value = value;
        Armor = armor;
    }
}
