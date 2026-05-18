using System;
using GamePlay.Items;

[Serializable]
public class CurrencyDropItemOverride : ItemUnitPropertyOverride
{
    public bool  overrideAmount;
    public float Amount = 0f;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        if (!overrideAmount || itemUnit == null) return;

        var dropItem = itemUnit as CurrencyDropItem;
        if (dropItem != null)
        {
            dropItem.Amount = Amount;
        }
    }
}
