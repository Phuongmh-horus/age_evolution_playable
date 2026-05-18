using System;
using UnityEngine;
using GamePlay.Items;

/// <summary>
/// Base class cho việc override properties của ItemUnit khi spawn
/// </summary>
[Serializable]
public class ItemUnitPropertyOverride
{
    /// <summary>
    /// Apply các override lên ItemUnit instance
    /// </summary>
    public virtual void ApplyOverrides(ItemUnit itemUnit) { }
}
