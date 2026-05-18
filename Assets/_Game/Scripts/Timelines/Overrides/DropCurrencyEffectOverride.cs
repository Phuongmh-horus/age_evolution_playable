using System;
using GamePlay.Effects;
using GamePlay.Items;
using UnityEngine;

[Serializable]
public class DropCurrencyEffectOverride : ItemUnitPropertyOverride
{
    public bool    overrideCurrencyValue;
    public Vector2 CurrencyValue = Vector2.zero;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        if (!overrideCurrencyValue || itemUnit == null) return;

        var effect = itemUnit.GetComponentInChildren<DropCurrencyEffect>(true);
        if (effect != null)
        {
            effect.CurrencyValue = CurrencyValue;
        }
    }
}
