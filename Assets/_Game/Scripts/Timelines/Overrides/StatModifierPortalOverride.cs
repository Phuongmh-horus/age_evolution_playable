using System;
using GamePlay.Items;

[Serializable]
internal    class StatModifierPortalOverride : ItemUnitPropertyOverride
{
    public bool overrideValue;
    public int  Value = 2;
    public int  Armor = 0;

    public float LeftOffset;
    public float RightOffset;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        var target = itemUnit as StatModifierGate;
        if (target == null || target.Data == null) return;

        target.Data.Value  = Value;
        target.Data.Armor  = Armor;
        target.LeftOffset  = LeftOffset;
        target.RightOffset = RightOffset;
    }
}
