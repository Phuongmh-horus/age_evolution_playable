using System;
using GamePlay.Enemies;
using GamePlay.HealthSystems;
using GamePlay.Items;

[Serializable]
public class HealthComponentOverride : ItemUnitPropertyOverride
{
    public bool overrideMaxHp;
    public int  maxHp = 10;

    public override void ApplyOverrides(ItemUnit itemUnit)
    {
        if (!overrideMaxHp || itemUnit == null) return;

        var health = itemUnit.GetComponentInChildren<HealthComponent>(true);
        if (health != null)
        {
            health.SetMaxHealth(maxHp, refill: true);
            if (itemUnit is EnemyUnit enemyUnit)
                enemyUnit.MarkHealthOverriddenFromContent();
        }
    }
}
