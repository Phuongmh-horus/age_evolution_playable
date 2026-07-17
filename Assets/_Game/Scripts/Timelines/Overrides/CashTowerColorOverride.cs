using System;
using GamePlay.Items;
using UnityEngine;

[Serializable]
public class CashTowerColorOverride : ItemUnitPropertyOverride
{
    public bool  overrideColor;
    public Color Color = Color.white;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        if (!overrideColor || itemUnit == null) return;

        var tower = itemUnit as CashTowerController;
        if (tower != null)
        {
            // tower.ApplyColor(Color);
        }
    }
}
