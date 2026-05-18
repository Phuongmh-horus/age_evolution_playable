using GamePlay.Items;
using GamePlay.Enemies;
using GamePlay.HealthSystems;
using UnityEngine;

[System.Serializable]
public class SpawnableObject
{
    [Tooltip("Prefab của object")]
    public ItemUnit Prefab;

    [Tooltip("Vị trí spawn trên map (distance từ đầu map)")]
    public float PositionOnMap;

    [Tooltip("Offset vị trí (x, y, z)")]
    public Vector3 PositionOffset;

    [Tooltip("Rotation của object")]
    public Vector3 Rotation;

    [Tooltip("Scale của object")]
    public Vector3 Scale = Vector3.one;

    [Header("Property Overrides")]
    [Tooltip("Override properties của ItemUnit khi spawn")]
    [SerializeReference]
    public System.Collections.Generic.List<ItemUnitPropertyOverride> propertyOverrides = new System.Collections.Generic.List<ItemUnitPropertyOverride>();

    [Header("Health Override (Playable)")]
    [Tooltip("Fallback for Luna: override max HP without SerializeReference.")]
    public bool overrideMaxHp;
    public int maxHp = 10;

    /// <summary>
    /// Apply các override lên ItemUnit instance sau khi spawn
    /// </summary>
    public void ApplyPropertyOverrides(ItemUnit itemUnit)
    {
        if (propertyOverrides != null && itemUnit != null)
        {
            foreach (var propertyOverride in propertyOverrides)
            {
                if (propertyOverride != null)
                {
                    propertyOverride.ApplyOverrides(itemUnit);
                }
            }
        }

        if (itemUnit != null && overrideMaxHp)
        {
            var health = itemUnit.GetComponentInChildren<HealthComponent>(true);
            if (health != null)
            {
                health.SetMaxHealth(maxHp, refill: true);
                if (itemUnit is EnemyUnit enemyUnit)
                    enemyUnit.MarkHealthOverriddenFromContent();
            }
        }
    }
}
