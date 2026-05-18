using System;
using GamePlay.Items;


[Serializable]
public class GamePlayTutElementOverride : ItemUnitPropertyOverride
{
    public bool overrideIsShowTut;
    public bool IsShowTut = true;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        if (!overrideIsShowTut || itemUnit == null) return;

        var tutElement = itemUnit.GetComponentInChildren<GamePlayTutElement>(true);
        if (tutElement != null)
        {
            tutElement.IsShowTut = IsShowTut;
            tutElement.ApplyVisibility();
        }
    }
}
