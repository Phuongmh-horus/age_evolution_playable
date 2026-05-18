using System;
using GamePlay.Items;

[Serializable]
public class CapacityPillarOverride : ItemUnitPropertyOverride
{
    public bool overrideValue;
    public int  Armor = 10;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        var target = itemUnit as CapacityIncreasePillar;
        if (target == null || target.Data == null) return;

        target.Data.Armor = Armor;
    }
}
