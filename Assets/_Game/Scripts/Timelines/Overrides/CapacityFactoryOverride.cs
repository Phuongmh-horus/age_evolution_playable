using System;
using GamePlay.Items;

[Serializable]
public class CapacityFactoryOverride : ItemUnitPropertyOverride
{
    public bool overrideValue;
    public int Armor = 0;

    public float LeftOffset;
    public float RightOffset;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        var target = itemUnit as CapacityIncreaseFactory;
        if (target == null || target.Data == null) return;

        target.Data.Armor = Armor;
        target.LeftOffset = LeftOffset;
        target.RightOffset = RightOffset;
    }
}