using System;
using UnityEngine;

namespace WeaponCraft
{
    [Serializable]
    public class WeaponItem
    {
        [SerializeField, Min(1)] private int tier = 1;
        [SerializeField] private string prefabKey;

        public int Tier
        {
            get => tier;
            set => tier = Mathf.Max(1, value);
        }

        public string PrefabKey
        {
            get => prefabKey;
            set => prefabKey = value;
        }

        public WeaponItem()
        {
        }

        public WeaponItem(int tier, string prefabKey = null)
        {
            Tier = tier;
            this.prefabKey = prefabKey;
        }

        public WeaponItem Clone()
        {
            return new WeaponItem(tier, prefabKey);
        }
    }
}
